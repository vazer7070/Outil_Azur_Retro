using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.Maps.Mouvements;
using Tool_BotProtocol.Utils.Crypto;

// Rendu des acteurs de la carte (lot M1) : miroir des orientations 3/4/7, bande de marche à 40 images/s, survol et clic
// au pixel près hors de la cellule, teinte de sélection, surtête des groupes (« Niveau N », étoiles), bulles minutées par
// une horloge injectée, smileys et émotes, PNG absent ou illisible → silhouette. Sprites et carte synthétiques dans un
// dossier temporaire, rendu par Graphics.FromImage, sans capture de fenêtre ni ressource du dépôt : passe sous Mono.
internal static class BotActorRenderSmoke
{
    private const int MapWidth = 8, MapHeight = 8;

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
            Layers();
            Run();
            Console.WriteLine("OK: orientation 3 mirrors R, walk strip frames, pixel-exact hit test above the cell, selection tint, "
                + "group overhead (Niveau N, stars), timed bubbles, chat emote/think bubbles, emote icons, path preview, missing/unreadable PNG silhouettes");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    private sealed class TestView : UserMapControl
    {
        public TestView(Func<double> clock, string sprites, string overheads) : base(clock, sprites, overheads) { }
        public void Move(Point point) { OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, point.X, point.Y, 0)); }
        public void Click(Point point, MouseButtons button)
        {
            OnMouseDown(new MouseEventArgs(button, 1, point.X, point.Y, 0));
            OnMouseUp(new MouseEventArgs(button, 1, point.X, point.Y, 0));
        }
    }

    private static string EncodedCell()
    {
        // Cellule active, praticable, en ligne de vue, sans sol ni objet (aucun PNG de décor demandé).
        int[] value = { 33, 7, 32, 0, 4, 0, 0, 0, 0, 0 };
        return new string(value.Select(part => Hash.caracteres_array[part]).ToArray());
    }

    /// <summary>Pose fixe 20×60 : moitié gauche rouge, moitié droite bleue, haut réduit à une colonne centrale (coins transparents).</summary>
    private static void Tall(string path)
    {
        using (var bitmap = new Bitmap(20, 60, PixelFormat.Format32bppArgb))
        {
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.FillRectangle(Brushes.Red, 0, 20, 10, 40);
                graphics.FillRectangle(Brushes.Blue, 10, 20, 10, 40);
                graphics.FillRectangle(Brushes.Gold, 6, 0, 8, 20);
            }
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    /// <summary>Bande de <paramref name="frames"/> images 20×60 de couleurs différentes (ancres dans ancres.tsv).</summary>
    private static void Strip(string path, int frames)
    {
        using (var bitmap = new Bitmap(20 * frames, 60, PixelFormat.Format32bppArgb))
        {
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                for (int frame = 0; frame < frames; frame++)
                    using (var brush = new SolidBrush(Color.FromArgb(255, 40 + frame * 50, 200, 40)))
                        graphics.FillRectangle(brush, frame * 20 + 2, 0, 16, 60);
            }
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    private static void Small(string path, Color color)
    {
        using (var bitmap = new Bitmap(14, 36, PixelFormat.Format32bppArgb))
        {
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                using (var brush = new SolidBrush(color)) graphics.FillRectangle(brush, 1, 2, 12, 34);
            }
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    private static Bitmap Render(UserMapControl view, string name)
    {
        Check(view.WaitForActorSprites(10000), "Actor PNG decoding did not finish");
        var bitmap = new Bitmap(view.Width, view.Height);
        using (Graphics graphics = Graphics.FromImage(bitmap)) view.DrawCells(graphics);
        bitmap.Save(Path.Combine(TestPaths.Work, name + ".png"), ImageFormat.Png);
        return bitmap;
    }

    private static Color At(Bitmap image, RectangleF bounds, float fx, float fy) =>
        image.GetPixel((int)(bounds.Left + bounds.Width * fx), (int)(bounds.Top + bounds.Height * fy));

    private static bool Near(Color pixel, Color expected) =>
        Math.Abs(pixel.R - expected.R) < 24 && Math.Abs(pixel.G - expected.G) < 24 && Math.Abs(pixel.B - expected.B) < 24;

    private static int Count(Bitmap image, Func<Color, bool> match)
    {
        int count = 0;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                if (match(image.GetPixel(x, y))) count++;
        return count;
    }

    private static ChatMessage Channel(long author, string raw)
    {
        ConstructorInfo constructor = typeof(ChatMessage).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == 8);
        return (ChatMessage)constructor.Invoke(new object[] { ChatMessageKind.Channel, ChatChannels.Default, "*", (long?)author, "Auteur", raw, null, null });
    }

    /// <summary>Calculs sans vue : étoiles, contenu de la surtête d'un groupe, durée et texte des bulles.</summary>
    private static void Layers()
    {
        Check(OverheadLayer.FilledStars(0) == 0 && OverheadLayer.FilledStars(45) == 3 && OverheadLayer.FilledStars(160) == 5,
            "Star count differs from getStarsColor (0 → 0, 45 → 3, 160 → 5)");
        int[] stars = OverheadLayer.StarColors(160);
        Check(stars.Take(3).All(color => color == 0xFFA000) && stars.Skip(3).All(color => color == 0xFFFF33),
            "Star colors for 160 must be three orange then two yellow");
        Check(OverheadLayer.StarColors(45).SequenceEqual(new[] { 0xFFFF33, 0xFFFF33, 0xFFFF33, -1, -1 }), "Star colors for 45 must be three yellow, two empty");

        GmParseResult parsed = GmParser.Parse("GM|+81;1;45;-2;101,102,103;-3;1001^100,1002^100,1003^100;5,7,6;-1,-1,-1;0,0,0,0;-1,-1,-1;0,0,0,0;-1,-1,-1;0,0,0,0"
            + "|+83;1;160;-3;101,102;-3;1001^100,1002^100;10,20;-1,-1,-1;0,0,0,0;-1,-1,-1;0,0,0,0", false, 42, -1);
        Check(parsed.Rejected.Count == 0 && parsed.Entries.Count == 2, "Synthetic group GM entries were rejected");
        var group = (MonsterGroupActor)parsed.Entries[0].Actor;
        var strong = (MonsterGroupActor)parsed.Entries[1].Actor;
        OverheadContent overhead = OverheadLayer.DescribeGroup(group);
        Check(overhead.Layout == OverheadLayout.Group && overhead.Title == "Niveau 18", "Group overhead title is not « Niveau 18 »: " + overhead.Title);
        string[] lines = overhead.Text.Split('\n');
        Check(lines.Length == 3 && lines[0].EndsWith("(7)") && lines[1].EndsWith("(6)") && lines[2].EndsWith("(5)"),
            "Group members are not listed by decreasing level: " + overhead.Text);
        Check(overhead.Stars == 45 && OverheadLayer.FilledStars(overhead.Stars) == 3, "Group overhead lost its bonus stars");
        OverheadContent second = OverheadLayer.DescribeGroup(strong);
        Check(second.Title == "Niveau 30" && OverheadLayer.FilledStars(second.Stars) == 5, "Second group overhead: wrong level or stars");
        using (var bitmap = new Bitmap(200, 120))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            Size size = OverheadLayer.Measure(graphics, overhead);
            Check(size.Width >= OverheadLayer.StarsRowWidth + 8 && size.Height > 30, "Group overhead does not leave room for the star row");
            OverheadLayer.Draw(graphics, overhead, new Rectangle(4, 4, size.Width, size.Height), null);
        }

        Check(BubbleLayer.DurationFor("Bonjour") == 4000 + 7 * 50, "Bubble duration is not 4000 + 50 per character");
        BubbleKind kind;
        Check(BubbleLayer.BubbleText(Channel(43, "*danse.*"), out kind) == "*Danse*" && kind == BubbleKind.Chat, "Emote bubble text was not capitalised without its final dot");
        Check(BubbleLayer.BubbleText(Channel(43, "!THINK!hmm"), out kind) == "hmm" && kind == BubbleKind.Think, "!THINK! did not give a think bubble");
        Check(BubbleLayer.BubbleText(Channel(43, "**12**"), out kind) == null, "A speaking item produced a bubble");
        Check(BubbleLayer.BubbleText(null, out kind) == null, "A null message produced a bubble");
    }

    private static void Run()
    {
        string sprites = Path.Combine(TestPaths.Work, "actor-render-sprites");
        string overheads = Path.Combine(TestPaths.Work, "actor-render-overheads");
        if (Directory.Exists(sprites)) Directory.Delete(sprites, true);
        Directory.CreateDirectory(sprites); Directory.CreateDirectory(overheads);
        Tall(Path.Combine(sprites, "10R.png"));
        foreach (string suffix in new[] { "S", "R", "F", "L", "B" })
        {
            Strip(Path.Combine(sprites, "10_walk" + suffix + ".png"), 4);
            Strip(Path.Combine(sprites, "10_run" + suffix + ".png"), 4);
        }
        File.WriteAllLines(Path.Combine(sprites, "ancres.tsv"), new[] { "gfx\tanim\txmin\tymin\twidth\theight\timages" }
            .Concat(new[] { "S", "R", "F", "L", "B" }.SelectMany(suffix => new[]
            {
                "10\twalk" + suffix + "\t-10\t-60\t20\t60\t4", "10\trun" + suffix + "\t-10\t-60\t20\t60\t4"
            })));
        Small(Path.Combine(sprites, "1001R.png"), Color.ForestGreen);
        Small(Path.Combine(sprites, "1002R.png"), Color.DarkOliveGreen);
        Small(Path.Combine(sprites, "1003R.png"), Color.SeaGreen);
        File.WriteAllBytes(Path.Combine(sprites, "77F.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4, 5 });
        Directory.CreateDirectory(Path.Combine(overheads, "Smileys"));
        using (var icon = new Bitmap(20, 20))
        {
            using (Graphics graphics = Graphics.FromImage(icon)) graphics.Clear(Color.Magenta);
            icon.Save(Path.Combine(overheads, "Smileys", "3.png"), ImageFormat.Png);
        }

        int count = MapHeight * (2 * MapWidth - 1) - (MapWidth - 1);
        string data = string.Concat(Enumerable.Repeat(EncodedCell(), count));
        using (var account = new Accounts(new AccountConfig("synthetic-actor-render", "Synthetic123", "test")))
        {
            Map map = account.Game.Map;
            map.MapID = 991301; map.MapWidth = MapWidth; map.MapHeight = MapHeight; map.MapData = data; map.DecompressMap(data);
            Check(map.MapCells.Length == count, "Synthetic map was not decoded");
            GmParseResult parsed = GmParser.Parse("GM"
                + "|+61;1;0;43;Alice;1;10^100;0;0,0,0,0;-1;-1;-1;;0;;;;;0;;0"
                + "|+63;3;0;44;Bruno;1;10^100;0;0,0,0,0;-1;-1;-1;;0;;;;;0;;0"
                + "|+81;1;45;-2;101,102,103;-3;1001^100,1002^100,1003^100;5,7,6;-1,-1,-1;0,0,0,0;-1,-1,-1;0,0,0,0;-1,-1,-1;0,0,0,0"
                + "|+91;2;0;45;Absent;1;999999^100;0;0,0,0,0;-1;-1;-1;;0;;;;;0;;0"
                + "|+93;2;0;46;Illisible;1;77^100;0;0,0,0,0;-1;-1;-1;;0;;;;;0;;0", false, 42, -1);
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
                var clicked = new List<Entites>();
                var hovered = new List<Entites>();
                int cellClicks = 0;
                view.ActorClicked += (actor, cell, button, modifiers) => clicked.Add(actor);
                view.ActorHovered += actor => hovered.Add(actor);
                view.CellClicked += (cell, button, moved) => cellClicks++;

                Bitmap first = Render(view, "bot-rendu-acteurs");
                UserMapControl.ActorVisualState alice = view.GetActorVisualState(43), bruno = view.GetActorVisualState(44);
                UserMapControl.ActorVisualState[] all = view.GetActorVisualStates();
                Check(alice.HasSprite && bruno.HasSprite && !alice.IsMirrored && bruno.IsMirrored && alice.SpriteReason == null && bruno.SpriteReason == null,
                    "Orientations 1 and 3 must both use the R pose, the second mirrored");
                Check(alice.Depth == 61 * 100 + 30 && all.Select(state => state.Depth).SequenceEqual(all.Select(state => state.Depth).OrderBy(depth => depth)),
                    "Actors are not ordered by the client depth (cell × 100 + 30)");
                using (first)
                {
                    Check(Near(At(first, alice.SpriteBounds, .25f, .75f), Color.Red) && Near(At(first, alice.SpriteBounds, .75f, .75f), Color.Blue),
                        "Orientation 1 is not drawn as the R pose");
                    Check(Near(At(first, bruno.SpriteBounds, .25f, .75f), Color.Blue) && Near(At(first, bruno.SpriteBounds, .75f, .75f), Color.Red),
                        "Orientation 3 is not the mirror image of R");
                }

                // Groupe : chef + deux membres autour (option ViewAllMonsterInGroup, vraie par défaut), même entité au clic.
                UserMapControl.ActorVisualState[] group = all.Where(state => state.ActorId == -2).ToArray();
                Check(view.ViewAllMonsterInGroup && group.Length == 3 && group.All(state => state.HasSprite && state.Entity is MonsterGroupActor)
                    && group.Count(state => state.MemberIndex < 0) == 1, "Monster group members are not drawn around the leader");
                view.ViewAllMonsterInGroup = false;
                Check(view.GetActorVisualStates().Count(state => state.ActorId == -2) == 1, "ViewAllMonsterInGroup=false still draws the members");
                view.ViewAllMonsterInGroup = true;

                // PNG absent ou illisible : silhouette, raison et diagnostic, sans exception au dessin ni au clic.
                UserMapControl.ActorVisualState absent = view.GetActorVisualState(45), unreadable = view.GetActorVisualState(46);
                Check(!absent.HasSprite && absent.SpriteReason != null && absent.SpriteReason.Contains("999999"), "Missing PNG did not give a silhouette with its reason");
                Check(!unreadable.HasSprite && unreadable.SpriteReason != null && unreadable.SpriteReason.Contains("illisible"), "Unreadable PNG was not reported");
                Check(view.MissingAssetCount >= 2 && view.ArtworkStatus.Contains("sprite(s) absent(s)"), "Missing sprites are not counted in the map status");
                Check(view.HitTest(Point.Round(new PointF(absent.SpriteBounds.Left + absent.SpriteBounds.Width / 2, absent.SpriteBounds.Top + absent.SpriteBounds.Height / 2)))?.Id == 45,
                    "A silhouette cannot be clicked");

                // Clic au pixel près : au-dessus de la cellule mais sur le sprite → acteur ; coin transparent du cadre → pas d'acteur.
                var head = Point.Round(new PointF(alice.SpriteBounds.Left + alice.SpriteBounds.Width / 2, alice.SpriteBounds.Top + alice.SpriteBounds.Height * .1f));
                Check(view.GetCell(head)?.id != alice.CellId, "Test point should lie outside the actor's cell");
                Check(view.HitTest(head)?.Id == 43, "Sprite pixel above the cell is not hit");
                var corner = Point.Round(new PointF(alice.SpriteBounds.Left + alice.SpriteBounds.Width * .1f, alice.SpriteBounds.Top + alice.SpriteBounds.Height * .1f));
                Check(view.HitTest(corner) == null, "Transparent corner of the sprite frame was hit");
                view.Click(head, MouseButtons.Left);
                Check(clicked.Count == 1 && clicked[0] is PlayerActor && ((PlayerActor)clicked[0]).Id == 43 && cellClicks == 0,
                    "Click on the sprite did not raise ActorClicked for the actor (or also clicked the cell)");
                view.Click(Point.Round(view.Cells[30].Centre), MouseButtons.Left);
                Check(clicked.Count == 1 && cellClicks == 1, "Click outside any sprite did not stay a cell click");

                // Survol : teinte de sélection sur le sprite principal et surtête au-dessus.
                Rectangle above = Rectangle.Intersect(new Rectangle(0, 0, view.Width, view.Height), new Rectangle((int)alice.SpriteBounds.Left - 25,
                    (int)alice.SpriteBounds.Top - 45, (int)alice.SpriteBounds.Width + 50, 70));
                Func<Bitmap, int> darkAbove = image =>
                {
                    int dark = 0;
                    for (int y = above.Top; y < above.Bottom; y++)
                        for (int x = above.Left; x < above.Right; x++)
                        {
                            Color c = image.GetPixel(x, y);
                            if (c.R < 90 && c.G < 90 && c.B < 90) dark++;
                        }
                    return dark;
                };
                Color plain; int plainDark;
                using (Bitmap before = Render(view, "bot-rendu-acteurs-sans-survol")) { plain = At(before, alice.SpriteBounds, .25f, .75f); plainDark = darkAbove(before); }
                view.Move(head);
                Check(view.HoveredActor is PlayerActor && hovered.Count == 1 && view.GetActorVisualState(43).IsHovered, "Hover did not select the actor");
                using (Bitmap tinted = Render(view, "bot-rendu-acteurs-survol"))
                {
                    Color pixel = At(tinted, alice.SpriteBounds, .25f, .75f);
                    Check(plain.G < 40 && pixel.G > 70 && pixel.R > 200, "Hovered sprite was not tinted (0.6 × couleur + 0.4)");
                    Check(darkAbove(tinted) > plainDark + 150, "Hovered player has no overhead above the sprite");
                }
                view.Move(Point.Round(view.Cells[30].Centre));
                Check(view.HoveredActor == null && hovered.Count == 2 && hovered[1] == null, "Leaving the sprite did not clear the hover");

                // Surtête des groupes (raccourci SHOWMONSTERSTOOLTIP) : fond sombre au-dessus du chef.
                int baseline;
                using (Bitmap plainGroups = Render(view, "bot-rendu-groupes")) baseline = Count(plainGroups, c => c.R < 90 && c.G < 90 && c.B < 90);
                view.ShowMonstersTooltip = true;
                using (Bitmap withTip = Render(view, "bot-rendu-groupes-surtete"))
                    Check(Count(withTip, c => c.R < 90 && c.G < 90 && c.B < 90) > baseline + 400, "Monster group overhead was not drawn");
                view.ShowMonstersTooltip = false;

                // Aperçu du chemin (#FF6600) sur la cellule survolée.
                view.SetPathPreview(new short[] { 62 });
                using (Bitmap preview = Render(view, "bot-rendu-chemin"))
                {
                    Color pixel = preview.GetPixel(view.Cells[62].Centre.X, view.Cells[62].Centre.Y);
                    Check(view.PathPreview.Count == 1 && pixel.R - pixel.B > 100, "Path preview cell is not orange");
                }
                view.SetPathPreview(null);
                Check(view.PathPreview.Count == 0, "Path preview was not cleared");

                // Bulles : durée 4000 + 50 × longueur, horloge injectée ; refusées pour un acteur absent.
                Check(!view.ShowBubble(9999, "Personne"), "Bubble accepted for an absent actor");
                Check(view.ShowBubble(43, "Bonjour"), "Bubble refused for a visible actor");
                int duration = BubbleLayer.DurationFor("Bonjour");
                Func<Color, bool> paper = c => Math.Abs(c.R - 0xFF) < 4 && Math.Abs(c.G - 0xFF) < 4 && Math.Abs(c.B - 0xCE) < 4;
                using (Bitmap shown = Render(view, "bot-rendu-bulle"))
                    Check(view.BubbleOf(43) != null && Count(shown, paper) > 150, "Bubble was not drawn");
                clock = 1000 + duration - 1;
                Check(view.BubbleOf(43) != null, "Bubble disappeared before its duration");
                clock = 1000 + duration;
                Check(view.BubbleOf(43) == null, "Bubble outlived its computed duration");
                using (Bitmap gone = Render(view, "bot-rendu-bulle-expiree"))
                    Check(Count(gone, paper) == 0, "Expired bubble is still drawn");
                Check(view.ShowChatMessage(Channel(44, "!THINK!hmm")) && view.BubbleOf(44).Kind == BubbleKind.Think, "Think message did not give a think bubble");
                using (Bitmap think = Render(view, "bot-rendu-pensee"))
                    Check(Count(think, c => c.R > 240 && c.G > 240 && c.B > 235) > 300, "Think bubble was not drawn");
                view.ChatEffects = false;
                Check(view.BubbleOf(44) == null && !view.ShowBubble(43, "Silence"), "ChatEffects=false still shows bubbles");
                view.ChatEffects = true;

                // Émotes sans bande exportée : icône, gardée tant qu'on est assis (1), 3 s sinon ; smiley 3 s.
                view.ShowEmote(44, 1);
                Check(view.GetActorVisualState(44).EmoteIcon == 1, "Sitting emote icon missing");
                clock += 60000;
                Check(view.GetActorVisualState(44).EmoteIcon == 1, "Sitting emote icon expired while still sitting");
                view.ShowEmote(44, 5);
                Check(view.GetActorVisualState(44).EmoteIcon == 5, "Emote icon was not replaced");
                clock += BubbleLayer.EmoteIconDelay;
                Check(view.GetActorVisualState(44).EmoteIcon == 0, "Temporary emote icon outlived 3 s");
                view.ShowSmiley(44, 3);
                Func<Color, bool> magenta = c => c.R > 230 && c.G < 30 && c.B > 230;
                Render(view, "bot-rendu-smiley").Dispose(); // premier dessin : l'icône est demandée puis lue hors du fil de l'interface
                using (Bitmap smiley = Render(view, "bot-rendu-smiley"))
                    Check(Count(smiley, magenta) > 100, "Smiley icon was not drawn above the actor");
                clock += BubbleLayer.SmileyDelay;
                using (Bitmap after = Render(view, "bot-rendu-smiley-expire"))
                    Check(Count(after, magenta) == 0, "Smiley outlived its 3 s");
                view.ForgetActor(44);

                // Marche : bande walk/run de l'allure du client, image à 40 par seconde, cellule du pas en cours.
                var path = new List<Cell> { map.MapCells[61], map.MapCells[69], map.MapCells[77] };
                AnimDuration timing = AnimDuration.Compute(path, UserMapControl.MovementProfile(account, 43));
                double start = clock;
                view.AddAnimations(43, path, timing, AnimationType.ENTITES);
                clock = start + 75;
                Check(view.WaitForActorSprites(10000), "Walk strip decoding did not finish");
                UserMapControl.ActorVisualState walking = view.GetActorVisualState(43);
                string strip = timing.Mode == MoveMode.Walk ? "walk" : "run";
                Check(walking.IsMoving && walking.HasSprite && walking.Animation == strip, "Moving actor does not use its " + strip + " strip: " + walking.Animation);
                Check(walking.Frame == (int)(75 * ActorSprites.FramesPerSecond / 1000.0) % 4 && walking.Orientation == 1 && walking.CellId == 69,
                    "Walk strip frame, direction or current cell is wrong");
                Check(!view.ShowBubble(43, "En route"), "Bubble accepted for a moving actor");
                using (Bitmap moving = Render(view, "bot-rendu-marche")) { }
                clock = start + timing.Total + 1;
                walking = view.GetActorVisualState(43);
                Check(!walking.IsMoving && walking.Animation == "static", "Finished movement did not return to the static pose");
            }
        }
    }
}
