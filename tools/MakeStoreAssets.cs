// Génère les logos du paquet Microsoft Store (dossier Assets) à partir du dessin de MakeIcon.cs.
// Compilé en mémoire avec MakeIcon.cs par build-store.ps1 (Add-Type), puis [MakeStoreAssets]::Main(@(dossier)).
// Pas de .exe : Smart App Control bloque les exécutables non signés.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class MakeStoreAssets
{
    static readonly int[] Scales = { 100, 125, 150, 200, 400 };
    static readonly int[] TargetSizes = { 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256 };

    public static void Main(string[] args)
    {
        string dir = args[0];
        Directory.CreateDirectory(dir);

        // Icône d'application (menu Démarrer, barre des tâches, liste des applis) : le dessin occupe tout le carré.
        foreach (int sc in Scales) Save(dir, "Square44x44Logo.scale-" + sc + ".png", Px(44, sc), Px(44, sc), 1.0f);
        foreach (int t in TargetSizes)
        {
            Save(dir, "Square44x44Logo.targetsize-" + t + ".png", t, t, 1.0f);
            Save(dir, "Square44x44Logo.targetsize-" + t + "_altform-unplated.png", t, t, 1.0f);
            Save(dir, "Square44x44Logo.targetsize-" + t + "_altform-lightunplated.png", t, t, 1.0f);
        }

        // Vignette moyenne (menu Démarrer de Windows 10) : dessin centré, marges confortables.
        foreach (int sc in Scales) Save(dir, "Square150x150Logo.scale-" + sc + ".png", Px(150, sc), Px(150, sc), 0.6f);

        // Logo du paquet (pages du Store, Paramètres > Applications).
        foreach (int sc in Scales) Save(dir, "StoreLogo.scale-" + sc + ".png", Px(50, sc), Px(50, sc), 1.0f);

        Console.WriteLine("Logos générés dans " + dir);
    }

    static int Px(int baseSize, int scale)
    {
        return (int)Math.Round(baseSize * scale / 100.0);
    }

    // Dessin carré de côté min(w,h)*fill, centré sur un fond transparent.
    static void Save(string dir, string name, int w, int h, float fill)
    {
        int s = (int)Math.Round(Math.Min(w, h) * fill);
        using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TranslateTransform((w - s) / 2f, (h - s) / 2f);
                MakeIcon.Draw(g, s);
            }
            bmp.Save(Path.Combine(dir, name), ImageFormat.Png);
        }
    }
}
