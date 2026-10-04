using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Managers.Mouvements;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Interactives;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Game.Perso.Inventory;
using Tool_BotProtocol.Game.Perso.Inventory.Enums;

namespace Tool_BotProtocol.Game.Interactions
{
    /// <summary>Temps écoulé et attente des actions d'objets interactifs ; un test le remplace par une horloge manuelle.</summary>
    public class InteractionClock
    {
        public static readonly InteractionClock Default = new InteractionClock();
        private readonly Stopwatch watch = Stopwatch.StartNew();
        /// <summary>Millisecondes écoulées depuis une origine fixe.</summary>
        public virtual long NowMs => watch.ElapsedMilliseconds;
        public virtual Task Delay(int milliseconds, CancellationToken token) => Task.Delay(Math.Max(0, milliseconds), token);
    }

    /// <summary>Action d'objet interactif annoncée par <c>GA&lt;id&gt;;501;&lt;acteur&gt;;&lt;cellule&gt;,&lt;durée&gt;</c>.</summary>
    public sealed class InteractiveActionInfo
    {
        public string GameActionId { get; internal set; }
        public long ActorId { get; internal set; }
        /// <summary>Vrai pour le personnage du compte : le bot acquittera par <c>GKK&lt;id&gt;</c> à la fin de la durée.</summary>
        public bool IsSelf { get; internal set; }
        public short CellId { get; internal set; }
        public int DurationMs { get; internal set; }
        /// <summary>Compétence demandée par le bot sur cette cellule (0 si l'action vient d'ailleurs).</summary>
        public short SkillId { get; internal set; }
        public long StartedAtMs { get; internal set; }
    }

    /// <summary>
    /// État d'une compétence dans le menu, comme <c>Skill.getState</c> du client : le critère <c>SK[id].c</c>
    /// (« J?V:- », « O&amp;!L?V:X »…) teste J, O, S, L, I, N dans cet ordre de paramètres ; « ! » inverse le paramètre.
    /// « V » : entrée active ; « X » : entrée cachée ; toute autre valeur : entrée grisée.
    /// </summary>
    public static class SkillState
    {
        public const string Enabled = "V";
        public const string Hidden = "X";
        public const string Disabled = "-";

        /// <summary>Évalue un critère ; sans critère, « V ». Un critère sans « ? » (« X » seul) donne une entrée grisée, comme le client.</summary>
        public static string Evaluate(string criterion, bool j, bool o = false, bool s = false, bool l = false, bool i = false, bool n = false)
        {
            if (string.IsNullOrEmpty(criterion)) return Enabled;
            int question = criterion.IndexOf('?');
            if (question < 0) return Disabled;
            string[] results = criterion.Substring(question + 1).Split(':');
            string success = results[0];
            string failure = results.Length > 1 ? results[1] : Disabled;
            var values = new Dictionary<char, bool> { { 'J', j }, { 'O', o }, { 'S', s }, { 'L', l }, { 'I', i }, { 'N', n } };
            foreach (string raw in criterion.Substring(0, question).Split('&'))
            {
                string condition = raw;
                bool negate = condition.StartsWith("!", StringComparison.Ordinal);
                if (negate) condition = condition.Substring(1);
                if (condition.Length != 1 || !values.ContainsKey(condition[0])) continue;
                // Le client inverse le paramètre lui-même (p6 = !p6) : une lettre répétée garde l'inversion.
                if (negate) values[condition[0]] = !values[condition[0]];
                if (!values[condition[0]]) return failure;
            }
            return success;
        }

        /// <summary>Critère de la compétence dans les textes du client (<c>skills</c>), ou <c>null</c>.</summary>
        public static string Criterion(short skillId)
        {
            IReadOnlyDictionary<string, string> skill = LangData.Raw("skills", "competence", skillId.ToString(CultureInfo.InvariantCulture));
            return skill != null && skill.TryGetValue("condition", out string condition) ? condition : null;
        }
    }

