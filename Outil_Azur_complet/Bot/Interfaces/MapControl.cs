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
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Managers.Mouvements;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;
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
        /// <summary>Qualité de rendu (option <c>DefaultQuality</c> du client : basse, moyenne, haute).</summary>
        public MapQuality Quality { get => UserMap.MapQ; set => UserMap.MapQ = value; }
        /// <summary>Option du client « voir tous les monstres du groupe » (vraie par défaut).</summary>
        public bool ViewAllMonsterInGroup { get => UserMap.ViewAllMonsterInGroup; set => UserMap.ViewAllMonsterInGroup = value; }
        /// <summary>Option du client « effets du chat » : bulles au-dessus des acteurs (vraie par défaut).</summary>
        public bool ChatEffects { get => UserMap.ChatEffects; set => UserMap.ChatEffects = value; }
        /// <summary>Surtête de tous les groupes de monstres (raccourci maintenu <c>SHOWMONSTERSTOOLTIP</c> du client).</summary>
        public bool ShowMonstersTooltip { get => UserMap.ShowMonstersTooltip; set => UserMap.ShowMonstersTooltip = value; }
        /// <summary>La souris entre sur un acteur de la carte (null quand elle le quitte).</summary>
        public event Action<Entites> ActorHovered;
        public void ZoomIn() => UserMap.ZoomIn();
        public void ZoomOut() => UserMap.ZoomOut();
        public void Fit() => UserMap.Fit();
        public MapControl(Accounts account) : this(account, null) { }

        /// <param name="clock">Horloge (ms) des déplacements, bulles, animations et effets de la carte, relayée à
        /// <see cref="UserMapControl.Clock"/> (tests) ; <c>null</c> : horloge interne.</param>
        public MapControl(Accounts account, Func<double> clock)
        {
            InitializeComponent();
            if (clock != null) UserMap.Clock = clock;
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
                UserMap.ActorClicked += UserMapActorClic;
                UserMap.ActorHovered += UserMapActorHovered;
                UserMap.CellHovered += PreviewPathTo;
                Account.Game.Map.RefreshMap += MapChange;
                Account.Game.Map.ActorAdded += ActorChanged;
                Account.Game.Map.ActorUpdated += ActorChanged;
                Account.Game.Map.ActorRemoved += ActorRemoved;
                Account.Game.Map.ActorsCleared += ActorsCleared;
                Account.Game.Chat.MessageReceived += OnChatMessage;
                Account.Game.Chat.SmileyReceived += OnSmiley;
                Account.Game.Chat.EmoteReceived += OnEmote;
                Account.Game.Map.RefreshEntities += RefreshEntities;
                Account.Game.Map.EntityMovement += EntityMovement;
                Account.Game.character.MoveMinimapPathfinding += GetPathfinding;
                Account.Game.Manager.Mouvements.FinalizeMove += MovementFinished;
                Account.Game.Fight.CombatChanged += CombatChanged;
                SubscribeVisuals();
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
            UserMap.ActorClicked -= UserMapActorClic;
            UserMap.ActorHovered -= UserMapActorHovered;
            UserMap.CellHovered -= PreviewPathTo;
            UserMap.SpellTargetReason = null;
            // La fenêtre libère le compte avant ses contrôles : la partie peut être déjà libérée (gestionnaires à null).
            var game = Account.Game;
            if (game == null) return;
            UnsubscribeVisuals(game);
            if (game.Map != null)
            {
                game.Map.RefreshMap -= MapChange; game.Map.RefreshEntities -= RefreshEntities; game.Map.EntityMovement -= EntityMovement;
                game.Map.ActorAdded -= ActorChanged; game.Map.ActorUpdated -= ActorChanged;
                game.Map.ActorRemoved -= ActorRemoved; game.Map.ActorsCleared -= ActorsCleared;
            }
            if (game.Chat != null) { game.Chat.MessageReceived -= OnChatMessage; game.Chat.SmileyReceived -= OnSmiley; game.Chat.EmoteReceived -= OnEmote; }
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
        private void CombatChanged() => OnUi(() => { RefreshSpellTargets(); DisarmFlagOutOfFight(); });

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
            DisarmFlagOutOfFight();
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
            Keys modifiers = ModifierKeys & (Keys.Shift | Keys.Control);
            // Le routeur ne lève jamais d'exception. Hors des sprites (test au pixel près de la vue), un clic gauche agit sur
            // la cellule comme dans le client ; le clic droit garde le menu des acteurs de la cellule.
            if (buttons == MouseButtons.Left) await Router.RequestMoveAsync(cell.id, modifiers);
            else await Router.RouteAsync(cell.id, buttons, modifiers);
        }

        private async void UserMapActorClic(Entites actor, short cellId, MouseButtons buttons, Keys modifiers)
        {
            if (actor == null || Router == null) return;
            await Router.RouteActorAsync(actor, cellId, buttons, modifiers);
        }

        private void UserMapActorHovered(Entites actor) => ActorHovered?.Invoke(actor);

        // ------------------------------------------------------------ événements du réseau (fil réseau → fil de l'interface)

        private void ActorChanged(MapActor actor) => UserMap.RequestRepaint();
        private void ActorRemoved(MapActor actor)
        {
            if (actor == null) return;
            long id = actor.Id;
            if (actor is FightSwordsActor) { UserMap.RequestRepaint(); return; }
            OnUi(() => UserMap.ForgetActor(id));
        }
        private void ActorsCleared() => OnUi(UserMap.ClearActorOverlays);

        /// <summary>Bulle d'un message du canal par défaut, hors combat lancé (le client n'en montre pas pendant un combat).</summary>
        private void OnChatMessage(ChatMessage message)
        {
            if (message == null || BubbleLayer.BubbleText(message, out BubbleKind ignored) == null) return;
            OnUi(() =>
            {
                var fight = Account.Game?.Fight;
                if (fight != null && fight.IsInFight && !fight.IsPlacement) return;
                UserMap.ShowChatMessage(message);
            });
        }
        private void OnSmiley(long actor, int smiley) => OnUi(() => UserMap.ShowSmiley(actor, smiley));
        private void OnEmote(long actor, int emote) => OnUi(() => UserMap.ShowEmote(actor, emote));

        // ------------------------------------------------------------ aperçu du chemin (A* sur le pool, dernier survol seulement)

        private short? previewPending;
        private bool previewRunning;

        private void PreviewPathTo(UserMapCell cell)
        {
            Map map = Account.Game?.Map;
            bool allowed = cell != null && map?.HasMapData == true && !SelectedSpellId.HasValue && Account.Game.Fight?.IsInFight != true
                && Account.Game.character?.Cell != null && !Account.IsMoving();
            if (!allowed) { previewPending = null; UserMap.SetPathPreview(null); return; }
            previewPending = cell.id;
            if (!previewRunning) StartPreview();
        }

        private void StartPreview()
        {
            if (!previewPending.HasValue) return;
            short target = previewPending.Value;
            Map map = Account.Game?.Map;
            Cell destination = map?.GetCellFromId(target);
            var mouvements = Account.Game?.Manager?.Mouvements;
            if (destination == null || mouvements == null) { UserMap.SetPathPreview(null); return; }
            previewRunning = true;
            Task.Run(() =>
            {
                try { return mouvements.PreviewPath(destination); }
                catch (Exception error) when (!(error is OutOfMemoryException))
                {
                    Account.Logger?.LogError("CARTE", "Aperçu du chemin impossible : " + error.Message);
                    return null;
                }
            }).ContinueWith(task =>
            {
                List<Cell> path = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
                OnUi(() =>
                {
                    previewRunning = false;
                    if (previewPending == target)
                        UserMap.SetPathPreview(path == null || !ReferenceEquals(map, Account.Game?.Map) ? null : path.Skip(1).Select(cell => cell.CellID));
                    else StartPreview();
                });
            }, TaskScheduler.Default);
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
            // Épées des combats (Gc+) : une par équipe, sur la cellule de chaque équipe.
            actors.AddRange(game?.Map?.FightSwords.Values.Where(swords => swords.Teams.Any(team => team.CellId == cellId)) ?? Enumerable.Empty<Entites>());
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

        /// <summary>
        /// Chemin reçu du serveur (fil réseau) : minutage du client pour cet acteur (groupe toujours au pas, PNJ et
        /// monstres en course au-delà de 6 cases…), calculé ici puis animé sur le fil de l'interface.
        /// </summary>
        private void EntityMovement(int id, List<Cell> cells, int duration)
        {
            if (cells == null || cells.Count < 2) return;
            var snapshot = cells.ToList();
            AnimDuration timing = AnimDuration.Compute(snapshot, UserMapControl.MovementProfile(Account, id));
            OnUi(() => UserMap.AddAnimations(id, snapshot, timing, id == Account.Game.character.id ? AnimationType.PERSONNAGE : AnimationType.ENTITES));
        }
        private void MovementFinished(bool success)
        {
            if (!success) OnUi(() => UserMap.CancelAnimation(Account.Game.character.id));
        }

        private void GetPathfinding(List<Cell> cells)
        {
            if (cells == null || cells.Count == 0) return;
            var snapshot = cells.ToList();
            int id = Account.Game.character.id;
            AnimDuration timing = AnimDuration.Compute(snapshot, UserMapControl.MovementProfile(Account, id));
            OnUi(() => UserMap.AddAnimations(id, snapshot, timing, AnimationType.PERSONNAGE));
        }
    }
}
