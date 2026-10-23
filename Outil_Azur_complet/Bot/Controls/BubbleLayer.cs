using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using Tool_BotProtocol.Game.Chat;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>Forme d'une bulle (<c>BUBBLE_TYPE_CHAT</c>, <c>BUBBLE_TYPE_THINK</c>).</summary>
    public enum BubbleKind { Chat = 1, Think = 2 }

    /// <summary>Bulle affichée au-dessus d'un acteur. Sa position est figée au moment de l'ajout, comme dans le client.</summary>
    public sealed class SpeechBubble
    {
        internal SpeechBubble(long actorId, string text, BubbleKind kind, PointF worldFoot, double shown, double expires)
        {
            ActorId = actorId; Text = text; Kind = kind; WorldFoot = worldFoot; Shown = shown; Expires = expires;
        }
        public long ActorId { get; }
        public string Text { get; }
        public BubbleKind Kind { get; }
        /// <summary>Pied de l'acteur au moment de l'ajout (repère de la carte).</summary>
        public PointF WorldFoot { get; }
        public double Shown { get; }
        /// <summary>Instant (horloge de la vue, ms) où la bulle disparaît.</summary>
        public double Expires { get; }
    }

    /// <summary>
    /// Bulles du chat, smileys et icônes d'émote au-dessus des acteurs, d'après <c>TextHandler.addBubble</c> et les
    /// constantes de <c>ank.battlefield.Constants</c> du client 1.34 : fond #FFFFCE bordé de #4B4B4B, texte Font1 10 px
    /// sur 150 px de large, marge 4, pointe 10 × 10, 50 px au-dessus du pied ; durée 4 000 ms + 50 ms par caractère ;
    /// une seule bulle par acteur ; smiley 3 000 ms. L'horloge est injectée (tests déterministes). Toutes les méthodes
    /// s'appellent sur le fil de l'interface (la vue les y ramène).
    /// </summary>
    public sealed class BubbleLayer
    {
        public const int RemoveTimer = 4000, RemoveCharTimer = 50, Margin = 4, PicWidth = 10, PicHeight = 10, YOffset = 50, TextWidth = 150;
        /// <summary><c>SMILEY_DELAY</c> du client.</summary>
        public const int SmileyDelay = 3000;
        /// <summary>Durée d'une icône d'émote non persistante (choix du bot : le client joue l'animation du sprite).</summary>
        public const int EmoteIconDelay = 3000;
        /// <summary>Côté des smileys et icônes d'émote au-dessus de la tête.</summary>
        public const int IconSize = 20;
        public static readonly Color Background = Color.FromArgb(0xFF, 0xFF, 0xCE);
        public static readonly Color Border = Color.FromArgb(0x4B, 0x4B, 0x4B);

        private readonly Func<double> clock;
        private readonly Dictionary<long, SpeechBubble> bubbles = new Dictionary<long, SpeechBubble>();
        private readonly Dictionary<long, Timed> smileys = new Dictionary<long, Timed>();
        private readonly Dictionary<long, Timed> emotes = new Dictionary<long, Timed>();

        private struct Timed
        {
            public int Value;
            public double Expires;
        }

        public BubbleLayer(Func<double> clock)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>Option <c>ChatEffects</c> du client (vraie par défaut) : faux, aucune bulle n'est ajoutée.</summary>
        public bool ChatEffects { get; set; } = true;

        /// <summary>Durée d'affichage d'une bulle : <c>BUBBLE_REMOVE_TIMER</c> + longueur × <c>BUBBLE_REMOVE_CHAR_TIMER</c>.</summary>
        public static int DurationFor(string text) => RemoveTimer + (text?.Length ?? 0) * RemoveCharTimer;

        /// <summary>
        /// Texte et forme de la bulle d'un message reçu, comme <c>Chat.onMessage</c> : seulement le canal par défaut ;
        /// une émote « *texte* » garde ses astérisques avec une majuscule (sans le point final unique), « !THINK! » donne
        /// une bulle de pensée ; objets parlants et autres canaux : aucune bulle (null).
        /// </summary>
        public static string BubbleText(ChatMessage message, out BubbleKind kind)
        {
            kind = BubbleKind.Chat;
            if (message == null || message.Kind != ChatMessageKind.Channel || message.Channel == null
                || message.Channel.Code != ChatChannels.Default.Code || !message.AuthorId.HasValue) return null;
            string text = message.Text ?? string.Empty;
            switch (message.Style)
            {
                case ChatMessageStyle.Emote:
                    if (text.EndsWith(".", StringComparison.Ordinal) && !text.EndsWith("..", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 1);
                    if (text.Length == 0) return null;
                    return "*" + char.ToUpperInvariant(text[0]) + text.Substring(1) + "*";
                case ChatMessageStyle.Think:
                    kind = BubbleKind.Think;
                    return text.Length == 0 ? null : text;
                case ChatMessageStyle.SpeakingItem:
                    return null;
                default:
                    return text.Length == 0 ? null : text;
            }
        }

        /// <summary>Ajoute (ou remplace) la bulle de l'acteur ; null si <see cref="ChatEffects"/> est désactivée ou le texte vide.</summary>
        public SpeechBubble Show(long actorId, string text, BubbleKind kind, PointF worldFoot)
        {
            if (!ChatEffects || string.IsNullOrEmpty(text)) return null;
            double now = clock();
            var bubble = new SpeechBubble(actorId, text, kind, worldFoot, now, now + DurationFor(text));
            bubbles[actorId] = bubble;
            return bubble;
        }

        public void ShowSmiley(long actorId, int smiley)
        {
            if (smiley <= 0) { smileys.Remove(actorId); return; }
            smileys[actorId] = new Timed { Value = smiley, Expires = clock() + SmileyDelay };
        }

        /// <summary>Icône de l'émote <paramref name="emote"/> ; 0 la retire. Persistante (assis…) jusqu'à la prochaine émote ou au déplacement.</summary>
        public void ShowEmote(long actorId, int emote, bool persistent)
        {
            if (emote <= 0) { emotes.Remove(actorId); return; }
            emotes[actorId] = new Timed { Value = emote, Expires = persistent ? double.PositiveInfinity : clock() + EmoteIconDelay };
        }

        public void RemoveBubble(long actorId) => bubbles.Remove(actorId);
        public void RemoveEmote(long actorId) => emotes.Remove(actorId);

        /// <summary>Tout ce qui concerne <paramref name="actorId"/> (acteur retiré de la carte).</summary>
        public void Remove(long actorId)
        {
            bubbles.Remove(actorId); smileys.Remove(actorId); emotes.Remove(actorId);
        }

        public void Clear()
        {
            bubbles.Clear(); smileys.Clear(); emotes.Clear();
        }

        /// <summary>Retire ce qui a expiré ; vrai si quelque chose a disparu (la vue redessine).</summary>
        public bool Prune()
        {
            double now = clock();
            bool changed = false;
            foreach (long id in bubbles.Where(pair => pair.Value.Expires <= now).Select(pair => pair.Key).ToList()) { bubbles.Remove(id); changed = true; }
            foreach (long id in smileys.Where(pair => pair.Value.Expires <= now).Select(pair => pair.Key).ToList()) { smileys.Remove(id); changed = true; }
            foreach (long id in emotes.Where(pair => pair.Value.Expires <= now).Select(pair => pair.Key).ToList()) { emotes.Remove(id); changed = true; }
            return changed;
        }

        /// <summary>Vrai tant qu'une bulle, un smiley ou une émote temporaire attend son expiration.</summary>
        public bool HasTimedItems => bubbles.Count > 0 || smileys.Count > 0 || emotes.Values.Any(item => !double.IsPositiveInfinity(item.Expires));

        /// <summary>Bulles encore visibles à l'instant de l'horloge.</summary>
        public IReadOnlyList<SpeechBubble> Bubbles
        {
            get { double now = clock(); return bubbles.Values.Where(bubble => bubble.Expires > now).ToList(); }
        }

        public SpeechBubble BubbleOf(long actorId)
        {
            return bubbles.TryGetValue(actorId, out SpeechBubble bubble) && bubble.Expires > clock() ? bubble : null;
        }

        public int? SmileyOf(long actorId) => smileys.TryGetValue(actorId, out Timed item) && item.Expires > clock() ? item.Value : (int?)null;
        public int? EmoteOf(long actorId) => emotes.TryGetValue(actorId, out Timed item) && item.Expires > clock() ? item.Value : (int?)null;

        // ---------------------------------------------------------------- dessin

        internal static Font TextFont => BotFonts.Get(7.5f);

        /// <summary>Taille du texte (largeur + 4, hauteur), au moins 11 × 11 comme le client.</summary>
        public static SizeF MeasureText(Graphics graphics, string text)
        {
            using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                SizeF size = graphics.MeasureString(text ?? string.Empty, TextFont, TextWidth, format);
                return new SizeF((float)Math.Ceiling(Math.Max(11, size.Width)) + 4, (float)Math.Ceiling(Math.Max(11, size.Height)));
            }
        }

        /// <summary>
        /// Cadre de la bulle (écran) pour un pied en <paramref name="foot"/> : à droite de la pointe et 50 px plus haut,
        /// retournée à gauche près du bord droit et sous le pied près du haut (<c>Bubble.adjust</c>).
        /// </summary>
        public static RectangleF Frame(Graphics graphics, SpeechBubble bubble, PointF foot, float scale, Rectangle view, out PointF tip, out bool flipX, out bool flipY)
        {
            SizeF text = MeasureText(graphics, bubble.Text);
            float width = text.Width + Margin * 2, height = text.Height + Margin * 2 + PicHeight;
            float offset = YOffset * (scale > 0 ? scale : 1);
            flipX = foot.X > view.Right - width;
            flipY = foot.Y - view.Top < height + offset;
            tip = flipY ? foot : new PointF(foot.X, foot.Y - offset);
            float left = flipX ? tip.X - width : tip.X;
            float top = flipY ? tip.Y + PicHeight : tip.Y - height;
            return new RectangleF(left, top, width, height - PicHeight);
        }

        public static void Draw(Graphics graphics, SpeechBubble bubble, PointF foot, float scale, Rectangle view)
        {
            if (graphics == null || bubble == null) return;
            RectangleF box = Frame(graphics, bubble, foot, scale, view, out PointF tip, out bool flipX, out bool flipY);
            SmoothingMode smoothing = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                if (bubble.Kind == BubbleKind.Think) DrawThink(graphics, box, tip, flipX, flipY);
                else DrawChat(graphics, box, tip, flipX, flipY);
                using (var brush = new SolidBrush(Color.Black))
                using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
                    graphics.DrawString(bubble.Text, TextFont, brush, new RectangleF(box.Left + Margin + 2, box.Top + 3, TextWidth, Math.Max(1, box.Height)), format);
            }
            finally { graphics.SmoothingMode = smoothing; }
        }

        private static void DrawChat(Graphics graphics, RectangleF box, PointF tip, bool flipX, bool flipY)
        {
            float sx = flipX ? -1 : 1, sy = flipY ? -1 : 1;
            // Tracé de drawBackground (repère de la pointe, axes retournés comme _xscale / _yscale = -100).
            PointF P(float x, float y) => new PointF(tip.X + sx * x, tip.Y + sy * y);
            float right = box.Width, top = box.Height + PicHeight;
            using (var path = new GraphicsPath(FillMode.Winding))
            {
                path.AddPolygon(new[]
                {
                    P(0, -PicHeight), P(PicWidth / 2f, -PicHeight), P(0, 0), P(PicWidth, -PicHeight),
                    P(right, -PicHeight), P(right, -top), P(0, -top)
                });
                using (var brush = new SolidBrush(Background)) graphics.FillPath(brush, path);
                using (var pen = new Pen(Border, 1)) graphics.DrawPath(pen, path);
            }
        }

        private static void DrawThink(Graphics graphics, RectangleF box, PointF tip, bool flipX, bool flipY)
        {
            float sx = flipX ? -1 : 1, sy = flipY ? -1 : 1;
            PointF P(float x, float y) => new PointF(tip.X + sx * x, tip.Y + sy * y);
            float right = box.Width, top = -(box.Height + PicHeight), bottom = -PicHeight;
            PointF a = P(0, top), b = P(right, bottom);
            // Union de régions et non remplissage « winding » d'un chemin : le sens des ellipses de libgdiplus est
            // l'inverse de celui des rectangles, ce qui percerait le nuage là où les cercles chevauchent le rectangle.
            using (var path = new GraphicsPath())
            using (var cloud = new Region(RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y))))
            {
                path.AddRectangle(RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
                void Circle(float x, float y, float radius)
                {
                    PointF c = P(x, y);
                    var bounds = new RectangleF(c.X - radius, c.Y - radius, radius * 2, radius * 2);
                    path.AddEllipse(bounds);
                    using (var circle = new GraphicsPath()) { circle.AddEllipse(bounds); cloud.Union(circle); }
                }
                for (float x = 0; x <= right; x += 14) { Circle(x, top, 7); Circle(x, bottom, 7); }
                for (float y = top; y <= bottom; y += 14) { Circle(right, y, 7); Circle(0, y, 7); }
                Circle(0, bottom + 5, 8);
                Circle(-5, 5, 4);
                // Halo noir (GlowFilter du client) sous le contour de chaque forme, puis nuage blanc à 90 % par-dessus.
                using (var glow = new Pen(Color.FromArgb(77, 0, 0, 0), 3f) { LineJoin = LineJoin.Round }) graphics.DrawPath(glow, path);
                using (var brush = new SolidBrush(Color.FromArgb(230, 255, 255, 255))) graphics.FillRegion(brush, cloud);
            }
        }
    }
}
