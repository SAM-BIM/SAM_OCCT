param(
    [string]$Configuration = "Release",
    [string]$Triplet = "x64-windows",
    [string]$VcpkgRoot = "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\vcpkg",
    [string]$CMakePath = "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe",
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
$toolchain = Join-Path $VcpkgRoot "scripts\buildsystems\vcpkg.cmake"
$vcpkgExe = Join-Path $VcpkgRoot "vcpkg.exe"

function Test-FileExists($path, $label) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "$label not found: $path"
    }
}

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

Test-FileExists $vcpkgExe "vcpkg"
Test-FileExists $toolchain "vcpkg CMake toolchain"
Test-FileExists $CMakePath "CMake"

if ([string]::IsNullOrWhiteSpace($NinjaPath) -and -not [string]::IsNullOrWhiteSpace($env:SAM_OCCT_NINJA)) {
    $NinjaPath = $env:SAM_OCCT_NINJA
}

if (-not [string]::IsNullOrWhiteSpace($NinjaPath)) {
    Test-FileExists $NinjaPath "Ninja"
    $ninjaVersion = Get-NinjaVersion $NinjaPath
    if ($null -eq $ninjaVersion -or $ninjaVersion -lt [version]"1.13.1") {
        throw "Ninja 1.13.1 or newer is required by the current vcpkg baseline. Found '$NinjaPath' version '$ninjaVersion'."
    }

    $env:PATH = "$(Split-Path -Parent $NinjaPath);$env:PATH"
}

New-Item -ItemType Directory -Force -Path $nativeOutput | Out-Null

if (-not $SkipVcpkgInstall) {
    & $vcpkgExe install --triplet $Triplet
    if ($LASTEXITCODE -ne 0) {
        throw "vcpkg install failed."
    }
}

$configureArgs = @(
    "-S", $nativeSource,
    "-B", $nativeBuild,
    "-G", "Visual Studio 17 2022",
    "-A", "x64",
    "-DCMAKE_TOOLCHAIN_FILE=$toolchain",
    "-DVCPKG_TARGET_TRIPLET=$Triplet",
    "-DCMAKE_INSTALL_PREFIX=$nativeOutput",
    "-DCMAKE_SUPPRESS_REGENERATION=ON",
    "-DSAM_OCCT_OUTPUT_DIRECTORY=$nativeOutput"
)

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
$dataExchangeDlls = @("TKDESTEP.dll", "TKDEIGES.dll", "TKXSBase.dll")
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

    $thirdPartyRuntimePatterns = @(
        "msvc-*\*.dll",
        "tbb-*\bin\*.dll",
        "jemalloc-*\bin\*.dll"
    )

    foreach ($pattern in $thirdPartyRuntimePatterns) {
        Get-ChildItem -Path (Join-Path $thirdPartyRoot $pattern) -ErrorAction SilentlyContinue |
            Copy-Item -Destination $nativeOutput -Force
    }
}

$samDir = Join-Path $env:APPDATA "SAM"
New-Item -ItemType Directory -Force -Path $samDir | Out-Null
Get-ChildItem -LiteralPath $nativeOutput -Filter "*.dll" | Copy-Item -Destination $samDir -Force

Write-Host "Native OCCT build copied to $nativeOutput"
Write-Host "Native OCCT runtime copied to $samDir"
