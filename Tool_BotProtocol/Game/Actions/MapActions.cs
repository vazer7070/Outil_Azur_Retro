using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Perso;

namespace Tool_BotProtocol.Game.Actions
{
    /// <summary>Sens d'un duel en attente : proposé au personnage, ou proposé par lui.</summary>
    public enum ChallengeDirection { Incoming, Outgoing }

    /// <summary>Duel annoncé par <c>GA;900;&lt;demandeur&gt;;&lt;cible&gt;</c>.</summary>
    public sealed class ChallengeInfo
    {
        internal ChallengeInfo(long challengerId, string challengerName, long targetId, string targetName, ChallengeDirection direction, int mapId)
        {
            ChallengerId = challengerId; ChallengerName = challengerName; TargetId = targetId; TargetName = targetName;
            Direction = direction; MapId = mapId;
        }
        public long ChallengerId { get; }
        public string ChallengerName { get; }
        public long TargetId { get; }
        public string TargetName { get; }
        public ChallengeDirection Direction { get; }
        public int MapId { get; }
        /// <summary>L'autre joueur du duel, vu du personnage du compte.</summary>
        public long OpponentId => Direction == ChallengeDirection.Incoming ? ChallengerId : TargetId;
        public string OpponentName => Direction == ChallengeDirection.Incoming ? ChallengerName : TargetName;
    }

    /// <summary>Canal d'un message des actions de carte : discussion d'information, d'erreur, ou texte centré du client.</summary>
    public enum MapActionNoticeKind { Info, Error, Alert }

    public sealed class MapActionNotice
    {
        internal MapActionNotice(MapActionNoticeKind kind, string text) { Kind = kind; Text = text; }
        public MapActionNoticeKind Kind { get; }
        public string Text { get; }
    }

    /// <summary>
    /// Actions du menu contextuel du client sur les joueurs, groupes de monstres, percepteurs, prismes et combats de la
    /// carte (<c>GameManager.getPlayerPopupMenu</c>, <c>dofus.aks.GameActions</c>, <c>dofus.aks.Fights</c>), et lecture des
    /// réponses : <c>GA;900</c> à <c>GA;909</c>, <c>fC</c>, <c>fL</c>, <c>fD</c>, <c>BWK</c>/<c>BWE</c>. Les envois reprennent
    /// <c>GameActions.sendActions</c> (<c>GA</c> + action sur trois chiffres + paramètres séparés par <c>;</c>).
    /// Les événements sont levés sur le fil réseau ; l'interface doit les remettre sur son propre fil.
    /// </summary>
    public sealed class MapActions : InteractionWindow
    {
        public const int ChallengeAction = 900;
        public const int AcceptChallengeAction = 901;
        public const int RefuseChallengeAction = 902;
        public const int JoinFightAction = 903;
        public const int AssaultAction = 906;
        public const int AttackCollectorAction = 909;
        public const int AttackPrismAction = 912;
        /// <summary>Bits de <c>AR</c> lus pour le personnage du compte (<c>dofus.datacenter.Player</c>).</summary>
        private const int CantAssault = 1, CantChallenge = 2, CantExchange = 4, CantInteractWithTaxCollector = 128, CantInteractWithPrism = 32768;
        private static readonly IReadOnlyList<MapFightInfo> NoFights = new MapFightInfo[0];

        private readonly object sync = new object();
        private readonly HashSet<string> ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private ChallengeInfo pending;
        private int fightCount;
        private int fightsMapId = int.MinValue;
        private IReadOnlyList<MapFightInfo> fights = NoFights;
        private WhoisInfo lastWhois;
        private string pendingWhois;
        private string lastJoinError;

        internal MapActions(Accounts.Accounts account) : base(account) { }

        protected override string Reference => "COMBATS";
        /// <summary>Aucune fenêtre serveur : l'état du compte n'est jamais modifié par ce service.</summary>
        protected override AccountStates OpenState => AccountStates.CONNECTED_INACTIVE;

        /// <summary>Duel proposé au personnage (<c>A_CHALENGE_YOU</c>) : le client ouvre <c>AskYesNoIgnore</c>.</summary>
        public event Action<ChallengeInfo> ChallengeAsked;
        /// <summary>Duel proposé par le personnage et confirmé par le serveur (<c>YOU_CHALENGE_B</c>, boîte « Annuler »).</summary>
        public event Action<ChallengeInfo> ChallengeSent;
        /// <summary>Duel du personnage terminé : <c>true</c> accepté (<c>GA;901</c>), <c>false</c> refusé ou annulé (<c>GA;902</c>).</summary>
        public event Action<ChallengeInfo, bool> ChallengeClosed;
        /// <summary>Message à afficher : information, erreur (<c>GA;903</c>), ou texte centré (<c>YOU_ARE_ATTAC</c>).</summary>
        public event Action<MapActionNotice> Notice;
        /// <summary>Compteur <c>fC</c>, liste <c>fL</c> ou détail <c>fD</c> mis à jour.</summary>
        public event Action FightsChanged;
        public event Action<WhoisInfo> WhoisReceived;
        /// <summary>« Message privé » : texte <c>/w &lt;nom&gt; </c> à placer dans la console de discussion (branchée par l'interface du chat).</summary>
        public event Action<string> ChatInputRequested;

