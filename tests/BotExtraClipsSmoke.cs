using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
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
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Mouvements;
using Tool_BotProtocol.Utils.Crypto;

// Émotes animées, auras, cercle d'équipe, sac de marchand, clip des PNJ et ailes d'alignement (lot AN8, plan § 3.8, 3.11
// et 3.12). Données livrées (émotes des 24 classes, Effets/auras, cercle, extra 0 et 4, Alignments/ailes) lues par le
// chargeur du bot ; règles pures (clips voulus comme onSpriteMovement, condition du clip du dessus, image des ailes,
// pvpGain du client avec la table A.g et PVP_VIEW_BONUS_* de lang.xml) ; ailes dessinées par OverheadLayer avec les
// bandes livrées (image 13, lueur rougeâtre, assombrissement à 50 %) ; puis une carte sur un faux serveur en boucle
// locale (paquets injectés comme BotPointsHitDeathSmoke) vue par un MapControl à horloge injectée, avec des sprites et des
// clips synthétiques : eUK sans durée (Hold), eUK|0, eUK avec durée (static au terme), émote d'entrée du GM
// (emoteStatic1R), marche qui reprend, aura visible en staticF seulement et selon l'option, sac du marchand hors marche,
// clip du PNJ, cercles rouge et bleu après GS, émote ignorée en combat, carte cachée, paquets mal formés. Un PNG par
// instant dans TestPaths.Work ; dessin par Graphics.FromImage : passe sous Mono.
internal static class BotExtraClipsSmoke
{
    private const int MapWidth = 8, MapHeight = 8;
    /// <summary>Un pas vers le sud-ouest (direction 3, bande walkR en miroir) : cellule + largeur − 1.</summary>
    private const int SouthWestStep = MapWidth - 1;
    private const short SelfCell = 46, PlayerCell = 49, MerchantCell = 52, NpcCell = 33, SitterCell = 61, PlainCell = 64;
    private static readonly Color StaticColor = Color.FromArgb(120, 120, 120);
    private static readonly Color PlainColor = Color.FromArgb(90, 130, 160);
    private static readonly Color[] EmoteColors = { Color.FromArgb(230, 230, 40), Color.FromArgb(40, 200, 200), Color.FromArgb(150, 60, 220) };
    private static readonly Color SitColor = Color.FromArgb(250, 250, 250);
    private static readonly Color AuraColor = Color.FromArgb(20, 220, 20);
    private static readonly Color BagColor = Color.FromArgb(250, 140, 20);
    private static readonly Color NpcClipColor = Color.FromArgb(200, 20, 200);

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
            ShippedData();
            Rules();
            Wings();
            MapScenario();
            Console.WriteLine("OK: shipped emote strips, auras, circle, extra 0/4 and wings bands; wanted clips and top-clip rule; wings frame, "
                + "A.g table, pvpGain, frame 13 drawn with red glow and halved luminance; eUK Hold (frame 1 at t+30, held at t+5000), eUK|0, "
                + "eUK with 2000 ms back to static at t+2100, GM emote field emoteStatic1R then walk, aura in staticF only and per option, "
                + "merchant bag hidden while walking, NPC extra clip, red and blue team circles after GS, emote ignored in fight, hidden map, malformed packets");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    // ------------------------------------------------------------------ données livrées

