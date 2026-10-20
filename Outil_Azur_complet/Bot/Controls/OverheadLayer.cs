using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
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
        /// <summary>
        /// Image des ailes d'alignement derrière le nom (lot AN8) : grade (Bonta), 10 + grade (Brâkmar), 20 + grade (mercenaire),
        /// 1 à 30 ; 0 sans ailes (grade nul, neutre, acteur sans alignement).
        /// </summary>
        public int WingsFrame { get; set; }
        /// <summary>Ange ou démon déchu (5e champ de l'alignement du <c>GM</c>) : <c>fallenDemonAngel.swf</c> à la place de <c>demonangel.swf</c>.</summary>
        public bool FallenWings { get; set; }
        /// <summary><c>pvpGain</c> du client : -1 ailes assombries (RVB × 0,5), 1 lueur de la couleur du camp, 0 rien.</summary>
        public int PvpGain { get; set; }
        /// <summary>Type de marchand hors ligne (<c>offlineType</c>) : icône <c>Effets/extra/&lt;type&gt;_scene.png</c> à gauche du nom.</summary>
        public int MerchantType { get; set; }

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

        // ---------------------------------------------------------------- ailes d'alignement (lot AN8)

        /// <summary>Images des deux SWF d'ailes (<c>gotoAndStop(image)</c> du client, 1 à 30).</summary>
        public const int WingsFrames = 30;
        /// <summary>Dossier des bandes d'ailes sous <c>ressources/Bot</c> ; bandes <c>1_scene.png</c> (<c>demonangel</c>) et <c>2_scene.png</c> (<c>fallenDemonAngel</c>).</summary>
        public const string WingsFolder = "Alignments/ailes";
        /// <summary><c>GlowFilter</c> d'<c>addPvpGfxEffect</c> : couleur par camp de l'image (<c>floor((image − 1) / 10)</c>), alpha 0,5, flou 10 × 10, force 2.</summary>
        public static readonly int[] GlowColors = { 0xAAEAFF, 0xCC0000, 0x000000 };
        public const float GlowAlpha = .5f, GlowStrength = 2f;
        public const int GlowBlur = 10, GlowQuality = 3;
        /// <summary><c>ColorMatrixFilter</c> de <c>pvpGain</c> −1 : chaque composante à 50 %.</summary>
        public const float DarkenFactor = .5f;
        /// <summary>Valeurs de <c>lang.xml</c> (<c>PVP_VIEW_BONUS_*</c>) employées quand la configuration n'est pas chargée.</summary>
        public const int DefaultPvpMinorLimit = 20, DefaultPvpMinorLimitPrc = 20, DefaultPvpMajorLimit = -20, DefaultPvpMajorLimitPrc = 20;

        /// <summary>Personnage du compte tel que la surtête le voit pour le <c>pvpGain</c> d'un autre joueur.</summary>
        public sealed class PvpViewer
        {
            public PvpViewer(long id, int level, int side) { Id = id; Level = level; Side = side; }
            public long Id { get; }
            public int Level { get; }
            /// <summary>Camp : 0 neutre, 1 Bonta, 2 Brâkmar, 3 mercenaire.</summary>
            public int Side { get; }
        }

        /// <summary>
        /// Personnage du compte pour le <c>pvpGain</c> des ailes (niveau et camp), posé par la carte (lot AN8) ; null : aucun
        /// <c>pvpGain</c>. Un seul pour le processus : la carte le repose à chaque survol.
        /// </summary>
        public static PvpViewer Viewer { get; set; }

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
                        Text = monster.DisplayName + (fightRunning && monster.Life.HasValue ? " (" + Int(monster.Life.Value) + ")"
                            : monster.Level > 0 ? " (" + Int(monster.Level) + ")" : string.Empty),
                        Color = AlignmentColor(MonsterAlignment(monster.TemplateId))
                    };
                case MerchantActor merchant:
                    return new OverheadContent
                    {
                        Layout = OverheadLayout.Merchant, Text = merchant.DisplayName, Color = 0, GuildName = Empty(merchant.GuildName),
                        MerchantType = Math.Max(0, merchant.OfflineType)
                    };
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
            OverheadContent content;
            if (player != null && player.HasGuild)
                content = new OverheadContent
                {
                    Layout = OverheadLayout.Guild, Text = name, Color = TextCharacter, GuildName = player.GuildName, Emblem = player.GuildEmblem,
                    Title = title, TitleColor = titleColor
                };
            else
            {
                string text = name;
                if (fightRunning && life.HasValue) text += " (" + Int(life.Value) + ")";
                else if (inFight && !fightRunning && level.HasValue && level.Value > 0) text += " (" + Int(level.Value) + ")";
                content = new OverheadContent { Text = text, Color = TextCharacter, Title = title, TitleColor = titleColor };
            }
            if (player != null) DescribeWings(player, content);
            return content;
        }

        /// <summary>
        /// Ailes d'un joueur (<c>TextOverHead</c> / <c>GuildOverHead</c> du client) : seulement quand le grade (<c>rank.value</c>) dépasse
        /// 0, image = grade (Bonta), 10 + grade (Brâkmar), 20 + grade (mercenaire), SWF des déchus selon le 5e champ ;
        /// <c>pvpGain</c> d'après <see cref="Viewer"/>, jamais pour le personnage du compte.
        /// </summary>
        private static void DescribeWings(PlayerActor player, OverheadContent content)
        {
            ActorAlignment alignment = player.Alignment;
            if (alignment == null || alignment.Grade <= 0) return;
            int frame = WingsFrameOf(alignment.Side, alignment.Grade);
            if (frame <= 0) return;
            content.WingsFrame = frame;
            content.FallenWings = alignment.FallenAngelDemon;
            PvpViewer viewer = Viewer;
            if (viewer != null && !player.IsSelf && player.Id != viewer.Id) content.PvpGain = PvpGain(viewer, alignment, player.Id);
        }

        /// <summary>Image des ailes pour un camp (1 Bonta, 2 Brâkmar, 3 mercenaire) et un grade de 1 à 10 ; 0 sinon.</summary>
        public static int WingsFrameOf(int side, int grade)
        {
            if (grade <= 0 || grade > 10 || side < 1 || side > 3) return 0;
            return (side - 1) * 10 + grade;
        }

        /// <summary>Couleur de lueur de l'image des ailes (<c>floor((image − 1) / 10)</c>) : Bonta, Brâkmar, mercenaire.</summary>
        public static int GlowColorOf(int frame) => GlowColors[Math.Max(0, Math.Min(GlowColors.Length - 1, (frame - 1) / 10))];

        /// <summary>
        /// <c>Lang.getAlignmentCanViewPvpGain(camp du compte, camp de l'autre)</c> : table <c>A.g</c> d'<c>alignment.xml</c>
        /// (ligne du camp du compte, colonne du camp de l'autre). Faux sans la table.
        /// </summary>
        public static bool CanViewPvpGain(int viewerSide, int otherSide)
        {
            if (viewerSide < 0 || otherSide < 0 || !LangData.IsLoaded("alignment")) return false;
            IReadOnlyDictionary<string, string> row = LangData.Raw("alignment", "A.g", Int(viewerSide));
            if (row == null || !row.TryGetValue("valeur", out string value) || string.IsNullOrEmpty(value)) return false;
            string[] columns = value.Split(',');
            return otherSide < columns.Length && string.Equals(columns[otherSide].Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// <c>pvpGain</c> calculé par le client à la lecture du <c>GM</c> d'un autre joueur : niveau de l'autre = 4e champ de
        /// l'alignement − identifiant ; −1 si le compte est trop haut (niveau × (1 − MINOR_PRC %) au-dessus, ou écart au-delà de
        /// MINOR_LIMIT), 1 s'il est trop bas (niveau × (1 + MAJOR_PRC %) en dessous, ou écart sous MAJOR_LIMIT), 0 sinon ; 0 aussi
        /// quand les camps ne se voient pas (<see cref="CanViewPvpGain"/>).
        /// </summary>
        public static int PvpGain(PvpViewer viewer, ActorAlignment other, long otherId)
        {
            if (viewer == null || other == null || !CanViewPvpGain(viewer.Side, other.Side)) return 0;
            double otherLevel = other.LevelCode - otherId;
            double minor = PvpConfig("PVP_VIEW_BONUS_MINOR_LIMIT", DefaultPvpMinorLimit), minorPrc = PvpConfig("PVP_VIEW_BONUS_MINOR_LIMIT_PRC", DefaultPvpMinorLimitPrc);
            double major = PvpConfig("PVP_VIEW_BONUS_MAJOR_LIMIT", DefaultPvpMajorLimit), majorPrc = PvpConfig("PVP_VIEW_BONUS_MAJOR_LIMIT_PRC", DefaultPvpMajorLimitPrc);
            int gain = 0;
            if (viewer.Level * (1 - minorPrc / 100) > otherLevel) gain = -1;
            if (viewer.Level - otherLevel > minor) gain = -1;
            if (viewer.Level * (1 + majorPrc / 100) < otherLevel) gain = 1;
            if (viewer.Level - otherLevel < major) gain = 1;
            return gain;
        }

        private static double PvpConfig(string key, int fallback)
        {
            try
            {
                string value = LangData.Text.Config(key);
                return value != null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : fallback;
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { return fallback; }
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
                // drawGfx : les ailes à l'origine de la surtête (haut, centre), profondeur 10, derrière le fond (20) et le texte (30).
                if (content.WingsFrame > 0) DrawWings(graphics, content, new Point(box.Left + box.Width / 2, box.Top), images);
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
                // Icône du type de marchand (clips/extra/<type>.swf, lot AN8 : Effets/extra/<type>_scene.png, 21 × 23 pour le type 0),
                // centrée dans le cadre de l'emblème ; cadre neutre tant qu'elle n'est pas lue ou si elle manque.
                Bitmap icon = images?.File("Effets/" + ActorExtraClips.ExtraFamily + "/" + Int(content.MerchantType) + "_scene.png");
                if (icon != null && icon.Width <= EmblemSize && icon.Height <= EmblemSize)
                    graphics.DrawImage(icon, new Rectangle(emblemBox.X + (EmblemSize - icon.Width) / 2, emblemBox.Y + (EmblemSize - icon.Height) / 2, icon.Width, icon.Height));
                else if (icon != null) graphics.DrawImage(icon, emblemBox);
                else using (var pen = new Pen(Color.FromArgb(120, 0, 0, 0))) graphics.DrawRectangle(pen, emblemBox.X + 4, emblemBox.Y + 4, emblemBox.Width - 9, emblemBox.Height - 9);
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

        // ---------------------------------------------------------------- ailes : bande, image, variantes

        /// <summary>Ligne d'<c>effets.tsv</c> d'une bande d'ailes, lue sur le pool avec l'image ; <see cref="Margin"/> : bord ajouté par la lueur.</summary>
        public sealed class WingsBand
        {
            public int XMin, YMin, Width, Height, Frames, Margin;
        }

        private static readonly object WingsSync = new object();
        private static readonly Dictionary<string, WingsBand> WingsBands = new Dictionary<string, WingsBand>(StringComparer.Ordinal);

        /// <summary>Clé d'image d'une variante d'ailes (bande, image, pvpGain).</summary>
        public static string WingsKey(bool fallen, int frame, int pvpGain) =>
            "ailes:" + (fallen ? "2" : "1") + ":" + Int(frame) + ":" + Int(pvpGain);

        /// <summary>Cadre de la variante prête (coin par rapport à l'origine de la surtête), ou null tant qu'elle ne l'est pas.</summary>
        public static WingsBand WingsBandOf(string key)
        {
            lock (WingsSync) return WingsBands.TryGetValue(key, out WingsBand band) ? band : null;
        }

        /// <summary>
        /// Dessine l'image des ailes (variante selon <see cref="OverheadContent.PvpGain"/>) à l'origine <paramref name="origin"/>
        /// (écran, taille fixe comme le client). L'image est produite une fois sur le pool par <paramref name="images"/> :
        /// rien n'est dessiné tant qu'elle n'est pas prête ou si la bande manque.
        /// </summary>
        public static void DrawWings(Graphics graphics, OverheadContent content, Point origin, OverheadImages images)
        {
            if (graphics == null || content == null || images == null || content.WingsFrame <= 0 || content.WingsFrame > WingsFrames) return;
            string key = WingsKey(content.FallenWings, content.WingsFrame, content.PvpGain);
            string root = images.Root;
            bool fallen = content.FallenWings; int frame = content.WingsFrame, gain = content.PvpGain;
            Bitmap image = images.TryGet(key, () => RenderWings(root, key, fallen, frame, gain));
            WingsBand band = image == null ? null : WingsBandOf(key);
            if (image == null || band == null) return;
            graphics.DrawImage(image, new Rectangle(origin.X + band.XMin - band.Margin, origin.Y + band.YMin - band.Margin, image.Width, image.Height));
        }

        /// <summary>
        /// Image <paramref name="frame"/> (1 à 30) de la bande d'ailes, avec sa variante : <paramref name="pvpGain"/> −1, composantes
        /// à 50 % ; 1, lueur de la couleur du camp derrière l'image (alpha de l'image flouté en boîte 10 × 10 sur 3 passes, × 2,
        /// alpha 0,5) ; 0, l'image telle quelle. Pool de threads ; null si la bande ou sa ligne d'<c>effets.tsv</c> manque.
        /// </summary>
        public static Bitmap RenderWings(string root, string key, bool fallen, int frame, int pvpGain)
        {
            string folder = Path.Combine(root ?? string.Empty, WingsFolder.Replace('/', Path.DirectorySeparatorChar));
            int id = fallen ? 2 : 1;
            WingsBand band = ReadWingsBand(Path.Combine(folder, "effets.tsv"), id);
            if (band == null || frame < 1 || frame > band.Frames) return null;
            Bitmap cut = null, result = null;
            try
            {
                using (var stream = new FileStream(Path.Combine(folder, Int(id) + "_scene.png"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var source = Image.FromStream(stream))
                {
                    int columns = Math.Max(1, source.Width / band.Width);
                    int index = frame - 1;
                    var cell = new Rectangle(index % columns * band.Width, index / columns * band.Height, band.Width, band.Height);
                    if (cell.Right > source.Width || cell.Bottom > source.Height) return null;
                    cut = new Bitmap(band.Width, band.Height, PixelFormat.Format32bppArgb);
                    using (Graphics graphics = Graphics.FromImage(cut))
                    {
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.DrawImage(source, new Rectangle(0, 0, band.Width, band.Height), cell, GraphicsUnit.Pixel);
                    }
                }
                int margin = 0;
                if (pvpGain < 0) { Darken(cut, DarkenFactor); result = cut; cut = null; }
                else if (pvpGain > 0) { result = Glow(cut, GlowColorOf(frame), out margin); }
                else { result = cut; cut = null; }
                if (result == null) return null;
                var info = new WingsBand { XMin = band.XMin, YMin = band.YMin, Width = band.Width, Height = band.Height, Frames = band.Frames, Margin = margin };
                lock (WingsSync) WingsBands[key] = info;
                return result;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException
                || error is System.Runtime.InteropServices.ExternalException || error is OutOfMemoryException || error is NotSupportedException)
            {
                result?.Dispose();
                return null;
            }
            finally { cut?.Dispose(); }
        }

        /// <summary>Ligne <c>gfx scene</c> d'<c>effets.tsv</c> (colonnes d'<c>ancres.tsv</c>), null si elle manque ou est illisible.</summary>
        public static WingsBand ReadWingsBand(string table, int id)
        {
            try
            {
                if (!File.Exists(table)) return null;
                foreach (string line in File.ReadLines(table))
                {
                    string[] f = line.Split('\t');
                    if (f.Length < 7 || f[0] != Int(id) || f[1] != "scene") continue;
                    if (!int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int xMin) || !int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int yMin)
                        || !int.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width) || !int.TryParse(f[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height)
                        || !int.TryParse(f[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int frames) || width <= 0 || height <= 0 || frames <= 0)
                        return null;
                    return new WingsBand { XMin = xMin, YMin = yMin, Width = width, Height = height, Frames = frames };
                }
                return null;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return null; }
        }

        private static int[] Pixels(Bitmap image, out BitmapData data)
        {
            data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            var pixels = new int[image.Width * image.Height];
            for (int y = 0; y < image.Height; y++)
                System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * image.Width, image.Width);
            return pixels;
        }

        private static void Store(Bitmap image, BitmapData data, int[] pixels)
        {
            for (int y = 0; y < image.Height; y++)
                System.Runtime.InteropServices.Marshal.Copy(pixels, y * image.Width, IntPtr.Add(data.Scan0, y * data.Stride), image.Width);
            image.UnlockBits(data);
        }

        /// <summary><c>ColorMatrixFilter</c> : composantes RVB × <paramref name="factor"/>, alpha gardé.</summary>
        internal static void Darken(Bitmap image, float factor)
        {
            int[] pixels = Pixels(image, out BitmapData data);
            for (int i = 0; i < pixels.Length; i++)
            {
                uint p = (uint)pixels[i];
                uint r = (uint)Math.Round(((p >> 16) & 0xFF) * factor), g = (uint)Math.Round(((p >> 8) & 0xFF) * factor), b = (uint)Math.Round((p & 0xFF) * factor);
                pixels[i] = (int)((p & 0xFF000000u) | (r << 16) | (g << 8) | b);
            }
            Store(image, data, pixels);
        }

        /// <summary>
        /// <c>GlowFilter</c> (extérieur, sans découpe) : image agrandie d'une marge, alpha flouté en boîte (<see cref="GlowBlur"/>,
        /// <see cref="GlowQuality"/> passes) × <see cref="GlowStrength"/> × <see cref="GlowAlpha"/>, coloré <paramref name="rgb"/>,
        /// l'image d'origine par-dessus. <paramref name="margin"/> : bord ajouté de chaque côté.
        /// </summary>
        internal static Bitmap Glow(Bitmap image, int rgb, out int margin)
        {
            int radius = GlowBlur / 2;
            margin = radius * GlowQuality;
            int width = image.Width + margin * 2, height = image.Height + margin * 2;
            var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            try
            {
                int[] source = Pixels(image, out BitmapData sourceData);
                image.UnlockBits(sourceData);
                var alpha = new float[width * height];
                for (int y = 0; y < image.Height; y++)
                    for (int x = 0; x < image.Width; x++)
                        alpha[(y + margin) * width + x + margin] = (uint)source[y * image.Width + x] >> 24;
                var buffer = new float[width * height];
                for (int pass = 0; pass < GlowQuality; pass++)
                {
                    BoxBlur(alpha, buffer, width, height, radius, true);
                    BoxBlur(buffer, alpha, width, height, radius, false);
                }
                int color = rgb & 0xFFFFFF;
                int[] pixels = Pixels(result, out BitmapData data);
                for (int i = 0; i < pixels.Length; i++)
                {
                    int a = (int)Math.Round(Math.Min(255f, alpha[i] * GlowStrength) * GlowAlpha);
                    pixels[i] = a <= 0 ? 0 : (a << 24) | color;
                }
                Store(result, data, pixels);
                using (Graphics graphics = Graphics.FromImage(result))
                {
                    graphics.CompositingMode = CompositingMode.SourceOver;
                    graphics.DrawImage(image, new Rectangle(margin, margin, image.Width, image.Height), new Rectangle(0, 0, image.Width, image.Height), GraphicsUnit.Pixel);
                }
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        /// <summary>Flou en boîte d'une passe sur un axe (moyenne des 2 × rayon + 1 voisins, bords transparents).</summary>
        private static void BoxBlur(float[] source, float[] target, int width, int height, int radius, bool horizontal)
        {
            int size = radius * 2 + 1;
            if (horizontal)
                for (int y = 0; y < height; y++)
                {
                    int row = y * width;
                    float sum = 0;
                    for (int x = -radius; x <= radius; x++) if (x >= 0 && x < width) sum += source[row + x];
                    for (int x = 0; x < width; x++)
                    {
                        target[row + x] = sum / size;
                        int leaving = x - radius, entering = x + radius + 1;
                        if (leaving >= 0) sum -= source[row + leaving];
                        if (entering < width) sum += source[row + entering];
                    }
                }
            else
                for (int x = 0; x < width; x++)
                {
                    float sum = 0;
                    for (int y = -radius; y <= radius; y++) if (y >= 0 && y < height) sum += source[y * width + x];
                    for (int y = 0; y < height; y++)
                    {
                        target[y * width + x] = sum / size;
                        int leaving = y - radius, entering = y + radius + 1;
                        if (leaving >= 0) sum -= source[leaving * width + x];
                        if (entering < height) sum += source[entering * width + x];
                    }
                }
        }

        // ---------------------------------------------------------------- teinte de sélection

        /// <summary>
        /// Matrice de <c>selectSprite</c> : chaque canal à 60 % plus 102 (0,4), qui éclaircit le sprite survolé.
        /// L'appelant libère l'objet rendu.
        /// </summary>
        /// <summary>
        /// Attributs d'un sprite : teinte de sélection (<see cref="SelectionTint"/>) si <paramref name="selected"/>, alpha
        /// multiplié par <paramref name="alpha"/> (0 à 1 ; <c>setAlpha</c> du client, lot X7).
        /// </summary>
        public static System.Drawing.Imaging.ImageAttributes SpriteAttributes(bool selected, float alpha)
        {
            float scale = selected ? .6f : 1f, offset = selected ? .4f : 0f;
            float opacity = float.IsNaN(alpha) ? 1f : Math.Max(0f, Math.Min(1f, alpha));
            var matrix = new System.Drawing.Imaging.ColorMatrix(new[]
            {
                new[] { scale, 0, 0, 0, 0 }, new[] { 0, scale, 0, 0, 0 }, new[] { 0, 0, scale, 0, 0 },
                new[] { 0, 0, 0, opacity, 0 }, new[] { offset, offset, offset, 0, 1f }
            });
            var attributes = new System.Drawing.Imaging.ImageAttributes();
            attributes.SetColorMatrix(matrix);
            return attributes;
        }

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
