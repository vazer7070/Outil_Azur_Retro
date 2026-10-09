using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Data;

namespace Outil_Azur_complet.Bot.Controls.Chat
{
    /// <summary>
    /// Volet « Smileys et attitudes » du client (<c>_sSmileys</c>) : les quinze smileys (<c>clips/smileys/1..15</c>, cinq par
    /// ligne) et les attitudes connues du personnage (liste <c>eL</c>, images <c>clips/emotes</c>), infobulle « nom
    /// (/raccourci) » tirée de <c>emotes_fr</c>. Les images viennent de <c>ressources/Bot/Smileys</c> et <c>Emotes</c> (lot
    /// D3), lues hors du thread de l'interface ; une image absente laisse le numéro. Un clic lève
    /// <see cref="SmileySelected"/> ou <see cref="EmoteSelected"/> ; le volet reste ouvert, comme le client par défaut.
    /// </summary>
    public sealed class SmileyPicker : Control
    {
        public const int Columns = 5;
        public const int Cell = 26;
        private const int Inset = 6, Header = 16, Gap = 10;

        private readonly List<int> smileys;
        private readonly List<int> emotes = new List<int>();
        private readonly ToolTip tips = new ToolTip { InitialDelay = 300, ReshowDelay = 100 };
        private Rectangle hover = Rectangle.Empty;
        private string tipShown;

        public SmileyPicker()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = BotUi.Paper; ForeColor = BotUi.Ink; Cursor = Cursors.Hand;
            smileys = Enumerable.Range(1, LangData.Smiley.Count).ToList();
            AccessibleName = ChatUiText.Get("CHAT_SHOW_SMILEYS", "Smileys et attitudes");
            Size = PickerSize;
        }

        public event Action<int> SmileySelected;
        public event Action<int> EmoteSelected;

        public IReadOnlyList<int> Smileys => smileys.AsReadOnly();
        public IReadOnlyList<int> Emotes => emotes.AsReadOnly();

        /// <summary>Attitudes proposées (identifiants positifs, triés, sans doublon).</summary>
        public void SetEmotes(IEnumerable<int> ids)
        {
            List<int> next = (ids ?? Enumerable.Empty<int>()).Where(id => id > 0).Distinct().OrderBy(id => id).ToList();
            if (next.SequenceEqual(emotes)) return;
            emotes.Clear(); emotes.AddRange(next);
            Size = PickerSize; Invalidate();
        }

        /// <summary>Taille du volet pour les smileys et les attitudes courantes.</summary>
        public Size PickerSize
        {
            get
            {
                int smileyRows = Math.Max(1, (smileys.Count + Columns - 1) / Columns);
                int emoteRows = Math.Max(1, (emotes.Count + Columns - 1) / Columns);
                return new Size(2 * Inset + 2 * Columns * Cell + Gap, Header + Inset + Math.Max(smileyRows, emoteRows) * Cell + Inset);
            }
        }

        public Rectangle SmileyBounds(int id)
        {
            int index = smileys.IndexOf(id);
            return index < 0 ? Rectangle.Empty : new Rectangle(Inset + index % Columns * Cell, Header + Inset + index / Columns * Cell, Cell, Cell);
        }

        public Rectangle EmoteBounds(int id)
        {
            int index = emotes.IndexOf(id);
            int left = Inset + Columns * Cell + Gap;
            return index < 0 ? Rectangle.Empty : new Rectangle(left + index % Columns * Cell, Header + Inset + index / Columns * Cell, Cell, Cell);
        }