        /// <summary>Duel en attente de réponse, ou <c>null</c>.</summary>
        public ChallengeInfo PendingChallenge { get { lock (sync) return pending; } }
        /// <summary>Nombre de combats sur la carte (<c>fC</c>).</summary>
        public int FightCount { get { lock (sync) return fightsMapId == CurrentMapId ? fightCount : 0; } }
        /// <summary>Combats de la carte courante lus dans <c>fL</c>/<c>fD</c> (vide après un changement de carte).</summary>
        public IReadOnlyList<MapFightInfo> Fights { get { lock (sync) return fightsMapId == CurrentMapId ? fights : NoFights; } }
        public WhoisInfo LastWhois { get { lock (sync) return lastWhois; } }
        /// <summary>Dernier code d'erreur de <c>GA;903</c> (<c>o</c>, <c>z</c>, <c>c</c>…), ou <c>null</c>.</summary>
        public string LastJoinError { get { lock (sync) return lastJoinError; } }
        /// <summary>Joueurs ignorés pour la session (« Ignorer » d'un duel, <c>TEMPORARY_BLACKLISTED</c>).</summary>
        public IReadOnlyCollection<string> IgnoredPlayers { get { lock (sync) return ignored.ToArray(); } }

        public MapFightInfo GetFight(long fightId) => Fights.FirstOrDefault(fight => fight.FightId == fightId);
        public bool IsIgnored(string name) { lock (sync) return !string.IsNullOrEmpty(name) && ignored.Contains(name); }

        private int CurrentMapId => Account?.Game?.Map?.MapID ?? 0;
        private long SelfId => Account?.Game?.character?.id ?? 0;
        private bool IsSelf(long id) => id != 0 && id == SelfId;

        // ---- Règles du menu du client -------------------------------------------------------------------

        /// <summary>Restriction <c>AR</c> du personnage (bits du client) ; aucune avant le premier <c>AR</c>.</summary>
        public bool HasOwnRestriction(int bit)
        {
            int raw = Account?.Game?.Session?.RawRestrictions ?? -1;
            return raw >= 0 && (raw & bit) != 0;
        }

        /// <summary>Camp du personnage : sprite <c>GM</c> du compte (comme <c>Player.data.alignment</c>), sinon <c>As</c>.</summary>
        public int OwnAlignment
        {
            get
            {
                if (Account?.Game?.Map?.Self is PlayerActor self) return self.Alignment?.Side ?? 0;
                return Account?.Game?.character?.stats?.Alignement ?? 0;
            }
        }

        /// <summary>« Défier » visible : <c>Player.canChallenge &amp;&amp; p1.canBeChallenge</c>.</summary>
        public bool CanChallengePlayer(PlayerActor target) =>
            target != null && !HasOwnRestriction(CantChallenge) && (target.Restrictions & PlayerRestrictions.CannotBeChallenged) == 0;

        /// <summary>« Agresser » visible : <c>Player.canAssault</c> et <c>getAlignmentCanAttack(moi, lui)</c>.</summary>
        public bool CanAssaultPlayer(PlayerActor target) =>
            target != null && !HasOwnRestriction(CantAssault) && CanAttackAlignment(OwnAlignment, target.Alignment?.Side ?? 0);

        /// <summary>« Échanger » visible : <c>Player.canExchange &amp;&amp; p1.canExchange</c>.</summary>
        public bool CanExchangeWith(PlayerActor target) =>
            target != null && !HasOwnRestriction(CantExchange) && (target.Restrictions & PlayerRestrictions.CannotExchange) == 0;

        /// <summary>Table <c>A.at</c> des textes du client ; sans elle, un personnage aligné peut tenter (le serveur tranche).</summary>
        public static bool CanAttackAlignment(int mine, int theirs) => AlignmentRule("A.at", mine, theirs) ?? mine > 0;
        /// <summary>Table <c>A.jo</c> des textes du client ; sans elle, seul le même camp peut rejoindre.</summary>
        public static bool CanJoinAlignment(int mine, int team) => AlignmentRule("A.jo", mine, team) ?? mine == team;

