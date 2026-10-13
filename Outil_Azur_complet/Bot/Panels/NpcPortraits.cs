using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Illustrations des dialogues PNJ : le client charge <c>clips/artworks/big/&lt;n&gt;.swf</c> (<c>customArtwork</c> du PNJ,
    /// sinon son gfx). Leur export en PNG (<c>Resources/Bot/Portraits/&lt;n&gt;.png</c>, copié dans <c>ressources/Bot/Portraits</c>)
    /// relève de l'export des icônes du client ; tant qu'il manque, le dialogue s'affiche sans illustration.
    /// La lecture se fait hors du thread de l'interface ; l'image rendue appartient à l'appelant, qui la libère.
    /// </summary>
    public static class NpcPortraits
    {
        private static string root = AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>Dossiers essayés dans l'ordre, relatifs à <see cref="Root"/>.</summary>
        public static readonly string[] Folders = {
            Path.Combine("ressources", "Bot", "Portraits"), Path.Combine("Resources", "Bot", "Portraits") };

        /// <summary>Dossier de base (celui de l'exécutable) ; un test le remplace par un dossier temporaire.</summary>
        public static string Root
        {
            get => root;
            set => root = string.IsNullOrEmpty(value) ? AppDomain.CurrentDomain.BaseDirectory : value;
        }

        /// <summary>Chemin du PNG de l'illustration, ou <c>null</c> s'il n'existe pas.</summary>
        public static string PathFor(int artworkId)
        {
            if (artworkId <= 0) return null;
            string baseFolder = Root;
            foreach (string folder in Folders)
            {
                string path = Path.Combine(baseFolder, folder, artworkId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".png");
                if (File.Exists(path)) return path;
            }
            return null;
        }

        /// <summary>
        /// Lit l'illustration en mémoire (le fichier n'est pas verrouillé) ; <c>null</c> si elle manque ou est illisible.
        /// Ne lève jamais d'exception.
        /// </summary>
        public static Bitmap Load(int artworkId)
        {
            string path = PathFor(artworkId);
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

        /// <summary><see cref="Load"/> sur une tâche de fond.</summary>
        public static Task<Bitmap> LoadAsync(int artworkId) => Task.Run(() => Load(artworkId));
    }
}
