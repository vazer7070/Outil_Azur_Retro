using Outil_Azur_complet.Bot.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Managers.Mouvements;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Mouvements;
using Tool_BotProtocol.Game.NPC;

namespace Outil_Azur_complet.Bot.Interfaces
{
    public partial class MapControl : UserControl
    {
        private readonly Accounts Account;
        private bool subscribed;
        public event Action DisplayStateChanged;
        public event Action<short?> SpellSelectionChanged;
        public event Action<string> ActionFeedback;
        public short? SelectedSpellId { get; private set; }
        public int ZoomPercent => UserMap.ZoomPercent;
        public int MissingAssetCount => UserMap.MissingAssetCount;
        public string ArtworkStatus => UserMap.ArtworkStatus;
        public bool ShowGrid { get => UserMap.ShowGrid; set => UserMap.ShowGrid = value; }
        public bool ShowCellIds { get => UserMap.ShowCellId; set => UserMap.ShowCellId = value; }
        public void ZoomIn() => UserMap.ZoomIn();
        public void ZoomOut() => UserMap.ZoomOut();
        public void Fit() => UserMap.Fit();
        public MapControl(Accounts account)
        {
            InitializeComponent();
            Account = account ?? throw new ArgumentNullException(nameof(account));
            UserMap.SetAccount(Account);
            BackColor = Color.FromArgb(211, 204, 169);
            UserMap.BackColor = BackColor;
            UserMap.Dock = DockStyle.Fill;
            UserMap.DisplayStateChanged += NotifyDisplayState;
            iTalk_Label1.Visible = iTalk_Label2.Visible = false;
            // The owner thread creates handles before any network event can update the view.
            IntPtr ownerHandle = Handle;
            IntPtr mapHandle = UserMap.Handle;
            MapChange();
        }

        private void NotifyDisplayState() => DisplayStateChanged?.Invoke();

        private void MapControl_Load(object sender, EventArgs e)
        {
            if (!subscribed)
            {
                UserMap.CellClicked += UserMapClic;
                Account.Game.Map.RefreshMap += MapChange;
                Account.Game.Map.RefreshEntities += RefreshEntities;
                Account.Game.Map.EntityMovement += EntityMovement;
                Account.Game.character.MoveMinimapPathfinding += GetPathfinding;
                Account.Game.Manager.Mouvements.FinalizeMove += MovementFinished;
                Account.Game.Fight.CombatChanged += CombatChanged;
                subscribed = true;
            }
            MapChange();
        }

        private void Unsubscribe()
        {
            UserMap.DisplayStateChanged -= NotifyDisplayState;
            if (!subscribed) return;
            subscribed = false;
            UserMap.CellClicked -= UserMapClic;
            if (Account.Game == null) return;
            Account.Game.Map.RefreshMap -= MapChange;
            Account.Game.Map.RefreshEntities -= RefreshEntities;
            Account.Game.Map.EntityMovement -= EntityMovement;
            Account.Game.character.MoveMinimapPathfinding -= GetPathfinding;
            Account.Game.Manager.Mouvements.FinalizeMove -= MovementFinished;
            Account.Game.Fight.CombatChanged -= CombatChanged;
            UserMap.SpellTargetReason = null;
        }

