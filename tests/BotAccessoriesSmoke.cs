using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Outil_Azur_complet.Bot.Controls;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Utils.Crypto;

// Accessoires des personnages (lot AN7) : table des clips réservés (emplacements/<gfx>.tsv : en-tête, lignes illisibles,
// emplacement hors de 0..4, bande sans casse), symboles des objets portés d'après items.xml (type_gfx, type du GM pour un objet
// vivant, image ajoutée à l'étiquette, objet inconnu ignoré, sans textes chargés aucun accessoire), bibliothèque des accessoires
// (lecture sur le pool, noms refusés sans toucher au disque, bande absente, tables partagées entre deux vues), puis une carte
// synthétique dessinée par Graphics.FromImage : coiffe devant la tête, cape derrière le corps, familier à _x inversé au même
// endroit quand le sprite est retourné, bouclier à l'échelle 2, arme tournée d'un quart de tour, objet vivant sur son image,
// familier qui marche (bande en boucle sans débordement sur l'image voisine), Oa qui retire tout. Données livrées : un PNG par
// ligne d'ancres.tsv et l'inverse, emplacements des 24 classes (clips 1 à 4 de staticR, arme dans une animation), budget de 20 Mo, 16_2_S et la
// coiffe 16_2 posée sur le gfx 10. Passe sous Mono.
internal static class BotAccessoriesSmoke
{
    private const int MapWidth = 8, MapHeight = 8;
    private static readonly Color BodyColor = Color.FromArgb(255, 100, 100, 100);
    private static readonly Color Red = Color.FromArgb(255, 230, 20, 20), Blue = Color.FromArgb(255, 20, 20, 230), Green = Color.FromArgb(255, 20, 200, 20);
    private static readonly Color Cyan = Color.FromArgb(255, 20, 220, 220), Yellow = Color.FromArgb(255, 230, 230, 20), Magenta = Color.FromArgb(255, 230, 20, 230);
    private static readonly Color Orange = Color.FromArgb(255, 240, 140, 20);
    private const string Header = "anim\timage\templacement\tetiquette\tmiroir\ta\tb\tc\td\ttx\tty\ttx_miroir\tty_miroir\tdevant";

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
            Placements();
            string root = Path.Combine(TestPaths.Work, "accessoires");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            string sprites = Path.Combine(root, "sprites"), accessories = Path.Combine(root, ActorAccessories.FolderName);
            string lang = Path.Combine(root, "lang"), overheads = Path.Combine(root, "overheads");
            foreach (string folder in new[] { sprites, accessories, Path.Combine(accessories, ActorAccessories.PlacementsFolderName), lang, overheads })
                Directory.CreateDirectory(folder);
            Assets(sprites, accessories);
            Worn(lang);
            Library(sprites, accessories);
            MapActors(root, sprites, overheads);
            Shipped(root, overheads);
            Console.WriteLine("OK: placements table parsed, worn symbols from items.xml (type_gfx, living item frame), accessory library on the pool, "
                + "hat in front, cape behind, mirrored pet kept on its side, scaled shield, rotated weapon, walking pet looping without bleeding, Oa clears, "
                + "shipped Accessoires data (one PNG per row, 24 placement tables, 20 MB budget, 16_2 on gfx 10)");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    // ---------------------------------------------------------------- table des emplacements

