using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Interfaces;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Combats;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Utils.Crypto;
using Tool_BotProtocol.Utils.Logger;

// Effets de sorts (lot AN4). Règles de SpellEffects (types, index 1 à 21 puis 0, profondeur, champ « anim », direction) ;
// données livrées : chaque gfx de sorts_utilises.txt a sa scène ou une exclusion motivée, PNG conformes à effets.tsv,
// budget du dossier Effets/ et des animations d'attaque des classes, lecture réelle d'une scène et du « ! » du coup
// critique ; puis une carte sur un faux serveur en boucle locale (MapControl à horloge injectée, carte déclarée affichée,
// sprites et effets synthétiques dans un dossier temporaire) : GA501 (anim3 en boucle pendant la durée), GA208 et
// GA228 hors combat, puis en combat GA300 (direction, anim0 bloquante, effet devant ou derrière le monstre, anim1 coupée
// à 1 000 ms, type 10 au lanceur, type 12 qui retient la file, anim -1), GA301 et GA304 (clip au-dessus pendant 5 000 ms,
// staticF d'un personnage), GA302 et GA305 (bulle), GA303 (an de l'arme : anim12 ; sans arme : anim0), lanceur invisible,
// carte cachée et paquets mal formés. Un PNG par instant dans TestPaths.Work ; dessin par Graphics.FromImage : passe sous Mono.
internal static class BotSpellEffectsSmoke
{
    private const int MapWidth = 8, MapHeight = 8;
    private const short SelfCell = 46, MonsterCell = 49, PlainCell = 52, HiddenCell = 55, UpCell = 31, OtherCell = 52;
    /// <summary>Budget du dossier Effets/ (plan des animations, section 3.5 : scènes et symboles au pas 2, 12 à 18 Mo).</summary>
    private const long EffectsBudget = 18000000;
    /// <summary>Part des animations d'attaque des classes (anim0 à 3, anim10 à 18) dans le budget des sprites.</summary>
    private const long ClassAnimsBudget = 23000000;
    private const string MissText = "Échec critique de test";

    private static readonly Color SelfColor = Color.FromArgb(120, 120, 120);
    private static readonly Color MonsterColor = Color.FromArgb(120, 60, 30);
    private static readonly Color PlainColor = Color.FromArgb(90, 130, 160);
    private static readonly Color[] Anim0Colors = Enumerable.Range(0, 8).Select(k => Color.FromArgb(20 + 25 * k, 20, 120)).ToArray();
    private static readonly Color[] Anim3Colors = { Color.FromArgb(30, 150, 30), Color.FromArgb(60, 170, 60), Color.FromArgb(90, 190, 90), Color.FromArgb(120, 210, 120) };
    private static readonly Color Anim1Color = Color.FromArgb(60, 100, 60), Anim12Color = Color.FromArgb(160, 90, 40), MonsterAnimColor = Color.FromArgb(150, 80, 50);
    private static readonly Color[] SceneColors = { Color.FromArgb(255, 0, 255), Color.FromArgb(0, 255, 0), Color.FromArgb(255, 255, 0), Color.FromArgb(0, 255, 255) };
    private static readonly Color BlockingColor = Color.FromArgb(255, 128, 192), ExtraColor = Color.FromArgb(255, 140, 0);

    private static double clock = 10000;

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
            Rules();
            ShippedEffects();
            string sprites = Assets();
            IndexRotation(sprites);
            RunMap(sprites);
            Console.WriteLine("OK: SpellEffects rules (types, index 1..21 then 0, depth, anim field, direction), shipped scenes or motivated exclusions, "
                + "PNG sizes, Effets/ and class anims budgets, real scene and critical clip, GA501 anim3 loop, GA208/GA228, GA300 direction + anim0 + effect "
                + "in front of and behind the monster, anim1 cut at 1000 ms, type 10 at the caster, type 12 holding the queue, anim -1, GA301/GA304 clip "
                + "5000 ms and staticF rule, GA302/GA305 bubble, GA303 anim12 from the weapon and anim0 without, invisible caster, hidden map, malformed packets");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
        finally { LangData.Clear(); }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    // ------------------------------------------------------------------ règles du client

    private static void Rules()
    {
        foreach (int type in new[] { 10, 11, 12, 20, 21, 30, 31, 40, 41, 50, 51 }) Check(SpellEffects.HasEffect(type), "Type " + type + " has an effect");
        foreach (int type in new[] { -1, 0, 1, 9, 13, 33, 52 }) Check(!SpellEffects.HasEffect(type), "Type " + type + " has no effect in launchVisualEffect");
        Check(new[] { 12, 30, 31, 40, 41, 51 }.All(SpellEffects.IsBlocking) && !new[] { 10, 11, 20, 21, 50 }.Any(SpellEffects.IsBlocking),
            "Blocking types are 12, 30, 31, 40, 41 and 51");
        Check(SpellEffects.AtCaster(10) && SpellEffects.AtCaster(12) && !SpellEffects.AtCaster(11), "Types 10 and 12 at the caster, 11 at the cell");
        Check(SpellEffects.IsDrawn(10) && SpellEffects.IsDrawn(11) && SpellEffects.IsDrawn(12) && !SpellEffects.IsDrawn(20) && !SpellEffects.IsDrawn(51),
            "This lot draws types 10, 11 and 12 only");

        // Index du client : 1, 2… 21, puis 0, 1 ; profondeur cellule × 100 + 50 ± (index + 51).
        var indexes = new SpellEffects();
        Check(indexes.LastIndex == 0, "Index starts at 0");
        int[] sequence = Enumerable.Range(0, 23).Select(i => indexes.NextIndex()).ToArray();
        Check(sequence.Take(21).SequenceEqual(Enumerable.Range(1, 21)) && sequence[21] == 0 && sequence[22] == 1, "Index sequence: " + string.Join(",", sequence));
        indexes.Reset();
        Check(indexes.LastIndex == 0 && indexes.NextIndex() == 1, "Reset does not restart the index");
        Check(SpellEffects.Depth(200, 1, true) == 20102 && SpellEffects.Depth(200, 1, false) == 19998 && SpellEffects.Depth(0, 0, true) == 101
            && SpellEffects.Depth(49, 21, false) == 4950 - 72, "Client depth formula");

        // Champ « anim » : -1 rien, -2 sans animation, nombre → anim<n>, bond a~b~c~d → b, le reste refusé.
        string animation;
        Check(!SpellEffects.TryParseAnimation("-1", true, out animation) && animation == null, "-1 must show nothing");
        Check(SpellEffects.TryParseAnimation("-2", true, out animation) && animation == null, "-2 must launch without animation");
        Check(SpellEffects.TryParseAnimation("-2", false, out animation) && animation == null, "GA208/228 have no -1/-2 codes (anim-2: effect, no strip)");
        Check(SpellEffects.TryParseAnimation("1.5", true, out animation) && animation == null, "anim1.5 is not a strip name");
        Check(SpellEffects.TryParseAnimation("a~../x~c", true, out animation) && animation == null, "Jump animation used as a file name");
        Check(SpellEffects.IsAnimationName("anim12") && !SpellEffects.IsAnimationName("anim-1") && !SpellEffects.IsAnimationName("../anim")
            && !SpellEffects.IsAnimationName("") && !SpellEffects.IsAnimationName(new string('a', 33)), "IsAnimationName");
        Check(SpellEffects.TryParseAnimation("0", true, out animation) && animation == "anim0", "0 → anim0");
        Check(SpellEffects.TryParseAnimation("12", true, out animation) && animation == "anim12", "12 → anim12");
        Check(SpellEffects.TryParseAnimation("2~anim2~1~3", true, out animation) && animation == "anim2", "Jump keeps its animation b");
        Check(SpellEffects.TryParseAnimation("~~", true, out animation) && animation == null, "Jump without animation b keeps the effect");
        foreach (string field in new[] { null, "", " ", "NaN", "x", "a~b" })
            Check(!SpellEffects.TryParseAnimation(field, true, out animation), "Field '" + field + "' must be refused");

        // Coordonnées à plat et directions 1, 3, 5, 7 (getDirectionFromCoordinates sans les diagonales pleines).
        PointF flat;
        Check(SpellEffects.TryFlatPosition(0, 8, out flat) && flat == new PointF(26.5f, 13.5f), "Flat position of cell 0: " + flat);
        Check(SpellEffects.TryFlatPosition(8, 8, out flat) && flat == new PointF(53f, 27f), "Flat position of cell 8 (odd row): " + flat);
        Check(!SpellEffects.TryFlatPosition(-1, 8, out flat) && !SpellEffects.TryFlatPosition(3, 1, out flat), "Invalid cell or width accepted");
        var directions = new Dictionary<int, int> { { 49, 1 }, { 54, 1 }, { 61, 3 }, { 53, 3 }, { 38, 5 }, { 39, 7 }, { UpCell, 7 } };
        foreach (KeyValuePair<int, int> pair in directions)
            Check(SpellEffects.DirectionTo(SelfCell, pair.Key, MapWidth) == pair.Value,
                "Direction 46 → " + pair.Key + " is " + SpellEffects.DirectionTo(SelfCell, pair.Key, MapWidth) + ", expected " + pair.Value);
        Check(SpellEffects.DirectionTo(MonsterCell, SelfCell, MapWidth) == 3, "Direction 49 → 46 is not 3");
        Check(SpellEffects.DirectionTo(SelfCell, SelfCell, MapWidth) == -1 && SpellEffects.DirectionTo(-1, 5, MapWidth) == -1
            && SpellEffects.DirectionTo(1, 5, 0) == -1, "Same or unreadable cell must keep the direction");
    }