    private static void ShippedData()
    {
        string bot = Path.Combine(TestPaths.ApplicationBin, "ressources", "Bot");
        string sprites = Path.Combine(bot, "sprites");
        Check(File.Exists(Path.Combine(sprites, "10_emote1R.png")) && File.Exists(Path.Combine(sprites, "10_emoteStatic1R.png"))
            && File.Exists(Path.Combine(sprites, "10_emote1R.couleurs.png")), "Shipped emote strips (and masks) missing next to the executable: " + sprites);
        foreach (string file in new[] { "Effets/auras/1_scene.png", "Effets/auras/2_scene.png", "Effets/auras/effets.tsv", "Effets/cercle/1_scene.png",
            "Effets/cercle/effets.tsv", "Effets/extra/0_scene.png", "Effets/extra/4_scene.png", "Effets/extra/5_scene.png", "Alignments/ailes/1_scene.png",
            "Alignments/ailes/2_scene.png", "Alignments/ailes/effets.tsv" })
            Check(File.Exists(Path.Combine(bot, file.Replace('/', Path.DirectorySeparatorChar))), "Shipped file missing: " + file);
        Check(File.ReadAllText(Path.Combine(bot, "Effets", "auras", "exclusions.tsv")).Contains("3\tvide"), "auras/3 (empty scene) is not listed as excluded");
        using (var library = new ActorSprites(sprites))
        {
            foreach (string animation in new[] { "emote1", "emote2", "emoteStatic1" }) library.Resolve(10, 1, false, animation);
            var strips = new Dictionary<string, ActorSprites.FixedStrip>
            {
                ["auras/1"] = library.ResolveFixed("auras", 1, "scene"), ["auras/2"] = library.ResolveFixed("auras", 2, "scene"),
                ["cercle/1"] = library.ResolveFixed("cercle", 1, "scene"), ["extra/0"] = library.ResolveFixed("extra", 0, "scene"),
                ["extra/4"] = library.ResolveFixed("extra", 4, "scene"), ["auras/3"] = library.ResolveFixed("auras", 3, "scene")
            };
            Check(library.WaitForPending(30000), "Shipped strips decoding did not finish");
            SpritePose emote1 = library.Resolve(10, 1, false, "emote1"), emote2 = library.Resolve(10, 1, false, "emote2"), pose = library.Resolve(10, 1, false, "emoteStatic1");
            Check(emote1.State == SpriteLoadState.Ready && emote1.FullName == "emote1R" && emote1.Sheet.Frames >= 9 && emote1.Sheet.Frames <= 13
                && emote1.Sheet.FramesPerSecond == 20 && emote1.Sheet.End == SpriteEnd.Stop, "Shipped 10_emote1R: 9 to 13 frames at 20 fps, fin arret");
            Check(emote2.State == SpriteLoadState.Ready && emote2.Sheet.End == SpriteEnd.Static && emote2.Sheet.Frames >= 20, "Shipped 10_emote2R: fin static, about 20 to 31 frames");
            Check(pose.State == SpriteLoadState.Ready && pose.FullName == "emoteStatic1R" && pose.Sheet.Frames == 1 && pose.Sheet.End == SpriteEnd.Stop, "Shipped 10_emoteStatic1R: one held frame");
            Check(library.Resolve(10, 2, false, "emote1").FullName == "emote1R" && library.Resolve(10, 2, false, "emote1").Mirrored, "Direction 2 plays emote1R mirrored (four-direction name)");
            SpriteSheet aura1 = strips["auras/1"].Sheet, aura2 = strips["auras/2"].Sheet, circle = strips["cercle/1"].Sheet, bag = strips["extra/0"].Sheet, npc = strips["extra/4"].Sheet;
            Check(aura1 != null && aura1.Frames == 88 && aura1.FramesPerSecond == 20 && aura1.End == SpriteEnd.Loop && aura1.FrameWidth == 71 && aura1.FrameHeight == 52
                && aura1.XMin == -38 && aura1.YMin == -43, "Shipped auras/1: 88 frames at 20 fps (176 at 40), 71 × 52 at (-38, -43), loop");
            Check(aura2 != null && aura2.Frames == 77 && aura2.FramesPerSecond == 20 && aura2.End == SpriteEnd.Loop, "Shipped auras/2: 77 frames at 20 fps (154 at 40), loop");
            Check(circle != null && circle.Frames == 1 && circle.FrameWidth == 38 && circle.FrameHeight == 20 && circle.XMin == -19 && circle.YMin == -10, "Shipped cercle/1: 38 × 20 at (-19, -10)");
            Check(bag != null && bag.Frames == 1 && bag.FrameWidth == 21 && bag.FrameHeight == 23, "Shipped extra/0: 21 × 23");
            Check(npc != null && npc.Frames == 102 && npc.FramesPerSecond == 40 && npc.End == SpriteEnd.Loop, "Shipped extra/4: 102 frames at 40 fps, loop");
            Check(strips["auras/3"].State == SpriteLoadState.Missing, "auras/3 has no strip (empty scene) and must be missing");
            // Le cercle est blanc à 10 % avec un contour noir : la teinte setRGB du bot ne garde que l'alpha.
            Bitmap tinted;
            SpriteSheet red = ExtraClipEffect.Tint(circle, 0xFF0000, out tinted);
            try
            {
                Check(red != null && red.FrameWidth == 38 && tinted.Width == 38, "Tinted circle has another frame");
                bool opaqueRed = false, faintRed = false, other = false;
                for (int y = 0; y < tinted.Height; y++)
                    for (int x = 0; x < tinted.Width; x++)
                    {
                        Color c = tinted.GetPixel(x, y);
                        if (c.A == 0) continue;
                        // libgdiplus stocke l'ARGB prémultiplié : GetPixel rend 254 au lieu de 255 sur les pixels translucides.
                        if (c.R < 253 || c.G > 1 || c.B > 1) other = true;
                        else if (c.A == 255) opaqueRed = true;
                        else faintRed = true;
                    }
                Check(opaqueRed && faintRed && !other, "setRGB tint must replace every RGB and keep the alpha (outline opaque, fill faint)");
            }
            finally { tinted?.Dispose(); }
            foreach (ActorSprites.FixedStrip strip in strips.Values) strip.Dispose();
        }
        OverheadLayer.WingsBand band = OverheadLayer.ReadWingsBand(Path.Combine(bot, "Alignments", "ailes", "effets.tsv"), 1);
        OverheadLayer.WingsBand fallen = OverheadLayer.ReadWingsBand(Path.Combine(bot, "Alignments", "ailes", "effets.tsv"), 2);
        Check(band != null && band.Frames == 30 && band.Width == 162 && band.Height == 136 && band.XMin == -81 && band.YMin == -94, "ailes/1 band: 30 frames of 162 × 136 at (-81, -94)");
        Check(fallen != null && fallen.Frames == 30 && fallen.Width == 150 && fallen.Height == 136, "ailes/2 band: 30 frames of 150 × 136");
        Check(OverheadLayer.ReadWingsBand(Path.Combine(bot, "Alignments", "ailes", "effets.tsv"), 7) == null && OverheadLayer.ReadWingsBand(Path.Combine(bot, "nothing.tsv"), 1) == null,
            "Unknown wings band or missing table must give null");
    }

    // ------------------------------------------------------------------ règles pures

