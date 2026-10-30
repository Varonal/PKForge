/* PKForge's flat C shim over libmgba 0.10.5 (mgba/core/core.h), the layer
 * MgbaNative.cs P/Invokes. Every function returns int (1 = success, 0 = failure)
 * and every string is UTF-8: no C bools to marshal, no struct layouts on the
 * managed side. One core handle per ROM; the caller owns the threading.
 *
 * Built by tools/build-mgba/build.sh (NDK, arm64-v8a) into libpkforge_mgba.so,
 * linking libmgba static — the same flags PokeDaisy uses:
 *   -DBUILD_QT=OFF -DBUILD_SDL=OFF -DBUILD_LIBRETRO=OFF -DBUILD_STATIC=ON
 *   -DBUILD_SHARED=OFF -DUSE_FFMPEG=OFF -DUSE_DISCORD_RPC=OFF
 */

#include <mgba/core/core.h>
#include <mgba/core/blip_buf.h>
#include <mgba-util/vfs.h>
#include <string.h>
#include <stdlib.h>
#include <fcntl.h>

/* SAVESTATE_SCREENSHOT | SAVESTATE_SAVEDATA | SAVESTATE_RTC | SAVESTATE_METADATA */
#define PKFORGE_STATE_FLAGS 0xF

/* The handle carries the core plus the video buffer PKForge allocated, so
 * destroy frees both (mGBA does not own the pinned buffer). */
struct PkforgeCore {
	struct mCore* core;
	uint32_t* frame;
};

int pkforg_core_create(const char* romPath) {
	struct mCore* core = mCoreFind(romPath);
	if (!core) {
		return 0;
	}
	if (!core->init(core)) {
		core->deinit(core);
		return 0;
	}
	/* The port name picks mGBA's config section; NULL keeps the default ("None"),
	 * which is fine: PKForge sets no per-port options through mCoreConfig. */
	mCoreInitConfig(core, NULL);
	struct PkforgeCore* handle = malloc(sizeof(struct PkforgeCore));
	if (!handle) {
		core->deinit(core);
		return 0;
	}
	handle->core = core;
	handle->frame = NULL;
	return (int)(intptr_t)handle;
}

void pkforg_core_destroy(intptr_t coreHandle) {
	struct PkforgeCore* handle = (struct PkforgeCore*)(void*)coreHandle;
	if (!handle) {
		return;
	}
	if (handle->frame) {
		free(handle->frame);
		handle->frame = NULL;
	}
	if (handle->core) {
		handle->core->deinit(handle->core);
	}
	free(handle);
}

static struct mCore* asCore(intptr_t coreHandle) {
	struct PkforgeCore* handle = (struct PkforgeCore*)(void*)coreHandle;
	return handle ? handle->core : NULL;
}

int pkforg_core_load(intptr_t coreHandle, const char* romPath, const char* savePath) {
	struct PkforgeCore* handle = (struct PkforgeCore*)(void*)coreHandle;
	struct mCore* core = handle ? handle->core : NULL;
	if (!core || !romPath) {
		return 0;
	}
	struct VFile* rom = VFileOpen(romPath, O_RDONLY);
	if (!rom) {
		return 0;
	}
	if (!core->loadROM(core, rom)) {
		rom->close(rom);
		return 0;
	}
	if (savePath) {
		struct VFile* save = VFileOpen(savePath, O_RDWR | O_CREAT);
		if (save) {
			core->loadSave(core, save);
		}
	}
	/* The video buffer is allocated and pinned once, at load: the core renders into
	 * it every runFrame; copy_frame only reads it back. RGBA8888, no stride padding
	 * (stride = width). */
	unsigned width = 0;
	unsigned height = 0;
	core->desiredVideoDimensions(core, &width, &height);
	size_t pixels = (size_t)width * height;
	uint32_t* frame = malloc(pixels * sizeof(uint32_t));
	if (frame) {
		core->setVideoBuffer(core, frame, width);
	}
	if (handle) {
		handle->frame = frame;
	}
	core->reset(core);
	return 1;
}

void pkforg_core_run_frame(intptr_t coreHandle) {
	struct mCore* core = asCore(coreHandle);
	if (core) {
		core->runFrame(core);
	}
}

void pkforg_core_reset(intptr_t coreHandle) {
	struct mCore* core = asCore(coreHandle);
	if (core) {
		core->reset(core);
	}
}

void pkforg_core_video_size(intptr_t coreHandle, unsigned* width, unsigned* height) {
	struct mCore* core = asCore(coreHandle);
	if (!core) {
		*width = 0;
		*height = 0;
		return;
	}
	core->desiredVideoDimensions(core, width, height);
}

