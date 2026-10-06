using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Tool_BotProtocol.Game.Exchanges
{
    /// <summary>
    /// Contenu d'un percepteur (type 8), l'interface <c>TaxCollectorStorage</c> du client 1.34 (propriétaire : lot F3). « Collecter » du
    /// menu du percepteur envoie <c>ER8|&lt;id&gt;</c> (<c>Exchange.request</c>) ; StarLoco exige la même guilde, la même carte, un percepteur
    /// hors combat et le droit de collecte (sinon <c>Im1101</c>), puis répond <c>ECK8|&lt;id&gt;</c> et <c>EL O&lt;fiche&gt;;…;G&lt;kamas&gt;</c>
    /// (<c>Collector.getItemCollectorList</c>). Retirer un objet envoie <c>EMO-&lt;objet&gt;|&lt;quantité&gt;</c>, les kamas <c>EMG-&lt;montant&gt;</c> ;
    /// le serveur confirme par <c>EsKO-&lt;objet&gt;</c> / <c>EsKG&lt;kamas&gt;</c> et met le sac à jour. Fermer envoie <c>EV</c> : StarLoco relève
    /// alors la collecte (<c>gTG…</c> aux membres, expérience donnée à la guilde) et retire le percepteur de la carte — ce qui n'a pas
    /// été récupéré avant est perdu.
    /// </summary>
    [ExchangeType(ExchangeTypes.Collector)]
    public sealed class CollectorExchange : ExchangeWindow
    {
        private readonly object sync = new object();
        private List<ExchangeItem> items = new List<ExchangeItem>();

        internal CollectorExchange(Accounts.Accounts account) : base(account) { }

        protected override string Reference => "PERCEPTEUR";
        protected override AccountStates OpenState => AccountStates.EXCHANGE;

        /// <summary>Identifiant du percepteur ouvert (<c>ECK8|&lt;id&gt;</c>), -1 fenêtre fermée.</summary>
        public long CollectorId { get; private set; } = -1;
        /// <summary>Objets du percepteur, dans l'ordre de réception.</summary>
        public IReadOnlyList<ExchangeItem> Items { get { lock (sync) return items.Select(item => item.Copy()).ToArray(); } }
        public long Kamas { get; private set; }
        /// <summary>Vrai dès que la liste <c>EL</c> a été reçue.</summary>
        public bool ContentReceived { get; private set; }
        public bool IsEmpty => Kamas <= 0 && Items.Count == 0;

        /// <summary>« Collecter » : <c>ER8|&lt;id&gt;</c>. Le serveur ouvre la fenêtre (<c>ECK8</c>) ou répond <c>Im1101</c> sans le droit.</summary>
        public Task<InteractionResult> OpenAsync(long collectorId)
        {
            InteractionResult refused = CheckCanOpen();
            if (refused != null) return Task.FromResult(refused);
            if (collectorId < 0) return Task.FromResult(Refuse("Percepteur inconnu."));
            return SendAsync("ER8|" + collectorId.ToString(CultureInfo.InvariantCulture), "Collecte du percepteur demandée.");
        }

        /// <summary>Envoie <c>EMO-&lt;objet&gt;|&lt;quantité&gt;</c> pour un objet du percepteur.</summary>
        public Task<InteractionResult> WithdrawAsync(uint itemId, int quantity)
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun percepteur ouvert."));
            ExchangeItem stored;
            lock (sync) stored = items.FirstOrDefault(entry => entry.Id == itemId)?.Copy();
            if (stored == null) return Task.FromResult(Refuse("Cet objet n'est pas dans le percepteur."));
            if (quantity <= 0 || quantity > stored.Quantity) return Task.FromResult(Refuse("Quantité invalide (1 à " + stored.Quantity + ")."));
            return SendAsync("EMO-" + itemId.ToString(CultureInfo.InvariantCulture) + "|" + quantity.ToString(CultureInfo.InvariantCulture),
                "Retrait de " + quantity + " × " + stored.Name + " demandé.");
        }

        /// <summary>Envoie <c>EMG-&lt;montant&gt;</c> : retire des kamas du percepteur.</summary>
        public Task<InteractionResult> WithdrawKamasAsync(long kamas)
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun percepteur ouvert."));
            if (kamas <= 0 || kamas > Kamas) return Task.FromResult(Refuse("Montant invalide (1 à " + Kamas + " kamas)."));
            return SendAsync("EMG-" + kamas.ToString(CultureInfo.InvariantCulture), kamas + " kamas à retirer.");
        }

        /// <summary>
        /// « Récupérer » (<c>GET_ITEM</c>) : un <c>EMO-&lt;objet&gt;|&lt;quantité&gt;</c> par objet (pile entière) puis <c>EMG-&lt;kamas&gt;</c>, dans l'ordre
        /// de la liste ; le contenu ne change qu'aux réponses <c>EsK…</c> du serveur. S'arrête au premier envoi impossible.
        /// </summary>
        public async Task<InteractionResult> WithdrawAllAsync()
        {
            if (!IsOpen) return Refuse("Aucun percepteur ouvert.");
            ExchangeItem[] list;
            lock (sync) list = items.Select(item => item.Copy()).ToArray();
            long kamas = Kamas;
            if (list.Length == 0 && kamas <= 0) return Refuse("Le percepteur n'a rien à récupérer.");
            int sent = 0;
            foreach (ExchangeItem item in list)
            {
                if (item.Quantity <= 0) continue;
                InteractionResult result = await SendAsync("EMO-" + item.Id.ToString(CultureInfo.InvariantCulture) + "|" + item.Quantity.ToString(CultureInfo.InvariantCulture),
                    "Retrait de " + item.Quantity + " × " + item.Name + " demandé.").ConfigureAwait(false);
                if (!result.Sent) return result;
                sent++;
            }
            if (kamas > 0)
            {
                InteractionResult result = await SendAsync("EMG-" + kamas.ToString(CultureInfo.InvariantCulture), kamas + " kamas à retirer.").ConfigureAwait(false);
                if (!result.Sent) return result;
                sent++;
            }
            string message = sent + " retrait(s) demandé(s) : le contenu suit les réponses du serveur (EsK).";
            LastMessage = message;
            return new InteractionResult(true, message);
        }

        /// <summary>Envoie <c>EV</c> : StarLoco relève la collecte et retire le percepteur ; la fenêtre se ferme au <c>EV</c> du serveur.</summary>
        public Task<InteractionResult> LeaveAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun percepteur ouvert."));
            return SendAsync("EV", "Collecte relevée : le serveur retire le percepteur.");
        }

        protected override void HandleCreated(int type, string data)
        {
            long id;
            CollectorId = TryLong(data, out id) ? id : -1;
            MarkOpen();
            Log("Percepteur ouvert ; en attente de son contenu.");
            Notify();
        }

        /// <summary><c>EL</c> : entrées séparées par <c>;</c>, <c>O&lt;fiche&gt;</c> ou <c>G&lt;kamas&gt;</c>, comme le coffre.</summary>
        protected override void HandleList(string payload)
        {
            var list = new List<ExchangeItem>();
            long kamas = 0;
            int unreadable = 0;
            foreach (string entry in payload.Split(';'))
            {
                if (entry.Length < 2) continue;
                if (entry[0] == 'G')
                {
                    long value;
                    if (TryLong(entry.Substring(1), out value) && value >= 0) kamas = value; else unreadable++;
                    continue;
                }
                if (entry[0] != 'O') { unreadable++; continue; }
                InventoryObjects record = InventoryObjects.Parse(entry.Substring(1));
                if (record == null) { unreadable++; continue; }
                list.Add(new ExchangeItem { Id = record.Inventory_ID, TemplateId = record.ID, Quantity = record.Qua,
                    Effects = record.Stats ?? string.Empty, Name = record.Name });
            }
            lock (sync) items = list;
            Kamas = kamas;
            ContentReceived = true;
            if (unreadable > 0) Account?.Logger?.LogError(Reference, unreadable + " entrée(s) illisible(s) dans le contenu du percepteur ignorée(s).");
            Log(list.Count + " objet(s) et " + kamas + " kamas dans le percepteur.");
            Notify();
        }

        /// <summary><c>EsKO-&lt;objet&gt;</c> (retiré), <c>EsKO+…</c> (toléré), <c>EsKG&lt;kamas&gt;</c> (reste).</summary>
        protected override void HandleStorageMovement(string data)
        {
            if (data.Length >= 2 && data[0] == 'G')
            {
                long kamas;
                if (!TryLong(data.Substring(1), out kamas) || kamas < 0) { Malformed("EsK", data); return; }
                Kamas = kamas;
                Notify();
                return;
            }
            if (data.Length < 3 || data[0] != 'O' || (data[1] != '+' && data[1] != '-')) { Malformed("EsK", data); return; }
            string[] fields = data.Substring(2).Split('|');
            uint id;
            if (!TryId(fields[0], out id)) { Malformed("EsK", data); return; }
            if (data[1] == '-')
            {
                lock (sync) items = items.Where(entry => entry.Id != id).ToList();
                Notify();
                return;
            }
            int quantity, template;
            if (fields.Length < 3 || !TryInt(fields[1], out quantity) || quantity <= 0 || !TryInt(fields[2], out template)) { Malformed("EsK", data); return; }
            var item = new ExchangeItem { Id = id, Quantity = quantity, TemplateId = template,
                Effects = fields.Length > 3 ? fields[3] : string.Empty, Name = InventoryObjects.DisplayName(template) };
            lock (sync)
            {
                var updated = items.ToList();
                int index = updated.FindIndex(entry => entry.Id == id);
                if (index >= 0) updated[index] = item; else updated.Add(item);
                items = updated;
            }
            Notify();
        }

        /// <summary><c>EV</c> : la collecte est relevée et le percepteur retiré par le serveur.</summary>
        protected override void HandleLeave(string suffix)
        {
            bool wasOpen = IsOpen;
            Reset();
            MarkClosed();
            if (wasOpen) Log("Collecte relevée : le percepteur a été retiré par le serveur.");
            Notify();
        }

        protected override void ResetState()
        {
            lock (sync) items = new List<ExchangeItem>();
            Kamas = 0;
            ContentReceived = false;
            CollectorId = -1;
        }
    }
}
