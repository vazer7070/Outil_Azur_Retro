using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Petites images des textes au-dessus des acteurs (étoiles, smileys, icônes d'émote, emblèmes) lues sur le pool de
    /// threads, jamais pendant un Paint : <see cref="TryGet"/> rend l'image si elle est prête, sinon null et lance la
    /// lecture ; <see cref="Loaded"/> est levé (hors du fil de l'interface) quand une image arrive. Les fichiers viennent
    /// des familles exportées du client (<c>ressources/Bot/Smileys</c>, <c>Emotes</c>, <c>UI/Client</c>) ; une image
    /// absente reste absente (l'appelant dessine un repli vectoriel). Les images appartiennent à ce cache et sont libérées
    /// par <see cref="Dispose"/>, sur le fil de l'interface, après le dernier dessin.
    /// </summary>
    public sealed class OverheadImages : IDisposable
    {
        private const int MaxEntries = 256;
        private readonly object sync = new object();
        private readonly Dictionary<string, Slot> slots = new Dictionary<string, Slot>(StringComparer.Ordinal);
        private bool disposed;

        private sealed class Slot
        {
            public Bitmap Image;
            public bool Loading = true;
        }

        public OverheadImages(string root = null)
        {
            Root = string.IsNullOrEmpty(root) ? DefaultRoot : root;
        }

        /// <summary><c>ressources/Bot</c> à côté de l'exécutable.</summary>
        public static string DefaultRoot => Path.Combine(Path.GetDirectoryName(typeof(OverheadImages).Assembly.Location) ?? ".", "ressources", "Bot");

        public string Root { get; }

        /// <summary>Une image demandée vient d'être lue (ou s'est révélée absente). Levé sur le pool.</summary>
        public event EventHandler Loaded;

        public int PendingCount
        {
            get { lock (sync) { int count = 0; foreach (Slot slot in slots.Values) if (slot.Loading) count++; return count; } }
        }

        public bool WaitForPending(int milliseconds)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (PendingCount > 0)
            {
                if (clock.ElapsedMilliseconds > milliseconds) return false;
                Thread.Sleep(5);
            }
            return true;
        }

        /// <summary>
        /// Image de la clé <paramref name="key"/>, produite une fois par <paramref name="load"/> sur le pool (la fonction rend
        /// une image qui lui appartient, ou null). Rend null tant qu'elle n'est pas prête ou si elle est absente.
        /// </summary>
        public Bitmap TryGet(string key, Func<Bitmap> load)
        {
            if (string.IsNullOrEmpty(key) || load == null) return null;
            lock (sync)
            {
                if (disposed) return null;
                if (slots.TryGetValue(key, out Slot existing)) return existing.Loading ? null : existing.Image;
                if (slots.Count >= MaxEntries) return null;
                slots[key] = new Slot();
            }
            Task.Factory.StartNew(() => Complete(key, Run(load)), CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
            return null;
        }

        /// <summary>PNG <paramref name="relative"/> sous <see cref="Root"/> (« Smileys/3.png »).</summary>
        public Bitmap File(string relative) => TryGet("file:" + relative, () => Decode(Path.Combine(Root, relative)));

        /// <summary>
        /// Masque blanc <paramref name="relative"/> recoloré en <paramref name="rgb"/> (0xRRGGBB) en gardant son alpha, comme
        /// <c>Color.setRGB</c> du client ; <paramref name="alpha"/> (0 à 255) multiplie l'alpha.
        /// </summary>
        public Bitmap Tinted(string relative, int rgb, int alpha = 255) =>
            TryGet("tint:" + relative + ":" + rgb.ToString("X6", System.Globalization.CultureInfo.InvariantCulture) + ":" + alpha,
                () => Tint(Decode(Path.Combine(Root, relative)), rgb, alpha));

        public void Dispose()
        {
            List<Bitmap> images = new List<Bitmap>();
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                foreach (Slot slot in slots.Values) if (slot.Image != null) images.Add(slot.Image);
                slots.Clear();
            }
            foreach (Bitmap image in images) image.Dispose();
            Loaded = null;
        }

        private static Bitmap Run(Func<Bitmap> load)
        {
            try { return load(); }
            catch (Exception error) when (!(error is ThreadAbortException)) { return null; }
        }

        private void Complete(string key, Bitmap image)
        {
            lock (sync)
            {
                if (disposed || !slots.TryGetValue(key, out Slot slot)) { image?.Dispose(); return; }
                slot.Image = image;
                slot.Loading = false;
            }
            try { Loaded?.Invoke(this, EventArgs.Empty); }
            catch (Exception error) when (!(error is OutOfMemoryException)) { /* un abonné défaillant n'arrête pas les lectures */ }
        }

        /// <summary>Lit un PNG en copie 32 bits indépendante du fichier ; null s'il est absent, trop grand ou illisible.</summary>
        internal static Bitmap Decode(string path)
        {
            Bitmap copy = null;
            try
            {
                if (!System.IO.File.Exists(path)) return null;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var source = Image.FromStream(stream))
                {
                    if (source.Width < 1 || source.Height < 1 || source.Width > 1024 || source.Height > 1024) return null;
                    copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
                    using (var graphics = Graphics.FromImage(copy))
                    {
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.DrawImage(source, 0, 0, source.Width, source.Height);
                    }
                }
                return copy;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException
                || error is ExternalException || error is OutOfMemoryException || error is NotSupportedException)
            {
                copy?.Dispose();
                return null;
            }
        }

        private static Bitmap Tint(Bitmap image, int rgb, int alpha)
        {
            if (image == null) return null;
            int color = rgb & 0xFFFFFF;
            alpha = Math.Max(0, Math.Min(255, alpha));
            BitmapData data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                var row = new int[image.Width];
                for (int y = 0; y < image.Height; y++)
                {
                    IntPtr line = IntPtr.Add(data.Scan0, y * data.Stride);
                    Marshal.Copy(line, row, 0, row.Length);
                    for (int x = 0; x < row.Length; x++)
                    {
                        int a = (int)((uint)row[x] >> 24) * alpha / 255;
                        row[x] = (a << 24) | color;
                    }
                    Marshal.Copy(row, 0, line, row.Length);
                }
            }
            finally { image.UnlockBits(data); }
            return image;
        }
    }
}