    // ------------------------------------------------------------------ données livrées

    private sealed class EffectRow
    {
        public string Gfx, Anim, Fin;
        public int XMin, YMin, Width, Height, Images, Fps;
    }

    private static List<EffectRow> ReadEffects(string folder, int expectedFps)
    {
        string[] lines = File.ReadAllLines(Path.Combine(folder, "effets.tsv"), Encoding.UTF8);
        Check(lines.Length > 1 && lines[0] == "gfx\tanim\txmin\tymin\tlargeur\thauteur\timages\tips\tfin", "effets.tsv header in " + folder);
        var rows = new List<EffectRow>();
        foreach (string line in lines.Skip(1).Where(l => l.Length > 0))
        {
            string[] f = line.Split('\t');
            Check(f.Length == 9, "effets.tsv row with " + f.Length + " columns: " + line);
            int[] n = f.Skip(2).Take(6).Select(v => int.Parse(v, NumberStyles.Integer, CultureInfo.InvariantCulture)).ToArray();
            var row = new EffectRow { Gfx = f[0], Anim = f[1], XMin = n[0], YMin = n[1], Width = n[2], Height = n[3], Images = n[4], Fps = n[5], Fin = f[8] };
            Check(row.Width > 0 && row.Height > 0 && row.Images > 0 && row.Fps == expectedFps, "effets.tsv row (size, images, ips " + expectedFps + "): " + line);
            Check(ActorSprites.TryParseEnd(row.Fin, out SpriteEnd end, out string next) && end != SpriteEnd.Next, "effets.tsv fin: " + line);
            Check(new[] { "scene", "shoot", "move", "duplicate" }.Contains(row.Anim), "Unknown effect animation: " + line);
            string png = Path.Combine(folder, row.Gfx + "_" + row.Anim + ".png");
            Check(ActorSprites.TryReadPngSize(png, out int width, out int height), "PNG missing or unreadable: " + png);
            Check(width <= ActorSprites.MaxImageSide && height <= ActorSprites.MaxImageSide, "PNG wider than 32 767 px: " + png);
            if (height == row.Height) Check(width == row.Width * row.Images, "Strip width of " + png + ": " + width + " for " + row.Images + " × " + row.Width);
            else
            {
                int columns = width / row.Width, lines2 = height / row.Height;
                Check(width % row.Width == 0 && height % row.Height == 0 && columns * lines2 >= row.Images && (lines2 - 1) * columns < row.Images,
                    "Grid of " + png + ": " + width + "×" + height + " for " + row.Images + " images of " + row.Width + "×" + row.Height);
            }
            rows.Add(row);
        }
        Check(rows.Select(r => r.Gfx + "_" + r.Anim).Distinct().Count() == rows.Count, "Duplicate rows in " + folder);
        var expected = new HashSet<string>(rows.Select(r => r.Gfx + "_" + r.Anim + ".png"), StringComparer.Ordinal);
        string[] orphans = Directory.GetFiles(folder, "*.png").Select(Path.GetFileName).Where(name => !expected.Contains(name)).ToArray();
        Check(orphans.Length == 0, "PNG without effets.tsv row: " + string.Join(", ", orphans));
        return rows;
    }