        private static bool? AlignmentRule(string table, int mine, int other)
        {
            IReadOnlyDictionary<string, string> row = LangData.Raw("alignment", table, mine.ToString(CultureInfo.InvariantCulture));
            if (row == null || !row.TryGetValue("valeur", out string value) || value == null) return null;
            string[] cells = value.Split(',');
            // Comme at[p1][p2] du client : une case absente vaut « faux ».
            return other >= 0 && other < cells.Length && string.Equals(cells[other].Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Équipe rejoignable d'après le clic sur une épée du client (type de combat, type d'équipe, alignements) ; les
        /// limites de places (<c>getMapMaxTeam</c>) ne sont pas connues du bot : le serveur répond <c>c</c> ou <c>t</c>.
        /// </summary>
        public bool CanJoinTeam(FightSwordsActor swords, FightTeamFlag team)
        {
            if (swords == null || team == null || !swords.Teams.Contains(team)) return false;
            int mine = OwnAlignment;
            FightTeamFlag enemy = swords.Teams.FirstOrDefault(other => !ReferenceEquals(other, team));
            switch (swords.FightType)
            {
                case 0: return team.TeamType == 0 || team.TeamType == 2;
                case 1:
                case 2:
                    if (team.TeamType != 0 && team.TeamType != 1) return false;
                    if (mine == team.Alignment) return true;
                    return enemy != null && CanAttackAlignment(mine, enemy.Alignment) && CanJoinAlignment(mine, team.Alignment);
                case 3:
                case 4:
                case 6: return team.TeamType == 0;
                case 5: return team.TeamType == 0 && !HasOwnRestriction(CantInteractWithTaxCollector);
                default: return false;
            }
        }

        // ---- Envois -------------------------------------------------------------------------------------

        /// <summary>« Défier » : <c>GA900&lt;id&gt;</c> (<c>GameActions.challenge</c>).</summary>
        public Task<InteractionResult> ChallengeAsync(long playerId)
        {
            PlayerActor target = null;
            InteractionResult refused = CheckReady() ?? CheckPlayer(playerId, out target);
            if (refused != null) return Task.FromResult(refused);
            if (!Account.Game.Map.CanChallenge) return Task.FromResult(Refuse("Les duels sont interdits sur cette carte."));
            if (!CanChallengePlayer(target)) return Task.FromResult(Refuse("Duel impossible avec " + target.DisplayName + " (restrictions du personnage)."));
            if (PendingChallenge != null) return Task.FromResult(Refuse("Un duel attend déjà une réponse."));
            return SendAsync("GA" + ChallengeAction + Id(playerId), "Duel proposé à " + target.DisplayName + " ; le serveur l'annonce à la carte.");
        }

        /// <summary>« Oui » au duel reçu : <c>GA901&lt;demandeur&gt;</c> (<c>GameActions.acceptChallenge</c>).</summary>
        public async Task<InteractionResult> AcceptChallengeAsync(long challengerId)
        {
            ChallengeInfo current = PendingChallenge;
            if (current == null || current.Direction != ChallengeDirection.Incoming || current.ChallengerId != challengerId)
                return Refuse("Aucun duel de ce joueur n'attend de réponse.");
            InteractionResult result = await SendAsync("GA" + AcceptChallengeAction + Id(challengerId), "Duel de " + current.ChallengerName + " accepté.").ConfigureAwait(false);
            if (result.Sent) ForgetPending(current);
            return result;
        }

        /// <summary>
        /// « Non » au duel reçu (<c>GA902&lt;demandeur&gt;</c>) ou « Annuler » le duel proposé (<c>GA902&lt;soi&gt;</c>, l'identifiant
        /// que le client passe à <c>refuseChallenge</c> depuis la boîte <c>YOU_CHALENGE_B</c>).
        /// </summary>
        public async Task<InteractionResult> RefuseChallengeAsync(long id)
        {
            ChallengeInfo current = PendingChallenge;
            if (current == null) return Refuse("Aucun duel en attente.");
            long sent;
            if (current.Direction == ChallengeDirection.Incoming && current.ChallengerId == id) sent = id;
            else if (current.Direction == ChallengeDirection.Outgoing && (IsSelf(id) || current.TargetId == id)) sent = SelfId;
            else return Refuse("Ce duel n'est pas celui en attente.");
            string message = current.Direction == ChallengeDirection.Incoming
                ? "Duel de " + current.ChallengerName + " refusé." : "Duel proposé à " + current.TargetName + " annulé.";
            InteractionResult result = await SendAsync("GA" + RefuseChallengeAction + Id(sent), message).ConfigureAwait(false);
            if (result.Sent) ForgetPending(current);
            return result;
        }

        /// <summary>« Annuler » le duel proposé par le personnage : <c>GA902&lt;soi&gt;</c>.</summary>
        public Task<InteractionResult> CancelChallengeAsync() => RefuseChallengeAsync(SelfId);

        /// <summary>« Ignorer » : le demandeur est ignoré pour la session (ses duels sont refusés d'office) puis <c>GA902&lt;demandeur&gt;</c>.</summary>
        public Task<InteractionResult> IgnoreChallengerAsync(long challengerId)
        {
            ChallengeInfo current = PendingChallenge;
            if (current == null || current.Direction != ChallengeDirection.Incoming || current.ChallengerId != challengerId)
                return Task.FromResult(Refuse("Aucun duel de ce joueur n'attend de réponse."));
            lock (sync) ignored.Add(current.ChallengerName);
            Log(MapActionTexts.TemporaryIgnored(current.ChallengerName));
            return RefuseChallengeAsync(challengerId);
        }

        /// <summary>« Rejoindre » une équipe : <c>GA903&lt;combat&gt;;&lt;équipe&gt;</c> (<c>GameActions.joinChallenge</c>).</summary>
        public Task<InteractionResult> JoinFightAsync(long fightId, long teamId)
        {
            InteractionResult refused = CheckReady();
            if (refused != null) return Task.FromResult(refused);
            if (!Account.Game.Map.FightSwords.TryGetValue(fightId, out FightSwordsActor swords) || swords == null)
                return Task.FromResult(Refuse("Ce combat n'est plus annoncé sur la carte."));
            if (!swords.Teams.Any(team => team.TeamId == teamId)) return Task.FromResult(Refuse("Cette équipe n'appartient pas au combat."));
            return SendAsync("GA" + JoinFightAction + Id(fightId) + ";" + Id(teamId), "Demande pour rejoindre le combat " + Id(fightId) + " envoyée.");
        }

        /// <summary>Spectateur : <c>GA903&lt;combat&gt;</c> (<c>joinChallenge</c> sans équipe, bouton du volet des combats).</summary>
        public Task<InteractionResult> SpectateAsync(long fightId)
        {
            InteractionResult refused = CheckReady();
            if (refused != null) return Task.FromResult(refused);
            if (!Account.Game.Map.FightSwords.ContainsKey(fightId) && GetFight(fightId) == null)
                return Task.FromResult(Refuse("Ce combat n'est pas connu sur la carte."));
            return SendAsync("GA" + JoinFightAction + Id(fightId), "Demande pour regarder le combat " + Id(fightId) + " envoyée.");
        }

        /// <summary>« Agresser » après confirmation : <c>GA906&lt;id&gt;</c> (<c>GameActions.attack</c>).</summary>
        public Task<InteractionResult> AssaultAsync(long playerId)
        {
            PlayerActor target = null;
            InteractionResult refused = CheckReady() ?? CheckPlayer(playerId, out target);
            if (refused != null) return Task.FromResult(refused);
            if (!Account.Game.Map.CanAttack) return Task.FromResult(Refuse("Les agressions sont interdites sur cette carte."));
            if (HasOwnRestriction(CantAssault)) return Task.FromResult(Refuse("Votre personnage ne peut pas agresser (restriction du serveur)."));
            if (!CanAttackAlignment(OwnAlignment, target.Alignment?.Side ?? 0))
                return Task.FromResult(Refuse("Votre alignement ne permet pas d'agresser " + target.DisplayName + "."));
            return SendAsync("GA" + AssaultAction + Id(playerId), "Agression de " + target.DisplayName + " demandée.");
        }

        /// <summary>Attaquer un percepteur : <c>GA909&lt;id&gt;</c> (<c>GameActions.attackTaxCollector</c>).</summary>
        public Task<InteractionResult> AttackCollectorAsync(long collectorId)
        {
            InteractionResult refused = CheckReady();
            if (refused != null) return Task.FromResult(refused);
            var collector = Account.Game.Map.GetActor(collectorId) as CollectorActor;
            if (collector == null) return Task.FromResult(Refuse("Ce percepteur n'est plus sur la carte."));
            if (HasOwnRestriction(CantInteractWithTaxCollector)) return Task.FromResult(Refuse("Votre personnage ne peut pas interagir avec les percepteurs."));
            return SendAsync("GA" + AttackCollectorAction + Id(collectorId), "Attaque de " + collector.DisplayName + " demandée.");
        }

        /// <summary>Attaquer un prisme : <c>GA912&lt;id&gt;</c> (<c>GameActions.attackPrism</c>).</summary>
        public Task<InteractionResult> AttackPrismAsync(long prismId)
        {
            InteractionResult refused = CheckReady();
            if (refused != null) return Task.FromResult(refused);
            var prism = Account.Game.Map.GetActor(prismId) as PrismActor;
            if (prism == null) return Task.FromResult(Refuse("Ce prisme n'est plus sur la carte."));
            if (HasOwnRestriction(CantInteractWithPrism)) return Task.FromResult(Refuse("Votre personnage ne peut pas interagir avec les prismes."));
            return SendAsync("GA" + AttackPrismAction + Id(prismId), "Attaque du prisme demandée.");
        }

        /// <summary>
        /// « Attaquer » un groupe de monstres : comme le client (<c>onCellRelease</c> sur la cellule du groupe), le personnage
        /// marche jusqu'au groupe et le serveur lance le combat à l'arrivée. <paramref name="moveTo"/> est le déplacement de
        /// l'interface (clic sur la cellule).
        /// </summary>
        public async Task<InteractionResult> AttackGroupAsync(long groupId, Func<short, Task> moveTo)
        {
            InteractionResult refused = CheckReady();
            if (refused != null) return refused;
            var group = Account.Game.Map.GetActor(groupId) as MonsterGroupActor;
            if (group == null || group.CellId < 0 || group.CellId > short.MaxValue) return Refuse("Ce groupe de monstres n'est plus sur la carte.");
            if (moveTo == null) return Refuse("Déplacement indisponible.");
            short cell = (short)group.CellId;
            await moveTo(cell).ConfigureAwait(false);
            string leader = group.Leader?.Name ?? group.DisplayName;
            return new InteractionResult(true, "Attaque du groupe de " + leader + " : déplacement vers la cellule " + cell + ".");
        }

        /// <summary>« Informations » : <c>BW&lt;nom&gt;</c> (<c>Basics.whoIs</c>) ; un nom vide demande le personnage du compte.</summary>
        public Task<InteractionResult> WhoisAsync(string name)
        {
            name = name ?? string.Empty;
            InteractionResult refused = CheckName(name, allowEmpty: true);
            if (refused != null) return Task.FromResult(refused);
            lock (sync) pendingWhois = name;
            return SendAsync("BW" + name, "Informations demandées sur " + (name.Length == 0 ? "votre personnage" : name) + ".");
        }

        /// <summary>« Inviter dans le groupe » : <c>PI&lt;nom&gt;</c> (<c>Party.invite</c>) ; les réponses <c>PIK</c>/<c>PIE</c> sont lues par le groupe.</summary>
        public Task<InteractionResult> InviteToPartyAsync(string name)
        {
            InteractionResult refused = CheckName(name, allowEmpty: false);
            if (refused != null) return Task.FromResult(refused);
            return SendAsync("PI" + name, "Invitation de groupe envoyée à " + name + ".");
        }

        /// <summary>
        /// « Échanger » : <c>ER1|&lt;id&gt;</c> (<c>GameManager.startExchange(1, id)</c>), envoyé par l'échange entre joueurs
        /// (<see cref="Exchanges.PlayerExchange.RequestAsync"/>) qui lit la suite (<c>ERK</c>, <c>ECK1</c>…).
        /// </summary>
        public Task<InteractionResult> RequestExchangeAsync(long playerId)
        {
            PlayerActor target = null;
            InteractionResult refused = CheckReady() ?? CheckPlayer(playerId, out target);
            if (refused != null) return Task.FromResult(refused);
            if (!CanExchangeWith(target)) return Task.FromResult(Refuse("Échange impossible avec " + target.DisplayName + " (restrictions du personnage)."));
            Exchanges.PlayerExchange exchange = Account.Game.Interactions?.Exchange;
            if (exchange == null) return Task.FromResult(Refuse("L'échange entre joueurs n'est pas disponible."));
            if (playerId > int.MaxValue) return Task.FromResult(Refuse("Identifiant de joueur invalide."));
            return exchange.RequestAsync((int)playerId);
        }

        /// <summary>Liste des combats de la carte : <c>fL</c> (<c>Fights.getList</c>, à l'ouverture du volet).</summary>
        public Task<InteractionResult> RequestFightListAsync()
        {
            InteractionResult refused = CheckConnected();
            return refused != null ? Task.FromResult(refused) : SendAsync("fL", "Liste des combats de la carte demandée.");
        }

        /// <summary>Détail d'un combat : <c>fD&lt;id&gt;</c> (<c>Fights.getDetails</c>, à la sélection).</summary>
        public Task<InteractionResult> RequestFightDetailsAsync(long fightId)
        {
            InteractionResult refused = CheckConnected();
            return refused != null ? Task.FromResult(refused) : SendAsync("fD" + Id(fightId), "Détail du combat " + Id(fightId) + " demandé.");
        }

        /// <summary>
        /// « Message privé » : comme <c>GameManager.askPrivateMessage</c>, prépare <c>/w &lt;nom&gt; </c> pour la console de
        /// discussion via <see cref="ChatInputRequested"/> ; aucun paquet n'est envoyé.
        /// </summary>
        public InteractionResult RequestPrivateMessage(string name)
        {
            InteractionResult refused = CheckName(name, allowEmpty: false);
            if (refused != null) return refused;
            Action<string> handler = ChatInputRequested;
            if (handler == null) return Refuse("La console de discussion n'est pas reliée : tapez « /w " + name + " » suivi du message.");
            try { handler("/w " + name + " "); }
            catch (Exception error) { Account?.Logger?.LogException(Reference, error); return Refuse("Console de discussion indisponible : " + error.Message); }
            return new InteractionResult(true, "Message privé pour " + name + " : saisissez le texte dans la console.");
        }

        // ---- Réponses du serveur (FightsFrame) ---------------------------------------------------------------

        /// <summary><c>GA;900;&lt;demandeur&gt;;&lt;cible&gt;</c> envoyé à toute la carte (<c>GAME_SEND_MAP_NEW_DUEL_TO_MAP</c>).</summary>
        internal async Task OnChallengeAsked(long challengerId, long targetId)
        {
            bool mine = IsSelf(challengerId), forMe = IsSelf(targetId);
            string challenger = NameOf(challengerId), target = NameOf(targetId);
            if (!mine && !forMe && (challenger == null || target == null)) return;
            challenger = challenger ?? "#" + Id(challengerId);
            target = target ?? "#" + Id(targetId);
            Announce(MapActionNoticeKind.Info, MapActionTexts.AChallengesB(challenger, target));
            if (mine)
            {
                var sent = new ChallengeInfo(challengerId, challenger, targetId, target, ChallengeDirection.Outgoing, CurrentMapId);
                lock (sync) pending = sent;
                Raise(() => ChallengeSent?.Invoke(sent));
                Notify();
                return;
            }
            if (!forMe) return;
            bool busy, ignore;
            lock (sync) { busy = pending != null; ignore = ignored.Contains(challenger); }
            if (busy || ignore)
            {
                // Le client refuse d'office pendant qu'une boîte de duel est ouverte, ou si le demandeur est ignoré.
                Log("Duel de " + challenger + " refusé automatiquement (" + (ignore ? "joueur ignoré" : "un duel est déjà en cours") + ").");
                await SendAsync("GA" + RefuseChallengeAction + Id(challengerId), LastMessage).ConfigureAwait(false);
                return;
            }
            var asked = new ChallengeInfo(challengerId, challenger, targetId, target, ChallengeDirection.Incoming, CurrentMapId);
            lock (sync) pending = asked;
            Log(MapActionTexts.ChallengeYou(challenger));
            Raise(() => ChallengeAsked?.Invoke(asked));
            Notify();
        }

        /// <summary><c>GA;901;&lt;demandeur&gt;;&lt;cible&gt;</c> : duel accepté, le combat suit (<c>GJK</c>).</summary>
        internal void OnChallengeAccepted(long first, long second) => CloseChallenge(first, second, true);

        /// <summary><c>GA;902;&lt;joueur&gt;;&lt;joueur&gt;</c> : duel refusé ou annulé.</summary>
        internal void OnChallengeCancelled(long first, long second) => CloseChallenge(first, second, false);

        private void CloseChallenge(long first, long second, bool accepted)
        {
            ChallengeInfo closed;
            lock (sync)
            {
                ChallengeInfo current = pending;
                bool involved = IsSelf(first) || IsSelf(second)
                    || (current != null && (current.OpponentId == first || current.OpponentId == second));
                if (!involved) return;
                closed = current;
                pending = null;
            }
            if (closed == null)
            {
                bool outgoing = IsSelf(first);
                closed = new ChallengeInfo(first, NameOf(first) ?? "#" + Id(first), second, NameOf(second) ?? "#" + Id(second),
                    outgoing ? ChallengeDirection.Outgoing : ChallengeDirection.Incoming, CurrentMapId);
            }
            Log(accepted ? "Duel accepté : le combat commence." : "Duel avec " + closed.OpponentName + " annulé.");
            ChallengeInfo info = closed;
            Raise(() => ChallengeClosed?.Invoke(info, accepted));
            Notify();
        }

        /// <summary><c>GA;903;&lt;id&gt;;&lt;code&gt;</c> : refus d'un duel ou d'un combat (codes du client).</summary>
        internal void OnJoinRefused(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                LogError("Refus GA;903 sans code ignoré.");
                return;
            }
            string text = MapActionTexts.JoinError(code);
            lock (sync) lastJoinError = code ?? string.Empty;
            LogError(text);
            Announce(MapActionNoticeKind.Error, text, log: false);
            Notify();
        }

