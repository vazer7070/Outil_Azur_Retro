using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.Maps.Mouvements;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Game.Perso.Inventory;
using Tool_BotProtocol.Game.Perso.Inventory.Enums;
using Tool_BotProtocol.Game.Perso.Spells;
using Tool_BotProtocol.Network;
using Tool_BotProtocol.Utils.Crypto;

namespace Tool_BotProtocol.Game.Combats
{
    public enum CombatPhase { None, Placement, Active, Finished }
    public sealed class CombatActionResult
    {
        public bool Sent { get; private set; }
        public string Message { get; private set; }
        internal CombatActionResult(bool sent, string message) { Sent = sent; Message = message; }
    }
    public sealed class CombatFighter
    {
        public int Id { get; internal set; }
        public string Name { get; internal set; }
        public short CellId { get; internal set; } = -1;
        public int Type { get; internal set; }
        public int Gfx { get; internal set; }
        public int Orientation { get; internal set; }
        public int Team { get; internal set; } = -1;
        public int Life { get; internal set; } = -1;
        public int MaximumLife { get; internal set; } = -1;
        public int ActionPoints { get; internal set; } = -1;
        public int MovementPoints { get; internal set; } = -1;
        public bool IsDead { get; internal set; }
        /// <summary>Invisible (<c>GA;150</c> avec un nombre de tours positif) ; sa cellule n'est plus transmise par <c>GTM</c>.</summary>
        public bool IsInvisible { get; internal set; }
        /// <summary>
        /// Prêt pendant le placement (<c>GR1&lt;id&gt;</c>, clip <c>ready.swf</c> du client au-dessus du sprite) ; remis à faux par
        /// <c>GR0&lt;id&gt;</c> et pour tous au premier <c>GTS</c> du combat (<c>Game.onTurnStart</c>, <c>isFirstTurn</c>).
        /// </summary>
        public bool IsReady { get; internal set; }
        /// <summary>Combattant porté par celui-ci (<c>GA;50</c>) ; 0 si aucun.</summary>
        public int CarryingId { get; internal set; }
        /// <summary>Porteur de ce combattant (<c>GA;50</c>) ; 0 si aucun.</summary>
        public int CarriedById { get; internal set; }
        internal int BaseActionPoints = -1;
        internal int BaseMovementPoints = -1;
        internal CombatFighter Copy() { return (CombatFighter)MemberwiseClone(); }
    }
    /// <summary>
    /// État d'un combat d'après les paquets du serveur (<c>GJK</c> … <c>GE</c>) et demandes manuelles du compte.
    /// Envoyer une demande ne dépense jamais de PA/PM localement : seuls les paquets du serveur changent les valeurs.
    /// Les actions <c>GA</c> reçues en combat passent par <see cref="FightActionTable"/> (voir <c>FightActions.cs</c>),
    /// puis par <see cref="GameActionRouter"/> pour celles qui n'appartiennent pas au combat. Les événements sont levés
    /// sur le fil réseau, après la mise à jour de l'état, jamais sous le verrou.
    /// </summary>
    public sealed partial class Fights : IDisposable
    {
        /// <summary>Nombre de lignes conservées dans <see cref="Journal"/>.</summary>
        public const int JournalCapacity = 200;
        /// <summary>
        /// Identifiant du corps à corps dans la barre de sorts (<c>CloseCombat.ID</c> du client, sort 0) : la case <c>²</c> et
        /// <c>SH0</c> le choisissent comme un sort, <see cref="GetSpellUnavailableReason"/> et <see cref="CastSpellAsync"/> le
        /// renvoient à <see cref="GetCloseCombatUnavailableReason"/> et <see cref="AttackAsync"/> tant qu'aucun sort 0 n'est appris.
        /// </summary>
        public const short CloseCombatSpellId = 0;
        private readonly Accounts.Accounts account;
        private readonly object sync = new object();
        private readonly Dictionary<int, CombatFighter> fighters = new Dictionary<int, CombatFighter>();
        private readonly Dictionary<short, int> lastSpellTurn = new Dictionary<short, int>();
        private readonly Dictionary<short, int> castsThisTurn = new Dictionary<short, int>();
        // Lancers confirmés de ce tour par sort et par combattant visé (clé sort << 32 | combattant), comme le compteur
        // « spriteOnID|sort » de SpellsManager.addLaunchedSpell ; vidé avec castsThisTurn (nextTurn du client).
        private readonly Dictionary<long, int> castsOnTarget = new Dictionary<long, int>();
        private readonly List<int> turnOrder = new List<int>();
        private readonly List<FightEffect> effects = new List<FightEffect>();
        private readonly Dictionary<int, HashSet<int>> states = new Dictionary<int, HashSet<int>>();
        private readonly Dictionary<long, FightZone> zones = new Dictionary<long, FightZone>();
        private readonly Dictionary<long, FightOptions> teamOptions = new Dictionary<long, FightOptions>();
        private readonly List<FightLogEntry> journal = new List<FightLogEntry>();
        private CancellationTokenSource cancellation = new CancellationTokenSource();
        private bool disposed;
        private bool tacticMode;
        private int generation;
        private string pendingKind;
        private bool pendingConfirmed;
        private DateTime pendingAt;
        private CombatPhase phase;
        private bool spectator, ready, canCancel, challengeMenu;
        private bool firstTurnPending;
        private int fightType = -1;
        private int actor, turn, turnDuration;
        private int lastActor, readyActor;
        private int? tableTurn;
        private DateTime turnStartedUtc = DateTime.MinValue;
        private int pa = -1, pm = -1;
        private short[] places = new short[0];
        private short[][] teamPlaces = { new short[0], new short[0] };
        private FightFlag lastFlag;
        private FightResult lastResult;
        private string lastMessage = "Aucun combat en cours.";
        internal Fights(Accounts.Accounts owner) { account = owner; }
        public CombatPhase Phase { get { lock (sync) return phase; } }
        public bool IsInFight { get { lock (sync) return InFight; } }
        public bool IsPlacement { get { lock (sync) return phase == CombatPhase.Placement; } }
        public bool IsSpectator { get { lock (sync) return spectator; } }
        public bool IsReady { get { lock (sync) return ready; } }
        /// <summary>Deuxième champ de <c>GJK</c> : le menu de placement du client montre son bouton « Annuler » (StarLoco : défis seulement).</summary>
        public bool CanCancel { get { lock (sync) return canCancel; } }
        /// <summary>Troisième champ de <c>GJK</c> : le client affiche le menu de placement (<c>ChallengeMenu</c>, prêt / annuler).</summary>
        public bool HasChallengeMenu { get { lock (sync) return challengeMenu; } }
        /// <summary>Sixième champ de <c>GJK</c> : type du combat (0 défi, 1 agression, 4 monstres, 5 percepteur chez StarLoco) ; -1 hors combat.</summary>
        public int FightType { get { lock (sync) return fightType; } }
        public bool IsMyTurn { get { lock (sync) return MyTurn; } }
        public bool IsActionPending { get { lock (sync) { ExpirePending(); return pendingKind != null; } } }
        public int CurrentActorId { get { lock (sync) return actor; } }
        public int TurnNumber { get { lock (sync) return turn; } }
        public int TurnDurationMilliseconds { get { lock (sync) return turnDuration; } }
        public int ActionPoints { get { lock (sync) return pa; } }
        public int MovementPoints { get { lock (sync) return pm; } }
        public string LastActionMessage { get { lock (sync) return lastMessage; } }
        public short[] PlacementCells { get { lock (sync) return (short[])places.Clone(); } }
        /// <summary>Cellules de placement de l'équipe 0 ou 1 (<c>GP</c>), que le client montre en rouge et en bleu ; vide hors placement.</summary>
        public short[] TeamPlacementCells(int team) { lock (sync) return team == 0 || team == 1 ? (short[])teamPlaces[team].Clone() : new short[0]; }
        public IReadOnlyDictionary<int, CombatFighter> Fighters
        { get { lock (sync) return fighters.ToDictionary(entry => entry.Key, entry => entry.Value.Copy()); } }
        /// <summary><see cref="CombatFighter.IsReady"/> du combattant ; faux s'il est inconnu.</summary>
        public bool IsFighterReady(int id) { lock (sync) { CombatFighter fighter; return fighters.TryGetValue(id, out fighter) && fighter.IsReady; } }
        /// <summary><see cref="CombatFighter.IsInvisible"/> du combattant ; faux s'il est inconnu.</summary>
        public bool IsFighterInvisible(int id) { lock (sync) { CombatFighter fighter; return fighters.TryGetValue(id, out fighter) && fighter.IsInvisible; } }
        /// <summary>Identifiants des combattants invisibles (<c>GA;150</c>), sans copie des combattants (lu à chaque image de la carte).</summary>
        public int[] InvisibleFighterIds { get { lock (sync) return fighters.Values.Where(fighter => fighter.IsInvisible).Select(fighter => fighter.Id).ToArray(); } }
        /// <summary>Ordre des tours (<c>GTL</c>), tour courant (<c>GTS</c>), dernier tour terminé (<c>GTF</c>) et dernier <c>GTR</c>.</summary>
        public FightTimeline Timeline
        { get { lock (sync) return new FightTimeline(turnOrder, actor, lastActor, readyActor, turnDuration, turnStartedUtc, tableTurn); } }
        public IReadOnlyList<int> TurnOrder { get { lock (sync) return turnOrder.ToArray(); } }
        /// <summary>Tous les effets en cours (<c>GIE</c> et bonus de caractéristiques reçus par <c>GA</c>), copiés.</summary>
        public IReadOnlyList<FightEffect> Effects { get { lock (sync) return effects.Select(effect => effect.Copy()).ToList(); } }
        /// <summary>Zones posées au sol (<c>GDZ</c>).</summary>
        public IReadOnlyList<FightZone> Zones { get { lock (sync) return zones.Values.ToList(); } }
        /// <summary>Options connues par équipe (<c>Go</c>), l'équipe étant identifiée par son initiateur.</summary>
        public IReadOnlyDictionary<long, FightOptions> TeamOptions { get { lock (sync) return new Dictionary<long, FightOptions>(teamOptions); } }
        /// <summary>Options de l'équipe dont le personnage du compte est l'initiateur (les seules qu'il peut changer chez StarLoco).</summary>
        public FightOptions OwnTeamOptions { get { lock (sync) return OptionsOf(account.Game.character.id); } }
        /// <summary>Dernière cellule signalée par un coéquipier (<c>Gf</c>) ; null si aucune depuis le début du combat.</summary>
        public FightFlag LastFlag { get { lock (sync) return lastFlag; } }
        /// <summary>Résultat du dernier combat (<c>GE</c>) ; conservé jusqu'au combat suivant ou à la réinitialisation du compte.</summary>
        public FightResult LastResult { get { lock (sync) return lastResult; } }
        /// <summary>
        /// Journal du combat en cours ou du dernier combat terminé, du plus ancien au plus récent (au plus <see cref="JournalCapacity"/> lignes) ;
        /// vidé au combat suivant ou à la réinitialisation du compte.
        /// </summary>
        public IReadOnlyList<FightLogEntry> Journal { get { lock (sync) return journal.ToArray(); } }
        public event Action CombatChanged;
        public event Action CombatReady;
        public event Action CombatFinished;
        /// <summary>Résultat lu dans <c>GE</c>, levé avant <see cref="CombatFinished"/>.</summary>
        public event Action<FightResult> CombatResultReceived;
        /// <summary>Cellule signalée par un combattant (<c>Gf</c>).</summary>
        public event Action<FightFlag> FlagReceived;
        /// <summary>Options d'une équipe modifiées (<c>Go</c>) : identifiant de l'équipe et nouvelles options.</summary>
        public event Action<long, FightOptions> FightOptionChanged;
        /// <summary>Nouvelle ligne du journal de combat.</summary>
        public event Action<FightLogEntry> JournalEntryAdded;
        /// <summary>Mode tactique activé ou coupé (<see cref="TacticMode"/>) ; levé sur le fil de l'appelant, hors du verrou.</summary>
        public event Action<bool> TacticModeChanged;