    private static void ShippedEffects()
    {
        string source = Path.GetFullPath(Path.Combine(TestPaths.ApplicationBin, "..", "..", "Resources", "Bot", "Effets"));
        string shipped = Path.Combine(TestPaths.ApplicationBin, "ressources", "Bot", "Effets");
        string list = Path.GetFullPath(Path.Combine(TestPaths.ApplicationBin, "..", "..", "..", "tools", "client-analysis", "sorts_utilises.txt"));
        Check(Directory.Exists(source) && File.Exists(list), "Effets/ or sorts_utilises.txt missing: " + source + " / " + list);
        Check(File.Exists(Path.Combine(source, "PROVENANCE.md")), "Effets/PROVENANCE.md missing");
        List<EffectRow> spells = ReadEffects(Path.Combine(source, "sorts"), 20);
        List<EffectRow> extra = ReadEffects(Path.Combine(source, "extra"), 40);
        Check(extra.Count == 1 && extra[0].Gfx == "5" && extra[0].Anim == "scene" && extra[0].Fin == "boucle", "extra/5_scene (critical hit clip, loop) missing");

        // Exclusions : une ligne par gfx de la liste sans scène, avec sa raison.
        string[] exclusionLines = File.ReadAllLines(Path.Combine(source, "sorts", "exclusions.tsv"), Encoding.UTF8);
        Check(exclusionLines[0] == "gfx\traison\tdetail", "exclusions.tsv header");
        var exclusions = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (string line in exclusionLines.Skip(1).Where(l => l.Length > 0))
        {
            string[] f = line.Split('\t');
            Check(f.Length == 3 && new[] { "symboles", "script", "vide", "cairo" }.Contains(f[1]) && f[2].Length > 0, "Exclusion without a known reason: " + line);
            Check(!exclusions.ContainsKey(f[0]), "Gfx excluded twice: " + f[0]);
            exclusions[f[0]] = f;
        }
        ILookup<string, string> anims = spells.ToLookup(r => r.Gfx, r => r.Anim);
        var used = new List<KeyValuePair<string, int[]>>();
        foreach (string line in File.ReadAllLines(list, Encoding.UTF8).Where(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal)))
        {
            string[] f = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            Check(f.Length == 3 && int.TryParse(f[0], out int gfx) && gfx > 0, "sorts_utilises.txt line: " + line);
            int[] types = f[1].Split(',').Select(t => int.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            Check(types.All(t => t >= 10 && t <= 51), "sorts_utilises.txt type below 10 (ignored by addEffect): " + line);
            used.Add(new KeyValuePair<string, int[]>(f[0], types));
        }
        Check(used.Count >= 200 && used.Select(u => u.Key).Distinct().Count() == used.Count, "sorts_utilises.txt lists " + used.Count + " gfx (or duplicates)");
        var projectilesWithoutSymbols = new List<string>();
        int scenes = 0, excluded = 0, sceneTypesWithout = 0;
        foreach (KeyValuePair<string, int[]> entry in used)
        {
            string gfx = entry.Key;
            bool scene = anims[gfx].Contains("scene");
            exclusions.TryGetValue(gfx, out string[] exclusion);
            Check(scene != (exclusion != null), "Gfx " + gfx + " must have exactly one of a scene row or an exclusion (scene " + scene + ")");
            if (scene) scenes++; else excluded++;
            if (exclusion != null && exclusion[1] == "symboles")
                foreach (string symbol in exclusion[2].Split(','))
                    Check(anims[gfx].Contains(symbol), "Gfx " + gfx + " excluded for its symbols but " + symbol + " is not exported");
            if (!scene && entry.Value.Any(SpellEffects.IsDrawn))
            {
                sceneTypesWithout++;
                Check(exclusion[1] == "script" || exclusion[1] == "vide" || exclusion[1] == "cairo", "Gfx " + gfx + " of type 10/11/12 excluded for " + exclusion[1]);
            }
            bool symbols = anims[gfx].Any(a => a != "scene");
            if (entry.Value.Any(t => t == 30 || t == 31 || t == 40 || t == 41) && !symbols)
            {
                projectilesWithoutSymbols.Add(gfx);
                // Projectile sans symbole : script du SWF ou SWF sans symbole exporté (scène seule, voir PROVENANCE.md).
                Check(scene || exclusion[1] == "script" || exclusion[1] == "vide", "Projectile gfx " + gfx + " has neither symbols, scene nor motivated exclusion");
            }
        }
        Check(exclusions.Keys.All(gfx => used.Any(u => u.Key == gfx)), "Exclusion of a gfx absent from sorts_utilises.txt");
        Check(spells.Where(r => r.Anim == "scene").All(r => used.Any(u => u.Key == r.Gfx)), "Scene exported for a gfx absent from sorts_utilises.txt");

        // Budget du dossier et livraison à côté de l'exécutable.
        long effectsBytes = Directory.GetFiles(source, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length);
        Check(effectsBytes <= EffectsBudget, "Effets/ weighs " + effectsBytes + " bytes, budget " + EffectsBudget);
        foreach (string path in Directory.GetFiles(source, "*", SearchOption.AllDirectories).Where(p => p.EndsWith(".png", StringComparison.Ordinal) || p.EndsWith(".tsv", StringComparison.Ordinal)))
        {
            string copy = Path.Combine(shipped, path.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            Check(File.Exists(copy) && new FileInfo(copy).Length == new FileInfo(path).Length, "Shipped copy differs or is missing: " + copy);
        }

        // Animations d'attaque des 24 classes (anim0 à 3, anim10 à 18, R et L) : lignes d'ancres, PNG et part du budget.
        string sprites = Path.GetFullPath(Path.Combine(source, "..", "sprites"));
        string[] anchorRows = File.ReadAllLines(Path.Combine(sprites, "ancres.tsv"), Encoding.UTF8);
        int[] classes = { 10, 11, 20, 21, 30, 31, 40, 41, 50, 51, 60, 61, 70, 71, 80, 81, 90, 91, 100, 101, 110, 111, 120, 121 };
        string[] attackAnims = { "anim0", "anim1", "anim2", "anim3", "anim10", "anim11", "anim12", "anim13", "anim14", "anim15", "anim16", "anim17", "anim18" };
        var anchors = new HashSet<string>(anchorRows.Select(r => string.Join("\t", r.Split('\t').Take(2))), StringComparer.Ordinal);
        long classBytes = 0;
        foreach (int gfx in classes)
            foreach (string anim in attackAnims)
                foreach (string side in new[] { "R", "L" })
                {
                    string name = gfx + "_" + anim + side + ".png";
                    Check(anchors.Contains(gfx + "\t" + anim + side) && File.Exists(Path.Combine(sprites, name)), "Class strip missing: " + name);
                    classBytes += new FileInfo(Path.Combine(sprites, name)).Length;
                }
        Check(classBytes <= ClassAnimsBudget, "Class attack anims weigh " + classBytes + " bytes, budget " + ClassAnimsBudget);
        Check(!anchors.Contains("10\tanim8R"), "anim8 is not exported (budget): update this test and PROVENANCE.md together");

        // Lecture réelle par le chargeur du bot : une scène de type 11, le clip du coup critique, anim3 et anim12 d'une classe.
        using (var library = new ActorSprites(Path.Combine(TestPaths.ApplicationBin, "ressources", "Bot", "sprites")))
        {
            EffectRow realScene = spells.First(r => r.Anim == "scene" && used.Any(u => u.Key == r.Gfx && u.Value.Contains(11)));
            using (ActorSprites.FixedStrip scene = library.ResolveFixed(SpellEffects.SpellFamily, int.Parse(realScene.Gfx), SpellEffects.SceneAnimation))
            using (ActorSprites.FixedStrip critical = library.ResolveFixed(SpellEffects.ExtraFamily, SpellEffects.CriticalHitClip, SpellEffects.SceneAnimation))
            {
                library.Resolve(10, 1, false, "anim3"); library.Resolve(10, 1, false, "anim12");
                Check(library.WaitForPending(20000), "Shipped effects decoding did not finish");
                Check(scene.State == SpriteLoadState.Ready && scene.Sheet.Frames == realScene.Images && scene.Sheet.FramesPerSecond == 20
                    && scene.Sheet.XMin == realScene.XMin && scene.Sheet.YMin == realScene.YMin, "Shipped scene " + realScene.Gfx + " not read as declared: " + scene.Reason);
                Check(critical.State == SpriteLoadState.Ready && critical.Sheet.Frames == 62 && critical.Sheet.FramesPerSecond == 40
                    && critical.Sheet.End == SpriteEnd.Loop, "Shipped extra/5 (62 images, 40 ips, loop) not read: " + critical.Reason);
                SpritePose anim3 = library.Resolve(10, 1, false, "anim3"), anim12 = library.Resolve(10, 1, false, "anim12");
                Check(anim3.State == SpriteLoadState.Ready && anim3.FullName == "anim3R" && anim3.Sheet.FramesPerSecond == 20
                    && anim12.State == SpriteLoadState.Ready && anim12.Sheet.FramesPerSecond == 20, "Shipped class strips anim3R/anim12R not read");
            }
        }
        Console.WriteLine("Effets/: " + spells.Count + " sorts rows (" + scenes + " of " + used.Count + " gfx with a scene, " + excluded + " excluded, "
            + sceneTypesWithout + " of type 10/11 without scene), " + effectsBytes + " bytes (budget " + EffectsBudget + "); projectiles without symbols: "
            + string.Join(", ", projectilesWithoutSymbols) + "; class attack anims " + classBytes + " bytes (budget " + ClassAnimsBudget + ")");
    }

    // ------------------------------------------------------------------ sprites et effets synthétiques

    private static void Strip(string path, Color[] colors, int width, int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (var image = new Bitmap(colors.Length * width, height, PixelFormat.Format32bppArgb))
        {
            using (Graphics graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.Transparent);
                for (int k = 0; k < colors.Length; k++)
                    using (var brush = new SolidBrush(colors[k])) graphics.FillRectangle(brush, k * width, 0, width, height);
            }
            image.Save(path, ImageFormat.Png);
        }
    }

