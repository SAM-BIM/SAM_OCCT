param(
    [string]$Configuration = "Release",
    [string]$Triplet = "x64-windows",
    # Empty by default: auto-detected from the installed Visual Studio via vswhere
    # so this works on VS 2019/2022/2026 instead of a single hardcoded version.
    # Pass explicit values to override.
    [string]$VcpkgRoot = "",
    [string]$CMakePath = "",
    [string]$Generator = "",
    [string]$NinjaPath = "",
    [string]$OpenCascadeDir = "",
    [string]$OpenCascadeIncludeDir = "",
    [string]$OpenCascadeLibraryDir = "",
    [string]$OpenCascadeRuntimeBin = "",
    [string]$ThirdPartyRuntimeRoot = "",
    [switch]$SkipVcpkgInstall
)

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$nativeSource = Join-Path $repoRoot "native\SAM.Occt.Native"
$nativeBuild = Join-Path $repoRoot "native\build\$Triplet"
$nativeOutput = Join-Path $repoRoot "build"
$manifestInstalled = Join-Path $repoRoot "vcpkg_installed"

function Test-FileExists($path, $label) {
    if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path)) {
        throw "$label not found: '$path'"
    }
}

# Locate the Visual Studio toolchain (CMake, vcpkg, Ninja, generator) via vswhere
# so the script adapts to whichever VS is installed (2019/2022/2026) instead of a
# hardcoded version path. Only fills in values that were not passed explicitly.
function Find-VisualStudioInstall {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path -LiteralPath $vswhere)) { return $null }

    $path = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>$null
    if ([string]::IsNullOrWhiteSpace($path)) {
        $path = & $vswhere -latest -products * -property installationPath 2>$null
    }
    if ([string]::IsNullOrWhiteSpace($path)) { return $null }

    $version = & $vswhere -latest -products * -property installationVersion 2>$null
    return [pscustomobject]@{ Path = $path.Trim(); Version = "$version".Trim() }
}

$vsInstall = Find-VisualStudioInstall
if ($null -ne $vsInstall) {
    if ([string]::IsNullOrWhiteSpace($CMakePath)) {
        $candidate = Join-Path $vsInstall.Path "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
        if (Test-Path -LiteralPath $candidate) { $CMakePath = $candidate }
    }
    if ([string]::IsNullOrWhiteSpace($VcpkgRoot)) {
        $candidate = Join-Path $vsInstall.Path "VC\vcpkg"
        if (Test-Path -LiteralPath $candidate) { $VcpkgRoot = $candidate }
    }
    if ([string]::IsNullOrWhiteSpace($NinjaPath)) {
        $candidate = Join-Path $vsInstall.Path "Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe"
        if (Test-Path -LiteralPath $candidate) { $NinjaPath = $candidate }
    }
    if ([string]::IsNullOrWhiteSpace($Generator)) {
        switch (($vsInstall.Version -split '\.')[0]) {
            "18" { $Generator = "Visual Studio 18 2026" }
            "17" { $Generator = "Visual Studio 17 2022" }
            "16" { $Generator = "Visual Studio 16 2019" }
        }
    }
}

# Last-resort fallbacks if vswhere did not resolve everything.
if ([string]::IsNullOrWhiteSpace($CMakePath)) {
    $cmakeCommand = Get-Command cmake -ErrorAction SilentlyContinue
    if ($null -ne $cmakeCommand) { $CMakePath = $cmakeCommand.Source }
}
if ([string]::IsNullOrWhiteSpace($Generator)) { $Generator = "Visual Studio 17 2022" }

$toolchain = if ([string]::IsNullOrWhiteSpace($VcpkgRoot)) { "" } else { Join-Path $VcpkgRoot "scripts\buildsystems\vcpkg.cmake" }
$vcpkgExe = if ([string]::IsNullOrWhiteSpace($VcpkgRoot)) { "" } else { Join-Path $VcpkgRoot "vcpkg.exe" }

function Get-NinjaVersion($path) {
    if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path)) {
        return $null
    }

    $versionText = & $path --version
    if ($LASTEXITCODE -ne 0) {
        return $null
    }

    return [version]($versionText.Trim())
}

