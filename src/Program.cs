using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Pesee
{
    public static class Program
    {
        internal const string Version = "1.0.0";
        internal static Icon AppIcon;

        // Lancement en .exe :
        //   Pesee.exe               -> propose l'installation (clic droit)
        //   Pesee.exe "D:\dossier"  -> analyse le dossier
        //   Pesee.exe --uninstall   -> désinstalle
        // Version Microsoft Store (paquet MSIX) : le Store installe, désinstalle et ajoute le clic droit
        // (PeseeMenu.dll). Sans argument, on ouvre directement le choix du dossier.
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
#if !STORE
            if (!IsPackaged)
            {
                if (args.Length == 1 && args[0] == "--uninstall") { Installer.RunUninstall(); return; }
                if (args.Length == 0 && Installer.RunSetup()) return;
            }
#endif
            Run(args, null);
        }

        // Vrai quand l'app tourne depuis son paquet MSIX : avec l'identité du paquet (menu Démarrer),
        // ou lancée par le clic droit depuis le dossier d'installation du Store (WindowsApps).
        internal static readonly bool IsPackaged = DetectPackage(out HasPackageIdentity);
        internal static readonly bool HasPackageIdentity;

        static bool DetectPackage(out bool identity)
        {
            identity = false;
            try
            {
                int len = 0;
                int r = GetCurrentPackageFullName(ref len, null);
                identity = r != 15700; // APPMODEL_ERROR_NO_PACKAGE
            }
            catch (EntryPointNotFoundException) { }
            string exe = Application.ExecutablePath ?? "";
            return identity || exe.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int GetCurrentPackageFullName(ref int length, System.Text.StringBuilder fullName);

        // Lancement depuis Pesee.ps1 : le code est compilé en mémoire par PowerShell.
        // Arguments : "D:\dossier"   options de test : --snapshot capture.png  --size  --threads N
        public static void Run(string[] args, string iconPath)
        {
            // Net à tous les zooms d'écran (125 %, 150 %...), y compris en passant d'un écran à l'autre.
            try { SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }

            // Identité propre dans la barre des tâches : la fenêtre n'est plus rangée avec PowerShell
            // et affiche l'icône de Pesée. Avec l'identité du paquet Store, Windows s'en charge déjà.
            if (!HasPackageIdentity)
            {
                try { SetCurrentProcessExplicitAppUserModelID("Pesee.TailleDossiers"); } catch (EntryPointNotFoundException) { }
            }

            string path = null, snapshot = null;
            bool bySize = false;
            int threads = 0;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--snapshot" && i + 1 < args.Length) snapshot = Path.GetFullPath(args[++i]);
                else if (a == "--threads" && i + 1 < args.Length) int.TryParse(args[++i], out threads);
                else if (a == "--size") bySize = true;
                else path = a;
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
            }
            catch (InvalidOperationException) { }

            // Erreur imprévue dans la fenêtre : notée dans %TEMP%\Pesee-erreur.txt, l'app continue.
            try { Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException); }
            catch (InvalidOperationException) { }
            Application.ThreadException += delegate (object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "Pesee-erreur.txt"), DateTime.Now + "\r\n" + e.Exception + "\r\n\r\n"); }
                catch { }
            };

            try
            {
                if (iconPath != null && File.Exists(iconPath)) AppIcon = new Icon(iconPath);
                else
                {
                    using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Pesee.app.ico"))
                        if (s != null) AppIcon = new Icon(s);
                }
            }
            catch { }

            if (path == null)
            {
                using (FolderBrowserDialog dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "Choisissez le dossier à analyser";
                    if (dlg.ShowDialog() != DialogResult.OK) return;
                    path = dlg.SelectedPath;
                }
            }

            Application.Run(new MainForm(NormalizePath(path), snapshot, bySize, threads));
        }

        [DllImport("user32.dll")]
        static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

        static string NormalizePath(string p)
        {
            // L'Explorateur transmet parfois  D:"  pour la racine D:\ (guillemet échappé).
            p = p.Trim().Trim('"').Replace('/', '\\');
            if (p.Length == 2 && p[1] == ':') return p + "\\";
            bool rooted = (p.Length >= 3 && p[1] == ':' && p[2] == '\\') || p.StartsWith(@"\\");
            if (!rooted && !p.StartsWith("::"))
            {
                try { p = Path.GetFullPath(p); } catch { }
            }
            if (p.Length > 3 && p.EndsWith("\\")) p = p.TrimEnd('\\');
            return p;
        }
    }
}