    private static Color[] Repeat(Color color, int count) => Enumerable.Repeat(color, count).ToArray();

    private static string Assets()
    {
        string root = Path.Combine(TestPaths.Work, "spell-effects");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        string sprites = Path.Combine(root, "sprites"), effects = Path.Combine(root, "Effets");
        var anchors = new List<string> { "gfx\tanim\txmin\tymin\tlargeur\thauteur\timages\tips\tfin" };
        Action<int, string, Color[], string> add = (gfx, anim, colors, end) =>
        {
            Strip(Path.Combine(sprites, gfx + "_" + anim + ".png"), colors, 20, 60);
            anchors.Add(gfx + "\t" + anim + "\t-10\t-60\t20\t60\t" + colors.Length + "\t40\t" + end);
        };
        foreach (string o in new[] { "R", "L" })
        {
            add(900, "static" + o, new[] { SelfColor }, "arret");
            add(900, "anim0" + o, Anim0Colors, "static");
            add(900, "anim1" + o, Repeat(Anim1Color, 60), "static");
            add(900, "anim12" + o, Repeat(Anim12Color, 4), "static");
            add(900, "anim3" + o, Anim3Colors, "boucle");
            add(902, "static" + o, new[] { MonsterColor }, "arret");
            add(902, "anim0" + o, Repeat(MonsterAnimColor, 4), "static");
            add(901, "static" + o, new[] { PlainColor }, "arret");
        }
        add(900, "staticF", new[] { SelfColor }, "arret");
        File.WriteAllLines(Path.Combine(sprites, "ancres.tsv"), anchors);

        // Scènes : 9001 (4 images, arret) et 9002 (6 images) dans Effets/sorts ; le « ! » dans Effets/extra, au-dessus de la tête.
        Strip(Path.Combine(effects, "sorts", "9001_scene.png"), SceneColors, 30, 30);
        Strip(Path.Combine(effects, "sorts", "9002_scene.png"), Repeat(BlockingColor, 6), 30, 30);
        File.WriteAllLines(Path.Combine(effects, "sorts", "effets.tsv"), new[]
        {
            "gfx\tanim\txmin\tymin\tlargeur\thauteur\timages\tips\tfin",
            "9001\tscene\t-15\t-45\t30\t30\t4\t40\tarret",
            "9002\tscene\t-15\t-45\t30\t30\t6\t40\tstatic"
        });
        Strip(Path.Combine(effects, "extra", "5_scene.png"), Repeat(ExtraColor, 2), 20, 15);
        File.WriteAllLines(Path.Combine(effects, "extra", "effets.tsv"), new[]
        {
            "gfx\tanim\txmin\tymin\tlargeur\thauteur\timages\tips\tfin",
            "5\tscene\t-10\t-80\t20\t15\t2\t40\tboucle"
        });

        // Textes : CRITICAL_MISS et une arme (objet 122, an = 12).
        string lang = Path.Combine(root, "BotLang"); Directory.CreateDirectory(lang);
        File.WriteAllText(Path.Combine(lang, "lang.xml"), "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"lang\" langue=\"fr\" version=\"1\" source=\"test\">\n"
            + "<texte cle=\"CRITICAL_MISS\" valeur=\"" + MissText + "\" />\n</BotLang>\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(lang, "items.xml"), "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"items\" langue=\"fr\" version=\"1\" source=\"test\">\n"
            + "<objet id=\"122\" nom=\"Arme fictive\" type=\"6\" gfx=\"7\" an=\"12\" />\n</BotLang>\n", new UTF8Encoding(false));
        Check(LangData.Load(lang) == 2 && LangData.Text.Has("CRITICAL_MISS"), "Synthetic texts not loaded: " + string.Join(" / ", LangData.LoadWarnings));
        return sprites;
    }

    /// <summary>SpellEffects.Add : un effet qui reprend l'index d'un effet encore affiché le remplace ; refus et gfx invalide.</summary>
    private static void IndexRotation(string spritesDirectory)
    {
        using (var library = new ActorSprites(spritesDirectory))
        using (var set = new MapEffectSet())
        {
            var spellEffects = new SpellEffects();
            Func<IMapEffect, bool> accept = effect => { set.Add(effect); return true; };
            var added = new List<StripEffect>();
            for (int i = 0; i < 22; i++) added.Add(spellEffects.Add(library, set, accept, 9001, new PointF(i, 0), 30, i % 2 == 0, 0));
            Check(added.All(e => e != null) && set.Count == 22 && spellEffects.LastIndex == 0, "22 effects not kept (indexes 1..21 and 0)");
            Check(added[0].Depth == SpellEffects.Depth(30, 1, true) && added[1].Depth == SpellEffects.Depth(30, 2, false) && added[21].Depth == SpellEffects.Depth(30, 0, false),
                "Effect depths do not follow their index");
            StripEffect replacing = spellEffects.Add(library, set, accept, 9001, PointF.Empty, 30, true, 0);
            Check(replacing != null && set.Count == 22 && added[0].Finished && !set.Active.Contains(added[0]) && spellEffects.LastIndex == 1,
                "Effect reusing index 1 did not replace the first one");
            StripEffect refused = spellEffects.Add(library, set, effect => { ((IDisposable)effect).Dispose(); return false; }, 9001, PointF.Empty, 30, true, 0);
            Check(refused == null && set.Count == 21 && added[1].Finished && spellEffects.LastIndex == 2, "Refused effect handling");
            Check(spellEffects.Add(library, set, accept, -1, PointF.Empty, 30, true, 0) == null && spellEffects.Add(null, set, accept, 9001, PointF.Empty, 30, true, 0) == null
                && spellEffects.Add(library, set, accept, 9001, PointF.Empty, -1, true, 0) == null && spellEffects.LastIndex == 2, "Invalid arguments accepted");
        }
    }

    // ------------------------------------------------------------------ outils