$defaultOpenCascadeRoot = "C:\OCCT\occt-8.0.0"
$defaultOpenCascadeSdkRoot = Join-Path $defaultOpenCascadeRoot "opencascade-8.0.0-vc14-64"
if ([string]::IsNullOrWhiteSpace($OpenCascadeIncludeDir) -and (Test-Path -LiteralPath (Join-Path $defaultOpenCascadeSdkRoot "inc\gp_Pnt.hxx"))) {
    $OpenCascadeIncludeDir = Join-Path $defaultOpenCascadeSdkRoot "inc"
}

if ([string]::IsNullOrWhiteSpace($OpenCascadeLibraryDir) -and (Test-Path -LiteralPath (Join-Path $defaultOpenCascadeSdkRoot "win64\vc14\lib\TKernel.lib"))) {
    $OpenCascadeLibraryDir = Join-Path $defaultOpenCascadeSdkRoot "win64\vc14\lib"
}

if ([string]::IsNullOrWhiteSpace($OpenCascadeRuntimeBin) -and (Test-Path -LiteralPath (Join-Path $defaultOpenCascadeSdkRoot "win64\vc14\bin"))) {
    $OpenCascadeRuntimeBin = Join-Path $defaultOpenCascadeSdkRoot "win64\vc14\bin"
}

# The runtime bin is normally the sibling of the lib folder we link against.
# Deriving it keeps the runtime copy working when the SDK is not at the default
# root (linking would succeed but the DLL copy used to skip silently).
if ([string]::IsNullOrWhiteSpace($OpenCascadeRuntimeBin) -and -not [string]::IsNullOrWhiteSpace($OpenCascadeLibraryDir)) {
    $siblingBin = Join-Path (Split-Path -Parent $OpenCascadeLibraryDir) "bin"
    if (Test-Path -LiteralPath $siblingBin) {
        $OpenCascadeRuntimeBin = $siblingBin
    }
}

if ([string]::IsNullOrWhiteSpace($ThirdPartyRuntimeRoot) -and (Test-Path -LiteralPath (Join-Path $defaultOpenCascadeRoot "3rdparty-vc14-64"))) {
    $ThirdPartyRuntimeRoot = Join-Path $defaultOpenCascadeRoot "3rdparty-vc14-64"
}

# When the prebuilt OpenCASCADE SDK is available (detected above or passed in), build
# CMake directly against it - no vcpkg toolchain, no manifest-mode source build (which
# would compile OpenCASCADE from scratch, taking 30-90 min). This mirrors how the
# SAM_Deploy CI drives the native build. vcpkg is only required as a fallback when no
# SDK is present.
$useOcctSdk = (-not [string]::IsNullOrWhiteSpace($OpenCascadeIncludeDir)) -and (-not [string]::IsNullOrWhiteSpace($OpenCascadeLibraryDir))

Test-FileExists $CMakePath "CMake"
if (-not $useOcctSdk) {
    if ([string]::IsNullOrWhiteSpace($vcpkgExe)) {
        throw "No prebuilt OpenCASCADE SDK was found (expected under C:\OCCT or via -OpenCascadeIncludeDir/-OpenCascadeLibraryDir) and vcpkg could not be located to build it from source. Install the OCCT SDK or pass -VcpkgRoot."
    }
    Test-FileExists $vcpkgExe "vcpkg"
    Test-FileExists $toolchain "vcpkg CMake toolchain"
}

if ([string]::IsNullOrWhiteSpace($NinjaPath) -and -not [string]::IsNullOrWhiteSpace($env:SAM_OCCT_NINJA)) {
    $NinjaPath = $env:SAM_OCCT_NINJA
}

# Ninja is only used by the vcpkg manifest build path; the Visual Studio generator
# used for the SDK-direct build does not need it.
if (-not $useOcctSdk -and -not [string]::IsNullOrWhiteSpace($NinjaPath)) {
    Test-FileExists $NinjaPath "Ninja"
    $ninjaVersion = Get-NinjaVersion $NinjaPath
    if ($null -eq $ninjaVersion -or $ninjaVersion -lt [version]"1.13.1") {
        throw "Ninja 1.13.1 or newer is required by the current vcpkg baseline. Found '$NinjaPath' version '$ninjaVersion'."
    }

    $env:PATH = "$(Split-Path -Parent $NinjaPath);$env:PATH"
}

