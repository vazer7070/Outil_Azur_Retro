using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Tool_BotProtocol.Game.Maps;

/// <summary>
/// Carte affichée par les captures. Avec <c>AZUR_CAPTURE_SQL</c> (export SQL du serveur de jeu, table <c>maps</c>), la carte
/// <c>AZUR_CAPTURE_MAP</c> (7411 par défaut) est lue au moment de la capture : dimensions, cellules et coordonnées ; avec
/// <c>AZUR_CAPTURE_CLIENT</c> (dossier du client 1.34), son fond est lu dans <c>data/maps/&lt;id&gt;_*.swf</c>. Rien n'est
/// écrit dans le dépôt. Sans export SQL, une prairie synthétique est composée avec les décors versionnés (sols choisis par
/// leur couleur dominante), ou, à défaut de décors, une carte de cellules nues comme dans les tests.
/// </summary>
internal static class CaptureMap
{
    internal const int SyntheticId = 900500;
    internal static string Source { get; private set; } = "?";

    /// <summary>Enregistre la carte dans <see cref="Map.AllBotMaps"/> et renvoie son identifiant.</summary>
    internal static int Prepare()
    {
        string sql = Environment.GetEnvironmentVariable("AZUR_CAPTURE_SQL");
        int wanted = int.TryParse(Environment.GetEnvironmentVariable("AZUR_CAPTURE_MAP"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 7411;
        if (!string.IsNullOrEmpty(sql) && File.Exists(sql))
        {
            Map map = FromSql(sql, wanted);
            if (map != null)
            {
                map.Back_ID = BackgroundFromClient(wanted);
                Map.AllBotMaps[map.MapID] = map;
                Source = "carte " + map.MapID + " lue dans l'export SQL" + (map.Back_ID > 0 ? ", fond " + map.Back_ID + " lu dans le client" : "");
                return map.MapID;
            }
            BotCaptures.Note("carte " + wanted + " absente de " + Path.GetFileName(sql) + " : prairie synthétique");
        }
        Map meadow = Meadow();
        Map.AllBotMaps[meadow.MapID] = meadow;
        return meadow.MapID;
    }

    // INSERT INTO `maps` VALUES ('id', 'date', 'width', 'heigth', 'places', 'key', 'mapData', 'monsters', 'capabilities', 'mappos', …)
    private static Map FromSql(string path, int id)
    {
        string prefix = "INSERT INTO `maps` VALUES ('" + id.ToString(CultureInfo.InvariantCulture) + "'";
        foreach (string line in File.ReadLines(path, Encoding.UTF8))
        {
            if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
            string[] values = Regex.Matches(line, @"'((?:[^'\\]|\\.)*)'").Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
            if (values.Length < 10) return null;
            string[] position = values[9].Split(',');
            var map = new Map
            {
                MapID = id, MapWidth = byte.Parse(values[2], CultureInfo.InvariantCulture), MapHeight = byte.Parse(values[3], CultureInfo.InvariantCulture),
                MapData = values[6],
                X = position.Length > 1 ? int.Parse(position[0], CultureInfo.InvariantCulture) : 0,
                Y = position.Length > 1 ? int.Parse(position[1], CultureInfo.InvariantCulture) : 0,
            };
            if (string.IsNullOrEmpty(map.MapData) || map.MapData.Length % 10 != 0) return null;
            return map;
        }
        return null;
    }

    private static int BackgroundFromClient(int id)
    {
        string client = Environment.GetEnvironmentVariable("AZUR_CAPTURE_CLIENT");
        if (string.IsNullOrEmpty(client)) return 0;
        string folder = Path.Combine(client, "data", "maps");
        if (!Directory.Exists(folder)) return 0;
        string file = Directory.EnumerateFiles(folder, id.ToString(CultureInfo.InvariantCulture) + "_*.swf").FirstOrDefault();
        if (file == null) return 0;
        try { return Tool_Editor.maps.managers.MapSwfSerializer.Load(file).BackGroundID; }
        catch (Exception error) when (error is IOException || error is InvalidDataException || error is FormatException || error is EndOfStreamException)
        { BotCaptures.Note("fond de la carte illisible dans " + Path.GetFileName(file) + " : " + error.Message); return 0; }
    }

    // ------------------------------------------------------------------------------------------------ prairie synthétique

    private const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";

    /// <summary>Dix caractères d'une cellule au format du client (voir Tool_Editor BuilderClass.GetCellData).</summary>
    internal static string Cell(int ground, int object1 = 0, int object2 = 0, int movement = 4, bool flip2 = false)
    {
        int[] v = new int[10];
        v[0] = 32 | 1 | ((ground & 0x600) >> 6) | ((object1 & 0x2000) >> 11) | ((object2 & 0x2000) >> 12);
        v[1] = 7;
        v[2] = (movement & 7) << 3 | ((ground >> 6) & 7);
        v[3] = ground & 0x3f;
        v[4] = (1 << 2) | ((object1 >> 12) & 1);
        v[5] = (object1 >> 6) & 0x3f;
        v[6] = object1 & 0x3f;
        v[7] = (flip2 ? 8 : 0) | ((object2 >> 12) & 1);
        v[8] = (object2 >> 6) & 0x3f;
        v[9] = object2 & 0x3f;
        return new string(v.Select(i => Alphabet[i]).ToArray());
    }

    private static Map Meadow()
    {
        string decor = Path.Combine(BotCaptures.Bin, "ressources", "maps", "sols");
        int grass = Directory.Exists(decor) ? GreenestGround(decor) : 0;
        var cells = new StringBuilder();
        const int width = 15, height = 17, count = width * 2 * height - width - height + 1; // 479 cellules
        for (int i = 0; i < count; i++) cells.Append(Cell(grass));
        Source = grass > 0 ? "prairie synthétique (sol " + grass + " des décors versionnés)" : "carte de cellules nues (décors absents)";
        return new Map { MapID = SyntheticId, MapWidth = width, MapHeight = height, X = 4, Y = -18, MapData = cells.ToString() };
    }

    /// <summary>Sol dont les pixels opaques sont les plus verts (herbe), parmi les PNG nommés par leur identifiant.</summary>
    private static int GreenestGround(string folder)
    {
        int best = 0; double score = double.MinValue;
        foreach (string file in Directory.EnumerateFiles(folder, "*.png").OrderBy(f => f, StringComparer.Ordinal).Take(400))
        {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(file), NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id <= 0 || id > 2047) continue;
            try
            {
                using (var image = new System.Drawing.Bitmap(file))
                {
                    if (image.Width < 40 || image.Height < 20) continue;
                    double r = 0, g = 0, b = 0; int n = 0;
                    for (int y = 0; y < image.Height; y += 3)
                        for (int x = 0; x < image.Width; x += 3)
                        {
                            var c = image.GetPixel(x, y); if (c.A < 200) continue;
                            r += c.R; g += c.G; b += c.B; n++;
                        }
                    if (n < 50) continue;
                    double s = (g - Math.Max(r, b)) / n;
                    if (s > score) { score = s; best = id; }
                }
            }
            catch (ArgumentException) { }
        }
        return best;
    }
}
