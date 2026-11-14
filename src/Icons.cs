using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Pesee
{
    // Icônes génériques (dossier, type de fichier) : obtenues sans toucher au disque, mises en cache.
    // Icônes particulières (dossiers personnalisés via desktop.ini, .exe, .lnk...) : chargées en arrière-plan.
    internal static class Icons
    {
        static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        static int generation;

        public static Bitmap For(Entry e, bool large)
        {
            Bitmap real = large ? e.RealIconLarge : e.RealIconSmall;
            if (real != null) return real;

            string key;
            if (e.IsDir) key = "<dir>";
            else
            {
                int dot = e.Name.LastIndexOf('.');
                key = dot > 0 ? e.Name.Substring(dot) : "<file>";
            }
            key += large ? "|L" : "|S";

            Bitmap bmp;
            if (cache.TryGetValue(key, out bmp)) return bmp;
            string probe = e.IsDir ? "dossier" : (key.StartsWith("<") ? "fichier" : "fichier" + key.Substring(0, key.Length - 2));
            uint attr = e.IsDir ? Native.FILE_ATTRIBUTE_DIRECTORY : Native.FILE_ATTRIBUTE_NORMAL;
            bmp = Native.ShellIcon(probe, attr, large, true);
            cache[key] = bmp;
            return bmp;
        }

        static bool NeedsRealIcon(Entry e)
        {
            if (e.IsDir) return (e.Attributes & (Native.FILE_ATTRIBUTE_READONLY | Native.FILE_ATTRIBUTE_SYSTEM)) != 0;
            string n = e.Name.ToLowerInvariant();
            return n.EndsWith(".exe") || n.EndsWith(".lnk") || n.EndsWith(".ico") || n.EndsWith(".url");
        }

        public static void LoadRealIconsAsync(List<Entry> entries, Control owner)
        {
            int gen = Interlocked.Increment(ref generation);
            List<Entry> todo = new List<Entry>();
            foreach (Entry e in entries)
                if (e.Visible && NeedsRealIcon(e) && e.RealIconSmall == null) todo.Add(e);
            if (todo.Count == 0) return;

            Thread t = new Thread(delegate ()
            {
                int n = 0;
                foreach (Entry e in todo)
                {
                    if (gen != generation || n++ > 2000) return;
                    try
                    {
                        e.RealIconSmall = Native.ShellIcon(e.Path, 0, false, false);
                        e.RealIconLarge = Native.ShellIcon(e.Path, 0, true, false);
                    }
                    catch { }
                }
                try { if (!owner.IsDisposed) owner.BeginInvoke(new MethodInvoker(owner.Invalidate)); }
                catch (InvalidOperationException) { }
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }
    }
}
