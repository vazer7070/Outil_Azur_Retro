using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls.Banner;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Combats;

namespace Outil_Azur_complet.Bot.Controls.Fight
{
    /// <summary>
    /// Bouton d'option de combat du client (<c>UI_FightOption*Up</c> / <c>Down</c>) : image « Down » quand l'option est active
    /// ou le bouton enfoncé, image « Up » sinon ; sans image, une case de la palette avec une lettre.
    /// </summary>
    public sealed class FightOptionButton : Control
    {
        private Bitmap up, down;
        private bool pressed, hover, isChecked;

        public FightOptionButton(string symbol, string letter)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Symbol = symbol; Letter = letter; Size = new Size(30, 30); BackColor = BotUi.Frame; Cursor = Cursors.Hand; TabStop = false;
            AccessibleRole = AccessibleRole.CheckButton; Name = "fight-option-" + letter;
            if (!string.IsNullOrEmpty(symbol))
            {
                BannerArt.Request(this, symbol + "Up", image => { up = image; Invalidate(); });
                BannerArt.Request(this, symbol + "Down", image => { down = image ?? up; Invalidate(); });
            }
        }

        /// <summary>Nom du symbole du client, sans le suffixe <c>Up</c> / <c>Down</c> (<c>UI_FightOptionBlockJoiner</c>…).</summary>
        public string Symbol { get; }
        /// <summary>Lettre de l'option (<c>N</c>, <c>P</c>, <c>H</c>, <c>S</c>, <c>F</c> drapeau, <c>T</c> tactique).</summary>
        public string Letter { get; }
        /// <summary>Option active (<c>Go+</c>) : image enfoncée.</summary>
        public bool Checked { get => isChecked; set { if (isChecked == value) return; isChecked = value; AccessibleDescription = value ? "active" : "inactive"; Invalidate(); } }
        public bool HasClientImages => up != null;

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle area = Rectangle.Inflate(ClientRectangle, -1, -1);
            using (var fill = new SolidBrush(isChecked || pressed ? Color.FromArgb(255, 102, 0) : hover ? BotUi.FrameLight : BotUi.Frame)) graphics.FillRectangle(fill, area);
            using (var rule = new Pen(BotUi.Gold)) graphics.DrawRectangle(rule, area);
            Bitmap image = isChecked || pressed ? (down ?? up) : up;
            if (image != null) { ClientAssets.DrawFit(graphics, image, Rectangle.Inflate(area, -2, -2)); }
            else
            {
                using (var brush = new SolidBrush(isChecked || pressed ? Color.White : BotUi.PaperLight))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    graphics.DrawString(Letter, BotFonts.Get(10, FontStyle.Bold), brush, area, format);
            }
            if (!Enabled) using (var veil = new SolidBrush(Color.FromArgb(130, BotUi.Frame))) graphics.FillRectangle(veil, area);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        /// <summary>Clic simulé (tests, raccourcis).</summary>
        public void PerformClick() { if (Enabled && Visible) OnClick(EventArgs.Empty); }
    }

    /// <summary>
    /// Options d'équipe du combat (<c>FightOptionButtons</c> du client 1.34), en haut à gauche de la carte sous les coordonnées :
    /// interdire aux joueurs de rejoindre (<c>fN</c>), seulement au groupe (<c>fP</c>, présent si un groupe est actif), demander de
    /// l'aide (<c>fH</c>), interdire les spectateurs (<c>fS</c>), drapeau (signaler une cellule : <c>Gf</c>) et mode tactique
    /// (à venir). Pendant le placement tous sont proposés ; une fois le combat lancé (<c>GS</c>), seuls les spectateurs et le
    /// drapeau restent, comme dans le client. Rien n'est affiché à un spectateur. L'état « actif » suit <c>Go±</c> pour l'équipe
    /// du personnage (<see cref="Fights.OwnTeamOptions"/>). Le serveur n'accepte ces demandes que de l'initiateur : la réponse
    /// (ou son absence) fait foi, le bouton ne change pas d'état tout seul.
    /// </summary>
    public sealed class FightOptionButtons : Panel
    {
        private readonly ToolTip tips = new ToolTip();
        private readonly Dictionary<FightOptions, FightOptionButton> buttons = new Dictionary<FightOptions, FightOptionButton>();
        private readonly FlowLayoutPanel flow;
        private Accounts account;
        private bool released;

        public FightOptionButtons()
        {
            DoubleBuffered = true; BackColor = BotUi.Frame; Visible = false; TabStop = false; Name = "fight-options";
            AccessibleName = "Options du combat"; Padding = new Padding(3);
            flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, BackColor = BotUi.Frame, Margin = new Padding(0), Padding = new Padding(0) };
            Controls.Add(flow);
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Add(FightOptions.BlockJoiner, "UI_FightOptionBlockJoiner", "N", BannerArt.Text("FIGHT_OPTION_BLOCKJOINER", "Interdire aux joueurs de rejoindre le combat"));
            Add(FightOptions.PartyOnly, "UI_FightOptionBlockJoinerExceptPartyMember", "P", BannerArt.Text("FIGHT_OPTION_BLOCKJOINEREXCEPTPARTY", "Interdire aux joueurs de rejoindre le combat excepté ceux de mon groupe"));
            Add(FightOptions.NeedHelp, "UI_FightOptionNeedHelp", "H", BannerArt.Text("FIGHT_OPTION_HELP", "Demander de l'aide"));
            Add(FightOptions.BlockSpectators, "UI_FightOptionBlockSpectator", "S", BannerArt.Text("FIGHT_OPTION_SPECTATOR", "Interdire / Autoriser les spectateurs à rejoindre le combat"));
            FlagButton = new FightOptionButton("UI_FightOptionButtonCell", "F") { Margin = new Padding(1), AccessibleName = "Signaler une cellule" };
            FlagButton.Click += (s, e) => FlagRequested?.Invoke(this, EventArgs.Empty);
            tips.SetToolTip(FlagButton, "Signaler une cellule à votre équipe (Gf)");
            flow.Controls.Add(FlagButton);
            TacticButton = new FightOptionButton("UI_FightOptionTacticMode", "T") { Margin = new Padding(1), Enabled = false, AccessibleName = BannerArt.Text("TACTIC_MODE", "Mode Tactique") };
            tips.SetToolTip(TacticButton, BannerArt.Text("TACTIC_MODE", "Mode Tactique") + "\nÀ venir : la carte ne dessine pas encore le mode tactique.");
            flow.Controls.Add(TacticButton);
        }

        /// <summary>Message court pour le bandeau (réponse locale d'une demande).</summary>
        public event Action<string> Feedback;
        /// <summary>Le bouton drapeau est cliqué : la carte passe en mode « signaler une cellule ».</summary>
        public event EventHandler FlagRequested;
        public FightOptionButton FlagButton { get; }
        public FightOptionButton TacticButton { get; }
        /// <summary>Bouton d'une option (<c>fN</c>, <c>fP</c>, <c>fH</c>, <c>fS</c>), ou <c>null</c>.</summary>
        public FightOptionButton ButtonFor(FightOptions option) { FightOptionButton button; return buttons.TryGetValue(option, out button) ? button : null; }
        public IReadOnlyList<FightOptionButton> Buttons => flow.Controls.OfType<FightOptionButton>().ToArray();
        public string TooltipOf(Control control) => tips.GetToolTip(control) ?? string.Empty;

        private void Add(FightOptions option, string symbol, string letter, string tip)
        {
            var button = new FightOptionButton(symbol, letter) { Margin = new Padding(1), AccessibleName = tip };
            button.Click += async (s, e) => await ToggleAsync(option);
            tips.SetToolTip(button, tip);
            buttons[option] = button; flow.Controls.Add(button);
        }

        /// <summary>Associe les boutons à la session (thread de l'interface) ; <c>null</c> les détache.</summary>
        public void Bind(Accounts value)
        {
            Release();
            account = value;
            var fight = account?.Game?.Fight;
            if (fight != null) { fight.CombatChanged += OnCombatChanged; fight.FightOptionChanged += OnOptionChanged; released = false; }
            RefreshFromFight();
        }

        public void Release()
        {
            if (released) return;
            released = true;
            var fight = account?.Game?.Fight;
            if (fight != null) { fight.CombatChanged -= OnCombatChanged; fight.FightOptionChanged -= OnOptionChanged; }
            account = null;
        }

        private void OnCombatChanged() => BotUi.OnUi(this, RefreshFromFight);
        private void OnOptionChanged(long team, FightOptions options) => BotUi.OnUi(this, RefreshFromFight);

        /// <summary>Vrai quand le personnage appartient à un groupe actif (bouton <c>fP</c>, comme <c>Party.isActive</c> du client).</summary>
        private bool HasParty => account?.Game?.Interactions?.Party?.Group?.IsActive == true;

        /// <summary>Recopie l'état du combat : visibilité, boutons proposés, options actives (thread de l'interface).</summary>
        public void RefreshFromFight()
        {
            if (IsDisposed || Disposing) return;
            var fight = account?.Game?.Fight;
            bool shown = fight != null && fight.IsInFight && !fight.IsSpectator;
            if (!shown) { Visible = false; return; }
            bool placement = fight.IsPlacement;
            FightOptions active = fight.OwnTeamOptions;
            if (active == FightOptions.None)
            {
                // Hors initiateur, Go± ne nomme que l'équipe par son initiateur : s'il n'y a qu'une équipe connue, c'est la sienne ou l'adverse ;
                // le client ne distingue pas mieux (il lit teamId == playerId). Une seule équipe annoncée est affichée.
                var known = fight.TeamOptions;
                if (known.Count == 1) active = known.Values.First();
            }
            foreach (KeyValuePair<FightOptions, FightOptionButton> entry in buttons)
            {
                bool offered = entry.Key == FightOptions.BlockSpectators || placement;
                if (entry.Key == FightOptions.PartyOnly) offered = offered && HasParty;
                entry.Value.Visible = offered;
                entry.Value.Checked = (active & entry.Key) != 0;
            }
            FlagButton.Visible = true;
            TacticButton.Visible = !placement;
            Visible = true;
        }

        /// <summary>Place le panneau en haut à gauche de la carte, sous les coordonnées (<c>MapInfos</c>).</summary>
        public void Place() => Location = new Point(6, 48);

        private async Task ToggleAsync(FightOptions option)
        {
            var fight = account?.Game?.Fight;
            if (fight == null) { Feedback?.Invoke("Aucun combat en cours."); return; }
            try
            {
                CombatActionResult result = await fight.ToggleOptionAsync(option);
                Feedback?.Invoke(result.Message);
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { Feedback?.Invoke("Envoi impossible : " + error.Message); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { Release(); tips.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
