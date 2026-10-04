using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Interactions;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Document ouvert par le serveur (<c>dofus.aks.Documents</c> : <c>dCK&lt;id&gt;_&lt;date&gt;</c>, fermeture <c>dV</c>) : livre,
    /// parchemin ou pancarte, dessinés sur les fonds exportés du client (<c>UI_DocumentBook</c>, <c>UI_DocumentParchment</c>,
    /// <c>UI_DocumentRoadSign</c>). Le texte de la page est mis en forme (gras, italique, souligné, titres) ; les illustrations,
    /// non exportées, sont signalées par « [illustration] ». Fermer (ou ×/Échap) envoie <c>dV</c>.
    /// </summary>
    public sealed class DocumentPanel : GamePanel
    {
        private const string NoDocumentText = "Un document s'ouvre en lisant un livre ou en s'arrêtant devant une pancarte.";
        private DocumentSheet documentSheet;
        private RichTextBox documentText;
        private Label documentPage, documentStatus;
        private ComboBox documentChapters;
        private Control documentPrevious, documentNext, documentLeave;
        private FlowLayoutPanel documentNavigation;
        private bool wasOpen, updatingChapters;
        private BotDocument shown;
        private int pageIndex;

        public override string Title => "Document";
        public override Image Icon => ClientAssets.Icon(Path.Combine("Interactifs", "document-livre"), 24);
        public override bool IsModal => true;
        public override bool IsServerWindowOpen => Game?.Interactions?.Interactive?.Document?.IsOpen == true;
        /// <summary>Page affichée (index dans <see cref="BotDocument.Pages"/>).</summary>
        internal int PageIndex => pageIndex;

        protected override Control CreateView()
        {
            var page = Page();
            documentSheet = new DocumentSheet { Dock = DockStyle.Top, Height = 190, AccessibleName = "Couverture du document" };
            documentNavigation = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, WrapContents = false, Padding = new Padding(0, 4, 0, 0), BackColor = BotUi.Paper };
            documentPrevious = MakeButton("‹", (s, e) => ShowPage(pageIndex - 1), false, 34);
            documentPrevious.AccessibleName = Text("PREVIOUS_PAGE", "Page précédente");
            documentPage = MakeLabel(string.Empty, 9);
            documentPage.AutoSize = false; documentPage.Width = 86; documentPage.Height = 30; documentPage.TextAlign = ContentAlignment.MiddleCenter;
            documentNext = MakeButton("›", (s, e) => ShowPage(pageIndex + 1), false, 34);
            documentNext.AccessibleName = Text("NEXT_PAGE", "Page suivante");
            documentChapters = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, BackColor = BotUi.PaperLight, ForeColor = BotUi.Ink,
                FlatStyle = FlatStyle.Flat, Margin = new Padding(6, 4, 0, 0), AccessibleName = Text("TABLE_OF_CONTENTS", "Sommaire") };
            documentChapters = WithFont(documentChapters, BotFonts.Get(8.25f));
            documentChapters.SelectedIndexChanged += (s, e) => GoToChapter();
            documentNavigation.Controls.AddRange(new Control[] { documentPrevious, documentPage, documentNext, documentChapters });
            documentText = MakeJournal();
            documentText.AccessibleName = "Texte de la page"; documentText.ScrollBars = RichTextBoxScrollBars.Vertical; documentText.WordWrap = true;
            documentStatus = MakeStatus(NoDocumentText);
            documentLeave = MakeButton(Text("CLOSE", "Fermer"), async (s, e) => await Leave(), false, 100);
            page.Controls.Add(documentText); page.Controls.Add(documentNavigation); page.Controls.Add(documentSheet);
            page.Controls.Add(documentStatus); page.Controls.Add(BotUi.Actions(documentLeave));
            return page;
        }

        protected override void OnBind(GameClass game) { wasOpen = false; game.Interactions.Interactive.Document.Changed += OnServerChanged; OnServerChanged(); }
        protected override void OnUnbind(GameClass game) { wasOpen = false; if (game.Interactions != null) game.Interactions.Interactive.Document.Changed -= OnServerChanged; }

        private void OnServerChanged() => OnUi(() =>
        {
            bool open = IsServerWindowOpen;
            if (open && !wasOpen) RequestShow();
            else if (!open && wasOpen) RaiseClosed();
            wasOpen = open;
            RefreshView();
        });

        public override void RefreshView()
        {
            if (Game == null || documentText == null) return;
            DocumentDialog dialog = Game.Interactions.Interactive.Document;
            BotDocument document = dialog.IsOpen ? dialog.Current : null;
            if (!ReferenceEquals(document, shown))
            {
                shown = document; pageIndex = 0;
                FillChapters(document);
                documentSheet.Document = document;
                RenderPage();
            }
            documentStatus.Text = dialog.IsOpen
                ? (dialog.LastMessage.Length > 0 ? dialog.LastMessage : "Document " + dialog.RequestedKey + ".")
                : NoDocumentText + (dialog.LastMessage.Length > 0 ? " " + dialog.LastMessage : string.Empty);
            documentLeave.Enabled = Connected && dialog.IsOpen;
            UpdateNavigation();
        }

        private void FillChapters(BotDocument document)
        {
            updatingChapters = true;
            try
            {
                documentChapters.Items.Clear();
                if (document != null && document.IsBook && document.Chapters.Count > 0)
                {
                    documentChapters.Items.Add(new ChapterItem(Text("TABLE_OF_CONTENTS", "Sommaire"), -1));
                    foreach (DocumentChapter chapter in document.Chapters.Where(entry => entry.PageIndex >= 0 && entry.PageIndex < document.Pages.Count))
                        documentChapters.Items.Add(new ChapterItem(chapter.Title, chapter.PageIndex));
                    documentChapters.SelectedIndex = 0;
                }
            }
            finally { updatingChapters = false; }
        }

        private void GoToChapter()
        {
            if (updatingChapters || !(documentChapters.SelectedItem is ChapterItem item) || item.PageIndex < 0) return;
            ShowPage(item.PageIndex);
        }

        /// <summary>Affiche une page du livre (bornée aux pages exportées).</summary>
        internal void ShowPage(int index)
        {
            if (shown == null || shown.Pages.Count == 0) return;
            int target = Math.Max(0, Math.Min(shown.Pages.Count - 1, index));
            if (target == pageIndex) return;
            pageIndex = target;
            RenderPage();
            UpdateNavigation();
        }

        private void UpdateNavigation()
        {
            int count = shown?.Pages.Count ?? 0;
            documentNavigation.Visible = shown != null && shown.IsBook && count > 0;
            documentPage.Text = count == 0 ? string.Empty : "Page " + (pageIndex + 1) + " / " + count;
            documentPrevious.Enabled = count > 0 && pageIndex > 0;
            documentNext.Enabled = count > 0 && pageIndex < count - 1;
            documentChapters.Visible = documentChapters.Items.Count > 0;
            documentSheet.PageLabel = documentPage.Text;
            documentSheet.Chapter = shown?.Chapters.LastOrDefault(chapter => chapter.PageIndex <= pageIndex)?.Title ?? string.Empty;
        }

        /// <summary>Page courante dans la zone de texte : un paragraphe par ligne, titres centrés, styles du HTML du document.</summary>
        private void RenderPage()
        {
            documentText.SuspendLayout();
            documentText.Clear();
            if (shown == null)
            {
                Append(Game?.Interactions?.Interactive?.Document?.IsOpen == true
                    ? "Ce document n'est pas dans les ressources du bot ; seul « Fermer » est possible." : string.Empty, BotFonts.Get(9), BotUi.Muted, HorizontalAlignment.Left);
            }
            else
            {
                IReadOnlyList<DocumentParagraph> paragraphs = shown.PageParagraphs(pageIndex);
                if (paragraphs.Count == 0) Append("(page vide)", BotFonts.Get(9, FontStyle.Italic), BotUi.Muted, HorizontalAlignment.Center);
                bool first = true;
                foreach (DocumentParagraph paragraph in paragraphs)
                {
                    if (!first) Append(Environment.NewLine, BotFonts.Get(6), BotUi.Ink, HorizontalAlignment.Left);
                    first = false;
                    float size = Size(paragraph.Class);
                    HorizontalAlignment alignment = paragraph.IsTitle ? HorizontalAlignment.Center : HorizontalAlignment.Left;
                    foreach (DocumentRun run in paragraph.Runs)
                    {
                        FontStyle style = (run.Bold || paragraph.IsTitle ? FontStyle.Bold : FontStyle.Regular) | (run.Italic || run.IsImage || paragraph.Class == "s" ? FontStyle.Italic : FontStyle.Regular)
                            | (run.Underline || run.IsLink ? FontStyle.Underline : FontStyle.Regular);
                        Color color = run.IsImage ? BotUi.Muted : run.IsLink ? BotUi.Olive : BotUi.Ink;
                        Append(run.Text.Replace("\n", Environment.NewLine), BotFonts.Get(size, style), color, alignment);
                    }
                    Append(Environment.NewLine, BotFonts.Get(size), BotUi.Ink, alignment);
                }
            }
            documentText.SelectionStart = 0; documentText.SelectionLength = 0;
            documentText.ResumeLayout();
        }

        private void Append(string text, Font font, Color color, HorizontalAlignment alignment)
        {
            if (string.IsNullOrEmpty(text)) return;
            documentText.SelectionStart = documentText.TextLength; documentText.SelectionLength = 0;
            documentText.SelectionFont = font; documentText.SelectionColor = color; documentText.SelectionAlignment = alignment;
            documentText.AppendText(text);
        }

        /// <summary>Tailles des classes CSS des documents (t1, t2 : titres ; s : petite note ; m, n : texte).</summary>
        private static float Size(string cssClass)
        {
            switch (cssClass)
            {
                case "t1": return 12;
                case "chapter": return 11;
                case "t2": return 10.5f;
                case "s": return 8.25f;
                default: return 9.5f;
            }
        }

        /// <summary>Texte de la page tel qu'affiché (pour l'accessibilité et les tests).</summary>
        internal string PageText => documentText?.Text ?? string.Empty;

        protected internal override bool OnUserClose()
        {
            if (!IsServerWindowOpen) return true;
            _ = Leave();
            return false;
        }

        private Task Leave() => ReportAsync(() => Game.Interactions.Interactive.Document.LeaveAsync());

        private static string Text(string key, string fallback) => LangData.Text.Has(key) ? LangData.Text.Get(key).Trim() : fallback;

        private sealed class ChapterItem
        {
            internal ChapterItem(string title, int pageIndex) { Title = title ?? string.Empty; PageIndex = pageIndex; }
            internal string Title { get; }
            internal int PageIndex { get; }
            public override string ToString() => Title;
        }
    }

    /// <summary>
    /// Fond du document peint avec l'image exportée du client selon son type : livre ouvert (titre, sous-titre et auteur sur la
    /// page de droite, chapitre et page sur celle de gauche), parchemin (titre) ou pancarte (texte de la pancarte sur la planche).
    /// </summary>
    internal sealed class DocumentSheet : Control
    {
        private BotDocument document;
        private string pageLabel = string.Empty, chapter = string.Empty;

        internal DocumentSheet()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = BotUi.Paper;
        }

        internal BotDocument Document { get => document; set { document = value; Invalidate(); } }
        internal string PageLabel { get => pageLabel; set { pageLabel = value ?? string.Empty; Invalidate(); } }
        internal string Chapter { get => chapter; set { chapter = value ?? string.Empty; Invalidate(); } }

        /// <summary>Image de fond du type de document (<c>Interactifs/document-…</c>).</summary>
        internal static string Background(string type)
        {
            switch (type)
            {
                case "parchment": return Path.Combine("Interactifs", "document-parchemin");
                case "roadsignleft": case "roadsignright": return Path.Combine("Interactifs", "document-pancarte");
                default: return Path.Combine("Interactifs", "document-livre");
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            using (var paper = new SolidBrush(BackColor)) graphics.FillRectangle(paper, ClientRectangle);
            string type = document?.Type ?? "book";
            Image background = ClientAssets.Get(Background(type));
            RectangleF area = Rectangle.Inflate(ClientRectangle, -4, -4);
            RectangleF drawn = background != null ? ClientAssets.DrawFit(graphics, background, area) : area;
            if (background == null)
                using (var frame = new Pen(BotUi.Frame, 2)) graphics.DrawRectangle(frame, Rectangle.Round(drawn));
            if (document == null)
            {
                DrawLines(graphics, Region(drawn, 0.10f, 0.20f, 0.80f, 0.60f), new[] { ("Aucun document", 10f, FontStyle.Bold, BotUi.Muted) });
                return;
            }
            switch (type)
            {
                case "parchment":
                    DrawLines(graphics, Region(drawn, 0.12f, 0.12f, 0.82f, 0.76f), TitleLines(document));
                    break;
                case "roadsignleft": case "roadsignright":
                    // Planche de la pancarte (d'après UI_DocumentRoadSignLeft) : texte de la seule page.
                    var lines = document.PageParagraphs(0).Select(paragraph => (paragraph.PlainText.Trim(), paragraph.IsTitle || paragraph.Runs.Any(run => run.Bold) ? 10f : 8.5f,
                        paragraph.Runs.Any(run => run.Bold) ? FontStyle.Bold : paragraph.Class == "s" ? FontStyle.Italic : FontStyle.Regular, BotUi.Ink)).Where(line => line.Item1.Length > 0).ToArray();
                    DrawLines(graphics, Region(drawn, 0.06f, 0.25f, 0.88f, 0.40f), lines.Length > 0 ? lines : TitleLines(document));
                    break;
                default:
                    DrawLines(graphics, Region(drawn, 0.53f, 0.12f, 0.38f, 0.72f), TitleLines(document));
                    var left = new List<(string, float, FontStyle, Color)>();
                    if (chapter.Length > 0) left.Add((chapter, 8.5f, FontStyle.Bold, BotUi.Ink));
                    if (pageLabel.Length > 0) left.Add((pageLabel, 8f, FontStyle.Italic, BotUi.Muted));
                    DrawLines(graphics, Region(drawn, 0.09f, 0.12f, 0.36f, 0.72f), left.ToArray());
                    break;
            }
        }

        private static (string, float, FontStyle, Color)[] TitleLines(BotDocument document)
        {
            var lines = new List<(string, float, FontStyle, Color)>();
            if (document.Title.Length > 0) lines.Add((document.Title.TrimStart('/', ' '), 11f, FontStyle.Bold, BotUi.Ink));
            if (document.Subtitle.Length > 0) lines.Add((document.Subtitle, 8.5f, FontStyle.Italic, BotUi.Ink));
            if (document.Author.Length > 0) lines.Add((document.Author, 8.5f, FontStyle.Regular, BotUi.Muted));
            if (lines.Count == 0) lines.Add(("Document " + document.Key, 10f, FontStyle.Bold, BotUi.Ink));
            return lines.ToArray();
        }

        private static RectangleF Region(RectangleF bounds, float x, float y, float width, float height) =>
            new RectangleF(bounds.X + bounds.Width * x, bounds.Y + bounds.Height * y, bounds.Width * width, bounds.Height * height);

        /// <summary>Lignes centrées verticalement dans la zone, coupées par mots et terminées par « … » si la place manque.</summary>
        private static void DrawLines(Graphics graphics, RectangleF zone, (string Text, float Size, FontStyle Style, Color Color)[] lines)
        {
            if (lines == null || lines.Length == 0 || zone.Width < 4 || zone.Height < 4) return;
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisWord })
            {
                var heights = lines.Select(line => Math.Min(zone.Height, graphics.MeasureString(line.Text, BotFonts.Get(line.Size, line.Style), (int)zone.Width, format).Height)).ToArray();
                float total = heights.Sum() + 4 * (lines.Length - 1);
                float y = zone.Y + Math.Max(0, (zone.Height - total) / 2);
                for (int index = 0; index < lines.Length && y < zone.Bottom; index++)
                {
                    float height = Math.Min(heights[index], zone.Bottom - y);
                    using (var ink = new SolidBrush(lines[index].Color))
                        graphics.DrawString(lines[index].Text, BotFonts.Get(lines[index].Size, lines[index].Style), ink, new RectangleF(zone.X, y, zone.Width, height), format);
                    y += height + 4;
                }
            }
        }
    }
}
