using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Images du volet Groupe : petite illustration de classe de chaque membre (<c>clips/artworks/mini/&lt;gfx&gt;.swf</c> du client,
    /// exportée dans <c>ressources/Bot/Artworks/Mini/&lt;gfx&gt;.png</c>) et repères du volet <c>Party</c> de <c>core.swf</c>
    /// (<c>ressources/Bot/Party/{chef,suivi,infos,groupe}.png</c>). Chaque PNG est lu une seule fois sur une tâche de fond ;
    /// les images rendues sont partagées par tous les volets et ne doivent jamais être libérées par l'appelant.
    /// Un fichier absent ou illisible donne <c>null</c>, jamais d'exception.
    /// </summary>
    public static class PartyArtworks
    {
        public const string MiniFamily = "Artworks/Mini";
        public const string UiFamily = "Party";
        public const string Leader = "chef", Follow = "suivi", Info = "infos", Party = "groupe";

        private static readonly ConcurrentDictionary<string, Task<Bitmap>> loads = new ConcurrentDictionary<string, Task<Bitmap>>(StringComparer.OrdinalIgnoreCase);
        private static string root = AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>Dossier de base (celui de l'exécutable) ; un test le remplace par un dossier temporaire, ce qui vide le cache.</summary>
        public static string Root
        {
            get => root;
            set
            {
                root = string.IsNullOrEmpty(value) ? AppDomain.CurrentDomain.BaseDirectory : value;
                loads.Clear();
            }
        }

        /// <summary>Chemin du PNG (<c>ressources/Bot/&lt;famille&gt;/&lt;nom&gt;.png</c>, puis <c>Resources/Bot/…</c>), ou <c>null</c>.</summary>
        public static string PathFor(string family, string name)
        {
            if (string.IsNullOrEmpty(family) || string.IsNullOrEmpty(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            string relative = family.Replace('/', Path.DirectorySeparatorChar);
            foreach (string folder in new[] { "ressources", "Resources" })
            {
                string path = Path.Combine(Root, folder, "Bot", relative, name + ".png");
                if (File.Exists(path)) return path;
            }
            return null;
        }

        /// <summary>Illustration du gfx d'un membre (<c>PM</c>), lue en tâche de fond ; <c>null</c> si elle manque.</summary>
        public static Task<Bitmap> MiniAsync(int gfx) =>
            gfx <= 0 ? Task.FromResult<Bitmap>(null) : LoadAsync(MiniFamily, gfx.ToString(CultureInfo.InvariantCulture));

        /// <summary>Illustration déjà chargée, sinon <c>null</c> (le chargement est alors lancé).</summary>
        public static Bitmap Mini(int gfx) => gfx <= 0 ? null : Ready(MiniAsync(gfx));

        /// <summary>Repère du volet (<see cref="Leader"/>, <see cref="Follow"/>, <see cref="Info"/>, <see cref="Party"/>) déjà chargé, sinon <c>null</c>.</summary>
        public static Bitmap Ui(string name) => Ready(LoadAsync(UiFamily, name));

        /// <summary>Lecture d'un PNG de la famille, une seule fois par nom, sur le pool de fils.</summary>
        public static Task<Bitmap> LoadAsync(string family, string name)
        {
            string key = (family ?? string.Empty) + "/" + (name ?? string.Empty);
            return loads.GetOrAdd(key, _ => Task.Run(() => Load(PathFor(family, name))));
        }

        private static Bitmap Ready(Task<Bitmap> task) => task.Status == TaskStatus.RanToCompletion ? task.Result : null;

        private static Bitmap Load(string path)
        {
            if (path == null) return null;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                using (var stream = new MemoryStream(bytes))
                using (var source = Image.FromStream(stream)) return new Bitmap(source);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException
                || error is System.Runtime.InteropServices.ExternalException || error is OutOfMemoryException) { return null; }
        }
    }
}