        /// <summary><c>GA;906;&lt;agresseur&gt;;&lt;cible&gt;</c> : agression ; sur le personnage, texte centré <c>YOU_ARE_ATTAC</c>.</summary>
        internal void OnAssault(long attackerId, long targetId)
        {
            string attacker = NameOf(attackerId), target = NameOf(targetId);
            if (attacker == null || target == null) return;
            Announce(MapActionNoticeKind.Info, MapActionTexts.AAttacksB(attacker, target));
            if (IsSelf(targetId)) Announce(MapActionNoticeKind.Alert, MapActionTexts.YouAreAttacked);
            Notify();
        }

        /// <summary><c>GA;909;&lt;joueur&gt;;&lt;percepteur ou prisme&gt;</c> : attaque annoncée à la carte.</summary>
        internal void OnCollectorAttack(long attackerId, long targetId)
        {
            string attacker = NameOf(attackerId), target = NameOf(targetId);
            if (attacker == null || target == null) return;
            Announce(MapActionNoticeKind.Info, MapActionTexts.AAttacksB(attacker, target));
            Notify();
        }

        /// <summary><c>fC&lt;n&gt;</c> : nombre de combats de la carte (une valeur négative retire des combats, comme le client).</summary>
        internal void OnFightCount(string payload)
        {
            payload = payload ?? string.Empty;
            int count = 0;
            if (payload.Length > 0 && !MapFightParser.TryInt(payload, out count))
            {
                LogError("Nombre de combats illisible : fC" + Shorten(payload));
                return;
            }
            int mapId = CurrentMapId;
            lock (sync)
            {
                if (fightsMapId != mapId) { fightsMapId = mapId; fights = NoFights; fightCount = 0; }
                fightCount = count < 0 ? Math.Max(0, fightCount + count) : count;
                if (fightCount == 0) fights = NoFights;
            }
            Raise(() => FightsChanged?.Invoke());
            Notify();
        }

