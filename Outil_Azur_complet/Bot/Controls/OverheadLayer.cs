using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.Monstres;
using Tool_BotProtocol.Game.Perso;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>Disposition d'un texte au-dessus d'un acteur, comme les classes de surtête du client 1.34.</summary>
    public enum OverheadLayout
    {
        /// <summary>Nom centré, titre facultatif dessous (<c>TextOverHead</c>).</summary>
        Text,
        /// <summary>Emblème 30 × 30 à gauche, guilde en petit au-dessus du nom (<c>GuildOverHead</c>).</summary>
        Guild,
        /// <summary>Marchand : fond blanc, texte noir, icône du type de marchand à gauche.</summary>
        Merchant,
        /// <summary>Groupe de monstres : « Niveau N », étoiles, puis un membre par ligne (<c>TextWithTitle</c>).</summary>
        Group
    }

    /// <summary>Contenu d'une surtête : ce que le client affiche quand la souris passe sur l'acteur.</summary>
    public sealed class OverheadContent
    {
        public OverheadLayout Layout { get; set; } = OverheadLayout.Text;
        /// <summary>Texte principal (nom, ou membres du groupe séparés par des retours à la ligne).</summary>
        public string Text { get; set; } = string.Empty;
        /// <summary>Couleur RVB du texte principal (0xRRGGBB).</summary>
        public int Color { get; set; } = OverheadLayer.TextOther;
        /// <summary>Titre du joueur (« « Titre » ») ou « Niveau N » d'un groupe ; null sans titre.</summary>
        public string Title { get; set; }
        public int TitleColor { get; set; } = OverheadLayer.TextTitle;
        public string GuildName { get; set; }
        /// <summary>Emblème « fond,couleur,motif,couleur » en base 36 (paquet <c>GM</c>).</summary>
        public string Emblem { get; set; }
        /// <summary>Valeur des étoiles d'un groupe (<c>bonusValue</c>), -1 sans étoiles.</summary>
        public int Stars { get; set; } = -1;
        /// <summary>
        /// Lignes du bot sous les membres d'un groupe (capture des âmes, lot F14) : le client n'en affiche pas. Dessinées en
        /// petit, couleur <see cref="ExtraColor"/>.
        /// </summary>
        public List<string> ExtraLines { get; } = new List<string>();
        public int ExtraColor { get; set; } = OverheadLayer.TextExtra;

        public override string ToString() => (Title == null ? Text : Title + " | " + Text)
            + (ExtraLines.Count == 0 ? string.Empty : " | " + string.Join(" | ", ExtraLines));
    }

    /// <summary>
    /// Textes au-dessus des acteurs, d'après <c>onSpriteRollOver</c> et les classes <c>OverHead</c> du client 1.34 :
    /// choix du contenu selon le type d'acteur, mesures (marges de 4 px, fond noir à 70 % arrondi de 3 px),
    /// couleurs et étoiles des groupes. Les tailles sont en pixels de l'écran (le client garde aussi une taille fixe
    /// quelle que soit l'échelle de la carte) ; tout se dessine sur le fil de l'interface.
    /// </summary>
    public static class OverheadLayer
    {
        /// <summary><c>OVERHEAD_TEXT_CHARACTER</c> : joueurs, montures d'enclos.</summary>
        public const int TextCharacter = 0xFFFFFF;
        /// <summary><c>OVERHEAD_TEXT_OTHER</c> : PNJ, groupes sans alignement, percepteurs…</summary>
        public const int TextOther = 0xFFFF99;
        /// <summary><c>OVERHEAD_TEXT_TITLE</c> : « Niveau N » des groupes.</summary>
        public const int TextTitle = 0xFFFFFF;
        /// <summary>Lignes ajoutées par le bot (<see cref="OverheadContent.ExtraLines"/>) : filets de la palette Retro.</summary>
        public const int TextExtra = 0xB4AC8D;
        /// <summary><c>NPC_ALIGNMENT_COLOR</c> : neutre, Bonta, Brâkmar.</summary>
        public static readonly int[] AlignmentColors = { 0x66FF99, 0x00FFFF, 0xFF6666 };
        /// <summary><c>STARS_COLORS</c> de <c>StarsDisplayer</c> (-1 : étoile vide).</summary>
        public static readonly int[] StarsColors = { -1, 0xFFFF33, 0xFFA000, 0x009900, 0x0099CC, 0x663300, 0x222222, 0xFF0000, 0x00FF00, 0xFFFFFF, 0xFF00FF };
        public const int StarsCount = 5, StarsWidth = 10, StarsMargin = 2;
        /// <summary>Largeur de la rangée d'étoiles (5 × 10 + 4 × 2).</summary>
        public const int StarsRowWidth = StarsCount * StarsWidth + (StarsCount - 1) * StarsMargin;
        public const int HeightSpacer = 4, WidthSpacer = 4, CornerRadius = 3, EmblemSize = 30;
        /// <summary>Opacité du fond (<c>BACKGROUND_ALPHA</c> = 70 %).</summary>
        public const int BackgroundAlpha = 178;
        /// <summary>Décalages de <c>OverHead.setPosition</c> (repère de la carte) : <c>TOP_Y</c>, <c>MODERATOR_Y</c>, <c>BOTTOM_Y</c>.</summary>
        public const float TopY = -50, ModeratorY = 15, BottomY = 10;

        /// <summary>Police <c>TEXT_FORMAT</c> (Font2, 10 px).</summary>
        internal static Font TextFont => BotFonts.Get(7.5f, FontStyle.Bold);
        /// <summary>Police <c>TEXT_FORMAT2</c> des titres (Font2, 9 px).</summary>
        internal static Font TitleFont => BotFonts.Get(6.75f, FontStyle.Bold);
        /// <summary>Police <c>TEXT_SMALL_FORMAT</c> des noms de guilde (Font1, 10 px).</summary>
        internal static Font SmallFont => BotFonts.Get(7.5f);

        // ---------------------------------------------------------------- étoiles

        /// <summary>
        /// Couleur de chacune des 5 étoiles pour <paramref name="bonusValue"/>, comme <c>getStarsColor</c> : centaines
        /// = couleur de base, chaque tranche de 20 au-delà fait passer une étoile à la couleur suivante ; -1 = vide.
        /// </summary>
        public static int[] StarColors(int bonusValue)
        {
            var colors = new int[StarsCount];
            int value = Math.Max(0, bonusValue);
            int hundreds = value / 100, rest = value % 100;
            for (int star = 0; star < StarsCount; star++)
            {
                int index = hundreds + (rest > star * (100 / StarsCount) ? 1 : 0);
                colors[star] = StarsColors[Math.Min(index, StarsColors.Length - 1)];
            }
            return colors;
        }

        /// <summary>Nombre d'étoiles colorées (non vides) pour <paramref name="bonusValue"/>.</summary>
        public static int FilledStars(int bonusValue) => StarColors(bonusValue).Count(color => color != -1);

        // ---------------------------------------------------------------- contenu

        /// <summary>Couleur d'alignement (<c>NPC_ALIGNMENT_COLOR</c>), <see cref="TextOther"/> hors de 0..2.</summary>
        public static int AlignmentColor(int alignment) =>
            alignment >= 0 && alignment < AlignmentColors.Length ? AlignmentColors[alignment] : TextOther;

        /// <summary>
        /// Contenu affiché au survol de <paramref name="entity"/> (null si le client n'affiche rien). <paramref name="inFight"/> :
        /// le personnage est en combat ; <paramref name="fightRunning"/> : le combat a commencé (après le placement).
        /// </summary>
        public static OverheadContent Describe(Entites entity, bool inFight, bool fightRunning) => Describe(entity, inFight, fightRunning, 0);

        /// <summary>Comme <see cref="Describe(Entites, bool, bool)"/>, sur la carte <paramref name="mapId"/> (capture des groupes).</summary>
        public static OverheadContent Describe(Entites entity, bool inFight, bool fightRunning, int mapId)
        {
            switch (entity)
            {
                case null: return null;
                case PlayerActor player: return DescribePlayer(player, player.DisplayName, player.Level, player.Life, inFight, fightRunning);
                case CharacterClass self:
                    return DescribePlayer(null, string.IsNullOrEmpty(self.Name) ? "Vous" : self.Name, self.Level, null, inFight, fightRunning);
                case MonsterGroupActor group: return DescribeGroup(group, mapId);
                case FightMonsterActor monster:
                    return new OverheadContent
                    {
                        Text = monster.DisplayName + (fightRunning && monster.Life.HasValue ? " (" + Int(monster.Life.Value) + ")" : " (" + Int(monster.Level) + ")"),
                        Color = AlignmentColor(MonsterAlignment(monster.TemplateId))
                    };
                case MerchantActor merchant:
                    return new OverheadContent { Layout = OverheadLayout.Merchant, Text = merchant.DisplayName, Color = 0, GuildName = Empty(merchant.GuildName) };
                case CollectorActor collector:
                    if (inFight)
                        return new OverheadContent { Text = collector.DisplayName + (fightRunning && collector.Life.HasValue
                            ? " (" + Int(collector.Life.Value) + ")" : " (" + Int(collector.Level) + ")") };
                    return new OverheadContent
                    {
                        Layout = OverheadLayout.Guild, Text = string.IsNullOrEmpty(collector.Name) ? "Percepteur" : collector.Name,
                        GuildName = Empty(collector.GuildName), Emblem = collector.GuildEmblem
                    };
                case PrismActor prism:
                    return new OverheadContent
                    {
                        Text = prism.LinkedMonsterId.HasValue && LangData.Monster.Has(prism.LinkedMonsterId.Value)
                            ? LangData.Monster.Name(prism.LinkedMonsterId.Value) : prism.DisplayName,
                        Color = AlignmentColor(prism.AlignmentSide)
                    };
                case ParkMountActor mount: return DescribeParkMount(mount);
                case FightSwordsActor swords:
                    {
                        int alignment = swords.Teams.Select(team => team.Alignment).FirstOrDefault(value => value >= 0);
                        return new OverheadContent { Text = swords.DisplayName, Color = swords.Teams.Any(team => team.Alignment >= 0) ? AlignmentColor(alignment) : TextOther };
                    }
                case MapActor actor:
                    // PNJ (couleur de l'alignement de la sous-zone dans le client : donnée non transmise au bot) et inconnus.
                    return new OverheadContent { Text = actor.DisplayName, Color = TextOther };
                default:
                    return new OverheadContent { Text = entity.Name ?? string.Empty, Color = TextOther };
            }
        }

        private static OverheadContent DescribePlayer(PlayerActor player, string name, int? level, int? life, bool inFight, bool fightRunning)
        {
            string title = null; int titleColor = TextTitle;
            if (player != null && player.TitleId > 0 && LangData.IsLoaded("titles"))
            {
                title = LangData.Title.Name(player.TitleId, player.TitleParameter);
                titleColor = LangData.Title.Color(player.TitleId) ?? TextTitle;
            }
            if (player != null && player.HasGuild)
                return new OverheadContent
                {
                    Layout = OverheadLayout.Guild, Text = name, Color = TextCharacter, GuildName = player.GuildName, Emblem = player.GuildEmblem,
                    Title = title, TitleColor = titleColor
                };
            string text = name;
            if (fightRunning && life.HasValue) text += " (" + Int(life.Value) + ")";
            else if (inFight && !fightRunning && level.HasValue && level.Value > 0) text += " (" + Int(level.Value) + ")";
            return new OverheadContent { Text = text, Color = TextCharacter, Title = title, TitleColor = titleColor };
        }

        /// <summary>« Nom (niveau) » par ligne, du plus haut niveau au plus bas ; titre « Niveau » + niveau total ; étoiles.</summary>
        public static OverheadContent DescribeGroup(MonsterGroupActor group) => DescribeGroup(group, 0);

        /// <summary>
        /// Comme <see cref="DescribeGroup(MonsterGroupActor)"/>, plus la ligne de capture des âmes quand la base exportée la
        /// connaît (<see cref="SoulStones.Evaluate"/> ; <paramref name="mapId"/> 0 = carte inconnue, test d'arène omis).
        /// </summary>
        public static OverheadContent DescribeGroup(MonsterGroupActor group, int mapId)
        {
            var members = (group.Members ?? new MonsterGroupMember[0]).Where(member => member != null)
                .Select((member, index) => new { member, index }).OrderByDescending(item => item.member.Level).ThenBy(item => item.index)
                .Select(item => item.member).ToList();
            string text = members.Count == 0 ? group.DisplayName
                : string.Join("\n", members.Select(member => member.Name + " (" + Int(member.Level) + ")"));
            int alignment = members.Count == 0 ? -1 : MonsterAlignment(members[0].TemplateId);
            string level = LangData.IsLoaded("lang") && LangData.Text.Has("LEVEL") ? LangData.Text.Get("LEVEL") : "Niveau";
            var content = new OverheadContent
            {
                Layout = OverheadLayout.Group, Text = text, Color = alignment >= 0 ? AlignmentColor(alignment) : TextOther,
                Title = level + " " + Int(group.TotalLevel), TitleColor = TextTitle, Stars = group.Stars
            };
            string capture = SoulStones.Describe(SoulStones.Evaluate(group, mapId));
            if (capture != null) content.ExtraLines.Add(capture);
            return content;
        }

        private static OverheadContent DescribeParkMount(ParkMountActor mount)
        {
            string model = null;
            if (mount.ModelId > 0) LangData.Raw("rides", "RI", Int(mount.ModelId))?.TryGetValue("n", out model);
            if (string.IsNullOrEmpty(model)) model = mount.ModelId > 0 ? "Monture " + Int(mount.ModelId) : mount.DisplayName;
            string owner = string.IsNullOrEmpty(mount.OwnerName) ? "?" : mount.OwnerName;
            string text = LangData.IsLoaded("lang") && LangData.Text.Has("MOUNT_PARK_OVERHEAD")
                ? LangData.Text.Get("MOUNT_PARK_OVERHEAD", model, Int(mount.Level), owner)
                : "Monture de " + owner + "\n" + model + "\nNiveau " + Int(mount.Level);
            return new OverheadContent { Text = text.Replace("\r\n", "\n"), Color = TextCharacter };
        }

        /// <summary>Attribut <c>alignement</c> d'un monstre dans <c>monsters.xml</c> (-1 inconnu ou sans alignement).</summary>
        public static int MonsterAlignment(int templateId)
        {
            if (templateId <= 0 || !LangData.IsLoaded("monsters")) return -1;
            IReadOnlyDictionary<string, string> raw = LangData.Raw("monsters", "monstre", Int(templateId));
            return raw != null && raw.TryGetValue("alignement", out string value)
                && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int alignment) ? alignment : -1;
        }

        private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
        private static string Empty(string value) => string.IsNullOrEmpty(value) ? null : value;

        // ---------------------------------------------------------------- mesures et dessin

        private static StringFormat NewFormat(StringAlignment alignment)
        {
            var format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.Alignment = alignment;
            format.FormatFlags |= StringFormatFlags.NoClip | StringFormatFlags.MeasureTrailingSpaces;
            return format;
        }

        private static SizeF MeasureText(Graphics graphics, string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return SizeF.Empty;
            using (StringFormat format = NewFormat(StringAlignment.Near))
            {
                SizeF size = graphics.MeasureString(text, font, PointF.Empty, format);
                return new SizeF((float)Math.Ceiling(size.Width), (float)Math.Ceiling(size.Height));
            }
        }

        /// <summary>Taille de la surtête (pixels), mêmes formules que le client.</summary>
        public static Size Measure(Graphics graphics, OverheadContent content)
        {
            if (graphics == null || content == null) return Size.Empty;
            SizeF text = MeasureText(graphics, content.Text, TextFont);
            switch (content.Layout)
            {
                case OverheadLayout.Guild:
                case OverheadLayout.Merchant:
                    {
                        SizeF guild = MeasureText(graphics, content.GuildName, SmallFont);
                        int width = (int)Math.Ceiling(Math.Max(guild.Width, text.Width) + WidthSpacer * 4) + EmblemSize;
                        int height = EmblemSize + HeightSpacer * 2;
                        if (content.Layout == OverheadLayout.Guild && !string.IsNullOrEmpty(content.Title))
                        {
                            SizeF title = MeasureText(graphics, content.Title, TitleFont);
                            width = (int)Math.Ceiling(Math.Max(width, title.Width + WidthSpacer * 2));
                            height = (int)Math.Ceiling(EmblemSize + HeightSpacer * 3 + title.Height);
                        }
                        return new Size(width, height);
                    }
                case OverheadLayout.Group:
                    {
                        SizeF title = MeasureText(graphics, content.Title, TextFont);
                        SizeF extra = MeasureText(graphics, ExtraText(content), TitleFont);
                        float stars = content.Stars > -1 ? StarsRowWidth : 0;
                        int width = (int)Math.Ceiling(Math.Max(Math.Max(Math.Max(text.Width, title.Width), stars), extra.Width) + WidthSpacer * 2);
                        float height = HeightSpacer + title.Height + HeightSpacer + text.Height + HeightSpacer;
                        if (content.Stars > -1) height += StarsWidth + HeightSpacer;
                        if (extra.Height > 0) height += extra.Height + HeightSpacer;
                        return new Size(width, (int)Math.Ceiling(height));
                    }
                default:
                    {
                        if (string.IsNullOrEmpty(content.Title))
                            return new Size((int)Math.Ceiling(text.Width + WidthSpacer * 2), (int)Math.Ceiling(text.Height + HeightSpacer * 2));
                        SizeF title = MeasureText(graphics, content.Title, TitleFont);
                        return new Size((int)Math.Ceiling(Math.Max(text.Width, title.Width) + WidthSpacer * 2),
                            (int)Math.Ceiling(text.Height + title.Height + HeightSpacer * 3));
                    }
            }
        }

        /// <summary>
        /// Coin haut-gauche de la surtête de taille <paramref name="size"/> pour un acteur dont le pied est en
        /// <paramref name="foot"/> (écran) et dont le sprite mesure <paramref name="spriteHeight"/> pixels de la carte, comme
        /// <c>OverHead.setPosition</c> : au-dessus de la tête (−65, −50 ou −35 selon la taille du sprite), sous le pied
        /// si elle sortirait par le haut, et ramenée dans la largeur de la vue. <paramref name="scale"/> : échelle de la carte.
        /// </summary>
        public static Point Place(PointF foot, float spriteHeight, Size size, float scale, Rectangle view)
        {
            scale = scale > 0 ? scale : 1;
            float top = Math.Abs(TopY);
            float bottom;
            if (foot.Y - view.Top < (top + ModeratorY) * scale + size.Height)
                bottom = foot.Y + BottomY * scale + size.Height;
            else if (spriteHeight > top + ModeratorY) bottom = foot.Y + (TopY - ModeratorY) * scale;
            else if (spriteHeight < top - ModeratorY) bottom = foot.Y + (TopY + ModeratorY) * scale;
            else bottom = foot.Y + TopY * scale;
            float center = foot.X;
            float half = size.Width / 2f;
            if (center < view.Left + half) center = view.Left + half;
            if (center > view.Right - half) center = view.Right - half;
            return new Point((int)Math.Round(center - half), (int)Math.Round(bottom - size.Height));
        }

        /// <summary>Dessine la surtête dans <paramref name="box"/> (coin et taille de <see cref="Place"/> et <see cref="Measure"/>).</summary>
        public static void Draw(Graphics graphics, OverheadContent content, Rectangle box, OverheadImages images)
        {
            if (graphics == null || content == null || box.Width <= 0 || box.Height <= 0) return;
            SmoothingMode smoothing = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                Color background = content.Layout == OverheadLayout.Merchant ? Color.FromArgb(BackgroundAlpha, 255, 255, 255) : Color.FromArgb(BackgroundAlpha, 0, 0, 0);
                using (GraphicsPath path = RoundRect(box, CornerRadius))
                using (var brush = new SolidBrush(background))
                    graphics.FillPath(brush, path);
                Color ink = Rgb(content.Color);
                switch (content.Layout)
                {
                    case OverheadLayout.Guild:
                    case OverheadLayout.Merchant:
                        DrawGuild(graphics, content, box, ink, images);
                        break;
                    case OverheadLayout.Group:
                        DrawGroup(graphics, content, box, ink, images);
                        break;
                    default:
                        {
                            SizeF text = MeasureText(graphics, content.Text, TextFont);
                            float y = box.Top + HeightSpacer - 3 + 2;
                            DrawCentered(graphics, content.Text, TextFont, ink, box, y);
                            if (!string.IsNullOrEmpty(content.Title))
                                DrawCentered(graphics, content.Title, TitleFont, Rgb(content.TitleColor), box, y + text.Height + HeightSpacer);
                            break;
                        }
                }
            }
            finally { graphics.SmoothingMode = smoothing; }
        }

        private static void DrawGuild(Graphics graphics, OverheadContent content, Rectangle box, Color ink, OverheadImages images)
        {
            var emblemBox = new Rectangle(box.Left + WidthSpacer, box.Top + HeightSpacer, EmblemSize, EmblemSize);
            if (content.Layout == OverheadLayout.Guild)
            {
                Bitmap emblem = string.IsNullOrEmpty(content.Emblem) || images == null ? null
                    : images.TryGet("emblem:" + content.Emblem, () => ClientAssets.Emblem(content.Emblem, EmblemSize, shadow: true));
                if (emblem != null) graphics.DrawImage(emblem, emblemBox);
            }
            else
            {
                // Icône du type de marchand (clips/extra/<type>.swf) : non exportée, cadre neutre à sa place.
                using (var pen = new Pen(Color.FromArgb(120, 0, 0, 0))) graphics.DrawRectangle(pen, emblemBox.X + 4, emblemBox.Y + 4, emblemBox.Width - 9, emblemBox.Height - 9);
            }
            float x = box.Left + EmblemSize + WidthSpacer * 2;
            bool hasGuild = !string.IsNullOrEmpty(content.GuildName);
            using (var brush = new SolidBrush(content.Layout == OverheadLayout.Merchant ? Color.Black : Color.White))
            using (StringFormat format = NewFormat(StringAlignment.Near))
            {
                if (hasGuild) graphics.DrawString(content.GuildName, SmallFont, brush, new PointF(x, box.Top - 2 + HeightSpacer + 2), format);
                using (var inkBrush = new SolidBrush(content.Layout == OverheadLayout.Merchant ? Color.Black : ink))
                    graphics.DrawString(content.Text, TextFont, inkBrush, new PointF(x, box.Top + (hasGuild ? 13 + HeightSpacer + 2 : 9 + HeightSpacer)), format);
            }
            if (content.Layout == OverheadLayout.Guild && !string.IsNullOrEmpty(content.Title))
                DrawCentered(graphics, content.Title, TitleFont, Rgb(content.TitleColor), box, box.Top + 27 + HeightSpacer * 2);
        }

        private static void DrawGroup(Graphics graphics, OverheadContent content, Rectangle box, Color ink, OverheadImages images)
        {
            float y = box.Top + HeightSpacer;
            SizeF title = MeasureText(graphics, content.Title, TextFont);
            DrawCentered(graphics, content.Title, TextFont, Rgb(content.TitleColor), box, y);
            y += title.Height + HeightSpacer;
            if (content.Stars > -1)
            {
                DrawStars(graphics, content.Stars, new PointF(box.Left + box.Width / 2f - StarsRowWidth / 2f, y), images);
                y += StarsWidth + HeightSpacer;
            }
            DrawCentered(graphics, content.Text, TextFont, ink, box, y);
            string extra = ExtraText(content);
            if (extra != null)
                DrawCentered(graphics, extra, TitleFont, Rgb(content.ExtraColor), box, y + MeasureText(graphics, content.Text, TextFont).Height + HeightSpacer);
        }

        private static string ExtraText(OverheadContent content) =>
            content == null || content.ExtraLines.Count == 0 ? null : string.Join("\n", content.ExtraLines);

        /// <summary>
        /// Rangée de 5 étoiles de 10 px (marge 2) à partir de <paramref name="origin"/> : contour toujours visible, intérieur
        /// recoloré par <see cref="StarColors"/> (transparent pour -1), avec les images <c>StarBorder_fill</c> et
        /// <c>StarBorder_contour</c> du client ; repli vectoriel tant qu'elles ne sont pas lues ou si elles manquent.
        /// </summary>
        public static void DrawStars(Graphics graphics, int bonusValue, PointF origin, OverheadImages images)
        {
            int[] colors = StarColors(bonusValue);
            Bitmap contour = images?.File("UI/Client/StarBorder_contour.png");
            for (int star = 0; star < StarsCount; star++)
            {
                var cell = new RectangleF(origin.X + star * (StarsWidth + StarsMargin), origin.Y, StarsWidth, StarsWidth);
                Bitmap fill = colors[star] == -1 || images == null ? null : images.Tinted("UI/Client/StarBorder_fill.png", colors[star]);
                if (fill != null) graphics.DrawImage(fill, cell);
                else if (colors[star] != -1)
                    using (var brush = new SolidBrush(Rgb(colors[star])))
                        graphics.FillPolygon(brush, StarPoints(cell, .9f));
                if (contour != null) graphics.DrawImage(contour, cell);
                else using (var pen = new Pen(Color.FromArgb(220, 30, 26, 18), 1f)) graphics.DrawPolygon(pen, StarPoints(cell, 1f));
            }
        }

        private static PointF[] StarPoints(RectangleF cell, float factor)
        {
            var points = new PointF[10];
            float cx = cell.Left + cell.Width / 2, cy = cell.Top + cell.Height / 2 + .5f;
            float outer = cell.Width / 2 * factor, inner = outer * .45f;
            for (int i = 0; i < 10; i++)
            {
                double angle = -Math.PI / 2 + i * Math.PI / 5;
                float radius = i % 2 == 0 ? outer : inner;
                points[i] = new PointF(cx + (float)(Math.Cos(angle) * radius), cy + (float)(Math.Sin(angle) * radius));
            }
            return points;
        }

        private static void DrawCentered(Graphics graphics, string text, Font font, Color color, Rectangle box, float y)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (var brush = new SolidBrush(color))
            using (StringFormat format = NewFormat(StringAlignment.Center))
                graphics.DrawString(text, font, brush, new RectangleF(box.Left, y, box.Width, Math.Max(1, box.Bottom - y)), format);
        }

        internal static Color Rgb(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

        internal static GraphicsPath RoundRect(RectangleF box, float radius)
        {
            var path = new GraphicsPath();
            float diameter = Math.Min(radius * 2, Math.Min(box.Width, box.Height));
            if (diameter <= 0) { path.AddRectangle(box); return path; }
            path.AddArc(box.Left, box.Top, diameter, diameter, 180, 90);
            path.AddArc(box.Right - diameter, box.Top, diameter, diameter, 270, 90);
            path.AddArc(box.Right - diameter, box.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(box.Left, box.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ---------------------------------------------------------------- teinte de sélection

        /// <summary>
        /// Matrice de <c>selectSprite</c> : chaque canal à 60 % plus 102 (0,4), qui éclaircit le sprite survolé.
        /// L'appelant libère l'objet rendu.
        /// </summary>
        public static System.Drawing.Imaging.ImageAttributes SelectionTint()
        {
            var matrix = new System.Drawing.Imaging.ColorMatrix(new[]
            {
                new[] { .6f, 0, 0, 0, 0 }, new[] { 0, .6f, 0, 0, 0 }, new[] { 0, 0, .6f, 0, 0 },
                new[] { 0, 0, 0, 1f, 0 }, new[] { .4f, .4f, .4f, 0, 1f }
            });
            var attributes = new System.Drawing.Imaging.ImageAttributes();
            attributes.SetColorMatrix(matrix);
            return attributes;
        }
    }
}
