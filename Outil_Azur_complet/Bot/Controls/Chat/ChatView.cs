using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Game.Session;

namespace Outil_Azur_complet.Bot.Controls.Chat
{
    /// <summary>
    /// Images du client pour les contrôles du chat, lues par <see cref="ClientAssets"/> sur le pool de threads : un
    /// <c>Paint</c> ne lit que le cache ; une image pas encore chargée renvoie null et le contrôle demandeur est redessiné à
    /// son arrivée. Les images sont partagées (jamais libérées) et verrouillées le temps d'un dessin.
    /// </summary>
    internal static class ChatImages
    {
        private static readonly Dictionary<string, List<Control>> pending = new Dictionary<string, List<Control>>(StringComparer.Ordinal);
        private static readonly object sync = new object();

        internal static Bitmap Get(string family, string name, Control requester)
        {
            Bitmap image;
            if (ClientAssets.TryCached(family, name, out image)) return image;
            string key = family + "/" + name;
            lock (sync)
            {
                List<Control> waiting;
                if (pending.TryGetValue(key, out waiting))
                {
                    if (requester != null && !waiting.Contains(requester)) waiting.Add(requester);
                    return null;
                }
                pending[key] = requester == null ? new List<Control>() : new List<Control> { requester };
            }
            ClientAssets.GetAsync(family, name).ContinueWith(task =>
            {
                List<Control> waiting;
                lock (sync) { if (!pending.TryGetValue(key, out waiting)) waiting = new List<Control>(); pending.Remove(key); }
                foreach (Control control in waiting) Repaint(control);
            }, TaskScheduler.Default);
            return null;
        }

        /// <summary>Dessine une image partagée dans le cadre, proportions conservées, centrée.</summary>
        internal static void Draw(Graphics graphics, Image image, RectangleF bounds)
        {
            if (image == null || bounds.Width < 1 || bounds.Height < 1) return;
            lock (image)
            {
                float ratio = Math.Min(bounds.Width / image.Width, bounds.Height / image.Height);
                float width = image.Width * ratio, height = image.Height * ratio;
                var state = graphics.Save();
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(image, new RectangleF(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height));
                graphics.Restore(state);
            }
        }

        private static void Repaint(Control control)
        {
            if (control == null || control.IsDisposed || !control.IsHandleCreated) return;
            try { control.BeginInvoke((Action)(() => { if (!control.IsDisposed) control.Invalidate(); })); }
            catch (InvalidOperationException) { /* Fenêtre fermée entre-temps. */ }
        }
    }

    /// <summary>Morceau d'une ligne du chat : texte, gras/italique (balises du client), lien éventuel.</summary>
    public sealed class ChatSegment
    {
        public ChatSegment(string text, bool bold = false, bool italic = false, ChatLink link = null)
        {
            Text = text ?? string.Empty; Bold = bold; Italic = italic; Link = link;
        }
        public string Text { get; }
        public bool Bold { get; }
        public bool Italic { get; }
        public ChatLink Link { get; }
    }

    /// <summary>Une ligne affichée par <see cref="ChatView"/> (texte du client, couleur, filtre, heure de réception).</summary>
    public sealed class ChatLine
    {
        public ChatLine(ChatFilter filter, Color color, IEnumerable<ChatSegment> segments, ChatMessage message, string author, bool timestamped, DateTime time)
        {
            Filter = filter; Color = color; Message = message; Author = author ?? string.Empty; Timestamped = timestamped; Time = time;
            Segments = Array.AsReadOnly((segments ?? Enumerable.Empty<ChatSegment>()).Where(segment => segment != null && segment.Text.Length > 0).ToArray());
            Text = string.Concat(Segments.Select(segment => segment.Text));
        }

        public ChatFilter Filter { get; }
        public Color Color { get; }
        public IReadOnlyList<ChatSegment> Segments { get; }
        /// <summary>Message du protocole à l'origine de la ligne, ou null (message <c>Im</c>, notice des actions de carte).</summary>
        public ChatMessage Message { get; }
        /// <summary>Auteur (nom cliquable) ou chaîne vide.</summary>
        public string Author { get; }
        /// <summary>Vrai si l'heure est affichée devant la ligne (toutes sauf les informations, comme le client).</summary>
        public bool Timestamped { get; }
        public DateTime Time { get; }
        /// <summary>Texte de la ligne, sans l'heure.</summary>
        public string Text { get; }
        public IEnumerable<ChatLink> Links => Segments.Where(segment => segment.Link != null).Select(segment => segment.Link);

