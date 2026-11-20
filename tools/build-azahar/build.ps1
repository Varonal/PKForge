param(
    [string]$NdkPath = $env:ANDROID_NDK_ROOT,
    [string]$CMakePath
)

$ErrorActionPreference = "Stop"
$scriptDirectory = $PSScriptRoot
$repositoryRoot = (Resolve-Path (Join-Path $scriptDirectory "../..")).Path
$azaharDirectory = Join-Path $repositoryRoot "external/azahar"
$sdkRoot = $env:ANDROID_SDK_ROOT

if (-not $NdkPath -and $sdkRoot) {
    $ndkRoot = Join-Path $sdkRoot "ndk"
    if (Test-Path $ndkRoot) {
        $NdkPath = (Get-ChildItem -LiteralPath $ndkRoot -Directory |
            Sort-Object Name -Descending | Select-Object -First 1).FullName
    }
}
if (-not $NdkPath -and $env:LOCALAPPDATA) {
    $ndkRoot = Join-Path $env:LOCALAPPDATA "Android/Sdk/ndk"
    if (Test-Path $ndkRoot) {
        $NdkPath = (Get-ChildItem -LiteralPath $ndkRoot -Directory |
            Sort-Object Name -Descending | Select-Object -First 1).FullName
    }
}
if (-not $NdkPath -or -not (Test-Path (Join-Path $NdkPath "build/cmake/android.toolchain.cmake"))) {
    throw "Set ANDROID_NDK_ROOT or pass -NdkPath with an installed Android NDK."
}
$ndkProperties = Join-Path $NdkPath "source.properties"
if (-not (Test-Path $ndkProperties)) { throw "NDK metadata is missing at $ndkProperties." }
$ndkRevision = Select-String -Path $ndkProperties -Pattern 'Pkg.Revision\s*=\s*(\d+)\.' | Select-Object -First 1
if (-not $ndkRevision -or [int]$ndkRevision.Matches[0].Groups[1].Value -lt 29) {
    throw "Azahar's Android core requires NDK r29 or newer. Install a supported NDK and pass its path with -NdkPath."
}
if (-not (Test-Path (Join-Path $azaharDirectory "CMakeLists.txt"))) {
    throw "The Azahar source pin is missing. Run: git submodule update --init external/azahar"
}

if (-not $CMakePath -and $sdkRoot) {
    $sdkCMake = Join-Path $sdkRoot "cmake/3.30.3/bin/cmake.exe"
    if (Test-Path $sdkCMake) { $CMakePath = $sdkCMake }
}
if (-not $CMakePath) {
    $CMakePath = (Get-Command cmake -ErrorAction Stop).Source
}
$cmakeVersionText = (& $CMakePath --version | Select-Object -First 1)
if ($LASTEXITCODE -ne 0 -or $cmakeVersionText -notmatch 'cmake version (\d+)\.(\d+)') {
    throw "Could not read the CMake version from $CMakePath."
}
if ([int]$Matches[1] -lt 3 -or ([int]$Matches[1] -eq 3 -and [int]$Matches[2] -lt 25)) {
    throw "Azahar requires CMake 3.25 or newer; found $cmakeVersionText."
}

$git = Get-Command git -ErrorAction Stop
& $git.Source -C $azaharDirectory submodule update --init --recursive
if ($LASTEXITCODE -ne 0) { throw "Could not initialize Azahar's pinned dependencies." }

$buildDirectory = Join-Path $scriptDirectory "out/android-arm64"
$toolchain = Join-Path $NdkPath "build/cmake/android.toolchain.cmake"
$configureArgs = @(
    "-S", $azaharDirectory,
    "-B", $buildDirectory,
    "-G", "Ninja",
    "-DCMAKE_BUILD_TYPE=Release",
    "-DCMAKE_TOOLCHAIN_FILE=$toolchain",
    "-DANDROID_ABI=arm64-v8a",
    "-DANDROID_PLATFORM=android-21",
    "-DANDROID_STL=c++_static",
    "-DENABLE_LIBRETRO=ON",
    "-DENABLE_TESTS=OFF",
    "-DENABLE_OPENGL=ON",
    "-DENABLE_VULKAN=ON",
    "-DENABLE_LTO=OFF"
)
& $CMakePath @configureArgs
if ($LASTEXITCODE -ne 0) { throw "Azahar Android arm64 CMake configure failed with exit code $LASTEXITCODE." }

& $CMakePath --build $buildDirectory --target azahar_libretro --config Release --parallel
if ($LASTEXITCODE -ne 0) { throw "Azahar Android arm64 core build failed with exit code $LASTEXITCODE." }

$hostTag = "windows-x86_64"
$strip = Join-Path $NdkPath "toolchains/llvm/prebuilt/$hostTag/bin/llvm-strip.exe"
if (-not (Test-Path $strip)) { throw "NDK llvm-strip was not found at $strip." }
$sourceLibrary = Join-Path $buildDirectory "bin/Release/azahar_libretro.so"
if (-not (Test-Path $sourceLibrary)) { throw "Azahar did not produce the expected libretro library: $sourceLibrary." }

$destination = Join-Path $repositoryRoot "src/PKForge.App/Libraries/arm64-v8a/libpkforge_azahar.so"
New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
$stagedLibrary = "$destination.tmp"
Copy-Item -LiteralPath $sourceLibrary -Destination $stagedLibrary -Force
& $strip --strip-unneeded $stagedLibrary
if ($LASTEXITCODE -ne 0) {
    Remove-Item -LiteralPath $stagedLibrary -Force -ErrorAction SilentlyContinue
    throw "Could not strip Azahar symbols (exit code $LASTEXITCODE)."
}
Move-Item -LiteralPath $stagedLibrary -Destination $destination -Force
Write-Output "Built the Azahar arm64 libretro core: $destination"
