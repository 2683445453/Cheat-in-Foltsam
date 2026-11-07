#include <windows.h>
#include <string>

extern "C" IMAGE_DOS_HEADER __ImageBase;

static DWORD InitializeTrainer()
{
    const HMODULE mono = GetModuleHandleW(L"mono-2.0-bdwgc.dll");
    if (!mono)
        return 10;

#define MONO(name, signature) auto name = reinterpret_cast<signature>(GetProcAddress(mono, #name))
    MONO(mono_get_root_domain, void*(*)());
    MONO(mono_thread_attach, void*(*)(void*));
    MONO(mono_thread_detach, void(*)(void*));
    MONO(mono_domain_assembly_open, void*(*)(void*, const char*));
    MONO(mono_assembly_get_image, void*(*)(void*));
    MONO(mono_class_from_name, void*(*)(void*, const char*, const char*));
    MONO(mono_class_get_method_from_name, void*(*)(void*, const char*, int));
    MONO(mono_string_new_utf16, void*(*)(void*, const wchar_t*, int));
    MONO(mono_runtime_invoke, void*(*)(void*, void*, void**, void**));
    MONO(mono_object_unbox, void*(*)(void*));
#undef MONO

    if (!mono_get_root_domain || !mono_thread_attach || !mono_thread_detach ||
        !mono_domain_assembly_open || !mono_assembly_get_image || !mono_class_from_name ||
        !mono_class_get_method_from_name || !mono_string_new_utf16 || !mono_runtime_invoke ||
        !mono_object_unbox)
        return 11;

    auto domain = mono_get_root_domain();
    if (!domain)
        return 10;

    wchar_t modulePath[32768];
    const auto length = GetModuleFileNameW(reinterpret_cast<HMODULE>(&__ImageBase), modulePath, ARRAYSIZE(modulePath));
    if (!length || length >= ARRAYSIZE(modulePath))
        return 12;

    const std::wstring fullPath(modulePath, length);
    const auto directory = fullPath.substr(0, fullPath.find_last_of(L'\\'));
    const auto managedPath = directory + L"\\Flotsam.Trainer.dll";
    const auto size = WideCharToMultiByte(CP_UTF8, 0, managedPath.c_str(), -1, nullptr, 0, nullptr, nullptr);
    if (!size)
        return 12;
    std::string utf8Path(size, '\0');
    if (!WideCharToMultiByte(CP_UTF8, 0, managedPath.c_str(), -1, utf8Path.data(), size, nullptr, nullptr))
        return 12;

    auto thread = mono_thread_attach(domain);
    if (!thread)
        return 10;

    DWORD status = 13;
    if (auto assembly = mono_domain_assembly_open(domain, utf8Path.c_str()))
    {
        auto image = mono_assembly_get_image(assembly);
        auto entry = mono_class_from_name(image, "FlotsamTrainer", "Entry");
        auto start = entry ? mono_class_get_method_from_name(entry, "Start", 1) : nullptr;
        if (start)
        {
            void* exception = nullptr;
            void* args[] = { mono_string_new_utf16(domain, directory.c_str(), static_cast<int>(directory.size())) };
            auto result = mono_runtime_invoke(start, nullptr, args, &exception);
            status = !exception && result ? *static_cast<int*>(mono_object_unbox(result)) : 14;
        }
    }
    mono_thread_detach(thread);
    return status;
}

extern "C" __declspec(dllexport) DWORD WINAPI StartTrainer(void*)
{
    const auto status = InitializeTrainer();
    // The managed panel owns the session. Nothing calls back into this DLL.
    FreeLibraryAndExitThread(reinterpret_cast<HMODULE>(&__ImageBase), status);
}
