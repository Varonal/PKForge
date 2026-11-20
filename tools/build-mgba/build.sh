#!/usr/bin/env bash
# Builds libpkforge_mgba.so for arm64-v8a: libmgba 0.10.5 static + PKForge's flat
# JNI shim. Run once per checkout (and after any mGBA bump):
#
#   NDK_HOME=/path/to/android-ndk tools/build-mgba/build.sh
#
# The output lands in src/PKForge.App/Libraries/arm64-v8a/libpkforge_mgba.so,
# which PKForge.App.csproj packs into the APK (AndroidNativeLibrary).
set -euo pipefail

MGBA_VERSION="0.10.5"
MGBA_TAR="https://github.com/mgba-emu/mgba/archive/refs/tags/${MGBA_VERSION}.tar.gz"
HERE="$(cd "$(dirname "$0")" && pwd)"
WORK="${HERE}/build"
OUT="${HERE}/../../src/PKForge.App/Libraries/arm64-v8a"

: "${NDK_HOME:?Set NDK_HOME to your Android NDK path (e.g. %LOCALAPPDATA%\\Android\\Sdk\\ndk\\27.x on Windows)}"

API=26
TRIPLE="aarch64-linux-android"
BUILD_DIR="build-android-ninja"

mkdir -p "${WORK}" "${OUT}"
cd "${WORK}"

# 1. mGBA source (pinned tag)
if [ ! -d "mgba-${MGBA_VERSION}" ]; then
  curl -L "${MGBA_TAR}" | tar xz
fi
cd "mgba-${MGBA_VERSION}"

# 2. libmgba static for arm64: GB/GBA cores, ZIP/7z ROM support, no frontend.
cmake -B "${BUILD_DIR}" -G Ninja \
  -DCMAKE_POLICY_VERSION_MINIMUM=3.5 \
  -DCMAKE_TOOLCHAIN_FILE="${NDK_HOME}/build/cmake/android.toolchain.cmake" \
  -DANDROID_ABI=arm64-v8a \
  -DANDROID_PLATFORM=android-${API} \
  -DBUILD_QT=OFF -DBUILD_SDL=OFF -DBUILD_LIBRETRO=OFF \
  -DBUILD_STATIC=ON -DBUILD_SHARED=OFF \
  -DM_CORE_GB=ON -DM_CORE_GBA=ON \
  -DUSE_FFMPEG=OFF -DUSE_DISCORD_RPC=OFF -DUSE_GDB_STUB=OFF \
  -DUSE_SQLITE3=OFF -DUSE_MINIZIP=OFF -DUSE_PNG=OFF -DUSE_LIBZIP=OFF \
  -DUSE_ZLIB=ON -DUSE_LZMA=ON
# BUILD_STATIC=ON produces libmgba.a (the standard add_library output name).
cmake --build "${BUILD_DIR}" --parallel

# 3. The shim links against the static lib.
case "$(uname -s)" in
  Darwin*) HOST_TAG="darwin-x86_64" ;;
  MINGW*|MSYS*|CYGWIN*) HOST_TAG="windows-x86_64" ;;
  *) HOST_TAG="linux-x86_64" ;;
esac
CLANG="${NDK_HOME}/toolchains/llvm/prebuilt/${HOST_TAG}/bin/clang"
if [ ! -x "${CLANG}" ] && [ -x "${CLANG}.exe" ]; then CLANG="${CLANG}.exe"; fi
if [ ! -x "${CLANG}" ]; then
  echo "Could not find the NDK clang compiler for ${HOST_TAG}." >&2
  exit 1
fi
"${CLANG}" \
  --target="${TRIPLE}${API}" \
  -shared -fPIC -O2 \
  -Wl,-z,max-page-size=16384 -Wl,-z,common-page-size=16384 \
  -Wl,--no-undefined \
  -I"${WORK}/mgba-${MGBA_VERSION}/include" \
  "${HERE}/jni_bridge.c" \
  "${WORK}/mgba-${MGBA_VERSION}/${BUILD_DIR}/libmgba.a" \
  -o "${OUT}/libpkforge_mgba.so" \
  -llog -landroid -lGLESv3 -lz -lm

echo "Built ${OUT}/libpkforge_mgba.so"
