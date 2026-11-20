/* PKForge's flat C shim over libmgba 0.10.5 (mgba/core/core.h), the layer
 * MgbaNative.cs P/Invokes. Status functions return int (1 = success, 0 = failure),
 * handles use intptr_t, and strings are UTF-8: no C bools to marshal and no struct
 * layouts on the managed side. One core handle per ROM; the caller owns threading.
 *
 * Built by tools/build-mgba/build.sh (NDK, arm64-v8a) into libpkforge_mgba.so,
 * linking libmgba static — the same flags PokeDaisy uses:
 *   -DBUILD_QT=OFF -DBUILD_SDL=OFF -DBUILD_LIBRETRO=OFF -DBUILD_STATIC=ON
 *   -DBUILD_SHARED=OFF -DUSE_FFMPEG=OFF -DUSE_DISCORD_RPC=OFF
 */

#include <mgba/core/core.h>
#include <mgba/core/blip_buf.h>
#include <mgba-util/vfs.h>
#include <stdbool.h>
#include <stdint.h>
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

int pkforg_core_available(void) {
	return 1;
}

intptr_t pkforg_core_create(const char* romPath) {
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
	mCoreLoadConfig(core);
	struct PkforgeCore* handle = malloc(sizeof(struct PkforgeCore));
	if (!handle) {
		core->deinit(core);
		return 0;
	}
	handle->core = core;
	handle->frame = NULL;
	return (intptr_t)handle;
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
	if (!mCoreLoadFile(core, romPath)) {
		return 0;
	}
	if (savePath) {
		struct VFile* save = VFileOpen(savePath, O_RDWR | O_CREAT);
		if (!save || !core->loadSave(core, save)) {
			return 0;
		}
	}
	/* The video buffer is allocated and pinned once, at load: the core renders into
	 * it every runFrame; copy_frame only reads it back. RGBA8888, no stride padding
	 * (stride = width). */
	unsigned width = 0;
	unsigned height = 0;
	core->desiredVideoDimensions(core, &width, &height);
	if (width == 0 || height == 0) {
		return 0;
	}
	if ((size_t)width > SIZE_MAX / height) {
		return 0;
	}
	size_t pixels = (size_t)width * height;
	if (pixels > SIZE_MAX / sizeof(uint32_t)) {
		return 0;
	}
	uint32_t* frame = malloc(pixels * sizeof(uint32_t));
	if (!frame) {
		return 0;
	}
	core->setVideoBuffer(core, frame, width);
	handle->frame = frame;
	core->reset(core);
	return 1;
}

int pkforg_core_flush_save(intptr_t coreHandle, const char* savePath) {
	struct mCore* core = asCore(coreHandle);
	if (!core || !savePath || !core->savedataClone) {
		return 0;
	}

	void* savedata = NULL;
	size_t savedataSize = core->savedataClone(core, &savedata);
	if (savedataSize && !savedata) {
		return 0;
	}
	struct VFile* vf = VFileOpen(savePath, O_RDWR | O_CREAT);
	if (!vf) {
		free(savedata);
		return 0;
	}

	ssize_t fileSize = vf->size(vf);
	if (fileSize < 0) {
		vf->close(vf);
		free(savedata);
		return 0;
	}
	size_t trailerSize = (size_t)fileSize > savedataSize ? (size_t)fileSize - savedataSize : 0;
	uint8_t* trailer = trailerSize ? malloc(trailerSize) : NULL;
	if (trailerSize && (!trailer
	    || vf->seek(vf, (off_t)savedataSize, SEEK_SET) < 0
	    || vf->read(vf, trailer, trailerSize) != (ssize_t)trailerSize)) {
		vf->close(vf);
		free(trailer);
		free(savedata);
		return 0;
	}

	bool ok = vf->seek(vf, 0, SEEK_SET) >= 0
	    && (!savedataSize || vf->write(vf, savedata, savedataSize) == (ssize_t)savedataSize)
	    && (!trailerSize || vf->write(vf, trailer, trailerSize) == (ssize_t)trailerSize);
	if (ok) {
		vf->truncate(vf, savedataSize + trailerSize);
		ok = vf->sync(vf, NULL, 0);
	}
	vf->close(vf);
	free(trailer);
	free(savedata);
	return ok ? 1 : 0;
}

size_t pkforg_core_copy_savedata(intptr_t coreHandle, uint8_t* destination, size_t capacity) {
	struct mCore* core = asCore(coreHandle);
	if (!core || !core->savedataClone || !destination || capacity == 0) {
		return 0;
	}

	void* savedata = NULL;
	size_t size = core->savedataClone(core, &savedata);
	if (!size || !savedata || size > capacity) {
		free(savedata);
		return 0;
	}
	memcpy(destination, savedata, size);
	free(savedata);
	return size;
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
	 * included); copy mapped ranges directly and use byte bus reads when the block
	 * cannot be exposed as a contiguous memory buffer.
	 */
	if (length - 1 > UINT32_MAX - address) {
		return 0;
	}
	const struct mCoreMemoryBlock* info = mCoreGetMemoryBlockInfo(core, address);
	if (!info || info->size == 0 || address < info->start || address >= info->end
	    || length > (size_t)(info->end - address)) {
		return 0;
	}
	size_t blockSize = 0;
	void* block = mCoreGetMemoryBlock(core, info->start, &blockSize);
	size_t offset = address - info->start;
	if (block && offset <= blockSize && length <= blockSize - offset) {
		memcpy(destination, (uint8_t*)block + offset, length);
		return 1;
	}
	for (size_t i = 0; i < length; ++i) {
		destination[i] = (uint8_t)core->busRead8(core, address + (uint32_t)i);
	}
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