        /// <summary>
        /// Mode tactique du client (<c>Game._bTacticMode</c>, bouton <c>TACTIC_MODE</c> du combat) : purement local, aucun
        /// paquet n'est envoyé. Comme le client, le choix survit à la fin du combat et vaut pour les combats suivants ; la
        /// carte ne le dessine que pendant un combat (placement compris, spectateur aussi).
        /// </summary>
        public bool TacticMode
        {
            get { lock (sync) return tacticMode; }
            set
            {
                lock (sync) { if (tacticMode == value) return; tacticMode = value; }
                TacticModeChanged?.Invoke(value);
            }
        }
        /// <summary>
        /// Inverse <see cref="TacticMode"/> (bouton du combat) et rend la nouvelle valeur. La commande <c>/tactic</c> du client reste
        /// dans <c>ChatCommands.ClientOnly</c> : le bot la signale comme commande de l'interface du client.
        /// </summary>
        public bool ToggleTacticMode()
        {
            bool value;
            lock (sync) value = tacticMode = !tacticMode;
            TacticModeChanged?.Invoke(value);
            return value;
        }
        private bool InFight => phase == CombatPhase.Placement || phase == CombatPhase.Active;
        private bool MyTurn => phase == CombatPhase.Active && !spectator && actor == account.Game.character.id;
        private void Changed() { CombatChanged?.Invoke(); }
        private CombatFighter GetFighter(int id)
        {
            CombatFighter value;
            if (!fighters.TryGetValue(id, out value)) fighters[id] = value = new CombatFighter { Id = id, Name = "Combattant #" + id };
            return value;
        }
        public IReadOnlyList<FightEffect> GetEffects(int fighterId)
        { lock (sync) return effects.Where(effect => effect.TargetId == fighterId).Select(effect => effect.Copy()).ToList(); }
        /// <summary>États actifs d'un combattant (<c>GA;950</c>), triés.</summary>
        public int[] GetStates(int fighterId)
        { lock (sync) { HashSet<int> set; return states.TryGetValue(fighterId, out set) ? set.OrderBy(state => state).ToArray() : new int[0]; } }
        public bool HasState(int fighterId, int state)
        { lock (sync) { HashSet<int> set; return states.TryGetValue(fighterId, out set) && set.Contains(state); } }
        public FightOptions GetTeamOptions(long teamId) { lock (sync) return OptionsOf(teamId); }
        private FightOptions OptionsOf(long teamId) { FightOptions value; return teamOptions.TryGetValue(teamId, out value) ? value : FightOptions.None; }
        private void ExpirePending()
        {
            if (pendingKind != null && DateTime.UtcNow - pendingAt > TimeSpan.FromSeconds(8))
            { pendingKind = null; lastMessage = "Aucune confirmation reçue : vous pouvez réessayer."; }
        }
        private string Disconnected()
        {
            if (disposed || account.isdisposed || account.Connexion == null || !account.Connexion.IsConnected()) return "Le serveur est déconnecté.";
            return InFight ? null : "Aucun combat en cours.";
        }
        private string ActionUnavailable(bool placement)
        {
            ExpirePending();
            string reason = Disconnected();
            if (reason != null) return reason;
            if (spectator) return "Les spectateurs ne peuvent pas agir.";
            if (placement ? phase != CombatPhase.Placement : !MyTurn) return placement ? "Le placement est terminé." : "Attendez votre tour.";
            CombatFighter self;
            if (fighters.TryGetValue(account.Game.character.id, out self) && self.IsDead) return "Votre personnage est hors combat.";
            if (pendingKind != null) return "Attendez la confirmation de l’action précédente.";
            return null;
        }
        public string GetSpellUnavailableReason(short spellId, short? targetCell = null)
        {
            if (IsCloseCombat(spellId)) return GetCloseCombatUnavailableReason(targetCell);
            lock (sync) return SpellUnavailable(spellId, targetCell);
        }
        private bool IsCloseCombat(short spellId) => spellId == CloseCombatSpellId && account.Game?.character?.Spells?.ContainsKey(spellId) != true;

        /// <summary>
        /// Bonus de portée du personnage, comme le client (<c>getModeratorValue(19) + Player.RangeModerator</c>) : portée de la
        /// fiche <c>As</c> sans les boosts (base, équipement, dons) plus les effets de caractéristique <c>GA</c> portés par le
        /// personnage dont l'effet de <c>effects.xml</c> vise la caractéristique 19, signés par son opérateur (117 « + », 116 « - »).
        /// </summary>
        public int RangeBonus { get { lock (sync) return RangeBonusLocked(); } }
        /// <summary>Caractéristique « portée » des effets (<c>c</c> de <c>effects.xml</c>, <c>CharacteristicsManager</c> du client).</summary>
        public const int RangeCharacteristic = 19;
        private int RangeBonusLocked()
        {
            int bonus = account.Game?.character?.stats?.Atteignable?.Displayed ?? 0;
            int self = account.Game?.character?.id ?? 0;
            foreach (FightEffect effect in effects)
            {
                if (effect.Source != FightEffectSource.GameAction || effect.TargetId != self || !effect.Param1.HasValue) continue;
                if (Data.LangData.Effect.Characteristic(effect.EffectId) != RangeCharacteristic) continue;
                bonus += Data.LangData.Effect.Operator(effect.EffectId) == "-" ? -effect.Param1.Value : effect.Param1.Value;
            }
            return bonus;
        }