void pkforg_core_copy_frame(intptr_t coreHandle, uint32_t* buffer, size_t pixels) {
	struct mCore* core = asCore(coreHandle);
	if (!core || !buffer) {
		return;
	}
	unsigned width = 0;
	unsigned height = 0;
	core->desiredVideoDimensions(core, &width, &height);
	size_t needed = (size_t)width * height;
	if (pixels < needed) {
		return;
	}
	/* The buffer was pinned at load; getPixels hands the core's own pointer back
	 * (a single memcpy, no re-render). */
	const void* pixelsOut = NULL;
	size_t stride = 0;
	core->getPixels(core, &pixelsOut, &stride);
	if (pixelsOut) {
		memcpy(buffer, pixelsOut, needed * sizeof(uint32_t));
	}
}

void pkforg_core_set_keys(intptr_t coreHandle, uint32_t keys) {
	struct mCore* core = asCore(coreHandle);
	if (core) {
		core->setKeys(core, keys);
	}
}

int pkforg_core_read_bytes(intptr_t coreHandle, uint32_t address, uint8_t* destination, size_t length) {
	struct mCore* core = asCore(coreHandle);
	if (!core || !destination || length == 0) {
		return 0;
	}
	/* mCoreGetMemoryBlockInfo resolves the block that owns the address (mirrors
	 * included); the read is a single memcpy when the range fits the block,
	 * per-word bus reads otherwise (unmapped or straddling ranges).
	 */
	const struct mCoreMemoryBlock* info = mCoreGetMemoryBlockInfo(core, address);
	if (!info || info->size == 0 || address < info->start
	    || length > (size_t)(info->end - address) || address < info->start) {
		for (size_t i = 0; i < length; i += 4) {
			uint32_t word = core->busRead32(core, address + (uint32_t)i);
			size_t chunk = length - i < 4 ? length - i : 4;
			memcpy(destination + i, &word, chunk);
		}
		return 1;
	}
	size_t blockSize = 0;
	void* block = mCoreGetMemoryBlock(core, info->start, &blockSize);
	if (!block || blockSize < info->size) {
		return 0;
	}
	memcpy(destination, (uint8_t*)block + (address - info->start), length);
	return 1;
}

void pkforg_core_game_title(intptr_t coreHandle, char* title) {
	struct mCore* core = asCore(coreHandle);
	if (!core || !title) {
		if (title) title[0] = '\0';
		return;
	}
	core->getGameTitle(core, title);
	title[16] = '\0';
}

void pkforg_core_game_code(intptr_t coreHandle, char* code) {
	struct mCore* core = asCore(coreHandle);
	if (!core || !code) {
		if (code) code[0] = '\0';
		return;
	}
	core->getGameCode(core, code);
	code[4] = '\0';
}

int pkforg_core_save_state(intptr_t coreHandle, const char* path) {
	struct mCore* core = asCore(coreHandle);
	if (!core || !path) {
		return 0;
	}
	struct VFile* vf = VFileOpen(path, O_WRONLY | O_CREAT | O_TRUNC);
	if (!vf) {
		return 0;
	}
	bool ok = mCoreSaveStateNamed(core, vf, PKFORGE_STATE_FLAGS);
	vf->close(vf);
	return ok ? 1 : 0;
}

int pkforg_core_load_state(intptr_t coreHandle, const char* path) {
	struct mCore* core = asCore(coreHandle);
	if (!core || !path) {
		return 0;
	}
	struct VFile* vf = VFileOpen(path, O_RDONLY);
	if (!vf) {
		return 0;
	}
	bool ok = mCoreLoadStateNamed(core, vf, PKFORGE_STATE_FLAGS);
	vf->close(vf);
	return ok ? 1 : 0;
}

int pkforg_core_read_audio(intptr_t coreHandle, int16_t* destination, int samplesPerChannel) {
	struct mCore* core = asCore(coreHandle);
	if (!core || !destination || samplesPerChannel <= 0) {
		return 0;
	}
	struct blip_t* left = core->getAudioChannel(core, 0);
	struct blip_t* right = core->getAudioChannel(core, 1);
	if (!left || !right) {
		return 0;
	}
	int avail = blip_samples_avail(left);
	if (avail <= 0) {
		return 0;
	}
	int count = avail < samplesPerChannel ? avail : samplesPerChannel;
	/* Interleave L,R into the destination (AudioTrack's stereo frame layout). */
	for (int i = 0; i < count; i++) {
		int16_t l = 0;
		int16_t r = 0;
		blip_read_samples(left, &l, 1, false);
		blip_read_samples(right, &r, 1, false);
		destination[i * 2] = l;
		destination[i * 2 + 1] = r;
	}
	return count;
}
