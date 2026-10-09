using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Alignement;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Liste des prismes (<c>dofus.aks.Subway</c> en mode prisme, titre <c>PRISM_LIST</c>) : s'ouvre sur <c>Wp</c> après
    /// <c>GA512&lt;id&gt;</c>, se ferme sur <c>Ww</c>. « Utiliser » envoie <c>Wp&lt;carte&gt;</c>, Fermer (ou ×/Échap) envoie <c>Ww</c>.
    /// Un prisme attaqué (<c>*</c>) est grisé ; un coût supérieur aux kamas est refusé (<c>NOT_ENOUGH_RICH</c>).
    /// </summary>
    public sealed class PrismTravelPanel : GamePanel
    {
        private ListView prismList;
        private Label prismStatus;
        private Control prismUse, prismLeave;
        private bool wasOpen;

        public override string Title => AlignmentTexts.Get("PRISM_LIST", "Liste des prismes");
        public override Image Icon => ClientAssets.Icon("icone-pvp", 24);
        public override bool IsModal => true;
        public override bool IsServerWindowOpen => Game?.Interactions?.Alignment?.Prism?.IsOpen == true;
        public ListView List => prismList;

        private static string NoticeText => AlignmentTexts.Get("PRISM_NOTICE", "Choisis le prisme vers lequel te téléporter.");

        protected override Control CreateView()
        {
            var page = Page();
            prismList = MakeList(9, AlignmentTexts.Get("PLACE", "Lieu"), AlignmentTexts.Get("COST", "Coût"));
            prismList.Columns[0].Width = 225; prismList.Columns[1].Width = 100; prismList.Name = "prism-list";
            prismList.SelectedIndexChanged += (s, e) => UpdateButtons();
            prismStatus = MakeStatus(NoticeText); prismStatus.Name = "prism-status";
            prismUse = MakeButton(AlignmentTexts.Get("USE_WORD", "Utiliser"), async (s, e) => await UseSelected(), true, 140); prismUse.Name = "prism-use";
            prismLeave = MakeButton(AlignmentTexts.Get("CLOSE", "Fermer"), async (s, e) => await Leave(), false, 100); prismLeave.Name = "prism-leave";
            page.Controls.Add(prismList); page.Controls.Add(prismStatus); page.Controls.Add(BotUi.Actions(prismUse, prismLeave));
            return page;
        }

        protected override void OnBind(GameClass game) { if (game.Interactions?.Alignment != null) game.Interactions.Alignment.Prism.Changed += OnServerChanged; }
        protected override void OnUnbind(GameClass game) { if (game.Interactions?.Alignment != null) game.Interactions.Alignment.Prism.Changed -= OnServerChanged; }

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
            if (Game == null || prismList == null) return;
            PrismTravel prism = Game.Interactions.Alignment?.Prism;
            if (prism == null) return;
            int selected = prismList.SelectedItems.Count == 0 ? -1 : (int)prismList.SelectedItems[0].Tag;
            prismList.BeginUpdate(); prismList.Items.Clear();
            foreach (PrismDestination destination in prism.Destinations)
            {
                var row = prismList.Items.Add(destination.Label + (destination.InFight ? " · en combat" : string.Empty) + (destination.IsCurrent ? " · ici" : string.Empty));
                row.SubItems.Add(destination.InFight ? "-" : destination.Cost + " kamas"); row.Tag = destination.MapId;
                if (destination.IsCurrent || destination.InFight) row.ForeColor = BotUi.Muted;
                if (destination.MapId == selected) row.Selected = true;
            }
            prismList.EndUpdate();
            prismStatus.Text = prism.IsOpen ? prism.Destinations.Count + " prisme(s) proposé(s). " + prism.LastMessage
                : NoticeText + (prism.LastMessage.Length > 0 ? " " + prism.LastMessage : string.Empty);
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            if (Game == null || prismUse == null) return;
            PrismTravel prism = Game.Interactions.Alignment?.Prism;
            if (prism == null) return;
            bool connected = Connected;
            PrismDestination selected = prismList.SelectedItems.Count == 1
                ? prism.Destinations.FirstOrDefault(entry => entry.MapId == (int)prismList.SelectedItems[0].Tag) : null;
            prismUse.Enabled = connected && prism.IsOpen && selected != null && !selected.IsCurrent && !selected.InFight && selected.Cost <= Game.character.Kamas;
            prismLeave.Enabled = connected && prism.IsOpen;
        }

        protected internal override bool OnUserClose()
        {
            if (!IsServerWindowOpen) return true;
            _ = Leave();
            return false;
        }

        private Task UseSelected()
        {
            if (prismList.SelectedItems.Count != 1) return Task.CompletedTask;
            int mapId = (int)prismList.SelectedItems[0].Tag;
            return ReportAsync(() => Game.Interactions.Alignment.Prism.TeleportAsync(mapId));
        }
        private Task Leave() => ReportAsync(() => Game.Interactions.Alignment.Prism.LeaveAsync());
    }
}
