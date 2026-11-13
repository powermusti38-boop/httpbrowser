#include <windows.h>
#include <string>
#include <cwctype>

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    wchar_t path[32768];
    DWORD length = GetModuleFileNameW(nullptr, path, ARRAYSIZE(path));
    if (!length || length >= ARRAYSIZE(path)) return 1;
    std::wstring directory(path, length);
    directory.resize(directory.find_last_of(L"\\/"));
    std::wstring executable = directory + L"\\data\\StreamBrowser.exe";
    const wchar_t* arguments = GetCommandLineW();
    if (*arguments == L'"') { ++arguments; while (*arguments && *arguments != L'"') ++arguments; if (*arguments) ++arguments; }
    else { while (*arguments && !iswspace(*arguments)) ++arguments; }
    while (iswspace(*arguments)) ++arguments;
    std::wstring command = L"\"" + executable + L"\" " + arguments;
    STARTUPINFOW startup{}; startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(executable.c_str(), command.data(), nullptr, nullptr, FALSE, 0, nullptr, directory.c_str(), &startup, &process))
    {
        MessageBoxW(nullptr, L"Die Anwendung konnte nicht gestartet werden. Bitte die ZIP vollständig entpacken und den Ordner data neben StreamBrowser.exe lassen.", L"StreamBrowser", MB_OK | MB_ICONERROR);
        return 1;
    }
    CloseHandle(process.hThread);
    WaitForSingleObject(process.hProcess, INFINITE);
    DWORD code = 1;
    GetExitCodeProcess(process.hProcess, &code);
    CloseHandle(process.hProcess);
    return static_cast<int>(code);
}
