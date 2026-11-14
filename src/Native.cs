using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security;

namespace Pesee
{
    // Layout identique à WIN32_FIND_DATAW. Passé par pointeur : aucun marshaling.
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public uint ftCreationLow, ftCreationHigh;
        public uint ftLastAccessLow, ftLastAccessHigh;
        public uint ftLastWriteLow, ftLastWriteHigh;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0; // tag de reparse point
        public uint dwReserved1;
        public fixed char cFileName[260];
        public fixed char cAlternateFileName[14];
    }

    [SuppressUnmanagedCodeSecurity]
    internal static unsafe class Native
    {
        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        public const uint FILE_ATTRIBUTE_READONLY = 0x1;
        public const uint FILE_ATTRIBUTE_HIDDEN = 0x2;
        public const uint FILE_ATTRIBUTE_SYSTEM = 0x4;
        public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
        public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        public const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400;

        const int FindExInfoBasic = 1;
        const int FindExSearchNameMatch = 0;
        const int FIND_FIRST_EX_LARGE_FETCH = 2;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        static extern IntPtr FindFirstFileExW(string lpFileName, int fInfoLevelId, IntPtr lpFindFileData,
            int fSearchOp, IntPtr lpSearchFilter, int dwAdditionalFlags);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        static extern bool FindNextFileW(IntPtr hFindFile, IntPtr lpFindFileData);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        public static extern bool FindClose(IntPtr hFindFile);

        public static IntPtr FindFirst(string dir, WIN32_FIND_DATAW* data)
        {
            return FindFirstFileExW(SearchPattern(dir), FindExInfoBasic, (IntPtr)data, FindExSearchNameMatch, IntPtr.Zero, FIND_FIRST_EX_LARGE_FETCH);
        }

        public static bool FindNext(IntPtr h, WIN32_FIND_DATAW* data)
        {
            return FindNextFileW(h, (IntPtr)data);
        }

        // Préfixe \\?\ : chemins longs (> 260 caractères) et pas de normalisation coûteuse.
        static string SearchPattern(string path)
        {
            string p;
            if (path.StartsWith(@"\\?\")) p = path;
            else if (path.StartsWith(@"\\")) p = @"\\?\UNC\" + path.Substring(2);
            else p = @"\\?\" + path;
            return p.EndsWith("\\") ? p + "*" : p + "\\*";
        }

        public static string Combine(string dir, string name)
        {
            return dir.EndsWith("\\") ? dir + name : dir + "\\" + name;
        }

        public static bool IsDotEntry(char* p)
        {
            return p[0] == '.' && (p[1] == '\0' || (p[1] == '.' && p[2] == '\0'));
        }

        // Jonctions et liens symboliques : on ne les suit pas (boucles, double comptage).
        // Les autres reparse points (OneDrive, Google Drive...) sont parcourus normalement.
        public static bool IsLinkTag(uint tag)
        {
            return tag == 0xA0000003 || tag == 0xA000000C || tag == 0xA000001D;
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern int StrCmpLogicalW(string a, string b);

        // ---- Icônes du shell ----

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        public const uint SHGFI_ICON = 0x100;
        public const uint SHGFI_DISPLAYNAME = 0x200;
        public const uint SHGFI_SMALLICON = 0x1;
        public const uint SHGFI_LARGEICON = 0x0;
        public const uint SHGFI_USEFILEATTRIBUTES = 0x10;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        public static Bitmap ShellIcon(string path, uint attributes, bool large, bool useAttributes)
        {
            SHFILEINFO info = new SHFILEINFO();
            uint flags = SHGFI_ICON | (large ? SHGFI_LARGEICON : SHGFI_SMALLICON);
            if (useAttributes) flags |= SHGFI_USEFILEATTRIBUTES;
            IntPtr r = SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), flags);
            if (r == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
            try
            {
                using (Icon icon = Icon.FromHandle(info.hIcon))
                    return icon.ToBitmap();
            }
            finally { DestroyIcon(info.hIcon); }
        }

        public static string DisplayName(string path)
        {
            SHFILEINFO info = new SHFILEINFO();
            IntPtr r = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), SHGFI_DISPLAYNAME);
            if (r == IntPtr.Zero || string.IsNullOrEmpty(info.szDisplayName)) return null;
            return info.szDisplayName;
        }

        // ---- Fenêtre / DPI ----

        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hwnd);

        public static int WindowDpi(IntPtr hwnd)
        {
            try
            {
                uint dpi = GetDpiForWindow(hwnd);
                return dpi == 0 ? 96 : (int)dpi;
            }
            catch (EntryPointNotFoundException) { return 96; }
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // Windows 11 : barre de titre de la même couleur que la fenêtre.
        public static void SetCaptionColor(IntPtr hwnd, Color c)
        {
            int colorref = c.R | (c.G << 8) | (c.B << 16);
            try { DwmSetWindowAttribute(hwnd, 35 /* DWMWA_CAPTION_COLOR */, ref colorref, 4); }
            catch (DllNotFoundException) { }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        public const uint SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

        // ---- Menu contextuel natif (look Windows 11, net à tous les DPI) ----

        [DllImport("user32.dll")]
        public static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool AppendMenuW(IntPtr hMenu, uint flags, UIntPtr id, string text);

        [DllImport("user32.dll")]
        public static extern bool SetMenuDefaultItem(IntPtr hMenu, uint item, uint byPos);

        [DllImport("user32.dll")]
        public static extern int TrackPopupMenuEx(IntPtr hMenu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);

        [DllImport("user32.dll")]
        public static extern bool DestroyMenu(IntPtr hMenu);

        public const uint MF_STRING = 0x0, MF_SEPARATOR = 0x800;
        public const uint TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 0x2;
    }
}