        /// <summary>« [HH:mm] » de la ligne.</summary>
        public string TimePrefix => "[" + Time.ToString("HH:mm", CultureInfo.InvariantCulture) + "] ";

        public override string ToString() => Text;
    }

    /// <summary>
    /// Mise en forme des lignes comme <c>Chat.onMessage</c> du client 1.34 : « Nom : texte », « (Guilde) Nom : texte »,
    /// « de <i>Nom</i> : texte », « à Nom : texte », émote « <i>Nom texte.</i> », pensée « <i>Nom pense : texte</i> ».
    /// Le nom est un lien (menu du joueur) ; les coordonnées <c>[x,y]</c> du texte d'un message de canal sont des liens
    /// (boussole). Les messages d'objets parlants ne donnent aucune ligne (option du client désactivée par défaut).
    /// </summary>
    public static class ChatLineBuilder
    {
        private const string Punctuation = ".!?~";

        public static ChatLine FromMessage(ChatMessage message, bool spectator)
        {
            if (message == null) return null;
            var segments = new List<ChatSegment>();
            string author = message.Author;
            switch (message.Kind)
            {
                case ChatMessageKind.WhisperReceived:
                    segments.Add(new ChatSegment(ChatUiText.Get("FROM", "de").Trim() + " "));
                    segments.Add(Name(message, true));
                    segments.Add(new ChatSegment(" : "));
                    AddText(segments, message.Text, message, false);
                    break;
                case ChatMessageKind.WhisperSent:
                    segments.Add(new ChatSegment(ChatUiText.Get("TO_DESTINATION", "à").Trim() + " "));
                    segments.Add(Name(message, false));
                    segments.Add(new ChatSegment(" : "));
                    AddText(segments, message.Text, message, false);
                    break;
                case ChatMessageKind.Channel:
                    if (message.Channel == ChatChannels.Default)
                    {
                        switch (message.Style)
                        {
                            case ChatMessageStyle.SpeakingItem:
                                return null;
                            case ChatMessageStyle.Emote:
                                segments.Add(Name(message, true));
                                segments.Add(new ChatSegment(" ", false, true));
                                AddText(segments, EmoteSentence(message.Text), message, true);
                                break;
                            case ChatMessageStyle.Think:
                                segments.Add(Name(message, true));
                                segments.Add(new ChatSegment(" " + ChatUiText.Get("THINKS_WORD", "pense") + " : ", false, true));
                                AddText(segments, message.Text, message, true);
                                break;
                            default:
                                segments.Add(Name(message, false));
                                segments.Add(new ChatSegment(" : "));
                                AddText(segments, message.Text, message, false);
                                break;
                        }
                    }
                    else
                    {
                        // Seul le canal par défaut reconnaît les émotes et pensées : ailleurs, le texte reçu est affiché tel quel.
                        segments.Add(new ChatSegment("(" + ChannelLabel(message.Channel, spectator) + ") "));
                        segments.Add(Name(message, false));
                        segments.Add(new ChatSegment(" : "));
                        AddText(segments, ChatMessage.PlainText(message.RawText), message, false);
                    }
                    break;
                default:
                    author = null;
                    segments.Add(new ChatSegment(message.Text));
                    break;
            }
            return new ChatLine(message.Filter, ParseColor(message.Color, BotUi.Ink), segments, message, author,
                message.Filter != ChatFilter.Infos, message.Timestamp);
        }

        /// <summary>Entrée d'un paquet <c>Im</c> : information (vert), erreur (rouge) ou JcJ (canal d'alignement), comme <c>Infos.onMessage</c>.</summary>
        public static ChatLine FromServerMessage(ServerMessage message)
        {
            if (message == null || string.IsNullOrEmpty(message.Text)) return null;
            switch (message.Kind)
            {
                case ServerMessageKind.Error: return Local(ChatFilter.Errors, ChatColors.Error, message.Text);
                case ServerMessageKind.Pvp: return Local(ChatFilter.Alignment, ChatChannels.Alignment.Color, message.Text);
                default: return Local(ChatFilter.Infos, ChatColors.Info, message.Text);
            }
        }

