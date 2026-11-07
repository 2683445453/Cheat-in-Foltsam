#include <windows.h>
#include <tlhelp32.h>
#include <cstdint>
#include <string>

class Handle
{
public:
    explicit Handle(HANDLE value) : value(value) {}
    ~Handle() { if (valid()) CloseHandle(value); }
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;

    bool valid() const { return value && value != INVALID_HANDLE_VALUE; }
    operator HANDLE() const { return value; }

private:
    HANDLE value;
};

static std::wstring FindGame(DWORD& pid)
{
    Handle snapshot(CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0));
    PROCESSENTRY32W process{ sizeof(process) };
    if (!snapshot.valid() || !Process32FirstW(snapshot, &process))
        return L"无法读取进程列表。";

    do
    {
        if (_wcsicmp(process.szExeFile, L"Flotsam.exe") == 0)
        {
            if (pid)
                return L"请只保留一个 Flotsam 进程。";
            pid = process.th32ProcessID;
        }
    } while (Process32NextW(snapshot, &process));
    return pid ? L"" : L"请先启动 Flotsam 并进入主菜单。";
}

static uintptr_t ModuleBase(DWORD pid, const wchar_t* name, bool fullPath = false)
{
    HANDLE raw;
    do
    {
        raw = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE, pid);
    } while (raw == INVALID_HANDLE_VALUE && GetLastError() == ERROR_BAD_LENGTH);

    Handle snapshot(raw);
    MODULEENTRY32W module{ sizeof(module) };
    if (!snapshot.valid() || !Module32FirstW(snapshot, &module))
        return 0;
    do
    {
        if (_wcsicmp(fullPath ? module.szExePath : module.szModule, name) == 0)
            return reinterpret_cast<uintptr_t>(module.modBaseAddr);
    } while (Module32NextW(snapshot, &module));
    return 0;
}

static bool CallRemote(HANDLE process, uintptr_t address, void* argument, DWORD& result)
{
    if (!address)
        return false;
    Handle thread(CreateRemoteThread(process, nullptr, 0,
        reinterpret_cast<LPTHREAD_START_ROUTINE>(address), argument, 0, nullptr));
    if (!thread.valid())
        return false;

    if (WaitForSingleObject(thread, 20000) != WAIT_OBJECT_0)
    {
        result = STILL_ACTIVE;
        return false;
    }
    return GetExitCodeThread(thread, &result) != FALSE;
}

static std::wstring Inject()
{
    DWORD pid = 0;
    auto error = FindGame(pid);
    if (!error.empty())
        return error;

    Handle lock(CreateMutexW(nullptr, TRUE, L"Local\\FlotsamTrainerLauncher"));
    if (!lock.valid() || GetLastError() == ERROR_ALREADY_EXISTS)
        return L"修改器正在连接。";

    wchar_t launcher[MAX_PATH];
    const auto length = GetModuleFileNameW(nullptr, launcher, ARRAYSIZE(launcher));
    if (!length || length >= ARRAYSIZE(launcher))
        return L"修改器路径过长，请移到更短的目录。";
    const std::wstring fullPath(launcher, length);
    const auto directory = fullPath.substr(0, fullPath.find_last_of(L'\\'));
    const auto bootstrap = directory + L"\\Flotsam.Bootstrap.dll";
    if (bootstrap.size() >= MAX_PATH)
        return L"修改器路径过长，请移到更短的目录。";
    if (GetFileAttributesW(bootstrap.c_str()) == INVALID_FILE_ATTRIBUTES ||
        GetFileAttributesW((directory + L"\\Flotsam.Trainer.dll").c_str()) == INVALID_FILE_ATTRIBUTES ||
        GetFileAttributesW((directory + L"\\0Harmony.dll").c_str()) == INVALID_FILE_ATTRIBUTES)
        return L"请完整解压修改器，并保持 EXE 和 DLL 在同一目录。";

    if (!ModuleBase(pid, L"mono-2.0-bdwgc.dll"))
        return L"游戏尚未进入主菜单，或此版本不受支持。";

    Handle process(OpenProcess(PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION |
        PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ, FALSE, pid));
    if (!process.valid())
        return L"无法连接游戏进程。请用与游戏相同的权限启动修改器。";

    if (ModuleBase(pid, bootstrap.c_str(), true))
        return L"上次连接仍在初始化，请稍后重试。";

    uintptr_t remoteBootstrap = 0;
    {
        const auto load = GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "LoadLibraryW");
        HMODULE owner = nullptr;
        wchar_t ownerPath[MAX_PATH];
        if (!load || !GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
            GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT, reinterpret_cast<LPCWSTR>(load), &owner) ||
            !GetModuleFileNameW(owner, ownerPath, ARRAYSIZE(ownerPath)))
            return L"无法定位系统加载器。";

        const std::wstring ownerName(ownerPath);
        const auto remoteOwner = ModuleBase(pid, ownerName.substr(ownerName.find_last_of(L'\\') + 1).c_str());
        if (!remoteOwner)
            return L"无法定位游戏进程内的系统加载器。";

        const auto bytes = (bootstrap.size() + 1) * sizeof(wchar_t);
        auto remotePath = VirtualAllocEx(process, nullptr, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        if (!remotePath)
            return L"无法为游戏分配注入参数。";

        DWORD result = 0;
        const bool loaded = WriteProcessMemory(process, remotePath, bootstrap.c_str(), bytes, nullptr) &&
            CallRemote(process, remoteOwner + reinterpret_cast<uintptr_t>(load) -
                reinterpret_cast<uintptr_t>(owner), remotePath, result);
        if (loaded || result != STILL_ACTIVE)
            VirtualFreeEx(process, remotePath, 0, MEM_RELEASE);
        remoteBootstrap = loaded ? ModuleBase(pid, bootstrap.c_str(), true) : 0;
        if (!remoteBootstrap)
            return L"无法加载修改器 DLL。";
    }

    const auto local = LoadLibraryExW(bootstrap.c_str(), nullptr, DONT_RESOLVE_DLL_REFERENCES);
    if (!local)
        return L"无法读取修改器入口。";
    const auto start = GetProcAddress(local, "StartTrainer");
    const auto offset = reinterpret_cast<uintptr_t>(start) - reinterpret_cast<uintptr_t>(local);
    FreeLibrary(local);

    DWORD result = 0;
    if (!start || !CallRemote(process, remoteBootstrap + offset, nullptr, result))
        return L"游戏内初始化未完成。请回到游戏后重试。";

    switch (result)
    {
    case 0: return L"";
    case 20: return L"游戏版本不匹配。";
    case 10: case 21: case 22: return L"游戏尚未就绪。请进入主菜单后重试。";
    default: return L"游戏内初始化失败（" + std::to_wstring(result) + L"）。";
    }
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    const auto error = Inject();
    if (error.empty())
        return 0;
    MessageBoxW(nullptr, error.c_str(), L"Flotsam 修改器", MB_OK | MB_ICONERROR);
    return 1;
}