        /// <summary><c>fL…</c> : combats de la carte ; le détail déjà reçu d'un combat toujours présent est conservé.</summary>
        internal void OnFightList(string payload)
        {
            IReadOnlyList<MapFightInfo> list = MapFightParser.ParseList(payload, out int skipped);
            if (skipped > 0) LogError(skipped + " combat(s) illisible(s) ignoré(s) dans fL" + Shorten(payload));
            int mapId = CurrentMapId;
            lock (sync)
            {
                IReadOnlyList<MapFightInfo> previous = fightsMapId == mapId ? fights : NoFights;
                fights = list.Select(fight =>
                {
                    MapFightInfo old = previous.FirstOrDefault(entry => entry.FightId == fight.FightId);
                    return old != null && old.HasDetails ? fight.WithDetails(old.Team1.Members, old.Team2.Members) : fight;
                }).ToList();
                fightsMapId = mapId;
            }
            Raise(() => FightsChanged?.Invoke());
            Notify();
        }

        /// <summary><c>fD&lt;id&gt;|…|…</c> : combattants des deux équipes.</summary>
        internal void OnFightDetails(string payload)
        {
            if (!MapFightParser.TryParseDetails(payload, out long fightId, out var team1, out var team2))
            {
                LogError("Détail de combat illisible : fD" + Shorten(payload));
                return;
            }
            int mapId = CurrentMapId;
            lock (sync)
            {
                List<MapFightInfo> list = (fightsMapId == mapId ? fights : NoFights).ToList();
                int index = list.FindIndex(fight => fight.FightId == fightId);
                if (index >= 0) list[index] = list[index].WithDetails(team1, team2);
                else list.Add(new MapFightInfo(fightId, null, new MapFightTeam(1, -1, 0, team1.Count, team1), new MapFightTeam(2, -1, 0, team2.Count, team2)));
                fights = list;
                fightsMapId = mapId;
            }
            Raise(() => FightsChanged?.Invoke());
            Notify();
        }