        /// <summary>
        /// Visée du sort (ou du corps à corps, sort 0) : portée effective, cellules de portée et zones d'effet à dessiner ; null si
        /// le sort n'est pas appris ou si ses données manquent (le serveur décide alors seul).
        /// </summary>
        public SpellAim GetSpellAim(short spellId)
        {
            if (IsCloseCombat(spellId)) return SpellAim.ForCloseCombat(CloseCombat);
            Spell spell;
            if (account.Game?.character?.Spells == null || !account.Game.character.Spells.TryGetValue(spellId, out spell)) return null;
            SpellStats stats = spell.GetStats();
            if (stats == null) return null;
            int bonus;
            lock (sync) bonus = RangeBonusLocked();
            return SpellAim.For(spellId, stats, bonus);
        }
        private string SpellUnavailable(short spellId, short? targetCell)
        {
            string reason = ActionUnavailable(false);
            if (reason != null) return reason;
            Spell spell;
            if (!account.Game.character.Spells.TryGetValue(spellId, out spell)) return "Ce sort n’est pas appris.";
            SpellStats stats = spell.GetStats();
            // États du lanceur (requiredStates / forbiddenStates du niveau, checkCanLaunchSpellReturnObject du client).
            if (stats != null)
            {
                int self = account.Game.character.id;
                foreach (int state in stats.ForbiddenStates ?? new int[0])
                    if (HasStateLocked(self, state)) return "Ce sort ne se lance pas dans l’état « " + StateName(state) + " ».";
                foreach (int state in stats.RequiredStates ?? new int[0])
                    if (!HasStateLocked(self, state)) return "Ce sort demande l’état « " + StateName(state) + " ».";
            }
            if (stats != null && pa >= 0 && stats.PA > pa) return "PA insuffisants (" + stats.PA + " requis).";
            int count, previous;
            if (stats != null && stats.PerTurn > 0 && castsThisTurn.TryGetValue(spellId, out count) && count >= stats.PerTurn)
                return "Nombre de lancers de ce sort atteint pour ce tour.";
            if (stats != null && stats.Interval > 0 && lastSpellTurn.TryGetValue(spellId, out previous) && previous + stats.Interval > turn)
                return "Ce sort doit encore attendre " + (previous + stats.Interval - turn) + " tour(s).";
            if (!targetCell.HasValue) return null;
            Cell target = account.Game.Map.GetCellFromId(targetCell.Value), origin = account.Game.character.Cell;
            if (target == null || !target.IsActive) return "Cette cellule ne fait pas partie du terrain de combat.";
            if (origin == null) return "La position de votre personnage n’est pas encore connue.";
            if (stats == null) return null;
            // Même visée que le dessin de la portée (SpellAim) : checkRange du client, bonus des PO compris.
            SpellAim aim = SpellAim.For(spellId, stats, RangeBonusLocked());
            int distance = origin.GetDistanceBetweenCells(target);
            if (distance < aim.MinRange || distance > aim.MaxRange) return "Cible hors portée (" + aim.MinRange + "–" + aim.MaxRange + ").";
            if (aim.LineOnly && !origin.AreCellsOnline(target)) return "Ce sort se lance en ligne.";
            CombatFighter occupant = OccupantLocked(target.CellID);
            // freeCell du client : ni combattant, ni case où l'on ne marche pas (mouvement ≤ 1).
            if (stats.EmptyCell && (occupant != null || (int)target.C_Types <= 1)) return "Ce sort nécessite une cellule vide.";
            // Bonus d'objets de classe (SB, SpellsBoostsManager du client) : 289 retire la ligne de vue, 291 ajoute des lancers par cible.
            if (stats.AvecLigneDeVue && SpellBoost(spellId, NoLineOfSightBoost) <= 0
                && !LineOfSight.Check(account.Game.Map, origin.CellID, target.CellID, OccupantsLocked))
                return "Cible hors de la ligne de vue.";
            int perTarget = stats.PerObjective + Math.Max(0, SpellBoost(spellId, PerTargetBoost)), launched;
            if (perTarget > 0 && occupant != null && castsOnTarget.TryGetValue(TargetKey(spellId, occupant.Id), out launched) && launched >= perTarget)
                return "Nombre de lancers de ce sort sur cette cible atteint pour ce tour.";
            // Les autres bonus d'objets de classe et la limite d'invocations restent décidés par le serveur.
            return null;
        }

        // Effets SB du client (ACTION_BOOST_SPELL_NOLINEOFSIGHT, ACTION_BOOST_SPELL_MAXPERTARGET).
        private const int NoLineOfSightBoost = 289, PerTargetBoost = 291;

        /// <summary>Bonus <c>SB</c> d'un objet de classe sur le sort (<c>getSpellModificator</c>) ; -1 sans bonus, comme le client.</summary>
        private int SpellBoost(short spellId, int effect)
        {
            SpellBook book = account.Game?.character?.SpellBook;
            if (book == null) return -1;
            foreach (KeyValuePair<int, int> boost in book.ModificatorsOf(spellId))
                if (boost.Key == effect) return boost.Value;
            return -1;
        }

        /// <summary>
        /// Cible visible depuis la cellule du personnage (<see cref="LineOfSight"/>, <c>checkView</c> du client) ; faux hors de
        /// la carte ou si la position du personnage est inconnue. Ne tient pas compte de la portée.
        /// </summary>
        public bool IsInLineOfSight(short targetCell)
        {
            Cell origin = account.Game?.character?.Cell;
            Map map = account.Game?.Map;
            if (origin == null || map == null) return false;
            lock (sync) return LineOfSight.Check(map, origin.CellID, targetCell, OccupantsLocked);
        }

        /// <summary>Lancers confirmés de ce tour du sort sur ce combattant (compteur par cible du client).</summary>
        public int GetSpellCastsOnTarget(short spellId, int fighterId)
        { lock (sync) { int count; return castsOnTarget.TryGetValue(TargetKey(spellId, fighterId), out count) ? count : 0; } }

        private static long TargetKey(short spellId, int fighterId) => ((long)spellId << 32) | (uint)fighterId;
        /// <summary>Compte un lancer confirmé sur ce combattant (sous le verrou, <c>GA;300</c> du personnage).</summary>
        private void CountCastOnTarget(short spellId, int fighterId)
        { long key = TargetKey(spellId, fighterId); int count; castsOnTarget.TryGetValue(key, out count); castsOnTarget[key] = count + 1; }

        /// <summary>Combattant vivant posé sur la cellule (le porteur plutôt que le porté, <c>spriteOnID</c> du client) ; null sinon.</summary>
        private CombatFighter OccupantLocked(int cellId)
        {
            CombatFighter found = null;
            foreach (CombatFighter fighter in fighters.Values)
            {
                if (fighter.IsDead || fighter.CellId < 0 || fighter.CellId != cellId) continue;
                if (fighter.CarriedById == 0) return fighter;
                if (found == null) found = fighter;
            }
            return found;
        }

        /// <summary>0 : cellule libre ; 1 : un combattant ; 2 : un porteur avec son porté (hauteurs de <c>checkView</c>).</summary>
        private int OccupantsLocked(int cellId)
        {
            CombatFighter fighter = OccupantLocked(cellId);
            if (fighter == null) return 0;
            return fighter.CarryingId != 0 ? 2 : 1;
        }
        public Task<CombatActionResult> CastSpellAsync(short spellId, short targetCell)
        {
            if (IsCloseCombat(spellId)) return AttackAsync(targetCell);
            return RequestAsync("GA300" + spellId + ";" + targetCell, "sort", () => SpellUnavailable(spellId, targetCell));
        }

