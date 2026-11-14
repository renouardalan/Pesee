// Génère res\app.ico : un dossier jaune simple.
// Usage : MakeIcon.exe sortie.ico [dossier_apercus]
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

static class MakeIcon
{
    static void Main(string[] args)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
        List<byte[]> images = new List<byte[]>();
        foreach (int s in sizes)
        {
            using (Bitmap bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    Draw(g, s);
                }
                images.Add(s >= 256 ? Png(bmp) : Dib(bmp));
                if (args.Length > 1) bmp.Save(Path.Combine(args[1], "icon-" + s + ".png"), ImageFormat.Png);
            }
        }

        using (FileStream fs = File.Create(args[0]))
        using (BinaryWriter w = new BinaryWriter(fs))
        {
            w.Write((short)0);
            w.Write((short)1);
            w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((short)1);
                w.Write((short)32);
                w.Write(images[i].Length);
                w.Write(offset);
                offset += images[i].Length;
            }
            foreach (byte[] img in images) w.Write(img);
        }
    }

    // Aussi utilisé par MakeStoreAssets.cs (logos du Microsoft Store).
    internal static void Draw(Graphics g, int s)
    {
        float u = s / 32f;
        g.ScaleTransform(u, u);

        // Arrière du dossier (onglet + fond)
        Fill(g, new RectangleF(2, 5, 12, 8), 2, Color.FromArgb(214, 145, 18));
        Fill(g, new RectangleF(2, 7, 28, 20), 2.5f, Color.FromArgb(226, 160, 32));

        // Face avant avec léger dégradé
        RectangleF front = new RectangleF(2, 10, 28, 17.5f);
        using (GraphicsPath p = Rounded(front, 2.5f))
        using (LinearGradientBrush b = new LinearGradientBrush(front, Color.FromArgb(255, 214, 102), Color.FromArgb(250, 186, 50), 90f))
            g.FillPath(b, p);

        // Liseré clair en haut de la face avant
        using (Pen pen = new Pen(Color.FromArgb(150, 255, 236, 170), 0.8f))
            g.DrawLine(pen, 3.5f, 10.6f, 28.5f, 10.6f);
    }

    static void Fill(Graphics g, RectangleF r, float radius, Color c)
    {
        using (GraphicsPath p = Rounded(r, radius))
        using (SolidBrush b = new SolidBrush(c))
            g.FillPath(b, p);
    }

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        float d = radius * 2;
        GraphicsPath p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static byte[] Png(Bitmap bmp)
    {
        using (MemoryStream ms = new MemoryStream())
        {
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }

    // Image ICO classique : BITMAPINFOHEADER + pixels BGRA de bas en haut + masque AND vide.
    static byte[] Dib(Bitmap bmp)
    {
        int s = bmp.Width;
        int maskRow = ((s + 31) / 32) * 4;
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(40);
            w.Write(s);
            w.Write(s * 2);
            w.Write((short)1);
            w.Write((short)32);
            w.Write(0);
            w.Write(s * s * 4 + maskRow * s);
            w.Write(0); w.Write(0); w.Write(0); w.Write(0);

            BitmapData data = bmp.LockBits(new Rectangle(0, 0, s, s), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] row = new byte[s * 4];
            for (int y = s - 1; y >= 0; y--)
            {
                Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), row, 0, row.Length);
                w.Write(row);
            }
            bmp.UnlockBits(data);
            w.Write(new byte[maskRow * s]);
            return ms.ToArray();
        }
    }
}