    private static void Placements()
    {
        AccessoryPlacements table = AccessoryPlacements.Parse(new[]
        {
            Header,
            "staticR\t0\t1\tR\t0\t1\t0\t0\t1\t5\t-30\t-5\t-30\t1",
            "staticR\t0\t2\tR\t0\t0.5\t0.25\t-0.25\t0.5\t-8\t-20\t8\t-20\t0",
            "staticR\t0\t3\tWL\t1\t1\t0\t0\t1\t12\t0\t-12\t0\t1",
            "walkL\t3\t0\tL\t0\t1\t0\t0\t1\t2\t-18\t-2\t-18\t1",
            "staticR\t0\t9\tR\t0\t1\t0\t0\t1\t0\t0\t0\t0\t1",          // emplacement inconnu
            "staticR\t0\t1\tR L\t0\t1\t0\t0\t1\t0\t0\t0\t0\t1",        // étiquette illisible
            "staticR\t0\t1\tR\t2\t1\t0\t0\t1\t0\t0\t0\t0\t1",          // miroir ni 0 ni 1
            "staticR\t0\t1\tR\t0\tx\t0\t0\t1\t0\t0\t0\t0\t1",          // matrice illisible
            "staticR\t0\t1\tR\t0\t1\t0\t0\t1\t0\t0\t0\t0",             // colonne manquante
            "# commentaire", "", "n'importe quoi"
        });
        Check(table.Count == 4 && table.Reason == null, "Placements: " + table.Count + " rows kept, expected 4");
        IReadOnlyList<AccessorySlot> slots = table.At("staticR", 0);
        Check(slots.Count == 3 && slots[0].Slot == 1 && slots[0].Label == "R" && !slots[0].Mirror && slots[0].Front && slots[0].Tx == 5 && slots[0].Ty == -30
            && slots[0].TxMirror == -5 && slots[0].TyMirror == -30 && slots[0].A == 1 && slots[0].D == 1 && slots[0].B == 0 && slots[0].C == 0, "staticR frame 0 slots misread");
        Check(slots[1].Slot == 2 && !slots[1].Front && Math.Abs(slots[1].A - .5f) < 1e-6 && Math.Abs(slots[1].B - .25f) < 1e-6 && Math.Abs(slots[1].C + .25f) < 1e-6
            && Math.Abs(slots[1].Determinant - .3125f) < 1e-5, "cape matrix misread: " + slots[1].A + " " + slots[1].B + " " + slots[1].C + " " + slots[1].D);
        Check(slots[2].Slot == 3 && slots[2].Mirror && slots[2].Label == "WL", "pet slot misread");
        Check(table.At("STATICR", 0).Count == 3 && table.At("staticR", 1).Count == 0 && table.At("walkL", 3).Count == 1 && table.At(null, 0).Count == 0
            && table.At("runR", 0).Count == 0, "At() lookups");
        Check(table.Bands.OrderBy(b => b, StringComparer.Ordinal).SequenceEqual(new[] { "staticR", "walkL" }), "Bands");
        Check(AccessoryPlacements.Empty.Count == 0 && AccessoryPlacements.Empty.At("staticR", 0).Count == 0, "Empty table");
        AccessoryPlacements missing = AccessoryPlacements.Read(Path.Combine(TestPaths.Work, "nulle-part", "9.tsv"));
        Check(missing.Count == 0 && !string.IsNullOrEmpty(missing.Reason), "Missing file must give an empty table with a reason");
        Check(AccessoryPlacements.Parse(null).Count == 0, "Parse(null)");
        Check(ActorAccessories.IsSymbol("16_2") && ActorAccessories.IsSymbol("82_1") && !ActorAccessories.IsSymbol("16_2_R") && !ActorAccessories.IsSymbol("../16_2")
            && !ActorAccessories.IsSymbol("") && !ActorAccessories.IsSymbol(null), "IsSymbol");
        Check(ActorAccessories.IsLabel("R") && ActorAccessories.IsLabel("WL") && ActorAccessories.IsLabel("R19") && !ActorAccessories.IsLabel("R L") && !ActorAccessories.IsLabel("3")
            && !ActorAccessories.IsLabel("a/b") && !ActorAccessories.IsLabel(""), "IsLabel");
        Check(ActorAccessories.BandName("10_staticR.png", 10) == "staticR" && ActorAccessories.BandName("10_anim12L.png", 10) == "anim12L"
            && ActorAccessories.BandName("10R.png", 10) == null && ActorAccessories.BandName("101_staticR.png", 10) == null && ActorAccessories.BandName(null, 10) == null, "BandName");
    }

    // ---------------------------------------------------------------- objets portés

    private static void WriteLang(string folder, string body) =>
        File.WriteAllText(Path.Combine(folder, "items.xml"), "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"items\" langue=\"fr\" version=\"1\" source=\"items_fr_1.swf\">\n"
            + body + "\n</BotLang>\n", new UTF8Encoding(false));

    private static void Worn(string lang)
    {
        LangData.Clear();
        Check(ActorAccessories.Worn(ActorAccessory.ParseList(",2b8,,,")).Count == 0, "Without items.xml no accessory can be named");
        // 696 coiffe 16_2, 700 cape 17_1, 701 familier 18_61, 702 bouclier 82_1, 703 arme 2_1, 704 sans gfx, 705 gfx illisible.
        WriteLang(lang, "<objet id=\"696\" nom=\"Coiffe fictive\" type=\"16\" gfx=\"2\" niveau=\"20\" />\n"
            + "<objet id=\"700\" nom=\"Cape fictive\" type=\"17\" gfx=\"1\" />\n<objet id=\"701\" nom=\"Familier fictif\" type=\"18\" gfx=\"61\" />\n"
            + "<objet id=\"702\" nom=\"Bouclier fictif\" type=\"82\" gfx=\"1\" />\n<objet id=\"703\" nom=\"Arc fictif\" type=\"2\" gfx=\"1\" an=\"10\" />\n"
            + "<objet id=\"704\" nom=\"Sans gfx\" type=\"16\" />\n<objet id=\"705\" nom=\"Gfx illisible\" type=\"16\" gfx=\"x\" />");
        Check(LangData.Load(lang) == 1 && LangData.IsLoaded("items"), "Synthetic items.xml not loaded: " + string.Join(" / ", LangData.LoadWarnings));
        IReadOnlyList<WornAccessory> worn = ActorAccessories.Worn(ActorAccessory.ParseList(",2b8,,,"));
        Check(worn.Count == 1 && worn[0].Slot == 1 && worn[0].Symbol == "16_2" && worn[0].FrameSuffix == "" && worn[0].TemplateId == 696, "Hat 696 must give 16_2 on slot 1");
        worn = ActorAccessories.Worn(ActorAccessory.ParseList("2bf,2b8~16~4,2bc,2bd,2be"));
        Check(worn.Count == 5 && worn.Select(w => w.Symbol).SequenceEqual(new[] { "2_1", "16_2", "17_1", "18_61", "82_1" })
            && worn.Select(w => w.Slot).SequenceEqual(new[] { 0, 1, 2, 3, 4 }) && worn[1].FrameSuffix == "3" && worn[0].FrameSuffix == "",
            "Full stuff misread: " + string.Join(",", worn.Select(w => w.Slot + ":" + w.Symbol + w.FrameSuffix)));
        Check(worn[1].LabelFor(AccessoryPlacements.Parse(new[] { "staticR\t0\t1\tR\t0\t1\t0\t0\t1\t0\t0\t0\t0\t1" }).At("staticR", 0)[0]) == "R3",
            "Living item label must be the slot label followed by its frame (R3)");
        // Type du GM (id~type~image) : objet vivant de type 17 sur la coiffe 696.
        Check(ActorAccessories.Worn(ActorAccessory.ParseList("2b8~17~1"))[0].Symbol == "17_2", "GM type must override the item type");
        Check(ActorAccessories.Worn(ActorAccessory.ParseList("270f,2c0,2c1")).Count == 0, "Unknown item, item without gfx and unreadable gfx must be skipped");
        Check(ActorAccessories.Worn(null).Count == 0 && ActorAccessories.Worn(new ActorAccessory[0]).Count == 0, "Empty lists");
        Check(ActorAccessories.Symbol(696) == "16_2" && ActorAccessories.Symbol(9999) == null && ActorAccessories.Symbol(704) == null && ActorAccessories.Symbol(696, 19) == "19_2",
            "Symbol()");
        Check(ActorAccessories.Of(null) == null && ActorAccessories.Of(new MonsterGroupActor(-2)) == null, "Of()");
    }

