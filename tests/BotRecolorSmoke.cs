using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Outil_Azur_complet.Bot.Controls;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Utils.Crypto;

// Recoloration des sprites (lot AN6) : couleurs du GM lues comme le client (parseInt hexadécimal, -1 = zone gardée), bande D
// synthétique et masque (zone 1 = carré gauche à 100 %, puis bande à 40 %, zone 2 à droite, index de ligne illisible) :
// pixel = D + (C - d) × part / 255 exact, alpha gardé, -1 ne change rien, couleurs sans zone touchée = bande d'origine ;
// même instance de Bitmap pour deux demandes, deux vues et deux acteurs de mêmes couleurs ; masques absents, illisibles,
// de mauvaise taille ou sans ligne : bande d'origine sans exception. Données livrées : un masque par bande du gfx 10, de la
// taille de sa bande, valeurs au pas de 17 et index connus, et 10_staticR et 10_walkR recolorées pixel pour pixel par la
// formule. Carte synthétique (GM de joueurs, PNJ et groupe) dessinée par Graphics.FromImage. Passe sous Mono.
internal static class BotRecolorSmoke
{
    private const int MapWidth = 8, MapHeight = 8;
    private static readonly Color D = Color.FromArgb(255, 50, 60, 70);

    private sealed class TestView : UserMapControl
    {
        public TestView(Func<double> clock, string sprites, string overheads) : base(clock, sprites, overheads) { }
    }

    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string file = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(args.Name).Name + ".dll");
            if (!File.Exists(file)) file = Path.ChangeExtension(file, "exe");
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        };
        try
        {
            Colors();
            string root = Path.Combine(TestPaths.Work, "recolor");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            string sprites = Path.Combine(root, "sprites");
            Directory.CreateDirectory(sprites);
            Assets(sprites);
            Synthetic(sprites);
            Shipped();
            MapActors(root, sprites);
            Console.WriteLine("OK: GM colours parsed like the client, exact D + (C - d) x part / 255 with alpha kept, -1 keeps D, untouched zones "
                + "share the original strip, one Bitmap per (gfx, anim, orientation, colours) across requests, views and actors, broken masks "
                + "fall back to D, one mask per strip of gfx 10, real 10_staticR and 10_walkR recoloured pixel for pixel, map actors coloured from GM");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    // ---------------------------------------------------------------- couleurs du GM

    private static void Colors()
    {
        Check(default(ActorColors).IsNone && default(ActorColors) == ActorColors.None && ActorColors.None.Key == "-|-|-", "default(ActorColors) is not None");
        Check(ActorColors.Parse("-1", "-1", "-1") == ActorColors.None && ActorColors.Parse(null, "", "  ").IsNone, "-1 and empty colours must be None");
        ActorColors parsed = ActorColors.Parse("ff0000", "-1", "30303");
        Check(parsed.Color1 == 0xFF0000 && parsed.Color2 == -1 && parsed.Color3 == 0x030303 && parsed[1] == 0xFF0000 && parsed[2] == -1 && parsed[4] == -1
            && parsed.Key == "ff0000|-|030303", "Colours misread: " + parsed.Key);
        Check(parsed == new ActorColors(0xFF0000, -1, 0x030303) && parsed.GetHashCode() == new ActorColors(0xFF0000, -1, 0x030303).GetHashCode()
            && parsed != new ActorColors(0xFF0000, -1, -1), "ActorColors equality");
        Check(ActorColors.ParseColor(" A0b1C2 ") == 0xA0B1C2 && ActorColors.ParseColor("0") == 0 && ActorColors.ParseColor("xyz") == -1
            && ActorColors.ParseColor("123456789") == -1 && ActorColors.ParseColor("-1") == -1 && ActorColors.ParseColor(null) == -1, "ParseColor");
        ActorColors black = ActorColors.Parse("0", "-1", "-1");
        Check(!black.IsNone && black.Color1 == 0, "Black (0) is a colour, not an absent zone");
        ActorColors member = ActorColors.ParseList("1,2");
        Check(member.Color1 == 1 && member.Color2 == 2 && member.Color3 == -1 && ActorColors.ParseList(null).IsNone && ActorColors.ParseList(",,").IsNone
            && ActorColors.ParseList("-1,-1,-1").IsNone, "Group member colours (c1,c2,c3) misread");
        Check(ActorColors.Parse(new[] { "ff", "-1" }).Color1 == 0xFF && ActorColors.Parse((IReadOnlyList<string>)null).IsNone, "Snapshot colours misread");
    }

    // ---------------------------------------------------------------- données synthétiques

    /// <summary>
    /// Bande D 30×12 (50, 60, 70) opaque, dernière ligne à demi transparente, un pixel transparent ; masque : x &lt; 10 zone 1
    /// à 255, 10 ≤ x &lt; 20 zone 1 à 102, à droite index 3 (ligne illisible) en haut et zone 2 en bas. Gfx 501 à 505 : masque
    /// trop petit, masque illisible, masque sans ligne, lignes sans masque. Gfx 510 (24×32, moitié gauche en zone 1) pour la carte.
    /// </summary>
    private static void Assets(string sprites)
    {
        foreach (int gfx in new[] { 500, 501, 502, 504, 505 }) Band(Path.Combine(sprites, gfx + "_staticR.png"), 30, 12, true);
        Mask(Path.Combine(sprites, "500_staticR.couleurs.png"), 30, 12, (x, y) =>
            x < 10 ? Color.FromArgb(255, 255, 1, 0) : x < 20 ? Color.FromArgb(255, 102, 1, 0) : y < 6 ? Color.FromArgb(255, 255, 3, 0) : Color.FromArgb(255, 255, 2, 0));
        Mask(Path.Combine(sprites, "501_staticR.couleurs.png"), 10, 10, (x, y) => Color.FromArgb(255, 255, 1, 0));
        File.WriteAllBytes(Path.Combine(sprites, "502_staticR.couleurs.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 });
        Mask(Path.Combine(sprites, "504_staticR.couleurs.png"), 30, 12, (x, y) => Color.FromArgb(255, 255, 1, 0));
        Band(Path.Combine(sprites, "510_staticR.png"), 24, 32, false);
        Mask(Path.Combine(sprites, "510_staticR.couleurs.png"), 24, 32, (x, y) => x < 12 ? Color.FromArgb(255, 255, 1, 0) : Color.Black);
        File.WriteAllLines(Path.Combine(sprites, "ancres.tsv"), new[] { "gfx\tanim\txmin\tymin\tlargeur\thauteur\timages\tips\tfin" }
            .Concat(new[] { 500, 501, 502, 504, 505 }.Select(gfx => gfx + "\tstaticR\t-15\t-12\t30\t12\t1\t40\tarret"))
            .Concat(new[] { "510\tstaticR\t-12\t-32\t24\t32\t1\t40\tarret" }));
        File.WriteAllLines(Path.Combine(sprites, "couleurs.tsv"), new[]
        {
            "gfx\tanim\tindex\tzone\tcouleur",
            "500\tstaticR\t1\t1\t0a141e", "500\tstaticR\t2\t2\t323c46",
            "500\tstaticR\t3\t9\t000000", "500\tstaticR\tx\t1\t000000", "500\tstaticR\t4\t1\tzzzzzz", "ligne illisible",
            "501\tstaticR\t1\t1\t000000", "502\tstaticR\t1\t1\t000000", "505\tstaticR\t1\t1\t000000",
            "510\tstaticR\t1\t1\t0a141e"
        });
    }

    private static void Band(string path, int width, int height, bool edges)
    {
        using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        {
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    bitmap.SetPixel(x, y, D);
            if (edges)
            {
                for (int x = 0; x < width; x++) bitmap.SetPixel(x, height - 1, Color.FromArgb(128, D));
                bitmap.SetPixel(5, 0, Color.FromArgb(0, 0, 0, 0));
            }
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    private static void Mask(string path, int width, int height, Func<int, int, Color> pixel)
    {
        using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        {
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    bitmap.SetPixel(x, y, pixel(x, y));
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    private static SpritePose Ready(ActorSprites library, int gfx, int direction, ActorColors colors, string animation = "static")
    {
        library.Resolve(gfx, direction, false, animation, colors);
        Check(library.WaitForPending(10000), "Strip decoding or recolouring did not finish (gfx " + gfx + ")");
        SpritePose pose = library.Resolve(gfx, direction, false, animation, colors);
        // Une bande recolorée sans effet renvoie à la bande d'origine, lue ensuite.
        if (pose.State == SpriteLoadState.Loading)
        {
            Check(library.WaitForPending(10000), "Original strip decoding did not finish (gfx " + gfx + ")");
            pose = library.Resolve(gfx, direction, false, animation, colors);
        }
        Check(pose.State == SpriteLoadState.Ready && pose.Sheet != null, "gfx " + gfx + " " + animation + " not ready: " + pose.State + " " + pose.Reason);
        return pose;
    }

    private static void Synthetic(string sprites)
    {
        var red = new ActorColors(0xC80000, -1, -1);
        using (var library = new ActorSprites(sprites, null))
        {
            // Calcul sur le pool : la première demande colorée est en lecture, jamais calculée sur le fil appelant.
            Check(library.Resolve(500, 1, false, "static", red).State == SpriteLoadState.Loading, "Recoloured strip must be computed off the calling thread");
            SpritePose original = Ready(library, 500, 1, ActorColors.None);
            SpritePose colored = Ready(library, 500, 1, red);
            Bitmap image = colored.Sheet.Image;
            Check(!ReferenceEquals(image, original.Sheet.Image), "Recolouring must work on a copy of the strip");
            Check(image.GetPixel(2, 3) == Color.FromArgb(255, 240, 40, 40) && image.GetPixel(9, 10) == Color.FromArgb(255, 240, 40, 40),
                "Zone 1 at 255: expected D + (190, -20, -30) = (240, 40, 40), got " + image.GetPixel(2, 3));
            Check(image.GetPixel(15, 4) == Color.FromArgb(255, 126, 52, 58), "Zone 1 at 102: expected (126, 52, 58), got " + image.GetPixel(15, 4));
            Check(image.GetPixel(25, 3) == D && image.GetPixel(25, 8) == D, "Pixels outside zone 1 changed: " + image.GetPixel(25, 3) + " " + image.GetPixel(25, 8));
            Check(image.GetPixel(2, 11).A == 128 && image.GetPixel(2, 11).R > 150 && image.GetPixel(5, 0).A == 0, "Alpha of D must be kept");
            Check(original.Sheet.Image.GetPixel(2, 3) == D, "The original strip was modified");
            Check(colored.Sheet.Frames == original.Sheet.Frames && colored.Sheet.XMin == original.Sheet.XMin && colored.Sheet.End == original.Sheet.End,
                "Recoloured strip lost its anchors");

            // Même instance : deux demandes, l'orientation miroir (même bande R) et une seconde vue sur le même dossier.
            Check(ReferenceEquals(library.Resolve(500, 1, false, "static", new ActorColors(0xC80000, -1, -1)).Sheet.Image, image), "Same colours, other Bitmap");
            SpritePose mirrored = library.Resolve(500, 3, false, "static", red);
            Check(mirrored.Mirrored && ReferenceEquals(mirrored.Sheet.Image, image), "Orientation 3 must reuse the recoloured R strip");
            using (var second = new ActorSprites(sprites, null))
                Check(ReferenceEquals(Ready(second, 500, 1, red).Sheet.Image, image), "Two views with the same colours must share the Bitmap");

            // Zone 2 seule (couleur d'origine = D) : x ≥ 20 en bas devient exactement la couleur ; zone 1 gardée.
            Bitmap green = Ready(library, 500, 1, new ActorColors(-1, 0x00FF00, -1)).Sheet.Image;
            Check(!ReferenceEquals(green, image) && green.GetPixel(25, 8) == Color.FromArgb(255, 0, 255, 0) && green.GetPixel(2, 3) == D && green.GetPixel(25, 3) == D,
                "Zone 2 only: expected (0, 255, 0) bottom right and D elsewhere");
            // Index 3 (ligne de zone 9 refusée) : jamais touché, même avec les trois couleurs.
            Bitmap all = Ready(library, 500, 1, new ActorColors(0xC80000, 0x00FF00, 0x0000FF)).Sheet.Image;
            Check(all.GetPixel(25, 3) == D && all.GetPixel(2, 3) == Color.FromArgb(255, 240, 40, 40), "Malformed couleurs.tsv line applied");
            // -1 partout = aucune couleur ; zone 3 seule (aucune ligne) = bande d'origine, même instance.
            Check(ReferenceEquals(Ready(library, 500, 1, ActorColors.Parse("-1", "-1", "-1")).Sheet.Image, original.Sheet.Image), "-1 must draw D");
            Check(ReferenceEquals(Ready(library, 500, 1, new ActorColors(-1, -1, 0x123456)).Sheet.Image, original.Sheet.Image),
                "Colours touching no zone must share the original strip");

            // Masques refusés : bande d'origine, sans exception.
            foreach (int gfx in new[] { 501, 502, 504, 505 })
            {
                SpritePose plain = Ready(library, gfx, 1, ActorColors.None);
                SpritePose fallback = Ready(library, gfx, 1, red);
                Check(ReferenceEquals(fallback.Sheet.Image, plain.Sheet.Image) && fallback.Sheet.Image.GetPixel(2, 3) == D,
                    "gfx " + gfx + ": a refused mask must fall back to the original strip");
            }
            SpritePose absent = library.Resolve(500, 1, false, "walk", red);
            Check(library.WaitForPending(10000), "Missing strip lookup did not finish");
            Check(library.Resolve(500, 1, false, "walk", red).State == SpriteLoadState.Missing, "A missing animation must stay missing with colours");
            Check(absent != null, "Resolve returned null");
        }
    }

    // ---------------------------------------------------------------- données livrées

    private static void Shipped()
    {
        string shipped = Path.Combine(TestPaths.ApplicationBin, "ressources", "Bot", "sprites");
        string table = Path.Combine(shipped, "couleurs.tsv");
        Check(File.Exists(table), "couleurs.tsv is not copied next to the executable: " + shipped);
        Dictionary<string, Dictionary<int, int[]>> origins = ReadTable(table);
        string[] rows = File.ReadAllLines(Path.Combine(shipped, "ancres.tsv")).Skip(1).Where(l => l.StartsWith("10\t", StringComparison.Ordinal)).ToArray();
        Check(rows.Length >= 40, "gfx 10 has too few strips: " + rows.Length);
        foreach (string row in rows)
        {
            string anim = row.Split('\t')[1];
            string band = Path.Combine(shipped, "10_" + anim + ".png"), mask = Path.Combine(shipped, "10_" + anim + ".couleurs.png");
            int bw, bh, mw, mh;
            Check(File.Exists(mask), "No colour mask for 10_" + anim);
            Check(ActorSprites.TryReadPngSize(band, out bw, out bh) && ActorSprites.TryReadPngSize(mask, out mw, out mh) && bw == mw && bh == mh,
                "Mask of 10_" + anim + " is not the size of its strip");
            Check(origins.ContainsKey("10\t" + anim), "couleurs.tsv has no line for 10_" + anim);
        }
        using (var library = new ActorSprites(shipped, null))
        {
            var colors = new ActorColors(0xFF0000, 0x00FF00, 0x0000FF);
            foreach (string anim in new[] { "static", "walk" })
            {
                SpritePose original = Ready(library, 10, 1, ActorColors.None, anim);
                SpritePose colored = Ready(library, 10, 1, colors, anim);
                Check(!ReferenceEquals(original.Sheet.Image, colored.Sheet.Image), "10_" + anim + "R was not recoloured");
                int changed = Compare(original.Sheet.Image, colored.Sheet.Image, Path.Combine(shipped, "10_" + anim + "R.couleurs.png"),
                    origins["10\t" + anim + "R"], colors, "10_" + anim + "R");
                Check(changed > 100, "10_" + anim + "R: only " + changed + " pixels recoloured");
                Console.WriteLine("10_" + anim + "R: " + changed + " pixels recoloured by the formula");
            }
        }
    }

    private static Dictionary<string, Dictionary<int, int[]>> ReadTable(string path)
    {
        var table = new Dictionary<string, Dictionary<int, int[]>>();
        string[] lines = File.ReadAllLines(path);
        Check(lines[0] == "gfx\tanim\tindex\tzone\tcouleur", "couleurs.tsv header: " + lines[0]);
        foreach (string line in lines.Skip(1))
        {
            string[] f = line.Split('\t');
            string key = f[0] + "\t" + f[1];
            if (!table.ContainsKey(key)) table[key] = new Dictionary<int, int[]>();
            table[key][int.Parse(f[2], CultureInfo.InvariantCulture)] =
                new[] { int.Parse(f[3], CultureInfo.InvariantCulture), int.Parse(f[4], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) };
        }
        return table;
    }

    /// <summary>
    /// Recalcule chaque pixel attendu d'après le masque livré (décodé à part) et compare : part au pas de 17, index connu,
    /// pixel = D + (C - origine) × part / 255 arrondi, borné, alpha de D : exact sur les pixels opaques ; sur un pixel à demi
    /// transparent, libgdiplus (Mono) garde l'ARGB 32 bits prémultiplié, d'où un écart d'au plus 255 / alpha par canal.
    /// Renvoie le nombre de pixels changés.
    /// </summary>
    private static int Compare(Bitmap original, Bitmap colored, string maskPath, Dictionary<int, int[]> rows, ActorColors colors, string name)
    {
        Check(original.Width == colored.Width && original.Height == colored.Height, name + ": size changed");
        int changed = 0, opaque = 0;
        using (var stream = new FileStream(maskPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var decoded = Image.FromStream(stream))
        using (var mask = new Bitmap(decoded))
        {
            for (int y = 0; y < original.Height; y++)
                for (int x = 0; x < original.Width; x++)
                {
                    Color m = mask.GetPixel(x, y), source = original.GetPixel(x, y), actual = colored.GetPixel(x, y);
                    Check(m.R % 17 == 0 && m.B == 0 && (m.R == 0 || rows.ContainsKey(m.G)), name + ": mask pixel (" + x + ", " + y + ") = " + m);
                    Color expected = source;
                    if (m.R > 0 && source.A > 0 && colors[rows[m.G][0]] >= 0)
                    {
                        int target = colors[rows[m.G][0]], origin = rows[m.G][1];
                        expected = Color.FromArgb(source.A, Channel(source.R, target >> 16, origin >> 16, m.R),
                            Channel(source.G, target >> 8, origin >> 8, m.R), Channel(source.B, target, origin, m.R));
                    }
                    int tolerance = actual.A == 255 ? 0 : 255 / Math.Max(1, (int)actual.A) + 1;
                    Check(actual.A == expected.A && Math.Abs(actual.R - expected.R) <= tolerance && Math.Abs(actual.G - expected.G) <= tolerance
                        && Math.Abs(actual.B - expected.B) <= tolerance, name + ": pixel (" + x + ", " + y + ") is " + actual + ", expected " + expected);
                    if (actual.A == 255) opaque++;
                    if (actual != source) changed++;
                }
        }
        Check(opaque > 100, name + ": too few opaque pixels compared exactly (" + opaque + ")");
        return changed;
    }

    private static int Channel(int value, int target, int origin, int part)
    {
        int product = ((target & 0xFF) - (origin & 0xFF)) * part;
        int delta = product >= 0 ? (product + 127) / 255 : -((-product + 127) / 255);
        return Math.Max(0, Math.Min(255, value + delta));
    }

    // ---------------------------------------------------------------- carte

    private static string EncodedCell()
    {
        // Cellule active, praticable, en ligne de vue, sans sol ni objet (aucun PNG de décor demandé).
        int[] value = { 33, 7, 32, 0, 4, 0, 0, 0, 0, 0 };
        return new string(value.Select(part => Hash.caracteres_array[part]).ToArray());
    }

    private static Bitmap PoseImage(UserMapControl.ActorVisualState state)
    {
        FieldInfo field = typeof(UserMapControl.ActorVisualState).GetField("Pose", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Check(field != null, "ActorVisualState.Pose not found");
        var pose = field.GetValue(state) as SpritePose;
        Check(pose != null && pose.Sheet != null, "Actor " + state.Id + " has no pose");
        return pose.Sheet.Image;
    }

    private static bool Near(Color pixel, Color expected) =>
        Math.Abs(pixel.R - expected.R) < 24 && Math.Abs(pixel.G - expected.G) < 24 && Math.Abs(pixel.B - expected.B) < 24;

    private static Color At(Bitmap image, RectangleF bounds, float fx, float fy) =>
        image.GetPixel((int)(bounds.Left + bounds.Width * fx), (int)(bounds.Top + bounds.Height * fy));

    /// <summary>
    /// GM d'Alice et Bruno (zone 1 en c80000), Chloé (-1), d'un PNJ (c80000) et d'un groupe (chef : zone 2 seule, membre :
    /// c80000), tous en gfx 510 : couleurs portées par chaque état, même Bitmap pour les couleurs égales, rouge dessiné à
    /// gauche des sprites colorés et D partout ailleurs.
    /// </summary>
    private static void MapActors(string root, string sprites)
    {
        string overheads = Path.Combine(root, "overheads");
        Directory.CreateDirectory(overheads);
        int count = MapHeight * (2 * MapWidth - 1) - (MapWidth - 1);
        string data = string.Concat(Enumerable.Repeat(EncodedCell(), count));
        using (var account = new Accounts(new AccountConfig("synthetic-recolor", "Synthetic123", "test")))
        {
            Map map = account.Game.Map;
            map.MapID = 991306; map.MapWidth = MapWidth; map.MapHeight = MapHeight; map.MapData = data; map.DecompressMap(data);
            GmParseResult parsed = GmParser.Parse("GM"
                + "|+16;1;0;43;Alice;1;510^100;0;0,0,0,0;c80000;-1;-1;;0;;;;;0;;0"
                + "|+20;1;0;44;Bruno;1;510^100;0;0,0,0,0;C80000;-1;-1;;0;;;;;0;;0"
                + "|+50;1;0;45;Chloe;1;510^100;0;0,0,0,0;-1;-1;-1;;0;;;;;0;;0"
                + "|+54;1;0;-3;9999;-4;510^100;0;c80000;-1;-1;;-1;0"
                + "|+95;1;45;-2;101,102;-3;510^100,510^100;5,7;-1,ff,-1;0,0,0,0;c80000,-1,-1;0,0,0,0", false, 42, -1);
            Check(parsed.Rejected.Count == 0 && parsed.Entries.Count == 5, "Synthetic GM entries were rejected: " + string.Join(" / ", parsed.Rejected));
            foreach (GmEntry entry in parsed.Entries)
            {
                entry.Actor.Cell = map.GetCellFromId((short)entry.Actor.CellId);
                map.AddActor(entry.Actor);
            }
            double clock = 1000;
            using (var view = new TestView(() => clock, sprites, overheads))
            {
                view.SetAccount(account); view.W = MapWidth; view.H = MapHeight; view.Size = new Size(640, 400);
                view.SetCellNum(); view.DrawGrille(); view.RefreshMap();
                Check(view.WaitForActorSprites(10000), "Actor PNG decoding did not finish");
                UserMapControl.ActorVisualState alice = view.GetActorVisualState(43), bruno = view.GetActorVisualState(44), chloe = view.GetActorVisualState(45);
                UserMapControl.ActorVisualState npc = view.GetActorVisualState(-3);
                UserMapControl.ActorVisualState[] group = view.GetActorVisualStates().Where(state => state.ActorId == -2).OrderBy(state => state.MemberIndex).ToArray();
                Check(alice != null && bruno != null && chloe != null && npc != null && group.Length == 2, "Synthetic actors are not all on the map");
                var red = new ActorColors(0xC80000, -1, -1);
                Check(alice.Colors == red && bruno.Colors == red && chloe.Colors.IsNone && npc.Colors == red, "GM colours not carried by the visual states");
                Check(group[0].MemberIndex < 0 && group[0].Colors == new ActorColors(-1, 0xFF, -1) && group[1].Colors == red,
                    "Group leader and member colours must come from their own GM fields");
                Check(new[] { alice, bruno, chloe, npc, group[0], group[1] }.All(state => state.HasSprite), "A coloured actor lost its sprite");
                Bitmap shared = PoseImage(alice);
                Check(ReferenceEquals(PoseImage(bruno), shared) && ReferenceEquals(PoseImage(npc), shared) && ReferenceEquals(PoseImage(group[1]), shared),
                    "Actors with the same colours must share one recoloured Bitmap");
                Check(!ReferenceEquals(PoseImage(chloe), shared) && ReferenceEquals(PoseImage(group[0]), PoseImage(chloe)),
                    "Uncoloured actors (and colours touching no zone) must share the original strip");

                using (var image = new Bitmap(view.Width, view.Height))
                {
                    using (Graphics graphics = Graphics.FromImage(image)) view.DrawCells(graphics);
                    image.Save(Path.Combine(TestPaths.Work, "bot-recoloration.png"), ImageFormat.Png);
                    Color redPixel = Color.FromArgb(240, 40, 40);
                    foreach (UserMapControl.ActorVisualState state in new[] { alice, bruno, npc, group[1] })
                        Check(Near(At(image, state.SpriteBounds, .25f, .5f), redPixel) && Near(At(image, state.SpriteBounds, .75f, .5f), D),
                            "Actor " + state.Id + " is not drawn recoloured: " + At(image, state.SpriteBounds, .25f, .5f));
                    foreach (UserMapControl.ActorVisualState state in new[] { chloe, group[0] })
                        Check(Near(At(image, state.SpriteBounds, .25f, .5f), D), "Actor " + state.Id + " without colours is not drawn as D");
                }
            }
        }
    }
}
