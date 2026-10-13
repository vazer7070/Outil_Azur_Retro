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
    /// Coffre ou banque (type 5), selon l'interface <c>Storage</c> du client 1.34 : ouverture <c>ECK5</c> (banquier :
    /// réponse de dialogue, <c>Action</c> de StarLoco ; coffre : code <c>KK0|&lt;code&gt;</c>, <c>Trunk.open</c>) puis contenu
    /// <c>EL O&lt;fiche&gt;;…;G&lt;kamas&gt;</c> (fiches hexadécimales comme <c>OAK</c>). Déposer envoie <c>EMO+&lt;objet&gt;|&lt;quantité&gt;</c>,
    /// retirer <c>EMO-&lt;objet&gt;|&lt;quantité&gt;</c>, les kamas <c>EMG&lt;±montant&gt;</c> (positif : dépôt) ; le serveur répond
    /// <c>EsKO+&lt;objet&gt;|&lt;quantité&gt;|&lt;modèle&gt;|&lt;effets&gt;</c>, <c>EsKO-&lt;objet&gt;</c>, <c>EsKG&lt;kamas&gt;</c> et met le sac à jour
    /// par <c>OAKO</c>/<c>OQ</c>/<c>OR</c>. Fermer envoie <c>EV</c>.
    /// </summary>
    [ExchangeType(ExchangeTypes.Storage)]
    public sealed class StorageExchange : ExchangeWindow
    {
        private readonly object sync = new object();
        private List<ExchangeItem> items = new List<ExchangeItem>();

        internal StorageExchange(Accounts.Accounts account) : base(account) { }

        protected override string Reference => "COFFRE";
        protected override AccountStates OpenState => AccountStates.STORAGE;

        /// <summary>Objets du coffre, dans l'ordre de réception.</summary>
        public IReadOnlyList<ExchangeItem> Items { get { lock (sync) return items.Select(item => item.Copy()).ToArray(); } }
        public long Kamas { get; private set; }
        /// <summary>Vrai dès que la liste <c>EL</c> a été reçue.</summary>
        public bool ContentReceived { get; private set; }

        /// <summary>Envoie <c>EMO+&lt;objet&gt;|&lt;quantité&gt;</c> pour un objet non équipé du sac.</summary>
        public Task<InteractionResult> DepositAsync(uint inventoryId, int quantity)
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun coffre ouvert."));
            InventoryObjects item = Account?.Game?.character?.Inventory?.GetByInventoryId(inventoryId);
            if (item == null) return Task.FromResult(Refuse("Cet objet n'est pas dans votre inventaire."));
            if (item.IsEquipped()) return Task.FromResult(Refuse("Déséquipez cet objet avant de le déposer."));
            if (quantity <= 0 || quantity > item.Qua) return Task.FromResult(Refuse("Quantité invalide (1 à " + item.Qua + ")."));
            return SendAsync("EMO+" + inventoryId.ToString(CultureInfo.InvariantCulture) + "|" + quantity.ToString(CultureInfo.InvariantCulture),
                "Dépôt de " + quantity + " × " + item.Name + " demandé.");
        }

        /// <summary>Envoie <c>EMO-&lt;objet&gt;|&lt;quantité&gt;</c> pour un objet du coffre.</summary>
        public Task<InteractionResult> WithdrawAsync(uint storageId, int quantity)
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun coffre ouvert."));
            ExchangeItem stored;
            lock (sync) stored = items.FirstOrDefault(entry => entry.Id == storageId)?.Copy();
            if (stored == null) return Task.FromResult(Refuse("Cet objet n'est pas dans le coffre."));
            if (quantity <= 0 || quantity > stored.Quantity) return Task.FromResult(Refuse("Quantité invalide (1 à " + stored.Quantity + ")."));
            return SendAsync("EMO-" + storageId.ToString(CultureInfo.InvariantCulture) + "|" + quantity.ToString(CultureInfo.InvariantCulture),
                "Retrait de " + quantity + " × " + stored.Name + " demandé.");
        }

        /// <summary>Envoie <c>EMG&lt;montant&gt;</c> : dépose des kamas portés par le personnage.</summary>
        public Task<InteractionResult> DepositKamasAsync(long kamas)
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun coffre ouvert."));
            long owned = Math.Max(0, Account?.Game?.character?.Kamas ?? 0);
            if (kamas <= 0 || kamas > owned) return Task.FromResult(Refuse("Montant invalide (1 à " + owned + " kamas)."));
            return SendAsync("EMG" + kamas.ToString(CultureInfo.InvariantCulture), kamas + " kamas à déposer.");
        }

        /// <summary>Envoie <c>EMG-&lt;montant&gt;</c> : retire des kamas du coffre.</summary>
        public Task<InteractionResult> WithdrawKamasAsync(long kamas)
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun coffre ouvert."));
            if (kamas <= 0 || kamas > Kamas) return Task.FromResult(Refuse("Montant invalide (1 à " + Kamas + " kamas)."));
            return SendAsync("EMG-" + kamas.ToString(CultureInfo.InvariantCulture), kamas + " kamas à retirer.");
        }

        /// <summary>Envoie <c>EV</c> ; la fenêtre se ferme au <c>EV</c> du serveur.</summary>
        public Task<InteractionResult> LeaveAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun coffre ouvert."));
            return SendAsync("EV", "Fermeture du coffre demandée.");
        }

        protected override void HandleCreated(int type, string data)
        {
            MarkOpen();
            Log("Coffre ouvert ; en attente de son contenu.");
            Notify();
        }

        /// <summary><c>EL</c> : entrées séparées par <c>;</c>, <c>O&lt;fiche&gt;</c> ou <c>G&lt;kamas&gt;</c> (branche « stockage » de <c>Exchange.onList</c>).</summary>
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
                    if (TryLong(entry.Substring(1), out long value) && value >= 0) kamas = value; else unreadable++;
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
            if (unreadable > 0) Account?.Logger?.LogError(Reference, unreadable + " entrée(s) illisible(s) dans le contenu du coffre ignorée(s).");
            Log(list.Count + " objet(s) et " + kamas + " kamas dans le coffre.");
            Notify();
        }

        /// <summary><c>EsKO+&lt;objet&gt;|&lt;quantité&gt;|&lt;modèle&gt;|&lt;effets&gt;</c> (quantité totale), <c>EsKO-&lt;objet&gt;</c>, <c>EsKG&lt;kamas&gt;</c>.</summary>
        protected override void HandleStorageMovement(string data)
        {
            if (data.Length >= 2 && data[0] == 'G')
            {
                if (!TryLong(data.Substring(1), out long kamas) || kamas < 0) { Malformed("EsK", data); return; }
                Kamas = kamas;
                Notify();
                return;
            }
            if (data.Length < 3 || data[0] != 'O' || (data[1] != '+' && data[1] != '-')) { Malformed("EsK", data); return; }
            string[] fields = data.Substring(2).Split('|');
            if (!TryId(fields[0], out uint id)) { Malformed("EsK", data); return; }
            if (data[1] == '-')
            {
                lock (sync) items = items.Where(entry => entry.Id != id).ToList();
                Notify();
                return;
            }
            if (fields.Length < 3 || !TryInt(fields[1], out int quantity) || quantity <= 0 || !TryInt(fields[2], out int template))
            {
                Malformed("EsK", data);
                return;
            }
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

        protected override void ResetState()
        {
            lock (sync) items = new List<ExchangeItem>();
            Kamas = 0;
            ContentReceived = false;
        }
    }
}
