using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Choix du symbole d'un sprite selon son orientation, comme <c>Sprite.setAnim</c> du client 1.34 :
    /// 0 → <c>S</c>, 1 → <c>R</c>, 2 → <c>F</c>, 3 → <c>R</c> retourné, 4 → <c>S</c> retourné, 5 → <c>L</c>, 6 → <c>B</c>,
    /// 7 → <c>L</c> retourné. Le suffixe <c>*</c> du champ graphique (<c>noFlip</c>) supprime le retournement.
    /// </summary>
    public static class ActorOrientation
    {
        private static readonly string[] Suffixes = { "S", "R", "F", "R", "S", "L", "B", "L" };

        public static int Normalize(int direction) => ((direction % 8) + 8) % 8;
        public static string Suffix(int direction) => Suffixes[Normalize(direction)];
        public static bool IsMirrored(int direction, bool noFlip = false)
        {
            int normalized = Normalize(direction);
            return !noFlip && (normalized == 3 || normalized == 4 || normalized == 7);
        }

        /// <summary>
        /// Orientation diagonale la plus proche (<c>convertHeightToFourDirection</c> du client : <c>d | 1</c>), donc une bande
        /// <c>R</c> ou <c>L</c>, retournée ou non.
        /// </summary>
        public static int FourDirection(int direction) => Normalize(direction) | 1;
    }

    public enum SpriteLoadState { Loading, Ready, Missing }

    /// <summary>
    /// Fin d'une bande, colonne <c>fin</c> de <c>ancres.tsv</c> et <c>effets.tsv</c> : <c>boucle</c> (par défaut), <c>arret</c>
    /// (dernière image gardée), <c>static</c> (retour à la pose fixe) ou <c>suite:&lt;anim&gt;</c> (bande suivante).
    /// </summary>
    public enum SpriteEnd { Loop, Stop, Static, Next }

    /// <summary>
    /// Image d'un sprite (une pose ou une bande d'images de même taille) et son ancrage : le coin haut-gauche d'une image
    /// se pose en (ancre + <see cref="XMin"/>, ancre + <see cref="YMin"/>), l'ancre étant le pied du sprite sur la cellule
    /// (convention de <c>ancres.tsv</c>). Une bande peut s'étaler sur plusieurs lignes : <see cref="Columns"/> = largeur du
    /// PNG / <see cref="FrameWidth"/>, images rangées ligne par ligne. Immuable ; l'image appartient à la bibliothèque et ne
    /// doit pas être libérée.
    /// </summary>
    public sealed class SpriteSheet
    {
        private readonly byte[] mask;
        private readonly int imageWidth, imageHeight;

        internal SpriteSheet(Bitmap image, int frames, int frameWidth, int frameHeight, int xMin, int yMin, string file, bool legacy, byte[] mask,
            int framesPerSecond = ActorSprites.FramesPerSecond, SpriteEnd end = SpriteEnd.Loop, string next = null)
        {
            Image = image; Frames = Math.Max(1, frames); FrameWidth = Math.Max(1, frameWidth); FrameHeight = Math.Max(1, frameHeight);
            XMin = xMin; YMin = yMin; File = file; Legacy = legacy; this.mask = mask;
            imageWidth = image?.Width ?? FrameWidth * Frames;
            imageHeight = image?.Height ?? FrameHeight;
            Columns = Math.Max(1, imageWidth / FrameWidth);
            FramesPerSecond = framesPerSecond > 0 ? framesPerSecond : ActorSprites.FramesPerSecond;
            End = end;
            NextAnimation = end == SpriteEnd.Next ? next : null;
        }

        public Bitmap Image { get; }
        public int Frames { get; }
        public int FrameWidth { get; }
        public int FrameHeight { get; }
        public int XMin { get; }
        public int YMin { get; }
        /// <summary>Nom du PNG lu (<c>1001_staticR.png</c>, ou l'ancien <c>1001R.png</c>).</summary>
        public string File { get; }
        /// <summary>Vrai pour un ancien PNG sans ancre : ancre déduite des pixels visibles (milieu, bas).</summary>
        public bool Legacy { get; }
        /// <summary>Images par ligne du PNG (1 ligne pour une bande ordinaire, plusieurs pour une grille).</summary>
        public int Columns { get; }
        /// <summary>Lignes occupées par les images de la bande.</summary>
        public int Rows => (Frames + Columns - 1) / Columns;
        /// <summary>Cadence de la bande (colonne <c>ips</c>, 40 par défaut comme <c>preloader.swf</c>).</summary>
        public int FramesPerSecond { get; }
        public SpriteEnd End { get; }
        /// <summary>Bande qui suit pour <see cref="SpriteEnd.Next"/> (<c>suite:&lt;anim&gt;</c>), sans suffixe d'orientation.</summary>
        public string NextAnimation { get; }
        /// <summary>Durée d'une passe : images × 1000 / ips.</summary>
        public double DurationMs => Frames * 1000.0 / FramesPerSecond;
        internal long Bytes => (long)imageWidth * imageHeight * 4 + (mask?.Length ?? 0);

        /// <summary>
        /// Image à afficher <paramref name="elapsed"/> ms après le début de la bande : en boucle, ou bloquée sur la dernière
        /// image (<paramref name="loop"/> faux : passe unique).
        /// </summary>
        public int FrameAt(double elapsed, bool loop)
        {
            if (Frames <= 1) return 0;
            long index = (long)Math.Floor(Math.Max(0, elapsed) * FramesPerSecond / 1000.0);
            return loop ? (int)(index % Frames) : (int)Math.Min(Frames - 1, index);
        }

        public Rectangle Source(int frame)
        {
            int index = ((frame % Frames) + Frames) % Frames;
            return new Rectangle(index % Columns * FrameWidth, index / Columns * FrameHeight, FrameWidth, FrameHeight);
        }

        /// <summary>Cadre de l'image posée sur <paramref name="anchor"/> (repère de la carte), retournée ou non.</summary>
        public RectangleF Destination(PointF anchor, float scaleX, float scaleY, bool mirrored)
        {
            float left = mirrored ? anchor.X - (XMin + FrameWidth) * scaleX : anchor.X + XMin * scaleX;
            return new RectangleF(left, anchor.Y + YMin * scaleY, FrameWidth * scaleX, FrameHeight * scaleY);
        }

        /// <summary>Vrai si le pixel (<paramref name="x"/>, <paramref name="y"/>) de l'image <paramref name="frame"/> n'est pas transparent.</summary>
        public bool IsOpaque(int frame, int x, int y)
        {
            if (x < 0 || y < 0 || x >= FrameWidth || y >= FrameHeight) return false;
            if (mask == null) return true;
            Rectangle source = Source(frame);
            int px = source.X + x, py = source.Y + y;
            if (px >= imageWidth || py >= imageHeight) return false;
            long bit = (long)py * imageWidth + px;
            return bit / 8 < mask.Length && (mask[bit / 8] & (1 << (int)(bit % 8))) != 0;
        }
    }

    /// <summary>Pose résolue d'un acteur : image (ou silhouette), retournement et diagnostic.</summary>
    public sealed class SpritePose
    {
        internal SpritePose(SpriteSheet sheet, bool mirrored, SpriteLoadState state, string reason, string animation, string suffix = "")
        {
            Sheet = sheet; Mirrored = mirrored; State = state; Reason = reason; Animation = animation; Suffix = suffix ?? string.Empty;
        }
        public SpriteSheet Sheet { get; }
        public bool Mirrored { get; }
        public SpriteLoadState State { get; }
        /// <summary>Repli utilisé ou cause de l'absence (null si le symbole demandé a été trouvé).</summary>
        public string Reason { get; }
        public string Animation { get; }
        /// <summary>Lettre d'orientation demandée (<c>S</c>, <c>R</c>, <c>F</c>, <c>L</c>, <c>B</c> ; vide pour <c>scene</c>).</summary>
        public string Suffix { get; }
        /// <summary>Nom complet demandé, comme le client le compose (<c>staticF</c>, <c>hitR</c>, <c>scene</c>).</summary>
        public string FullName => Animation + Suffix;
    }

    /// <summary>
    /// Sprites d'acteurs (<c>ressources/Bot/sprites</c>) pour une vue de la carte. Les PNG sont lus et décodés sur le pool
    /// de threads, jamais pendant un Paint : <see cref="Resolve"/> rend l'image si elle est prête, sinon l'état
    /// <see cref="SpriteLoadState.Loading"/> et lance la lecture ; <see cref="SheetsLoaded"/> est levé (hors du fil de
    /// l'interface) quand une image demandée arrive. Les images sont partagées entre les vues d'un même dossier par un cache
    /// à compteur de références (une vue garde ses images jusqu'à <see cref="ReleaseAll"/>) ; les images que plus aucune vue
    /// ne tient restent en cache jusqu'à 48 Mo, puis sont libérées de la plus ancienne à la plus récente.
    /// Nouveaux noms d'abord (<c>&lt;gfx&gt;_static&lt;O&gt;.png</c> + <c>ancres.tsv</c>, lot D2), anciens ensuite
    /// (<c>&lt;gfx&gt;&lt;O&gt;.png</c>), puis une autre orientation du même gfx, sinon silhouette.
    /// Lot AN1 : dossier local <c>sprites-local</c> lu avant le dossier versionné, noms cherchés sans tenir compte de la casse,
    /// colonnes <c>ips</c> et <c>fin</c>, bandes en grille, largeur lue dans l'en-tête PNG avant tout décodage (refus au-delà de
    /// 32 767 px), effets fixes (<see cref="ResolveFixed"/>, budget de 32 Mo) et <see cref="Duration"/>.
    /// </summary>
    public sealed class ActorSprites : IDisposable
    {
        /// <summary>Images par seconde des clips de sprites (<c>preloader.swf</c> du client : 40), valeur par défaut de <c>ips</c>.</summary>
        public const int FramesPerSecond = 40;
        /// <summary>Largeur ou hauteur maximale d'un PNG : au-delà, le décodage par libgdiplus (Mono) tue le processus.</summary>
        public const int MaxImageSide = 32767;
        /// <summary>Surface maximale d'un PNG décodé (16 Mpx).</summary>
        public const long MaxImagePixels = 16L * 1024 * 1024;
        /// <summary>Budget des bandes d'effets que plus aucun effet n'utilise (taille décodée, largeur × hauteur × 4).</summary>
        public const long EffectBudget = 32L * 1024 * 1024;
        /// <summary>Dossier local non versionné, à côté de l'exécutable, lu avant <c>ressources/Bot/sprites</c>.</summary>
        public const string LocalFolderName = "sprites-local";
        /// <summary>Dossier des effets (<c>Effets/&lt;famille&gt;/&lt;id&gt;_&lt;anim&gt;.png</c> + <c>effets.tsv</c>).</summary>
        public const string EffectsFolderName = "Effets";
        private const long IdleBudget = 48L * 1024 * 1024;

        private static readonly Regex FourDirectionNames = new Regex(@"^(?:hit|die|anim\d+|emote\d+|emotestatic\d+|bonus)$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex FamilyName = new Regex(@"^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant);
        private static readonly Regex AnimationName = new Regex(@"^[A-Za-z0-9]+$", RegexOptions.CultureInvariant);
        private static readonly Dictionary<string, Library> libraries = new Dictionary<string, Library>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string[]> Alternatives = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // Orientation la plus proche d'abord ; toutes en dernier recours (un monstre n'a souvent que R et L, ou F seul).
            { "S", new[] { "R", "L", "F", "B" } }, { "R", new[] { "S", "F", "L", "B" } }, { "F", new[] { "R", "S", "L", "B" } },
            { "L", new[] { "B", "R", "S", "F" } }, { "B", new[] { "L", "S", "R", "F" } }
        };

        private readonly Library library;
        private readonly Library effects;
        private readonly HashSet<Entry> held = new HashSet<Entry>();
        private readonly HashSet<FixedStrip> strips = new HashSet<FixedStrip>();
        private bool disposed;

        /// <param name="directory">Dossier des sprites ; <c>null</c> = <see cref="DefaultDirectory"/>, avec le dossier local
        /// <see cref="DefaultLocalDirectory"/> devant lui. Un dossier donné (tests) est lu seul.</param>
        public ActorSprites(string directory) : this(directory, string.IsNullOrEmpty(directory) ? DefaultLocalDirectory : null) { }

        /// <param name="directory">Dossier des sprites (<c>null</c> = <see cref="DefaultDirectory"/>).</param>
        /// <param name="localDirectory">Dossier lu avant <paramref name="directory"/> (<c>null</c> : aucun).</param>
        /// <param name="effectsDirectory">Dossier des effets (<c>null</c> : dossier <c>Effets</c> voisin de <paramref name="directory"/>).</param>
        public ActorSprites(string directory, string localDirectory, string effectsDirectory = null)
        {
            Directory = string.IsNullOrEmpty(directory) ? DefaultDirectory : directory;
            LocalDirectory = string.IsNullOrEmpty(localDirectory) ? null : localDirectory;
            EffectsDirectory = string.IsNullOrEmpty(effectsDirectory) ? Sibling(Directory, EffectsFolderName) : effectsDirectory;
            library = Shared(new[] { LocalDirectory, Directory }, "ancres.tsv", IdleBudget);
            effects = Shared(new[] { LocalDirectory == null ? null : Path.Combine(LocalDirectory, EffectsFolderName), EffectsDirectory },
                "effets.tsv", EffectBudget);
        }

        public static string DefaultDirectory => Path.Combine(ExecutableDirectory, "ressources", "Bot", "sprites");

        /// <summary>Dossier <c>sprites-local</c> à côté de l'exécutable (non versionné : export complet déposé sans le pousser).</summary>
        public static string DefaultLocalDirectory => Path.Combine(ExecutableDirectory, LocalFolderName);

        private static string ExecutableDirectory => Path.GetDirectoryName(typeof(ActorSprites).Assembly.Location) ?? ".";

        public string Directory { get; }
        /// <summary>Dossier lu avant <see cref="Directory"/>, ou <c>null</c>.</summary>
        public string LocalDirectory { get; }
        /// <summary>Racine des familles d'effets (<c>Effets/&lt;famille&gt;</c>).</summary>
        public string EffectsDirectory { get; }

        /// <summary>Une image demandée par cette vue vient d'être chargée (ou s'est révélée absente). Levé sur le pool.</summary>
        public event EventHandler SheetsLoaded;

        /// <summary>
        /// Vrai pour les animations que le bot pose sur une diagonale (<see cref="ActorOrientation.FourDirection"/>) : <c>hit</c>,
        /// <c>die</c>, <c>anim&lt;n&gt;</c>, <c>emote&lt;n&gt;</c>, <c>emoteStatic&lt;n&gt;</c> et <c>bonus</c>, exportées en
        /// <c>R</c> et <c>L</c> seulement. Choix du bot : le client chercherait la lettre exacte puis tomberait sur <c>static</c>.
        /// </summary>
        public static bool IsFourDirection(string animation) => !string.IsNullOrEmpty(animation) && FourDirectionNames.IsMatch(animation);

        /// <summary>Lit une valeur de la colonne <c>fin</c> ; faux (et <see cref="SpriteEnd.Loop"/>) si elle n'est pas reconnue.</summary>
        public static bool TryParseEnd(string text, out SpriteEnd end, out string next)
        {
            end = SpriteEnd.Loop; next = null;
            string value = (text ?? string.Empty).Trim();
            if (value.Equals("boucle", StringComparison.OrdinalIgnoreCase)) return true;
            if (value.Equals("arret", StringComparison.OrdinalIgnoreCase)) { end = SpriteEnd.Stop; return true; }
            if (value.Equals("static", StringComparison.OrdinalIgnoreCase)) { end = SpriteEnd.Static; return true; }
            if (value.StartsWith("suite:", StringComparison.OrdinalIgnoreCase) && AnimationName.IsMatch(value.Substring(6)))
            {
                end = SpriteEnd.Next; next = value.Substring(6);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Largeur et hauteur lues dans l'en-tête <c>IHDR</c> d'un PNG, sans le décoder ; faux si le fichier n'est pas un PNG.
        /// </summary>
        public static bool TryReadPngSize(string path, out int width, out int height)
        {
            width = height = 0;
            var header = new byte[24];
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    int read = 0;
                    while (read < header.Length)
                    {
                        int count = stream.Read(header, read, header.Length - read);
                        if (count <= 0) return false;
                        read += count;
                    }
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException
                || error is NotSupportedException) { return false; }
            byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            for (int index = 0; index < signature.Length; index++) if (header[index] != signature[index]) return false;
            if (header[12] != 'I' || header[13] != 'H' || header[14] != 'D' || header[15] != 'R') return false;
            long w = ((long)header[16] << 24) | ((long)header[17] << 16) | ((long)header[18] << 8) | header[19];
            long h = ((long)header[20] << 24) | ((long)header[21] << 16) | ((long)header[22] << 8) | header[23];
            width = (int)Math.Min(int.MaxValue, w); height = (int)Math.Min(int.MaxValue, h);
            return true;
        }

        /// <summary>Nombre d'images demandées par cette vue (sprites et effets) et encore en lecture.</summary>
        public int PendingCount
        {
            get
            {
                int count = 0;
                lock (library.Sync) foreach (Entry entry in held) if (entry.State == SpriteLoadState.Loading) count++;
                lock (effects.Sync) foreach (FixedStrip strip in strips) if (strip.Entry.State == SpriteLoadState.Loading) count++;
                return count;
            }
        }

        /// <summary>Attend la fin des lectures demandées (tests, captures) ; faux si le délai expire.</summary>
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
        /// Pose d'un acteur pour l'animation demandée (<c>static</c>, <c>walk</c>, <c>run</c>, <c>emote&lt;n&gt;</c>, <c>hit</c>,
        /// <c>scene</c>…). Ne lit jamais le disque. Une animation absente rend <see cref="SpriteLoadState.Missing"/> : l'appelant
        /// revient à <c>static</c>. Les animations de <see cref="IsFourDirection"/> prennent l'orientation <c>d | 1</c>.
        /// </summary>
        public SpritePose Resolve(int gfx, int direction, bool noFlip, string animation = "static")
        {
            animation = string.IsNullOrEmpty(animation) ? "static" : animation;
            bool scene = animation == "scene";
            int shown = !scene && IsFourDirection(animation) ? ActorOrientation.FourDirection(direction) : ActorOrientation.Normalize(direction);
            string suffix = scene ? string.Empty : ActorOrientation.Suffix(shown);
            bool mirrored = !scene && ActorOrientation.IsMirrored(shown, noFlip);
            if (gfx <= 0)
                return new SpritePose(null, mirrored, SpriteLoadState.Missing, "L'identifiant graphique de cet acteur n'a pas été transmis.", animation, suffix);
            if (disposed) return new SpritePose(null, mirrored, SpriteLoadState.Missing, "Vue de la carte fermée.", animation, suffix);
            SpriteLoadState state = Acquire(SheetKey(gfx, animation, suffix), gfx, animation, suffix, out LoadResult result);
            if (state == SpriteLoadState.Loading) return new SpritePose(null, mirrored, SpriteLoadState.Loading, null, animation, suffix);
            if (result?.Sheet == null) return new SpritePose(null, mirrored, SpriteLoadState.Missing, result?.Reason, animation, suffix);
            string reason = result.Reason;
            if (reason != null && !scene) reason = "Orientation " + shown + " : " + reason;
            return new SpritePose(result.Sheet, mirrored, SpriteLoadState.Ready, reason, animation, suffix);
        }

        /// <summary>Demande la lecture d'une pose sans attendre (préchargement depuis n'importe quel fil).</summary>
        public void Prefetch(int gfx, int direction, string animation = "static")
        {
            if (gfx > 0 && !disposed) Resolve(gfx, direction, false, animation);
        }

        /// <summary>
        /// Durée d'une passe de la bande <paramref name="animation"/> du sprite <paramref name="gfx"/> (images × 1000 / ips),
        /// sans rien décoder ni bloquer : celle de la bande déjà lue, sinon celle de <c>ancres.tsv</c> s'il est déjà en mémoire
        /// (sa lecture est sinon lancée sur le pool). <c>null</c> si elle est inconnue (l'appelant garde son délai par défaut).
        /// </summary>
        public int? Duration(int gfx, string animation, int direction = 1)
        {
            if (gfx <= 0 || string.IsNullOrEmpty(animation) || disposed) return null;
            bool scene = animation == "scene";
            string suffix = scene ? string.Empty
                : ActorOrientation.Suffix(IsFourDirection(animation) ? ActorOrientation.FourDirection(direction) : direction);
            lock (library.Sync)
            {
                if (library.Entries.TryGetValue(SheetKey(gfx, animation, suffix), out Entry entry) && entry.State == SpriteLoadState.Ready
                    && entry.Result?.Sheet != null && entry.Result.Reason == null)
                    return (int)Math.Round(entry.Result.Sheet.DurationMs);
            }
            return library.AnchorDuration(gfx.ToString(CultureInfo.InvariantCulture), animation + suffix);
        }

        /// <summary>
        /// Bande d'effet <c>Effets/&lt;famille&gt;/&lt;id&gt;_&lt;anim&gt;.png</c> (ancre et colonnes de <c>effets.tsv</c>), sans
        /// orientation ni miroir. La lecture part sur le pool ; la bande est tenue jusqu'au <see cref="FixedStrip.Dispose"/> du
        /// porteur, puis reste en cache dans la limite de <see cref="EffectBudget"/>. Ne lit jamais le disque sur ce fil.
        /// </summary>
        public FixedStrip ResolveFixed(string family, int id, string animation)
        {
            bool valid = !string.IsNullOrEmpty(family) && FamilyName.IsMatch(family) && id >= 0
                && !string.IsNullOrEmpty(animation) && AnimationName.IsMatch(animation);
            string key = (family ?? string.Empty) + "|" + id.ToString(CultureInfo.InvariantCulture) + "|" + (animation ?? string.Empty);
            Entry entry; bool start = false;
            var strip = new FixedStrip(this, family, id, animation);
            lock (effects.Sync)
            {
                if (disposed || !valid)
                {
                    strip.Attach(new Entry(key)
                    {
                        State = SpriteLoadState.Missing,
                        Result = new LoadResult { Reason = disposed ? "Vue de la carte fermée." : "Nom d'effet invalide : " + key + "." }
                    }, false);
                    return strip;
                }
                if (!effects.Entries.TryGetValue(key, out entry))
                {
                    entry = new Entry(key);
                    effects.Entries[key] = entry;
                    start = true;
                }
                effects.Use(entry);
                if (entry.State == SpriteLoadState.Loading && !entry.Waiters.Contains(this)) entry.Waiters.Add(this);
                strip.Attach(entry, true);
                strips.Add(strip);
            }
            if (start)
            {
                Library owner = effects;
                Task.Factory.StartNew(() => owner.Complete(entry, owner.Load(() => owner.LoadFixed(family, id, animation), family + "/" + id)),
                    CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
            }
            return strip;
        }

        /// <summary>Rend à la bibliothèque toutes les images d'acteurs tenues par cette vue (changement de carte).</summary>
        public void ReleaseAll()
        {
            lock (library.Sync)
            {
                foreach (Entry entry in held) library.Release(entry, this);
                held.Clear();
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            ReleaseAll();
            lock (effects.Sync)
            {
                disposed = true;
                foreach (FixedStrip strip in strips.ToArray()) ReleaseStrip(strip);
                strips.Clear();
            }
            SheetsLoaded = null;
        }

        private void ReleaseStrip(FixedStrip strip)
        {
            lock (effects.Sync)
            {
                if (!strip.Held) return;
                strip.Held = false;
                strips.Remove(strip);
                effects.Release(strip.Entry, null);
            }
        }

        private static string SheetKey(int gfx, string animation, string suffix) =>
            gfx.ToString(CultureInfo.InvariantCulture) + "|" + animation + "|" + suffix;

        private static string Sibling(string directory, string name)
        {
            try
            {
                string full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return Path.Combine(Path.GetDirectoryName(full) ?? ".", name);
            }
            catch (Exception error) when (error is ArgumentException || error is NotSupportedException || error is PathTooLongException
                || error is System.Security.SecurityException) { return Path.Combine(directory, "..", name); }
        }

        private static Library Shared(string[] roots, string anchorFile, long budget)
        {
            string[] kept = roots.Where(root => !string.IsNullOrEmpty(root)).ToArray();
            string key = anchorFile + "\n" + string.Join("\n", kept.Select(FullPath));
            lock (libraries)
            {
                if (!libraries.TryGetValue(key, out Library shared)) { shared = new Library(kept, anchorFile, budget); libraries[key] = shared; }
                return shared;
            }
        }

        private static string FullPath(string path)
        {
            try { return Path.GetFullPath(path); }
            catch (Exception error) when (error is ArgumentException || error is NotSupportedException || error is PathTooLongException
                || error is System.Security.SecurityException) { return path; }
        }

        private SpriteLoadState Acquire(string key, int gfx, string animation, string suffix, out LoadResult result)
        {
            Entry entry; bool start = false; SpriteLoadState state;
            lock (library.Sync)
            {
                if (!library.Entries.TryGetValue(key, out entry))
                {
                    entry = new Entry(key);
                    library.Entries[key] = entry;
                    start = true;
                }
                if (held.Add(entry)) library.Use(entry);
                if (entry.State == SpriteLoadState.Loading && !entry.Waiters.Contains(this)) entry.Waiters.Add(this);
                state = entry.State;
                result = entry.Result;
            }
            if (start)
            {
                Library owner = library;
                Task.Factory.StartNew(() => owner.Complete(entry, owner.Load(() => owner.LoadCore(gfx, animation, suffix), gfx.ToString(CultureInfo.InvariantCulture))),
                    CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
            }
            return state;
        }

        private void OnLoaded()
        {
            if (disposed) return;
            try { SheetsLoaded?.Invoke(this, EventArgs.Empty); }
            catch (Exception error) when (!(error is OutOfMemoryException)) { /* un abonné défaillant n'arrête pas la lecture */ }
        }

        /// <summary>
        /// Bande d'effet tenue par un effet de la carte (<see cref="ResolveFixed"/>) : à libérer par <see cref="Dispose"/> quand
        /// l'effet se termine. Lecture de l'état sans blocage, depuis le fil de l'interface.
        /// </summary>
        public sealed class FixedStrip : IDisposable
        {
            private readonly ActorSprites owner;

            internal FixedStrip(ActorSprites owner, string family, int id, string animation)
            {
                this.owner = owner; Family = family; Id = id; Animation = animation;
            }

            internal Entry Entry { get; private set; }
            internal bool Held { get; set; }
            internal void Attach(Entry entry, bool held) { Entry = entry; Held = held; }

            public string Family { get; }
            public int Id { get; }
            public string Animation { get; }

            public SpriteLoadState State
            {
                get { lock (owner.effects.Sync) return Entry.State == SpriteLoadState.Ready && !Held ? SpriteLoadState.Missing : Entry.State; }
            }

            /// <summary>Bande prête, ou <c>null</c> (en lecture, absente ou déjà rendue).</summary>
            public SpriteSheet Sheet
            {
                get { lock (owner.effects.Sync) return Held && Entry.State == SpriteLoadState.Ready ? Entry.Result?.Sheet : null; }
            }

            /// <summary>Cause de l'absence, ou <c>null</c>.</summary>
            public string Reason
            {
                get { lock (owner.effects.Sync) return Entry.Result?.Reason; }
            }

            public void Dispose() => owner.ReleaseStrip(this);
        }

        // ---------------------------------------------------------------- bibliothèque partagée

        internal sealed class LoadResult
        {
            public SpriteSheet Sheet;
            public string Reason;
        }

        internal sealed class Entry
        {
            public Entry(string key) { Key = key; }
            public readonly string Key;
            public SpriteLoadState State = SpriteLoadState.Loading;
            public LoadResult Result;
            public int References;
            public LinkedListNode<Entry> Idle;
            public List<ActorSprites> Waiters = new List<ActorSprites>();
        }

        private sealed class AnchorRow
        {
            public int XMin, YMin, Width, Height, Frames, FramesPerSecond = ActorSprites.FramesPerSecond;
            public SpriteEnd End;
            public string Next;
        }

        /// <summary>Un dossier lu : ses ancres et l'index de ses noms de fichiers sans casse (systèmes sensibles à la casse).</summary>
        private sealed class Folder
        {
            public Folder(string path) { Path = path; }
            public readonly string Path;
            public volatile Dictionary<string, AnchorRow> Anchors;
            public int AnchorsRequested;
            public Dictionary<string, string> Files;
            public DateTime FilesStamp;
        }

        private sealed class Library
        {
            public readonly object Sync = new object();
            public readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            private readonly LinkedList<Entry> idle = new LinkedList<Entry>();
            private readonly string[] roots;
            private readonly string anchorFile;
            private readonly long budget;
            private readonly Dictionary<string, Folder> folders = new Dictionary<string, Folder>(StringComparer.Ordinal);
            private long idleBytes;

            public Library(string[] roots, string anchorFile, long budget)
            {
                this.roots = roots; this.anchorFile = anchorFile; this.budget = budget;
            }

            public void Use(Entry entry)
            {
                if (entry.Idle != null) { idle.Remove(entry.Idle); idleBytes -= Bytes(entry); entry.Idle = null; }
                entry.References++;
            }

            public void Release(Entry entry, ActorSprites view)
            {
                if (view != null) entry.Waiters?.Remove(view);
                if (--entry.References > 0) return;
                entry.Idle = idle.AddLast(entry);
                idleBytes += Bytes(entry);
                while (idleBytes > budget && idle.First != null)
                {
                    Entry oldest = idle.First.Value;
                    idle.RemoveFirst(); oldest.Idle = null;
                    idleBytes -= Bytes(oldest);
                    Entries.Remove(oldest.Key);
                    oldest.Result?.Sheet?.Image.Dispose();
                    oldest.Result = null;
                    oldest.State = SpriteLoadState.Missing;
                }
            }

            private static long Bytes(Entry entry) => entry.Result?.Sheet?.Bytes ?? 0;

            public void Complete(Entry entry, LoadResult result)
            {
                List<ActorSprites> waiters;
                lock (Sync)
                {
                    if (!Entries.TryGetValue(entry.Key, out Entry current) || current != entry)
                    {
                        // Évincée pendant la lecture : plus personne ne la tient.
                        result.Sheet?.Image.Dispose();
                        return;
                    }
                    entry.Result = result;
                    entry.State = result.Sheet != null ? SpriteLoadState.Ready : SpriteLoadState.Missing;
                    if (entry.Idle != null) idleBytes += Bytes(entry);
                    waiters = entry.Waiters;
                    entry.Waiters = null;
                }
                if (waiters != null) foreach (ActorSprites view in waiters) view.OnLoaded();
            }

            public LoadResult Load(Func<LoadResult> core, string label)
            {
                try { return core(); }
                catch (Exception error) when (!(error is ThreadAbortException))
                {
                    return new LoadResult { Reason = "Le sprite " + label + " est illisible : " + error.Message };
                }
            }

            private Folder FolderAt(string path)
            {
                lock (folders)
                {
                    if (!folders.TryGetValue(path, out Folder folder)) { folder = new Folder(path); folders[path] = folder; }
                    return folder;
                }
            }

            private IEnumerable<Folder> Folders(string family) =>
                roots.Select(root => FolderAt(family == null ? root : Path.Combine(root, family)));

            public LoadResult LoadCore(int gfx, string animation, string suffix)
            {
                string id = gfx.ToString(CultureInfo.InvariantCulture);
                Folder[] places = Folders(null).ToArray();
                if (animation == "scene")
                {
                    SpriteSheet scene = ReadSheet(places, id, "scene", allowLegacy: false);
                    return scene != null ? new LoadResult { Sheet = scene }
                        : new LoadResult { Reason = "Le sprite " + gfx + " (scène) est absent du dossier des personnages/monstres." };
                }
                if (animation != "static")
                {
                    SpriteSheet strip = ReadSheet(places, id, animation + suffix, allowLegacy: false);
                    return strip != null ? new LoadResult { Sheet = strip }
                        : new LoadResult { Reason = "Animation " + animation + suffix + " absente pour le sprite " + gfx + "." };
                }
                string unreadable = null;
                foreach (string candidate in Candidates(suffix))
                {
                    SpriteSheet sheet;
                    try { sheet = ReadSheet(places, id, "static" + candidate, allowLegacy: true, legacySuffix: candidate); }
                    catch (InvalidDataException error) { unreadable = error.Message; continue; }
                    if (sheet == null) continue;
                    string reason = candidate == suffix ? null : "variante " + suffix + " absente, visuel " + candidate + " utilisé.";
                    return new LoadResult { Sheet = sheet, Reason = reason };
                }
                SpriteSheet fallback;
                try { fallback = ReadSheet(places, id, "scene", allowLegacy: false); }
                catch (InvalidDataException error) { unreadable = unreadable ?? error.Message; fallback = null; }
                if (fallback != null) return new LoadResult { Sheet = fallback, Reason = "aucune pose static, image de la scène utilisée." };
                return new LoadResult { Reason = unreadable ?? "Le sprite " + gfx + " est absent du dossier des personnages/monstres." };
            }

            public LoadResult LoadFixed(string family, int id, string animation)
            {
                SpriteSheet sheet = ReadSheet(Folders(family).ToArray(), id.ToString(CultureInfo.InvariantCulture), animation, allowLegacy: false);
                return sheet != null ? new LoadResult { Sheet = sheet }
                    : new LoadResult { Reason = "Effet " + family + "/" + id + "_" + animation + " absent du dossier des effets." };
            }

            /// <summary>Durée tirée des ancres déjà lues (dossier local d'abord) ; lance leur lecture sur le pool sinon.</summary>
            public int? AnchorDuration(string id, string name)
            {
                foreach (Folder folder in Folders(null))
                {
                    Dictionary<string, AnchorRow> rows = folder.Anchors;
                    if (rows == null)
                    {
                        if (Interlocked.Exchange(ref folder.AnchorsRequested, 1) == 0)
                            Task.Factory.StartNew(() => Anchors(folder), CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
                        return null;
                    }
                    if (rows.TryGetValue(id + "\t" + name, out AnchorRow row) && row.Frames > 0)
                        return (int)Math.Round(row.Frames * 1000.0 / row.FramesPerSecond);
                }
                return null;
            }

            private static IEnumerable<string> Candidates(string suffix)
            {
                yield return suffix;
                if (Alternatives.TryGetValue(suffix, out string[] others)) foreach (string other in others) yield return other;
            }

            /// <summary>
            /// Lit <c>&lt;id&gt;_&lt;anim&gt;.png</c> (ancre de l'index du même dossier) dans chaque dossier, le local d'abord, puis
            /// si permis l'ancien <c>&lt;id&gt;&lt;O&gt;.png</c>. Un PNG présent mais refusé (trop large, illisible) lève
            /// <see cref="InvalidDataException"/> si aucun autre fichier ne le remplace.
            /// </summary>
            private SpriteSheet ReadSheet(Folder[] places, string id, string anim, bool allowLegacy, string legacySuffix = null)
            {
                string unreadable = null;
                foreach (Folder folder in places)
                {
                    string path = Find(folder, id + "_" + anim + ".png");
                    if (path == null) continue;
                    Bitmap image = Decode(path, out byte[] mask, out string refused);
                    if (image == null) { unreadable = unreadable ?? refused; continue; }
                    Anchors(folder).TryGetValue(id + "\t" + anim, out AnchorRow row);
                    string file = Path.GetFileName(path);
                    if (row != null && row.Width > 0 && row.Height > 0)
                    {
                        int columns = Math.Max(1, image.Width / row.Width), lines = Math.Max(1, image.Height / row.Height);
                        int declared = Math.Max(1, row.Frames), frames;
                        if (lines == 1) frames = row.Width * declared == image.Width ? declared : columns;
                        else frames = Math.Min(declared, columns * lines); // grille : dernière ligne éventuellement incomplète
                        return new SpriteSheet(image, frames, row.Width, Math.Min(row.Height, image.Height), row.XMin, row.YMin, file, false, mask,
                            row.FramesPerSecond, row.End, row.Next);
                    }
                    Point foot = Foot(mask, image.Width, image.Height);
                    return new SpriteSheet(image, 1, image.Width, image.Height, -foot.X, -foot.Y, file, true, mask);
                }
                if (allowLegacy)
                    foreach (Folder folder in places)
                    {
                        string path = Find(folder, id + legacySuffix + ".png");
                        if (path == null) continue;
                        Bitmap legacy = Decode(path, out byte[] legacyMask, out string refused);
                        if (legacy == null) throw new InvalidDataException("Le PNG du sprite " + id + legacySuffix + " est illisible" + (refused == null ? "." : " : " + refused));
                        Point anchor = Foot(legacyMask, legacy.Width, legacy.Height);
                        return new SpriteSheet(legacy, 1, legacy.Width, legacy.Height, -anchor.X, -anchor.Y, Path.GetFileName(path), true, legacyMask);
                    }
                if (unreadable != null) throw new InvalidDataException(unreadable);
                return null;
            }

            /// <summary>Chemin du fichier dans le dossier : nom exact, sinon même nom à la casse près (index refait si le dossier change).</summary>
            private static string Find(Folder folder, string name)
            {
                string exact = Path.Combine(folder.Path, name);
                if (File.Exists(exact)) return exact;
                lock (folder)
                {
                    DateTime stamp;
                    try
                    {
                        if (!System.IO.Directory.Exists(folder.Path)) return null;
                        stamp = System.IO.Directory.GetLastWriteTimeUtc(folder.Path);
                        if (folder.Files == null || stamp != folder.FilesStamp)
                        {
                            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            foreach (string path in System.IO.Directory.EnumerateFiles(folder.Path))
                            {
                                string file = Path.GetFileName(path);
                                if (!files.ContainsKey(file)) files[file] = file;
                            }
                            folder.Files = files; folder.FilesStamp = stamp;
                        }
                    }
                    catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException
                        || error is NotSupportedException || error is System.Security.SecurityException) { return null; }
                    return folder.Files.TryGetValue(name, out string actual) ? Path.Combine(folder.Path, actual) : null;
                }
            }

            /// <summary>Pied d'un PNG sans ancre : milieu des colonnes visibles, sous la dernière ligne visible.</summary>
            private static Point Foot(byte[] mask, int width, int height)
            {
                int left = width, right = -1, bottom = -1;
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        long bit = (long)y * width + x;
                        if ((mask[bit / 8] & (1 << (int)(bit % 8))) == 0) continue;
                        if (x < left) left = x;
                        if (x > right) right = x;
                        bottom = y;
                    }
                return right < 0 ? new Point(width / 2, height) : new Point((left + right + 1) / 2, bottom + 1);
            }

            /// <summary>
            /// Décode un PNG en copie 32 bits indépendante du fichier. L'en-tête est lu d'abord : un fichier qui n'est pas un
            /// PNG, plus large ou plus haut que 32 767 px ou de plus de 16 Mpx est refusé sans décodage (<paramref name="refused"/>).
            /// </summary>
            private static Bitmap Decode(string path, out byte[] mask, out string refused)
            {
                mask = null; refused = null;
                string name = Path.GetFileName(path);
                if (!TryReadPngSize(path, out int width, out int height))
                {
                    refused = name + " n'est pas un PNG lisible.";
                    return null;
                }
                if (width < 1 || height < 1 || width > MaxImageSide || height > MaxImageSide)
                {
                    refused = name + " mesure " + width + " × " + height + " px : au-delà de " + MaxImageSide + " px, la bande doit être exportée en grille.";
                    return null;
                }
                if ((long)width * height > MaxImagePixels)
                {
                    refused = name + " dépasse 16 Mpx (" + width + " × " + height + " px).";
                    return null;
                }
                Bitmap copy = null;
                try
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var source = System.Drawing.Image.FromStream(stream))
                    {
                        if (source.Width < 1 || source.Height < 1 || source.Width > MaxImageSide || source.Height > MaxImageSide
                            || (long)source.Width * source.Height > MaxImagePixels) { refused = name + " : taille refusée."; return null; }
                        copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
                        using (var graphics = Graphics.FromImage(copy))
                        {
                            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
                        }
                    }
                    mask = AlphaMask(copy);
                    return copy;
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException
                    || error is ExternalException || error is OutOfMemoryException || error is NotSupportedException)
                {
                    copy?.Dispose();
                    mask = null;
                    refused = name + " est illisible : " + error.Message;
                    return null;
                }
            }

            private static byte[] AlphaMask(Bitmap image)
            {
                int width = image.Width, height = image.Height;
                var mask = new byte[(int)(((long)width * height + 7) / 8)];
                BitmapData data = image.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var row = new int[width];
                    for (int y = 0; y < height; y++)
                    {
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, width);
                        for (int x = 0; x < width; x++)
                        {
                            if ((row[x] >> 24 & 0xFF) < 16) continue;
                            long bit = (long)y * width + x;
                            mask[bit / 8] |= (byte)(1 << (int)(bit % 8));
                        }
                    }
                }
                finally { image.UnlockBits(data); }
                return mask;
            }

            /// <summary>
            /// Ancres du dossier (<c>ancres.tsv</c> ou <c>effets.tsv</c>), lues une fois : 7 colonnes
            /// (<c>id, anim, xmin, ymin, largeur, hauteur, images</c>) ou 9 avec <c>ips</c> et <c>fin</c>. Clés sans casse.
            /// </summary>
            private Dictionary<string, AnchorRow> Anchors(Folder folder)
            {
                Dictionary<string, AnchorRow> loaded = folder.Anchors;
                if (loaded != null) return loaded;
                lock (folder)
                {
                    if (folder.Anchors != null) return folder.Anchors;
                    var rows = new Dictionary<string, AnchorRow>(StringComparer.OrdinalIgnoreCase);
                    string path = Path.Combine(folder.Path, anchorFile);
                    try
                    {
                        if (File.Exists(path))
                            foreach (string raw in File.ReadAllLines(path))
                            {
                                // Le fichier peut être extrait en CRLF sous Windows (.gitattributes « text=auto »).
                                string line = raw.TrimEnd('\r');
                                if (line.Length == 0 || line[0] == '#') continue;
                                string[] fields = line.Split('\t');
                                if (fields.Length < 7) continue;
                                if (!int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int xMin)
                                    || !int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int yMin)
                                    || !int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
                                    || !int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height)
                                    || !int.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int frames)) continue;
                                var row = new AnchorRow { XMin = xMin, YMin = yMin, Width = width, Height = height, Frames = frames };
                                if (fields.Length > 7 && int.TryParse(fields[7], NumberStyles.None, CultureInfo.InvariantCulture, out int fps)
                                    && fps > 0 && fps <= 1000) row.FramesPerSecond = fps;
                                if (fields.Length > 8 && TryParseEnd(fields[8], out SpriteEnd end, out string next)) { row.End = end; row.Next = next; }
                                rows[fields[0] + "\t" + fields[1]] = row;
                            }
                    }
                    catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException
                        || error is NotSupportedException) { /* ancres illisibles : chaque PNG est ancré sur ses pixels */ }
                    folder.Anchors = rows;
                    return rows;
                }
            }
        }
    }
}