        /// <summary>
        /// Corps à corps actuel : arme en position 1, sinon poings de la classe (<see cref="CloseCombatStats"/>). Lu à chaque
        /// appel, l'arme pouvant changer entre deux tours (<c>OM</c>).
        /// </summary>
        public CloseCombatStats CloseCombat
        {
            get
            {
                CharacterClass character = account.Game?.character;
                InventoryObjects weapon = character?.Inventory?.GetObjetsPosition(InventorySlots.WEAPON);
                return CloseCombatStats.For(character?.Race_ID ?? 0, weapon?.ID ?? 0, weapon?.Name);
            }
        }
        public string GetCloseCombatUnavailableReason(short? targetCell = null)
        {
            CloseCombatStats stats = CloseCombat;
            lock (sync) return CloseCombatUnavailable(stats, targetCell);
        }
        /// <summary>
        /// Vérifications du client avant <c>GA303</c> (<c>checkCanLaunchSpellReturnObject</c> du sort 0, puis cellules à portée) :
        /// tour, états requis et interdits de la classe, PA, cellule du terrain, portée de l'arme ou des poings, ligne.
        /// La ligne de vue, la zone et les effets restent décidés par le serveur.
        /// </summary>
        private string CloseCombatUnavailable(CloseCombatStats stats, short? targetCell)
        {
            string reason = ActionUnavailable(false);
            if (reason != null) return reason;
            int self = account.Game.character.id;
            foreach (int state in stats.RequiredStates)
                if (!HasStateLocked(self, state)) return "Corps à corps impossible hors de l’état « " + StateName(state) + " ».";
            foreach (int state in stats.ForbiddenStates)
                if (HasStateLocked(self, state)) return "Corps à corps impossible dans l’état « " + StateName(state) + " ».";
            if (stats.HasData && pa >= 0 && stats.ActionPoints > pa) return "PA insuffisants (" + stats.ActionPoints + " requis).";
            if (!targetCell.HasValue) return null;
            Cell target = account.Game.Map.GetCellFromId(targetCell.Value), origin = account.Game.character.Cell;
            if (target == null || !target.IsActive) return "Cette cellule ne fait pas partie du terrain de combat.";
            if (origin == null) return "La position de votre personnage n’est pas encore connue.";
            if (!stats.HasData) return null;
            int distance = origin.GetDistanceBetweenCells(target);
            if (distance < stats.MinRange || distance > stats.MaxRange)
                return stats.MinRange <= 1 && stats.MaxRange == 1 ? "La cible doit être adjacente à votre personnage."
                    : "Cible hors portée (" + stats.MinRange + "–" + stats.MaxRange + ").";
            if (stats.LineOnly && !origin.AreCellsOnline(target)) return "Cette arme frappe en ligne.";
            return null;
        }
        private bool HasStateLocked(int fighterId, int state) { HashSet<int> set; return states.TryGetValue(fighterId, out set) && set.Contains(state); }
        private static string StateName(int state)
        {
            IReadOnlyDictionary<string, string> data = Data.LangData.Raw("states", "ST", state.ToString(CultureInfo.InvariantCulture));
            string name;
            return data != null && data.TryGetValue("n", out name) && !string.IsNullOrEmpty(name) ? name : "n° " + state.ToString(CultureInfo.InvariantCulture);
        }
        /// <summary>
        /// <c>GA303&lt;cellule&gt;</c> : attaque au corps à corps, comme le client (<c>sendActions(303, [cellule])</c> au relâchement
        /// d'une cellule en mode <c>cc</c>). StarLoco répond <c>GAS</c>, puis avec une arme <c>GA;303</c> (ou <c>GA;305</c>,
        /// échec critique qui finit le tour), les effets, <c>GA;102</c> et <c>GAF</c> ; à mains nues il lance le sort 0
        /// (<c>GA;300</c>…) et ne libère le lancer qu'avec <c>GA;102;id;id,-0</c> une seconde plus tard, comme un sort.
        /// <c>GKK</c> est renvoyé sur <c>GAF</c> par <c>MapFrame</c>, comme pour <see cref="CastSpellAsync"/>.
        /// </summary>
        public Task<CombatActionResult> AttackAsync(short targetCell)
        {
            CloseCombatStats stats = CloseCombat;
            return RequestAsync("GA303" + targetCell.ToString(CultureInfo.InvariantCulture), stats.IsArmed ? "corps à corps" : "poings",
                () => CloseCombatUnavailable(stats, targetCell));
        }
        /// <summary>Attentes libérées par <c>GA;102;id;id,-0</c> (verrou de lancer de StarLoco) plutôt que par <c>GAF</c>.</summary>
        internal static bool WaitsForCastingUnlock(string kind) { return kind == "sort" || kind == "poings"; }
        public Task<CombatActionResult> SetReadyAsync(bool value)
        { return RequestAsync("GR" + (value ? "1" : "0"), "prêt", () => ActionUnavailable(true)); }
        public Task<CombatActionResult> PlaceAsync(short targetCell)
        { return RequestAsync("Gp" + targetCell, "placement", () => ActionUnavailable(true) ?? (places.Contains(targetCell) ? null : "Choisissez une cellule de placement de votre équipe.")); }
        public Task<CombatActionResult> PassTurnAsync()
        { return RequestAsync("Gt", "tour", () => ActionUnavailable(false)); }

        /// <summary>
        /// <c>GQ</c> (<c>Game.leave</c> du client) : en placement le personnage quitte le combat (ou l'annule s'il l'a lancé),
        /// en combat actif StarLoco le compte comme mort (abandon, matrice §2 n° 22), en spectateur il cesse d'observer.
        /// Aucune action n'est mise en attente : la réponse attendue est <c>GV</c>.
        /// </summary>
        public Task<CombatActionResult> GiveUpAsync()
        {
            return SendDirectAsync("GQ", Disconnected, () => spectator ? "Demande de sortie du mode spectateur envoyée."
                : phase == CombatPhase.Placement ? "Demande de sortie du combat envoyée."
                : "Abandon envoyé : le serveur compte le personnage comme mort.");
        }
        public string GetGiveUpUnavailableReason() { lock (sync) return Disconnected(); }

        /// <summary><c>GQ&lt;id&gt;</c> : exclut un coéquipier pendant le placement (StarLoco ne l'accepte que de l'initiateur du combat).</summary>
        public Task<CombatActionResult> KickAsync(int fighterId)
        { return SendDirectAsync("GQ" + fighterId, () => KickUnavailable(fighterId), () => "Demande d’exclusion envoyée."); }
        public string GetKickUnavailableReason(int fighterId) { lock (sync) return KickUnavailable(fighterId); }
        private string KickUnavailable(int fighterId)
        {
            string reason = Disconnected();
            if (reason != null) return reason;
            if (spectator) return "Les spectateurs ne peuvent pas agir.";
            if (phase != CombatPhase.Placement) return "L’exclusion n’est possible que pendant le placement.";
            if (fighterId == account.Game.character.id) return "Utilisez « Abandonner » pour quitter le combat.";
            if (fighterId <= 0) return "Seul un joueur peut être exclu.";
            CombatFighter target, self;
            if (!fighters.TryGetValue(fighterId, out target)) return "Ce combattant est inconnu.";
            if (fighters.TryGetValue(account.Game.character.id, out self) && self.Team >= 0 && target.Team >= 0 && self.Team != target.Team)
                return "Ce combattant n’est pas dans votre équipe.";
            return null;
        }

        /// <summary>
        /// Bascule une option de l'équipe comme les boutons <c>FightOptionButtons</c> du client : <c>fN</c>, <c>fS</c>, <c>fP</c>, <c>fH</c>.
        /// Le serveur répond par <c>Go±&lt;lettre&gt;&lt;équipe&gt;</c> si le personnage est l'initiateur. Comme dans le client, seule
        /// l'option spectateurs reste disponible une fois le combat commencé.
        /// </summary>
        public Task<CombatActionResult> ToggleOptionAsync(FightOptions option)
        {
            string packet = FightEffectPackets.RequestFor(option);
            return SendDirectAsync(packet ?? "f", () => OptionUnavailable(option), () => "Option de combat demandée.");
        }
        public string GetOptionUnavailableReason(FightOptions option) { lock (sync) return OptionUnavailable(option); }
        private string OptionUnavailable(FightOptions option)
        {
            if (FightEffectPackets.RequestFor(option) == null) return "Choisissez une seule option de combat.";
            string reason = Disconnected();
            if (reason != null) return reason;
            if (spectator) return "Les spectateurs ne peuvent pas agir.";
            if (option != FightOptions.BlockSpectators && phase != CombatPhase.Placement) return "Cette option ne se change que pendant le placement.";
            return null;
        }

        /// <summary><c>Gf&lt;cellule&gt;</c> (<c>Game.setFlag</c>) : signale une cellule aux coéquipiers, qui reçoivent <c>Gf&lt;id&gt;|&lt;cellule&gt;</c>.</summary>
        public Task<CombatActionResult> SetFlagAsync(short cellId)
        { return SendDirectAsync("Gf" + cellId, () => FlagUnavailable(cellId), () => "Cellule signalée à l’équipe."); }
        public string GetFlagUnavailableReason(short cellId) { lock (sync) return FlagUnavailable(cellId); }
        private string FlagUnavailable(short cellId)
        {
            string reason = Disconnected();
            if (reason != null) return reason;
            if (spectator) return "Les spectateurs ne peuvent pas agir.";
            return account.Game.Map.GetCellFromId(cellId) == null ? "Cette cellule n’existe pas sur la carte." : null;
        }

