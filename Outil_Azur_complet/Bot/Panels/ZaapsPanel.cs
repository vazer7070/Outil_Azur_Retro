using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Interactions;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Destinations d'un zaap (<c>dofus.aks.Waypoints</c>) : s'ouvre sur <c>WC</c>, se ferme sur <c>WV</c>.
    /// Se téléporter envoie <c>WU&lt;carte&gt;</c>, Fermer (ou ×/Échap) envoie <c>WV</c>.
    /// </summary>
    public sealed class ZaapsPanel : GamePanel
    {
        private const string NoZaapText = "Cliquez sur le zaap de la carte pour recevoir la liste des destinations.";
        private ListView zaapList;
        private Label zaapStatus;
        private Control zaapTeleport, zaapLeave;
        private bool wasOpen;

        public override string Title => "Zaaps";
        public override Image Icon => ClientAssets.Icon("zaap", 24);
        public override bool IsModal => true;
        public override bool IsServerWindowOpen => Game?.Interactions?.Zaap?.IsOpen == true;

        protected override Control CreateView()
        {
            var page = Page();
            zaapList = MakeList(9, "Destination", "Coût");
            zaapList.Columns[0].Width = 225; zaapList.Columns[1].Width = 100;
            zaapList.SelectedIndexChanged += (s, e) => UpdateButtons();
            zaapStatus = MakeStatus(NoZaapText);
            zaapTeleport = MakeButton("Se téléporter", async (s, e) => await TeleportSelected(), true, 140);
            zaapLeave = MakeButton("Fermer", async (s, e) => await LeaveZaap(), false, 100);
            page.Controls.Add(zaapList); page.Controls.Add(zaapStatus); page.Controls.Add(BotUi.Actions(zaapTeleport, zaapLeave));
            return page;
        }

        protected override void OnBind(GameClass game) { game.Interactions.Zaap.Changed += OnServerChanged; }
        protected override void OnUnbind(GameClass game) { if (game.Interactions != null) game.Interactions.Zaap.Changed -= OnServerChanged; }

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
            if (Game == null || zaapList == null) return;
            ZaapDialog zaap = Game.Interactions.Zaap;
            int selected = zaapList.SelectedItems.Count == 0 ? -1 : (int)zaapList.SelectedItems[0].Tag;
            zaapList.BeginUpdate(); zaapList.Items.Clear();
            foreach (ZaapDestination destination in zaap.Destinations)
            {
                var row = zaapList.Items.Add(destination.Label + (destination.IsSaved ? " · sauvegarde" : string.Empty) + (destination.IsCurrent ? " · ici" : string.Empty));
                row.SubItems.Add(destination.Cost + " kamas"); row.Tag = destination.MapId;
                if (destination.IsCurrent) row.ForeColor = BotUi.Muted;
                if (destination.MapId == selected) row.Selected = true;
            }
            zaapList.EndUpdate();
            zaapStatus.Text = zaap.IsOpen ? zaap.Destinations.Count + " destination(s) proposée(s). " + zaap.LastMessage
                : NoZaapText + (zaap.LastMessage.Length > 0 ? " " + zaap.LastMessage : string.Empty);
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            if (Game == null || zaapTeleport == null) return;
            ZaapDialog zaap = Game.Interactions.Zaap;
            bool connected = Connected;
            ZaapDestination selected = zaapList.SelectedItems.Count == 1
                ? zaap.Destinations.FirstOrDefault(entry => entry.MapId == (int)zaapList.SelectedItems[0].Tag) : null;
            zaapTeleport.Enabled = connected && zaap.IsOpen && selected != null && !selected.IsCurrent && selected.Cost <= Game.character.Kamas;
            zaapLeave.Enabled = connected && zaap.IsOpen;
        }

        protected internal override bool OnUserClose()
        {
            if (!IsServerWindowOpen) return true;
            _ = LeaveZaap();
            return false;
        }

        private Task TeleportSelected()
        {
            if (zaapList.SelectedItems.Count != 1) return Task.CompletedTask;
            int mapId = (int)zaapList.SelectedItems[0].Tag;
            return ReportAsync(() => Game.Interactions.Zaap.TeleportAsync(mapId));
        }
        private Task LeaveZaap() => ReportAsync(() => Game.Interactions.Zaap.LeaveAsync());
    }
}