New-Item -ItemType Directory -Force -Path $nativeOutput | Out-Null

if (-not $SkipVcpkgInstall -and -not $useOcctSdk) {
    & $vcpkgExe install --triplet $Triplet
    if ($LASTEXITCODE -ne 0) {
        throw "vcpkg install failed."
    }
}

$configureArgs = @(
    "-S", $nativeSource,
    "-B", $nativeBuild,
    "-G", $Generator,
    "-A", "x64",
    "-DCMAKE_INSTALL_PREFIX=$nativeOutput",
    "-DCMAKE_SUPPRESS_REGENERATION=ON",
    "-DSAM_OCCT_OUTPUT_DIRECTORY=$nativeOutput"
)

if (-not $useOcctSdk) {
    $configureArgs += "-DCMAKE_TOOLCHAIN_FILE=$toolchain"
    $configureArgs += "-DVCPKG_TARGET_TRIPLET=$Triplet"
}

if (-not [string]::IsNullOrWhiteSpace($OpenCascadeDir)) {
    Test-FileExists (Join-Path $OpenCascadeDir "OpenCASCADEConfig.cmake") "OpenCASCADEConfig.cmake"
    $configureArgs += "-DOpenCASCADE_DIR=$OpenCascadeDir"
}

if (-not [string]::IsNullOrWhiteSpace($OpenCascadeIncludeDir)) {
    Test-FileExists (Join-Path $OpenCascadeIncludeDir "gp_Pnt.hxx") "OCCT include directory"
    $configureArgs += "-DSAM_OCCT_OCCT_INCLUDE_DIR=$OpenCascadeIncludeDir"
}

if (-not [string]::IsNullOrWhiteSpace($OpenCascadeLibraryDir)) {
    Test-FileExists (Join-Path $OpenCascadeLibraryDir "TKernel.lib") "OCCT library directory"
    $configureArgs += "-DSAM_OCCT_OCCT_LIBRARY_DIR=$OpenCascadeLibraryDir"
}

& $CMakePath @configureArgs
if ($LASTEXITCODE -ne 0) {
    throw "CMake configure failed."
}

& $CMakePath --build $nativeBuild --config $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Native build failed."
}

$vcpkgBin = Join-Path $manifestInstalled "$Triplet\bin"
if (Test-Path -LiteralPath $vcpkgBin) {
    Get-ChildItem -LiteralPath $vcpkgBin -Filter "*.dll" | Copy-Item -Destination $nativeOutput -Force
}

if (-not [string]::IsNullOrWhiteSpace($OpenCascadeRuntimeBin)) {
    Test-FileExists $OpenCascadeRuntimeBin "OpenCascadeRuntimeBin"
    Get-ChildItem -LiteralPath $OpenCascadeRuntimeBin -Filter "*.dll" | Copy-Item -Destination $nativeOutput -Force
} else {
    Write-Warning "OpenCascadeRuntimeBin was not detected and was not passed - the OCCT runtime DLLs were NOT copied to $nativeOutput. Pass -OpenCascadeRuntimeBin <folder containing TKernel.dll>."
}