        /// <summary>
        /// <c>GA001&lt;chemin&gt;</c> : déplacement en combat le long du chemin que le client calcule et sélectionne au survol
        /// (<see cref="PreviewMovePath"/>). La recherche tourne sur le pool de threads, hors du verrou ; l'envoi est refusé si
        /// le tour, la position, les PM ou les cellules occupées ont changé entre-temps.
        /// </summary>
        public async Task<CombatActionResult> MoveAsync(short targetCell)
        {
            CombatMoveSnapshot snapshot; string reason;
            lock (sync) reason = TakeMoveSnapshot(targetCell, out snapshot);
            if (reason != null) return new CombatActionResult(false, reason);
            List<Cell> path = await Task.Run(() => SearchMovePath(snapshot)).ConfigureAwait(false);
            if (path == null) return new CombatActionResult(false, "Aucun chemin libre vers cette cellule.");
            string packet = "GA001" + PathfinderUtils.GetCleanRoad(path);
            return await RequestAsync(packet, "déplacement", () => ActionUnavailable(false) ?? MoveSnapshotChanged(snapshot, path)).ConfigureAwait(false);
        }

        /// <summary>
        /// Chemin (départ compris) que suivrait <see cref="MoveAsync"/> vers <paramref name="targetCell"/>, sans rien envoyer :
        /// celui que le client sélectionne en <c>CELL_PATH_OVER_COLOR</c> pendant son tour (<c>InteractionsManager.calculatePath</c>
        /// en combat : cible à au plus PM cellules, ni occupée ni infranchissable, A* en quatre directions de longueur au plus PM).
        /// Null hors de son tour, pendant une action, ou sans chemin. La recherche s'exécute sur le fil appelant, hors du verrou :
        /// depuis l'interface, l'appeler sur le pool de threads.
        /// </summary>
        public List<Cell> PreviewMovePath(short targetCell)
        {
            CombatMoveSnapshot snapshot;
            lock (sync) if (TakeMoveSnapshot(targetCell, out snapshot) != null) return null;
            return SearchMovePath(snapshot);
        }

        private sealed class CombatMoveSnapshot
        {
            public Cell[] Cells; public int Width; public Cell Origin; public Cell Destination; public HashSet<int> Occupied;
            public int MovementPoints; public int Generation;
        }

        private string TakeMoveSnapshot(short targetCell, out CombatMoveSnapshot snapshot)
        {
            snapshot = null;
            string reason = ActionUnavailable(false);
            if (reason != null) return reason;
            Map map = account.Game.Map;
            Cell destination = map.GetCellFromId(targetCell), origin = account.Game.character.Cell;
            if (origin == null || destination == null || !destination.IsWalkable() || map.MapCells == null) return "Cette cellule n’est pas accessible.";
            if (origin == destination) return "Votre personnage est déjà sur cette cellule.";
            int self = account.Game.character.id;
            var occupied = new HashSet<int>(fighters.Values.Where(f => !f.IsDead && f.CellId >= 0 && f.Id != self).Select(f => (int)f.CellId));
            if (occupied.Contains(destination.CellID)) return "Cette cellule est occupée par un combattant.";
            // checkRange(lanceur, cible, false, 0, PM, 0) du client avant toute recherche.
            if (pm >= 0 && origin.GetDistanceBetweenCells(destination) > pm) return "PM insuffisants pour ce trajet.";
            snapshot = new CombatMoveSnapshot
            {
                Cells = map.MapCells, Width = map.MapWidth, Origin = origin, Destination = destination, Occupied = occupied,
                MovementPoints = pm, Generation = generation
            };
            return null;
        }

        // pathFind du client en combat : quatre directions, longueur au plus PM, combattants infranchissables (cible comprise).
        // Un chemin qui n'atteint pas la cible (tronqué avant un obstacle) n'est ni montré ni envoyé.
        private static List<Cell> SearchMovePath(CombatMoveSnapshot snapshot)
        {
            List<Cell> path = Pathfinder.FindPath(snapshot.Cells, snapshot.Width, snapshot.Origin, snapshot.Destination, new PathRequest
            {
                AllDirections = false, InFight = true, SpriteCells = snapshot.Occupied,
                MaxLength = snapshot.MovementPoints >= 0 ? snapshot.MovementPoints : 500
            });
            return path != null && path.Count >= 2 && ReferenceEquals(path[path.Count - 1], snapshot.Destination) ? path : null;
        }