        /// <summary>Notice des actions de carte : information ou erreur ; une réponse « qui est » prend la couleur des commandes.</summary>
        public static ChatLine FromNotice(MapActionNotice notice, bool whois)
        {
            if (notice == null || string.IsNullOrEmpty(notice.Text) || notice.Kind == MapActionNoticeKind.Alert) return null;
            if (notice.Kind == MapActionNoticeKind.Error) return Local(ChatFilter.Errors, ChatColors.Error, notice.Text);
            return whois ? Local(ChatFilter.Admin, ChatColors.Commands, notice.Text) : Local(ChatFilter.Infos, ChatColors.Info, notice.Text);
        }

        /// <summary>Ligne locale sans lien.</summary>
        public static ChatLine Local(ChatFilter filter, string color, string text) =>
            new ChatLine(filter, ParseColor(color, BotUi.Ink), new[] { new ChatSegment(ChatMessage.PlainText(text ?? string.Empty)) }, null, null,
                filter != ChatFilter.Infos, DateTime.Now);

        /// <summary>Libellé d'un canal préfixé, clés du client (<c>GUILD</c>, <c>TEAM</c> ou <c>SPECTATOR</c>…).</summary>
        public static string ChannelLabel(ChatChannel channel, bool spectator)
        {
            if (channel == ChatChannels.Team) return spectator ? ChatUiText.Get("SPECTATOR", "Spectateur") : ChatUiText.Get("TEAM", "Équipe");
            if (channel == ChatChannels.Party) return ChatUiText.Get("PARTY", "Groupe");
            if (channel == ChatChannels.Guild) return ChatUiText.Get("GUILD", "Guilde");
            if (channel == ChatChannels.Alignment) return ChatUiText.Get("ALIGNMENT", "Alignement");
            if (channel == ChatChannels.Recruitment) return ChatUiText.Get("RECRUITMENT", "Recrutement");
            if (channel == ChatChannels.Trade) return ChatUiText.Get("TRADE", "Commerce");
            if (channel == ChatChannels.Incarnam) return ChatUiText.Get("MEETIC", "Incarnam");
            if (channel == ChatChannels.Admin) return ChatUiText.Get("PRIVATE_CHANNEL", "Admin");
            return channel?.Label ?? string.Empty;
        }

        /// <summary>Phrase d'émote du client : point final ajouté sauf ponctuation (<c>PONCTUATION</c>), première lettre en minuscule.</summary>
        public static string EmoteSentence(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (Punctuation.IndexOf(text[text.Length - 1]) < 0) text += ".";
            return char.ToLower(text[0], CultureInfo.CurrentCulture) + text.Substring(1);
        }

