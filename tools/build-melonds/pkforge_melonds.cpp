// Minimal PKForge C ABI over melonDS 1.1. The .NET engine owns timing and all
// calls into a handle are serialized on its emulation thread.
#include <Args.h>
#include <GPU3D_Soft.h>
#include <NDS.h>
#include <NDSCart.h>
#include <Savestate.h>

#include <algorithm>
#include <array>
#include <cstdio>
#include <cstdint>
#include <cstring>
#include <fstream>
#include <memory>
#include <string>
#include <vector>

#include <unistd.h>

namespace
{
constexpr size_t kScreenWidth = 256;
constexpr size_t kScreenHeight = 192;
constexpr size_t kCombinedFramePixels = kScreenWidth * kScreenHeight * 2;
constexpr uint64_t kMaximumRomBytes = 512ull * 1024 * 1024;
constexpr uint64_t kMaximumSaveBytes = 16ull * 1024 * 1024;
constexpr uint64_t kMaximumStateBytes = 128ull * 1024 * 1024;

thread_local std::string gLastError;

struct Core
{
    std::unique_ptr<melonDS::NDS> nds;
    std::string savePath;
    std::string title;
    std::string code;
    std::vector<uint32_t> frame = std::vector<uint32_t>(kCombinedFramePixels);
    bool saveDirty = false;
};

bool ReadFile(const std::string& path, uint64_t maximumBytes, std::vector<uint8_t>& output, bool allowEmpty = false)
{
    std::ifstream input(path, std::ios::binary | std::ios::ate);
    if (!input) return false;
    const auto end = input.tellg();
    if (end < 0 || (!allowEmpty && end == 0) || static_cast<uint64_t>(end) > maximumBytes) return false;
    output.resize(static_cast<size_t>(end));
    input.seekg(0, std::ios::beg);
    return end == 0 || static_cast<bool>(input.read(reinterpret_cast<char*>(output.data()), end));
}

bool WriteFileAtomically(const std::string& path, const uint8_t* data, size_t length)
{
    const std::string temporaryPath = path + ".tmp";
    FILE* file = fopen(temporaryPath.c_str(), "wb");
    if (!file) return false;
    bool success = length == 0 || fwrite(data, 1, length, file) == length;
    if (success) success = fflush(file) == 0;
    if (success) success = fsync(fileno(file)) == 0;
    if (fclose(file) != 0) success = false;
    if (success && rename(temporaryPath.c_str(), path.c_str()) != 0) success = false;
    if (!success) unlink(temporaryPath.c_str());
    return success;
}

void SetError(const char* error) { gLastError = error; }

bool FlushSave(Core& core)
{
    if (!core.nds || !core.saveDirty) return true;
    const auto* save = core.nds->GetNDSSave();
    const uint32_t length = core.nds->GetNDSSaveLength();
    if (!save || length == 0)
    {
        core.saveDirty = false;
        return true;
    }
    if (!WriteFileAtomically(core.savePath, save, length)) return false;
    core.saveDirty = false;
    return true;
}

void CopyScreen(const uint32_t* source, uint32_t* destination)
{
    for (size_t i = 0; i < kScreenWidth * kScreenHeight; ++i)
    {
        const uint32_t pixel = source[i]; // melonDS: 0xAARRGGBB
        destination[i] = 0xFF000000u | ((pixel & 0xFFu) << 16)
            | (pixel & 0x0000FF00u) | ((pixel >> 16) & 0xFFu); // RGBA byte order
    }
}
}

extern "C" void pkforge_melonds_mark_save_dirty(void* userdata)
{
    if (auto* core = static_cast<Core*>(userdata)) core->saveDirty = true;
}

extern "C" int pkforge_melonds_available() { return 1; }
extern "C" const char* pkforge_melonds_last_error() { return gLastError.c_str(); }

extern "C" void* pkforge_melonds_create(const char* romPath, const char* savePath)
{
    gLastError.clear();
    if (!romPath || !savePath)
    {
        SetError("Le chemin de ROM ou de sauvegarde est vide.");
        return nullptr;
    }

    std::vector<uint8_t> romBytes;
    if (!ReadFile(romPath, kMaximumRomBytes, romBytes) || romBytes.size() < 0x200)
    {
        SetError("La ROM DS est illisible, trop petite ou dépasse la limite de 512 Mio.");
        return nullptr;
    }

    auto core = std::make_unique<Core>();
    core->savePath = savePath;
    std::vector<uint8_t> saveBytes;
    if (access(savePath, F_OK) == 0 && !ReadFile(savePath, kMaximumSaveBytes, saveBytes, true))
    {
        SetError("La sauvegarde DS existante est illisible ou dépasse la limite de 16 Mio.");
        return nullptr;
    }

    melonDS::NDSArgs args;
    args.OutputSampleRate = 32768.0;
    args.Interpolation = melonDS::AudioInterpolation::None;
    // Keep the arm64 JIT, but use guarded memory to avoid installing the JIT's
    // optional fault handler alongside Android's and PKForge's handlers.
    args.JIT = melonDS::JITArgs{32, true, true, false};
    core->nds = std::make_unique<melonDS::NDS>(std::move(args), core.get());
    core->nds->Reset();

    melonDS::NDSCart::NDSCartArgs cartArgs;
    if (!saveBytes.empty())
    {
        cartArgs.SRAM = std::make_unique<melonDS::u8[]>(saveBytes.size());
        std::memcpy(cartArgs.SRAM.get(), saveBytes.data(), saveBytes.size());
        cartArgs.SRAMLength = static_cast<melonDS::u32>(saveBytes.size());
    }
    auto rom = std::make_unique<melonDS::u8[]>(romBytes.size());
    std::memcpy(rom.get(), romBytes.data(), romBytes.size());
    auto cart = melonDS::NDSCart::ParseROM(std::move(rom), static_cast<melonDS::u32>(romBytes.size()),
        core.get(), std::move(cartArgs));
    if (!cart)
    {
        SetError("melonDS ne reconnaît pas cette ROM comme une cartouche DS valide.");
        return nullptr;
    }

    const auto& header = cart->GetHeader();
    core->title.assign(header.GameTitle, strnlen(header.GameTitle, sizeof(header.GameTitle)));
    core->code.assign(header.GameCode, strnlen(header.GameCode, sizeof(header.GameCode)));
    core->nds->SetNDSCart(std::move(cart));
    core->nds->Reset();
    core->nds->SetupDirectBoot("rom.nds");
    core->nds->Start();
    if (!core->nds->IsRunning())
    {
        SetError("melonDS n'a pas démarré la cartouche DS.");
        return nullptr;
    }
    return core.release();
}