    private static void PumpUntil(Func<bool> done)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(10);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("Spell effects loopback timed out"); Application.DoEvents(); Thread.Sleep(5); }
        Application.DoEvents();
    }

    private static void Complete(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }

    private static void Feed(Accounts account, string packet) { Complete(MessagesReception.ReceptionAsync(account.Connexion, packet)); }

    private static string Read(Socket socket)
    {
        PumpUntil(() => socket.Available > 0);
        using (var stream = new MemoryStream())
        {
            byte[] one = new byte[1];
            while (true)
            {
                if (socket.Receive(one) != 1) throw new IOException("Loopback disconnected");
                if (one[0] == 0) return Encoding.UTF8.GetString(stream.ToArray()).TrimEnd('\r', '\n');
                stream.WriteByte(one[0]);
            }
        }
    }

    private static string EncodedCell()
    {
        // Cellule active, praticable, en ligne de vue, sans sol ni objet (aucun PNG de décor demandé).
        int[] value = { 33, 7, 32, 0, 4, 0, 0, 0, 0, 0 };
        return new string(value.Select(part => Hash.caracteres_array[part]).ToArray());
    }

    private static Bitmap Render(UserMapControl view, string name)
    {
        Check(view.WaitForActorSprites(10000), "Sprite decoding did not finish");
        var bitmap = new Bitmap(view.Width, view.Height);
        using (Graphics graphics = Graphics.FromImage(bitmap)) view.DrawCells(graphics);
        bitmap.Save(Path.Combine(TestPaths.Work, name + ".png"), ImageFormat.Png);
        return bitmap;
    }

    private static bool Near(Color pixel, Color expected) =>
        Math.Abs(pixel.R - expected.R) <= 12 && Math.Abs(pixel.G - expected.G) <= 12 && Math.Abs(pixel.B - expected.B) <= 12;

    /// <summary>Pixel de l'image au point <paramref name="world"/> du repère de la carte.</summary>
    private static Color Pixel(Bitmap image, UserMapControl view, PointF world)
    {
        PointF screen = view.EffectView.ToScreen(world);
        return image.GetPixel((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
    }

    private static PointF Foot(UserMapControl view, long id)
    {
        Check(view.TryGetActorAnchor(id, out ActorAnchor anchor), "Actor " + id + " is not drawn");
        return anchor.WorldFoot;
    }

    private static PointF Offset(PointF point, float dx, float dy) => new PointF(point.X + dx, point.Y + dy);

    private static bool Same(PointF a, PointF b) => Math.Abs(a.X - b.X) < 0.01 && Math.Abs(a.Y - b.Y) < 0.01;

    private static UserMapControl.ActorVisualState State(UserMapControl view, int id) => view.GetActorVisualState(id);

    private static StripEffect[] Scenes(MapControl control) => control.Effects.Active.OfType<StripEffect>().ToArray();

    private static ActorAttachedEffect[] Clips(MapControl control, long actor) =>
        control.Effects.Active.OfType<ActorAttachedEffect>().Where(clip => clip.ActorId == actor).ToArray();

    private static void At(MapControl control, double time)
    {
        clock = time;
        control.MapSurface.TickAnimations();
    }

    // ------------------------------------------------------------------ carte en boucle locale

    private static void RunMap(string sprites)
    {
        int count = MapHeight * (2 * MapWidth - 1) - (MapWidth - 1);
        MessagesReception.Init();
        Tool_BotProtocol.Game.Maps.Map.AllBotMaps[900096] = new Tool_BotProtocol.Game.Maps.Map
        {
            MapID = 900096, MapWidth = MapWidth, MapHeight = MapHeight, MapData = string.Concat(Enumerable.Repeat(EncodedCell(), count))
        };
        // Bandes tenues pendant tout le test (bibliothèque partagée par dossier) : prêtes ou absentes dès leur demande par la carte.
        using (var keeper = new ActorSprites(sprites))
        {
            foreach (int direction in new[] { 1, 2, 3, 5, 7 })
            {
                foreach (string anim in new[] { "static", "anim0", "anim1", "anim3", "anim8", "anim12", "anim-1" }) keeper.Resolve(900, direction, false, anim);
                foreach (string anim in new[] { "static", "anim0", "hit" }) keeper.Resolve(902, direction, false, anim);
                foreach (string anim in new[] { "static", "anim0", "anim3", "hit" }) keeper.Resolve(901, direction, false, anim);
            }
            var held = new[]
            {
                keeper.ResolveFixed(SpellEffects.SpellFamily, 9001, SpellEffects.SceneAnimation), keeper.ResolveFixed(SpellEffects.SpellFamily, 9002, SpellEffects.SceneAnimation),
                keeper.ResolveFixed(SpellEffects.SpellFamily, 9999, SpellEffects.SceneAnimation),
                keeper.ResolveFixed(SpellEffects.ExtraFamily, SpellEffects.CriticalHitClip, SpellEffects.SceneAnimation)
            };
            Check(keeper.WaitForPending(10000), "Synthetic strips decoding did not finish");
            Check(held[0].State == SpriteLoadState.Ready && held[1].State == SpriteLoadState.Ready && held[2].State == SpriteLoadState.Missing
                && held[3].State == SpriteLoadState.Ready && held[3].Sheet.End == SpriteEnd.Loop, "Synthetic effect strips not read");
            Check(keeper.Resolve(900, 7, false, "anim8").State == SpriteLoadState.Missing, "anim8 must be missing for the synthetic class");

            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            try
            {
                using (var account = new Accounts(new AccountConfig("synthetic-spells", "synthetic", "loopback")))
                {
                    Task<Socket> accept = listener.AcceptSocketAsync();
                    Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                    using (Socket peer = accept.Result)
                    {
                        peer.ReceiveTimeout = 6000; account.Game.character.id = 42;
                        Feed(account, "GDM|900096|date|"); Check(Read(peer) == "GI", "GDM did not request GI");
                        using (var control = new MapControl(account, () => clock))
                        {
                            UserMapControl view = control.MapSurface;
                            FieldInfo field = typeof(UserMapControl).GetField("sprites", BindingFlags.Instance | BindingFlags.NonPublic);
                            var shipped = (ActorSprites)field.GetValue(view);
                            field.SetValue(view, new ActorSprites(sprites));
                            shipped.Dispose();
                            control.Options = new BotOptions(Path.Combine(TestPaths.Work, "spell-effects", "BotOptions.json")) { AutoSave = false };
                            control.MapShown = () => true;
                            control.Size = new Size(640, 400); view.Size = new Size(640, 400);
                            typeof(UserControl).GetMethod("OnLoad", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { EventArgs.Empty });

                            var failures = new List<string>();
                            control.Sequencer.StepFailed += (step, error) => failures.Add(step.Name + ": " + error);
                            control.Effects.EffectFailed += (effect, error) => failures.Add(effect.GetType().Name + ": " + error);
                            account.Logger.log_event += (message, color) => { if (message.reference == "CARTE" && message.message.StartsWith("Effet visuel", StringComparison.Ordinal)) failures.Add(message.message); };
                            var ignored = new List<string>();
                            account.Logger.debug_event += message => { if (message.message.Contains("illisible")) ignored.Add(message.message); };

                            OutOfFight(account, control, view, failures);
                            Feed(account, "GJK2|1|1|0|30000|0");
                            Feed(account, "GP" + Hash.Get_Cell_Char(SelfCell) + "|" + Hash.Get_Cell_Char(MonsterCell) + Hash.Get_Cell_Char(PlainCell) + "|0");
                            // Personnage du compte en combat : niveau, alignement, couleurs, puis l'arme 0x7a (objet 122, an = 12).
                            Feed(account, "GM|+" + SelfCell + ";1;0;42;Synthetic;1;900^100;0;1;0,0,0,0;-1;-1;-1;7a,,,,;100;8;3");
                            Feed(account, "GS");
                            Feed(account, "GM|+" + MonsterCell + ";1;0;-7;101;-2;902^100;1;-1;-1;-1;0,0,0,0;60;4;2;1"
                                + "|+" + PlainCell + ";1;0;-8;102;-2;901^100;1;-1;-1;-1;0,0,0,0;60;4;2;1"
                                + "|+" + HiddenCell + ";1;0;-9;103;-2;901^0;1;-1;-1;-1;0,0,0,0;60;4;2;1");
                            Feed(account, "GTM|42;0;100;8;3;" + SelfCell + ";;100|-7;0;60;4;2;" + MonsterCell + ";;60|-8;0;60;4;2;" + PlainCell + ";;60|-9;0;60;4;2;" + HiddenCell + ";;60");
                            Feed(account, "GTS42|30000");
                            Check(account.Game.Fight.IsInFight && account.Game.Fight.CurrentActorId == 42, "Synthetic fight did not start on the player's turn");
                            InFight(account, control, view, failures, ignored);
                        }
                    }
                }
            }
            finally
            {
                listener.Stop();
                foreach (ActorSprites.FixedStrip strip in held) strip.Dispose();
            }
        }
    }

    /// <summary>GA501 (anim3 en boucle, étape de la durée pour soi seulement), GA208 et GA228 hors combat.</summary>
    private static void OutOfFight(Accounts account, MapControl control, UserMapControl view, List<string> failures)
    {
        Feed(account, "GM|+" + SelfCell + ";1;0;42;Synthetic;1;900^100;0;0,0,0,0;-1;-1;-1;"
            + "|+" + OtherCell + ";1;0;77;Passant;1;901^100;0;0,0,0,0;-1;-1;-1;");
        const double h = 20000;
        At(control, h);
        using (Bitmap image = Render(view, "bot-spells-0-base"))
            Check(State(view, 42).AnimationName == "staticR" && Near(Pixel(image, view, Offset(Foot(view, 42), 0, -30)), SelfColor), "Player not drawn with staticR");

        // GA501 vers la cellule 31 (au-dessus : direction 7) : anim3 (ToolAnimation hors combat, sans arme) en boucle 1 000 ms.
        Feed(account, "GA;501;42;" + UpCell + ",1000");
        Check(account.Game.character.Orientation == 7, "GA501 did not turn the player towards the object: " + account.Game.character.Orientation);
        UserMapControl.ActorVisualState state = State(view, 42);
        Check(state.Animation == "anim3" && state.AnimationName == "anim3L" && state.IsMirrored && state.Frame == 0, "GA501 does not play anim3: " + state.AnimationName);
        Check(control.Sequencer.Count(42) == 1, "GA501 of the player must hold its queue for the duration");
        At(control, h + 150);
        using (Bitmap image = Render(view, "bot-spells-1-harvest"))
            Check(State(view, 42).Frame == 2 && Near(Pixel(image, view, Offset(Foot(view, 42), 0, -30)), Anim3Colors[2]), "anim3 frame 2 not drawn at t + 150 (loop)");
        At(control, h + 250);
        Check(State(view, 42).Animation == "anim3" && State(view, 42).Frame == 2, "anim3 does not loop after its 4 frames");
        At(control, h + 999);
        Check(State(view, 42).Animation == "anim3" && control.Sequencer.Count(42) == 1, "GA501 ended before its duration");
        At(control, h + 1000);
        Check(State(view, 42).AnimationName == "staticL" && control.Sequencer.IsIdle, "GA501 not over after exactly 1 000 ms: " + State(view, 42).AnimationName);

        // GA501 d'un autre joueur : direction, étape non bloquante ; gfx sans anim3 : rien n'est inventé.
        Feed(account, "GA;501;77;45,500");
        Check(control.Sequencer.Count(77) == 0 && !control.AnimationQueue.IsPlaying(77) && account.Game.Map.GetActor(77).Orientation == 3,
            "GA501 of another player held the queue, played a missing strip or did not turn it");

        // Troisième champ : anim<n> à la place de ToolAnimation ; illisible, aucune animation mais la durée est tenue.
        Feed(account, "GA;501;42;" + UpCell + ",300,../x");
        Check(!control.AnimationQueue.IsPlaying(42) && control.Sequencer.Count(42) == 1, "Unreadable GA501 animation played or did not hold the queue");
        At(control, h + 1300);
        Check(control.Sequencer.IsIdle, "GA501 with an unreadable animation did not last 300 ms");
        Feed(account, "GA;501;42;" + UpCell + ",200,12");
        Check(State(view, 42).Animation == "anim12", "GA501 third field not played as anim12");
        At(control, h + 1500);
        Check(control.Sequencer.IsIdle && State(view, 42).Animation == "static", "GA501 with anim12 not over after 200 ms");

        // GA208 : anim8 non exportée (étape sautée), effet aussitôt devant, au centre de la cellule.
        At(control, h + 2000);
        Feed(account, "GA;208;42;" + UpCell + ",9001,11,8,1");
        At(control, h + 2000);
        StripEffect balloon = Scenes(control).SingleOrDefault();
        Check(balloon != null && control.Sequencer.IsIdle, "GA208 effect not added at once (anim8 is not exported)");
        Check(view.EffectView.TryGetCellCenter(UpCell, out PointF up) && Same(balloon.World, up), "GA208 effect not at the cell center");
        Check(balloon.Depth == SpellEffects.Depth(UpCell, control.SpellEffects.LastIndex, true), "GA208 effect is not in front of the sprite: " + balloon.Depth);
        At(control, h + 2050);
        // La cellule 31 est derrière celle du joueur : son sprite (cellule 46, profondeur plus grande) recouvre le bas de la scène.
        using (Bitmap image = Render(view, "bot-spells-2-balloon"))
            Check(Near(Pixel(image, view, Offset(up, 12.5f, -30)), SceneColors[2]) && Near(Pixel(image, view, Offset(up, 0, -25)), SelfColor),
                "GA208 scene frame 2 not drawn at t + 50 behind the player's sprite");
        At(control, h + 2101);
        Check(control.Effects.Count == 0, "GA208 effect not removed after its 4 frames");

        // GA228 type 10 sur sa propre cellule : effet au pied, direction inchangée ; « -1 » n'est pas un code pour 228.
        Feed(account, "GA;228;42;" + SelfCell + ",9002,10,-1");
        At(control, h + 2101);
        StripEffect firework = Scenes(control).SingleOrDefault();
        Check(firework != null && Same(firework.World, Foot(view, 42)) && account.Game.character.Orientation == 7, "GA228 type 10 not at the caster's foot");
        At(control, h + 2300);
        Check(control.Effects.Count == 0 && control.Sequencer.IsIdle && failures.Count == 0, "Out of fight effects: " + string.Join(" | ", failures));
    }

    private static void InFight(Accounts account, MapControl control, UserMapControl view, List<string> failures, List<string> ignored)
    {
        account.Game.character.Orientation = 1;
        At(control, 30000);
        using (Bitmap baseline = Render(view, "bot-spells-3-fight"))
        {
            Check(State(view, -7).HasSprite && State(view, -7).AnimationName == "staticR" && State(view, 42).AnimationName == "staticR", "Fighters not drawn");
            Check(Near(Pixel(baseline, view, Offset(Foot(view, -7), 0, -30)), MonsterColor), "Monster body not where expected");
        }
        PointF monster = Foot(view, -7), self = Foot(view, 42);
        PointF overlap = Offset(monster, 0, -30), aside = Offset(monster, 12.5f, -30);

        // GA300 type 11 devant : direction, anim0 (8 images, 200 ms) bloquante, puis la scène sur la cellule visée.
        account.Game.character.Orientation = 5;
        const double f1 = 40000;
        At(control, f1);
        Feed(account, "GA;300;42;161," + MonsterCell + ",9001,1,11,0,1");
        Check(account.Game.character.Orientation == 1, "GA300 did not turn the caster towards the cell");
        Check(State(view, 42).Animation == "anim0" && State(view, 42).AnimationName == "anim0R" && State(view, 42).Frame == 0 && control.Effects.Count == 0
            && control.Sequencer.Count(42) == 2, "GA300 does not start with anim0 (steps " + control.Sequencer.Count(42) + ")");
        At(control, f1 + 199);
        Check(State(view, 42).Frame == 7 && control.Effects.Count == 0, "Effect added before the end of anim0");
        At(control, f1 + 200);
        StripEffect front = Scenes(control).SingleOrDefault();
        Check(front != null && front.ShownAt == f1 + 200 && front.Frame == 0 && control.Sequencer.IsIdle && State(view, 42).AnimationName == "staticR",
            "Effect not shown at the end of anim0");
        Check(Same(front.World, monster) && front.Depth == SpellEffects.Depth(MonsterCell, control.SpellEffects.LastIndex, true), "Type 11 effect position or depth");
        using (Bitmap image = Render(view, "bot-spells-4-front-t200"))
            Check(Near(Pixel(image, view, overlap), SceneColors[0]) && Near(Pixel(image, view, aside), SceneColors[0]), "Effect frame 0 not drawn over the monster at t + 200");
        At(control, f1 + 250);
        using (Bitmap image = Render(view, "bot-spells-5-front-t250"))
            Check(front.Frame == 2 && Near(Pixel(image, view, overlap), SceneColors[2]), "Effect frame 2 not drawn at t + 250");
        At(control, f1 + 299);
        Check(control.Effects.Count == 1, "Effect removed before its last frame");
        At(control, f1 + 301);
        using (Bitmap image = Render(view, "bot-spells-6-gone"))
            Check(control.Effects.Count == 0 && Near(Pixel(image, view, overlap), MonsterColor), "Effect still drawn at t + 301");

        // Derrière (devant = 0) : le monstre recouvre la scène, visible seulement autour de lui.
        const double f2 = 42000;
        At(control, f2);
        Feed(account, "GA;300;42;161," + MonsterCell + ",9001,1,11,0,0");
        At(control, f2 + 200);
        StripEffect behind = Scenes(control).Single();
        Check(behind.Depth == SpellEffects.Depth(MonsterCell, control.SpellEffects.LastIndex, false), "Behind effect depth: " + behind.Depth);
        using (Bitmap image = Render(view, "bot-spells-7-behind"))
            Check(Near(Pixel(image, view, overlap), MonsterColor) && Near(Pixel(image, view, aside), SceneColors[0]), "Behind effect not covered by the monster");
        At(control, f2 + 400);

        // anim1 de 60 images (1 500 ms) : l'effet part à 1 000 ms ; type 10 : au pied du lanceur.
        const double f3 = 44000;
        At(control, f3);
        Feed(account, "GA;300;42;161," + MonsterCell + ",9001,1,10,1,1");
        At(control, f3 + 999);
        Check(control.Effects.Count == 0 && State(view, 42).Animation == "anim1", "Effect added before 1 000 ms of a long animation");
        At(control, f3 + 1000);
        StripEffect atCaster = Scenes(control).SingleOrDefault();
        Check(atCaster != null && Same(atCaster.World, self) && State(view, 42).Animation == "anim1", "Type 10 effect not at the caster at 1 000 ms");
        using (Bitmap image = Render(view, "bot-spells-8-type10"))
            Check(Near(Pixel(image, view, Offset(self, 12.5f, -30)), SceneColors[0]), "Type 10 effect not drawn at the caster");
        At(control, f3 + 1500);
        Check(State(view, 42).AnimationName == "staticR" && control.Effects.Count == 0, "anim1 or its effect did not end");

        // Type 12 sans animation (-2) : la file du lanceur (joueur du tour) attend la fin de la scène (150 ms) avant le GA100.
        const double f4 = 47000;
        At(control, f4);
        Feed(account, "GA;300;42;161," + MonsterCell + ",9002,1,12,-2,1");
        Feed(account, "GA;100;42;-7,-5");
        At(control, f4);
        Check(Scenes(control).Length == 1 && !control.AnimationQueue.IsPlaying(42) && control.Points == null, "Type 12 did not start at once or GA100 did not wait");
        At(control, f4 + 149);
        Check(control.Points == null, "GA100 shown before the end of the type 12 effect");
        At(control, f4 + 150);
        Check(control.Points == null && control.Effects.Count == 0, "Type 12 effect not over at 150 ms");
        At(control, f4 + 151);
        Check(control.Points != null && control.Points.Numbers.Any(n => n.ActorId == -7 && n.Text == "-5"), "GA100 not shown after the type 12 effect");
        At(control, f4 + 1000);

        // anim -1 : rien du tout, pas même la direction.
        account.Game.character.Orientation = 5;
        Feed(account, "GA;300;42;161," + MonsterCell + ",9001,1,11,-1,1");
        Check(control.Sequencer.IsIdle && control.Effects.Count == 0 && !control.AnimationQueue.IsPlaying(42) && account.Game.character.Orientation == 5,
            "anim -1 showed something");

        // Lanceur invisible (taille 0) : ni animation ni effet.
        Feed(account, "GA;300;-9;161," + MonsterCell + ",9001,1,11,0,1");
        At(control, f4 + 2000);
        Check(control.Effects.Count == 0, "Effect of an invisible caster");

        // GA301 : « ! » au-dessus du personnage du compte en staticF, 5 000 ms ; caché dans une autre pose.
        account.Game.character.Orientation = 2;
        const double f6 = 50000;
        At(control, f6);
        Feed(account, "GA;301;42;161");
        At(control, f6);
        ActorAttachedEffect clip = Clips(control, 42).SingleOrDefault();
        Check(clip != null && clip.Above && clip.ShownAt == f6, "GA301 clip not added at once");
        PointF head = Offset(self, 0, -72);
        using (Bitmap image = Render(view, "bot-spells-9-critical"))
            Check(State(view, 42).AnimationName == "staticF" && clip.Visible && Near(Pixel(image, view, head), ExtraColor), "Critical clip not drawn above the player in staticF");
        At(control, f6 + 2000);
        account.Game.character.Orientation = 1;
        using (Bitmap image = Render(view, "bot-spells-10-critical-hidden"))
            Check(!clip.Visible && !Near(Pixel(image, view, head), ExtraColor), "Critical clip drawn on a player outside staticF");
        account.Game.character.Orientation = 2;
        At(control, f6 + 4900);
        using (Bitmap image = Render(view, "bot-spells-11-critical-4900"))
            Check(Clips(control, 42).Length == 1 && clip.Visible && Near(Pixel(image, view, head), ExtraColor), "Critical clip gone before 5 000 ms");
        At(control, f6 + 5100);
        using (Bitmap image = Render(view, "bot-spells-12-critical-5100"))
            Check(Clips(control, 42).Length == 0 && !Near(Pixel(image, view, head), ExtraColor), "Critical clip still drawn at t + 5 100");

        // GA304 sur un monstre : toujours visible ; un second remplace le premier.
        const double f7 = 57000;
        At(control, f7);
        Feed(account, "GA;304;-7;");
        At(control, f7);
        ActorAttachedEffect first = Clips(control, -7).Single();
        using (Bitmap image = Render(view, "bot-spells-13-critical-monster"))
            Check(first.Visible && Near(Pixel(image, view, Offset(monster, 0, -72)), ExtraColor), "GA304 clip not drawn above the monster");
        At(control, f7 + 1000);
        Feed(account, "GA;304;-7;");
        At(control, f7 + 1000);
        Check(Clips(control, -7).Length == 1 && first.Finished && Clips(control, -7)[0] != first, "Second GA304 did not replace the first clip");
        At(control, f7 + 7000);

        // GA303 du joueur (arme 122, an = 12) puis du monstre sans arme (anim0 en combat), dans la file du joueur du tour.
        const double f9 = 66000;
        At(control, f9);
        Feed(account, "GA;303;42;" + MonsterCell);
        Feed(account, "GA;303;-7;" + SelfCell);
        Check(account.Game.character.Orientation == 1 && State(view, 42).Animation == "anim12" && State(view, 42).AnimationName == "anim12R",
            "GA303 of the player does not play anim12 (an of its weapon): " + State(view, 42).AnimationName);
        Check(State(view, -7).AnimationName == "staticR" && control.Sequencer.Count(42) == 3, "Monster GA303 did not wait for the player's attack");
        At(control, f9 + 100);
        UserMapControl.ActorVisualState attacker = State(view, -7);
        Check(attacker.Animation == "anim0" && attacker.AnimationName == "anim0R" && attacker.IsMirrored && attacker.Orientation == 3,
            "GA303 of the monster does not play anim0 turned towards the player: " + attacker.AnimationName + " " + attacker.Orientation);
        At(control, f9 + 200);
        Check(control.Sequencer.IsIdle && State(view, -7).Animation == "static", "GA303 steps not over");

        // GA303 avec fichier : comme un sort animé par ToolAnimation.
        const double f10 = 68000;
        At(control, f10);
        Feed(account, "GA;303;42;" + MonsterCell + ",9001,11,1");
        Check(State(view, 42).Animation == "anim12" && control.Effects.Count == 0, "GA303 with a file does not start with anim12: "
            + State(view, 42).AnimationName + ", effects " + string.Join(",", control.Effects.Active.Select(e => e.GetType().Name)) + ", "
            + control.Sequencer.Count(42) + " step(s)");
        At(control, f10 + 100);
        Check(Scenes(control).Length == 1 && Same(Scenes(control)[0].World, monster), "GA303 with a file did not add its effect after anim12");
        At(control, f10 + 500);

        // GA302 : bulle CRITICAL_MISS du lang.
        Feed(account, "GA;302;42;161");
        Check(view.BubbleOf(42) != null && view.BubbleOf(42).Text == MissText, "GA302 bubble missing or wrong: " + view.BubbleOf(42)?.Text);

        // Carte cachée : aucune file, aucun effet, aucune bulle.
        At(control, f10 + 20000);
        SpeechBubble before = view.BubbleOf(42);
        control.MapShown = () => false;
        foreach (string packet in new[] { "GA;300;42;161," + MonsterCell + ",9001,1,11,0,1", "GA;301;42;161", "GA;302;42;161", "GA;303;42;" + MonsterCell, "GA;304;-7;" })
            Feed(account, packet);
        Check(control.Sequencer.IsIdle && control.Effects.Count == 0 && control.AnimationQueue.Count == 0 && view.BubbleOf(42) == before, "Hidden map kept a spell effect");
        control.MapShown = () => true;

        // Paquets mal formés, lanceur inconnu, fichier absent, type inconnu : rien ne casse.
        At(control, f10 + 30000);
        int ignoredBefore = ignored.Count;
        foreach (string packet in new[] { "GA;300;42;x", "GA;300;42;161", "GA;300;42;161,x,9001,1,11,0,1", "GA;300;999;161," + MonsterCell + ",9001,1,11,0,1",
            "GA;300;42;161," + MonsterCell + ",abc,1,11,-2,1", "GA;300;42;161," + MonsterCell + ",9001,1,99,-2,1", "GA;300;42;161," + MonsterCell + ",9999,1,12,-2,1",
            "GA;300;42;161," + MonsterCell + ",9001,1,11,~~,1", "GA;303;42;x", "GA;303;42;", "GA;301;999;1", "GA;302;999;1", "GA;501;42;x" })
            Feed(account, packet);
        control.ShowVisual(new VisualEvent(VisualSource.GameAction, 300, 42, 0, -1, null));
        control.ShowVisual(new VisualEvent(VisualSource.GameAction, 501, 42, 0, -1, new[] { "x" }));
        control.ShowVisual(new VisualEvent(VisualSource.GameAction, 501, 42, 0, SelfCell, new[] { SelfCell.ToString() }));
        control.ShowVisual(new VisualEvent(VisualSource.GameAction, 208, 42, 0, -1, new[] { "", "9001" }));
        control.ShowVisual(new VisualEvent(VisualSource.GameAction, 303, 999, 0, SelfCell, new[] { SelfCell.ToString() }));
        // Fichier 9999 absent (type 12) : l'effet est sauté à sa première mise à jour et libère la file au tick suivant,
        // puis le bond sans animation (~~) pose sa scène.
        At(control, f10 + 30001);
        At(control, f10 + 30002);
        Check(Scenes(control).Length == 1 && Scenes(control)[0].Depth == SpellEffects.Depth(MonsterCell, control.SpellEffects.LastIndex, true),
            "Missing type 12 file did not release the queue, or the jump without animation lost its effect");
        At(control, f10 + 31500);
        Check(failures.Count == 0, "Visual step, effect or family failed: " + string.Join(" | ", failures));
        Check(ignored.Count > ignoredBefore, "Unreadable spell packets were not logged");
        Check(control.Sequencer.IsIdle && control.Effects.Count == 0, "Malformed packets left steps or effects");

        // Sans lang.xml : texte de repli (GA305).
        LangData.Clear();
        Feed(account, "GA;305;42;");
        Check(view.BubbleOf(42) != null && view.BubbleOf(42).Text == MapControl.CriticalMissFallback, "GA305 fallback bubble: " + view.BubbleOf(42)?.Text);
    }
}
