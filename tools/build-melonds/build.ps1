param(
    [string]$NdkPath = $env:ANDROID_NDK_ROOT
)

$ErrorActionPreference = "Stop"
$scriptDirectory = $PSScriptRoot
$repositoryRoot = (Resolve-Path (Join-Path $scriptDirectory "../..")).Path
$sdkRoot = $env:ANDROID_SDK_ROOT

if (-not $NdkPath -and $sdkRoot) {
    $ndkRoot = Join-Path $sdkRoot "ndk"
    if (Test-Path $ndkRoot) {
        $NdkPath = (Get-ChildItem -LiteralPath $ndkRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
    }
}
if (-not $NdkPath -and $env:LOCALAPPDATA) {
    $ndkRoot = Join-Path $env:LOCALAPPDATA "Android/Sdk/ndk"
    if (Test-Path $ndkRoot) {
        $NdkPath = (Get-ChildItem -LiteralPath $ndkRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
    }
}
if (-not $NdkPath -or -not (Test-Path (Join-Path $NdkPath "build/cmake/android.toolchain.cmake"))) {
    throw "Set ANDROID_NDK_ROOT or pass -NdkPath with an installed Android NDK."
}

$buildDirectory = Join-Path $scriptDirectory "out/android-arm64"
$toolchain = Join-Path $NdkPath "build/cmake/android.toolchain.cmake"
$configureArgs = @(
    "-S", $scriptDirectory,
    "-B", $buildDirectory,
    "-G", "Ninja",
    "-DCMAKE_BUILD_TYPE=Release",
    "-DCMAKE_TOOLCHAIN_FILE=$toolchain",
    "-DANDROID_ABI=arm64-v8a",
    "-DANDROID_PLATFORM=android-26",
    "-DANDROID_STL=c++_static"
)
& cmake @configureArgs
if ($LASTEXITCODE -ne 0) { throw "melonDS CMake configure failed with exit code $LASTEXITCODE." }

& cmake --build $buildDirectory --target pkforge_melonds --parallel
if ($LASTEXITCODE -ne 0) { throw "melonDS native build failed with exit code $LASTEXITCODE." }

$hostTag = "windows-x86_64"
$strip = Join-Path $NdkPath "toolchains/llvm/prebuilt/$hostTag/bin/llvm-strip.exe"
if (-not (Test-Path $strip)) { throw "NDK llvm-strip was not found at $strip." }
$sourceLibrary = Join-Path $buildDirectory "libpkforge_melonds.so"
$destination = Join-Path $repositoryRoot "src/PKForge.App/Libraries/arm64-v8a/libpkforge_melonds.so"
New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
$stagedLibrary = "$destination.tmp"
Copy-Item -LiteralPath $sourceLibrary -Destination $stagedLibrary -Force
& $strip --strip-unneeded $stagedLibrary
if ($LASTEXITCODE -ne 0) {
    Remove-Item -LiteralPath $stagedLibrary -Force -ErrorAction SilentlyContinue
    throw "Could not strip melonDS symbols (exit code $LASTEXITCODE)."
}
Move-Item -LiteralPath $stagedLibrary -Destination $destination -Force
Write-Output "Built arm64 melonDS core: $destination"
