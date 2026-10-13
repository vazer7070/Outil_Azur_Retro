using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Interactions;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Destinations d'un zaapi (<c>dofus.aks.Subway</c>, interface <c>Subway</c>) : s'ouvre sur <c>Wc</c>, se ferme sur <c>Wv</c>.
    /// Comme le client, les destinations sont des repères (<c>hints</c>) rangés par catégorie et triés par nom.
    /// Se faire transporter envoie <c>Wu&lt;carte&gt;</c> ; Fermer (ou ×/Échap) envoie <c>Wv</c>.
    /// </summary>
    public sealed class ZaapiPanel : GamePanel
    {
        private const string NoZaapiText = "Cliquez sur un zaapi pour recevoir la liste des destinations.";
        private ListView zaapiList;
        private Label zaapiStatus;
        private Control zaapiUse, zaapiLeave;
        private bool wasOpen;

        public override string Title => "Zaapis";
        public override Image Icon => ClientAssets.Icon("zaap", 24);
        public override bool IsModal => true;
        public override bool IsServerWindowOpen => Game?.Interactions?.Zaapi?.IsOpen == true;

        protected override Control CreateView()
        {
            var page = Page();
            zaapiList = MakeList(9, "Destination", "Catégorie", "Coût");
            zaapiList.Columns[0].Width = 185; zaapiList.Columns[1].Width = 95; zaapiList.Columns[2].Width = 70;
            zaapiList.AccessibleName = "Destinations du zaapi";
            zaapiList.SelectedIndexChanged += (s, e) => UpdateButtons();
            zaapiList.DoubleClick += async (s, e) => await UseSelected();
            zaapiStatus = MakeStatus(NoZaapiText);
            zaapiUse = MakeButton("Se faire transporter", async (s, e) => await UseSelected(), true, 170);
            zaapiLeave = MakeButton("Fermer", async (s, e) => await LeaveZaapi(), false, 100);
            page.Controls.Add(zaapiList); page.Controls.Add(zaapiStatus); page.Controls.Add(BotUi.Actions(zaapiUse, zaapiLeave));
            return page;
        }

        protected override void OnBind(GameClass game) { wasOpen = false; game.Interactions.Zaapi.Changed += OnServerChanged; OnServerChanged(); }
        protected override void OnUnbind(GameClass game) { wasOpen = false; if (game.Interactions != null) game.Interactions.Zaapi.Changed -= OnServerChanged; }

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
            if (Game == null || zaapiList == null) return;
            ZaapiDialog zaapi = Game.Interactions.Zaapi;
            string selected = zaapiList.SelectedItems.Count == 0 ? null : (string)zaapiList.SelectedItems[0].Name;
            zaapiList.BeginUpdate(); zaapiList.Items.Clear(); zaapiList.Groups.Clear();
            foreach (var category in zaapi.VisibleDestinations.GroupBy(entry => entry.Category))
            {
                var group = zaapiList.Groups.Add(category.Key, category.Key);
                foreach (ZaapiDestination destination in category)
                {
                    var row = new ListViewItem(destination.Label + (destination.IsCurrent ? " · ici" : string.Empty), group)
                    { Tag = destination.MapId, Name = destination.MapId + "|" + destination.Name };
                    row.SubItems.Add(destination.Category); row.SubItems.Add(destination.Cost + " kamas");
                    if (destination.IsCurrent) row.ForeColor = BotUi.Muted;
                    zaapiList.Items.Add(row);
                    if (row.Name == selected) row.Selected = true;
                }
            }
            zaapiList.EndUpdate();
            zaapiStatus.Text = zaapi.IsOpen ? zaapi.VisibleDestinations.Count + " destination(s) proposée(s). " + zaapi.LastMessage
                : NoZaapiText + (zaapi.LastMessage.Length > 0 ? " " + zaapi.LastMessage : string.Empty);
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            if (Game == null || zaapiUse == null) return;
            ZaapiDialog zaapi = Game.Interactions.Zaapi;
            ZaapiDestination selected = zaapiList.SelectedItems.Count == 1
                ? zaapi.Destinations.FirstOrDefault(entry => entry.MapId == (int)zaapiList.SelectedItems[0].Tag) : null;
            zaapiUse.Enabled = Connected && zaapi.IsOpen && selected != null && !selected.IsCurrent && selected.Cost <= Game.character.Kamas;
            zaapiLeave.Enabled = Connected && zaapi.IsOpen;
        }

        protected internal override bool OnUserClose()
        {
            if (!IsServerWindowOpen) return true;
            _ = LeaveZaapi();
            return false;
        }

        private Task UseSelected()
        {
            if (zaapiList.SelectedItems.Count != 1 || !zaapiUse.Enabled) return Task.CompletedTask;
            int mapId = (int)zaapiList.SelectedItems[0].Tag;
            return ReportAsync(() => Game.Interactions.Zaapi.TeleportAsync(mapId));
        }

        private Task LeaveZaapi() => ReportAsync(() => Game.Interactions.Zaapi.LeaveAsync());
    }
}
