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

mkdir -p "${WORK}" "${OUT}"
cd "${WORK}"

# 1. mGBA source (pinned tag)
if [ ! -d "mgba-${MGBA_VERSION}" ]; then
  curl -L "${MGBA_TAR}" | tar xz
fi
cd "mgba-${MGBA_VERSION}"

# 2. libmgba static for arm64 — the flags PokeDaisy uses (no frontends, no extras).
cmake -B build-android \
  -DCMAKE_TOOLCHAIN_FILE="${NDK_HOME}/build/cmake/android.toolchain.cmake" \
  -DANDROID_ABI=arm64-v8a \
  -DANDROID_PLATFORM=android-${API} \
  -DBUILD_QT=OFF -DBUILD_SDL=OFF -DBUILD_LIBRETRO=OFF \
  -DBUILD_STATIC=ON -DBUILD_SHARED=OFF \
  -DUSE_FFMPEG=OFF -DUSE_DISCORD_RPC=OFF -DUSE_GDB_STUB=OFF \
  -DUSE_SQLITE3=OFF -DUSE_MINIZIP=OFF -DUSE_PNG=OFF -DUSE_LIBZIP=OFF
cmake --build build-android --target mgba-static -j"$(nproc)"

# 3. The shim links against the static lib.
"${NDK_HOME}/toolchains/llvm/prebuilt/linux-x86_64/bin/clang" \
  --target="${TRIPLE}${API}" \
  -shared -fPIC -O2 \
  -I"${PWD}/include" -I"${PWD}/src" \
  "${HERE}/jni_bridge.c" \
  "${WORK}/mgba-${MGBA_VERSION}/build-android/libmgba.a" \
  -o "${OUT}/libpkforge_mgba.so" \
  -llog

echo "Built ${OUT}/libpkforge_mgba.so"
