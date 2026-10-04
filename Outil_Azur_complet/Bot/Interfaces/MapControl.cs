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
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.Maps.Mouvements;
using Tool_BotProtocol.Game.Perso;

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
        /// <summary>Routeur des clics de la carte (cellules, acteurs, menu contextuel).</summary>
        public InteractionRouter Router { get; }
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
            Router = new InteractionRouter(UserMap, () => Account, ActorsAt,
                () => SelectedSpellId.HasValue || Account.Game?.Fight?.IsInFight == true, DefaultCellActionAsync);
            Router.Feedback += message => ActionFeedback?.Invoke(message);
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
            Router?.Dispose();
            UserMap.DisplayStateChanged -= NotifyDisplayState;
            if (!subscribed) return;
            subscribed = false;
            UserMap.CellClicked -= UserMapClic;
            UserMap.SpellTargetReason = null;
            // La fenêtre libère le compte avant ses contrôles : la partie peut être déjà libérée (gestionnaires à null).
            var game = Account.Game;
            if (game == null) return;
            if (game.Map != null) { game.Map.RefreshMap -= MapChange; game.Map.RefreshEntities -= RefreshEntities; game.Map.EntityMovement -= EntityMovement; }
            if (game.character != null) game.character.MoveMinimapPathfinding -= GetPathfinding;
            if (game.Manager?.Mouvements != null) game.Manager.Mouvements.FinalizeMove -= MovementFinished;
            if (game.Fight != null) game.Fight.CombatChanged -= CombatChanged;
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
            if (cell == null || dragged || Router == null) return;
            // Le routeur ne lève jamais d'exception : clic gauche → cellule ou acteur, clic droit → menu de l'acteur.
            await Router.RouteAsync(cell.id, buttons, ModifierKeys & (Keys.Shift | Keys.Control));
        }

        /// <summary>Clic gauche sur une cellule, comme un clic de la souris (Maj et Ctrl facultatifs).</summary>
        public Task HandleCellActionAsync(short cellId, Keys modifiers = Keys.None) =>
            Router.RouteAsync(cellId, MouseButtons.Left, modifiers);

        /// <summary>
        /// Acteurs de la cellule dans l'ordre des menus : PNJ, groupes de monstres, joueurs, autres, puis le personnage
        /// du compte. Instantané : la carte peut changer sur le thread réseau pendant la lecture.
        /// </summary>
        public IReadOnlyList<Entites> ActorsAt(short cellId)
        {
            var game = Account.Game;
            var entities = game?.Map?.Entites;
            var actors = new List<Entites>();
            if (entities != null)
                actors.AddRange(entities.Values.Where(entity => entity?.Cell != null && entity.Cell.CellID == cellId)
                    .OrderBy(ActorOrder).ThenBy(entity => entity.id));
            CharacterClass self = game?.character;
            if (self?.Cell != null && self.Cell.CellID == cellId && !actors.Any(entity => entity.id == self.id)) actors.Add(self);
            return actors;
        }

        private static int ActorOrder(Entites entity)
        {
            switch (ActorClassifier.Of(entity))
            {
                case MenuActorKind.Npc: return 0;
                case MenuActorKind.MonsterGroup: return 1;
                case MenuActorKind.Player: return 2;
                default: return 3;
            }
        }

        /// <summary>
        /// Clic gauche sur une cellule : sort sélectionné, placement ou déplacement en combat, zaap
        /// (<c>GA500&lt;cellule&gt;;114</c> comme le client), sinon déplacement. Les PNJ passent par leur menu.
        /// </summary>
        private async Task DefaultCellActionAsync(short cellId)
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
            InteractionsClass interactions = Account.Game.Interactions;
            if (interactions.Zaap.IsZaapCell(cellId)) {
                InteractionResult zaap = await interactions.Zaap.OpenAsync(cellId);
                ActionFeedback?.Invoke(zaap.Message); Account.Logger.LogInfo("CARTE", zaap.Message); return;
            }
            MoveResults result = await Account.Game.Manager.Mouvements.MoveToAsync(destination);
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
