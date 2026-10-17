using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Tool_BotProtocol.Game.Exchanges
{
    /// <summary>Service rendu par le PNJ d'un échange de familiers (type 9).</summary>
    public enum PetsService
    {
        /// <summary>Ni la demande envoyée ni les actions du PNJ ne permettent de le savoir.</summary>
        Unknown,
        /// <summary>Garde (action 4, <c>ER9</c>) : un familier contre son certificat de garde, ou l'inverse.</summary>
        Keeping,
        /// <summary>Résurrection (action 7, <c>ER17</c>) : un fantôme de familier et l'objet demandé par le PNJ.</summary>
        Resurrection,
    }

    /// <summary>
    /// Échange avec un PNJ gardien ou ressusciteur de familiers (type 9), lot X4. Le client 1.34 ouvre pour ce type la même
    /// interface <c>Exchange</c> que pour un joueur (<c>Exchange.onCreate</c> : types 2, 9, 17 et 18), avec le PNJ comme autre
    /// côté. Demandes : <c>ER9|&lt;pnj&gt;</c> (garde) ou <c>ER17|&lt;pnj&gt;</c> (résurrection), envoyées par le menu du PNJ ;
    /// StarLoco répond <c>ECK9|&lt;pnj&gt;</c> dans les deux cas et ne transmet aucune liste <c>EL</c>. Envois du client :
    /// <c>EMO+&lt;objet&gt;|&lt;quantité&gt;</c>, <c>EMO-&lt;objet&gt;|&lt;quantité&gt;</c>, <c>EMG&lt;kamas&gt;</c>, <c>EK</c>, <c>EV</c> ;
    /// réceptions : <c>EMKO±</c>/<c>EMKG</c> (son côté), <c>EmKO+&lt;id&gt;|&lt;qté&gt;|&lt;modèle&gt;|&lt;effets&gt;</c>, <c>EmKO-</c>
    /// (proposition du PNJ), <c>EK&lt;0|1&gt;[&lt;acteur&gt;]</c> (sans acteur : le PNJ), fin <c>EVa</c> (effectué) ou <c>EV</c>.
    /// Validation permise 3 secondes après la dernière modification, comme le bouton du client. Aucun état local ne change
    /// avant la réponse du serveur ; les objets reçus arrivent par <c>OAK</c>, <c>OR</c>, <c>OQ</c>, <c>OCO</c> et <c>Im021</c>.
    /// </summary>
    [ExchangeType(ExchangeTypes.Pets)]
    public sealed class PetsExchange : ExchangeWindow
    {
        /// <summary>Délai du client entre la dernière modification et l'activation du bouton de validation.</summary>
        public static readonly TimeSpan ValidationDelay = PlayerExchange.ValidationDelay;
        /// <summary>Types d'objets échangés chez StarLoco : familier (18), certificat de garde (77), fantôme de familier (90).</summary>
        public const int PetType = 18, CertificateType = 77, GhostType = 90;

        private readonly object sync = new object();
        private List<ExchangeItem> local = new List<ExchangeItem>(), distant = new List<ExchangeItem>();
        private long lastChange = Stopwatch.GetTimestamp();
        private int requestedType = -1, requestedNpc;
        private long requestedAt;

        internal PetsExchange(Accounts.Accounts account) : base(account) { }

        protected override string Reference => "FAMILIERS";
        protected override AccountStates OpenState => AccountStates.EXCHANGE;

        /// <summary>Identifiant du PNJ sur la carte (<c>ECK9|&lt;pnj&gt;</c>), 0 s'il n'a pas été transmis.</summary>
        public int NpcId { get; private set; }
        public string NpcName { get; private set; } = string.Empty;
        /// <summary>Garde ou résurrection : d'après la demande envoyée (<c>ER9</c>/<c>ER17</c>), sinon d'après les actions du PNJ.</summary>
        public PetsService Service { get; private set; }
        /// <summary>Contenu brut du dernier <c>EL</c> (StarLoco n'en envoie pas pour ce type), vide sinon.</summary>
        public string RawList { get; private set; } = string.Empty;
        public IReadOnlyList<ExchangeItem> LocalItems { get { lock (sync) return local.Select(item => item.Copy()).ToArray(); } }
        public IReadOnlyList<ExchangeItem> DistantItems { get { lock (sync) return distant.Select(item => item.Copy()).ToArray(); } }
        public long LocalKamas { get; private set; }
        public long DistantKamas { get; private set; }
        /// <summary>Validation du personnage telle que le serveur l'annonce (<c>EK1&lt;soi&gt;</c>).</summary>
        public bool LocalReady { get; private set; }
        /// <summary>Le PNJ accepte la proposition (<c>EK1</c> sans acteur) : StarLoco l'envoie quand le dépôt est complet.</summary>
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

        /// <summary>Texte d'aide du service, à afficher sous la fenêtre.</summary>
        public string ServiceHint
        {
            get
            {
                switch (Service)
                {
                    case PetsService.Keeping: return "Garde : proposez un familier du sac pour recevoir son certificat, ou un certificat pour reprendre le familier.";
                    case PetsService.Resurrection: return "Résurrection : proposez le fantôme du familier et l'objet que demande le PNJ.";
                    default: return "Proposez un familier, un certificat ou un fantôme de familier.";
                }
            }
        }

        /// <summary>
        /// Mémorise la demande envoyée par le menu du PNJ (<c>ER9</c> ou <c>ER17</c>) : le <c>ECK9</c> qui suit est le même
        /// dans les deux cas, seule la demande distingue la garde de la résurrection.
        /// </summary>
        internal void NoteRequest(int requestType, int npcId)
        {
            if (requestType != ExchangeTypes.Pets && requestType != ExchangeTypes.PetsResurrection) return;
            lock (sync) { requestedType = requestType; requestedNpc = npcId; requestedAt = Stopwatch.GetTimestamp(); }
        }

        /// <summary>Envoie <c>EMO+&lt;objet&gt;|&lt;quantité&gt;</c> : propose une quantité d'un objet du sac au PNJ.</summary>
        public Task<InteractionResult> AddItemAsync(uint inventoryId, int quantity)
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun échange de familiers ouvert."));
            InventoryObjects item = Account?.Game?.character?.Inventory?.GetByInventoryId(inventoryId);
            if (item == null) return Task.FromResult(Refuse("Cet objet n'est pas dans votre inventaire."));
            if (item.IsEquipped()) return Task.FromResult(Refuse("Déséquipez cet objet avant de le proposer."));
            int offered;
            lock (sync) offered = local.Where(entry => entry.Id == inventoryId).Select(entry => entry.Quantity).FirstOrDefault();
            int available = item.Qua - offered;
            if (available <= 0) return Task.FromResult(Refuse("Tout l'exemplaire est déjà proposé."));
            if (quantity <= 0 || quantity > available) return Task.FromResult(Refuse("Quantité invalide (1 à " + available + ")."));
            return SendAsync("EMO+" + inventoryId.ToString(CultureInfo.InvariantCulture) + "|" + quantity.ToString(CultureInfo.InvariantCulture),
                "Dépôt de " + quantity + " × " + item.Name + " demandé.");
        }

        /// <summary>Envoie <c>EMO-&lt;objet&gt;|&lt;quantité&gt;</c> : retire une quantité d'un objet de sa proposition.</summary>
        public Task<InteractionResult> RemoveItemAsync(uint inventoryId, int quantity)
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun échange de familiers ouvert."));
            ExchangeItem offered;
            lock (sync) offered = local.FirstOrDefault(entry => entry.Id == inventoryId)?.Copy();
            if (offered == null) return Task.FromResult(Refuse("Cet objet n'est pas dans votre proposition."));
            if (quantity <= 0 || quantity > offered.Quantity) return Task.FromResult(Refuse("Quantité invalide (1 à " + offered.Quantity + ")."));
            return SendAsync("EMO-" + inventoryId.ToString(CultureInfo.InvariantCulture) + "|" + quantity.ToString(CultureInfo.InvariantCulture),
                "Retrait de " + quantity + " × " + offered.Name + " demandé.");
        }

        /// <summary>Envoie <c>EMG&lt;kamas&gt;</c>, ramené aux kamas du personnage comme le client (StarLoco ne les prélève pas).</summary>
        public Task<InteractionResult> SetKamasAsync(long kamas)
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun échange de familiers ouvert."));
            if (kamas < 0) return Task.FromResult(Refuse("Montant de kamas invalide."));
            long owned = Math.Max(0, Account?.Game?.character?.Kamas ?? 0);
            if (kamas > owned) kamas = owned;
            return SendAsync("EMG" + kamas.ToString(CultureInfo.InvariantCulture), kamas + " kamas proposés.");
        }

        /// <summary>Envoie <c>EK</c> (valider ou retirer sa validation), refusé pendant les 3 secondes qui suivent une modification.</summary>
        public Task<InteractionResult> ValidateAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun échange de familiers ouvert."));
            TimeSpan wait = ValidationWait;
            if (wait > TimeSpan.Zero)
                return Task.FromResult(Refuse("L'échange vient de changer : validation possible dans " + Math.Ceiling(wait.TotalSeconds) + " s."));
            return SendAsync("EK", LocalReady ? "Validation retirée ; le serveur confirme." : "Validation envoyée ; le serveur confirme.");
        }

        /// <summary>Envoie <c>EV</c> ; la fermeture attend le <c>EV</c> du serveur.</summary>
        public Task<InteractionResult> LeaveAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun échange de familiers ouvert."));
            return SendAsync("EV", "Fermeture de l'échange de familiers demandée.");
        }

        protected override void HandleCreated(int type, string data)
        {
            string head = data.Split('|')[0];
            int npc = int.TryParse(head, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int announced) ? announced : 0;
            int noted, notedNpc;
            long notedAt;
            lock (sync) { noted = requestedType; notedNpc = requestedNpc; notedAt = requestedAt; requestedType = -1; }
            NpcId = npc;
            int template = -1;
            NpcName = npc == 0 ? "PNJ inconnu" : ResolveName(npc, out template);
            bool recent = noted >= 0 && (Stopwatch.GetTimestamp() - notedAt) / Stopwatch.Frequency < 60 && (npc == 0 || notedNpc == npc);
            Service = recent ? (noted == ExchangeTypes.PetsResurrection ? PetsService.Resurrection : PetsService.Keeping) : FromNpc(template);
            Touch();
            MarkOpen();
            Log((Service == PetsService.Resurrection ? "Résurrection de familier" : Service == PetsService.Keeping ? "Garde de familier" : "Échange de familiers")
                + " ouvert avec " + NpcName + ".");
            Notify();
        }

        /// <summary><c>EL</c> : StarLoco n'en envoie pas pour ce type ; le contenu est gardé brut et journalisé, sans être interprété.</summary>
        protected override void HandleList(string payload)
        {
            RawList = payload;
            Account?.Logger?.LogDebug(Reference, "Liste EL reçue pour l'échange de familiers (non interprétée) : " + (payload.Length > 120 ? payload.Substring(0, 120) + "…" : payload));
            Notify();
        }

        protected override void HandleLocalMovement(string data)
        {
            if (!ApplyMovement(data, true)) Malformed("EMK", data);
        }

        protected override void HandleDistantMovement(string data)
        {
            if (!ApplyMovement(data, false)) Malformed("EmK", data);
        }

        /// <summary><c>EK&lt;0|1&gt;&lt;acteur&gt;</c> : l'acteur du compte est « soi » ; sans acteur (StarLoco) c'est le PNJ.</summary>
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
            string partner = NpcName;
            PetsService service = Service;
            ResetState();
            MarkClosed();
            if (wasOpen)
                Log(suffix == "a"
                    ? (service == PetsService.Resurrection ? "Résurrection effectuée par " : service == PetsService.Keeping ? "Garde effectuée par " : "Échange effectué avec ") + partner + "."
                    : ExchangeRegistry.Text("EXCHANGE_CANCEL", "Échange annulé."));
            Notify();
        }

        protected override void ResetState()
        {
            lock (sync) { local = new List<ExchangeItem>(); distant = new List<ExchangeItem>(); }
            LocalKamas = 0; DistantKamas = 0; LocalReady = false; DistantReady = false; RawList = string.Empty;
            NpcId = 0; NpcName = string.Empty; Service = PetsService.Unknown;
        }

        protected override void OnClearing()
        {
            lock (sync) requestedType = -1;
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
                    var updated = (mine ? local : distant).Where(entry => entry.Id != id).ToList();
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
                // StarLoco : EmKO+<modèle>|1|<modèle>|<effets> (l'identifiant est celui du modèle remis par le PNJ).
                if (fields.Length < 3 || !TryInt(fields[2], out int template) || template < 0) return false;
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

        private void Touch() => Volatile.Write(ref lastChange, Stopwatch.GetTimestamp());

        /// <summary>Service d'après les actions du modèle de PNJ (<c>npc.xml</c>) quand il n'en propose qu'un des deux.</summary>
        private static PetsService FromNpc(int templateId)
        {
            if (templateId < 0) return PetsService.Unknown;
            int[] actions = NpcActions.ForTemplate(templateId, out bool fromClient);
            if (!fromClient) return PetsService.Unknown;
            bool keeps = actions.Contains(NpcActions.Pets), revives = actions.Contains(NpcActions.PetResurrection);
            return keeps == revives ? PetsService.Unknown : keeps ? PetsService.Keeping : PetsService.Resurrection;
        }

        private string ResolveName(int npcId, out int templateId)
        {
            templateId = -1;
            MapActor actor = Account?.Game?.Map?.GetActor(npcId);
            if (actor is NpcActor npc)
            {
                templateId = npc.TemplateId;
                if (LangData.Npc.Has(npc.TemplateId)) return LangData.Text.Fetch(LangData.Npc.Name(npc.TemplateId));
            }
            if (actor != null && !string.IsNullOrEmpty(actor.Name)) return actor.Name;
            return "PNJ " + npcId.ToString(CultureInfo.InvariantCulture);
        }
    }
}
