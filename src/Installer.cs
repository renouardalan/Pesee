// Absent de la version Microsoft Store (build-store.ps1 compile avec /define:STORE) : le Store installe et désinstalle.
#if !STORE
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Pesee
{
    // Installation par l'utilisateur courant, sans droits administrateur :
    //  - copie de Pesee.exe dans %LOCALAPPDATA%\Programs\Pesee
    //  - "Analyser la taille" au clic droit (dossiers, fond des dossiers, disques)
    //  - entrée dans Paramètres > Applications pour désinstaller
    internal static class Installer
    {
        static readonly string[] Types = { "Directory", @"Directory\Background", "Drive" };
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Pesee";
        const string Title = "Pesée";

        static string InstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Pesee"); }
        }

        static string InstalledExe { get { return Path.Combine(InstallDir, "Pesee.exe"); } }

        static bool IsInstalled()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Directory\Background\shell\Pesee\command"))
                return k != null && File.Exists(InstalledExe);
        }

        static bool SamePath(string a, string b)
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        // Double-clic sur Pesee.exe. Renvoie false s'il faut continuer vers l'analyse d'un dossier.
        public static bool RunSetup()
        {
            string self = Application.ExecutablePath;
            bool installed = IsInstalled();
            if (installed && SamePath(self, InstalledExe)) return false;

            string question = installed
                ? "Pesée est déjà installé sur ce PC.\n\nLe remplacer par cette version ?"
                : "Ajouter « Analyser la taille » au clic droit de l'Explorateur ?\n\n" +
                  "Pesée s'installe pour votre compte uniquement, sans droits administrateur.\n" +
                  "Vous pourrez le désinstaller depuis Paramètres > Applications.";
            if (MessageBox.Show(question, Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return true;

            try { Install(self); }
            catch (Exception ex)
            {
                MessageBox.Show("L'installation a échoué :\n\n" + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return true;
            }
            MessageBox.Show("C'est prêt !\n\nDans un dossier : Maj + clic droit (ou clic droit > Afficher plus d'options)\n> Analyser la taille.",
                Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }

        public static void RunUninstall()
        {
            if (MessageBox.Show("Désinstaller Pesée et retirer « Analyser la taille » du clic droit ?", Title,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            foreach (string t in Types)
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + t + @"\shell\Pesee", false);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            Notify();
            MessageBox.Show("Pesée a été désinstallé.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);

            // Le .exe en cours d'exécution ne peut pas s'effacer lui-même : on le supprime juste après sa fermeture.
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + InstallDir + "\"");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            try { Process.Start(psi); } catch { }
        }

        static void Install(string sourceExe)
        {
            Directory.CreateDirectory(InstallDir);
            if (!SamePath(sourceExe, InstalledExe)) File.Copy(sourceExe, InstalledExe, true);

            string exe = InstalledExe;
            string icon = "\"" + exe + "\",0";
            foreach (string t in Types)
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + t + @"\shell\Pesee"))
                {
                    k.SetValue("", "Analyser la taille");
                    k.SetValue("Icon", icon);
                    if (t == "Directory") k.SetValue("MultiSelectModel", "Single");
                    using (RegistryKey c = k.CreateSubKey("command"))
                        c.SetValue("", "\"" + exe + "\" \"%V\"");
                }
            }

            using (RegistryKey u = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                u.SetValue("DisplayName", Title);
                u.SetValue("DisplayIcon", icon);
                u.SetValue("DisplayVersion", Program.Version);
                u.SetValue("Publisher", Title);
                u.SetValue("InstallLocation", InstallDir);
                u.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
                u.SetValue("NoModify", 1, RegistryValueKind.DWord);
                u.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                u.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024) + 1, RegistryValueKind.DWord);
            }
            Notify();
        }

        [DllImport("shell32.dll")]
        static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        static void Notify()
        {
            try { SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero); } catch { }
        }
    }
}
#endif
