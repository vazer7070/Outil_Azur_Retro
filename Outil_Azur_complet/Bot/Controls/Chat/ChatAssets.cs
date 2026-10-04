using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Outil_Azur_complet.Bot.Controls.Chat
{
    /// <summary>
    /// Images du client utilisées par le chat, aux noms de l'export du lot D3 : symboles de <c>core.swf</c>
    /// (<c>FilterIcon0..8</c>, <c>ButtonChatUp/Down</c>, <c>ButtonSitUp/Down</c>, <c>UI_BannerChatCommandAll</c>,
    /// <c>SmileysHighlight</c>) dans <c>ressources/Bot/UI/Client</c>, smileys (<c>clips/smileys/&lt;n&gt;.swf</c>) dans
    /// <c>ressources/Bot/Smileys</c> et émotes (<c>clips/emotes/&lt;n&gt;.swf</c>) dans <c>ressources/Bot/Emotes</c>
    /// (ou <c>Resources/Bot/…</c>, dossiers du dépôt). Les PNG sont lus sur le pool de threads (<see cref="LoadAsync"/>),
    /// jamais pendant un <c>Paint</c> : l'interface ne lit que le cache (<see cref="Cached"/>). Un fichier absent ou
    /// illisible donne null et le contrôle revient à son rendu dessiné. Les images sont partagées et jamais libérées.
    /// </summary>
    public static class ChatAssets
    {
        public const string ClientFamily = "Client";
        public const string SmileyFamily = "Smileys";
        public const string EmoteFamily = "Emotes";

        private static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private static readonly object sync = new object();
        private static string root;
        private static int generation;

        /// <summary>Dossier qui contient <c>ressources</c> (par défaut celui de l'exécutable) ; le changer vide le cache.</summary>
        public static string Root
        {
            get { lock (sync) return root ?? DefaultRoot(); }
            set { lock (sync) { root = string.IsNullOrWhiteSpace(value) ? null : value; cache.Clear(); generation++; } }
        }

        /// <summary>Symboles du chat dans <c>core.swf</c>, chargés par <see cref="LoadChrome"/>.</summary>
        public static readonly string[] ChromeNames =
        {
            "FilterIcon0", "FilterIcon1", "FilterIcon2", "FilterIcon3", "FilterIcon4", "FilterIcon5", "FilterIcon6", "FilterIcon7",
            "FilterIcon8", "ButtonChatUp", "ButtonChatDown", "ButtonSitUp", "ButtonSitDown", "UI_BannerChatCommandAll", "SmileysHighlight",
        };

        /// <summary>Image en cache (null si absente ou pas encore chargée) ; ne lit jamais le disque.</summary>
        public static Bitmap Cached(string family, string name)
        {
            string key = Key(family, name); if (key == null) return null;
            lock (sync) { cache.TryGetValue(key, out Bitmap image); return image; }
        }

        /// <summary>Vrai si l'image (ou son absence) a déjà été cherchée.</summary>
        public static bool IsKnown(string family, string name)
        {
            string key = Key(family, name); if (key == null) return true;
            lock (sync) return cache.ContainsKey(key);
        }

        /// <summary>Charge les symboles du chat sur le pool de threads.</summary>
        public static Task LoadChrome() => LoadAsync(ChromeNames.Select(name => new KeyValuePair<string, string>(ClientFamily, name)));

        /// <summary>Charge les smileys <c>1..count</c> et les émotes indiquées sur le pool de threads.</summary>
        public static Task LoadPicker(int smileyCount, IEnumerable<int> emotes)
        {
            var wanted = Enumerable.Range(1, Math.Max(0, smileyCount)).Select(id => new KeyValuePair<string, string>(SmileyFamily, Id(id)))
                .Concat((emotes ?? Enumerable.Empty<int>()).Select(id => new KeyValuePair<string, string>(EmoteFamily, Id(id)))).ToList();
            return LoadAsync(wanted);
        }

        /// <summary>Lit les images demandées absentes du cache, hors du thread de l'interface.</summary>
        public static Task LoadAsync(IEnumerable<KeyValuePair<string, string>> wanted)
        {
            var list = (wanted ?? Enumerable.Empty<KeyValuePair<string, string>>()).ToList();
            return Task.Run(() =>
            {
                foreach (KeyValuePair<string, string> entry in list)
                {
                    string key = Key(entry.Key, entry.Value); if (key == null) continue;
                    string folder; int seen;
                    lock (sync) { if (cache.ContainsKey(key)) continue; folder = root ?? DefaultRoot(); seen = generation; }
                    Bitmap image = Read(folder, entry.Key, entry.Value);
                    lock (sync)
                    {
                        if (seen != generation || cache.ContainsKey(key)) { image?.Dispose(); continue; }
                        cache[key] = image;
                    }
                }
            });
        }

        private static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);

        private static string Key(string family, string name)
        {
            if (string.IsNullOrEmpty(family) || string.IsNullOrEmpty(name)) return null;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains("..")) return null;
            return family + "/" + name;
        }

        private static string DefaultRoot() => Path.GetDirectoryName(typeof(ChatAssets).Assembly.Location) ?? AppDomain.CurrentDomain.BaseDirectory;

        private static IEnumerable<string> Candidates(string folder, string family, string name)
        {
            string file = name + ".png";
            if (family == ClientFamily)
            {
                yield return Path.Combine(folder, "ressources", "Bot", "UI", "Client", file);
                yield return Path.Combine(folder, "Resources", "Bot", "Client", file);
            }
            else
            {
                yield return Path.Combine(folder, "ressources", "Bot", family, file);
                yield return Path.Combine(folder, "Resources", "Bot", family, file);
            }
        }

        private static Bitmap Read(string folder, string family, string name)
        {
            foreach (string path in Candidates(folder, family, name))
            {
                if (!File.Exists(path)) continue;
                try
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var source = Image.FromStream(stream)) return new Bitmap(source);
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException ||
                    error is System.Runtime.InteropServices.ExternalException || error is OutOfMemoryException) { }
            }
            return null;
        }
    }
}