        /// <summary>Traite un clic à cette position (aussi utilisé par les tests) ; vrai si un smiley ou une attitude a été choisi.</summary>
        public bool ClickAt(Point point)
        {
            try
            {
                foreach (int id in smileys) if (SmileyBounds(id).Contains(point)) { SmileySelected?.Invoke(id); return true; }
                foreach (int id in emotes) if (EmoteBounds(id).Contains(point)) { EmoteSelected?.Invoke(id); return true; }
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { System.Diagnostics.Trace.WriteLine("Chat : " + error.Message); }
            return false;
        }

        /// <summary>Infobulle d'une case : « Smiley n » ou « nom (/raccourci) » de l'attitude.</summary>
        public string TooltipAt(Point point)
        {
            foreach (int id in smileys) if (SmileyBounds(id).Contains(point)) return "Smiley " + id.ToString(CultureInfo.InvariantCulture);
            foreach (int id in emotes) if (EmoteBounds(id).Contains(point)) return EmoteTitle(id);
            return null;
        }

        public static string EmoteTitle(int id)
        {
            string key = id.ToString(CultureInfo.InvariantCulture);
            try
            {
                if (LangData.IsLoaded("emotes") && LangData.Raw("emotes", "emote", key) != null)
                {
                    string command = LangData.Emote.Command(id);
                    return LangData.Emote.Name(id) + (string.IsNullOrEmpty(command) || command == key ? string.Empty : " (/" + command + ")");
                }
            }
            catch (Exception) { /* Fichier de langue illisible : numéro de l'attitude. */ }
            return "Attitude " + key;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(BackColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var frame = new Pen(BotUi.Gold)) graphics.DrawRectangle(frame, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            int left = Inset + Columns * Cell + Gap;
            using (var brush = new SolidBrush(BotUi.Muted))
            {
                Font font = BotFonts.Get(7.5f, FontStyle.Bold);
                graphics.DrawString("Smileys", font, brush, Inset, 4);
                graphics.DrawString("Attitudes", font, brush, left, 4);
            }
            using (var line = new Pen(BotUi.Gold)) graphics.DrawLine(line, left - Gap / 2, Header, left - Gap / 2, ClientSize.Height - Inset);
            foreach (int id in smileys) PaintCell(graphics, SmileyBounds(id), "Smileys", id);
            foreach (int id in emotes) PaintCell(graphics, EmoteBounds(id), "Emotes", id);
            if (emotes.Count == 0)
                using (var brush = new SolidBrush(BotUi.Muted))
                    graphics.DrawString("Aucune attitude reçue", BotFonts.Get(7.5f), brush, new RectangleF(left, Header + Inset, Columns * Cell, Cell * 2));
        }

        private void PaintCell(Graphics graphics, Rectangle cell, string family, int id)
        {
            if (cell.IsEmpty) return;
            if (cell == hover)
            {
                Bitmap highlight = ChatImages.Get("Client", "SmileysHighlight", this);
                if (highlight != null) ChatImages.Draw(graphics, highlight, cell);
                else using (var glow = new SolidBrush(Color.FromArgb(90, BotUi.Gold))) graphics.FillEllipse(glow, Rectangle.Inflate(cell, -1, -1));
            }
            Bitmap image = ChatImages.Get(family, id.ToString(CultureInfo.InvariantCulture), this);
            if (image != null) { ChatImages.Draw(graphics, image, Rectangle.Inflate(cell, -3, -3)); return; }
            using (var fill = new SolidBrush(BotUi.PaperLight)) graphics.FillEllipse(fill, Rectangle.Inflate(cell, -4, -4));
            using (var edge = new Pen(BotUi.Gold)) graphics.DrawEllipse(edge, Rectangle.Inflate(cell, -4, -4));
            using (var brush = new SolidBrush(BotUi.Ink))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                graphics.DrawString(id.ToString(CultureInfo.InvariantCulture), BotFonts.Get(7.5f), brush, cell, format);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Rectangle cell = Rectangle.Empty;
            foreach (int id in smileys) if (SmileyBounds(id).Contains(e.Location)) cell = SmileyBounds(id);
            foreach (int id in emotes) if (EmoteBounds(id).Contains(e.Location)) cell = EmoteBounds(id);
            if (cell != hover) { hover = cell; Invalidate(); }
            string tip = TooltipAt(e.Location);
            if (tip != tipShown) { tipShown = tip; tips.SetToolTip(this, tip ?? string.Empty); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!hover.IsEmpty) { hover = Rectangle.Empty; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) ClickAt(e.Location);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { tips.Dispose(); SmileySelected = null; EmoteSelected = null; }
            base.Dispose(disposing);
        }
    }
}