        /// <summary><c>BWK&lt;pseudo&gt;|&lt;état&gt;|&lt;personnage&gt;|&lt;zone&gt;</c> (<c>Basics.onWhoIs</c>).</summary>
        internal void OnWhois(string payload)
        {
            WhoisInfo info = MapFightParser.ParseWhois(payload);
            if (info == null)
            {
                LogError("Réponse « qui est » illisible : BWK" + Shorten(payload));
                return;
            }
            string login = Account?.accountConfig?.Account ?? string.Empty;
            info.Text = MapActionTexts.WhoisAnswer(info, string.Equals(info.Pseudo.ToLowerInvariant(), login, StringComparison.Ordinal));
            lock (sync) { lastWhois = info; pendingWhois = null; }
            Announce(MapActionNoticeKind.Info, info.Text);
            Raise(() => WhoisReceived?.Invoke(info));
            Notify();
        }

        /// <summary>
        /// Joueur introuvable pour une demande « Informations » de ce service. StarLoco répond <c>PIEn&lt;nom&gt;</c>, préfixe
        /// du groupe : son lecteur peut appeler cette méthode, qui ne signale rien si aucune demande « qui est » n'attend ce
        /// nom (le même <c>PIEn</c> répond aussi à une invitation de groupe).
        /// </summary>
        public bool ReportWhoisNotFound(string name)
        {
            name = name ?? string.Empty;
            lock (sync)
            {
                if (pendingWhois == null || (name.Length > 0 && !string.Equals(pendingWhois, name, StringComparison.OrdinalIgnoreCase))) return false;
                pendingWhois = null;
            }
            AnnounceWhoisNotFound(name);
            return true;
        }

