// PeseeMenu.dll : « Analyser la taille » dans le clic droit moderne de Windows 11.
//
// Windows 11 n'affiche dans son menu principal que les commandes des applications empaquetées
// (MSIX) qui fournissent un objet COM IExplorerCommand. L'Explorateur charge cette DLL,
// demande le titre, l'icône et l'état de la commande, puis appelle Invoke au clic :
// on lance alors Pesee.exe (même dossier que la DLL) avec le chemin du dossier.
//
// Déclarée dans AppxManifest.xml (desktop4:FileExplorerContextMenus + com:SurrogateServer).
// Compilée avec /MT : aucune dépendance au runtime Visual C++.

#include <windows.h>
#include <shobjidl_core.h>
#include <shlwapi.h>
#include <wrl/module.h>
#include <wrl/implements.h>
#include <wrl/client.h>
#include <string>

using namespace Microsoft::WRL;

static HMODULE g_module = nullptr;

// Chemin de Pesee.exe, à côté de cette DLL dans le dossier d'installation du paquet.
static std::wstring PeseeExePath()
{
    wchar_t buf[MAX_PATH * 2] = {};
    DWORD n = GetModuleFileNameW(g_module, buf, ARRAYSIZE(buf));
    if (n == 0 || n >= ARRAYSIZE(buf)) return std::wstring();
    std::wstring p(buf, n);
    size_t slash = p.find_last_of(L'\\');
    if (slash == std::wstring::npos) return std::wstring();
    return p.substr(0, slash + 1) + L"Pesee.exe";
}

// Premier élément de la sélection, s'il s'agit d'un vrai dossier ou disque du système de fichiers
// (pas « Ce PC », une bibliothèque, la Corbeille...).
static HRESULT FirstFileSystemItem(IShellItemArray* items, IShellItem** item)
{
    *item = nullptr;
    if (!items) return E_INVALIDARG;
    DWORD count = 0;
    HRESULT hr = items->GetCount(&count);
    if (FAILED(hr)) return hr;
    if (count != 1) return E_FAIL; // comme l'ancienne version : un seul dossier à la fois
    ComPtr<IShellItem> first;
    hr = items->GetItemAt(0, &first);
    if (FAILED(hr)) return hr;
    SFGAOF attrs = 0;
    hr = first->GetAttributes(SFGAO_FILESYSTEM | SFGAO_FOLDER, &attrs);
    if (FAILED(hr)) return hr;
    if ((attrs & (SFGAO_FILESYSTEM | SFGAO_FOLDER)) != (SFGAO_FILESYSTEM | SFGAO_FOLDER)) return E_FAIL;
    *item = first.Detach();
    return S_OK;
}

class __declspec(uuid("3CE6D7A1-C30C-4FC1-9A7D-1449787722E9")) PeseeCommand final
    : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand, IObjectWithSite>
{
public:
    // ---- IExplorerCommand

    IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* name) override
    {
        return SHStrDupW(L"Analyser la taille", name);
    }

    IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* icon) override
    {
        std::wstring exe = PeseeExePath();
        if (exe.empty()) { *icon = nullptr; return E_FAIL; }
        exe += L",0";
        return SHStrDupW(exe.c_str(), icon);
    }

    IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* tip) override
    {
        *tip = nullptr;
        return E_NOTIMPL;
    }

    IFACEMETHODIMP GetCanonicalName(GUID* guid) override
    {
        *guid = __uuidof(PeseeCommand);
        return S_OK;
    }

    IFACEMETHODIMP GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override
    {
        ComPtr<IShellItem> item;
        *state = SUCCEEDED(FirstFileSystemItem(items, &item)) ? ECS_ENABLED : ECS_HIDDEN;
        return S_OK;
    }

    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override
    {
        ComPtr<IShellItem> item;
        HRESULT hr = FirstFileSystemItem(items, &item);
        if (FAILED(hr)) return hr;

        PWSTR path = nullptr;
        hr = item->GetDisplayName(SIGDN_FILESYSPATH, &path);
        if (FAILED(hr)) return hr;
        std::wstring folder(path);
        CoTaskMemFree(path);

        std::wstring exe = PeseeExePath();
        if (exe.empty()) return E_FAIL;

        // "D:\" : la barre finale doit être doublée, sinon \" serait lu comme un guillemet échappé.
        if (!folder.empty() && folder.back() == L'\\') folder += L'\\';
        std::wstring cmd = L"\"" + exe + L"\" \"" + folder + L"\"";

        STARTUPINFOW si = { sizeof(si) };
        PROCESS_INFORMATION pi = {};
        if (!CreateProcessW(exe.c_str(), &cmd[0], nullptr, nullptr, FALSE, 0, nullptr, nullptr, &si, &pi))
            return HRESULT_FROM_WIN32(GetLastError());
        AllowSetForegroundWindow(pi.dwProcessId); // la fenêtre de Pesée passe devant l'Explorateur
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return S_OK;
    }

    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
    {
        *flags = ECF_DEFAULT;
        return S_OK;
    }

    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override
    {
        *commands = nullptr;
        return E_NOTIMPL;
    }

    // ---- IObjectWithSite (demandé par l'Explorateur, non utilisé)

    IFACEMETHODIMP SetSite(IUnknown* site) override
    {
        m_site = site;
        return S_OK;
    }

    IFACEMETHODIMP GetSite(REFIID riid, void** site) override
    {
        return m_site.CopyTo(riid, site);
    }

private:
    ComPtr<IUnknown> m_site;
};

CoCreatableClass(PeseeCommand)

// ---- Points d'entrée COM

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** instance)
{
    return Module<InProc>::GetModule().GetClassObject(clsid, riid, instance);
}

STDAPI DllCanUnloadNow()
{
    return Module<InProc>::GetModule().GetObjectCount() == 0 ? S_OK : S_FALSE;
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = module;
        DisableThreadLibraryCalls(module);
    }
    return TRUE;
}