        private string MoveSnapshotChanged(CombatMoveSnapshot snapshot, List<Cell> path)
        {
            if (generation != snapshot.Generation || pm != snapshot.MovementPoints || account.Game.character.Cell != snapshot.Origin
                || !ReferenceEquals(account.Game.Map.MapCells, snapshot.Cells)) return "La situation a changé pendant le calcul du chemin : réessayez.";
            int self = account.Game.character.id;
            var crossed = new HashSet<int>(path.Skip(1).Select(cell => (int)cell.CellID));
            if (fighters.Values.Any(f => !f.IsDead && f.Id != self && crossed.Contains(f.CellId))) return "Un combattant occupe désormais le chemin : réessayez.";
            return null;
        }
        private Task<CombatActionResult> RequestAsync(string packet, string kind, Func<string> validate)
        {
            int version; TcpClient connection;
            lock (sync)
            {
                string reason = validate();
                if (reason != null) return Task.FromResult(new CombatActionResult(false, reason));
                StartPending(kind); version = generation; connection = account.Connexion;
            }
            Changed(); return SendActionAsync(connection, packet, version);
        }
        /// <summary>Envoi sans action en attente (abandon, exclusion, options, drapeau) : le serveur peut ne pas répondre.</summary>
        private async Task<CombatActionResult> SendDirectAsync(string packet, Func<string> validate, Func<string> success)
        {
            int version; TcpClient connection; string message;
            lock (sync)
            {
                string reason = validate();
                if (reason != null) return new CombatActionResult(false, reason);
                version = generation; connection = account.Connexion; message = success();
            }
            try
            {
                await connection.SendPacket(packet).ConfigureAwait(false);
                lock (sync)
                {
                    if (disposed || version != generation || !ReferenceEquals(connection, account.Connexion) || !connection.IsConnected())
                        return new CombatActionResult(false, "La connexion a été interrompue.");
                    lastMessage = message;
                }
                Changed(); return new CombatActionResult(true, message);
            }
            catch (Exception error) { return new CombatActionResult(false, "Envoi impossible : " + error.Message); }
        }
        private void StartPending(string kind)
        { pendingKind = kind; pendingAt = DateTime.UtcNow; pendingConfirmed = false; lastMessage = "Action envoyée, en attente du serveur."; }
        private async Task<CombatActionResult> SendActionAsync(TcpClient connection, string packet, int version)
        {
            try
            {
                lock (sync) { if (disposed || version != generation || !ReferenceEquals(connection, account.Connexion)) return new CombatActionResult(false, "Le combat ou la connexion a changé."); }
                await connection.SendPacket(packet).ConfigureAwait(false);
                lock (sync) { if (disposed || version != generation || !ReferenceEquals(connection, account.Connexion) || !connection.IsConnected()) return new CombatActionResult(false, "La connexion a été interrompue."); }
                return new CombatActionResult(true, "Action envoyée au serveur.");
            }
            catch (Exception error)
            {
                lock (sync) { if (!disposed && version == generation) { pendingKind = null; lastMessage = "Envoi impossible : " + error.Message; } }
                Changed(); return new CombatActionResult(false, "Envoi impossible : " + error.Message);
            }
        }
        public int GetSpellCooldownRemaining(short spellId)
        {
            lock (sync)
            {
                Spell spell; int previous;
                if (!account.Game.character.Spells.TryGetValue(spellId, out spell) || spell.GetStats() == null || !lastSpellTurn.TryGetValue(spellId, out previous)) return 0;
                return Math.Max(0, previous + spell.GetStats().Interval - turn);
            }
        }
        public int GetSpellCastsThisTurn(short spellId) { lock (sync) { int count; return castsThisTurn.TryGetValue(spellId, out count) ? count : 0; } }
        internal void Join(string payload)
        {
            string[] fields = payload.Split('|'); int state, viewing;
            if (fields.Length < 4 || !int.TryParse(fields[0], out state) || state < 1 || state > 3 || !int.TryParse(fields[3], out viewing)) return;
            account.Game.Manager.Mouvements.CancelForMapChange(); Clear(false);
            // Game.onJoin appelle cleanMap(1) : les acteurs de la carte (PNJ, joueurs, groupes, épées, objets au sol) disparaissent
            // et seuls les combattants reviennent par les GM qui suivent ; la carte complète revient avec GDM après GE puis GC1.
            // Les états GDF de la carte restent, comme les cellules GDC non permanentes (Map.ClearForFight).
            account.Game.PersoInWorld.Clear();
            account.Game.Map.ClearForFight();
            account.Game.Map.GetEntitiesRefreshEvent();
            int cancel, menu, type;
            lock (sync)
            {
                if (disposed) return;
                phase = state >= 3 ? CombatPhase.Active : CombatPhase.Placement; spectator = viewing == 1;
                // GJK<état>|<annuler>|<menu>|<spectateur>|<durée>|<type> (Game.onJoin) : champs lus tels quels, absents = valeurs du client.
                canCancel = fields.Length > 1 && int.TryParse(fields[1], out cancel) && cancel == 1;
                challengeMenu = fields.Length > 2 && int.TryParse(fields[2], out menu) && menu == 1;
                fightType = fields.Length > 5 && int.TryParse(fields[5], out type) ? type : -1;
                lastMessage = spectator ? "Combat observé en spectateur." : "Choisissez votre position puis indiquez que vous êtes prêt.";
            }
            account.AccountStates = AccountStates.FIGHTING; Changed(); CombatReady?.Invoke();
        }
        internal void SetPlaces(string payload)
        {
            string[] fields = payload.Split('|'); int team;
            if (fields.Length < 3 || !int.TryParse(fields[2], out team) || team < 0 || team > 1) return;
            short[] own = DecodePlaces(fields[team]);
            if (own == null) return;
            short[] other = DecodePlaces(fields[1 - team]) ?? new short[0];
            lock (sync) { if (!InFight) return; places = own; teamPlaces = team == 0 ? new[] { own, other } : new[] { other, own }; } Changed();
        }
        private static short[] DecodePlaces(string encoded)
        {
            if (encoded.Length % 2 != 0 || encoded.Any(c => !Hash.caracteres_array.Contains(c))) return null;
            var cells = new List<short>();
            for (int i = 0; i < encoded.Length; i += 2) cells.Add(Hash.Get_Cell_From_Hash(encoded.Substring(i, 2)));
            return cells.Distinct().ToArray();
        }
        /// <summary>
        /// <c>GR1&lt;id&gt;</c> / <c>GR0&lt;id&gt;</c> : <c>Game.onReady</c> du client pose ou retire le clip <c>ready.swf</c> au-dessus du
        /// sprite de n'importe quel combattant (<see cref="CombatFighter.IsReady"/>) ; pour le compte, l'état du bouton « Prêt ».
        /// </summary>
        internal void SetReady(string payload)
        {
            int id;
            if (payload.Length < 2 || (payload[0] != '0' && payload[0] != '1') || !int.TryParse(payload.Substring(1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out id)) return;
            bool value = payload[0] == '1';
            lock (sync)
            {
                if (!InFight) return;
                CombatFighter fighter;
                if (fighters.TryGetValue(id, out fighter)) fighter.IsReady = value;
                if (id == account.Game.character.id)
                {
                    ready = value; if (pendingKind == "prêt") pendingKind = null;
                    lastMessage = ready ? "Vous êtes prêt. Le serveur démarrera le combat." : "Vous pouvez choisir votre position.";
                }
                else if (fighter == null) return;
            }
            Changed();
        }
        internal void Start()
        { lock (sync) { if (!InFight) return; phase = CombatPhase.Active; firstTurnPending = true; places = new short[0]; teamPlaces = new[] { new short[0], new short[0] }; pendingKind = null; lastMessage = "Combat commencé. Attendez votre tour."; } Changed(); }

        /// <summary><c>GTL|id|id…</c> : ordre de jeu des combattants vivants (début du combat, invocations, morts).</summary>
        internal void SetTurnList(string payload)
        {
            List<int> order;
            if (!FightTurnPackets.TryParseTurnList(payload, out order)) { Malformed("GTL", payload); return; }
            lock (sync) { if (!InFight) return; turnOrder.Clear(); turnOrder.AddRange(order); }
            Changed();
        }

        /// <summary><c>GTR&lt;id&gt;</c> : le client répond toujours <c>GT</c> (<c>Game.turnOk</c>) ; StarLoco l'ignore (matrice §2 n° 39).</summary>
        internal async Task TurnReadyAsync(TcpClient client, string payload)
        {
            int id;
            if (!FightTurnPackets.TryParseActor(payload, out id)) { Malformed("GTR", payload); return; }
            lock (sync) { if (!InFight || disposed) return; readyActor = id; }
            Changed();
            if (client != null && ReferenceEquals(client, account.Connexion) && client.IsConnected()) await client.SendPacket("GT").ConfigureAwait(false);
        }

        internal void StartTurn(string payload)
        {
            int id, duration; int? table;
            if (!FightTurnPackets.TryParseTurnStart(payload, out id, out duration, out table)) { Malformed("GTS", payload); return; }
            lock (sync)
            {
                if (!InFight) return;
                phase = CombatPhase.Active; actor = id; turnDuration = duration; tableTurn = table; turnStartedUtc = DateTime.UtcNow; pendingKind = null;
                // Premier tour : Game.onTurnStart retire le clip du dessus (« prêt ») de tous les sprites.
                if (firstTurnPending) { firstTurnPending = false; foreach (CombatFighter fighter in fighters.Values) fighter.IsReady = false; }
                // Game.onTurnStart → GameManager.cleanPlayer(lastPlayerID) : les effets du combattant précédent perdent un tour.
                if (lastActor != 0 && lastActor != id) ExpireEffects(lastActor);
                if (id == account.Game.character.id && !spectator)
                {
                    turn++; castsThisTurn.Clear(); castsOnTarget.Clear(); CombatFighter self = GetFighter(id);
                    pa = self.ActionPoints = self.BaseActionPoints; pm = self.MovementPoints = self.BaseMovementPoints;
                    lastMessage = "Votre tour : choisissez un sort ou déplacez votre personnage.";
                }
                else lastMessage = "Tour de " + GetFighter(id).Name + ".";
            }
            Changed();
        }
        internal void EndTurn(string payload)
        {
            int id; if (!FightTurnPackets.TryParseActor(payload, out id)) { Malformed("GTF", payload); return; }
            lock (sync)
            {
                if (!InFight || id != actor) return;
                lastActor = actor; actor = 0; turnDuration = 0; turnStartedUtc = DateTime.MinValue; pendingKind = null; lastMessage = "Tour terminé. Attendez le suivant.";
            }
            Changed();
        }
        private void ExpireEffects(int fighterId)
        {
            for (int index = effects.Count - 1; index >= 0; index--)
            {
                FightEffect effect = effects[index];
                // Une durée négative (-1) est un effet sans fin : il n'est retiré que par GA;132, la mort ou la fin du combat.
                if (effect.TargetId != fighterId || effect.RemainingTurns < 0) continue;
                effect.RemainingTurns--;
                if (effect.RemainingTurns <= 0) effects.RemoveAt(index);
            }
        }

        /// <summary>
        /// <c>GIE…</c> : effet affiché sur chaque cible (icônes <c>Buff</c>). Comme <c>EffectsManager.addEffect</c>, un effet de même
        /// numéro et de même durée s'additionne au précédent ; posé pendant le tour de sa cible, il gagne un tour (<c>Game.onEffect</c>).
        /// </summary>
        internal void ApplyEffectPacket(string payload)
        {
            List<FightEffect> received = FightEffectPackets.ParseEffect(payload);
            if (received.Count == 0) { Malformed("GIE", payload); return; }
            lock (sync)
            {
                if (!InFight) return;
                foreach (FightEffect effect in received)
                {
                    if (effect.TargetId == actor && effect.RemainingTurns >= 0) effect.RemainingTurns++;
                    AddEffect(effect);
                }
            }
            Changed();
        }
        private void AddEffect(FightEffect effect)
        {
            FightEffect same = effects.FirstOrDefault(existing => existing.Source == effect.Source && existing.TargetId == effect.TargetId
                && existing.EffectId == effect.EffectId && existing.RemainingTurns == effect.RemainingTurns);
            if (same == null) { effects.Add(effect); return; }
            if (same.Param1.HasValue || effect.Param1.HasValue) same.Param1 = (same.Param1 ?? 0) + (effect.Param1 ?? 0);
        }
        /// <summary><c>GIe</c> (<c>Game.onClearAllEffect</c>) : retire tous les effets de tous les combattants.</summary>
        internal void ClearAllEffects()
        { lock (sync) { if (!InFight) return; effects.Clear(); } Changed(); }

        /// <summary><c>GDZ±&lt;cellule&gt;;&lt;taille&gt;;&lt;couleur&gt;</c> : zone dessinée (<c>+</c>) ou effacée (<c>-</c>).</summary>
        internal void ApplyZones(string payload)
        {
            List<FightZoneChange> changes = FightEffectPackets.ParseZones(payload);
            if (changes.Count == 0) { Malformed("GDZ", payload); return; }
            lock (sync)
            {
                if (!InFight) return;
                foreach (FightZoneChange change in changes)
                {
                    long key = ((long)change.Zone.CellId << 32) | (uint)change.Zone.Color;
                    if (change.Visible) zones[key] = change.Zone; else zones.Remove(key);
                }
            }
            Changed();
        }

        /// <summary><c>Go±&lt;A|S|P|H&gt;&lt;équipe&gt;</c> : envoyé à toute la carte, y compris hors combat pour les épées d'autres combats.</summary>
        internal void ApplyFightOption(string payload)
        {
            bool enabled; FightOptions option; long team; FightOptions current;
            if (!FightEffectPackets.TryParseOption(payload, out enabled, out option, out team)) { Malformed("Go", payload); return; }
            lock (sync)
            {
                if (disposed) return;
                current = OptionsOf(team);
                current = enabled ? current | option : current & ~option;
                if (current == FightOptions.None) teamOptions.Remove(team); else teamOptions[team] = current;
            }
            FightOptionChanged?.Invoke(team, current); Changed();
        }

        /// <summary>
        /// Oublie les options des équipes d'un combat de la carte qui apparaît (<c>Gc+</c>) ou disparaît (<c>Gc-</c>) : le client recrée ou
        /// retire alors les sprites de ces équipes, et leurs options avec eux.
        /// </summary>
        internal void ForgetTeamOptions(IEnumerable<long> teams)
        {
            if (teams == null) return;
            bool changed = false;
            lock (sync)
            {
                if (disposed) return;
                foreach (long team in teams) changed |= teamOptions.Remove(team);
            }
            if (changed) Changed();
        }

        /// <summary><c>Gf&lt;combattant&gt;|&lt;cellule&gt;</c> : cellule signalée (message <c>PLAYER_SET_FLAG</c> du client).</summary>
        internal void ShowFlag(string payload)
        {
            int actorId, cellId; FightFlag flag;
            if (!FightEffectPackets.TryParseFlag(payload, out actorId, out cellId)) { Malformed("Gf", payload); return; }
            lock (sync) { if (!InFight) return; lastFlag = flag = new FightFlag(actorId, cellId, DateTime.UtcNow); }
            Report(0, actorId, 0, NameOf(actorId) + " signale la cellule " + cellId + ".");
            FlagReceived?.Invoke(flag); Changed();
        }

        /// <summary><c>GV</c> : sortie du combat sans résultat (abandon, spectateur, exclusion).</summary>
        internal void Finish() { Finish(null); }
        /// <summary>
        /// <c>GE…</c> : fin du combat ; le résultat est conservé dans <see cref="LastResult"/>. Renvoie vrai si un combat
        /// en cours vient de se terminer (faux hors combat ou après fermeture du compte).
        /// </summary>
        internal bool Finish(string resultPayload)
        {
            FightResult result = resultPayload == null ? null : FightResult.Parse(resultPayload);
            if (resultPayload != null && result == null) Malformed("GE", resultPayload);
            else if (result != null && result.Rejected.Count > 0)
                account.Logger?.LogDanger("COMBAT", result.Rejected.Count + " ligne(s) du résultat de combat illisible(s), ignorée(s).");
            int self = account.Game.character.id;
            FightLogEntry[] kept;
            lock (sync) { if (!InFight) return false; kept = journal.ToArray(); if (result != null) result.Challenges = challenges.ToArray(); }
            Clear(false);
            lock (sync)
            {
                if (disposed) return false;
                phase = CombatPhase.Finished; lastResult = result; journal.AddRange(kept);
                FightResultEntry own = result?.Find(self);
                lastMessage = own == null ? "Combat terminé." : own.Kind == FightResultKind.Winner ? "Combat terminé : victoire."
                    : own.Kind == FightResultKind.Loser ? "Combat terminé : défaite." : "Combat terminé.";
            }
            if (account.AccountStates == AccountStates.FIGHTING) account.AccountStates = AccountStates.CONNECTED_INACTIVE;
            Changed();
            if (result != null) CombatResultReceived?.Invoke(result);
            CombatFinished?.Invoke();
            return true;
        }
        public void UpdateFighterFromMap(string[] info)
        {
            int id, type; short cell;
            if (info == null || info.Length < 7 || !int.TryParse(info[3], out id) || !short.TryParse(info[0], out cell) || !int.TryParse(info[5].Split(',')[0], out type)) return;
            lock (sync)
            {
                if (!InFight) return;
                CombatFighter fighter = GetFighter(id); fighter.Type = type; fighter.CellId = cell; fighter.IsDead = false;
                int value;
                // Monstre (type -2) : le cinquième champ est son modèle ; le client affiche le nom de monsters.xml (getMonstersText).
                fighter.Name = type == -2 && int.TryParse(info[4], out value) ? Monstres.Monstres.ResolveName(value) : info[4];
                if (int.TryParse(info[6].Split('^')[0], out value)) fighter.Gfx = value;
                if (int.TryParse(info[1], out value)) fighter.Orientation = value;
                int lifeIndex = type > 0 ? 14 : type == -2 ? 12 : -1;
                if (lifeIndex >= 0 && info.Length > lifeIndex + 2)
                {
                    if (int.TryParse(info[lifeIndex], out value)) fighter.Life = value;
                    if (int.TryParse(info[lifeIndex + 1], out value)) fighter.ActionPoints = fighter.BaseActionPoints = value;
                    if (int.TryParse(info[lifeIndex + 2], out value)) fighter.MovementPoints = fighter.BaseMovementPoints = value;
                }
                int teamIndex = type > 0 ? 24 : type == -2 ? Array.FindLastIndex(info, field => !string.IsNullOrEmpty(field)) : -1;
                if (teamIndex >= 0 && info.Length > teamIndex && int.TryParse(info[teamIndex], out value)) fighter.Team = value;
                if (id == account.Game.character.id) { pa = fighter.ActionPoints; pm = fighter.MovementPoints; }
            }
            Changed();
        }
        public void RemoveFighter(int id) { lock (sync) { if (!InFight) return; fighters.Remove(id); } Changed(); }
        internal void UpdatePositions(string payload)
        {
            foreach (string entry in payload.TrimStart('|').Split('|'))
            {
                string[] fields = entry.Split(';'); int id; short cell;
                if (fields.Length < 2 || !int.TryParse(fields[0], out id) || !short.TryParse(fields[1], out cell)) continue;
                lock (sync)
                {
                    if (!InFight) return; GetFighter(id).CellId = cell;
                    if (id == account.Game.character.id && pendingKind == "placement") { pendingKind = null; lastMessage = "Position confirmée par le serveur."; }
                }
                UpdateMapCell(id, cell);
            }
            account.Game.Map.GetEntitiesRefreshEvent(); Changed();
        }
        internal void UpdateTeamStats(string payload)
        {
            foreach (string entry in payload.TrimStart('|').Split('|'))
            {
                string[] fields = entry.Split(';'); int id, dead;
                if (fields.Length < 2 || !int.TryParse(fields[0], out id) || !int.TryParse(fields[1], out dead)) continue;
                short cell = -1;
                lock (sync)
                {
                    if (!InFight) return;
                    CombatFighter fighter = GetFighter(id); fighter.IsDead = dead == 1;
                    if (dead == 1) { fighter.Life = 0; fighter.CellId = -1; }
                    else if (fields.Length >= 6)
                    {
                        int life, actionPoints, movementPoints;
                        if (!int.TryParse(fields[2], out life) || !int.TryParse(fields[3], out actionPoints) || !int.TryParse(fields[4], out movementPoints) || !short.TryParse(fields[5], out cell)) continue;
                        fighter.Life = life; fighter.ActionPoints = fighter.BaseActionPoints = actionPoints;
                        fighter.MovementPoints = fighter.BaseMovementPoints = movementPoints; fighter.CellId = cell;
                        int maximum; if (fields.Length > 7 && int.TryParse(fields[7], out maximum)) fighter.MaximumLife = maximum;
                    }
                    else continue;
                    if (id == account.Game.character.id) { pa = fighter.ActionPoints; pm = fighter.MovementPoints; }
                }
                UpdateMapCell(id, dead == 1 ? (short)-1 : cell);
            }
            account.Game.Map.GetEntitiesRefreshEvent(); Changed();
        }
        private void UpdateMapCell(int id, short cellId)
        {
            Map map = account.Game.Map;
            Cell cell = map.GetCellFromId(cellId);
            if (id == account.Game.character.id)
            {
                account.Game.character.Cell = cell;
                MapActor self = map.Self;
                if (self != null) self.Cell = cell;
            }
            else { Entites entity; if (map.Entites.TryGetValue(id, out entity)) entity.Cell = cell; }
        }
        internal void BeginAction(string payload)
        {
            int id; if (!int.TryParse(payload, out id)) return;
            lock (sync) { if (!InFight || id != account.Game.character.id) return; if (pendingKind == null) StartPending("action"); } Changed();
        }
        internal void EndAction(int id)
        {
            lock (sync)
            {
                if (!InFight || id != account.Game.character.id) return;
                bool refused = lastMessage.StartsWith("Le serveur a refusé", StringComparison.Ordinal);
                if (!WaitsForCastingUnlock(pendingKind))
                {
                    if (pendingKind == "corps à corps" && !pendingConfirmed && !refused) lastMessage = "Le serveur n’a pas confirmé l’attaque au corps à corps.";
                    pendingKind = null;
                }
                // StarLoco clears its casting lock later with GA;102;guid;guid,-0 (spells and bare-handed close combat).
                else if (!pendingConfirmed && !refused)
                    lastMessage = pendingKind == "poings" ? "Le serveur n’a pas confirmé le coup de poing." : "Le serveur n’a pas confirmé le lancement du sort.";
            }
            Changed();
        }
        /// <summary><c>GA;303</c> ou <c>GA;305</c> du personnage : le serveur a traité l'attaque au corps à corps demandée.</summary>
        internal void ConfirmCloseCombat(int actorId, bool criticalFailure)
        {
            lock (sync)
            {
                if (!InFight || actorId != account.Game.character.id || pendingKind == null) return;
                pendingConfirmed = true;
                lastMessage = criticalFailure ? "Échec critique au corps à corps : le serveur termine le tour." : "Attaque au corps à corps confirmée par le serveur.";
            }
        }
        internal void Refuse(string message)
        { lock (sync) { if (!InFight) return; lastMessage = "Le serveur a refusé l’action : " + message; } account.Logger.LogDanger("COMBAT", LastActionMessage); Changed(); }

        /// <summary>
        /// <c>GA</c> reçu en combat (appelé par <c>MapFrame</c> avec le paquet découpé sur <c>;</c>). 0 (refus) et 1 (déplacement)
        /// sont traités ici ; les autres actions passent par <see cref="FightActionTable"/>, puis par <see cref="GameActionRouter"/>
        /// (actions hors combat : défis, interactifs…). Un acteur vide désigne le personnage du compte, comme dans le client.
        /// </summary>
        internal async Task ProcessActionAsync(TcpClient connection, string[] parts)
        {
            if (parts == null || parts.Length < 2) return;
            GameActionPacket packet = GameActionPacket.Parse("GA" + string.Join(";", parts));
            if (packet == null) { Malformed("GA", string.Join(";", parts)); return; }
            if (packet.ActionId == 0) { lock (sync) { pendingKind = null; lastMessage = "Le serveur a refusé l’action."; } Changed(); return; }
            int source;
            if (packet.Actor.Length == 0) source = account.Game.character.id;
            else if (!int.TryParse(packet.Actor, out source)) { Malformed("GA", packet.Raw); return; }
            if (packet.ActionId == 1) { await ProcessMovementAsync(connection, packet.GameActionId, source, packet.Parameters).ConfigureAwait(false); return; }
            lock (sync) { if (!InFight) return; }
            var context = new FightActionContext(this, connection, packet, source);
            try
            {
                if (!await FightActionTable.DispatchAsync(context).ConfigureAwait(false)
                    && !await GameActionRouter.DispatchAsync(new GameActionContext(connection, packet)).ConfigureAwait(false))
                    account.Logger?.LogDebug("COMBAT", "Action de jeu GA " + packet.ActionId + " sans gestionnaire en combat, ignorée.");
            }
            catch (Exception error)
            {
                // Un gestionnaire défaillant ne doit ni couper la lecture des paquets suivants ni laisser l'interface sans rafraîchissement.
                account.Logger?.LogException("COMBAT", error);
            }
            account.Game.Map.GetEntitiesRefreshEvent(); Changed();
        }
        private async Task ProcessMovementAsync(TcpClient connection, string actionIdText, int id, string encoded)
        {
            List<Cell> path = PathfinderUtils.DecodeServerPath(account.Game.Map, encoded);
            if (path == null || path.Count == 0) return;
            int version, carried; CancellationToken token;
            Cell destination = path.Last();
            lock (sync)
            {
                if (!InFight || disposed) return;
                version = generation; token = cancellation.Token;
                CombatFighter mover = GetFighter(id);
                mover.CellId = destination.CellID;
                // Un combattant porté qui se déplace quitte son porteur ; celui qu'un porteur transporte le suit (client : uncarriedSprite).
                if (mover.CarriedById != 0) Unlink(mover.CarriedById, id);
                carried = mover.CarryingId;
                if (carried != 0) GetFighter(carried).CellId = destination.CellID;
            }
            int duration = PathfinderUtils.GetTimeOnMap(path[0], path);
            account.Game.Map.NotifyEntityMovement(id, path, duration);
            UpdateMapCell(id, destination.CellID);
            if (carried != 0) UpdateMapCell(carried, destination.CellID);
            account.Game.Map.GetEntitiesRefreshEvent(); Changed();
            int actionId;
            if (id != account.Game.character.id || !int.TryParse(actionIdText, out actionId) || actionId < 0) return;
            try { await Task.Delay(duration, token).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
            lock (sync) { if (disposed || version != generation || !MyTurn || !ReferenceEquals(connection, account.Connexion) || account.Game.Map.GetCellFromId(destination.CellID) != destination) return; }
            await connection.SendPacket("GKK" + actionId).ConfigureAwait(false);
        }

        /// <summary>Nom affichable d'un combattant : acteur de la carte (nom résolu des monstres), sinon nom reçu dans <c>GM</c>.</summary>
        internal string NameOf(int id)
        {
            MapActor mapActor = account.Game.Map.GetActor(id);
            if (mapActor != null && !string.IsNullOrEmpty(mapActor.DisplayName)) return mapActor.DisplayName;
            if (id == account.Game.character.id && !string.IsNullOrEmpty(account.Game.character.Name)) return account.Game.character.Name;
            lock (sync) { CombatFighter fighter; if (fighters.TryGetValue(id, out fighter) && !string.IsNullOrEmpty(fighter.Name)) return fighter.Name; }
            return "Combattant #" + id;
        }
        /// <summary>Ajoute une ligne au journal puis lève <see cref="JournalEntryAdded"/> hors du verrou.</summary>
        internal void Report(int actionId, int actorId, int targetId, string text)
        {
            FightLogEntry entry = new FightLogEntry(actionId, actorId, targetId, text);
            lock (sync)
            {
                if (!InFight) return;
                journal.Add(entry);
                if (journal.Count > JournalCapacity) journal.RemoveRange(0, journal.Count - JournalCapacity);
            }
            JournalEntryAdded?.Invoke(entry);
        }
        private void Malformed(string prefix, string payload)
        {
            string shown = payload ?? string.Empty;
            if (shown.Length > 80) shown = shown.Substring(0, 80) + "…";
            account.Logger?.LogDanger("COMBAT", "Paquet " + prefix + " illisible ignoré : " + shown);
        }
        public void Clear(bool notify = true)
        {
            CancellationTokenSource previous;
            lock (sync)
            {
                generation++; previous = cancellation; cancellation = disposed ? null : new CancellationTokenSource();
                phase = CombatPhase.None; spectator = ready = canCancel = challengeMenu = firstTurnPending = false; fightType = -1; actor = turn = turnDuration = 0; pa = pm = -1;
                lastActor = readyActor = 0; tableTurn = null; turnStartedUtc = DateTime.MinValue;
                places = new short[0]; teamPlaces = new[] { new short[0], new short[0] }; fighters.Clear(); lastSpellTurn.Clear(); castsThisTurn.Clear(); castsOnTarget.Clear();
                turnOrder.Clear(); effects.Clear(); states.Clear(); zones.Clear(); teamOptions.Clear(); journal.Clear(); challenges.Clear();
                lastFlag = null; lastResult = null;
                pendingKind = null; pendingConfirmed = false; lastMessage = "Aucun combat en cours.";
            }
            if (previous != null) { previous.Cancel(); previous.Dispose(); }
            if (notify && !disposed) Changed();
        }
        public void Dispose()
        {
            lock (sync) { if (disposed) return; disposed = true; }
            Clear(false);
            CombatChanged = null; CombatReady = null; CombatFinished = null;
            CombatResultReceived = null; FlagReceived = null; FightOptionChanged = null; JournalEntryAdded = null; ChallengeChanged = null;
        }
    }
}
