using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Tool_BotProtocol.Game.Exchanges
{
    /// <summary>Demande d'échange en attente (<c>ERK&lt;demandeur&gt;|&lt;cible&gt;|&lt;type&gt;</c>).</summary>
    public sealed class ExchangeRequest
    {
        internal ExchangeRequest(int requesterId, int targetId, int type, bool outgoing, string partnerName)
        {
            RequesterId = requesterId; TargetId = targetId; Type = type; Outgoing = outgoing; PartnerName = partnerName ?? string.Empty;
        }
        public int RequesterId { get; }
        public int TargetId { get; }
        /// <summary>Type demandé : 1 pour un échange entre joueurs (12/13 : artisanat sécurisé, refusé par le bot).</summary>
        public int Type { get; }
        /// <summary>Vrai quand le personnage du compte a fait la demande (il attend la réponse de l'autre joueur).</summary>
        public bool Outgoing { get; }
        public int PartnerId => Outgoing ? TargetId : RequesterId;
        public string PartnerName { get; }
    }

    /// <summary>
    /// Demande d'échange reçue d'un autre joueur. Un abonné qui présentera la question au joueur met <see cref="Handled"/>
    /// à vrai et répondra plus tard (<see cref="PlayerExchange.AcceptAsync"/>, <see cref="PlayerExchange.RefuseAsync"/>) ;
    /// sans abonné, le bot refuse poliment (<c>EV</c>) : il n'accepte jamais seul.
    /// </summary>
    public sealed class ExchangeRequestEventArgs : EventArgs
    {
        internal ExchangeRequestEventArgs(ExchangeRequest request) { Request = request; }
        public ExchangeRequest Request { get; }
        public bool Handled { get; set; }
    }

    /// <summary>
    /// Échange entre joueurs (type 1) ou avec un PNJ (type 2), selon <c>dofus.aks.Exchange</c> et l'interface <c>Exchange</c>
    /// du client 1.34 : demande <c>ER1|&lt;joueur&gt;</c> (ou <c>ER2|&lt;pnj&gt;</c>), réponse <c>ERK&lt;demandeur&gt;|&lt;cible&gt;|1</c>,
    /// acceptation <c>EA</c> ou refus <c>EV</c>, ouverture <c>ECK1</c> ; mouvements <c>EMO+&lt;objet&gt;|&lt;quantité&gt;</c>,
    /// <c>EMO-&lt;objet&gt;|&lt;quantité&gt;</c>, <c>EMG&lt;kamas&gt;</c> confirmés par <c>EMK…</c> (son côté) et <c>EmK…</c> (l'autre côté) ;
    /// validation <c>EK</c> (bouton actif 3 s après la dernière modification, <c>DELAY_BEFORE_VALIDATE</c>), états <c>EK&lt;0|1&gt;&lt;id&gt;</c> ;
    /// fin <c>EV</c> (annulé) ou <c>EVa</c> (effectué). Aucun état local ne change avant la réponse du serveur.
    /// </summary>
    [ExchangeType(ExchangeTypes.Player)]
    [ExchangeType(ExchangeTypes.NpcExchange)]
    public sealed class PlayerExchange : ExchangeWindow
    {
        /// <summary>Délai du client entre la dernière modification d'un échange et l'activation du bouton de validation.</summary>
        public static readonly TimeSpan ValidationDelay = TimeSpan.FromMilliseconds(3000);

        private readonly object sync = new object();
        private readonly HashSet<string> ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<ExchangeItem> local = new List<ExchangeItem>(), distant = new List<ExchangeItem>();
        private long lastChange = Stopwatch.GetTimestamp();
        private ExchangeRequest pending;

        internal PlayerExchange(Accounts.Accounts account) : base(account) { }

        protected override string Reference => "ÉCHANGE";
        protected override AccountStates OpenState => AccountStates.EXCHANGE;

        /// <summary>Demande en attente (envoyée ou reçue), <c>null</c> sinon.</summary>
        public ExchangeRequest PendingRequest { get { lock (sync) return pending; } }
        public int PartnerId { get; private set; }
        public string PartnerName { get; private set; } = string.Empty;
        /// <summary>Vrai pour un échange avec un PNJ (type 2).</summary>
        public bool PartnerIsNpc { get; private set; }
        public IReadOnlyList<ExchangeItem> LocalItems { get { lock (sync) return local.Select(item => item.Copy()).ToArray(); } }
        public IReadOnlyList<ExchangeItem> DistantItems { get { lock (sync) return distant.Select(item => item.Copy()).ToArray(); } }
        public long LocalKamas { get; private set; }
        public long DistantKamas { get; private set; }
        /// <summary>État « prêt » du personnage, tel que le serveur l'annonce (<c>EK1&lt;soi&gt;</c>).</summary>
        public bool LocalReady { get; private set; }
        public bool DistantReady { get; private set; }
        /// <summary>Temps restant avant que la validation soit permise (zéro quand elle l'est).</summary>
        public TimeSpan ValidationWait
        {
            get
            {
                long elapsed = (Stopwatch.GetTimestamp() - Volatile.Read(ref lastChange)) * 1000L / Stopwatch.Frequency;
                long remaining = (long)ValidationDelay.TotalMilliseconds - elapsed;
                return remaining > 0 ? TimeSpan.FromMilliseconds(remaining) : TimeSpan.Zero;
            }
        }
        public bool CanValidate => IsOpen && ValidationWait == TimeSpan.Zero;
        /// <summary>Joueurs ignorés pour la session (« Ignorer » de la demande) : leurs demandes suivantes sont refusées d'office.</summary>
        public IReadOnlyCollection<string> IgnoredPlayers { get { lock (sync) return ignored.ToArray(); } }

        /// <summary>Levé sur le fil de réception réseau quand un joueur propose un échange au personnage.</summary>
        public event EventHandler<ExchangeRequestEventArgs> RequestReceived;

        /// <summary>Envoie <c>ER1|&lt;joueur&gt;</c> ; StarLoco répond <c>ERK&lt;soi&gt;|&lt;joueur&gt;|1</c> aux deux joueurs.</summary>
        public Task<InteractionResult> RequestAsync(int playerId)
        {
            InteractionResult refused = CheckCanRequest();
            if (refused != null) return Task.FromResult(refused);
            int self = Account?.Game?.character?.id ?? 0;
            if (playerId <= 0 || playerId == self) return Task.FromResult(Refuse("Choisissez un autre joueur de la carte."));
            return SendAsync("ER" + ExchangeTypes.Player + "|" + playerId.ToString(CultureInfo.InvariantCulture),
                "Demande d'échange envoyée à " + ResolveName(playerId, false) + " ; en attente de sa réponse.");
        }

        /// <summary>Envoie <c>ER2|&lt;pnj&gt;</c> (action « Échanger » d'un PNJ) ; StarLoco ouvre par <c>ECK2|&lt;pnj&gt;</c>.</summary>
        public Task<InteractionResult> RequestNpcAsync(int npcId)
        {
            InteractionResult refused = CheckCanRequest();
            if (refused != null) return Task.FromResult(refused);
            return SendAsync("ER" + ExchangeTypes.NpcExchange + "|" + npcId.ToString(CultureInfo.InvariantCulture),
                "Échange demandé à " + ResolveName(npcId, true) + ".");
        }

        /// <summary>Envoie <c>EA</c> pour accepter la demande reçue ; l'échange s'ouvre sur <c>ECK1</c>.</summary>
        public Task<InteractionResult> AcceptAsync()
        {
            ExchangeRequest request = PendingRequest;
            if (request == null || request.Outgoing) return Task.FromResult(Refuse("Aucune demande d'échange à accepter."));
            return SendAsync("EA", "Échange avec " + request.PartnerName + " accepté ; ouverture par le serveur.");
        }

        /// <summary>
        /// Envoie <c>EV</c> pour refuser la demande reçue. StarLoco ne renvoie rien au joueur qui refuse : la demande est
        /// oubliée localement dès l'envoi.
        /// </summary>
        public Task<InteractionResult> RefuseAsync() => DropRequestAsync(false);

        /// <summary>« Ignorer » du client : le joueur rejoint la liste noire temporaire (session) puis la demande est refusée (<c>EV</c>).</summary>
        public Task<InteractionResult> IgnoreAsync() => DropRequestAsync(true);

        /// <summary>Envoie <c>EMO+&lt;objet&gt;|&lt;quantité&gt;</c> : ajoute une quantité d'un objet du sac à sa proposition.</summary>
        public Task<InteractionResult> AddItemAsync(uint inventoryId, int quantity)
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun échange ouvert."));
            InventoryObjects item = Account?.Game?.character?.Inventory?.GetByInventoryId(inventoryId);
            if (item == null) return Task.FromResult(Refuse("Cet objet n'est pas dans votre inventaire."));
            if (item.IsEquipped()) return Task.FromResult(Refuse("Déséquipez cet objet avant de le proposer."));
            int offered;
            lock (sync) offered = local.Where(entry => entry.Id == inventoryId).Select(entry => entry.Quantity).FirstOrDefault();
            int available = item.Qua - offered;
            if (available <= 0) return Task.FromResult(Refuse("Tout l'exemplaire est déjà proposé."));
            if (quantity <= 0 || quantity > available) return Task.FromResult(Refuse("Quantité invalide (1 à " + available + ")."));
            return SendAsync("EMO+" + inventoryId.ToString(CultureInfo.InvariantCulture) + "|" + quantity.ToString(CultureInfo.InvariantCulture),
                "Ajout de " + quantity + " × " + item.Name + " demandé.");
        }

        /// <summary>Envoie <c>EMO-&lt;objet&gt;|&lt;quantité&gt;</c> : retire une quantité d'un objet de sa proposition.</summary>
        public Task<InteractionResult> RemoveItemAsync(uint inventoryId, int quantity)
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun échange ouvert."));
            ExchangeItem offered;
            lock (sync) offered = local.FirstOrDefault(entry => entry.Id == inventoryId)?.Copy();
            if (offered == null) return Task.FromResult(Refuse("Cet objet n'est pas dans votre proposition."));
            if (quantity <= 0 || quantity > offered.Quantity) return Task.FromResult(Refuse("Quantité invalide (1 à " + offered.Quantity + ")."));
            return SendAsync("EMO-" + inventoryId.ToString(CultureInfo.InvariantCulture) + "|" + quantity.ToString(CultureInfo.InvariantCulture),
                "Retrait de " + quantity + " × " + offered.Name + " demandé.");
        }

        /// <summary>Envoie <c>EMG&lt;kamas&gt;</c> : montant total proposé, ramené aux kamas du personnage comme le client.</summary>
        public Task<InteractionResult> SetKamasAsync(long kamas)
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun échange ouvert."));
            if (kamas < 0) return Task.FromResult(Refuse("Montant de kamas invalide."));
            long owned = Math.Max(0, Account?.Game?.character?.Kamas ?? 0);
            if (kamas > owned) kamas = owned;
            return SendAsync("EMG" + kamas.ToString(CultureInfo.InvariantCulture), kamas + " kamas proposés.");
        }

        /// <summary>
        /// Envoie <c>EK</c> : valide (ou retire sa validation, le serveur basculant l'état). Refusé pendant les 3 secondes
        /// qui suivent une modification, comme le bouton du client.
        /// </summary>
        public Task<InteractionResult> ValidateAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun échange ouvert."));
            TimeSpan wait = ValidationWait;
            if (wait > TimeSpan.Zero)
                return Task.FromResult(Refuse("L'échange vient de changer : validation possible dans " + Math.Ceiling(wait.TotalSeconds) + " s."));
            return SendAsync("EK", LocalReady ? "Validation retirée ; le serveur confirme." : "Validation envoyée ; le serveur confirme.");
        }

        /// <summary>
        /// Envoie <c>EV</c>. Échange ouvert : la fermeture attend le <c>EV</c> du serveur. Demande en attente : elle est annulée
        /// et oubliée dès l'envoi, StarLoco ne répondant qu'à l'autre joueur.
        /// </summary>
        public Task<InteractionResult> LeaveAsync()
        {
            if (IsOpen) return SendAsync("EV", "Annulation de l'échange demandée.");
            if (PendingRequest != null) return DropRequestAsync(false);
            return Task.FromResult(Refuse("Aucun échange ouvert."));
        }

        /// <summary><c>ERK&lt;demandeur&gt;|&lt;cible&gt;|&lt;type&gt;</c> (<c>Exchange.onRequest</c>).</summary>
        internal async Task OnRequestAsync(string payload)
        {
            string[] fields = (payload ?? string.Empty).Split('|');
            if (fields.Length < 3 || !TryInt(fields[0], out int requester) || !TryInt(fields[1], out int target) || !TryInt(fields[2], out int type))
            {
                Malformed("ERK", payload);
                return;
            }
            int self = Account?.Game?.character?.id ?? 0;
            if (requester == self)
            {
                var outgoing = new ExchangeRequest(requester, target, type, true, ResolveName(target, false));
                lock (sync) pending = outgoing;
                Log(Lang("WAIT_FOR_EXCHANGE", "En attente de la réponse de " + outgoing.PartnerName + "…", outgoing.PartnerName));
                Notify();
                return;
            }
            if (target != self)
            {
                Account?.Logger?.LogDebug(Reference, "Demande d'échange entre deux autres joueurs ignorée : ERK" + payload);
                return;
            }
            var incoming = new ExchangeRequest(requester, target, type, false, ResolveName(requester, false));
            lock (sync) pending = incoming;
            if (type != ExchangeTypes.Player)
            {
                Log("Demande « " + ExchangeTypes.Describe(type) + " » de " + incoming.PartnerName + " refusée : le bot ne la prend pas en charge.");
                await DropRequestAsync(false).ConfigureAwait(false);
                return;
            }
            bool isIgnored;
            lock (sync) isIgnored = ignored.Contains(incoming.PartnerName);
            if (isIgnored)
            {
                Log("Demande d'échange de " + incoming.PartnerName + " refusée : joueur ignoré pour la session.");
                await DropRequestAsync(false).ConfigureAwait(false);
                return;
            }
            Log(Lang("A_WANT_EXCHANGE", incoming.PartnerName + " vous propose un échange.", incoming.PartnerName));
            var args = new ExchangeRequestEventArgs(incoming);
            RaiseRequest(args);
            Notify();
            if (args.Handled) return;
            Log("Demande d'échange de " + incoming.PartnerName + " refusée : le bot n'accepte pas d'échange automatiquement.");
            await DropRequestAsync(false).ConfigureAwait(false);
        }

        /// <summary><c>ERE</c> après une demande : elle est abandonnée.</summary>
        internal void OnRequestRefused(string reason)
        {
            bool had;
            lock (sync) { had = pending != null; pending = null; }
            if (!had) return;
            LogError(reason);
            Notify();
        }

        protected override void HandleCreated(int type, string data)
        {
            ExchangeRequest request;
            lock (sync) { request = pending; pending = null; }
            PartnerIsNpc = type == ExchangeTypes.NpcExchange;
            int partner;
            if (TryInt(data.Split('|')[0], out int announced)) partner = announced;
            else partner = request?.PartnerId ?? 0;
            PartnerId = partner;
            PartnerName = partner == 0 ? (PartnerIsNpc ? "PNJ inconnu" : "joueur inconnu") : ResolveName(partner, PartnerIsNpc);
            Touch();
            MarkOpen();
            Log("Échange ouvert avec " + PartnerName + ".");
            Notify();
        }

        /// <summary><c>EMKO+&lt;objet&gt;|&lt;quantité&gt;</c>, <c>EMKO-&lt;objet&gt;</c>, <c>EMKG&lt;kamas&gt;</c> (<c>Exchange.modifyLocal</c>).</summary>
        protected override void HandleLocalMovement(string data)
        {
            if (!ApplyMovement(data, true)) Malformed("EMK", data);
        }

        /// <summary><c>EmKO+&lt;objet&gt;|&lt;quantité&gt;|&lt;modèle&gt;|&lt;effets&gt;</c>, <c>EmKO-&lt;objet&gt;</c>, <c>EmKG&lt;kamas&gt;</c> (<c>Exchange.modifyDistant</c>).</summary>
        protected override void HandleDistantMovement(string data)
        {
            if (!ApplyMovement(data, false)) Malformed("EmK", data);
        }

        /// <summary><c>EK&lt;0|1&gt;&lt;acteur&gt;</c> : l'acteur du compte est « soi », tout autre (ou aucun, PNJ) est l'autre côté.</summary>
        protected override void HandleReady(string data)
        {
            if (data.Length == 0 || (data[0] != '0' && data[0] != '1')) { Malformed("EK", data); return; }
            bool ready = data[0] == '1';
            int self = Account?.Game?.character?.id ?? 0;
            bool mine = TryInt(data.Substring(1), out int actor) && actor == self && self != 0;
            if (mine) LocalReady = ready; else DistantReady = ready;
            Notify();
        }

        protected override void HandleLeave(string suffix)
        {
            bool wasOpen = IsOpen;
            ExchangeRequest request;
            lock (sync) { request = pending; pending = null; }
            ResetState();
            ResetSides();
            MarkClosed();
            if (wasOpen) Log(suffix == "a" ? Lang("EXCHANGE_OK", "Échange effectué.") : Lang("EXCHANGE_CANCEL", "Échange annulé."));
            else if (request != null) Log(request.Outgoing ? "Demande d'échange refusée ou annulée par " + request.PartnerName + "."
                : "Demande d'échange de " + request.PartnerName + " annulée.");
            Notify();
        }

        protected override void ResetState()
        {
            lock (sync) { local = new List<ExchangeItem>(); distant = new List<ExchangeItem>(); }
            LocalKamas = 0; DistantKamas = 0; LocalReady = false; DistantReady = false;
        }

        protected override void OnClearing()
        {
            lock (sync) pending = null;
            ResetSides();
        }

        private void ResetSides()
        {
            PartnerId = 0; PartnerName = string.Empty; PartnerIsNpc = false;
        }

        private bool ApplyMovement(string data, bool mine)
        {
            if (data.Length < 2) return false;
            if (data[0] == 'G')
            {
                if (!TryLong(data.Substring(1), out long kamas) || kamas < 0) return false;
                if (mine) LocalKamas = kamas; else DistantKamas = kamas;
                Touch();
                Notify();
                return true;
            }
            if (data[0] != 'O' || (data[1] != '+' && data[1] != '-')) return false;
            string[] fields = data.Substring(2).Split('|');
            if (!TryId(fields[0], out uint id)) return false;
            if (data[1] == '-')
            {
                lock (sync)
                {
                    List<ExchangeItem> side = mine ? local : distant;
                    var updated = side.Where(entry => entry.Id != id).ToList();
                    if (mine) local = updated; else distant = updated;
                }
                Touch();
                Notify();
                return true;
            }
            if (fields.Length < 2 || !TryInt(fields[1], out int quantity) || quantity <= 0) return false;
            var item = new ExchangeItem { Id = id, Quantity = quantity };
            if (mine)
            {
                InventoryObjects owned = Account?.Game?.character?.Inventory?.GetByInventoryId(id);
                item.TemplateId = owned?.ID ?? 0;
                item.Effects = owned?.Stats ?? string.Empty;
                item.Name = owned != null ? owned.Name : "Objet " + id;
            }
            else
            {
                if (fields.Length < 3 || !TryInt(fields[2], out int template)) return false;
                item.TemplateId = template;
                item.Effects = fields.Length > 3 ? fields[3] : string.Empty;
                item.Name = InventoryObjects.DisplayName(template);
            }
            lock (sync)
            {
                List<ExchangeItem> side = (mine ? local : distant).ToList();
                int index = side.FindIndex(entry => entry.Id == id);
                if (index >= 0) side[index] = item; else side.Add(item);
                if (mine) local = side; else distant = side;
            }
            Touch();
            Notify();
            return true;
        }

        private InteractionResult CheckCanRequest()
        {
            if (PendingRequest != null) return Refuse("Une demande d'échange est déjà en attente.");
            return CheckCanOpen();
        }

        private async Task<InteractionResult> DropRequestAsync(bool ignore)
        {
            ExchangeRequest request = PendingRequest;
            if (request == null) return Refuse("Aucune demande d'échange en attente.");
            if (ignore && !request.Outgoing)
                lock (sync) ignored.Add(request.PartnerName);
            InteractionResult result = await SendAsync("EV", request.Outgoing ? "Demande d'échange annulée."
                : ignore ? Lang("TEMPORARY_BLACKLISTED", request.PartnerName + " est ignoré jusqu'à la fin de la session.", request.PartnerName)
                : "Demande d'échange de " + request.PartnerName + " refusée.")
                .ConfigureAwait(false);
            if (result.Sent)
            {
                bool cleared;
                lock (sync) { cleared = ReferenceEquals(pending, request); if (cleared) pending = null; }
                if (cleared) { LastMessage = result.Message; Notify(); }
            }
            return result;
        }

        private void RaiseRequest(ExchangeRequestEventArgs args)
        {
            EventHandler<ExchangeRequestEventArgs> handlers = RequestReceived;
            if (handlers == null) return;
            foreach (EventHandler<ExchangeRequestEventArgs> handler in handlers.GetInvocationList())
            {
                try { handler(this, args); }
                catch (Exception error) { Account?.Logger?.LogException(Reference, error); }
            }
        }

        private void Touch() => Volatile.Write(ref lastChange, Stopwatch.GetTimestamp());

        private string ResolveName(int actorId, bool npc)
        {
            var map = Account?.Game?.Map;
            if (map?.Entites != null && map.Entites.TryGetValue(actorId, out Entites entity) && !string.IsNullOrEmpty(entity?.Name))
                return entity.Name;
            return (npc ? "PNJ " : "Joueur ") + actorId.ToString(CultureInfo.InvariantCulture);
        }

        private static string Lang(string key, string fallback, params string[] args) => ExchangeRegistry.Text(key, fallback, args);
    }
}
