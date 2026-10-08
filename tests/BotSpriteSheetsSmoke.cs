using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

// Vérifie les sprites d'acteurs exportés du client 1.34 par tools/client-analysis/exporter_sprites.py
// (Resources/Bot/sprites) : syntaxe d'ancres.tsv, PNG présents et de la taille annoncée, bandes de
// marche/course des gfx de sprites_animes.txt, anciens <gfx><O>.png conservés, PROVENANCE.md qui
// cite la commande, et copie d'ancres.tsv à côté de l'exécutable. Fichiers seulement : passe sous Mono.
// Lot AN1 : 7 ou 9 colonnes (ips, fin), noms hit/die/anim<n>/emote<n>/emoteStatic<n>/bonus en R ou L, bandes en grille
// (colonnes = largeur du PNG / largeur d'image), aucun côté de PNG au-delà de 32 767 px.
// Lot AN2 : sprites_animes.txt au format « <gfx> <famille>[:<pas>],... » (un gfx seul vaut walk,run), chaque
// famille demandée a ses bandes, hit et die des 24 classes en R et L, part du budget (4 Mo).
internal static class BotSpriteSheetsSmoke
{
    private static readonly string[] Header = { "gfx", "anim", "xmin", "ymin", "largeur", "hauteur", "images" };
    private static readonly string[] LongHeader = Header.Concat(new[] { "ips", "fin" }).ToArray();
    private static readonly Regex Anim = new Regex("^(?:(?:static|walk|run)[SRLFB]|(?:hit|die|anim[0-9]+|emote[0-9]+|emote[Ss]tatic[0-9]+|bonus)[RL]|scene)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex End = new Regex("^(?:boucle|arret|static|suite:[A-Za-z0-9]+)$", RegexOptions.CultureInvariant);
    private const int MaxSide = 32767;
    // Part du lot AN2 dans le budget des animations (plan, section 4) : hit et die des 24 classes, 3,5 Mo mesurés.
    private const long HitDieBudget = 4L * 1024 * 1024;
    private static readonly Regex AnimatedLine = new Regex("^([0-9]+)(?:[ \\t]+([A-Za-z][A-Za-z0-9]*(?::[0-9]+)?(?:,[A-Za-z][A-Za-z0-9]*(?::[0-9]+)?)*))?$",
        RegexOptions.CultureInvariant);

    private sealed class Row
    {
        public int Gfx; public string Anim; public int XMin, YMin, Width, Height, Images; public int Line;
        public int Fps = 40; public string End = "boucle";
        public string File { get { return Gfx + "_" + Anim + ".png"; } }
    }

    [STAThread]
    private static void Main()
    {
        try { Run(); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

    /// <summary>
    /// Lecture stricte d'ancres.tsv : en-tête à sept colonnes, ou à neuf avec ips (entier de 1 à 1000) et fin (boucle, arret,
    /// static, suite:&lt;anim&gt;), même nombre de colonnes sur chaque ligne, entiers (sauf anim), clé gfx/anim unique.
    /// </summary>
    private static List<Row> Parse(string[] lines)
    {
        bool wide = lines.Length > 0 && lines[0] == string.Join("\t", LongHeader);
        Check(lines.Length > 0 && (wide || lines[0] == string.Join("\t", Header)),
            "ancres.tsv header must be: " + string.Join(" ", Header) + " [ips fin]");
        int columns = wide ? LongHeader.Length : Header.Length;
        var rows = new List<Row>(); var keys = new HashSet<string>();
        for (int i = 1; i < lines.Length; i++)
        {
            string[] c = lines[i].Split('\t');
            Check(c.Length == columns, "ancres.tsv line " + (i + 1) + ": " + c.Length + " columns, header has " + columns);
            var row = new Row { Anim = c[1], Line = i + 1 };
            int[] values = new int[6];
            for (int k = 0; k < 6; k++)
                Check(int.TryParse(c[k == 0 ? 0 : k + 1], System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out values[k]),
                    "ancres.tsv line " + (i + 1) + ": column " + Header[k == 0 ? 0 : k + 1] + " is not an integer: " + lines[i]);
            row.Gfx = values[0]; row.XMin = values[1]; row.YMin = values[2]; row.Width = values[3]; row.Height = values[4]; row.Images = values[5];
            Check(row.Gfx >= 0, "ancres.tsv line " + row.Line + ": negative gfx");
            Check(Anim.IsMatch(row.Anim), "ancres.tsv line " + row.Line + ": unknown animation " + row.Anim);
            Check(row.Width > 0 && row.Height > 0 && row.Images > 0, "ancres.tsv line " + row.Line + ": empty frame");
            if (wide)
            {
                Check(int.TryParse(c[7], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out row.Fps)
                    && row.Fps >= 1 && row.Fps <= 1000, "ancres.tsv line " + row.Line + ": ips must be an integer from 1 to 1000: " + c[7]);
                Check(End.IsMatch(c[8]), "ancres.tsv line " + row.Line + ": unknown fin " + c[8]);
                row.End = c[8];
            }
            Check(row.Images == 1 || !(row.Anim.StartsWith("static") || row.Anim == "scene"), "ancres.tsv line " + row.Line + ": a static image has one frame");
            Check(keys.Add(row.Gfx + "/" + row.Anim), "ancres.tsv line " + row.Line + ": duplicate " + row.Gfx + "/" + row.Anim);
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>
    /// Lit sprites_animes.txt : « <gfx> [<famille>[:<pas>],...] », # pour un commentaire ; un gfx seul vaut walk,run
    /// (format d'origine). Le pas divise 40 (ips = 40 / pas). Rend gfx → (famille → ips).
    /// </summary>
    private static Dictionary<int, Dictionary<string, int>> ParseAnimated(IEnumerable<string> lines)
    {
        var result = new Dictionary<int, Dictionary<string, int>>();
        int number = 0;
        foreach (string raw in lines)
        {
            number++;
            string line = (raw.IndexOf('#') >= 0 ? raw.Substring(0, raw.IndexOf('#')) : raw).Trim();
            if (line.Length == 0) continue;
            Match m = AnimatedLine.Match(line);
            Check(m.Success, "sprites_animes.txt line " + number + ": expected « <gfx> <famille>[:<pas>],... », got " + line);
            int gfx = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            Check(!result.ContainsKey(gfx), "sprites_animes.txt line " + number + ": gfx " + gfx + " listed twice");
            var families = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string part in (m.Groups[2].Success ? m.Groups[2].Value : "walk,run").Split(','))
            {
                string[] p = part.Split(':');
                int step = p.Length > 1 ? int.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture) : 1;
                Check(step >= 1 && 40 % step == 0, "sprites_animes.txt line " + number + ": step of " + part + " does not divide 40");
                Check(!families.ContainsKey(p[0]), "sprites_animes.txt line " + number + ": family " + p[0] + " listed twice");
                families[p[0]] = 40 / step;
            }
            result[gfx] = families;
        }
        return result;
    }

    private static string Family(string anim)
    {
        return anim == "scene" ? "scene" : anim.Substring(0, anim.Length - 1);
    }

    private static bool Rejected(params string[] lines)
    {
        try { Parse(lines); return false; } catch (Exception) { return true; }
    }

    /// <summary>
    /// Taille de PNG admise pour une ligne : bande d'une ligne (largeur × images), ou grille de colonnes = largeur du PNG /
    /// largeur d'image, lignes juste suffisantes ; jamais plus de 32 767 px de côté.
    /// </summary>
    private static string SizeError(Row row, Size size)
    {
        if (size.Width > MaxSide || size.Height > MaxSide) return row.File + " is " + size + ": a PNG side above " + MaxSide + " px crashes libgdiplus, export a grid";
        if (size.Width == row.Width * row.Images && size.Height == row.Height) return null;
        if (size.Width < row.Width || size.Width % row.Width != 0) return row.File + " is " + size + ", not a whole number of " + row.Width + " px frames";
        int columns = size.Width / row.Width, lines = (row.Images + columns - 1) / columns;
        if (lines < 2 || columns >= row.Images) return row.File + " is " + size + ", ancres.tsv says " + row.Width + "x" + row.Images + " by " + row.Height;
        return size.Height == row.Height * lines ? null
            : row.File + " is a " + columns + "-column grid of " + size + ", expected " + lines + " lines of " + row.Height + " px";
    }

    /// <summary>Taille d'un PNG lue dans l'en-tête IHDR, sans décoder l'image.</summary>
    private static Size PngSize(string path)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            byte[] head = new byte[24];
            Check(stream.Read(head, 0, 24) == 24 && head[1] == 'P' && head[2] == 'N' && head[3] == 'G' && head[12] == 'I' && head[15] == 'R', "Not a PNG: " + path);
            Func<int, int> be = o => (head[o] << 24) | (head[o + 1] << 16) | (head[o + 2] << 8) | head[o + 3];
            return new Size(be(16), be(20));
        }
    }

    /// <summary>Chargement comme le bot : copie 32 bpp ARGB, fichier libéré.</summary>
    private static Bitmap Load(string path)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var source = Image.FromStream(stream)) return new Bitmap(source);
    }

    private static bool Opaque(Bitmap image, int x0, int y0, int x1, int y1)
    {
        for (int y = Math.Max(0, y0); y < Math.Min(image.Height, y1); y++)
            for (int x = Math.Max(0, x0); x < Math.Min(image.Width, x1); x++)
                if (image.GetPixel(x, y).A > 0) return true;
        return false;
    }

    private static void Run()
    {
        // 1. Le lecteur strict refuse les lignes mal formées (fichiers synthétiques, aucun PNG).
        string header = string.Join("\t", Header);
        Check(Parse(new[] { header, "10\tstaticR\t-8\t-39\t16\t43\t1", "10\twalkR\t-8\t-37\t21\t43\t30" }).Count == 2, "A valid ancres.tsv is rejected");
        Check(Rejected("gfx\tanim\txmin", "10\tstaticR\t-8\t-39\t16\t43\t1"), "Wrong header accepted");
        Check(Rejected(header, "10\tstaticR\t-8\t-39\t16\t43"), "Six columns accepted");
        Check(Rejected(header, "10\tstaticR\t-8.5\t-39\t16\t43\t1"), "Decimal anchor accepted");
        Check(Rejected(header, "10\tstaticX\t-8\t-39\t16\t43\t1"), "Unknown orientation accepted");
        Check(Rejected(header, "10\tstaticR\t-8\t-39\t16\t43\t3"), "Static strip accepted");
        Check(Rejected(header, "10\tstaticR\t-8\t-39\t16\t43\t1", "10\tstaticR\t-8\t-39\t16\t43\t1"), "Duplicate gfx/anim accepted");
        Check(Rejected(header, "10\twalkR\t-8\t-37\t0\t43\t30"), "Empty frame accepted");
        // Lot AN1 : colonnes ips et fin, nouveaux noms, grille.
        string wideHeader = string.Join("\t", LongHeader);
        List<Row> wide = Parse(new[] { wideHeader, "10\tstaticR\t-8\t-39\t16\t43\t1\t40\tboucle", "10\thitR\t-8\t-39\t30\t45\t12\t40\tstatic",
            "10\tdieL\t-8\t-39\t30\t45\t20\t20\tarret", "10\temote1R\t-8\t-39\t30\t45\t9\t40\tsuite:emoteStatic1",
            "10\temoteStatic1R\t-8\t-39\t30\t45\t4\t40\tboucle", "10\tanim0L\t-8\t-39\t30\t45\t15\t40\tstatic", "10\tbonusR\t-8\t-39\t30\t45\t6\t40\tstatic" });
        Check(wide.Count == 7 && wide[2].Fps == 20 && wide[2].End == "arret", "A valid nine-column ancres.tsv is rejected or misread");
        foreach (Row row in wide)
        {
            Outil_Azur_complet.Bot.Controls.SpriteEnd end; string next;
            Check(Outil_Azur_complet.Bot.Controls.ActorSprites.TryParseEnd(row.End, out end, out next), "The bot does not read fin " + row.End);
        }
        Check(Rejected(wideHeader, "10\tstaticR\t-8\t-39\t16\t43\t1"), "Seven columns accepted under a nine-column header");
        Check(Rejected(header, "10\tstaticR\t-8\t-39\t16\t43\t1\t40\tboucle"), "Nine columns accepted under a seven-column header");
        Check(Rejected(wideHeader, "10\thitR\t-8\t-39\t30\t45\t12\t0\tstatic"), "Zero ips accepted");
        Check(Rejected(wideHeader, "10\thitR\t-8\t-39\t30\t45\t12\t40\tfige"), "Unknown fin accepted");
        Check(Rejected(header, "10\thitF\t-8\t-39\t30\t45\t12"), "hit outside R/L accepted (the bot uses direction | 1)");
        Check(Rejected(header, "10\tsceneR\t-8\t-39\t30\t45\t1"), "Oriented scene accepted");
        var grid = new Row { Gfx = 10, Anim = "dieR", Width = 20, Height = 30, Images = 5 };
        Check(SizeError(grid, new Size(100, 30)) == null, "One-line strip refused");
        Check(SizeError(grid, new Size(60, 60)) == null, "Two-line grid (3 columns) refused");
        Check(SizeError(grid, new Size(60, 30)) != null && SizeError(grid, new Size(60, 90)) != null && SizeError(grid, new Size(50, 60)) != null,
            "Inconsistent grid accepted");
        var wideStrip = new Row { Gfx = 10, Anim = "anim0R", Width = 400, Height = 300, Images = 100 };
        Check(SizeError(wideStrip, new Size(40000, 300)) != null, "A 40 000 px line accepted");
        Check(SizeError(wideStrip, new Size(32400, 600)) == null, "Grid under 32 767 px refused");

        // 2. Ancres livrées avec le dépôt.
        string source = Path.GetFullPath(Path.Combine(TestPaths.ApplicationBin, "..", "..", "Resources", "Bot", "sprites"));
        Check(Directory.Exists(source), "Sprite folder missing: " + source);
        string anchors = Path.Combine(source, "ancres.tsv");
        Check(File.Exists(anchors), "ancres.tsv missing: " + anchors);
        string[] lines = File.ReadAllLines(anchors).Where(l => l.Length > 0).ToArray();
        List<Row> rows = Parse(lines);
        Check(rows.Count(r => r.Anim.StartsWith("static")) >= 2000, "Too few static images: " + rows.Count(r => r.Anim.StartsWith("static")));
        var byKey = rows.ToDictionary(r => r.Gfx + "/" + r.Anim);
        foreach (int sword in new[] { 0, 1, 2, 3, 4 })
            Check(byKey.ContainsKey(sword + "/scene"), "Fight sword " + sword + " (scene of clips/sprites/" + sword + ".swf) missing");
        foreach (string o in new[] { "R", "L" }) Check(byKey.ContainsKey("6000/static" + o), "Tax collector 6000 static" + o + " missing");

        // 3. Bandes : format de sprites_animes.txt (AN2) ; chaque famille demandée a au moins une bande, à son ips ;
        // les 24 classes ont walk et run dans les cinq orientations, hit et die en R et L.
        var parsed = ParseAnimated(new[] { "# commentaire", "10", "11 walk,run,hit,die:2   # fin de ligne", "", "12 anim0:4" });
        Check(parsed.Count == 3 && parsed[10].Count == 2 && parsed[10]["walk"] == 40 && parsed[10]["run"] == 40
            && parsed[11].Count == 4 && parsed[11]["die"] == 20 && parsed[12]["anim0"] == 10, "sprites_animes.txt sample misread");
        foreach (string bad in new[] { "10 walk run", "10 walk:3", "10 walk,walk", "x walk", "10 2walk", "10 walk," })
        {
            bool refused = false;
            try { ParseAnimated(new[] { bad }); } catch (Exception) { refused = true; }
            Check(refused, "sprites_animes.txt line accepted: " + bad);
        }
        bool twice = false;
        try { ParseAnimated(new[] { "10", "10 hit" }); } catch (Exception) { twice = true; }
        Check(twice, "sprites_animes.txt: a gfx listed twice is accepted");

        Dictionary<int, Dictionary<string, int>> animated = ParseAnimated(File.ReadAllLines(Path.Combine(source, "sprites_animes.txt")));
        int[] classes = Enumerable.Range(1, 12).SelectMany(c => new[] { c * 10, c * 10 + 1 }).ToArray();
        Check(classes.All(c => animated.ContainsKey(c)), "sprites_animes.txt must list the 24 class gfx: " + string.Join(",", animated.Keys));
        foreach (var entry in animated)
            foreach (var family in entry.Value)
            {
                Row[] strips = rows.Where(r => r.Gfx == entry.Key && r.Images > 1 && Family(r.Anim) == family.Key).ToArray();
                Check(strips.Length > 0, "No " + family.Key + " strip for animated gfx " + entry.Key);
                foreach (Row strip in strips)
                    Check(strip.Fps == family.Value, "Strip " + strip.File + " plays at " + strip.Fps + " fps, sprites_animes.txt asks " + family.Value);
            }
        foreach (int gfx in classes)
        {
            foreach (string family in new[] { "walk", "run", "hit", "die" })
                Check(animated[gfx].ContainsKey(family), "sprites_animes.txt: class gfx " + gfx + " lacks " + family);
            foreach (string cycle in new[] { "walk", "run" })
                foreach (char o in "SRLFB")
                {
                    Row strip;
                    Check(byKey.TryGetValue(gfx + "/" + cycle + o, out strip), "Missing strip " + gfx + "_" + cycle + o);
                    Check(strip.Images >= 8, "Strip " + strip.File + " has only " + strip.Images + " frames");
                }
            foreach (char o in "RL")
            {
                // hit : 24 images (600 ms) chez le client, retour à la pose de repos ; die : au moins 25 images, tenue ou boucle.
                Row hit, die;
                Check(byKey.TryGetValue(gfx + "/hit" + o, out hit), "Missing strip " + gfx + "_hit" + o);
                Check(hit.Images >= 20 && hit.End == "static", "Strip " + hit.File + ": " + hit.Images + " frames, fin " + hit.End + " (expected about 24, static)");
                Check(byKey.TryGetValue(gfx + "/die" + o, out die), "Missing strip " + gfx + "_die" + o);
                Check(die.Images >= 20 && (die.End == "arret" || die.End == "boucle"), "Strip " + die.File + ": " + die.Images + " frames, fin " + die.End);
            }
        }
        Check(rows.Where(r => r.Images > 1).All(r => animated.ContainsKey(r.Gfx) && animated[r.Gfx].ContainsKey(Family(r.Anim))),
            "A strip belongs to a gfx or a family not listed in sprites_animes.txt");

        // 4. PNG : présents et de la taille annoncée (largeur x images, hauteur), si le dossier en contient.
        bool shippedPng = Directory.GetFiles(source, "*_static?.png").Length > 0;
        if (!shippedPng) Console.WriteLine("NOTE: no <gfx>_static<O>.png in " + source + " (PNG not versioned here): file checks skipped");
        else
        {
            foreach (Row row in rows)
            {
                string path = Path.Combine(source, row.File);
                Check(File.Exists(path), "ancres.tsv line " + row.Line + " points to a missing PNG: " + row.File);
                string error = SizeError(row, PngSize(path));
                Check(error == null, "ancres.tsv line " + row.Line + ": " + error);
            }
            var listed = new HashSet<string>(rows.Select(r => r.File));
            foreach (string file in Directory.GetFiles(source, "*_*.png").Select(f => Path.GetFileName(f)))
                Check(listed.Contains(file), "PNG without ancres.tsv line: " + file);

            // Point d'ancrage = pixel (-xmin, -ymin) : le pied du personnage, en bas de l'image.
            Row stand = byKey["10/staticR"];
            using (Bitmap image = Load(Path.Combine(source, stand.File)))
            {
                Check(image.PixelFormat == PixelFormat.Format32bppArgb, "Sprite not loaded as 32 bpp ARGB: " + image.PixelFormat);
                int ax = -stand.XMin, ay = -stand.YMin;
                Check(ax > 0 && ax < image.Width && ay > image.Height * 2 / 3 && ay <= image.Height + 2, "Anchor (" + ax + ", " + ay + ") is not at the feet of " + stand.File + " " + image.Size);
                Check(Opaque(image, ax - 4, ay - 8, ax + 5, ay + 1), "No body pixel just above the anchor of " + stand.File);
                Check(image.GetPixel(0, 0).A == 0 || image.GetPixel(image.Width - 1, 0).A == 0, "Transparent margins lost in " + stand.File);
            }
            // Bande : chaque image occupe sa propre case (ligne par ligne pour une grille) et n'est pas vide.
            // 60_dieR est la bande la plus large du dépôt (112 images, 6 832 px) ; le client n'en montre que les
            // 60 premières (mort de 1 500 ms à 40 ips) et ses images 85 à 112 sont vides dans le SWF.
            foreach (string key in new[] { "10/walkR", "10/hitR", "11/dieL", "60/dieR" })
            {
                Row walk = byKey[key];
                int shown = walk.Anim.StartsWith("die") ? Math.Min(walk.Images, 60) : walk.Images;
                using (Bitmap strip = Load(Path.Combine(source, walk.File)))
                {
                    int perLine = Math.Max(1, strip.Width / walk.Width);
                    for (int k = 0; k < shown; k++)
                    {
                        int x = k % perLine * walk.Width, y = k / perLine * walk.Height;
                        Check(Opaque(strip, x, y, x + walk.Width, y + walk.Height), walk.File + ": frame " + (k + 1) + " is empty");
                    }
                }
            }

            // Budget (plan des animations, section 4) : hit et die des 24 classes restent dans la part du lot AN2.
            Row[] hitDieRows = rows.Where(r => classes.Contains(r.Gfx) && (r.Anim.StartsWith("hit") || r.Anim.StartsWith("die"))).ToArray();
            long hitDie = hitDieRows.Sum(r => new FileInfo(Path.Combine(source, r.File)).Length);
            Check(hitDieRows.Length == 96 && hitDie <= HitDieBudget, "hit/die strips of the 24 classes: " + hitDieRows.Length + " files, " + hitDie + " bytes, AN2 budget is " + HitDieBudget);
            Console.WriteLine("hit/die of the 24 classes: " + hitDieRows.Length + " strips, " + hitDie + " bytes (budget " + HitDieBudget + ")");
        }

        // 5. Les anciens <gfx><O>.png restent en place pour le chargeur actuel (UserMapControl.LoadSprite).
        int legacy = Directory.GetFiles(source, "*.png").Select(f => Path.GetFileName(f)).Count(f => Regex.IsMatch(f, "^[0-9]+[SRLFB]\\.png$"));
        Check(legacy >= 2200, "Legacy <gfx><O>.png sprites were removed: " + legacy + " left");

        // 6. Provenance : source, outil et commande exacte de régénération.
        string provenance = File.ReadAllText(Path.Combine(source, "PROVENANCE.md"));
        foreach (string needed in new[] { "exporter_sprites.py", "clips/sprites", "swfsvg", "--frame all", "ancres.tsv", "sprites_animes.txt", "cargo build --release", "--anims hit,die" })
            Check(provenance.Contains(needed), "PROVENANCE.md does not mention " + needed);

        // 7. Livraison : ancres.tsv est copié à côté de l'exécutable avec les PNG.
        string shipped = Path.Combine(TestPaths.ApplicationBin, "ressources", "Bot", "sprites");
        Check(File.Exists(Path.Combine(shipped, "ancres.tsv")), "ancres.tsv is not copied next to the executable: " + shipped);
        Check(File.ReadAllText(Path.Combine(shipped, "ancres.tsv")) == File.ReadAllText(anchors), "Shipped ancres.tsv differs from the source");
        if (shippedPng) Check(File.Exists(Path.Combine(shipped, "10_walkR.png")) && File.Exists(Path.Combine(shipped, "1001R.png")), "Sprite PNG are not copied next to the executable");

        Console.WriteLine("OK: " + rows.Count + " sprite anchors (" + rows.Count(r => r.Images > 1) + " strips), sprites_animes.txt families, PNG sizes, feet anchor, hit/die budget, legacy sprites, provenance and delivery");
    }
}