    private static void Rules()
    {
        // Condition xtraClipTopAnimations : personnage en staticF, marchand en staticL/F/R, les autres toujours.
        Check(ActorAttachedEffect.TopClipVisible(ActorKind.Player, "staticF") && !ActorAttachedEffect.TopClipVisible(ActorKind.Player, "staticR")
            && !ActorAttachedEffect.TopClipVisible(ActorKind.Player, "walkF"), "Character top clip is visible in staticF only");
        foreach (string pose in new[] { "staticL", "staticF", "staticR" }) Check(ActorAttachedEffect.TopClipVisible(ActorKind.Merchant, pose), "Merchant top clip visible in " + pose);
        Check(!ActorAttachedEffect.TopClipVisible(ActorKind.Merchant, "walkR") && !ActorAttachedEffect.TopClipVisible(ActorKind.Merchant, "staticS")
            && ActorAttachedEffect.TopClipVisible(ActorKind.Npc, "walkR"), "Merchant top clip hidden while walking; other kinds always visible");

        // Clips voulus (onSpriteMovement) : marchand, PNJ, cercle en combat lancé, aura hors combat selon l'option.
        Func<ActorExtraClips.Wanted, string> describe = w => w.Family + "/" + w.Id + (w.Above ? "+" : "-") + (w.Rgb.HasValue ? w.Rgb.Value.ToString("X6") : "");
        Func<MapActor, bool, bool, string> wanted = (actor, running, aura) =>
            string.Join(",", ActorExtraClips.WantedFor(actor, running, aura, id => id == 7 ? 1 : (int?)null).Select(describe));
        var merchant = new MerchantActor { Id = 8, OfflineType = 0 };
        var npc = new NpcActor(9, 500) { ExtraClip = 4 };
        var plainNpc = new NpcActor(11, 501);
        var player = new PlayerActor { Id = 7, Aura = 1 };
        var noAura = new PlayerActor { Id = 12, Aura = 0 };
        var teamed = new PlayerActor { Id = 13, Aura = 2, Team = 0 };
        var monster = new FightMonsterActor(-7, 31) { Team = 1 };
        Check(wanted(merchant, false, true) == "extra/0+" && wanted(merchant, true, true) == "extra/0+", "Merchant bag: extra/<offlineType> above, in and out of fight: " + wanted(merchant, true, true));
        Check(wanted(npc, false, true) == "extra/4-" && wanted(plainNpc, false, true) == "", "NPC: extra/<extraClip> below, nothing without the field");
        Check(wanted(player, false, true) == "auras/1+" && wanted(player, false, false) == "" && wanted(noAura, false, true) == "", "Aura above out of fight when the option is on");
        Check(wanted(player, true, true) == "cercle/1-0000FF" && wanted(teamed, true, true) == "cercle/1-FF0000" && wanted(monster, true, true) == "cercle/1-0000FF",
            "Running fight: circle below, red for team 0 and blue for team 1 (team from the actor or the fight): " + wanted(player, true, true));
        Check(wanted(new PlayerActor { Id = 14, Team = 2 }, true, true) == "cercle/1-" && ActorExtraClips.TeamColor(0) == 0xFF0000 && ActorExtraClips.TeamColor(1) == 0x0000FF
            && !ActorExtraClips.TeamColor(5).HasValue && !ActorExtraClips.TeamColor(null).HasValue, "Unknown team: circle without tint");
        Check(wanted(null, true, true) == "", "Null actor wants nothing");
        var key1 = new ActorExtraClips.Wanted(7, "auras", 1, true); var key2 = new ActorExtraClips.Wanted(7, "cercle", 1, false, 0xFF0000);
        Check(key1.Key == "7|auras/1|dessus" && key2.Key == "7|cercle/1|dessous|FF0000", "Wanted keys: " + key1.Key + " " + key2.Key);

        // Suivi d'émote : annule l'animation à son terme si c'est encore elle ; s'arrête seul si la file l'a abandonnée.
        var queue = new ActorAnimationQueue();
        queue.Play(7, "emote1", 0, ActorAnimationMode.Hold);
        var timer = new EmoteTimer(queue, 7, "emote1", 2000);
        Check(timer.Update(1999, null) && queue.IsPlaying(7, "emote1") && !timer.Update(2000, null) && !queue.IsPlaying(7) && !timer.Update(2001, null), "EmoteTimer did not cancel at its end");
        queue.Play(7, "emote2", 0, ActorAnimationMode.Hold);
        Check(!new EmoteTimer(queue, 7, "emote1", 10).Update(20, null) && queue.IsPlaying(7, "emote2"), "EmoteTimer cancelled another emote");
        Check(new EmoteTimer(queue, 7, "emote2", null).Update(100000, null) && queue.IsPlaying(7, "emote2"), "Untimed EmoteTimer must not cancel a held emote");
        // L'icône (onShown) ne s'efface qu'à la première image observée de la bande, une seule fois ; jamais si la bande manque.
        int cleared = 0;
        var follow = new EmoteTimer(queue, 7, "emote2", null, () => cleared++);
        Check(follow.Update(10, null) && cleared == 0, "onShown fired before the strip was drawn");
        using (var library = new ActorSprites(Assets()))
        {
            ActorSprites.FixedStrip strip = library.ResolveFixed("cercle", 1, "scene");
            DateTime limit = DateTime.UtcNow.AddSeconds(10);
            while (strip.State == SpriteLoadState.Loading && DateTime.UtcNow < limit) Thread.Sleep(10);
            Check(strip.State == SpriteLoadState.Ready, "Synthetic circle strip did not load for the EmoteTimer check");
            queue.Observe(7, "emote2", SpriteLoadState.Ready, strip.Sheet, 20);
            Check(follow.Update(30, null) && cleared == 1 && follow.Update(40, null) && cleared == 1, "onShown must fire once when the strip is first drawn");
            strip.Dispose();
        }
        queue.Observe(7, "emote2", SpriteLoadState.Missing, null, 50);
        Check(!follow.Update(60, null) && !queue.IsPlaying(7), "EmoteTimer must end once the queue dropped the emote");
        queue.Play(8, "emote3", 0, ActorAnimationMode.Hold);
        var missing = new EmoteTimer(queue, 8, "emote3", null, () => cleared++);
        queue.Observe(8, "emote3", SpriteLoadState.Missing, null, 1);
        Check(!missing.Update(2, null) && cleared == 1, "onShown fired for a missing strip (icon must stay)");
    }

    // ------------------------------------------------------------------ ailes