extern "C" void pkforge_melonds_destroy(void* handle)
{
    auto* core = static_cast<Core*>(handle);
    if (!core) return;
    (void)FlushSave(*core);
    if (core->nds) core->nds->Stop();
    delete core;
}

extern "C" int pkforge_melonds_is_running(void* handle)
{
    const auto* core = static_cast<const Core*>(handle);
    return core && core->nds && core->nds->IsRunning() ? 1 : 0;
}

extern "C" void pkforge_melonds_game_title(void* handle, char* output, size_t capacity)
{
    if (!output || capacity == 0) return;
    output[0] = '\0';
    const auto* core = static_cast<const Core*>(handle);
    if (!core) return;
    const size_t length = std::min(capacity - 1, core->title.size());
    std::memcpy(output, core->title.data(), length);
    output[length] = '\0';
}

extern "C" void pkforge_melonds_game_code(void* handle, char* output, size_t capacity)
{
    if (!output || capacity == 0) return;
    output[0] = '\0';
    const auto* core = static_cast<const Core*>(handle);
    if (!core) return;
    const size_t length = std::min(capacity - 1, core->code.size());
    std::memcpy(output, core->code.data(), length);
    output[length] = '\0';
}

extern "C" void pkforge_melonds_run_frame(void* handle, uint32_t keys, int touchDown, int touchX, int touchY)
{
    auto* core = static_cast<Core*>(handle);
    if (!core || !core->nds || !core->nds->IsRunning()) return;
    core->nds->SetKeyMask(~keys & 0xFFFu);
    if (touchDown)
    {
        const auto x = static_cast<melonDS::u16>(std::clamp(touchX, 0, 255));
        const auto y = static_cast<melonDS::u16>(std::clamp(touchY, 0, 191));
        core->nds->TouchScreen(x, y);
    }
    else core->nds->ReleaseScreen();
    core->nds->RunFrame();

    const int front = core->nds->GPU.FrontBuffer;
    auto* top = core->nds->GPU.Framebuffer[front][0].get();
    auto* bottom = core->nds->GPU.Framebuffer[front][1].get();
    if (top && bottom)
    {
        CopyScreen(top, core->frame.data());
        CopyScreen(bottom, core->frame.data() + kScreenWidth * kScreenHeight);
    }
}

extern "C" int pkforge_melonds_copy_frame(void* handle, uint32_t* output, size_t pixelCapacity)
{
    const auto* core = static_cast<const Core*>(handle);
    if (!core || !output || pixelCapacity < kCombinedFramePixels) return 0;
    std::memcpy(output, core->frame.data(), kCombinedFramePixels * sizeof(uint32_t));
    return 1;
}

extern "C" int pkforge_melonds_read_audio(void* handle, int16_t* output, int frameCapacity)
{
    auto* core = static_cast<Core*>(handle);
    if (!core || !core->nds || !output || frameCapacity <= 0) return 0;
    return core->nds->SPU.ReadOutput(output, frameCapacity);
}

extern "C" int pkforge_melonds_flush_save(void* handle)
{
    auto* core = static_cast<Core*>(handle);
    return core && FlushSave(*core) ? 1 : 0;
}

extern "C" size_t pkforge_melonds_save_size(void* handle)
{
    const auto* core = static_cast<const Core*>(handle);
    return core && core->nds ? core->nds->GetNDSSaveLength() : 0;
}

extern "C" size_t pkforge_melonds_copy_save(void* handle, uint8_t* output, size_t capacity)
{
    const auto* core = static_cast<const Core*>(handle);
    if (!core || !core->nds || !output) return 0;
    const auto length = core->nds->GetNDSSaveLength();
    const auto* save = core->nds->GetNDSSave();
    if (!save || length == 0 || capacity < length) return 0;
    std::memcpy(output, save, length);
    return length;
}

extern "C" int pkforge_melonds_save_state(void* handle, const char* path)
{
    auto* core = static_cast<Core*>(handle);
    if (!core || !core->nds || !path) return 0;
    melonDS::Savestate state;
    if (state.Error || !core->nds->DoSavestate(&state) || state.Error) return 0;
    state.Finish();
    return WriteFileAtomically(path, static_cast<const uint8_t*>(state.Buffer()), state.Length()) ? 1 : 0;
}

extern "C" int pkforge_melonds_load_state(void* handle, const char* path)
{
    auto* core = static_cast<Core*>(handle);
    if (!core || !core->nds || !path) return 0;
    std::vector<uint8_t> bytes;
    if (!ReadFile(path, kMaximumStateBytes, bytes) || bytes.size() < 16) return 0;
    melonDS::Savestate state(bytes.data(), static_cast<melonDS::u32>(bytes.size()), false);
    if (state.Error || !core->nds->DoSavestate(&state) || state.Error) return 0;
    return 1;
}
