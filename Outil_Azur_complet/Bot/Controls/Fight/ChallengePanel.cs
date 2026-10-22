using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls.Banner;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Combats;

namespace Outil_Azur_complet.Bot.Controls.Fight
{
    /// <summary>Textes du client autour d'un défi (infobulle de <c>FightChallengeIcon</c>, bonus de <c>FightChallengeViewer</c>).</summary>
    internal static class ChallengeTexts
    {
        /// <summary>
        /// Infobulle de l'icône (<c>FightChallengeIcon.over</c>) : nom, description, « Butin : +n% », « XP : +n% », « Etat : … ».
        /// Une description vide (textes du client absents) n'ajoute pas de ligne.
        /// </summary>
        internal static string Tooltip(FightChallenge challenge)
        {
            if (challenge == null) return string.Empty;
            return challenge.Name + "\n" + (string.IsNullOrEmpty(challenge.Description) ? string.Empty : challenge.Description + "\n")
                + BannerArt.Text("LOOT", "Butin") + " : +" + Number(challenge.DropBonus) + "%\n"
                + BannerArt.Text("WORD_XP", "XP") + " : +" + Number(challenge.XpBonus) + "%\n"
                + BannerArt.Text("STATE", "Etat") + " : " + challenge.StateText;
        }

        /// <summary>Infobulle des bonus de la fenêtre de détail : « Bonus de base : n%\nBonus de groupe : n% ».</summary>
        internal static string BonusDetail(int basic, int team) =>
            BannerArt.Text("BASIC_BONUS", "Bonus de base") + " : " + Number(basic) + "%\n" + BannerArt.Text("GROUP_BONUS", "Bonus de groupe") + " : " + Number(team) + "%";

        /// <summary>Résumé d'un défi dans le résultat du combat : « nom (état) : XP +n%, Butin +n% ».</summary>
        internal static string Summary(FightChallenge challenge) =>
            challenge.Name + " (" + challenge.StateText.Trim() + ") : " + BannerArt.Text("WORD_XP", "XP") + " +" + Number(challenge.XpBonus) + "%, "
            + BannerArt.Text("LOOT", "Butin") + " +" + Number(challenge.DropBonus) + "%";

        internal static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Icône d'un défi (<c>FightChallengeIcon</c> du client) : image <c>Challenges/&lt;g&gt;.png</c> (<c>clips/challenges/&lt;g&gt;.swf</c>) et, pour
    /// un défi terminé, le symbole <c>ChallengeOK</c> (réussi) ou <c>ChallengeKO</c> (raté) posé dessus comme <c>_ldrState</c>.
    /// 38 × 38 dans la colonne du combat, 17 × 17 dans le résultat. Les images sont lues hors du fil de l'interface.
    /// </summary>
    public sealed class ChallengeIcon : Control
    {
        public const int ColumnSize = 38, ResultSize = 17;
        private Bitmap icon, mark;
        private FightChallenge challenge;
        private int? shownGfx;
        private FightChallengeState? shownState;

        public ChallengeIcon(int size)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(size, size); BackColor = BotUi.Frame; TabStop = false; AccessibleRole = AccessibleRole.Graphic;
        }

        public FightChallenge Challenge => challenge;
        /// <summary>Vrai quand l'image du défi a été lue (sinon une case orange porte son numéro).</summary>
        public bool HasIcon => icon != null;
        /// <summary>Vrai quand le symbole d'état (<c>ChallengeOK</c> / <c>ChallengeKO</c>) a été lu.</summary>
        public bool HasStateMark => mark != null;