        public static Color ParseColor(string hex, Color fallback)
        {
            int value;
            if (!string.IsNullOrEmpty(hex) && hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                return Color.FromArgb(255, (value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
            return fallback;
        }

        private static ChatSegment Name(ChatMessage message, bool italic)
        {
            string name = message.Author ?? string.Empty;
            return new ChatSegment(name, true, italic, ChatLinks.IsValidName(name) ? ChatLink.Player(name, message.AuthorId, message) : null);
        }

        private static void AddText(List<ChatSegment> segments, string text, ChatMessage message, bool italic)
        {
            if (string.IsNullOrEmpty(text)) return;
            int start = 0;
            foreach (ChatCoordinateMatch match in ChatLinks.FindCoordinates(text))
            {
                if (match.Start > start) segments.Add(new ChatSegment(text.Substring(start, match.Start - start), false, italic));
                segments.Add(new ChatSegment(text.Substring(match.Start, match.Length), false, italic, ChatLink.Coordinates(match.X, match.Y, message)));
                start = match.Start + match.Length;
            }
            if (start < text.Length) segments.Add(new ChatSegment(text.Substring(start), false, italic));
        }
    }

    /// <summary>Lien visible et sa zone dans <see cref="ChatView"/> (coordonnées du contrôle).</summary>
    public struct ChatLinkArea
    {
        public ChatLink Link;
        public Rectangle Bounds;
        public ChatLine Line;
    }

    /// <summary>Clic droit sur une ligne (hors lien).</summary>
    public sealed class ChatLineEventArgs : EventArgs
    {
        public ChatLineEventArgs(ChatLine line, Point location) { Line = line; Location = location; }
        public ChatLine Line { get; }
        public Point Location { get; }
    }

    /// <summary>
    /// Zone de texte du chat dessinée comme celle du client : fond parchemin, lignes colorées par canal, heure « [HH:mm] »,
    /// noms en gras et liens cliquables, retour à la ligne au mot. Garde 150 lignes (<c>MAX_ALL_LENGTH</c>) et en affiche
    /// les 30 dernières visibles (<c>MAX_VISIBLE</c>) selon les filtres ; défilement automatique sauf si l'utilisateur est
    /// remonté de plus de 6 lignes (<c>STOP_SCROLL_LENGTH</c>). Thread de l'interface uniquement.
    /// </summary>
    public sealed class ChatView : Control
    {
        public const int MaxLines = ChatService.MaxMessages;
        public const int MaxVisibleLines = 30;
        public const int StopScrollRows = 6;
        private const int ScrollBarWidth = 7;
        private const float FontSize = 8.25f;

        private sealed class Run
        {
            internal string Text; internal Font Font; internal float X; internal float Width; internal ChatLink Link; internal bool Underline;
        }

        private sealed class Row
        {
            internal ChatLine Line; internal readonly List<Run> Runs = new List<Run>();
        }

        private readonly List<ChatLine> lines = new List<ChatLine>();
        private readonly List<Row> rows = new List<Row>();
        private readonly bool[] visible = Enumerable.Repeat(true, 10).ToArray();
        private readonly Dictionary<string, float> widths = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly StringFormat typographic;
        private Bitmap measureSurface;
        private Graphics measure;
        private List<ChatLine> displayed = new List<ChatLine>();
        private int firstRow, rowHeight = 14, dragOffset = -1, layoutWidth = -1;
        private bool showTimestamps = true, dragged;
        private ChatLink hover;

        public ChatView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = BotUi.PaperLight; ForeColor = BotUi.Ink; Font = BotFonts.Get(FontSize);
            Padding = new Padding(4, 2, 2, 2);
            typographic = (StringFormat)StringFormat.GenericTypographic.Clone();
            typographic.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
            typographic.Trimming = StringTrimming.None;
            AccessibleName = "Messages du chat"; AccessibleRole = AccessibleRole.List;
        }

        /// <summary>Clic sur un nom ou des coordonnées.</summary>
        public event EventHandler<ChatLinkEventArgs> LinkClicked;
        /// <summary>Clic droit sur une ligne, hors lien (menu « copier », « nom »).</summary>
        public event EventHandler<ChatLineEventArgs> LineMenuRequested;

        /// <summary>Lignes gardées (150 au plus), toutes filtres confondus.</summary>
        public IReadOnlyList<ChatLine> Lines => lines.AsReadOnly();
        /// <summary>Lignes affichées : visibles selon les filtres, les 30 dernières.</summary>
        public IReadOnlyList<ChatLine> DisplayedLines => displayed.AsReadOnly();
        /// <summary>Heure devant les lignes (option <c>TimestampInChat</c> du client, active par défaut).</summary>
        public bool ShowTimestamps
        {
            get { return showTimestamps; }
            set { if (showTimestamps == value) return; showTimestamps = value; Relayout(true); }
        }
        public int RowCount => rows.Count;
        public int FirstRow => firstRow;
        public int VisibleRowCount => Math.Max(1, (ClientSize.Height - Padding.Vertical) / Math.Max(1, rowHeight));
        public bool AtBottom => firstRow >= MaxFirstRow;
        private int MaxFirstRow => Math.Max(0, rows.Count - VisibleRowCount);

        public bool IsFilterVisible(ChatFilter filter) { int index = (int)filter; return index < 0 || index >= visible.Length || visible[index]; }

        public void SetFilterVisible(ChatFilter filter, bool value)
        {
            int index = (int)filter;
            if (index < 0 || index >= visible.Length || visible[index] == value) return;
            visible[index] = value; Relayout(true);
        }

        public void Append(ChatLine line)
        {
            if (line == null || IsDisposed) return;
            lines.Add(line);
            if (lines.Count > MaxLines) lines.RemoveRange(0, lines.Count - MaxLines);
            Relayout(false);
        }

        public void Clear()
        {
            lines.Clear(); hover = null; firstRow = 0; Relayout(true);
        }

        public void ScrollToBottom() { firstRow = MaxFirstRow; Invalidate(); }

        /// <summary>Fait défiler la vue pour montrer la première rangée de la ligne ; faux si elle n'est pas affichée.</summary>
        public bool EnsureVisible(ChatLine line)
        {
            int start = rows.FindIndex(row => ReferenceEquals(row.Line, line));
            if (start < 0) return false;
            if (start < firstRow || start >= firstRow + VisibleRowCount) { firstRow = Math.Max(0, Math.Min(MaxFirstRow, start)); Invalidate(); }
            return true;
        }

        public void ScrollBy(int delta)
        {
            int target = Math.Max(0, Math.Min(MaxFirstRow, firstRow + delta));
            if (target == firstRow) return;
            firstRow = target; Invalidate();
        }

        /// <summary>Liens des lignes à l'écran et leur zone.</summary>
        public IReadOnlyList<ChatLinkArea> LinkAreas
        {
            get
            {
                var areas = new List<ChatLinkArea>();
                int y = Padding.Top;
                for (int index = firstRow; index < rows.Count && y < ClientSize.Height; index++, y += rowHeight)
                    foreach (Run run in rows[index].Runs)
                        if (run.Link != null)
                            areas.Add(new ChatLinkArea { Link = run.Link, Line = rows[index].Line,
                                Bounds = new Rectangle(Padding.Left + (int)run.X, y, Math.Max(1, (int)Math.Ceiling(run.Width)), rowHeight) });
                return areas;
            }
        }

        public ChatLink LinkAt(Point point)
        {
            foreach (ChatLinkArea area in LinkAreas) if (area.Bounds.Contains(point)) return area.Link;
            return null;
        }

        public ChatLine LineAt(Point point)
        {
            if (point.X < 0 || point.X >= TextRight || point.Y < Padding.Top) return null;
            int index = firstRow + (point.Y - Padding.Top) / Math.Max(1, rowHeight);
            return index >= 0 && index < rows.Count ? rows[index].Line : null;
        }

        /// <summary>Traite un clic à cette position (aussi utilisé par les tests) ; vrai si un lien ou une ligne a répondu.</summary>
        public bool ClickAt(Point point, MouseButtons button, Keys modifiers)
        {
            ChatLink link = LinkAt(point);
            if (link != null && (button == MouseButtons.Left || button == MouseButtons.Right))
            {
                Raise(() => LinkClicked?.Invoke(this, new ChatLinkEventArgs(link, button, modifiers, point)));
                return true;
            }
            if (button != MouseButtons.Right) return false;
            ChatLine line = LineAt(point);
            if (line == null) return false;
            Raise(() => LineMenuRequested?.Invoke(this, new ChatLineEventArgs(line, point)));
            return true;
        }

        /// <summary>Texte d'une ligne tel qu'affiché (heure comprise si elle est montrée), pour « copier ».</summary>
        public string DisplayText(ChatLine line) => line == null ? string.Empty : (showTimestamps && line.Timestamped ? line.TimePrefix : string.Empty) + line.Text;

        private int TextRight => ClientSize.Width - Padding.Right - ScrollBarWidth - 1;

        private void Raise(Action action)
        {
            try { action(); }
            catch (Exception error) when (!(error is OutOfMemoryException)) { System.Diagnostics.Trace.WriteLine("Chat : " + error.Message); }
        }

        // ------------------------------------------------------------------ mise en page

        private void Relayout(bool keepPosition)
        {
            if (IsDisposed) return;
            int distance = MaxFirstRow - firstRow;
            ChatLine anchor = firstRow < rows.Count ? rows[firstRow].Line : null;
            int anchorOffset = 0;
            if (anchor != null) { int start = rows.FindIndex(row => ReferenceEquals(row.Line, anchor)); anchorOffset = Math.Max(0, firstRow - start); }

            displayed = DisplayableLines();
            LayoutRows();

            if (!keepPosition && distance < StopScrollRows) firstRow = MaxFirstRow;
            else if (keepPosition && distance <= 0) firstRow = MaxFirstRow;
            else if (anchor != null)
            {
                int start = rows.FindIndex(row => ReferenceEquals(row.Line, anchor));
                firstRow = start < 0 ? 0 : start + anchorOffset;
            }
            firstRow = Math.Max(0, Math.Min(MaxFirstRow, firstRow));
            if (hover != null && !displayed.Any(line => line.Links.Contains(hover))) hover = null;
            Invalidate();
        }

        private List<ChatLine> DisplayableLines()
        {
            var result = new List<ChatLine>();
            for (int index = lines.Count - 1; index >= 0 && result.Count < MaxVisibleLines; index--)
                if (IsFilterVisible(lines[index].Filter)) result.Add(lines[index]);
            result.Reverse();
            return result;
        }

        private Graphics Measure()
        {
            if (measure != null) return measure;
            measureSurface = new Bitmap(1, 1);
            measure = Graphics.FromImage(measureSurface);
            measure.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            return measure;
        }

        private float TextWidth(string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            string key = ((int)font.Style).ToString(CultureInfo.InvariantCulture) + "|" + text;
            float width;
            if (widths.TryGetValue(key, out width)) return width;
            if (widths.Count > 8000) widths.Clear();
            width = Measure().MeasureString(text, font, PointF.Empty, typographic).Width;
            widths[key] = width;
            return width;
        }

        private void LayoutRows()
        {
            rows.Clear();
            Font regular = BotFonts.Get(FontSize);
            rowHeight = Math.Max(10, (int)Math.Ceiling(regular.GetHeight(Measure())) + 1);
            float available = Math.Max(24, TextRight - Padding.Left);
            layoutWidth = ClientSize.Width;
            foreach (ChatLine line in displayed)
            {
                var row = new Row { Line = line };
                float x = 0;
                var pieces = new List<ChatSegment>();
                if (showTimestamps && line.Timestamped) pieces.Add(new ChatSegment(line.TimePrefix));
                pieces.AddRange(line.Segments);
                foreach (ChatSegment piece in pieces)
                {
                    Font font = BotFonts.Get(FontSize, (piece.Bold ? FontStyle.Bold : FontStyle.Regular) | (piece.Italic ? FontStyle.Italic : FontStyle.Regular));
                    bool underline = piece.Link != null && piece.Link.Kind == ChatLinkKind.Coordinates;
                    foreach (string token in Tokens(piece.Text))
                    {
                        float width = TextWidth(token, font);
                        if (x > 0 && x + width > available && token.Trim().Length > 0) { rows.Add(row); row = new Row { Line = line }; x = 0; }
                        if (width <= available || token.Length < 2)
                        {
                            row.Runs.Add(new Run { Text = token, Font = font, X = x, Width = width, Link = piece.Link, Underline = underline });
                            x += width;
                            continue;
                        }
                        // Mot plus large que la zone : coupé au caractère.
                        int start = 0;
                        while (start < token.Length)
                        {
                            int length = 1;
                            while (start + length < token.Length && TextWidth(token.Substring(start, length + 1), font) <= available - x) length++;
                            string part = token.Substring(start, length);
                            float partWidth = TextWidth(part, font);
                            if (x > 0 && x + partWidth > available) { rows.Add(row); row = new Row { Line = line }; x = 0; continue; }
                            row.Runs.Add(new Run { Text = part, Font = font, X = x, Width = partWidth, Link = piece.Link, Underline = underline });
                            x += partWidth; start += length;
                            if (start < token.Length) { rows.Add(row); row = new Row { Line = line }; x = 0; }
                        }
                    }
                }
                rows.Add(row);
            }
        }

        /// <summary>Mots suivis de leurs espaces (une espace de tête forme son propre morceau).</summary>
        private static IEnumerable<string> Tokens(string text)
        {
            if (string.IsNullOrEmpty(text)) yield break;
            var builder = new StringBuilder();
            bool inSpaces = false;
            foreach (char character in text)
            {
                bool space = character == ' ';
                if (!space && inSpaces && builder.Length > 0) { yield return builder.ToString(); builder.Clear(); inSpaces = false; }
                builder.Append(character);
                if (space) inSpaces = true;
            }
            if (builder.Length > 0) yield return builder.ToString();
        }

        // ------------------------------------------------------------------ dessin et souris

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(BackColor);
            if (layoutWidth != ClientSize.Width) { LayoutRows(); firstRow = Math.Max(0, Math.Min(MaxFirstRow, firstRow)); }
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            int y = Padding.Top;
            var clip = new Rectangle(0, 0, TextRight + 1, ClientSize.Height);
            var state = graphics.Save();
            graphics.SetClip(clip);
            for (int index = firstRow; index < rows.Count && y < ClientSize.Height; index++, y += rowHeight)
            {
                Row row = rows[index];
                using (var brush = new SolidBrush(row.Line.Color))
                    foreach (Run run in row.Runs)
                    {
                        float x = Padding.Left + run.X;
                        graphics.DrawString(run.Text, run.Font, brush, x, y, typographic);
                        if (run.Underline || (run.Link != null && ReferenceEquals(run.Link, hover) && run.Text.Trim().Length > 0))
                            using (var pen = new Pen(row.Line.Color))
                            {
                                float underline = y + rowHeight - 2;
                                graphics.DrawLine(pen, x, underline, x + TextWidth(run.Text.TrimEnd(' '), run.Font), underline);
                            }
                    }
            }
            graphics.Restore(state);
            PaintScrollBar(graphics);
            if (Focused && ShowFocusCues)
                using (var pen = new Pen(BotUi.Gold) { DashStyle = DashStyle.Dot })
                    graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        private Rectangle Track => new Rectangle(ClientSize.Width - Padding.Right - ScrollBarWidth, Padding.Top, ScrollBarWidth, Math.Max(1, ClientSize.Height - Padding.Vertical));

        private Rectangle Thumb
        {
            get
            {
                Rectangle track = Track;
                int total = Math.Max(1, rows.Count), shown = Math.Min(total, VisibleRowCount);
                int height = Math.Max(10, track.Height * shown / total);
                int range = Math.Max(1, MaxFirstRow);
                int top = track.Top + (track.Height - height) * Math.Min(firstRow, range) / range;
                return new Rectangle(track.X + 1, top, track.Width - 2, height);
            }
        }

        private void PaintScrollBar(Graphics graphics)
        {
            if (rows.Count <= VisibleRowCount) return;
            Rectangle track = Track, thumb = Thumb;
            using (var back = new SolidBrush(BotUi.Paper)) graphics.FillRectangle(back, track);
            using (var fill = new SolidBrush(BotUi.Gold)) graphics.FillRectangle(fill, thumb);
            using (var edge = new Pen(BotUi.Muted)) graphics.DrawRectangle(edge, thumb.X, thumb.Y, thumb.Width - 1, thumb.Height - 1);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (CanFocus && !Focused) Focus();
            dragged = false;
            if (e.Button != MouseButtons.Left || rows.Count <= VisibleRowCount || !Track.Contains(e.Location)) return;
            Rectangle thumb = Thumb;
            if (thumb.Contains(e.Location)) { dragOffset = e.Y - thumb.Top; dragged = true; return; }
            dragged = true;
            ScrollBy(e.Y < thumb.Top ? -VisibleRowCount : VisibleRowCount);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragOffset >= 0)
            {
                Rectangle track = Track, thumb = Thumb;
                int free = Math.Max(1, track.Height - thumb.Height);
                int target = (int)Math.Round((double)(e.Y - dragOffset - track.Top) * MaxFirstRow / free);
                int clamped = Math.Max(0, Math.Min(MaxFirstRow, target));
                if (clamped != firstRow) { firstRow = clamped; Invalidate(); }
                return;
            }
            ChatLink link = LinkAt(e.Location);
            if (!ReferenceEquals(link, hover)) { hover = link; Invalidate(); }
            Cursor = link != null ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != null) { hover = null; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool wasDragging = dragged; dragOffset = -1; dragged = false;
            if (!wasDragging) ClickAt(e.Location, e.Button, ModifierKeys);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollBy(-Math.Sign(e.Delta) * Math.Max(1, Math.Abs(e.Delta) / 120) * 3);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.PageUp || key == Keys.PageDown || key == Keys.Home || key == Keys.End || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.PageUp: ScrollBy(-VisibleRowCount); e.Handled = true; break;
                case Keys.PageDown: ScrollBy(VisibleRowCount); e.Handled = true; break;
                case Keys.Home: if (e.Control) { firstRow = 0; Invalidate(); e.Handled = true; } break;
                case Keys.End: if (e.Control) { ScrollToBottom(); e.Handled = true; } break;
            }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnResize(EventArgs e)
        {
            bool bottom = AtBottom;
            base.OnResize(e);
            if (layoutWidth != ClientSize.Width) LayoutRows();
            firstRow = bottom ? MaxFirstRow : Math.Max(0, Math.Min(MaxFirstRow, firstRow));
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                measure?.Dispose(); measure = null;
                measureSurface?.Dispose(); measureSurface = null;
                typographic.Dispose();
                LinkClicked = null; LineMenuRequested = null;
            }
            base.Dispose(disposing);
        }
    }
}