        private void OnUi(Action action)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)(() => { if (!IsDisposed && !Disposing) action(); })); }
                catch (InvalidOperationException) { }
            }
            else action();
        }

        private void RefreshEntities() => OnUi(() => { RefreshSpellTargets(); UserMap.Invalidate(); });
        private void CombatChanged() => OnUi(RefreshSpellTargets);

        public void SelectSpell(short? id)
        {
            if (id.HasValue) {
                string reason = Account.Game.Fight.GetSpellUnavailableReason(id.Value);
                if (reason != null) { ActionFeedback?.Invoke(reason); return; }
            }
            SelectedSpellId = id;
            RefreshSpellTargets(); SpellSelectionChanged?.Invoke(SelectedSpellId);
            if (SelectedSpellId.HasValue) ActionFeedback?.Invoke("Sort sélectionné · cliquez sur une cible · Échap : annuler");
        }
        private void RefreshSpellTargets()
        {
            if (SelectedSpellId.HasValue && Account.Game.Fight.GetSpellUnavailableReason(SelectedSpellId.Value) != null) {
                SelectedSpellId = null; SpellSelectionChanged?.Invoke(null);
            }
            if (!SelectedSpellId.HasValue) { UserMap.SetSpellTargets(null); UserMap.SpellTargetReason = null; return; }
            short spell = SelectedSpellId.Value;
            var cells = Account.Game.Map.MapCells ?? new Cell[0];
            UserMap.SetSpellTargets(cells.Where(cell => cell != null && Account.Game.Fight.GetSpellUnavailableReason(spell, cell.CellID) == null).Select(cell => cell.CellID));
            UserMap.SpellTargetReason = cell => Account.Game.Fight.GetSpellUnavailableReason(spell, cell);
        }

        private void MapChange() => OnUi(() =>
        {
            Map map = Account.Game?.Map;
            if (map == null) return;
            SelectSpell(null);
            UserMap.W = map.MapWidth;
            UserMap.H = map.MapHeight;
            UserMap.SetCellNum();
            UserMap.DrawGrille();
            iTalk_Label2.Text = map.HasMapData
                ? "Carte " + map.MapID + "  " + map.GetCoordinates
                : map.LoadError ?? "En attente de la carte";
            UserMap.RefreshMap();
            DisplayStateChanged?.Invoke();
        });

        private async void UserMapClic(UserMapCell cell, MouseButtons buttons, bool dragged)
        {
            if (cell == null || dragged) return;
            try
            {
                if (buttons == MouseButtons.Left) await HandleCellActionAsync(cell.id);
                else if (buttons == MouseButtons.Right) await HandleNpcTradeAsync(cell.id);
            }
            catch (Exception ex) { ActionFeedback?.Invoke("Action impossible : " + ex.Message); }
        }

        /// <summary>
        /// Clic droit sur un PNJ : l'entrée « Échanger » du menu du client, soit <c>ER0|&lt;pnj&gt;</c>.
        /// StarLoco n'ouvre la boutique d'un PNJ que sur ce paquet (<c>ECK0|&lt;pnj&gt;</c> puis <c>EL</c>), jamais depuis une réponse de dialogue.
        /// </summary>
        public async Task HandleNpcTradeAsync(short cellId)
        {
            Map map = Account.Game?.Map;
            if (map == null || Account.Game.character.Cell == null) { ActionFeedback?.Invoke("Cette cellule n’est pas disponible."); return; }
            if (Account.Connexion == null || !Account.Connexion.IsConnected()) { ActionFeedback?.Invoke("Connectez le personnage pour agir sur la carte."); return; }
            PNJ npc = map.NPC_List().FirstOrDefault(entity => entity.Cell != null && entity.Cell.CellID == cellId);
            if (npc == null) { ActionFeedback?.Invoke("Clic droit : visez un personnage non joueur pour ouvrir sa boutique."); return; }
            InteractionResult trade = await Account.Game.Interactions.Shop.OpenAsync(npc.id);
            ActionFeedback?.Invoke(trade.Message); Account.Logger.LogInfo("CARTE", trade.Message);
        }

        public async Task HandleCellActionAsync(short cellId)
        {
            Map map = Account.Game?.Map;
            Cell destination = map?.GetCellFromId(cellId);
            if (destination == null || Account.Game.character.Cell == null) { ActionFeedback?.Invoke("Cette cellule n’est pas disponible."); return; }
            if (Account.Connexion == null || !Account.Connexion.IsConnected()) { ActionFeedback?.Invoke("Connectez le personnage pour agir sur la carte."); return; }
            var fight = Account.Game.Fight;
            if (SelectedSpellId.HasValue) {
                var cast = await fight.CastSpellAsync(SelectedSpellId.Value, cellId);
                if (cast.Sent) SelectSpell(null);
                ActionFeedback?.Invoke(cast.Message); return;
            }
            if (fight.IsInFight) {
                var combat = fight.IsPlacement ? await fight.PlaceAsync(cellId) : await fight.MoveAsync(cellId);
                ActionFeedback?.Invoke(combat.Message); return;
            }
            // Comme le client : un clic sur un PNJ envoie DC<pnj>, un clic sur le zaap envoie GA500<cellule>;114.
            InteractionsClass interactions = Account.Game.Interactions;
            PNJ npc = map.NPC_List().FirstOrDefault(entity => entity.Cell != null && entity.Cell.CellID == cellId);
            if (npc != null) {
                InteractionResult talk = await interactions.Npc.OpenAsync(npc.id);
                ActionFeedback?.Invoke(talk.Message); Account.Logger.LogInfo("CARTE", talk.Message); return;
            }
            if (interactions.Zaap.IsZaapCell(cellId)) {
                InteractionResult zaap = await interactions.Zaap.OpenAsync(cellId);
                ActionFeedback?.Invoke(zaap.Message); Account.Logger.LogInfo("CARTE", zaap.Message); return;
            }
            MoveResults result = Account.Game.Manager.Mouvements.GetCellsMove(destination, map.CellsOccuped());
            string message;
            switch(result) {
                case MoveResults.EXIT: message = "Déplacement vers la cellule " + cellId; break;
                case MoveResults.SAMECELL: message = "Vous êtes déjà sur cette cellule."; break;
                case MoveResults.CharacterBusyOrFull: message = "Déplacement indisponible : personnage occupé ou inventaire plein."; break;
                case MoveResults.CellNotWalkable: message = "Cette cellule n’est pas praticable."; break;
                case MoveResults.MONSTER: message = "Cette cellule est occupée par un groupe de monstres."; break;
                default: message = "Aucun chemin disponible vers cette cellule."; break;
            }
            ActionFeedback?.Invoke(message);
            Account.Logger.LogInfo("CARTE", message);
        }

        private void EntityMovement(int id, List<Cell> cells, int duration)
        {
            if (cells == null || cells.Count < 2) return;
            var snapshot = cells.ToList();
            OnUi(() => UserMap.AddAnimations(id, snapshot, duration, id == Account.Game.character.id ? AnimationType.PERSONNAGE : AnimationType.ENTITES));
        }
        private void MovementFinished(bool success)
        {
            if (!success) OnUi(() => UserMap.CancelAnimation(Account.Game.character.id));
        }

        private void GetPathfinding(List<Cell> cells)
        {
            if (cells == null || cells.Count == 0) return;
            var snapshot = cells.ToList();
            OnUi(() => UserMap.AddAnimations(Account.Game.character.id, snapshot,
                PathfinderUtils.GetTimeOnMap(snapshot[0], snapshot), AnimationType.PERSONNAGE));
        }
    }
}
