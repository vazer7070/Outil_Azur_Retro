using System;
using System.IO;
using System.Linq;
using Tool_Editor.maps.data;
using Tool_Editor.maps.managers;
using Tools_protocol.Parser.XML;

namespace Outil_Azur_complet.Parser
{
    public static class ResourceMapConversion
    {
        public static string Decrypt(string data, string key, int width, int height)
        {
            ValidateDimensions(width, height);
            string prepared = DecryptClass.PrepareKey((key ?? "").Trim());
            string clear = DecryptClass.DecypherData((data ?? "").Trim(), prepared,
                Convert.ToInt32(DecryptClass.CheckSum(prepared), 16) * 2);
            ValidateData(clear, width, height);
            return clear;
        }

        // Work on a separate map so a failed conversion never changes the import.
        public static Map Prepare(Map original, int id, int width, int height, string data, string key)
        {
            ValidateDimensions(width, height);
            if (id <= 0) throw new FormatException("L'identifiant de carte doit être positif.");
            data = (data ?? "").Trim(); key = (key ?? "").Trim();
            if (key.Length != 0) data = Decrypt(data, key, width, height);
            ValidateData(data, width, height);
            var map = new Map { ID = id, Width = width, Height = height, MapData = data, Key = "" };
            if (original != null)
            {
                map.DateMap = original.DateMap; map.BackGroundID = original.BackGroundID;
                map.Musique = original.Musique; map.MusiqueName = original.MusiqueName;
                map.Ambiance = original.Ambiance; map.IsOutDoor = original.IsOutDoor;
                map.Capabilities = original.Capabilities; map.NbGroups = original.NbGroups;
                map.GroupMaxSize = original.GroupMaxSize; map.X = original.X; map.Y = original.Y;
                map.Area = original.Area; map.SubArea = original.SubArea; map.SuperArea = original.SuperArea;
                map.NextRoom = original.NextRoom; map.NextCell = original.NextCell;
                map.Mobs = original.Mobs; map.GroupFixe_Mobs = original.GroupFixe_Mobs;
                map.Groupefixe_Cell = original.Groupefixe_Cell;
                if (width != original.Width || height != original.Height)
                    throw new FormatException("Pour redimensionner une carte importée, utilisez l'éditeur de cartes. Le convertisseur conserve ses dimensions et ses placements.");
                map.fightPlaces = original.HasProjectCells ? "" : original.fightPlaces;
            }
            map.Load();
            if (original != null && original.HasProjectCells && original.Cells != null && original.Cells.Length == map.Cells.Length)
                for (int index = 0; index < map.Cells.Length; index++)
                {
                    var source = original.Cells[index]; if (source == null) continue;
                    map.Cells[index].FightCell = source.FightCell;
                    map.Cells[index].Trigger = source.Trigger;
                    map.Cells[index].TriggerName = source.TriggerName;
                }
            map.HasProjectCells = true;
            if (original != null && original.HasProjectCells) map.fightPlaces = original.fightPlaces;
            return map;
        }

        /// <summary>
        /// Dossier des cartes du client : <c>data/maps</c> d'une installation du client, un dossier <c>maps</c>
        /// (racine du « dataserver »), ou le dossier indiqué lui-même. Null si rien n'existe.
        /// </summary>
        public static string ClientMapsDirectory(string clientDirectory)
        {
            if (string.IsNullOrWhiteSpace(clientDirectory)) return null;
            string root;
            try { root = Path.GetFullPath(clientDirectory.Trim()); }
            catch (Exception error) when (error is ArgumentException || error is NotSupportedException || error is PathTooLongException) { return null; }
            foreach (string candidate in new[] { Path.Combine(root, "data", "maps"), Path.Combine(root, "maps"), root })
                if (Directory.Exists(candidate)) return candidate;
            return null;
        }

        /// <summary>
        /// Fond (<c>backgroundNum</c>) d'une carte lu dans le SWF que le client 1.34 charge après <c>GDM|id|date|clé</c> :
        /// <c>&lt;id&gt;_&lt;date&gt;X.swf</c> quand la carte a une clé (cellules chiffrées), sinon <c>&lt;id&gt;_&lt;date&gt;.swf</c>.
        /// Les cellules du fichier sont déchiffrées avec la clé de la base et comparées aux cellules de la base : un écart
        /// est signalé sans écarter le fond, qui reste celui que le client affiche. Fichier absent, illisible ou d'une
        /// autre carte : 0 et avertissement dans <see cref="MapBackgroundRequest.Warning"/>. Le SWF n'est jamais exécuté.
        /// </summary>
        public static int ReadClientBackground(string mapsDirectory, MapBackgroundRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            request.Warning = null;
            if (string.IsNullOrEmpty(mapsDirectory) || !Directory.Exists(mapsDirectory))
            {
                request.Warning = "dossier des cartes du client introuvable.";
                return 0;
            }
            string date = request.Date.Trim(), key = request.Key.Trim();
            if (date.Length == 0 || date.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || date.Contains(".."))
            {
                request.Warning = "date « " + date + " » inutilisable pour retrouver le fichier du client.";
                return 0;
            }
            string name = request.MapId + "_" + date + (key.Length != 0 ? "X" : "") + ".swf";
            string path = Path.Combine(mapsDirectory, name);
            if (!File.Exists(path))
            {
                request.Warning = "fichier " + name + " absent du dossier du client.";
                return 0;
            }
            Map client;
            try { client = MapSwfSerializer.Load(path); }
            catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException)
            {
                request.Warning = name + " illisible : " + error.Message;
                return 0;
            }
            if (client.ID != request.MapId)
            {
                request.Warning = name + " décrit la carte " + client.ID + ".";
                return 0;
            }
            try
            {
                string cells = key.Length != 0 ? Decrypt(client.MapData, key, client.Width, client.Height) : client.MapData;
                if (client.Width != request.Width || client.Height != request.Height || !string.Equals(cells, request.MapData.Trim(), StringComparison.Ordinal))
                    request.Warning = name + " ne contient pas les mêmes cellules que la base ; fond du fichier conservé.";
            }
            catch (FormatException error)
            {
                request.Warning = name + " ne se déchiffre pas avec la clé de la base (" + error.Message + ") ; fond du fichier conservé.";
            }
            return client.BackGroundID;
        }

        private static void ValidateDimensions(int width, int height)
        {
            if (width < 2 || width > 100 || height < 2 || height > 100)
                throw new FormatException("Les dimensions doivent être comprises entre 2 et 100.");
        }

        private static void ValidateData(string data, int width, int height)
        {
            int expected = checked(Map.CellCount(width, height) * 10);
            if (data.Length != expected || data.Any(c => (int)DecryptClass.HashCode(c.ToString()) < 0))
                throw new FormatException("Les données doivent contenir " + expected + " caractères valides (" + Map.CellCount(width, height) + " cellules). Vérifiez la clef et les dimensions.");
        }
    }
}