        /// <summary><c>BWE&lt;nom&gt;</c> : <c>Basics.onWhoIs(false, nom)</c> du client, affiché même pour un <c>/whois</c> de la console.</summary>
        internal void OnWhoisError(string name)
        {
            lock (sync) pendingWhois = null;
            AnnounceWhoisNotFound(name ?? string.Empty);
        }

        private void AnnounceWhoisNotFound(string name)
        {
            string text = MapActionTexts.WhoisNotFound(name);
            LogError(text);
            Announce(MapActionNoticeKind.Error, text, log: false);
            Notify();
        }

        // ---- Outils -------------------------------------------------------------------------------------------

        protected override void Reset()
        {
            lock (sync)
            {
                pending = null; ignored.Clear(); fightCount = 0; fightsMapId = int.MinValue; fights = NoFights;
                lastWhois = null; pendingWhois = null; lastJoinError = null;
            }
        }

        private void ForgetPending(ChallengeInfo answered)
        {
            lock (sync) if (ReferenceEquals(pending, answered)) pending = null;
            Notify();
        }

        private InteractionResult CheckConnected() =>
            Account?.Connexion == null || !Account.Connexion.IsConnected() ? Refuse("Connectez le personnage avant cette action.") : null;

        private InteractionResult CheckReady()
        {
            InteractionResult refused = CheckConnected();
            if (refused != null) return refused;
            if (Account.Game?.Map == null) return Refuse("La carte n'est pas chargée.");
            if (Account.IsFighting()) return Refuse("Action impossible pendant un combat.");
            return null;
        }