    /// <summary>
    /// Enclos de la carte annoncé par <c>Rp&lt;propriétaire&gt;;&lt;prix&gt;;&lt;taille&gt;;&lt;objets&gt;;&lt;guilde&gt;;&lt;blason&gt;</c>
    /// (<c>Mount.onMountPark</c>). Le préfixe <c>Rp</c> appartient au lot des montures : il appelle
    /// <see cref="InteractiveActions.SetMountPark"/> ; le menu de l'enclos s'en sert.
    /// </summary>
    public sealed class MountParkInfo
    {
        public int OwnerId { get; private set; }
        public int Price { get; private set; }
        public int Size { get; private set; }
        public int Items { get; private set; }
        public string GuildName { get; private set; } = string.Empty;
        public string GuildEmblem { get; private set; } = string.Empty;
        /// <summary>Propriétaire -1 : enclos public.</summary>
        public bool IsPublic => OwnerId == -1;
        /// <summary>Propriétaire 0 : enclos à vendre sans propriétaire.</summary>
        public bool HasNoOwner => OwnerId == 0;
        /// <summary>Enclos de la guilde du personnage (<c>isMine</c> compare les noms de guilde).</summary>
        public bool IsMine(string playerGuild) => !string.IsNullOrEmpty(GuildName) && string.Equals(GuildName, playerGuild, StringComparison.Ordinal);

