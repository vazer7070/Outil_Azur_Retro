using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Outil_Azur_complet.Bot;

// Complète BotClientSkinSmoke pour les familles d'images exportées du client (Resources/Bot/<Famille>) :
// livraison et provenance de chaque famille, accès par famille de ClientAssets (absence, noms invalides,
// PNG en palette avec transparence, cache borné, lecture hors du thread appelant), recoloration comme
// Color.setRGB, emblèmes de guilde et cœur des points de vie composés au pixel près.
internal static class BotClientIconsSmoke
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static Type assets;

    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
            string file = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(e.Name).Name + ".dll");
            if (!File.Exists(file)) file = Path.ChangeExtension(file, "exe"); return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        };
        try { Run(); Console.WriteLine("OK: exported client families are shipped, documented and loaded safely by ClientAssets"); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static object Call(string name, params object[] args)
    {
        var method = assets.GetMethods(Any).First(m => m.Name == name && m.GetParameters().Length == args.Length &&
            m.GetParameters().Select(p => p.ParameterType).SequenceEqual(args.Select(a => a == null ? typeof(string) : a.GetType()), new Loose()));
        return method.Invoke(null, args);
    }
    private sealed class Loose : IEqualityComparer<Type>
    {
        public bool Equals(Type parameter, Type argument) { return parameter == argument || parameter.IsAssignableFrom(argument) || (argument == typeof(string) && !parameter.IsValueType); }
        public int GetHashCode(Type type) { return 0; }
    }
    private static Bitmap Get(string family, string name) { return (Bitmap)Call("Get", family, name); }
    private static string Root { set { assets.GetProperty("Root", Any).SetValue(null, value, null); } get { return (string)assets.GetProperty("Root", Any).GetValue(null, null); } }
    private static bool Cached(string family, string name)
    {
        var args = new object[] { family, name, null };
        return (bool)assets.GetMethod("TryCached", Any).Invoke(null, args);
    }

    private static void Run()
    {
        assets = typeof(LoginForm).Assembly.GetType("Outil_Azur_complet.Bot.ClientAssets");
        Check(assets != null, "ClientAssets type is missing");

        // 1. Provenance : chaque famille versionnée a son PROVENANCE.md qui nomme le SWF source et l'outil.
        string source = Path.GetFullPath(Path.Combine(TestPaths.ApplicationBin, "..", "..", "Resources", "Bot"));
        Check(Directory.Exists(source), "Resources/Bot not found next to bin/Debug: " + source);
        var families = new Dictionary<string, string> {
            { "Items", "clips/items/" }, { "Portraits", "clips/artworks/big/" }, { "Smileys", "clips/smileys/" }, { "Emotes", "clips/emotes/" },
            { "Jobs", "clips/jobs/" }, { "Alignments", "clips/alignments/" }, { "Emblems", "clips/emblems/back/" }, { "WorldMap", "clips/maps/" },
            { "sorts", "clips/spells/icons/" }, { "Client", "modules/core.swf" } };
        foreach (var family in families) {
            string file = Path.Combine(source, family.Key, "PROVENANCE.md");
            Check(File.Exists(file), "Missing provenance: " + file);
            string text = File.ReadAllText(file);
            Check(text.Contains(family.Value), family.Key + "/PROVENANCE.md does not name its source SWF " + family.Value);
            Check(text.Contains("exporter_icons.py"), family.Key + "/PROVENANCE.md does not name the export tool");
            Check(Directory.GetFiles(Path.Combine(source, family.Key), "*.png", SearchOption.AllDirectories).Length > 0, "No PNG in " + family.Key);
        }
        Check(Enumerable.Range(1, 17).All(n => File.Exists(Path.Combine(source, "Emblems", "back", n + ".png")) && File.Exists(Path.Combine(source, "Emblems", "back", n + "_contour.png"))),
            "Emblem backs 1..17 (layer and contour) are not all exported");
        Check(Enumerable.Range(1, 104).All(n => File.Exists(Path.Combine(source, "Emblems", "up", n + ".png"))), "Emblem motifs 1..104 are not all exported");

        // 2. Livraison : la compilation copie les familles (et les .tsv de la carte) sous ressources/Bot.
        string shipped = Path.Combine(TestPaths.ApplicationBin, "ressources", "Bot");
        foreach (string folder in new[] { "Items", "Portraits", "Smileys", "Emotes", "Jobs", "Alignments", "Emblems", "WorldMap", "sorts", Path.Combine("UI", "Client") })
            Check(Directory.Exists(Path.Combine(shipped, folder)) && Directory.GetFiles(Path.Combine(shipped, folder), "*.png", SearchOption.AllDirectories).Length > 0, "Family not shipped: " + folder);
        Check(File.Exists(Path.Combine(shipped, "WorldMap", "0", "tuiles.tsv")), "World map tile offsets are not shipped");
        Check(!File.Exists(Path.Combine(shipped, "1.png")) && !File.Exists(Path.Combine(shipped, "UI", "agi.png")), "Legacy Resources/Bot PNG are now copied");
        Check(Path.GetFullPath(Root) == Path.GetFullPath(shipped), "Default root is not ressources/Bot next to the executable: " + Root);

        // 3. Données livrées : PNG en palette avec transparence, icône d'objet, sort ajouté, emblème réel.
        var item = (Bitmap)Call("ItemIcon", 1, 107);
        Check(item != null && item.Width > 40, "Shipped item icon 1/107 not loaded");
        Check(Alpha(item, 0) > 0 && Alpha(item, 255) > 0, "Palette PNG lost its transparency (tRNS) when loaded");
        Check(ReferenceEquals(item, Get("Items", "1/107")), "ItemIcon does not share the family cache");
        Check(Get("Spells", "202") != null && Get("Client", "Heart_vide") != null && Get("WorldMap", "0/-1_-1") != null, "Spell, UI layer or world map tile not loaded");
        using (var real = (Bitmap)Call("Emblem", "1,0,1,0", 40, false)) Check(real != null && real.Width == 40 && Alpha(real, 255) > 0, "Shipped emblem not composed");

        // 4. Dossier vide : null sans exception, absence mise en cache ; noms invalides refusés sans lecture.
        string work = Path.Combine(TestPaths.Work, "bot-client-icons-" + Guid.NewGuid().ToString("N"));
        string empty = Path.Combine(work, "vide"); Directory.CreateDirectory(empty);
        var reads = new List<KeyValuePair<string, int>>();
        Action<string, int> observer = (key, thread) => { lock (reads) reads.Add(new KeyValuePair<string, int>(key, thread)); };
        var readEvent = assets.GetEvent("AssetFileRead", Any);
        readEvent.GetAddMethod(true).Invoke(null, new object[] { observer });
        try {
            Root = empty;
            Check(Get("Smileys", "1") == null && Get("Items", "1/1") == null && Get("Portraits", "10") == null && Call("ItemIcon", 1, 1) == null, "Empty root returned an image");
            Check(Cached("Smileys", "1") && Cached("Items", "1/1"), "Absence is not cached");
            using (var none = (Bitmap)Call("Emblem", "1,0,1,0", 40, false)) Check(none == null, "Emblem composed without images");
            Check(Call("Heart", 0.5) == null, "Heart composed without images");
            int before = reads.Count;
            foreach (string bad in new[] { null, "", "..", "../x", "a/../../b", "/etc/passwd", "C:\\Windows\\x", "x.png", "a\0b", "a//b", "a b", new string('a', 300) })
                Check(Get("Items", bad) == null, "Invalid name accepted: " + bad);
            foreach (string badFamily in new[] { null, "", "..", "Items/..", "-Items", "UI/Client" })
                Check(Get(badFamily, "1") == null, "Invalid family accepted: " + badFamily);
            Check(reads.Count == before, "An invalid name reached the disk");
            Check(Call("ItemIcon", -1, 3) == null, "Negative item type accepted");

            // 5. PNG synthétiques : palette avec tRNS, RGBA, fichiers illisibles ; cache et changement de racine.
            string root = Path.Combine(work, "racine");
            Directory.CreateDirectory(Path.Combine(root, "Items", "7"));
            File.WriteAllBytes(Path.Combine(root, "Items", "7", "42.png"), PalettePng(6, 4,
                new[] { Color.FromArgb(0, 0, 0, 0), Color.FromArgb(255, 200, 40, 10), Color.FromArgb(128, 10, 20, 250) }, (x, y) => x < 2 ? 0 : x < 4 ? 1 : 2));
            File.WriteAllBytes(Path.Combine(root, "Items", "7", "43.png"), new byte[] { 137, 80, 78, 71, 1, 2, 3 });
            File.WriteAllBytes(Path.Combine(root, "Items", "7", "44.png"), PalettePng(6, 4, new[] { Color.Red }, (x, y) => 0).Take(40).ToArray());
            Root = root;
            Check(!Cached("Items", "1/1"), "Changing Root did not clear the cache");
            before = reads.Count;
            var icon = Get("Items", "7/42");
            Check(icon != null && icon.Width == 6 && icon.Height == 4, "Synthetic palette PNG not loaded");
            Check(icon.GetPixel(0, 0).A == 0, "Transparent palette entry is opaque");
            Check(icon.GetPixel(2, 1).ToArgb() == Color.FromArgb(255, 200, 40, 10).ToArgb(), "Opaque palette colour changed: " + icon.GetPixel(2, 1));
            var half = icon.GetPixel(5, 3);
            Check(Math.Abs(half.A - 128) <= 1 && Math.Abs(half.B - 250) <= 2, "Half-transparent palette entry changed: " + half);
            Check(ReferenceEquals(icon, Get("Items", "7/42")) && ReferenceEquals(icon, Call("ItemIcon", 7, 42)), "Second access did not hit the cache");
            Check(reads.Count == before + 1 && reads[before].Key == "Items/7/42", "Cached image read again from disk (" + (reads.Count - before) + " reads)");
            Check(Get("Items", "7/43") == null && Get("Items", "7/44") == null, "Malformed PNG did not return null");
            // Cas de la fiche : un smiley copié dans la racine temporaire est rendu puis servi par le cache.
            Directory.CreateDirectory(Path.Combine(root, "Smileys"));
            File.WriteAllBytes(Path.Combine(root, "Smileys", "1.png"), PalettePng(4, 4, new[] { Color.FromArgb(0, 0, 0, 0), Color.Gold }, (x, y) => x == y ? 1 : 0));
            before = reads.Count;
            var smiley = Get("Smileys", "1");
            Check(smiley != null && smiley.Width == 4 && smiley.GetPixel(2, 2).ToArgb() == Color.Gold.ToArgb() && smiley.GetPixel(3, 0).A == 0, "Synthetic smiley not loaded");
            Check(ReferenceEquals(smiley, Get("Smileys", "1")) && reads.Count == before + 1, "Smiley not served by the cache");

            // 6. Lecture asynchrone : jamais sur le thread appelant, image en cache rendue sans attendre.
            using (var rgba = new Bitmap(3, 3, PixelFormat.Format32bppArgb)) {
                rgba.SetPixel(1, 1, Color.FromArgb(255, 1, 2, 3));
                Directory.CreateDirectory(Path.Combine(root, "Portraits")); rgba.Save(Path.Combine(root, "Portraits", "99.png"), ImageFormat.Png);
            }
            before = reads.Count;
            var task = (Task<Bitmap>)Call("GetAsync", "Portraits", "99");
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted && waited.ElapsedMilliseconds < 20000) Thread.Sleep(5);
            Check(task.IsCompleted && task.Result != null && task.Result.GetPixel(1, 1).ToArgb() == Color.FromArgb(255, 1, 2, 3).ToArgb(), "GetAsync did not load the RGBA PNG");
            Check(reads.Count == before + 1 && reads[before].Value != Thread.CurrentThread.ManagedThreadId, "GetAsync read the PNG on the calling thread");
            var again = (Task<Bitmap>)Call("GetAsync", "Portraits", "99");
            Check(again.IsCompleted && ReferenceEquals(again.Result, task.Result), "Cached image not returned synchronously by GetAsync");
            Check(((Task<Bitmap>)Call("GetAsync", "Portraits", "../99")).Result == null, "GetAsync accepted an invalid name");

            // 7. Cache borné : les entrées les plus anciennes sont évincées (sans libérer les images rendues).
            int limit = (int)assets.GetField("AssetCacheEntries", Any).GetValue(null);
            for (int i = 0; i <= limit; i++) Get("Jobs", "absent" + i);
            Check(!Cached("Jobs", "absent0") && Cached("Jobs", "absent" + limit), "Cache is not bounded to " + limit + " entries");
            Check(icon.GetPixel(2, 1).A == 255, "Evicted image was disposed under its holder");

            // 8. Recoloration comme Color.setRGB : couleur remplacée, alpha gardé ; -1 rend l'image transparente.
            using (var tinted = (Bitmap)Call("Tinted", icon, 0x123456)) {
                Check(tinted != null && !ReferenceEquals(tinted, icon), "Tinted did not return a copy");
                Check(tinted.GetPixel(0, 0).A == 0 && tinted.GetPixel(2, 1).ToArgb() == Color.FromArgb(255, 0x12, 0x34, 0x56).ToArgb(), "Tinted colour wrong: " + tinted.GetPixel(2, 1));
                // libgdiplus (Mono) garde les pixels en alpha prémultiplié : une couleur à demi transparente varie de ±1.
                Check(Math.Abs(tinted.GetPixel(5, 3).A - 128) <= 1 && Math.Abs(tinted.GetPixel(5, 3).G - 0x34) <= 2, "Tinted lost the alpha channel: " + tinted.GetPixel(5, 3));
            }
            using (var hidden = (Bitmap)Call("Tinted", icon, -1)) Check(Alpha(hidden, 0) == hidden.Width * hidden.Height, "Colour -1 did not hide the image");
            Check(icon.GetPixel(2, 1).ToArgb() == Color.FromArgb(255, 200, 40, 10).ToArgb(), "Tinted modified the cached image");

            // 9. Emblème : chaîne en base 36 lue comme createGuildEmblem, composition au pixel témoin.
            CheckEmblem("3,a,5,z", true, 3, 10, 5, 35);
            CheckEmblem("0,0,0,0", true, 1, 0, 1, 0);
            CheckEmblem("i,0,2w,0", true, 1, 0, 104, 0);
            CheckEmblem("2x,0,2x,0", true, 1, 0, 1, 0);
            CheckEmblem("zz!,1g,-3,  5", true, 1, 52, 1, 5);
            CheckEmblem("!,!,!,!", true, 1, 0, 1, 0);
            CheckEmblem("a,b", false, 1, 0, 1, 0);
            CheckEmblem(null, false, 1, 0, 1, 0);
            Directory.CreateDirectory(Path.Combine(root, "Emblems", "back")); Directory.CreateDirectory(Path.Combine(root, "Emblems", "up"));
            File.WriteAllBytes(Path.Combine(root, "Emblems", "back", "3.png"), PalettePng(20, 20, new[] { Color.White }, (x, y) => 0));
            File.WriteAllBytes(Path.Combine(root, "Emblems", "back", "3_contour.png"), PalettePng(20, 20, new[] { Color.FromArgb(0, 0, 0, 0), Color.Black }, (x, y) => y == 19 ? 1 : 0));
            File.WriteAllBytes(Path.Combine(root, "Emblems", "up", "5.png"), PalettePng(10, 10, new[] { Color.Gray }, (x, y) => 0));
            string red = ToBase36(0xFF0000), blue = ToBase36(0x0000FF);
            using (var emblem = (Bitmap)Call("Emblem", "3," + red + ",5," + blue, 80, false)) {
                Check(emblem != null && emblem.Width == 80 && emblem.Height == 80, "Synthetic emblem not composed");
                Check(IsColour(emblem.GetPixel(6, 30), 255, 0, 0), "Emblem back not tinted red: " + emblem.GetPixel(6, 30));
                Check(IsColour(emblem.GetPixel(40, 40), 0, 0, 255), "Emblem motif not tinted blue at its 50 x 50 box: " + emblem.GetPixel(40, 40));
                Check(IsColour(emblem.GetPixel(40, 77), 0, 0, 0), "Emblem contour not drawn untinted: " + emblem.GetPixel(40, 77));
                Check(emblem.GetPixel(0, 40).A == 0 && emblem.GetPixel(79, 40).A == 0, "Emblem back leaks outside its 78 x 78 box without shadow");
            }
            using (var shadowed = (Bitmap)Call("Emblem", 3, 0xFF0000, 5, 0x0000FF, 80, true)) {
                var edge = shadowed.GetPixel(0, 40);
                Check(edge.A > 0 && edge.R > 200 && edge.G > 200 && edge.B > 200, "Emblem shadow is not white around the back: " + edge);
                Check(IsColour(shadowed.GetPixel(40, 40), 0, 0, 255), "Shadow covers the motif");
            }
            Check(Call("Emblem", 3, 0, 5, 0, 0, false) == null && Call("Emblem", 3, 0, 5, 0, 5000, false) == null, "Emblem accepted an invalid size");

            // 10. Cœur des points de vie : Heart_vide puis le bas de Heart selon le ratio (rectangle de y = 4 à 76).
            Directory.CreateDirectory(Path.Combine(root, "UI", "Client"));
            File.WriteAllBytes(Path.Combine(root, "UI", "Client", "Heart.png"), PalettePng(88, 80, new[] { Color.Red }, (x, y) => 0));
            File.WriteAllBytes(Path.Combine(root, "UI", "Client", "Heart_vide.png"), PalettePng(88, 80, new[] { Color.Yellow }, (x, y) => 0));
            using (var heart = (Bitmap)Call("Heart", 0.5)) {
                Check(heart != null && heart.Width == 88, "Heart not composed");
                Check(IsColour(heart.GetPixel(44, 70), 255, 0, 0) && IsColour(heart.GetPixel(44, 20), 255, 255, 0) && IsColour(heart.GetPixel(44, 78), 255, 255, 0),
                    "Heart at 50% does not fill the lower half of the rectangle");
            }
            using (var heart = (Bitmap)Call("Heart", double.NaN)) Check(heart != null && IsColour(heart.GetPixel(44, 70), 255, 255, 0), "Heart NaN is not empty");
            using (var heart = (Bitmap)Call("Heart", 7.0)) Check(heart != null && IsColour(heart.GetPixel(44, 6), 255, 0, 0), "Heart ratio is not clamped to 1");
        }
        finally {
            readEvent.GetRemoveMethod(true).Invoke(null, new object[] { observer });
            Root = null;
            try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void CheckEmblem(string value, bool ok, int back, int backColor, int up, int upColor)
    {
        var args = new object[] { value, 0, 0, 0, 0 };
        bool parsed = (bool)assets.GetMethod("TryParseEmblem", Any).Invoke(null, args);
        Check(parsed == ok && (int)args[1] == back && (int)args[2] == backColor && (int)args[3] == up && (int)args[4] == upColor,
            "Emblem string '" + value + "' parsed as " + parsed + " " + args[1] + "," + args[2] + "," + args[3] + "," + args[4]);
    }

    private static int Alpha(Bitmap image, int alpha)
    {
        int count = 0; for (int y = 0; y < image.Height; y++) for (int x = 0; x < image.Width; x++) if (image.GetPixel(x, y).A == alpha) count++; return count;
    }

    private static bool IsColour(Color c, int r, int g, int b) { return c.A > 200 && Math.Abs(c.R - r) < 40 && Math.Abs(c.G - g) < 40 && Math.Abs(c.B - b) < 40; }

    private static string ToBase36(int value)
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz"; string text = "";
        do { text = digits[value % 36] + text; value /= 36; } while (value > 0); return text;
    }

    // PNG en palette (type 3, 8 bits) avec un bloc tRNS, comme ceux qu'écrit exporter_icons.py.
    private static byte[] PalettePng(int width, int height, Color[] palette, Func<int, int, int> index)
    {
        var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
        Chunk(output, "IHDR", Concat(BigEndian(width), BigEndian(height), new byte[] { 8, 3, 0, 0, 0 }));
        Chunk(output, "PLTE", palette.SelectMany(c => new[] { c.R, c.G, c.B }).ToArray());
        Chunk(output, "tRNS", palette.Select(c => c.A).ToArray());
        var raw = new MemoryStream();
        for (int y = 0; y < height; y++) { raw.WriteByte(0); for (int x = 0; x < width; x++) raw.WriteByte((byte)index(x, y)); }
        Chunk(output, "IDAT", Zlib(raw.ToArray()));
        Chunk(output, "IEND", new byte[0]);
        return output.ToArray();
    }

    private static byte[] Zlib(byte[] data)
    {
        var output = new MemoryStream(); output.WriteByte(0x78); output.WriteByte(0x9C);
        using (var deflate = new DeflateStream(output, CompressionMode.Compress, true)) deflate.Write(data, 0, data.Length);
        uint a = 1, b = 0; foreach (byte value in data) { a = (a + value) % 65521; b = (b + a) % 65521; }
        output.Write(BigEndian((int)((b << 16) | a)), 0, 4);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        byte[] name = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(BigEndian(data.Length), 0, 4); output.Write(name, 0, 4); output.Write(data, 0, data.Length);
        output.Write(BigEndian((int)Crc(Concat(name, data))), 0, 4);
    }

    private static uint Crc(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data) { crc ^= value; for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1; }
        return crc ^ 0xFFFFFFFF;
    }

    private static byte[] BigEndian(int value) { return new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value }; }
    private static byte[] Concat(params byte[][] parts) { return parts.SelectMany(p => p).ToArray(); }
}
