param(
    [string]$Configuration = "Release",
    [string]$Triplet = "x64-windows",
    [string]$VcpkgRoot = "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\vcpkg",
    [string]$CMakePath = "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe",
    [string]$NinjaPath = "",
    [string]$OpenCascadeDir = "",
    [string]$OpenCascadeRuntimeBin = "",
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
    "-DSAM_OCCT_OUTPUT_DIRECTORY=$nativeOutput"
)

if (-not [string]::IsNullOrWhiteSpace($OpenCascadeDir)) {
    Test-FileExists (Join-Path $OpenCascadeDir "OpenCASCADEConfig.cmake") "OpenCASCADEConfig.cmake"
    $configureArgs += "-DOpenCASCADE_DIR=$OpenCascadeDir"
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
}

Write-Host "Native OCCT build copied to $nativeOutput"