        /// <summary>Affiche un défi (thread de l'interface) ; seules les images qui changent sont redemandées.</summary>
        public void Show(FightChallenge value)
        {
            if (value == null || IsDisposed) return;
            challenge = value;
            Name = "challenge-" + value.Id.ToString(CultureInfo.InvariantCulture);
            AccessibleName = value.Name; AccessibleDescription = value.StateText;
            if (shownGfx != value.Gfx)
            {
                shownGfx = value.Gfx; icon = null;
                int? gfx = value.Gfx;
                if (gfx.HasValue)
                    BannerArt.Request(this, "Challenges", gfx.Value.ToString(CultureInfo.InvariantCulture), image => { if (shownGfx == gfx) { icon = image; Invalidate(); } });
            }
            if (shownState != value.State)
            {
                shownState = value.State; mark = null;
                FightChallengeState state = value.State;
                if (state != FightChallengeState.Running)
                    BannerArt.Request(this, "Challenges", state == FightChallengeState.Succeeded ? "ChallengeOK" : "ChallengeKO",
                        image => { if (shownState == state) { mark = image; Invalidate(); } });
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias; graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (var back = new SolidBrush(Parent?.BackColor ?? BotUi.Frame)) graphics.FillRectangle(back, ClientRectangle);
            var area = new RectangleF(0, 0, Width, Height);
            if (icon != null) BannerArt.Draw(graphics, icon, area);
            else
            {
                // Sans l'image : la case orange du client et le numéro du défi.
                using (var fill = new SolidBrush(Color.FromArgb(232, 131, 39))) graphics.FillRectangle(fill, Rectangle.Inflate(ClientRectangle, -1, -1));
                using (var rule = new Pen(BotUi.FrameLight)) graphics.DrawRectangle(rule, 1, 1, Width - 3, Height - 3);
                if (challenge != null && Height >= 16)
                    using (var brush = new SolidBrush(BotUi.Frame))
                    using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        graphics.DrawString(challenge.Id.ToString(CultureInfo.InvariantCulture), BotFonts.Get(Height >= 30 ? 10 : 6.5f, FontStyle.Bold), brush, area, format);
            }
            if (challenge == null || challenge.State == FightChallengeState.Running) return;
            if (mark != null) { BannerArt.Draw(graphics, mark, area); return; }
            // Sans le symbole : coche verte ou croix rouge sur un voile, comme ChallengeOK / ChallengeKO.
            using (var veil = new SolidBrush(Color.FromArgb(110, BotUi.Frame))) graphics.FillRectangle(veil, area);
            bool done = challenge.State == FightChallengeState.Succeeded;
            float w = Width, h = Height;
            using (var pen = new Pen(done ? Color.FromArgb(70, 170, 40) : Color.FromArgb(200, 30, 20), Math.Max(2f, w / 8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                if (done) graphics.DrawLines(pen, new[] { new PointF(w * .25f, h * .55f), new PointF(w * .42f, h * .72f), new PointF(w * .76f, h * .30f) });
                else { graphics.DrawLine(pen, w * .28f, h * .28f, w * .72f, h * .72f); graphics.DrawLine(pen, w * .72f, h * .28f, w * .28f, h * .72f); }
            }
        }
    }

    /// <summary>
    /// Détail d'un défi (<c>FightChallengeViewer</c> du client, ouvert par un clic sur son icône) : titre <c>CURRENT_FIGHT_CHALLENGE</c>, nom,
    /// description avec la cible, bonus de butin et d'xp (infobulle : bonus de base et de groupe), état, bouton « voir la cible »
    /// (<c>_btnView</c>, présent si le défi désigne une cible) et fermeture. Parchemin et filets de la palette du client.
    /// </summary>
    public sealed class ChallengeViewer : Panel
    {
        public const int ViewerWidth = 180;
        private readonly Label title, name, description, dropTitle, dropValue, xpTitle, xpValue, state;
        private readonly ClientButton view, close;
        private readonly ChallengeIcon icon;
        private readonly ToolTip tips = new ToolTip();

        public ChallengeViewer()
        {
            DoubleBuffered = true; BackColor = BotUi.Paper; Visible = false; TabStop = false; Name = "challenge-viewer";
            AccessibleName = "Détail du challenge"; Width = ViewerWidth;
            title = MakeLabel(BannerArt.Text("CURRENT_FIGHT_CHALLENGE", "Challenge en cours"), 8.25f, true, BotUi.PaperLight);
            title.BackColor = BotUi.Frame; title.Location = new Point(1, 1); title.Size = new Size(ViewerWidth - 24, 20); title.TextAlign = ContentAlignment.MiddleLeft;
            title.Padding = new Padding(6, 0, 0, 0); title.Name = "challenge-viewer-title";
            close = new ClientButton { Text = "×", Width = 22, Height = 20, Location = new Point(ViewerWidth - 23, 1), Font = BotFonts.Get(9, FontStyle.Bold),
                Name = "challenge-viewer-close", AccessibleName = BannerArt.Text("CLOSE", "Fermer"), TabStop = false };
            close.Click += (s, e) => CloseRequested?.Invoke(this, EventArgs.Empty);
            tips.SetToolTip(close, BannerArt.Text("CLOSE", "Fermer"));
            icon = new ChallengeIcon(ChallengeIcon.ColumnSize) { Location = new Point(8, 28), BackColor = BotUi.Paper };
            name = MakeLabel(string.Empty, 9, true, BotUi.Ink); name.Location = new Point(52, 28); name.Size = new Size(ViewerWidth - 58, 38);
            name.TextAlign = ContentAlignment.MiddleLeft; name.Name = "challenge-viewer-name";
            description = MakeLabel(string.Empty, 8.25f, false, BotUi.Ink); description.AutoSize = true; description.MaximumSize = new Size(ViewerWidth - 16, 0);
            description.Location = new Point(8, 72); description.Name = "challenge-viewer-description";
            dropTitle = MakeLabel(BannerArt.Text("LOOT", "Butin"), 8.25f, false, BotUi.Muted); dropValue = MakeLabel(string.Empty, 9, true, BotUi.Olive);
            xpTitle = MakeLabel(BannerArt.Text("WORD_XP", "XP"), 8.25f, false, BotUi.Muted); xpValue = MakeLabel(string.Empty, 9, true, BotUi.Olive);
            dropValue.Name = "challenge-viewer-drop"; xpValue.Name = "challenge-viewer-xp";
            state = MakeLabel(string.Empty, 8.25f, false, BotUi.Ink); state.Name = "challenge-viewer-state";
            view = new ClientButton { Text = "Voir la cible", Width = ViewerWidth - 16, Height = 26, Font = BotFonts.Get(8.25f),
                Name = "challenge-viewer-target", AccessibleName = BannerArt.Text("VIEW_CHALENGE_TARGET", "Voir la cible du challenge") };
            view.Click += (s, e) => { if (Challenge != null) TargetRequested?.Invoke(Challenge); };
            tips.SetToolTip(view, BannerArt.Text("VIEW_CHALENGE_TARGET", "Voir la cible du challenge"));
            Controls.AddRange(new Control[] { title, close, icon, name, description, dropTitle, dropValue, xpTitle, xpValue, state, view });
        }

        public FightChallenge Challenge { get; private set; }
        /// <summary>« Voir la cible » cliqué : le panneau envoie <c>Gdi&lt;type&gt;</c> puis ferme la fenêtre, comme le client.</summary>
        public event Action<FightChallenge> TargetRequested;
        public event EventHandler CloseRequested;
        public Button ViewButton => view;
        public Button CloseButton => close;
        public string TitleText => title.Text;
        public string NameText => name.Text;
        public string DescriptionText => description.Text;
        public string DropText => dropValue.Text;
        public string XpText => xpValue.Text;
        public string StateText => state.Text;
        public string TooltipOf(Control control) => tips.GetToolTip(control) ?? string.Empty;

        private static Label MakeLabel(string text, float size, bool bold, Color color) =>
            new Label { Text = text, AutoSize = false, Font = BotFonts.Get(size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color, BackColor = Color.Transparent, UseMnemonic = false };

        /// <summary>Remplit la fenêtre (thread de l'interface) et recalcule sa hauteur d'après la description.</summary>
        public void Show(FightChallenge value)
        {
            if (value == null || IsDisposed) return;
            Challenge = value;
            icon.Show(value);
            name.Text = value.Name;
            description.Text = value.Description;
            dropValue.Text = "+" + ChallengeTexts.Number(value.DropBonus) + "%";
            xpValue.Text = "+" + ChallengeTexts.Number(value.XpBonus) + "%";
            string drop = ChallengeTexts.BonusDetail(value.BasicDropBonus, value.TeamDropBonus), xp = ChallengeTexts.BonusDetail(value.BasicXpBonus, value.TeamXpBonus);
            tips.SetToolTip(dropTitle, drop); tips.SetToolTip(dropValue, drop); tips.SetToolTip(xpTitle, xp); tips.SetToolTip(xpValue, xp);
            state.Text = BannerArt.Text("STATE", "Etat") + " : " + value.StateText;
            tips.SetToolTip(state, value.StateText);
            view.Visible = value.ShowTarget;
            int y = description.Text.Length == 0 ? 72 : description.Bottom + 8;
            dropTitle.SetBounds(8, y, 70, 18); dropValue.SetBounds(78, y, ViewerWidth - 86, 18); y += 20;
            xpTitle.SetBounds(8, y, 70, 18); xpValue.SetBounds(78, y, ViewerWidth - 86, 18); y += 22;
            state.SetBounds(8, y, ViewerWidth - 16, 18); y += 24;
            if (view.Visible) { view.Location = new Point(8, y); y += view.Height + 6; }
            Height = y + 4;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var rule = new Pen(BotUi.Gold)) e.Graphics.DrawRectangle(rule, 2, 22, Width - 5, Height - 25);
            using (var edge = new Pen(BotUi.Frame)) e.Graphics.DrawRectangle(edge, 0, 0, Width - 1, Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { tips.Dispose(); TargetRequested = null; CloseRequested = null; }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Défis du combat (<c>UI_FightChallenge</c> du client), en haut à gauche de la carte sous les options de combat : bouton
    /// « affiche / cache » (<c>_btnOpenClose</c>, infobulle <c>PARTY_OPEN_CLOSE</c> comme dans le client) puis une icône de 38 × 38 par
    /// défi, l'une sous l'autre tous les 44 pixels (<c>(6 + hauteur) × rang</c>). Visible tant qu'il y a des défis (<c>Gd</c>),
    /// vidé à la fin du combat. Un clic sur une icône ouvre le détail (<see cref="ChallengeViewer"/>) à droite de la colonne ;
    /// son bouton « voir la cible » envoie <c>Gdi&lt;type&gt;</c> (au plus une demande par seconde, <c>_lastShowAsk</c> du client)
    /// et le serveur répond par <c>Gf</c>, dessiné en drapeau sur la carte.
    /// </summary>
    public sealed class ChallengePanel : Panel
    {
        /// <summary>Haut de la première icône sous le bouton (<c>_btnOpenClose._y + 15</c>).</summary>
        public const int IconTop = 18;
        /// <summary>Écart vertical entre deux icônes (<c>6 + _height</c>).</summary>
        public const int IconPitch = 6 + ChallengeIcon.ColumnSize;
        private const int Edge = 3;
        private readonly List<ChallengeIcon> icons = new List<ChallengeIcon>();
        private readonly ToolTip tips = new ToolTip();
        private readonly ToggleBox toggle;
        private ChallengeViewer viewer;
        private Accounts account;
        private bool released = true;
        private DateTime lastTargetAsk = DateTime.MinValue;

        public ChallengePanel()
        {
            DoubleBuffered = true; BackColor = BotUi.Frame; Visible = false; TabStop = false; Name = "fight-challenges";
            AccessibleName = BannerArt.Text("CURRENT_FIGHT_CHALLENGE", "Challenge en cours");
            toggle = new ToggleBox { Location = new Point(Edge, Edge) };
            toggle.Click += (s, e) => Toggle();
            tips.SetToolTip(toggle, BannerArt.Text("PARTY_OPEN_CLOSE", "Affiche/Cache le groupe"));
            Controls.Add(toggle);
            Size = new Size(Edge * 2 + ChallengeIcon.ColumnSize, IconTop);
        }

        /// <summary>Message court pour le bandeau (réponse locale de « voir la cible »).</summary>
        public event Action<string> Feedback;
        public IReadOnlyList<ChallengeIcon> Icons => icons;
        /// <summary>Fenêtre de détail, créée au premier clic sur une icône ; null avant.</summary>
        public ChallengeViewer Viewer => viewer;
        public bool ViewerOpen => viewer != null && !viewer.IsDisposed && viewer.Visible;
        public Control ToggleButton => toggle;
        /// <summary>Icônes cachées par le bouton (<c>_btnOpenClose.selected</c>).</summary>
        public bool Collapsed { get; private set; }
        public string TooltipOf(Control control) => tips.GetToolTip(control) ?? string.Empty;

        public void Bind(Accounts value)
        {
            Release();
            account = value;
            var fight = account?.Game?.Fight;
            if (fight != null) { fight.CombatChanged += OnFightChanged; fight.ChallengeChanged += OnChallengeChanged; released = false; }
            RefreshFromFight();
        }

        public void Release()
        {
            if (released) return;
            released = true;
            var fight = account?.Game?.Fight;
            if (fight != null) { fight.CombatChanged -= OnFightChanged; fight.ChallengeChanged -= OnChallengeChanged; }
            account = null;
            CloseViewer();
        }

        private void OnFightChanged() => BotUi.OnUi(this, RefreshFromFight);
        private void OnChallengeChanged(FightChallenge challenge) => BotUi.OnUi(this, RefreshFromFight);

        /// <summary>Recopie les défis du combat : icônes, infobulles, détail ouvert, visibilité (thread de l'interface).</summary>
        public void RefreshFromFight()
        {
            if (IsDisposed || Disposing) return;
            var fight = account?.Game?.Fight;
            IReadOnlyList<FightChallenge> list = fight != null && fight.IsInFight ? fight.Challenges : (IReadOnlyList<FightChallenge>)new FightChallenge[0];
            SuspendLayout();
            try
            {
                while (icons.Count > list.Count) { ChallengeIcon last = icons[icons.Count - 1]; icons.RemoveAt(icons.Count - 1); Controls.Remove(last); last.Dispose(); }
                for (int index = 0; index < list.Count; index++)
                {
                    if (index >= icons.Count)
                    {
                        var created = new ChallengeIcon(ChallengeIcon.ColumnSize) { Location = new Point(Edge, IconTop + IconPitch * index), Cursor = Cursors.Hand };
                        created.Click += (s, e) => OpenViewer(((ChallengeIcon)s).Challenge);
                        icons.Add(created); Controls.Add(created);
                    }
                    icons[index].Show(list[index]);
                    icons[index].Visible = !Collapsed;
                    tips.SetToolTip(icons[index], ChallengeTexts.Tooltip(list[index]));
                }
            }
            finally { ResumeLayout(false); }
            if (list.Count == 0) { Visible = false; CloseViewer(); return; }
            Height = Collapsed ? IconTop : IconTop + IconPitch * list.Count - 6 + Edge;
            if (ViewerOpen)
            {
                FightChallenge shown = null;
                foreach (FightChallenge challenge in list) if (challenge.Id == viewer.Challenge?.Id) shown = challenge;
                if (shown == null) CloseViewer(); else viewer.Show(shown);
            }
            Visible = true;
            Place();
            Invalidate();
        }

        /// <summary>Bouton « affiche / cache » : montre ou cache les icônes, comme <c>FightChallenge.click</c>.</summary>
        public void Toggle()
        {
            Collapsed = !Collapsed;
            toggle.Collapsed = Collapsed;
            if (Collapsed) CloseViewer();
            RefreshFromFight();
        }

        /// <summary>Ouvre (ou remplace) le détail d'un défi à droite de la colonne, comme <c>FightChallengeIcon.onEventRelease</c>.</summary>
        public void OpenViewer(FightChallenge challenge)
        {
            if (challenge == null || IsDisposed) return;
            if (viewer == null || viewer.IsDisposed)
            {
                viewer = new ChallengeViewer();
                viewer.CloseRequested += (s, e) => CloseViewer();
                viewer.TargetRequested += OnTargetRequested;
            }
            viewer.Show(challenge);
            if (Parent != null && viewer.Parent != Parent) Parent.Controls.Add(viewer);
            PlaceViewer();
            viewer.Visible = true;
            viewer.BringToFront();
        }

        public void CloseViewer()
        {
            if (viewer != null && !viewer.IsDisposed) viewer.Visible = false;
        }

        /// <summary>Place la colonne en haut à gauche de la carte, sous les options de combat (<see cref="FightOptionButtons.Place"/> : 6, 48).</summary>
        public void Place()
        {
            if (Parent == null) return;
            Location = new Point(6, 92);
            PlaceViewer();
        }

        private void PlaceViewer()
        {
            if (viewer == null || viewer.IsDisposed || viewer.Parent == null) return;
            Size area = viewer.Parent.ClientSize;
            int x = Right + 4, y = Top;
            if (x + viewer.Width > area.Width) x = Math.Max(0, area.Width - viewer.Width);
            if (y + viewer.Height > area.Height) y = Math.Max(0, area.Height - viewer.Height);
            viewer.Location = new Point(x, y);
        }

        /// <summary>« Voir la cible » : <c>Gdi&lt;type&gt;</c>, au plus une fois par seconde ; la fenêtre se ferme comme dans le client.</summary>
        private async void OnTargetRequested(FightChallenge challenge)
        {
            var fight = account?.Game?.Fight;
            if (fight == null || challenge == null) { Feedback?.Invoke("Aucun combat en cours."); return; }
            DateTime now = DateTime.UtcNow;
            if ((now - lastTargetAsk).TotalMilliseconds < Fights.ChallengeTargetDelayMilliseconds) return;
            lastTargetAsk = now;
            CloseViewer();
            try
            {
                CombatActionResult result = await fight.ShowChallengeTargetAsync(challenge.Id);
                Feedback?.Invoke(result.Message);
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { Feedback?.Invoke("Envoi impossible : " + error.Message); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var rule = new Pen(BotUi.Gold)) e.Graphics.DrawRectangle(rule, 0, 0, Width - 1, Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Release();
                if (viewer != null) { viewer.TargetRequested -= OnTargetRequested; viewer.Dispose(); viewer = null; }
                tips.Dispose(); Feedback = null;
            }
            base.Dispose(disposing);
        }

        /// <summary>Petit bouton carré « − » / « + » (<c>_btnOpenClose</c>, 10 × 10 dans le client).</summary>
        private sealed class ToggleBox : Control
        {
            private bool collapsed;
            internal ToggleBox()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                Size = new Size(12, 12); Cursor = Cursors.Hand; TabStop = false; Name = "challenge-toggle"; AccessibleRole = AccessibleRole.CheckButton;
                AccessibleName = BannerArt.Text("PARTY_OPEN_CLOSE", "Affiche/Cache le groupe");
            }
            internal bool Collapsed { get => collapsed; set { collapsed = value; AccessibleDescription = value ? "fermé" : "ouvert"; Invalidate(); } }
            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics graphics = e.Graphics;
                using (var back = new SolidBrush(BotUi.FrameLight)) graphics.FillRectangle(back, ClientRectangle);
                using (var rule = new Pen(BotUi.Gold)) graphics.DrawRectangle(rule, 0, 0, Width - 1, Height - 1);
                using (var pen = new Pen(BotUi.PaperLight, 1.6f))
                {
                    float mid = Height / 2f;
                    graphics.DrawLine(pen, 3, mid, Width - 3, mid);
                    if (collapsed) graphics.DrawLine(pen, Width / 2f, 3, Width / 2f, Height - 3);
                }
            }
        }
    }
}
