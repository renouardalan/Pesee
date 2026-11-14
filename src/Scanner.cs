using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace Pesee
{
    // Un élément affiché : sous-dossier ou fichier du dossier analysé.
    internal sealed class Entry
    {
        public string Name;
        public string Path;
        public bool IsDir;
        public bool IsLink;      // jonction / lien symbolique : non parcouru
        public bool Visible;     // suit le réglage "éléments masqués" de l'Explorateur
        public uint Attributes;

        // Mis à jour par les threads d'analyse (Interlocked).
        public long Size;
        public long Files;
        public long Dirs;
        public int Pending;
        public int Errors;
        public volatile bool Done;

        public long SortSize;    // copie figée pour un tri cohérent
        public volatile Bitmap RealIconSmall, RealIconLarge;
    }

    internal static class Lister
    {
        public static unsafe List<Entry> List(string dir, bool showHidden, bool showSuperHidden, out string error)
        {
            List<Entry> list = new List<Entry>();
            error = null;
            WIN32_FIND_DATAW data;
            IntPtr h = Native.FindFirst(dir, &data);
            if (h == Native.INVALID_HANDLE_VALUE)
            {
                int code = Marshal.GetLastWin32Error();
                if (code == 2 || code == 18) return list; // racine de disque vide
                if (code == 5) error = "Accès refusé à ce dossier.";
                else if (code == 3) error = "Dossier introuvable.";
                else error = "Impossible de lire ce dossier (erreur " + code + ").";
                return list;
            }
            try
            {
                do
                {
                    char* p = data.cFileName;
                    uint a = data.dwFileAttributes;
                    bool isDir = (a & Native.FILE_ATTRIBUTE_DIRECTORY) != 0;
                    if (isDir && Native.IsDotEntry(p)) continue;

                    Entry e = new Entry();
                    e.Name = new string(p);
                    e.Path = Native.Combine(dir, e.Name);
                    e.IsDir = isDir;
                    e.Attributes = a;
                    bool hidden = (a & Native.FILE_ATTRIBUTE_HIDDEN) != 0;
                    bool system = (a & Native.FILE_ATTRIBUTE_SYSTEM) != 0;
                    e.Visible = !hidden || (showHidden && (!system || showSuperHidden));
                    if (isDir)
                    {
                        if ((a & Native.FILE_ATTRIBUTE_REPARSE_POINT) != 0 && Native.IsLinkTag(data.dwReserved0))
                        {
                            e.IsLink = true;
                            e.Done = true;
                        }
                    }
                    else
                    {
                        e.Size = ((long)data.nFileSizeHigh << 32) | data.nFileSizeLow;
                        e.Files = 1;
                        e.Done = true;
                    }
                    list.Add(e);
                } while (Native.FindNext(h, &data));
            }
            finally { Native.FindClose(h); }
            return list;
        }
    }

    // Calcule la taille de chaque sous-dossier en parallèle.
    // Une pile de travail partagée : chaque thread prend un dossier, additionne ses fichiers
    // et empile ses sous-dossiers. Les gros dossiers sont ainsi répartis sur tous les cœurs.
    internal sealed class Scanner
    {
        struct Work
        {
            public readonly string Path;
            public readonly Entry Owner;
            public Work(string path, Entry owner) { Path = path; Owner = owner; }
        }

        readonly ConcurrentStack<Work> stack = new ConcurrentStack<Work>();
        readonly SemaphoreSlim available = new SemaphoreSlim(0);
        readonly Stopwatch watch = new Stopwatch();
        Thread[] threads;
        int outstanding;
        long dirsScanned;
        long elapsedMs;
        volatile bool finished, cancelled;

        public bool Finished { get { return finished; } }
        public long DirsScanned { get { return Interlocked.Read(ref dirsScanned); } }
        public int ThreadCount { get { return threads == null ? 0 : threads.Length; } }
        public long ElapsedMs { get { return finished ? Interlocked.Read(ref elapsedMs) : watch.ElapsedMilliseconds; } }

        public void Start(List<Entry> entries, int threadCount)
        {
            watch.Start();
            List<Work> initial = new List<Work>();
            foreach (Entry e in entries)
            {
                if (!e.IsDir || e.IsLink) continue;
                e.Pending = 1;
                initial.Add(new Work(e.Path, e));
            }
            if (initial.Count == 0)
            {
                watch.Stop();
                finished = true;
                return;
            }
            outstanding = initial.Count;
            // Empilés à l'envers : les premiers de la liste sont traités en premier.
            for (int i = initial.Count - 1; i >= 0; i--) stack.Push(initial[i]);

            if (threadCount <= 0) threadCount = Math.Max(4, Math.Min(Environment.ProcessorCount * 2, 32));
            threads = new Thread[threadCount];
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread(Worker, 256 * 1024);
                threads[i].IsBackground = true;
                threads[i].Start();
            }
            available.Release(initial.Count);
        }

        public void Cancel()
        {
            if (finished || cancelled) return;
            cancelled = true;
            if (threads != null) available.Release(threads.Length);
        }

        unsafe void Worker()
        {
            WIN32_FIND_DATAW data;
            List<Work> children = new List<Work>();
            while (true)
            {
                available.Wait();
                if (cancelled || finished) return;
                Work w;
                if (!stack.TryPop(out w)) continue;

                long bytes = 0, files = 0;
                children.Clear();
                IntPtr h = Native.FindFirst(w.Path, &data);
                if (h == Native.INVALID_HANDLE_VALUE)
                {
                    Interlocked.Increment(ref w.Owner.Errors);
                }
                else
                {
                    do
                    {
                        uint a = data.dwFileAttributes;
                        if ((a & Native.FILE_ATTRIBUTE_DIRECTORY) != 0)
                        {
                            char* p = data.cFileName;
                            if (Native.IsDotEntry(p)) continue;
                            if ((a & Native.FILE_ATTRIBUTE_REPARSE_POINT) != 0 && Native.IsLinkTag(data.dwReserved0)) continue;
                            children.Add(new Work(Native.Combine(w.Path, new string(p)), w.Owner));
                        }
                        else
                        {
                            bytes += ((long)data.nFileSizeHigh << 32) | data.nFileSizeLow;
                            files++;
                        }
                    } while (!cancelled && Native.FindNext(h, &data));
                    Native.FindClose(h);
                }

                Entry o = w.Owner;
                if (bytes != 0) Interlocked.Add(ref o.Size, bytes);
                if (files != 0) Interlocked.Add(ref o.Files, files);
                int c = children.Count;
                if (c > 0)
                {
                    // Compteurs incrémentés AVANT de décrémenter le dossier courant.
                    Interlocked.Add(ref o.Dirs, c);
                    Interlocked.Add(ref o.Pending, c);
                    Interlocked.Add(ref outstanding, c);
                    stack.PushRange(children.ToArray());
                    available.Release(c);
                }
                Interlocked.Increment(ref dirsScanned);
                if (Interlocked.Decrement(ref o.Pending) == 0) o.Done = true;
                if (Interlocked.Decrement(ref outstanding) == 0)
                {
                    Interlocked.Exchange(ref elapsedMs, watch.ElapsedMilliseconds);
                    watch.Stop();
                    finished = true;
                    available.Release(threads.Length);
                    return;
                }
            }
        }
    }
}