        private InteractionResult CheckPlayer(long playerId, out PlayerActor target)
        {
            target = Account?.Game?.Map?.GetActor(playerId) as PlayerActor;
            if (target == null || target.IsSelf || IsSelf(playerId)) return Refuse("Ce joueur n'est plus sur la carte.");
            return null;
        }

        private InteractionResult CheckName(string name, bool allowEmpty)
        {
            InteractionResult refused = CheckConnected();
            if (refused != null) return refused;
            if (string.IsNullOrEmpty(name)) return allowEmpty ? null : Refuse("Nom de personnage manquant.");
            if (name.Length > 64 || name.Any(character => character == '|' || char.IsControl(character)))
                return Refuse("Nom de personnage invalide.");
            return null;
        }

        /// <summary>Nom d'un acteur de la carte (ou du personnage du compte), <c>null</c> s'il est inconnu.</summary>
        private string NameOf(long id)
        {
            if (IsSelf(id))
            {
                string own = Account?.Game?.character?.Name;
                return string.IsNullOrEmpty(own) ? "#" + Id(id) : own;
            }
            MapActor actor = Account?.Game?.Map?.GetActor(id);
            return actor == null ? null : actor.DisplayName;
        }

        private void Announce(MapActionNoticeKind kind, string text, bool log = true)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (log) { if (kind == MapActionNoticeKind.Error) LogError(text); else Log(text); }
            var notice = new MapActionNotice(kind, text);
            Raise(() => Notice?.Invoke(notice));
        }

        private void Raise(Action action)
        {
            try { action(); }
            catch (Exception error) { Account?.Logger?.LogException(Reference, error); }
        }

        private static string Id(long value) => value.ToString(CultureInfo.InvariantCulture);
        private static string Shorten(string value) => value == null ? string.Empty : value.Length <= 80 ? value : value.Substring(0, 80) + "…";
    }
}