    private static void Wings()
    {
        // Image = grade, 10 + grade, 20 + grade ; lueur par camp de l'image.
        Check(OverheadLayer.WingsFrameOf(1, 1) == 1 && OverheadLayer.WingsFrameOf(2, 3) == 13 && OverheadLayer.WingsFrameOf(3, 10) == 30
            && OverheadLayer.WingsFrameOf(0, 3) == 0 && OverheadLayer.WingsFrameOf(2, 0) == 0 && OverheadLayer.WingsFrameOf(4, 1) == 0 && OverheadLayer.WingsFrameOf(1, 11) == 0,
            "Wings frame: grade, 10 + grade, 20 + grade");
        Check(OverheadLayer.GlowColorOf(1) == 0xAAEAFF && OverheadLayer.GlowColorOf(10) == 0xAAEAFF && OverheadLayer.GlowColorOf(11) == 0xCC0000
            && OverheadLayer.GlowColorOf(13) == 0xCC0000 && OverheadLayer.GlowColorOf(21) == 0 && OverheadLayer.GlowColorOf(30) == 0, "Glow colour by floor((frame - 1) / 10)");

        // Table A.g et seuils PVP_VIEW_BONUS_* lus dans les fichiers de langue livrés (alignment.xml et lang.xml seuls).
        string shippedLang = Path.Combine(TestPaths.ApplicationBin, "ressources", "Bot", "BotLang");
        string lang = Path.Combine(TestPaths.Work, "extra-clips", "lang");
        if (Directory.Exists(lang)) Directory.Delete(lang, true);
        Directory.CreateDirectory(lang);
        foreach (string file in new[] { "alignment.xml", "lang.xml" }) File.Copy(Path.Combine(shippedLang, file), Path.Combine(lang, file));
        Check(Task.Run(() => LangData.Load(lang)).Result >= 2 && LangData.IsLoaded("alignment") && LangData.IsLoaded("lang"), "alignment.xml and lang.xml not loaded");
        Check(LangData.Text.Config("PVP_VIEW_BONUS_MINOR_LIMIT") == "20" && LangData.Text.Config("PVP_VIEW_BONUS_MAJOR_LIMIT") == "-20", "PVP_VIEW_BONUS limits of lang.xml");
        Check(OverheadLayer.CanViewPvpGain(2, 2) && OverheadLayer.CanViewPvpGain(1, 2) && OverheadLayer.CanViewPvpGain(2, 1) && OverheadLayer.CanViewPvpGain(3, 3)
            && !OverheadLayer.CanViewPvpGain(1, 3) && !OverheadLayer.CanViewPvpGain(0, 1) && !OverheadLayer.CanViewPvpGain(1, 0) && !OverheadLayer.CanViewPvpGain(2, 7)
            && !OverheadLayer.CanViewPvpGain(-1, 1), "A.g: Bonta and Brâkmar see each other, mercenaries see mercenaries, neutrals nothing");
        var viewer = new OverheadLayer.PvpViewer(42, 10, 2);
        Func<int, int, long, int> gain = (side, level, id) => OverheadLayer.PvpGain(viewer, new ActorAlignment { Side = side, LevelCode = level + id }, id);
        Check(gain(2, 11, 7) == 0 && gain(2, 8, 7) == 0 && gain(2, 7, 7) == -1 && gain(2, 13, 7) == 1 && gain(2, 40, 7) == 1 && gain(2, 1, 7) == -1,
            "pvpGain: 10 × 0,8 > other → -1, 10 × 1,2 < other → 1, else 0");
        Check(gain(3, 40, 7) == 0 && gain(0, 40, 7) == 0 && OverheadLayer.PvpGain(null, new ActorAlignment { Side = 2 }, 7) == 0 && OverheadLayer.PvpGain(viewer, null, 7) == 0,
            "pvpGain 0 when the sides do not see each other or without viewer");
        Check(OverheadLayer.PvpGain(new OverheadLayer.PvpViewer(42, 100, 1), new ActorAlignment { Side = 2, LevelCode = 79 + 7 }, 7) == -1
            && OverheadLayer.PvpGain(new OverheadLayer.PvpViewer(42, 100, 1), new ActorAlignment { Side = 2, LevelCode = 121 + 7 }, 7) == 1
            && OverheadLayer.PvpGain(new OverheadLayer.PvpViewer(42, 100, 1), new ActorAlignment { Side = 2, LevelCode = 100 + 7 }, 7) == 0, "pvpGain at level 100: 79 → -1, 121 → 1, 100 → 0");

        // Describe : grade 3 Brâkmar vu par un Brâkmar de niveau 10 → image 13, pvpGain 1 (niveau 40) ; sans grade : rien ; déchu ; soi-même : pas de pvpGain.
        OverheadLayer.Viewer = viewer;
        var brak = new PlayerActor { Id = 7, Name = "Brak", Alignment = ActorAlignment.Parse("2,0,3,47,0") };
        OverheadContent content = OverheadLayer.Describe(brak, false, false);
        Check(content.WingsFrame == 13 && !content.FallenWings && content.PvpGain == 1 && content.Text == "Brak", "Describe: Brâkmar grade 3 level 40 → frame 13, pvpGain 1: " + content.WingsFrame + " " + content.PvpGain);
        OverheadContent weak = OverheadLayer.Describe(new PlayerActor { Id = 7, Name = "Brak", Alignment = ActorAlignment.Parse("2,0,3,8,1") }, false, false);
        Check(weak.WingsFrame == 13 && weak.FallenWings && weak.PvpGain == -1, "Describe: fallen, level 1 → pvpGain -1");
        Check(OverheadLayer.Describe(new PlayerActor { Id = 7, Name = "Neutre", Alignment = ActorAlignment.Parse("2,0,0,47") }, false, false).WingsFrame == 0, "Grade 0: no wings");
        Check(OverheadLayer.Describe(new PlayerActor { Id = 7, Name = "Bonta", Alignment = ActorAlignment.Parse("1,0,2,47") }, false, false).WingsFrame == 2, "Bonta grade 2: frame 2");
        OverheadContent guild = OverheadLayer.Describe(new PlayerActor { Id = 7, Name = "Guilde", GuildName = "G", GuildEmblem = "1,1,1,1", Alignment = ActorAlignment.Parse("3,0,5,47") }, false, false);
        Check(guild.Layout == OverheadLayout.Guild && guild.WingsFrame == 25 && guild.PvpGain == 0, "Guild overhead keeps the wings (mercenary grade 5 → 25, pvpGain 0 for an unseen side)");
        Check(OverheadLayer.Describe(new PlayerActor { Id = 42, IsSelf = true, Name = "Moi", Alignment = ActorAlignment.Parse("2,0,3,82") }, false, false).PvpGain == 0, "Self: no pvpGain");
        OverheadLayer.Viewer = null;
        Check(OverheadLayer.Describe(brak, false, false).PvpGain == 0 && OverheadLayer.Describe(brak, false, false).WingsFrame == 13, "Without viewer: wings without pvpGain");
        OverheadLayer.Viewer = viewer;

        // Dessin avec les bandes livrées : image 13 derrière le nom, lueur rougeâtre (0xCC0000) autour, assombrissement à 50 % (± 2).
        using (var images = new OverheadImages(Path.Combine(TestPaths.ApplicationBin, "ressources", "Bot")))
        {
            var box = new Rectangle(140, 160, 120, 20);
            var origin = new Point(box.Left + box.Width / 2, box.Top);
            Func<int, Bitmap> draw = pvpGain =>
            {
                var bitmap = new Bitmap(400, 300, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    OverheadLayer.Draw(graphics, new OverheadContent { Text = "Brak", WingsFrame = 13, PvpGain = pvpGain }, box, images);
                }
                return bitmap;
            };
            foreach (int pvpGain in new[] { 0, 1, -1 }) draw(pvpGain).Dispose();
            Check(images.WaitForPending(20000), "Wings images not rendered in time");
            OverheadLayer.WingsBand plain = OverheadLayer.WingsBandOf(OverheadLayer.WingsKey(false, 13, 0)), glow = OverheadLayer.WingsBandOf(OverheadLayer.WingsKey(false, 13, 1));
            Check(plain != null && plain.Margin == 0 && glow != null && glow.Margin == 15 && glow.XMin == -81, "Wings band info: no margin without glow, 15 px (5 × 3 passes) with glow");
            using (Bitmap none = draw(0))
            using (Bitmap bright = draw(1))
            using (Bitmap dark = draw(-1))
            {
                none.Save(Path.Combine(TestPaths.Work, "bot-extra-wings-0.png"), ImageFormat.Png);
                bright.Save(Path.Combine(TestPaths.Work, "bot-extra-wings-glow.png"), ImageFormat.Png);
                dark.Save(Path.Combine(TestPaths.Work, "bot-extra-wings-dark.png"), ImageFormat.Png);
                var area = new Rectangle(origin.X - 81 - 15, origin.Y - 94 - 15, 162 + 30, 94 + 15);
                int drawn = 0, reddish = 0, opaque = 0, halved = 0, wrong = 0;
                for (int y = area.Top; y < area.Bottom; y++)
                    for (int x = area.Left; x < area.Right; x++)
                    {
                        Color p0 = none.GetPixel(x, y), p1 = bright.GetPixel(x, y), p2 = dark.GetPixel(x, y);
                        if (p0.A > 0) drawn++;
                        if (p0.A == 0 && p1.A > 0 && p1.R > p1.G + 40 && p1.R > p1.B + 40) reddish++;
                        if (p0.A == 255 && y < box.Top - 1)
                        {
                            opaque++;
                            if (Math.Abs(p2.R - p0.R / 2) <= 2 && Math.Abs(p2.G - p0.G / 2) <= 2 && Math.Abs(p2.B - p0.B / 2) <= 2 && p2.A == 255) halved++; else wrong++;
                        }
                    }
                Check(drawn >= 1000, "Frame 13 not drawn above the name: " + drawn + " pixels");
                Check(reddish >= 200, "No reddish glow around the wings with pvpGain 1: " + reddish + " pixels");
                Check(opaque >= 200 && wrong == 0, "pvpGain -1 must halve every channel (± 2): " + halved + " ok, " + wrong + " wrong of " + opaque);
                // Sans ailes : rien hors de la boîte.
                var bitmap = new Bitmap(400, 300, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(bitmap)) { graphics.Clear(Color.Transparent); OverheadLayer.Draw(graphics, new OverheadContent { Text = "Brak" }, box, images); }
                int outside = 0;
                for (int y = area.Top; y < box.Top - 1; y++) for (int x = area.Left; x < area.Right; x++) if (bitmap.GetPixel(x, y).A > 0) outside++;
                bitmap.Dispose();
                Check(outside == 0, "Pixels above the box without wings: " + outside);
            }
            // Image hors bande (31) ou bande absente : rien, sans exception.
            using (var bitmap = new Bitmap(50, 50))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                OverheadLayer.Draw(graphics, new OverheadContent { Text = "x", WingsFrame = 31 }, new Rectangle(5, 30, 40, 15), images);
                OverheadLayer.Draw(graphics, new OverheadContent { Text = "x", WingsFrame = 13 }, new Rectangle(5, 30, 40, 15), null);
            }
            Check(OverheadLayer.RenderWings(Path.Combine(TestPaths.Work, "nowhere"), "k", false, 13, 1) == null, "Missing band folder must render null");
        }
        OverheadLayer.Viewer = null;
    }

    // ------------------------------------------------------------------ sprites et clips synthétiques

    private static void Strip(string path, Color[] colors, int width, int height)
    {
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

    private static string Assets()
    {
        string root = Path.Combine(TestPaths.Work, "extra-clips");
        string sprites = Path.Combine(root, "sprites"), effects = Path.Combine(root, "Effets");
        if (Directory.Exists(sprites)) Directory.Delete(sprites, true);
        if (Directory.Exists(effects)) Directory.Delete(effects, true);
        Directory.CreateDirectory(sprites);
        foreach (string o in new[] { "R", "L", "F" })
        {
            Strip(Path.Combine(sprites, "900_static" + o + ".png"), new[] { StaticColor }, 20, 60);
            Strip(Path.Combine(sprites, "901_static" + o + ".png"), new[] { PlainColor }, 20, 60);
        }
        foreach (string o in new[] { "R", "L" })
        {
            Strip(Path.Combine(sprites, "900_walk" + o + ".png"), new[] { StaticColor, PlainColor }, 20, 60);
            Strip(Path.Combine(sprites, "901_walk" + o + ".png"), new[] { PlainColor, StaticColor }, 20, 60);
        }
        Strip(Path.Combine(sprites, "900_emote1R.png"), EmoteColors, 20, 60);
        Strip(Path.Combine(sprites, "900_emoteStatic1R.png"), new[] { SitColor }, 20, 60);
        var anchors = new List<string> { "gfx\tanim\txmin\tymin\tlargeur\thauteur\timages\tips\tfin" };
        foreach (int gfx in new[] { 900, 901 })
        {
            foreach (string o in new[] { "F", "L", "R" }) anchors.Add(gfx + "\tstatic" + o + "\t-10\t-60\t20\t60\t1\t40\tarret");
            foreach (string o in new[] { "L", "R" }) anchors.Add(gfx + "\twalk" + o + "\t-10\t-60\t20\t60\t2\t40\tboucle");
        }
        anchors.Add("900\temote1R\t-10\t-60\t20\t60\t3\t40\tarret");
        anchors.Add("900\temoteStatic1R\t-10\t-60\t20\t60\t1\t40\tarret");
        File.WriteAllLines(Path.Combine(sprites, "ancres.tsv"), anchors);
        // Clips annexes : aura verte au-dessus de la tête, cercle blanc opaque aux pieds (teinté par le bot), sac orange au-dessus,
        // clip violet du PNJ à droite des pieds.
        Directory.CreateDirectory(Path.Combine(effects, "auras")); Directory.CreateDirectory(Path.Combine(effects, "cercle")); Directory.CreateDirectory(Path.Combine(effects, "extra"));
        Strip(Path.Combine(effects, "auras", "1_scene.png"), new[] { AuraColor, AuraColor }, 30, 20);
        File.WriteAllLines(Path.Combine(effects, "auras", "effets.tsv"), new[] { "gfx\tanim\txmin\tymin\tlargeur\thauteur\timages\tips\tfin", "1\tscene\t-15\t-85\t30\t20\t2\t20\tboucle" });
        Strip(Path.Combine(effects, "cercle", "1_scene.png"), new[] { Color.White }, 40, 10);
        File.WriteAllLines(Path.Combine(effects, "cercle", "effets.tsv"), new[] { "gfx\tanim\txmin\tymin\tlargeur\thauteur\timages\tips\tfin", "1\tscene\t-20\t-5\t40\t10\t1\t40\tarret" });
        Strip(Path.Combine(effects, "extra", "0_scene.png"), new[] { BagColor }, 16, 16);
        Strip(Path.Combine(effects, "extra", "4_scene.png"), new[] { NpcClipColor }, 12, 12);
        File.WriteAllLines(Path.Combine(effects, "extra", "effets.tsv"), new[] { "gfx\tanim\txmin\tymin\tlargeur\thauteur\timages\tips\tfin",
            "0\tscene\t-8\t-80\t16\t16\t1\t40\tarret", "4\tscene\t12\t-12\t12\t12\t1\t40\tarret" });
        return sprites;
    }

    // ------------------------------------------------------------------ outils

    private static void PumpUntil(Func<bool> done)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(10);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("Extra clips loopback timed out"); Application.DoEvents(); Thread.Sleep(5); }
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

    private static Color Center(Bitmap image, RectangleF bounds) =>
        image.GetPixel((int)(bounds.Left + bounds.Width / 2), (int)(bounds.Top + bounds.Height / 2));

    private static bool IsRed(Color c) => c.R >= 200 && c.G <= 60 && c.B <= 60;
    private static bool IsBlue(Color c) => c.B >= 200 && c.R <= 60 && c.G <= 60;

    private static int Count(Bitmap image, UserMapControl view, RectangleF world, Func<Color, bool> match)
    {
        RectangleF screen = view.EffectView.ToScreen(world);
        int count = 0;
        for (int y = Math.Max(0, (int)screen.Top); y < Math.Min(image.Height, (int)Math.Ceiling(screen.Bottom)); y++)
            for (int x = Math.Max(0, (int)screen.Left); x < Math.Min(image.Width, (int)Math.Ceiling(screen.Right)); x++)
                if (match(image.GetPixel(x, y))) count++;
        return count;
    }

    private static PointF Foot(UserMapControl view, long id)
    {
        ActorAnchor anchor;
        Check(view.TryGetActorAnchor(id, out anchor), "Actor " + id + " is not drawn");
        return anchor.WorldFoot;
    }

    /// <summary>Cadre d'un clip synthétique autour du pied (repère de la carte).</summary>
    private static RectangleF AuraArea(PointF foot) => new RectangleF(foot.X - 15, foot.Y - 85, 30, 20);
    private static RectangleF BagArea(PointF foot) => new RectangleF(foot.X - 8, foot.Y - 80, 16, 16);
    private static RectangleF NpcClipArea(PointF foot) => new RectangleF(foot.X + 12, foot.Y - 12, 12, 12);
    private static RectangleF CircleArea(PointF foot) => new RectangleF(foot.X - 20, foot.Y - 5, 40, 10);

    private static UserMapControl.ActorVisualState State(UserMapControl view, int id) => view.GetActorVisualState(id);

    private static void At(MapControl control, double time)
    {
        clock = time;
        control.MapSurface.TickAnimations();
    }

    private static ExtraClipEffect Clip(MapControl control, long actorId, string family) =>
        control.ExtraClips.Active.FirstOrDefault(clip => clip.ActorId == actorId && clip.Family == family);

    private static string Path1(Map map, int from, int to) =>
        PathfinderUtils.EncodePoint(0, from) + PathfinderUtils.CompressPath(new List<Cell> { map.MapCells[from], map.MapCells[to] });

    // ------------------------------------------------------------------ carte en boucle locale

    private static void MapScenario()
    {
        string sprites = Assets();
        int count = MapHeight * (2 * MapWidth - 1) - (MapWidth - 1);
        MessagesReception.Init();
        Map.AllBotMaps[900096] = new Map { MapID = 900096, MapWidth = MapWidth, MapHeight = MapHeight, MapData = string.Concat(Enumerable.Repeat(EncodedCell(), count)) };
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            using (var account = new Accounts(new AccountConfig("synthetic-clips", "synthetic", "loopback")))
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
                        var options = new BotOptions(Path.Combine(TestPaths.Work, "extra-clips", "BotOptions.json")) { AutoSave = false };
                        Check(options.Aura && BotOptions.BooleanNames.Contains("Aura") && BotOptions.DefaultValue("Aura") is bool, "Aura option missing or not true by default");
                        control.Options = options;
                        control.MapShown = () => true;
                        control.Size = new Size(640, 400); view.Size = new Size(640, 400);
                        typeof(UserControl).GetMethod("OnLoad", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { EventArgs.Empty });
                        Scenario(account, control, view);
                    }
                }
            }
        }
        finally { listener.Stop(); }
    }

    private static void Scenario(Accounts account, MapControl control, UserMapControl view)
    {
        var failures = new List<string>();
        control.Sequencer.StepFailed += (step, error) => failures.Add(step.Name + ": " + error);
        control.Effects.EffectFailed += (effect, error) => failures.Add(effect.GetType().Name + ": " + error);
        var routed = new List<string>();
        control.VisualRouted += (family, visual, shown) => routed.Add(family + (shown ? "" : " (cachée)"));
        Map map = account.Game.Map;

        // Hors combat : le personnage (42), un joueur de face avec l'aura 1 (7), un marchand (8), un PNJ à clip 4 (9).
        At(control, 10000);
        Feed(account, "GM|+" + SelfCell + ";1;0;42;Moi;1;900^100;0;0,0,0,52;-1;-1;-1;;0;;;;;;"
            + "|+" + PlayerCell + ";2;0;7;Brak;1;900^100;0;2,0,3,47;-1;-1;-1;;1;;;;;;"
            + "|+" + MerchantCell + ";2;0;8;Marchand;-5;901^100;-1;-1;-1;;;;0"
            + "|+" + NpcCell + ";1;0;9;500;-4;901^100;0;-1;-1;-1;;4;0");
        PlayerActor brak = map.GetActor(7) as PlayerActor;
        Check(brak != null && brak.Aura == 1 && brak.Alignment.Grade == 3 && (map.GetActor(8) as MerchantActor)?.OfflineType == 0 && (map.GetActor(9) as NpcActor)?.ExtraClip == 4,
            "GM entries misread (aura, alignment, offlineType, extraClip)");
        Check(control.ExtraClips.Count == 3 && Clip(control, 7, "auras") != null && Clip(control, 8, "extra") != null && Clip(control, 9, "extra") != null,
            "Expected aura, bag and NPC clip: " + string.Join(",", control.ExtraClips.Active.Select(c => c.ActorId + ":" + c.Family + "/" + c.ClipId)));
        Check(Clip(control, 7, "auras").Above && Clip(control, 8, "extra").Above && !Clip(control, 9, "extra").Above, "Aura and bag above, NPC clip below");
        At(control, 10000);
        PointF self = Foot(view, 42), brakFoot = Foot(view, 7), merchant = Foot(view, 8), npc = Foot(view, 9);
        using (Bitmap image = Render(view, "bot-extra-1-clips"))
        {
            At(control, 10000);
            image.Dispose();
            using (Bitmap again = Render(view, "bot-extra-1-clips"))
            {
                Check(State(view, 7).AnimationName == "staticF" && State(view, 8).AnimationName == "staticF", "Player 7 and merchant 8 must face front");
                Check(Count(again, view, AuraArea(brakFoot), c => Near(c, AuraColor)) >= 20, "Aura 1 not drawn above player 7 in staticF");
                Check(Count(again, view, BagArea(merchant), c => Near(c, BagColor)) >= 20, "Merchant bag (extra/0) not drawn above the merchant");
                Check(Count(again, view, NpcClipArea(npc), c => Near(c, NpcClipColor)) >= 20, "NPC clip (extra/4) not drawn below the NPC");
                Check(Count(again, view, AuraArea(self), c => Near(c, AuraColor)) == 0, "Aura drawn above the account's character (aura 0)");
                Check(Clip(control, 7, "auras").Visible && Clip(control, 8, "extra").Visible && Clip(control, 9, "extra").Visible, "Clips not marked visible");
            }
        }
        Check(failures.Count == 0, "Effect failed: " + string.Join(" | ", failures));

        // Aura en staticF seulement : tourné à droite, plus d'aura ; de face, elle revient.
        brak.Orientation = 1;
        At(control, 10100);
        using (Bitmap image = Render(view, "bot-extra-2-aura-right"))
            Check(State(view, 7).AnimationName == "staticR" && Count(image, view, AuraArea(brakFoot), c => Near(c, AuraColor)) == 0 && !Clip(control, 7, "auras").Visible,
                "Aura drawn on a character in staticR");
        brak.Orientation = 2;
        At(control, 10200);
        using (Bitmap image = Render(view, "bot-extra-3-aura-front"))
            Check(Count(image, view, AuraArea(brakFoot), c => Near(c, AuraColor)) >= 20, "Aura not back in staticF");

        // Option Aura décochée : l'aura est retirée aussitôt ; recochée : elle revient.
        control.Options.Aura = false;
        Check(Clip(control, 7, "auras") == null && control.ExtraClips.Count == 2, "Aura clip kept with the option off");
        At(control, 10300);
        using (Bitmap image = Render(view, "bot-extra-4-aura-off"))
            Check(Count(image, view, AuraArea(brakFoot), c => Near(c, AuraColor)) == 0, "Aura drawn with the option off");
        control.Options.Aura = true;
        Check(Clip(control, 7, "auras") != null, "Aura clip not back with the option on");
        At(control, 10400);
        using (Bitmap image = Render(view, "bot-extra-5-aura-on"))
            Check(Count(image, view, AuraArea(brakFoot), c => Near(c, AuraColor)) >= 20, "Aura not drawn again with the option on");

        // Sac du marchand : absent pendant la marche (walk), de retour à l'arrêt.
        At(control, 11000);
        Feed(account, "GA0;1;8;" + Path1(map, MerchantCell, MerchantCell + SouthWestStep));
        At(control, 11050);
        using (Bitmap image = Render(view, "bot-extra-6-merchant-walk"))
        {
            UserMapControl.ActorVisualState walking = State(view, 8);
            Check(walking != null && walking.IsMoving && walking.Animation == "walk", "Merchant not walking after GA;1: " + walking?.AnimationName);
            Check(Count(image, view, BagArea(merchant), c => Near(c, BagColor)) == 0 && Count(image, view, BagArea(Foot(view, 8)), c => Near(c, BagColor)) == 0
                && !Clip(control, 8, "extra").Visible, "Merchant bag drawn while walking");
        }
        At(control, 16000);
        using (Bitmap image = Render(view, "bot-extra-7-merchant-stopped"))
        {
            UserMapControl.ActorVisualState stopped = State(view, 8);
            Check(stopped != null && !stopped.IsMoving && stopped.AnimationName.StartsWith("static", StringComparison.Ordinal), "Merchant still moving at t + 5 s: " + stopped?.AnimationName);
            Check(Count(image, view, BagArea(Foot(view, 8)), c => Near(c, BagColor)) >= 20, "Merchant bag not back once stopped (" + stopped.AnimationName + ")");
        }

        // Émote eUK7|1 sans durée (StarLoco) : bande emote1R (de face : R en miroir), image 1 à t + 30, tenue à t + 5 000 (fin arret), sans icône.
        routed.Clear();
        At(control, 20000);
        Feed(account, "eUK7|1");
        Check(routed.Count == 1 && routed[0] == "Emote", "eUK not routed to Emote: " + string.Join(",", routed));
        Check(control.AnimationQueue.IsPlaying(7, "emote1"), "eUK did not start emote1 on actor 7");
        using (Bitmap image = Render(view, "bot-extra-8-emote-t"))
        {
            UserMapControl.ActorVisualState state = State(view, 7);
            Check(state.AnimationName == "emote1R" && state.Frame == 0 && state.IsMirrored && state.EmoteIcon == 0 && Near(Center(image, state.SpriteBounds), EmoteColors[0]),
                "emote1R frame 0 not drawn at t: " + state.AnimationName + " " + state.Frame + " icon " + state.EmoteIcon);
        }
        At(control, 20030);
        using (Bitmap image = Render(view, "bot-extra-9-emote-t30"))
            Check(State(view, 7).Frame == 1 && Near(Center(image, State(view, 7).SpriteBounds), EmoteColors[1]), "emote1R frame 1 not drawn at t + 30");
        At(control, 25000);
        using (Bitmap image = Render(view, "bot-extra-10-emote-t5000"))
        {
            UserMapControl.ActorVisualState state = State(view, 7);
            Check(state.AnimationName == "emote1R" && state.Frame == 2 && state.EmoteIcon == 0 && Near(Center(image, state.SpriteBounds), EmoteColors[2]),
                "emote1R (fin arret) not held on its last frame at t + 5 000: " + state.AnimationName + " " + state.Frame);
            Check(Count(image, view, AuraArea(brakFoot), c => Near(c, AuraColor)) == 0, "Aura drawn during an emote (top clip is staticF only)");
        }
        // eUK7|0 (se relever) : retour à static.
        Feed(account, "eUK7|0");
        At(control, 25100);
        Check(!control.AnimationQueue.IsPlaying(7) && State(view, 7).AnimationName == "staticF" && State(view, 7).EmoteIcon == 0, "eUK|0 did not return to static: " + State(view, 7).AnimationName);

        // eUK7|1|2000 : minuteur, static à t + 2 100 (encore l'émote à t + 1 999).
        At(control, 30000);
        Feed(account, "eUK7|1|2000");
        Check(control.AnimationQueue.IsPlaying(7, "emote1") && control.Effects.Active.OfType<EmoteTimer>().Count(t => t.ActorId == 7) == 1, "Timed emote: no EmoteTimer");
        At(control, 31999);
        Check(State(view, 7).AnimationName == "emote1R" && State(view, 7).Frame == 2, "Timed emote ended before its duration");
        At(control, 32100);
        using (Bitmap image = Render(view, "bot-extra-11-emote-timer"))
            Check(State(view, 7).AnimationName == "staticF" && !control.AnimationQueue.IsPlaying(7) && !control.Effects.Active.OfType<EmoteTimer>().Any()
                && Near(Center(image, State(view, 7).SpriteBounds), StaticColor), "Timed emote not back to static at t + 2 100: " + State(view, 7).AnimationName);
        // Une nouvelle émote remplace le minuteur de la précédente.
        At(control, 33000);
        Feed(account, "eUK7|1|500");
        Feed(account, "eUK7|1");
        Check(control.AnimationQueue.IsPlaying(7, "emote1") && control.Effects.Active.OfType<EmoteTimer>().Count() == 1
            && control.Effects.Active.OfType<EmoteTimer>().All(t => t.ActorId == 7 && !t.End.HasValue), "A new emote must drop the previous timer");
        At(control, 34000);
        Check(control.AnimationQueue.IsPlaying(7, "emote1"), "Untimed emote cancelled by the replaced timer");
        Feed(account, "eUK7|0");
        At(control, 34100);

        // Gfx sans bande d'émote (901) : l'icône du lot AN1 reste.
        Feed(account, "GM|+" + PlainCell + ";1;0;11;Sans;1;901^100;0;0,0,0,56;-1;-1;-1;;0;;;;;;");
        At(control, 34200);
        Feed(account, "eUK11|1");
        Check(!control.AnimationQueue.IsPlaying(11), "Emote played without a strip");
        Render(view, "bot-extra-12-emote-icon").Dispose();
        Check(State(view, 11).AnimationName == "staticR" && State(view, 11).EmoteIcon == 1, "Emote icon fallback lost for a gfx without emote strips: " + State(view, 11).EmoteIcon);
        Feed(account, "eUK11|0");

        // Émote d'entrée : champ émote du GM à 1 → emoteStatic1R dès l'arrivée, sans icône ; après un GA;1, la marche reprend puis static.
        At(control, 40000);
        Feed(account, "GM|+" + SitterCell + ";1;0;10;Assis;1;900^100;0;0,0,0,15;-1;-1;-1;;0;1;;;;;;");
        Check((map.GetActor(10) as PlayerActor)?.Emote == "1", "GM emote field misread");
        Check(control.AnimationQueue.IsPlaying(10, "emoteStatic1"), "Entry emote not queued on arrival");
        using (Bitmap image = Render(view, "bot-extra-13-entry-emote"))
        {
            UserMapControl.ActorVisualState state = State(view, 10);
            Check(state.AnimationName == "emoteStatic1R" && state.EmoteIcon == 0 && Near(Center(image, state.SpriteBounds), SitColor),
                "emoteStatic1R not drawn on arrival: " + state.AnimationName + " icon " + state.EmoteIcon);
        }
        At(control, 41000);
        Check(State(view, 10).AnimationName == "emoteStatic1R", "Entry emote not held");
        Feed(account, "GA0;1;10;" + Path1(map, SitterCell, SitterCell + SouthWestStep));
        At(control, 41050);
        using (Bitmap image = Render(view, "bot-extra-14-entry-walk"))
            Check(State(view, 10).IsMoving && State(view, 10).Animation == "walk" && !control.AnimationQueue.IsPlaying(10), "Walk did not resume after GA;1: " + State(view, 10).AnimationName);
        At(control, 46000);
        Feed(account, "GM|~" + (SitterCell + SouthWestStep) + ";3;0;10;Assis;1;900^100;0;0,0,0,15;-1;-1;-1;;0;1;;;;;;");
        At(control, 46100);
        using (Bitmap image = Render(view, "bot-extra-15-entry-stopped"))
        {
            UserMapControl.ActorVisualState state = State(view, 10);
            Check(!state.IsMoving && state.Animation == "static" && state.EmoteIcon == 0 && !control.AnimationQueue.IsPlaying(10),
                "Entry emote came back after the walk: " + state.AnimationName + " icon " + state.EmoteIcon);
        }

        // Combat : placement sans cercle ni aura, puis GS → cercle rouge sous l'équipe 0 (42), bleu sous l'équipe 1 (7) ; eUK ignoré.
        At(control, 50000);
        Feed(account, "GJK2|1|1|0|30000|0");
        Feed(account, "GP" + Hash.Get_Cell_Char(SelfCell) + "|" + Hash.Get_Cell_Char(PlayerCell) + "|0");
        Feed(account, "GM|+" + SelfCell + ";1;0;42;Moi;1;900^100;0;10;0,0,0,52;-1;-1;-1;;100;8;3;0;0;0;0;0;0;0;0"
            + "|+" + PlayerCell + ";1;0;7;Brak;1;900^100;0;40;2,0,3,47;-1;-1;-1;;100;8;3;0;0;0;0;0;0;0;1");
        Check(account.Game.Fight.IsInFight && account.Game.Fight.IsPlacement, "Synthetic fight placement did not start");
        Check((map.GetActor(7) as PlayerActor)?.Team == 1, "Fight GM team misread");
        At(control, 50100);
        Check(control.ExtraClips.Count == 0, "Clips during placement: " + string.Join(",", control.ExtraClips.Active.Select(c => c.ActorId + ":" + c.Family)));
        using (Bitmap image = Render(view, "bot-extra-16-placement"))
            Check(Count(image, view, CircleArea(Foot(view, 42)), IsRed) == 0 && Count(image, view, CircleArea(Foot(view, 7)), IsBlue) == 0, "Circle drawn during placement");
        Feed(account, "GS");
        Check(!account.Game.Fight.IsPlacement, "GS did not start the fight");
        Check(Clip(control, 42, "cercle") != null && Clip(control, 7, "cercle") != null && Clip(control, 42, "cercle").Rgb == 0xFF0000 && Clip(control, 7, "cercle").Rgb == 0x0000FF
            && !Clip(control, 42, "cercle").Above, "Team circles missing after GS: " + string.Join(",", control.ExtraClips.Active.Select(c => c.ActorId + ":" + c.Family + "/" + c.Rgb)));
        At(control, 50200);
        using (Bitmap image = Render(view, "bot-extra-17-circles"))
        {
            Check(Count(image, view, CircleArea(Foot(view, 42)), IsRed) >= 10, "Red circle not drawn under team 0");
            Check(Count(image, view, CircleArea(Foot(view, 7)), IsBlue) >= 10, "Blue circle not drawn under team 1");
            Check(Count(image, view, CircleArea(Foot(view, 42)), IsBlue) == 0 && Count(image, view, CircleArea(Foot(view, 7)), IsRed) == 0, "Circle colours swapped");
            Check(Count(image, view, AuraArea(Foot(view, 7)), c => Near(c, AuraColor)) == 0 && Clip(control, 7, "auras") == null, "Aura drawn in fight");
        }
        routed.Clear();
        Feed(account, "eUK7|1");
        Check(routed.Count == 1 && !control.AnimationQueue.IsPlaying(7), "Emote played in fight (the client ignores it)");
        Render(view, "bot-extra-18-emote-fight").Dispose();
        Check(State(view, 7).EmoteIcon == 0, "Emote icon shown in fight (the client ignores eUK in fight)");
        At(control, 50300);
        Check(State(view, 7).AnimationName == "staticR", "Emote changed the pose in fight: " + State(view, 7).AnimationName);

        // Carte cachée : les clips sont oubliés et rien n'est ajouté ; carte affichée : ils reviennent au prochain changement.
        control.MapShown = () => false;
        Feed(account, "GM|~" + PlayerCell + ";1;0;7;Brak;1;900^100;0;40;2,0,3,47;-1;-1;-1;;100;8;3;0;0;0;0;0;0;0;1");
        Check(control.ExtraClips.Count == 0 && control.Effects.Count == 0, "Hidden map kept extra clips");
        control.MapShown = () => true;
        Feed(account, "GM|~" + PlayerCell + ";1;0;7;Brak;1;900^100;0;40;2,0,3,47;-1;-1;-1;;100;8;3;0;0;0;0;0;0;0;1");
        Check(control.ExtraClips.Count == 2, "Clips not back once the map is shown: " + control.ExtraClips.Count);

        // Paquets mal formés ou acteurs inconnus : rien ne casse.
        Feed(account, "eUKx|y");
        Feed(account, "eUK7|");
        Feed(account, "eUK999|1|abc");
        control.ShowVisual(new VisualEvent(VisualSource.Emote, 0, 999, 999, -1, null, 1));
        control.ShowVisual(new VisualEvent(VisualSource.Emote, 0, 7, 7, -1, null, int.MaxValue, 5));
        At(control, 50400);
        Check(failures.Count == 0, "Effect or step failed: " + string.Join(" | ", failures));
        Check(control.ExtraClips.Count == 2, "Malformed packets changed the clips");
    }
}