        public static bool TryParse(string payload, out MountParkInfo info)
        {
            info = null;
            string[] fields = (payload ?? string.Empty).Split(';');
            if (fields.Length < 4 || !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int owner)
                || !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int price)
                || !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int size)
                || !int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int items)) return false;
            info = new MountParkInfo
            {
                OwnerId = owner, Price = price, Size = size, Items = items,
                GuildName = fields.Length > 4 ? fields[4] : string.Empty, GuildEmblem = fields.Length > 5 ? fields[5] : string.Empty
            };
            return true;
        }

        /// <summary>Texte affiché au-dessus de l'enclos par le client (public, à vendre, privé).</summary>
        public string Describe()
        {
            string Text(string key, string fallback, params string[] args) => LangData.Text.Has(key) ? LangData.Text.Get(key, args).Trim() : fallback;
            if (IsPublic) return Text("MOUNTPARK_PUBLIC", "Enclos public");
            if (HasNoOwner) return Text("MOUNTPARK_TO_BUY", "Enclos à vendre : " + Price + " kamas, " + Size + " places, " + Items + " objets",
                Price.ToString(CultureInfo.InvariantCulture), Size.ToString(CultureInfo.InvariantCulture), Items.ToString(CultureInfo.InvariantCulture));
            string state = Price > 0 ? Text("MOUNTPARK_PRIVATE_TO_BUY", "Enclos privé à vendre : " + Price + " kamas", Price.ToString(CultureInfo.InvariantCulture))
                : Text("MOUNTPARK_PRIVATE", "Enclos privé");
            return GuildName.Length == 0 ? state : GuildName + " · " + state;
        }
    }

    /// <summary>
    /// Utilisation des objets interactifs comme <c>GameManager.useRessource</c> du client 1.34 : déplacement à portée
    /// (<c>onCellRelease</c>) puis <c>GA500&lt;cellule&gt;;&lt;compétence&gt;</c> ; anti-spam de 800 ms (<c>CLICK_MIN_DELAY</c>).
    /// <c>GA&lt;id&gt;;501;&lt;acteur&gt;;&lt;cellule&gt;,&lt;durée&gt;</c> ouvre l'action (état « récolte », image 2) et le bot
    /// acquitte par <c>GKK&lt;id&gt;</c> à la fin de la durée annoncée (StarLoco expulse un acquittement trop précoce).
    /// <c>IQ&lt;sprite&gt;|&lt;quantité&gt;</c> et <c>IO&lt;sprite&gt;|±&lt;objet&gt;</c> donnent le résultat. Fenêtres associées :
    /// <see cref="Code"/> (<c>KC</c>/<c>KK</c>/<c>KV</c>) et <see cref="Document"/> (<c>dC</c>/<c>dV</c>). Le bot n'émet jamais
    /// <c>GA034</c> : StarLoco le traite comme <c>GA300</c> ; une pancarte s'ouvre en marchant sur sa cellule (<c>dCK</c>).
    /// </summary>
    public sealed class InteractiveActions : InteractionWindow
    {
        /// <summary><c>dofus.Constants.CLICK_MIN_DELAY</c>.</summary>
        public const int ClickMinDelayMs = 800;
        /// <summary>Métier de pêcheur : la canne équipée fixe la portée (<c>JobConstant.getDistCanne</c> de StarLoco).</summary>
        public const int FishingJob = 36;

        private readonly object sync = new object();
        private InteractionClock clock = InteractionClock.Default;
        private long? lastUseAt;
        private PendingUse pending;
        private InteractiveActionInfo current;
        private CancellationTokenSource acknowledgement;
        private short requestedCell = -1, requestedSkill;
        private MountParkInfo mountPark;

        public KeyCodeDialog Code { get; }
        public DocumentDialog Document { get; }
        /// <summary>Horloge de l'anti-spam et de l'acquittement (remplaçable pour les tests).</summary>
        public InteractionClock Clock { get => clock; set => clock = value ?? InteractionClock.Default; }
        /// <summary>Action en cours du personnage, ou <c>null</c>.</summary>
        public InteractiveActionInfo Current { get { lock (sync) return current; } }
        /// <summary>Utilisation en attente de la fin du déplacement (cellule, compétence), ou <c>null</c>.</summary>
        public Tuple<short, short> WaitingUse { get { lock (sync) return pending == null ? null : Tuple.Create(pending.Cell, pending.Skill); } }
        /// <summary>Dernière quantité reçue pour le personnage (<c>IQ</c>), -1 avant la première.</summary>
        public int LastQuantity { get; private set; } = -1;
        /// <summary>Enclos de la carte (<c>Rp</c>), ou <c>null</c> tant que le lot des montures ne l'a pas transmis.</summary>
        public MountParkInfo MountPark { get { lock (sync) return mountPark; } }

        /// <summary>Action commencée (personnage du compte ou autre joueur de la carte).</summary>
        public event Action<InteractiveActionInfo> ActionStarted;
        /// <summary>Action du personnage acquittée par <c>GKK</c>.</summary>
        public event Action<InteractiveActionInfo> ActionFinished;
        /// <summary>Utilisation abandonnée avant l'envoi de <c>GA500</c> (déplacement refusé ou interrompu) : cellule visée.</summary>
        public event Action<short> UseAbandoned;
        /// <summary><c>IQ</c> : sprite et quantité.</summary>
        public event Action<long, int> QuantityReceived;
        /// <summary><c>IO</c> : sprite, réussite, modèle d'objet.</summary>
        public event Action<long, bool, int> ObjectResultReceived;

        protected override string Reference => "INTERACTIF";
        protected override AccountStates OpenState => AccountStates.GATHERING;

        internal InteractiveActions(Accounts.Accounts account) : base(account)
        {
            Code = new KeyCodeDialog(account);
            Document = new DocumentDialog(account);
        }

        /// <summary>Portée d'utilisation : canne à pêche équipée pour une compétence de pêcheur, sinon une case.</summary>
        public static int Reach(CharacterClass character, short skillId)
        {
            if (character == null || LangData.Skill.Job(skillId) != FishingJob) return 1;
            InventoryObjects weapon = character.Inventory?.GetObjetsPosition(InventorySlots.WEAPON);
            return weapon == null ? 1 : Math.Max(1, (int)ToolDistance(weapon.ID));
        }

        /// <summary>Portée des cannes à pêche par modèle, comme <c>JobConstant.getDistCanne</c> de StarLoco (1 sinon).</summary>
        public static byte ToolDistance(int itemTemplateId)
        {
            switch (itemTemplateId)
            {
                case 8541: case 6661: case 596: return 2;
                case 1866: return 3;
                case 1865: case 1864: return 4;
                case 1867: case 2188: return 5;
                case 1863: case 1862: return 6;
                case 1868: return 7;
                case 1861: case 1860: return 8;
                case 2366: return 9;
                default: return 1;
            }
        }

        /// <summary>
        /// Utilise la compétence sur l'objet de la cellule. À portée, <c>GA500&lt;cellule&gt;;&lt;compétence&gt;</c> part tout de
        /// suite ; sinon le personnage marche (chemin calculé hors du thread appelant) et l'action part à l'arrivée. Pendant
        /// une marche déjà en cours, la demande est mise en file et rejouée à la fin de cette marche (le serveur met lui aussi
        /// en file un <c>GA500</c> reçu pendant une marche) ; une nouvelle marche ou une marche interrompue l'annule.
        /// </summary>
        public Task<InteractionResult> UseAsync(short cellId, short skillId) => UseCoreAsync(cellId, skillId, null);

        private async Task<InteractionResult> UseCoreAsync(short cellId, short skillId, PendingUse replay)
        {
            if (replay == null) DropStalePending();
            InteractionResult refused = CheckCanOpen();
            // Seule une marche en cours (et rien d'autre) permet de mettre la demande en file.
            bool afterWalk = refused != null && replay == null && Account?.Connexion != null && Account.Connexion.IsConnected()
                && Account.IsMoving() && !IsOpen;
            if (refused != null && !afterWalk) return refused;
            GameClass game = Account.Game;
            Map map = game?.Map;
            CharacterClass character = game?.character;
            if (map == null || character?.Cell == null) return Refuse("La carte ou la position du personnage n'est pas encore connue.");
            if (!map.Interactives.TryGetValue(cellId, out Interactives target) || target?.Cell == null)
                return Refuse("Aucun objet interactif sur la cellule " + cellId + ".");
            if (!target.HasSkill(skillId)) return Refuse("« " + LangData.Skill.Name(skillId) + " » n'est pas une compétence de " + target.Name + ".");
            if (!target.IsUsable) return Refuse(target.Name + " n'est pas utilisable pour l'instant.");
            Mouvement movement = game.Manager?.Mouvements;
            if (replay == null)
            {
                lock (sync)
                {
                    long now = clock.NowMs;
                    if (lastUseAt.HasValue && now - lastUseAt.Value < ClickMinDelayMs)
                        return Refuse(LangData.Text.Has("SRV_MSG_0") ? LangData.Text.Get("SRV_MSG_0").Trim() : "Trop de clics : attendez avant de réessayer.");
                    if (pending != null) return Refuse("Une utilisation attend déjà la fin du déplacement.");
                    lastUseAt = now;
                }
            }
            if (afterWalk)
            {
                if (movement == null) return Refuse("Déplacement indisponible.");
                Watch(new PendingUse(cellId, skillId, map.MapID, walkedToTarget: false), movement);
                return new InteractionResult(true, "« " + LangData.Skill.Name(skillId) + " » sera demandé sur " + target.Name + " à la fin du déplacement.");
            }
            int reach = Reach(character, skillId);
            if (character.Cell.GetDistanceBetweenCells(target.Cell) <= reach) return await SendUseAsync(target, skillId).ConfigureAwait(false);
            if (replay != null && replay.WalkedToTarget) return Refuse(target.Name + " est encore hors de portée à l'arrivée.");

            if (movement == null) return Refuse("Déplacement indisponible.");
            var use = new PendingUse(cellId, skillId, map.MapID, walkedToTarget: true);
            Watch(use, movement);
            MoveResults result;
            try
            {
                result = await Task.Run(() => movement.GetCellsMove(target.Cell, map.CellsOccuped(), true, (byte)Math.Min(reach, byte.MaxValue))).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                Abandon(use, movement);
                Account?.Logger?.LogException(Reference, error);
                return Refuse("Déplacement impossible : " + error.Message);
            }
            switch (result)
            {
                case MoveResults.EXIT:
                    return new InteractionResult(true, "Déplacement vers " + target.Name + " ; « " + LangData.Skill.Name(skillId) + " » sera demandé à l'arrivée.");
                case MoveResults.SAMECELL:
                    Abandon(use, movement);
                    return await SendUseAsync(target, skillId).ConfigureAwait(false);
                default:
                    Abandon(use, movement);
                    return Refuse("Aucun chemin vers " + target.Name + " (" + result + ").");
            }
        }

        /// <summary>Retient l'utilisation jusqu'à la fin de la marche (<see cref="Mouvement.FinalizeMove"/>).</summary>
        private void Watch(PendingUse use, Mouvement movement)
        {
            use.Handler = ok => OnMoveFinished(use, movement, ok);
            lock (sync) pending = use;
            movement.FinalizeMove += use.Handler;
        }

        /// <summary>Transmis par le lot des montures à la réception de <c>Rp</c> (le menu de l'enclos s'en sert).</summary>
        public void SetMountPark(MountParkInfo info)
        {
            lock (sync) mountPark = info;
            Notify();
        }

        private async Task<InteractionResult> SendUseAsync(Interactives target, short skillId)
        {
            lock (sync) { requestedCell = target.Cell.CellID; requestedSkill = skillId; }
            return await SendAsync("GA500" + target.Cell.CellID.ToString(CultureInfo.InvariantCulture) + ";" + skillId.ToString(CultureInfo.InvariantCulture),
                "« " + LangData.Skill.Name(skillId) + " » demandé sur " + target.Name + ".").ConfigureAwait(false);
        }

        private void OnMoveFinished(PendingUse use, Mouvement movement, bool success)
        {
            movement.FinalizeMove -= use.Handler;
            lock (sync)
            {
                if (!ReferenceEquals(pending, use)) return;
                pending = null;
            }
            Map map = Account?.Game?.Map;
            if (!success || map == null || map.MapID != use.MapId || !map.Interactives.TryGetValue(use.Cell, out Interactives target) || target?.Cell == null)
            {
                LogError("Déplacement interrompu : l'objet de la cellule " + use.Cell + " n'est pas utilisé.");
                UseAbandoned?.Invoke(use.Cell);
                Notify();
                return;
            }
            _ = ReplayAsync(use);
        }

        /// <summary>Fin de la marche : la demande est rejouée (portée vérifiée de nouveau, sans anti-spam).</summary>
        private async Task ReplayAsync(PendingUse use)
        {
            try
            {
                InteractionResult result = await UseCoreAsync(use.Cell, use.Skill, use).ConfigureAwait(false);
                if (!result.Sent) { LogError(result.Message); UseAbandoned?.Invoke(use.Cell); }
                else Log(result.Message);
            }
            catch (Exception error) { Account?.Logger?.LogException(Reference, error); }
            Notify();
        }

        private void Abandon(PendingUse use, Mouvement movement)
        {
            movement.FinalizeMove -= use.Handler;
            lock (sync) if (ReferenceEquals(pending, use)) pending = null;
        }

        /// <summary>Un déplacement terminé sans <c>FinalizeMove</c> (changement de carte) ne doit pas bloquer les utilisations suivantes.</summary>
        private void DropStalePending()
        {
            PendingUse stale;
            lock (sync)
            {
                stale = pending;
                if (stale == null || (Account != null && Account.IsMoving() && Account.Game?.Map?.MapID == stale.MapId)) return;
                pending = null;
            }
            Mouvement movement = Account?.Game?.Manager?.Mouvements;
            if (movement != null) movement.FinalizeMove -= stale.Handler;
        }

        /// <summary><c>GA&lt;id&gt;;501;&lt;acteur&gt;;&lt;cellule&gt;,&lt;durée&gt;[,…]</c> (<c>GameActions.onActions</c>, action 501).</summary>
        internal void OnActionStarted(GameActionPacket packet)
        {
            CharacterClass character = Account?.Game?.character;
            if (packet == null || character == null) return;
            string[] parameters = (packet.Parameters ?? string.Empty).Split(',');
            if (parameters.Length < 2 || !short.TryParse(parameters[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out short cellId) || cellId < 0
                || !int.TryParse(parameters[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int duration) || duration < 0)
            {
                LogError("Action 501 illisible : " + Shorten(packet.Raw));
                Notify();
                return;
            }
            long actor = packet.ActorId ?? (packet.Actor.Length == 0 ? character.id : long.MinValue);
            if (actor == long.MinValue) { LogError("Acteur de l'action 501 illisible : " + Shorten(packet.Raw)); return; }
            Map map = Account.Game.Map;
            if (map != null && map.Interactives.TryGetValue(cellId, out Interactives target) && target != null)
                target.ApplyState(new InteractiveObjectState(cellId, (int)InteractiveState.InUse, null));
            var info = new InteractiveActionInfo
            {
                GameActionId = packet.GameActionId ?? string.Empty, ActorId = actor, IsSelf = actor == character.id,
                CellId = cellId, DurationMs = duration, StartedAtMs = clock.NowMs
            };
            if (!info.IsSelf)
            {
                Raise(ActionStarted, info);
                return;
            }
            CancellationTokenSource cancellation = new CancellationTokenSource(), previous;
            lock (sync)
            {
                info.SkillId = requestedCell == cellId ? requestedSkill : (short)0;
                previous = acknowledgement;
                acknowledgement = cancellation;
                current = info;
            }
            if (previous != null) { previous.Cancel(); previous.Dispose(); }
            MarkOpen();
            Log("Action sur la cellule " + cellId + " pendant " + duration + " ms.");
            Notify();
            Raise(ActionStarted, info);
            if (!int.TryParse(info.GameActionId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int gameActionId) || gameActionId < 0)
            {
                LogError("Action 501 sans identifiant : aucun GKK ne peut l'acquitter.");
                return;
            }
            _ = AcknowledgeAsync(info, gameActionId, cancellation.Token, Account.Connexion);
        }

        private async Task AcknowledgeAsync(InteractiveActionInfo info, int gameActionId, CancellationToken token, Network.TcpClient connection)
        {
            try
            {
                await clock.Delay(info.DurationMs, token).ConfigureAwait(false);
                if (token.IsCancellationRequested || connection == null || !ReferenceEquals(connection, Account?.Connexion) || !connection.IsConnected()) return;
                await connection.SendPacket("GKK" + gameActionId.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception error)
            {
                Account?.Logger?.LogException(Reference, error);
                return;
            }
            lock (sync)
            {
                if (!ReferenceEquals(current, info)) return;
                current = null;
            }
            MarkClosed();
            Log("Action terminée sur la cellule " + info.CellId + ".");
            Notify();
            Raise(ActionFinished, info);
        }

        /// <summary><c>IQ&lt;sprite&gt;|&lt;quantité&gt;</c> : quantité récoltée affichée au-dessus du sprite.</summary>
        internal void OnQuantity(string payload)
        {
            string[] fields = (payload ?? string.Empty).Split('|');
            if (fields.Length < 2 || !long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long sprite)
                || !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int quantity))
            {
                LogError("Quantité illisible : IQ" + Shorten(payload));
                Notify();
                return;
            }
            if (sprite == (Account?.Game?.character?.id ?? long.MinValue))
            {
                LastQuantity = quantity;
                Log("Récolte : " + quantity + " ressource(s).");
                Notify();
            }
            try { QuantityReceived?.Invoke(sprite, quantity); }
            catch (Exception error) { Account?.Logger?.LogException(Reference, error); }
        }

        /// <summary><c>IO&lt;sprite&gt;|+&lt;objet&gt;</c> (réussite) ou <c>|-&lt;objet&gt;</c> (échec), affiché 2 s par le client.</summary>
        internal void OnObjectResult(string payload)
        {
            string[] fields = (payload ?? string.Empty).Split('|');
            if (fields.Length < 2 || fields[1].Length < 2 || (fields[1][0] != '+' && fields[1][0] != '-')
                || !long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long sprite)
                || !int.TryParse(fields[1].Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int template))
            {
                LogError("Résultat d'objet illisible : IO" + Shorten(payload));
                Notify();
                return;
            }
            bool success = fields[1][0] == '+';
            if (sprite == (Account?.Game?.character?.id ?? long.MinValue))
            {
                Log((success ? "Réussite : " : "Échec : ") + LangData.Item.Name(template) + ".");
                Notify();
            }
            try { ObjectResultReceived?.Invoke(sprite, success, template); }
            catch (Exception error) { Account?.Logger?.LogException(Reference, error); }
        }

        private void Raise(Action<InteractiveActionInfo> handler, InteractiveActionInfo info)
        {
            try { handler?.Invoke(info); }
            catch (Exception error) { Account?.Logger?.LogException(Reference, error); }
        }

        protected override void Reset()
        {
            CancellationTokenSource previous;
            PendingUse stale;
            lock (sync)
            {
                previous = acknowledgement; acknowledgement = null;
                stale = pending; pending = null;
                current = null; lastUseAt = null; mountPark = null;
                requestedCell = -1; requestedSkill = 0;
            }
            if (previous != null) { previous.Cancel(); previous.Dispose(); }
            Mouvement movement = Account?.Game?.Manager?.Mouvements;
            if (stale != null && movement != null) movement.FinalizeMove -= stale.Handler;
            LastQuantity = -1;
            Code.Clear();
            Document.Clear();
        }

        private static string Shorten(string text) => text == null ? string.Empty : text.Length <= 80 ? text : text.Substring(0, 80) + "…";

        private sealed class PendingUse
        {
            public PendingUse(short cell, short skill, int mapId, bool walkedToTarget)
            {
                Cell = cell; Skill = skill; MapId = mapId; WalkedToTarget = walkedToTarget;
            }
            public short Cell { get; }
            public short Skill { get; }
            public int MapId { get; }
            /// <summary>Vrai si la marche menait à l'objet ; faux pour une demande mise en file pendant une autre marche.</summary>
            public bool WalkedToTarget { get; }
            public Action<bool> Handler { get; set; }
        }
    }
}
