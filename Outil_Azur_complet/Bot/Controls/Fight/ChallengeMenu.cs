using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls.Banner;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Combats;

namespace Outil_Azur_complet.Bot.Controls.Fight
{
    /// <summary>
    /// Menu de placement du client (<c>UI_ChallengeMenu</c>) en bas de la carte : bande parchemin avec la pilule « Prêt »
    /// (<c>READY</c>, coche <c>_mcTick</c> une fois prêt ; clic → <c>GR1</c>, second clic → <c>GR0</c>) et la pilule « Annuler »
    /// (<c>CANCEL_SMALL</c>, <c>GQ</c> : quitter le combat avant son début), montrée seulement si le deuxième champ de <c>GJK</c>
    /// vaut 1 (StarLoco : défis). Affiché pendant le placement d'un combattant quand le troisième champ de <c>GJK</c> vaut 1,
    /// jamais à un spectateur. La barre d'état garde ses propres boutons « Prêt » et « Passer ».
    /// </summary>
    public sealed class ChallengeMenu : Panel
    {
        private readonly ClientButton ready, cancel;
        private readonly ToolTip tips = new ToolTip();
        private Bitmap tick, backdrop;
        private Accounts account;
        private bool released;

        public ChallengeMenu()
        {
            DoubleBuffered = true; BackColor = BotUi.Paper; Visible = false; TabStop = false; Name = "challenge-menu";
            AccessibleName = "Menu de placement"; Size = new Size(260, 48);
            ready = new ClientButton { Text = BannerArt.Text("READY", "Prêt"), Primary = true, Width = 110, Height = 32, Font = BotFonts.Get(9, FontStyle.Bold),
                Name = "challenge-ready", AccessibleName = "Prêt pour le combat", Location = new Point(14, 8), Tag = "client-icon" };
            ready.Click += async (s, e) => await ToggleReadyAsync();
            cancel = new ClientButton { Text = BannerArt.Text("CANCEL_SMALL", "Annuler"), Width = 110, Height = 32, Font = BotFonts.Get(9),
                Name = "challenge-cancel", AccessibleName = "Annuler le combat", Location = new Point(136, 8), Tag = "client-icon" };
            cancel.Click += async (s, e) => await CancelAsync();
            Controls.Add(ready); Controls.Add(cancel);
            BannerArt.RequestIcon(this, "UI_ChallengeMenu_coche", 20, image => { tick = image; RefreshFromFight(); });
            BannerArt.Request(this, "UI_ChallengeMenu_fond", image => { backdrop = image; Invalidate(); });
        }

        /// <summary>Message court pour le bandeau (réponse locale d'une demande).</summary>
        public event Action<string> Feedback;
        public Button ReadyButton => ready;
        public Button CancelButton => cancel;
        public string TooltipOf(Control control) => tips.GetToolTip(control) ?? string.Empty;

        public void Bind(Accounts value)
        {
            Release();
            account = value;
            var fight = account?.Game?.Fight;
            if (fight != null) { fight.CombatChanged += OnCombatChanged; released = false; }
            RefreshFromFight();
        }

        public void Release()
        {
            if (released) return;
            released = true;
            var fight = account?.Game?.Fight;
            if (fight != null) fight.CombatChanged -= OnCombatChanged;
            account = null;
        }

        private void OnCombatChanged() => BotUi.OnUi(this, RefreshFromFight);

        /// <summary>Recopie l'état du placement : visibilité, coche « prêt », bouton « Annuler » (thread de l'interface).</summary>
        public void RefreshFromFight()
        {
            if (IsDisposed || Disposing) return;
            var fight = account?.Game?.Fight;
            bool shown = fight != null && fight.IsPlacement && !fight.IsSpectator && fight.HasChallengeMenu;
            if (!shown) { Visible = false; return; }
            bool connected = account.Connexion != null && account.Connexion.IsConnected();
            ready.Glyph = fight.IsReady ? tick : null;
            ready.Text = fight.IsReady ? BannerArt.Text("CANCEL_SMALL", "Annuler") : BannerArt.Text("READY", "Prêt");
            ready.AccessibleName = fight.IsReady ? "Ne plus être prêt" : "Prêt pour le combat";
            ready.Enabled = connected && !fight.IsActionPending;
            tips.SetToolTip(ready, fight.IsReady ? "Annuler votre préparation (GR0)" : "Indiquer que vous êtes prêt (GR1)");
            cancel.Visible = fight.CanCancel;
            cancel.Enabled = connected;
            tips.SetToolTip(cancel, "Quitter ce combat avant son début (GQ)");
            Width = cancel.Visible ? 260 : 138;
            Visible = true;
            Place();
            Invalidate();
        }

        /// <summary>Place le menu en bas de son parent, centré (appelé au redimensionnement de la carte).</summary>
        public void Place()
        {
            if (Parent == null) return;
            Location = new Point(Math.Max(0, (Parent.ClientSize.Width - Width) / 2), Math.Max(0, Parent.ClientSize.Height - Height - 8));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle area = ClientRectangle;
            if (backdrop != null)
            {
                // Bande parchemin du client (UI_ChallengeMenu sans sa coche) : extrémités conservées, centre étiré.
                ClientAssets.DrawPill(e.Graphics, backdrop, area, 24);
                using (var veil = new SolidBrush(Color.FromArgb(150, BotUi.Paper))) e.Graphics.FillRectangle(veil, Rectangle.Inflate(area, -2, -2));
            }
            else
            {
                using (var paper = new SolidBrush(BotUi.Paper)) e.Graphics.FillRectangle(paper, Rectangle.Inflate(area, -1, -1));
            }
            using (var rule = new Pen(BotUi.Gold)) e.Graphics.DrawRectangle(rule, 1, 1, Width - 3, Height - 3);
            using (var edge = new Pen(BotUi.Frame)) e.Graphics.DrawRectangle(edge, 0, 0, Width - 1, Height - 1);
        }

        private async Task ToggleReadyAsync()
        {
            var fight = account?.Game?.Fight;
            if (fight == null) { Feedback?.Invoke("Aucun combat en cours."); return; }
            try
            {
                CombatActionResult result = await fight.SetReadyAsync(!fight.IsReady);
                Feedback?.Invoke(result.Message);
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { Feedback?.Invoke("Envoi impossible : " + error.Message); }
        }

        private async Task CancelAsync()
        {
            var fight = account?.Game?.Fight;
            if (fight == null || !fight.IsPlacement) { Feedback?.Invoke("Le placement est terminé."); return; }
            try
            {
                CombatActionResult result = await fight.GiveUpAsync();
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