# STEP/IGES (issue #20): SAM.Occt.Native delay-loads the Data Exchange
# toolkits, so a missing DLL no longer breaks loading - but import/export then
# reports status 64 at runtime. Verify they were deployed and say so clearly.
# openvr_api.dll is a transitive requirement: TKDESTEP -> ... -> TKV3d ->
# TKService statically imports it, so its absence makes TKDESTEP/TKDEIGES fail
# to LOAD (status 64) even though the toolkits themselves are present.
$dataExchangeDlls = @("TKDESTEP.dll", "TKDEIGES.dll", "TKXSBase.dll", "openvr_api.dll")
$missingDataExchange = @($dataExchangeDlls | Where-Object { -not (Test-Path -LiteralPath (Join-Path $nativeOutput $_)) })
if ($missingDataExchange.Count -gt 0) {
    Write-Warning ("STEP/IGES Data Exchange DLLs missing from {0}: {1}" -f $nativeOutput, ($missingDataExchange -join ", "))
    if (-not [string]::IsNullOrWhiteSpace($OpenCascadeRuntimeBin)) {
        Write-Warning ("They were also not found in OpenCascadeRuntimeBin '{0}' - that OCCT runtime does not include the Data Exchange toolkits. Point -OpenCascadeRuntimeBin at a full OCCT bin folder (the one containing TKDESTEP.dll) or copy TKDESTEP.dll/TKDEIGES.dll/TKXSBase.dll and the XCAF/CAF DLLs into {1} manually." -f $OpenCascadeRuntimeBin, $nativeOutput)
    }
    Write-Warning "Everything except STEP/IGES import/export keeps working; those nodes will report native status 64 until the DLLs above are deployed."
} else {
    Write-Host "STEP/IGES Data Exchange runtime verified in $nativeOutput (TKDESTEP/TKDEIGES/TKXSBase)."
}

if (-not [string]::IsNullOrWhiteSpace($ThirdPartyRuntimeRoot)) {
    Test-FileExists $ThirdPartyRuntimeRoot "ThirdPartyRuntimeRoot"
    $thirdPartyRoot = $ThirdPartyRuntimeRoot
    $nestedThirdPartyRoot = Join-Path $ThirdPartyRuntimeRoot "3rdparty-vc14-64"
    if (Test-Path -LiteralPath $nestedThirdPartyRoot) {
        $thirdPartyRoot = $nestedThirdPartyRoot
    }

    # Copy every third-party runtime DLL that ships with a bin\ folder, not just
    # tbb/jemalloc. The Data Exchange + XCAF stack (issue #20) transitively pulls
    # the OCCT visualization toolkits (TKV3d -> TKService), which need
    # freetype.dll / FreeImage.dll - DLLs the core modeling stack never required,
    # so a "msvc/tbb/jemalloc only" copy left TKDESTEP.dll unloadable (status 64).
    # win64\ is included because TKService (pulled in transitively by the Data
    # Exchange + XCAF + TKV3d stack) statically imports openvr_api.dll, which the
    # OCCT 3rdparty tree ships under <pkg>\bin\win64\ - a layout the bin\ and
    # bin\vc14\ globs miss, leaving TKDESTEP/TKDEIGES unloadable (status 64).
    $thirdPartyRuntimePatterns = @(
        "msvc-*\*.dll",
        "*\bin\*.dll",
        "*\bin\vc14\*.dll",
        "*\bin\win64\*.dll"
    )

    foreach ($pattern in $thirdPartyRuntimePatterns) {
        Get-ChildItem -Path (Join-Path $thirdPartyRoot $pattern) -ErrorAction SilentlyContinue |
            Copy-Item -Destination $nativeOutput -Force
    }
}

$samDir = Join-Path $env:APPDATA "SAM"
New-Item -ItemType Directory -Force -Path $samDir | Out-Null

# Rhino/Grasshopper locks the DLLs it has loaded from %APPDATA%\SAM, which
# makes this deployment copy fail half-way and leaves a stale runtime that
# Rhino keeps loading. Copy file-by-file so one locked DLL does not abort the
# rest, and fail with an actionable message listing what could not be updated.
$lockedDlls = @()
foreach ($dll in Get-ChildItem -LiteralPath $nativeOutput -Filter "*.dll") {
    try {
        Copy-Item -LiteralPath $dll.FullName -Destination $samDir -Force
    } catch {
        $lockedDlls += $dll.Name
    }
}

if ($lockedDlls.Count -gt 0) {
    throw ("Could not update {0} DLL(s) in {1} (most likely locked by a running Rhino/Grasshopper): {2}. Close Rhino and rebuild (or re-run build-native.ps1) so the deployed runtime matches the build output." -f $lockedDlls.Count, $samDir, ($lockedDlls -join ", "))
}

Write-Host "Native OCCT build copied to $nativeOutput"
Write-Host "Native OCCT runtime copied to $samDir"
