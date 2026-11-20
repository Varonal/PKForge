// Android platform services required by melonDS' core library.
// The app owns ROMs, battery saves, audio, input and rendering; this layer only
// supplies the small OS boundary used by the upstream core.
#include <Platform.h>
#include <SPI_Firmware.h>

#include <android/log.h>
#include <dlfcn.h>
#include <sys/stat.h>
#include <unistd.h>

#include <chrono>
#include <condition_variable>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <thread>

extern "C" void pkforge_melonds_mark_save_dirty(void* userdata);

namespace melonDS::Platform
{

void SignalStop(StopReason, void*) {}

struct FileHandle { FILE* file; };

static const char* FileModeString(FileMode mode)
{
    const bool text = (mode & FileMode::Text) != 0;
    if (mode & FileMode::Append) return text ? "a+" : "a+b";
    if ((mode & FileMode::Read) && (mode & FileMode::Write))
        return (mode & FileMode::Preserve) ? (text ? "r+" : "r+b") : (text ? "w+" : "w+b");
    if (mode & FileMode::Write)
        return (mode & FileMode::Preserve) ? (text ? "r+" : "r+b") : (text ? "w" : "wb");
    return text ? "r" : "rb";
}

FileHandle* OpenFile(const std::string& path, FileMode mode)
{
    struct stat info {};
    const bool exists = stat(path.c_str(), &info) == 0;
    if ((mode & FileMode::NoCreate) && !exists) return nullptr;

    FILE* file = fopen(path.c_str(), FileModeString(mode));
    if (!file && !exists && (mode & FileMode::Write) && (mode & FileMode::Preserve)
        && !(mode & FileMode::NoCreate))
    {
        const bool text = (mode & FileMode::Text) != 0;
        file = fopen(path.c_str(), text ? "w+" : "w+b");
    }
    return file ? new FileHandle{file} : nullptr;
}

FileHandle* OpenLocalFile(const std::string& path, FileMode mode) { return OpenFile(path, mode); }
std::string GetLocalFilePath(const std::string& filename) { return filename; }
bool FileExists(const std::string& name) { return access(name.c_str(), F_OK) == 0; }
bool LocalFileExists(const std::string& name) { return FileExists(name); }
bool CheckFileWritable(const std::string& path)
{
    return access(path.c_str(), F_OK) != 0 || access(path.c_str(), W_OK) == 0;
}
bool CheckLocalFileWritable(const std::string& path) { return CheckFileWritable(path); }

bool CloseFile(FileHandle* file)
{
    if (!file) return false;
    const bool success = fclose(file->file) == 0;
    delete file;
    return success;
}

bool IsEndOfFile(FileHandle* file) { return !file || feof(file->file) != 0; }
bool FileReadLine(char* text, int count, FileHandle* file)
{
    return file && fgets(text, count, file->file) != nullptr;
}
u64 FilePosition(FileHandle* file)
{
    if (!file) return 0;
    const auto position = ftello(file->file);
    return position < 0 ? 0 : static_cast<u64>(position);
}
bool FileSeek(FileHandle* file, s64 offset, FileSeekOrigin origin)
{
    if (!file) return false;
    const int whence = origin == FileSeekOrigin::Start ? SEEK_SET
        : origin == FileSeekOrigin::Current ? SEEK_CUR : SEEK_END;
    return fseeko(file->file, static_cast<off_t>(offset), whence) == 0;
}
void FileRewind(FileHandle* file) { if (file) rewind(file->file); }
u64 FileRead(void* data, u64 size, u64 count, FileHandle* file)
{
    return file ? static_cast<u64>(fread(data, static_cast<size_t>(size), static_cast<size_t>(count), file->file)) : 0;
}
bool FileFlush(FileHandle* file) { return file && fflush(file->file) == 0; }
u64 FileWrite(const void* data, u64 size, u64 count, FileHandle* file)
{
    return file ? static_cast<u64>(fwrite(data, static_cast<size_t>(size), static_cast<size_t>(count), file->file)) : 0;
}
u64 FileWriteFormatted(FileHandle* file, const char* format, ...)
{
    if (!file) return 0;
    va_list args;
    va_start(args, format);
    const int result = vfprintf(file->file, format, args);
    va_end(args);
    return result < 0 ? 0 : static_cast<u64>(result);
}
u64 FileLength(FileHandle* file)
{
    if (!file) return 0;
    const auto current = ftello(file->file);
    if (current < 0 || fseeko(file->file, 0, SEEK_END) != 0) return 0;
    const auto length = ftello(file->file);
    (void)fseeko(file->file, current, SEEK_SET);
    return length < 0 ? 0 : static_cast<u64>(length);
}

void Log(LogLevel level, const char* format, ...)
{
    int priority = ANDROID_LOG_DEBUG;
    if (level == LogLevel::Info) priority = ANDROID_LOG_INFO;
    else if (level == LogLevel::Warn) priority = ANDROID_LOG_WARN;
    else if (level == LogLevel::Error) priority = ANDROID_LOG_ERROR;
    va_list args;
    va_start(args, format);
    __android_log_vprint(priority, "PKForge-melonDS", format, args);
    va_end(args);
}

struct Thread { std::thread thread; };
Thread* Thread_Create(std::function<void()> function) { return new Thread{std::thread(std::move(function))}; }
void Thread_Free(Thread* thread)
{
    if (!thread) return;
    if (thread->thread.joinable())
    {
        if (thread->thread.get_id() == std::this_thread::get_id()) thread->thread.detach();
        else thread->thread.join();
    }
    delete thread;
}
void Thread_Wait(Thread* thread)
{
    if (thread && thread->thread.joinable()) thread->thread.join();
}

struct Semaphore
{
    std::mutex mutex;
    std::condition_variable condition;
    int count = 0;
};
Semaphore* Semaphore_Create() { return new Semaphore(); }
void Semaphore_Free(Semaphore* semaphore) { delete semaphore; }
void Semaphore_Reset(Semaphore* semaphore)
{
    std::lock_guard lock(semaphore->mutex);
    semaphore->count = 0;
}
void Semaphore_Wait(Semaphore* semaphore)
{
    std::unique_lock lock(semaphore->mutex);
    semaphore->condition.wait(lock, [semaphore] { return semaphore->count > 0; });
    --semaphore->count;
}
bool Semaphore_TryWait(Semaphore* semaphore, int timeoutMs)
{
    std::unique_lock lock(semaphore->mutex);
    if (timeoutMs == 0)
    {
        if (semaphore->count == 0) return false;
    }
    else if (!semaphore->condition.wait_for(lock, std::chrono::milliseconds(timeoutMs),
                 [semaphore] { return semaphore->count > 0; })) return false;
    --semaphore->count;
    return true;
}
void Semaphore_Post(Semaphore* semaphore, int count)
{
    {
        std::lock_guard lock(semaphore->mutex);
        semaphore->count += count;
    }
    semaphore->condition.notify_all();
}

struct Mutex { std::mutex mutex; };
Mutex* Mutex_Create() { return new Mutex(); }
void Mutex_Free(Mutex* mutex) { delete mutex; }
void Mutex_Lock(Mutex* mutex) { mutex->mutex.lock(); }
void Mutex_Unlock(Mutex* mutex) { mutex->mutex.unlock(); }
bool Mutex_TryLock(Mutex* mutex) { return mutex->mutex.try_lock(); }

void Sleep(u64 usecs) { std::this_thread::sleep_for(std::chrono::microseconds(usecs)); }
u64 GetMSCount()
{
    using namespace std::chrono;
    return static_cast<u64>(duration_cast<milliseconds>(steady_clock::now().time_since_epoch()).count());
}
u64 GetUSCount()
{
    using namespace std::chrono;
    return static_cast<u64>(duration_cast<microseconds>(steady_clock::now().time_since_epoch()).count());
}

void WriteNDSSave(const u8*, u32, u32, u32, void* userdata) { pkforge_melonds_mark_save_dirty(userdata); }
void WriteGBASave(const u8*, u32, u32, u32, void*) {}
void WriteFirmware(const Firmware&, u32, u32, void*) {}
void WriteDateTime(int, int, int, int, int, int, void*) {}

void MP_Begin(void*) {}
void MP_End(void*) {}
int MP_SendPacket(u8*, int, u64, void*) { return 0; }
int MP_RecvPacket(u8*, u64*, void*) { return 0; }
int MP_SendCmd(u8*, int, u64, void*) { return 0; }
int MP_SendReply(u8*, int, u64, u16, void*) { return 0; }
int MP_SendAck(u8*, int, u64, void*) { return 0; }
int MP_RecvHostPacket(u8*, u64*, void*) { return 0; }
u16 MP_RecvReplies(u8*, u64, u16, void*) { return 0; }
int Net_SendPacket(u8*, int, void*) { return 0; }
int Net_RecvPacket(u8*, void*) { return 0; }
void Camera_Start(int, void*) {}
void Camera_Stop(int, void*) {}
void Camera_CaptureFrame(int, u32*, int, int, bool, void*) {}
void Mic_Start(void*) {}
void Mic_Stop(void*) {}
int Mic_ReadInput(s16*, int, void*) { return 0; }
AACDecoder* AAC_Init() { return nullptr; }
void AAC_DeInit(AACDecoder*) {}
bool AAC_Configure(AACDecoder*, int, int) { return false; }
bool AAC_DecodeFrame(AACDecoder*, const void*, int, void*, int) { return false; }
bool Addon_KeyDown(KeyType, void*) { return false; }
void Addon_RumbleStart(u32, void*) {}
void Addon_RumbleStop(void*) {}
float Addon_MotionQuery(MotionQueryType type, void*)
{
    return type == MotionAccelerationZ ? 1.0f : 0.0f;
}

struct DynamicLibrary { void* handle; };
DynamicLibrary* DynamicLibrary_Load(const char* library)
{
    void* handle = dlopen(library, RTLD_NOW | RTLD_LOCAL);
    return handle ? new DynamicLibrary{handle} : nullptr;
}
void DynamicLibrary_Unload(DynamicLibrary* library)
{
    if (!library) return;
    if (library->handle) dlclose(library->handle);
    delete library;
}
void* DynamicLibrary_LoadFunction(DynamicLibrary* library, const char* name)
{
    return library && library->handle ? dlsym(library->handle, name) : nullptr;
}

} // namespace melonDS::Platform