    // ---------------------------------------------------------------- données synthétiques

    private static void Band(string path, int width, int height, params Color[] frames)
    {
        using (var bitmap = new Bitmap(width * frames.Length, height, PixelFormat.Format32bppArgb))
        {
            for (int frame = 0; frame < frames.Length; frame++)
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        bitmap.SetPixel(frame * width + x, y, frames[frame]);
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    /// <summary>
    /// Corps 600 (20 × 30 gris, pied au milieu en bas), corps 601 (même chose) ; accessoires : coiffe 16_2_R (6 × 8 rouge,
    /// origine au milieu, 4 px sous le haut), coiffe 16_2_R3 (orange), cape 17_1_R (10 × 10 bleu, origine au centre), familier
    /// 18_61_R (4 × 4 vert, origine au milieu en bas) et 18_61_WR (vert puis cyan, 20 ips en boucle), bouclier 82_1_R (3 × 3 jaune,
    /// origine au coin haut-gauche), arme 2_1_R (4 × 4 magenta, origine au coin haut-gauche). Emplacements du gfx 600 en staticR :
    /// coiffe devant en (5, −30), cape derrière en (−8, −20), familier devant à _x inversé en (12, 0), bouclier devant à l'échelle 2
    /// en (−30, −10), arme devant tournée d'un quart de tour en (30, −20) ; gfx 601 : familier WR en (12, 0).
    /// </summary>
    private static void Assets(string sprites, string accessories)
    {
        Band(Path.Combine(sprites, "600_staticR.png"), 20, 30, BodyColor);
        Band(Path.Combine(sprites, "601_staticR.png"), 20, 30, BodyColor);
        File.WriteAllLines(Path.Combine(sprites, "ancres.tsv"), new[]
        {
            "gfx\tanim\txmin\tymin\tlargeur\thauteur\timages\tips\tfin",
            "600\tstaticR\t-10\t-30\t20\t30\t1\t40\tarret", "601\tstaticR\t-10\t-30\t20\t30\t1\t40\tarret"
        });
        Band(Path.Combine(accessories, "16_2_R.png"), 6, 8, Red);
        Band(Path.Combine(accessories, "16_2_R3.png"), 6, 8, Orange);
        Band(Path.Combine(accessories, "17_1_R.png"), 10, 10, Blue);
        Band(Path.Combine(accessories, "18_61_R.png"), 4, 4, Green);
        Band(Path.Combine(accessories, "18_61_WR.png"), 4, 4, Green, Cyan);
        Band(Path.Combine(accessories, "82_1_R.png"), 3, 3, Yellow);
        Band(Path.Combine(accessories, "2_1_R.png"), 4, 4, Magenta);
        File.WriteAllLines(Path.Combine(accessories, "ancres.tsv"), new[]
        {
            "gfx\tanim\txmin\tymin\tlargeur\thauteur\timages\tips\tfin",
            "16_2\tR\t-3\t-4\t6\t8\t1\t40\tarret", "16_2\tR3\t-3\t-4\t6\t8\t1\t40\tarret", "17_1\tR\t-5\t-5\t10\t10\t1\t40\tarret",
            "18_61\tR\t-2\t-4\t4\t4\t1\t40\tarret", "18_61\tWR\t-2\t-4\t4\t4\t2\t20\tboucle",
            "82_1\tR\t0\t0\t3\t3\t1\t40\tarret", "2_1\tR\t0\t0\t4\t4\t1\t40\tarret"
        });
        File.WriteAllLines(Path.Combine(accessories, ActorAccessories.PlacementsFolderName, "600.tsv"), new[]
        {
            Header,
            "staticR\t0\t1\tR\t0\t1\t0\t0\t1\t5\t-30\t-5\t-30\t1",
            "staticR\t0\t2\tR\t0\t1\t0\t0\t1\t-8\t-20\t8\t-20\t0",
            "staticR\t0\t3\tR\t1\t1\t0\t0\t1\t12\t0\t-12\t0\t1",
            "staticR\t0\t4\tR\t0\t2\t0\t0\t2\t-30\t-10\t30\t-10\t1",
            "staticR\t0\t0\tR\t0\t0\t1\t-1\t0\t30\t-20\t-30\t-20\t1",
            "ligne illisible",
            "staticR\t0\t9\tR\t0\t1\t0\t0\t1\t0\t0\t0\t0\t1"
        });
        File.WriteAllLines(Path.Combine(accessories, ActorAccessories.PlacementsFolderName, "601.tsv"), new[]
        {
            Header, "staticR\t0\t3\tWR\t1\t1\t0\t0\t1\t12\t0\t-12\t0\t1"
        });
    }

    // ---------------------------------------------------------------- bibliothèque

    private static void Library(string sprites, string accessories)
    {
        using (var library = new ActorSprites(sprites, null))
        {
            Check(string.Equals(Path.GetFullPath(library.AccessoriesDirectory), Path.GetFullPath(accessories), StringComparison.Ordinal),
                "Default accessories folder must be the Accessoires sibling of the sprites folder: " + library.AccessoriesDirectory);
            SpritePose pose = library.ResolveAccessory("16_2", "R");
            Check(pose.State == SpriteLoadState.Loading && pose.Sheet == null, "First request must be in reading state");
            Check(library.PendingCount >= 1 && library.WaitForPending(10000), "Accessory decoding did not finish");
            pose = library.ResolveAccessory("16_2", "R");
            Check(pose.State == SpriteLoadState.Ready && pose.Sheet != null && pose.Sheet.FrameWidth == 6 && pose.Sheet.FrameHeight == 8 && pose.Sheet.Frames == 1
                && pose.Sheet.XMin == -3 && pose.Sheet.YMin == -4 && pose.Sheet.File == "16_2_R.png" && !pose.Mirrored && pose.Animation == "R", "16_2_R misread");
            Bitmap shared = pose.Sheet.Image;
            SpritePose walking = library.ResolveAccessory("18_61", "WR");
            Check(library.WaitForPending(10000), "WR decoding did not finish");
            walking = library.ResolveAccessory("18_61", "WR");
            Check(walking.State == SpriteLoadState.Ready && walking.Sheet.Frames == 2 && walking.Sheet.FramesPerSecond == 20 && walking.Sheet.End == SpriteEnd.Loop
                && walking.Sheet.FrameAt(1000, true) == 0 && walking.Sheet.FrameAt(1050, true) == 1, "18_61_WR band misread");
            SpritePose absent = library.ResolveAccessory("16_2", "Z");
            Check(absent.State == SpriteLoadState.Loading && library.WaitForPending(10000), "Missing accessory lookup did not finish");
            absent = library.ResolveAccessory("16_2", "Z");
            Check(absent.State == SpriteLoadState.Missing && absent.Reason != null && absent.Reason.Contains("absent"), "Missing accessory: " + absent.Reason);
            foreach (var bad in new[] { new[] { "16_2; rm", "R" }, new[] { "16_2", "R L" }, new[] { "../16_2", "R" }, new[] { null, "R" }, new[] { "16_2", "" } })
            {
                SpritePose refused = library.ResolveAccessory(bad[0], bad[1]);
                Check(refused.State == SpriteLoadState.Missing && refused.Reason != null && refused.Reason.Contains("invalide") && library.PendingCount == 0,
                    "Invalid name must be refused without reading: " + bad[0] + "/" + bad[1]);
            }
            Check(library.Placements(0) == AccessoryPlacements.Empty && library.Placements(-5) == AccessoryPlacements.Empty, "Placements of gfx 0");
            Check(library.Placements(600) == null && library.PendingCount >= 1, "Placements must be read on the pool first");
            Check(library.WaitForPending(10000), "Placements reading did not finish");
            AccessoryPlacements table = library.Placements(600);
            Check(table != null && table.Count == 5 && table.At("staticR", 0).Count == 5 && table.Reason == null, "600.tsv misread: " + (table?.Count ?? -1));
            Check(library.Placements(602) == null && library.WaitForPending(10000), "Placements of an unknown gfx");
            AccessoryPlacements unknown = library.Placements(602);
            Check(unknown != null && unknown.Count == 0 && unknown.Reason != null, "Unknown gfx must give an empty table with a reason");
            using (var other = new ActorSprites(sprites, null))
            {
                SpritePose again = other.ResolveAccessory("16_2", "R");
                Check(again.State == SpriteLoadState.Ready && ReferenceEquals(again.Sheet.Image, shared), "Second view must share the decoded accessory");
                Check(ReferenceEquals(other.Placements(600), table), "Second view must share the placements table");
            }
            library.ReleaseAll();
            Check(library.ResolveAccessory("16_2", "R").State == SpriteLoadState.Ready, "Released accessory stays in cache");
        }
        using (var custom = new ActorSprites(sprites, null, null, Path.Combine(sprites, "ailleurs")))
            Check(custom.AccessoriesDirectory.EndsWith("ailleurs", StringComparison.Ordinal), "Explicit accessories folder ignored");
    }

    // ---------------------------------------------------------------- carte

    private static string EncodedCell()
    {
        int[] value = { 33, 7, 32, 0, 4, 0, 0, 0, 0, 0 };
        return new string(value.Select(part => Hash.caracteres_array[part]).ToArray());
    }

    private static bool Near(Color pixel, Color expected) =>
        pixel.A > 200 && Math.Abs(pixel.R - expected.R) < 40 && Math.Abs(pixel.G - expected.G) < 40 && Math.Abs(pixel.B - expected.B) < 40;

    private static Color At(Bitmap image, PointF foot, float dx, float dy)
    {
        int x = (int)Math.Round(foot.X + dx), y = (int)Math.Round(foot.Y + dy);
        Check(x >= 0 && y >= 0 && x < image.Width && y < image.Height, "Sample outside the image: " + x + ", " + y);
        return image.GetPixel(x, y);
    }

    private static string Describe(Color color) => color.A + ":" + color.R + "," + color.G + "," + color.B;

    private static bool Clear(Bitmap image, PointF foot, float dx, float dy)
    {
        Color pixel = At(image, foot, dx, dy);
        return !Near(pixel, Red) && !Near(pixel, Blue) && !Near(pixel, Green) && !Near(pixel, Cyan) && !Near(pixel, Yellow) && !Near(pixel, Magenta) && !Near(pixel, Orange);
    }

    private static Map SyntheticMap(Accounts account)
    {
        int count = MapHeight * (2 * MapWidth - 1) - (MapWidth - 1);
        string data = string.Concat(Enumerable.Repeat(EncodedCell(), count));
        Map map = account.Game.Map;
        map.MapID = 991307; map.MapWidth = MapWidth; map.MapHeight = MapHeight; map.MapData = data; map.DecompressMap(data);
        return map;
    }

    private static void AddActors(Map map, string gm)
    {
        GmParseResult parsed = GmParser.Parse(gm, false, 42, -1);
        Check(parsed.Rejected.Count == 0, "Synthetic GM entries were rejected: " + string.Join(" / ", parsed.Rejected));
        foreach (GmEntry entry in parsed.Entries)
        {
            entry.Actor.Cell = map.GetCellFromId((short)entry.Actor.CellId);
            map.AddActor(entry.Actor);
        }
    }

    /// <summary>Vue à l'échelle 1 (un pixel de carte = un pixel d'écran), carte rafraîchie.</summary>
    private static void Prepare(TestView view, Accounts account)
    {
        view.SetAccount(account); view.W = MapWidth; view.H = MapHeight; view.Size = new Size(640, 400);
        view.SetCellNum(); view.DrawGrille(); view.RefreshMap();
        Check(view.WaitForActorSprites(10000), "Actor PNG decoding did not finish");
        UserMapControl.ActorVisualState any = view.GetActorVisualStates().FirstOrDefault(state => state.HasSprite);
        Check(any != null, "No actor with a sprite");
        double scale = any.SpriteBounds.Width / any.WorldBounds.Width;
        view.ZoomAt(1 / scale, new Point(view.Width / 2, view.Height / 2));
        any = view.GetActorVisualStates().First(state => state.HasSprite);
        Check(Math.Abs(any.SpriteBounds.Width - any.WorldBounds.Width) < .01, "View scale is not 1: " + any.SpriteBounds.Width + " for " + any.WorldBounds.Width);
    }

    private static Bitmap Render(TestView view, string file)
    {
        var image = new Bitmap(view.Width, view.Height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(image)) view.DrawCells(graphics);
        if (file != null) image.Save(Path.Combine(TestPaths.Work, file), ImageFormat.Png);
        return image;
    }

    /// <summary>
    /// Alice (gfx 600, orientation 1, tout l'équipement), Bruno (orientation 3 : retourné), Chloé (sans accessoire), Dany (coiffe
    /// vivante 696~16~4) et Émile (gfx 601, familier qui marche) sur une carte synthétique : accessoires résolus, dessinés devant
    /// ou derrière le corps aux positions de la matrice, familier gardé du même côté quand le sprite est retourné, image du familier
    /// qui suit l'horloge, Oa qui retire tout.
    /// </summary>
    private static void MapActors(string root, string sprites, string overheads)
    {
        using (var account = new Accounts(new AccountConfig("synthetic-accessoires", "Synthetic123", "test")))
        {
            Map map = SyntheticMap(account);
            AddActors(map, "GM"
                + "|+16;1;0;43;Alice;1;600^100;0;0,0,0,0;-1;-1;-1;2bf,2b8,2bc,2bd,2be;0;;;;;0;;0"
                + "|+20;3;0;44;Bruno;1;600^100;0;0,0,0,0;-1;-1;-1;2bf,2b8,2bc,2bd,2be;0;;;;;0;;0"
                + "|+50;1;0;45;Chloe;1;600^100;0;0,0,0,0;-1;-1;-1;;0;;;;;0;;0"
                + "|+54;1;0;46;Dany;1;600^100;0;0,0,0,0;-1;-1;-1;,2b8~16~4,,,;0;;;;;0;;0"
                + "|+95;1;0;47;Emile;1;601^100;0;0,0,0,0;-1;-1;-1;,,,2bd,;0;;;;;0;;0");
            double clock = 1000;
            using (var view = new TestView(() => clock, sprites, overheads))
            {
                Prepare(view, account);
                UserMapControl.ActorVisualState alice = view.GetActorVisualState(43), bruno = view.GetActorVisualState(44), chloe = view.GetActorVisualState(45);
                UserMapControl.ActorVisualState dany = view.GetActorVisualState(46), emile = view.GetActorVisualState(47);
                Check(alice != null && bruno != null && chloe != null && dany != null && emile != null, "Synthetic actors are not all on the map");
                Check(alice.Worn.Count == 5 && bruno.Worn.Count == 5 && chloe.Worn.Count == 0 && dany.Worn.Count == 1 && emile.Worn.Count == 1, "Worn accessories not carried by the states");
                Check(ActorAccessories.Of(map.GetActor(43)).Count == 5 && ActorAccessories.Of(map.GetActor(45)).Count == 0, "Of(PlayerActor)");
                Check(alice.AccessoryCount == 5 && alice.AccessoriesReason == null, "Alice must show 5 accessories: " + alice.AccessoryCount + " (" + alice.AccessoriesReason + ")");
                Check(alice.AccessoryFiles.SequenceEqual(new[] { "17_1_R.png", "16_2_R.png", "18_61_R.png", "82_1_R.png", "2_1_R.png" }),
                    "Alice draw order (back first): " + string.Join(",", alice.AccessoryFiles));
                Check(bruno.IsMirrored && bruno.AccessoryCount == 5, "Bruno (orientation 3) must be mirrored with 5 accessories");
                Check(chloe.AccessoryCount == 0 && chloe.AccessoriesReason == null, "Chloe has no accessory");
                Check(dany.AccessoryCount == 1 && dany.AccessoryFiles[0] == "16_2_R3.png", "Living hat must use the R3 label: " + string.Join(",", dany.AccessoryFiles));
                Check(emile.AccessoryCount == 1 && emile.AccessoryFiles[0] == "18_61_WR.png", "Walking pet band expected on Emile");

                using (Bitmap image = Render(view, "bot-accessoires.png"))
                {
                    PointF foot = alice.ScreenPosition;
                    // Corps : gris sur 20 × 30 au-dessus du pied.
                    Check(Near(At(image, foot, 0, -15), BodyColor) && Near(At(image, foot, -8, -2), BodyColor), "Body not drawn as grey: " + Describe(At(image, foot, 0, -15)));
                    // Coiffe devant : (5, -30) + [-3, 3) × [-4, 4) : au-dessus du corps et sur le corps.
                    Check(Near(At(image, foot, 5, -32), Red) && Near(At(image, foot, 5, -28), Red) && Near(At(image, foot, 6, -32), Red),
                        "Hat not drawn in front of the head: " + Describe(At(image, foot, 5, -32)) + " / " + Describe(At(image, foot, 5, -28)));
                    Check(Clear(image, foot, 10, -32) && Clear(image, foot, 5, -36), "Hat drawn outside its frame");
                    // Cape derrière : (-8, -20) + [-5, 5)² : visible à gauche du corps, cachée par le corps.
                    Check(Near(At(image, foot, -11.5f, -20), Blue) && Near(At(image, foot, -5, -20), BodyColor),
                        "Cape must be behind the body: " + Describe(At(image, foot, -11.5f, -20)) + " / " + Describe(At(image, foot, -5, -20)));
                    // Familier devant, (12, 0) + [-2, 2) × [-4, 0).
                    Check(Near(At(image, foot, 12, -2), Green) && Clear(image, foot, 15, -2) && Clear(image, foot, 12, 2), "Pet not at (12, -2): " + Describe(At(image, foot, 12, -2)));
                    // Bouclier à l'échelle 2 : (-30, -10) + [0, 6)².
                    Check(Near(At(image, foot, -27, -7), Yellow) && Near(At(image, foot, -26, -6), Yellow) && Clear(image, foot, -31.5f, -7) && Clear(image, foot, -27, -12),
                        "Scaled shield not at (-30..-24, -10..-4): " + Describe(At(image, foot, -27, -7)));
                    // Arme tournée : (x, y) -> (-y + 30, x - 20) : [0, 4) × [0, 4) devient [26, 30) × [-20, -16).
                    Check(Near(At(image, foot, 28, -18), Magenta) && Clear(image, foot, 32, -18) && Clear(image, foot, 28, -13) && Clear(image, foot, 24, -18),
                        "Rotated weapon not at (26..30, -20..-16): " + Describe(At(image, foot, 28, -18)) + " / " + Describe(At(image, foot, 32, -18)));

                    // Bruno retourné : coiffe en x = -5, arme en [-30, -28), familier toujours à droite (tx_miroir).
                    PointF mirrored = bruno.ScreenPosition;
                    Check(Near(At(image, mirrored, -5, -32), Red) && Clear(image, mirrored, 5, -32), "Mirrored hat must be at x = -5: " + Describe(At(image, mirrored, -5, -32)));
                    Check(Near(At(image, mirrored, -28, -18), Magenta) && Clear(image, mirrored, 28, -18), "Mirrored weapon must be at x = -28");
                    Check(Near(At(image, mirrored, 12, -2), Green) && Clear(image, mirrored, -12, -2), "Mirrored pet (clip with _x inverted) must stay at x = 12: "
                        + Describe(At(image, mirrored, 12, -2)) + " / " + Describe(At(image, mirrored, -12, -2)));
                    Check(Near(At(image, mirrored, 11.5f, -20), Blue) && Near(At(image, mirrored, 5, -20), BodyColor), "Mirrored cape must be behind the body on the right");
                    // Chloé : rien autour du corps ; Dany : coiffe orange ; Émile : familier vert (image 0 à t = 1 000 ms), sans cyan.
                    Check(Clear(image, chloe.ScreenPosition, 5, -32) && Clear(image, chloe.ScreenPosition, -11.5f, -20) && Clear(image, chloe.ScreenPosition, 12, -2), "Chloe must have no accessory drawn");
                    Check(Near(At(image, dany.ScreenPosition, 5, -32), Orange), "Living hat must be drawn orange: " + Describe(At(image, dany.ScreenPosition, 5, -32)));
                    Check(Near(At(image, emile.ScreenPosition, 12, -2), Green) && Clear(image, emile.ScreenPosition, 16, -2) && Clear(image, emile.ScreenPosition, 8, -2),
                        "Walking pet frame 0 must be green without bleeding: " + Describe(At(image, emile.ScreenPosition, 12, -2)) + " / " + Describe(At(image, emile.ScreenPosition, 16, -2)));
                }
                clock = 1050;
                emile = view.GetActorVisualState(47);
                using (Bitmap image = Render(view, null))
                    Check(Near(At(image, emile.ScreenPosition, 12, -2), Cyan) && Clear(image, emile.ScreenPosition, 16, -2),
                        "Walking pet frame 1 at t + 50 ms must be cyan: " + Describe(At(image, emile.ScreenPosition, 12, -2)));
                // Oa : l'équipement d'Alice disparaît (modèle mis à jour comme par MapFrame).
                var player = map.GetActor(43) as PlayerActor;
                Check(player != null, "Alice is not a PlayerActor");
                player.Stuff = ",,,,"; player.Accessories = ActorAccessory.ParseList(player.Stuff);
                alice = view.GetActorVisualState(43);
                Check(alice.Worn.Count == 0 && alice.AccessoryCount == 0, "After Oa with empty stuff Alice must show no accessory");
                using (Bitmap image = Render(view, null))
                    Check(Clear(image, alice.ScreenPosition, 5, -32) && Clear(image, alice.ScreenPosition, 28, -18), "Accessories still drawn after Oa");
                // Survol : la teinte de sélection ne fait pas disparaître les accessoires de Bruno.
                player = map.GetActor(44) as PlayerActor;
                player.Accessories = ActorAccessory.ParseList(",2b8,,,");
                bruno = view.GetActorVisualState(44);
                Check(bruno.AccessoryCount == 1, "Bruno keeps his hat after a stuff change");
            }
        }
    }

    // ---------------------------------------------------------------- données livrées

    private static void Shipped(string root, string overheads)
    {
        string bin = TestPaths.ApplicationBin;
        string shipped = Path.Combine(bin, "ressources", "Bot", ActorAccessories.FolderName);
        string anchors = Path.Combine(shipped, "ancres.tsv");
        Check(File.Exists(anchors), "Accessoires/ancres.tsv is not copied next to the executable: " + shipped);
        string[] rows = File.ReadAllLines(anchors).Skip(1).Where(line => line.Length > 0).ToArray();
        Check(rows.Length >= 4000, "Too few accessory strips: " + rows.Length);
        var named = new HashSet<string>(StringComparer.Ordinal);
        int hats = 0;
        foreach (string row in rows)
        {
            string[] f = row.Split('\t');
            Check(f.Length == 9 && ActorAccessories.IsSymbol(f[0]) && ActorAccessories.IsLabel(f[1]) && (f[7] == "40" || f[7] == "20") && (f[8] == "arret" || f[8] == "boucle"),
                "Unexpected ancres.tsv row: " + row);
            Check(named.Add(f[0] + "_" + f[1]), "Duplicate row: " + row);
            Check(File.Exists(Path.Combine(shipped, f[0] + "_" + f[1] + ".png")), "No PNG for " + row);
            if (f[0].StartsWith("16_", StringComparison.Ordinal)) hats++;
        }
        Check(hats >= 1000, "Too few hats: " + hats);
        long bytes = 0;
        foreach (string file in Directory.EnumerateFiles(shipped))
        {
            bytes += new FileInfo(file).Length;
            string name = Path.GetFileName(file);
            if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
            Check(named.Contains(Path.GetFileNameWithoutExtension(name)), "Orphan PNG without ancres.tsv row: " + name);
        }
        string placements = Path.Combine(shipped, ActorAccessories.PlacementsFolderName);
        int[] classes = { 10, 11, 20, 21, 30, 31, 40, 41, 50, 51, 60, 61, 70, 71, 80, 81, 90, 91, 100, 101, 110, 111, 120, 121 };
        foreach (int gfx in classes)
        {
            string file = Path.Combine(placements, gfx + ".tsv");
            Check(File.Exists(file), "No placements table for gfx " + gfx);
            bytes += new FileInfo(file).Length;
            AccessoryPlacements table = AccessoryPlacements.Read(file);
            Check(table.Reason == null && table.Count >= 100, "Placements of gfx " + gfx + ": " + table.Count + " rows (" + table.Reason + ")");
            // La pose fixe réserve coiffe, cape, familier et bouclier ; l'arme n'a de clip que dans des animations (anim<n>).
            IReadOnlyList<AccessorySlot> slots = table.At("staticR", 0);
            Check(Enumerable.Range(1, ActorAccessories.SlotCount - 1).All(slot => slots.Any(s => s.Slot == slot)), "staticR of gfx " + gfx + " must reserve slots 1 to 4: "
                + string.Join(",", slots.Select(s => s.Slot)));
            Check(table.Bands.Any(band => table.At(band, 0).Any(s => s.Slot == 0)), "No band of gfx " + gfx + " reserves the weapon slot");
            Check(table.At("walkR", 0).Count >= 1 && table.At("runL", 0).Count >= 1, "walkR/runL of gfx " + gfx + " have no slot");
        }
        Check(bytes <= 20000000, "Accessoires folder over its 20 MB budget: " + bytes + " bytes");
        Console.WriteLine("Accessoires: " + rows.Length + " strips, " + hats + " hat strips, " + bytes + " bytes with the 24 placement tables");

        string sprites = Path.Combine(bin, "ressources", "Bot", "sprites");
        using (var library = new ActorSprites(sprites, null))
        {
            string[] row = rows.Select(line => line.Split('\t')).First(f => f[0] == "16_2" && f[1] == "S");
            library.ResolveAccessory("16_2", "S");
            Check(library.WaitForPending(10000), "16_2_S decoding did not finish");
            SpritePose pose = library.ResolveAccessory("16_2", "S");
            Check(pose.State == SpriteLoadState.Ready && pose.Sheet.FrameWidth == int.Parse(row[4], CultureInfo.InvariantCulture)
                && pose.Sheet.FrameHeight == int.Parse(row[5], CultureInfo.InvariantCulture) && pose.Sheet.XMin == int.Parse(row[2], CultureInfo.InvariantCulture)
                && pose.Sheet.Frames == 1, "Real 16_2_S misread: " + pose.Reason);
        }
        // La Coiffe du Ploukosse (696 : 16_2) sur un Iop (gfx 10), textes livrés.
        string lang = Path.Combine(bin, "ressources", "Bot", "BotLang");
        Check(LangData.Load(lang) >= 1 && LangData.IsLoaded("items"), "Shipped BotLang not loaded from " + lang);
        Check(ActorAccessories.Symbol(696) == "16_2" && ActorAccessories.Symbol(675) == "19_8", "Shipped items.xml: 696 must be 16_2 and 675 19_8");
        using (var account = new Accounts(new AccountConfig("synthetic-accessoires-reel", "Synthetic123", "test")))
        {
            Map map = SyntheticMap(account);
            AddActors(map, "GM|+16;1;0;48;Iop;1;10^100;0;0,0,0,0;-1;-1;-1;,2b8,,,;0;;;;;0;;0");
            double clock = 1000;
            using (var view = new TestView(() => clock, sprites, overheads))
            {
                Prepare(view, account);
                UserMapControl.ActorVisualState iop = view.GetActorVisualState(48);
                Check(iop != null && iop.HasSprite && iop.Worn.Count == 1 && iop.AccessoryCount == 1 && iop.AccessoryFiles[0].StartsWith("16_2_", StringComparison.Ordinal),
                    "Hat 16_2 not resolved on gfx 10: " + (iop?.AccessoriesReason ?? "no state") + " / " + string.Join(",", iop?.AccessoryFiles ?? new string[0]));
                using (Bitmap withHat = Render(view, "bot-accessoires-iop.png"))
                {
                    // Sans la coiffe, le rendu change sur au moins 20 pixels autour de la tête (la coiffe est dessinée).
                    var player = map.GetActor(48) as PlayerActor;
                    player.Stuff = ",,,,"; player.Accessories = ActorAccessory.ParseList(player.Stuff);
                    Check(view.GetActorVisualState(48).AccessoryCount == 0, "Iop still has an accessory after Oa");
                    using (Bitmap bare = Render(view, null))
                    {
                        int changed = 0;
                        for (int y = Math.Max(0, (int)iop.SpriteBounds.Top - 20); y < Math.Min(withHat.Height, (int)iop.SpriteBounds.Bottom); y++)
                            for (int x = Math.Max(0, (int)iop.SpriteBounds.Left - 20); x < Math.Min(withHat.Width, (int)iop.SpriteBounds.Right + 20); x++)
                                if (withHat.GetPixel(x, y) != bare.GetPixel(x, y)) changed++;
                        Check(changed >= 20, "Hat 16_2 changes too few pixels of the Iop: " + changed);
                    }
                }
            }
        }
    }
}
