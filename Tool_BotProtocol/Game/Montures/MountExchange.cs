using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Tool_BotProtocol.Game.Montures
{
    /// <summary>
    /// Sacoches de la monture équipée (type 15, interface <c>Storage</c> avec <c>isMount</c> du client 1.34). Le bot envoie
    /// <c>ER15|</c> comme <c>Exchange.request(15)</c> ; StarLoco répond <c>ECK15|&lt;monture&gt;</c>, <c>EL O&lt;fiche&gt;;…</c>
    /// (<c>parseToMountObjects</c>) et <c>Ew&lt;pods&gt;;&lt;max&gt;</c>. Déposer envoie <c>EMO+&lt;objet&gt;|&lt;quantité&gt;</c>,
    /// retirer <c>EMO-&lt;objet&gt;|&lt;quantité&gt;</c> ; le serveur répond <c>EsKO+…</c>/<c>EsKO-…</c>, <c>Ew</c> et, après un
    /// dépôt, la liste complète <c>EL</c>. Fermer envoie <c>EV</c>.
    /// </summary>
    [ExchangeType(ExchangeTypes.MountStorage)]
    public sealed class MountExchange : ExchangeWindow
    {
        private readonly object sync = new object();
        private List<ExchangeItem> items = new List<ExchangeItem>();

        internal MountExchange(Accounts.Accounts account) : base(account) { }

        protected override string Reference => "SACOCHES";
        protected override AccountStates OpenState => AccountStates.STORAGE;

        /// <summary>Identifiant de la monture annoncé par <c>ECK15|&lt;id&gt;</c>, 0 s'il est illisible.</summary>
        public int MountId { get; private set; }
        public IReadOnlyList<ExchangeItem> Items { get { lock (sync) return items.Select(item => item.Copy()).ToArray(); } }
        public bool ContentReceived { get; private set; }
        /// <summary>Charge (<c>Ew</c>) ; null tant qu'elle n'a pas été reçue.</summary>
        public int? Pods { get; private set; }
        public int? PodsMax { get; private set; }

        /// <summary>Envoie <c>EMO+&lt;objet&gt;|&lt;quantité&gt;</c> pour un objet non équipé du sac.</summary>
        public Task<InteractionResult> DepositAsync(uint inventoryId, int quantity)
        {
            if (!IsOpen) return Task.FromResult(Refuse("Les sacoches de la monture ne sont pas ouvertes."));
            InventoryObjects item = Account?.Game?.character?.Inventory?.GetByInventoryId(inventoryId);
            if (item == null) return Task.FromResult(Refuse("Cet objet n'est pas dans votre inventaire."));
            if (item.IsEquipped()) return Task.FromResult(Refuse("Déséquipez cet objet avant de le déposer."));
            if (quantity <= 0 || quantity > item.Qua) return Task.FromResult(Refuse("Quantité invalide (1 à " + item.Qua + ")."));
            return SendAsync("EMO+" + inventoryId.ToString(CultureInfo.InvariantCulture) + "|" + quantity.ToString(CultureInfo.InvariantCulture),
                "Dépôt de " + quantity + " × " + item.Name + " dans les sacoches demandé.");
        }

        /// <summary>Envoie <c>EMO-&lt;objet&gt;|&lt;quantité&gt;</c> pour un objet des sacoches.</summary>
        public Task<InteractionResult> WithdrawAsync(uint storedId, int quantity)
        {
            if (!IsOpen) return Task.FromResult(Refuse("Les sacoches de la monture ne sont pas ouvertes."));
            ExchangeItem stored;
            lock (sync) stored = items.FirstOrDefault(entry => entry.Id == storedId)?.Copy();
            if (stored == null) return Task.FromResult(Refuse("Cet objet n'est pas dans les sacoches."));
            if (quantity <= 0 || quantity > stored.Quantity) return Task.FromResult(Refuse("Quantité invalide (1 à " + stored.Quantity + ")."));
            return SendAsync("EMO-" + storedId.ToString(CultureInfo.InvariantCulture) + "|" + quantity.ToString(CultureInfo.InvariantCulture),
                "Retrait de " + quantity + " × " + stored.Name + " des sacoches demandé.");
        }

        /// <summary>Envoie <c>EV</c> ; la fenêtre se ferme au <c>EV</c> du serveur.</summary>
        public Task<InteractionResult> LeaveAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("Les sacoches de la monture ne sont pas ouvertes."));
            return SendAsync("EV", "Fermeture des sacoches demandée.");
        }

        protected override void HandleCreated(int type, string data)
        {
            MountId = int.TryParse(data, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ? id : 0;
            MarkOpen();
            Log("Sacoches de la monture ouvertes ; en attente de leur contenu.");
            Notify();
        }

        /// <summary><c>EL O&lt;fiche&gt;;O&lt;fiche&gt;;…</c> (le « ; » final de StarLoco donne une entrée vide, ignorée).</summary>
        protected override void HandleList(string payload)
        {
            var list = new List<ExchangeItem>();
            int unreadable = 0;
            foreach (string entry in payload.Split(';'))
            {
                if (entry.Length == 0) continue;
                if (entry.Length < 2 || entry[0] != 'O') { unreadable++; continue; }
                InventoryObjects record = InventoryObjects.Parse(entry.Substring(1));
                if (record == null) { unreadable++; continue; }
                list.Add(new ExchangeItem { Id = record.Inventory_ID, TemplateId = record.ID, Quantity = record.Qua,
                    Effects = record.Stats ?? string.Empty, Name = record.Name });
            }
            lock (sync) items = list;
            ContentReceived = true;
            if (unreadable > 0) Account?.Logger?.LogError(Reference, unreadable + " entrée(s) illisible(s) dans les sacoches ignorée(s).");
            Log(list.Count + " objet(s) dans les sacoches.");
            Notify();
        }

        /// <summary><c>EsKO+&lt;objet&gt;|&lt;quantité&gt;|&lt;modèle&gt;|&lt;effets&gt;</c> ou <c>EsKO-&lt;objet&gt;</c> (<c>Mount.addObject/removeObject</c>).</summary>
        protected override void HandleStorageMovement(string data)
        {
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

        /// <summary><c>Ew&lt;pods&gt;;&lt;max&gt;</c> (<c>Exchange.onMountPods</c>) : met aussi à jour la fiche de la monture équipée.</summary>
        protected override void HandlePods(string data)
        {
            string[] fields = data.Split(';');
            if (fields.Length < 2 || !TryInt(fields[0], out int pods) || !TryInt(fields[1], out int max) || pods < 0 || max < 0)
            {
                Malformed("Ew", data);
                return;
            }
            Pods = pods; PodsMax = max;
            Account?.Game?.Interactions?.Mount?.OnPods(pods, max);
            Notify();
        }

        protected override void ResetState()
        {
            lock (sync) items = new List<ExchangeItem>();
            ContentReceived = false;
            MountId = 0;
            Pods = null; PodsMax = null;
        }
    }

    /// <summary>Emplacement d'une monture dans l'interface <c>MountStorage</c> (étable et enclos).</summary>
    public enum MountLocation
    {
        /// <summary>Monture équipée (<c>FROM_INVENTORY</c>).</summary>
        Equipped,
        /// <summary>Étable (<c>FROM_SHED</c>).</summary>
        Shed,
        /// <summary>Enclos (<c>FROM_MOUNTPARK</c>).</summary>
        Park,
        /// <summary>Certificat dans l'inventaire (<c>FROM_CERTIFICATE</c>).</summary>
        Certificate
    }

    /// <summary>
    /// Étable et enclos (type 16, interface <c>MountStorage</c> du client 1.34), ouverts par la compétence « Accéder » (175)
    /// d'un enclos : <c>ECK16|&lt;étable&gt;~&lt;enclos&gt;</c>, chaque liste de fiches <see cref="Mount"/> séparées par « ; ».
    /// Le serveur tient les listes à jour par <c>Ee</c> (étable : <c>~</c> naissance, <c>+</c> ajout, <c>-</c> retrait par
    /// identifiant) et <c>Ef</c> (enclos : <c>+</c>, <c>-</c>). Les déplacements reprennent ceux de <c>MountStorage.click</c> :
    /// <c>Erp</c> (équipée → étable), <c>Erg</c> (étable → équipée), <c>Erc</c> (étable → certificat), <c>ErC</c> (certificat →
    /// étable), <c>Efp</c> (étable → enclos), <c>Efg</c> (enclos → étable), enchaînés quand le client passe par l'étable.
    /// <c>Erf</c>/<c>Eff</c> (libérer depuis l'étable ou l'enclos) ne sont pas traités par StarLoco : le bot ne les émet pas.
    /// Les certificats sont les objets de type 97 de l'inventaire (<c>createCertificateArray</c>). Fermer envoie <c>EV</c>.
    /// </summary>
    [ExchangeType(ExchangeTypes.Shed)]
    public sealed class ShedExchange : ExchangeWindow
    {
        /// <summary>Type d'objet des certificats de monture (<c>createCertificateArray</c>).</summary>
        public const int CertificateType = 97;

        private readonly object sync = new object();
        private List<Mount> shed = new List<Mount>();
        private List<Mount> park = new List<Mount>();

        internal ShedExchange(Accounts.Accounts account) : base(account) { }

        protected override string Reference => "ETABLE";
        protected override AccountStates OpenState => AccountStates.STORAGE;

        public IReadOnlyList<Mount> ShedMounts { get { lock (sync) return shed.ToArray(); } }
        public IReadOnlyList<Mount> ParkMounts { get { lock (sync) return park.ToArray(); } }

        /// <summary>Certificats de monture du sac (objets de type 97).</summary>
        public IReadOnlyList<InventoryObjects> Certificates
        {
            get
            {
                var bag = Account?.Game?.character?.Inventory?.Objets;
                if (bag == null) return new InventoryObjects[0];
                try { return bag.Where(item => item != null && item.Type == CertificateType).ToArray(); }
                catch (InvalidOperationException) { return new InventoryObjects[0]; }
            }
        }

        /// <summary>
        /// Paquets envoyés par le client pour déplacer une monture de <paramref name="from"/> vers <paramref name="to"/>
        /// (<c>MountStorage.click</c>) ; liste vide si le client ne propose pas ce déplacement. <paramref name="id"/> est
        /// l'identifiant de la monture, ou celui de l'objet pour un certificat.
        /// </summary>
        public static IReadOnlyList<string> MovePackets(MountLocation from, MountLocation to, long id)
        {
            string value = id.ToString(CultureInfo.InvariantCulture);
            switch (to)
            {
                case MountLocation.Certificate:
                    if (from == MountLocation.Shed) return new[] { "Erc" + value };
                    if (from == MountLocation.Park) return new[] { "Efg" + value, "Erc" + value };
                    if (from == MountLocation.Equipped) return new[] { "Erp" + value, "Erc" + value };
                    break;
                case MountLocation.Park:
                    if (from == MountLocation.Shed) return new[] { "Efp" + value };
                    if (from == MountLocation.Equipped) return new[] { "Erp" + value, "Efp" + value };
                    break;
                case MountLocation.Equipped:
                    if (from == MountLocation.Shed) return new[] { "Erg" + value };
                    if (from == MountLocation.Park) return new[] { "Efg" + value, "Erg" + value };
                    break;
                case MountLocation.Shed:
                    if (from == MountLocation.Certificate) return new[] { "ErC" + value };
                    if (from == MountLocation.Park) return new[] { "Efg" + value };
                    if (from == MountLocation.Equipped) return new[] { "Erp" + value };
                    break;
            }
            return new string[0];
        }

        /// <summary>
        /// Déplace une monture comme les boutons de <c>MountStorage</c> ; vérifie que la monture (ou le certificat) est bien à
        /// l'emplacement de départ et qu'aucune monture n'est déjà équipée avant de l'équiper (<c>MOUNT_ERROR_ALREADY_HAVE_ONE</c>).
        /// </summary>
        public async Task<InteractionResult> MoveAsync(MountLocation from, MountLocation to, long id)
        {
            if (!IsOpen) return Refuse("L'étable n'est pas ouverte.");
            IReadOnlyList<string> packets = MovePackets(from, to, id);
            if (packets.Count == 0) return Refuse("Déplacement impossible de « " + Describe(from) + " » vers « " + Describe(to) + " ».");
            MountActions actions = Account?.Game?.Interactions?.Mount;
            switch (from)
            {
                case MountLocation.Equipped:
                    if (actions?.Current == null || actions.Current.Id != id) return Refuse(Mount.Text("MOUNT_NO_EQUIP", "Tu n'as pas de monture équipée."));
                    break;
                case MountLocation.Shed:
                    if (!ShedMounts.Any(mount => mount.Id == id)) return Refuse("Cette monture n'est pas dans l'étable.");
                    break;
                case MountLocation.Park:
                    if (!ParkMounts.Any(mount => mount.Id == id)) return Refuse("Cette monture n'est pas dans l'enclos.");
                    break;
                case MountLocation.Certificate:
                    if (!Certificates.Any(item => item.Inventory_ID == id)) return Refuse("Ce certificat n'est pas dans votre inventaire.");
                    break;
            }
            if (to == MountLocation.Equipped && actions?.HasMount == true)
                return Refuse(Mount.Text("MOUNT_ERROR_ALREADY_HAVE_ONE", "Vous possédez déjà une monture."));
            InteractionResult result = null;
            foreach (string packet in packets)
            {
                result = await SendAsync(packet, "Monture : « " + Describe(from) + " » vers « " + Describe(to) + " » demandé.").ConfigureAwait(false);
                if (!result.Sent) return result;
            }
            return result;
        }

        /// <summary>Envoie <c>EV</c> ; la fenêtre se ferme au <c>EV</c> du serveur.</summary>
        public Task<InteractionResult> LeaveAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("L'étable n'est pas ouverte."));
            return SendAsync("EV", "Fermeture de l'étable demandée.");
        }

        public static string Describe(MountLocation location)
        {
            switch (location)
            {
                case MountLocation.Equipped: return Mount.Text("MOUNT_INVENTORY", "Monture équipée");
                case MountLocation.Shed: return Mount.Text("MOUNT_SHED", "Etable");
                case MountLocation.Park: return Mount.Text("MOUNT_PARK", "Enclos");
                default: return Mount.Text("MOUNT_CERTIFICATES", "Certificats");
            }
        }

        protected override void HandleCreated(int type, string data)
        {
            string[] lists = data.Split('~');
            int unreadable = 0;
            List<Mount> shedList = ReadList(lists[0], ref unreadable);
            List<Mount> parkList = lists.Length > 1 ? ReadList(lists[1], ref unreadable) : new List<Mount>();
            lock (sync) { shed = shedList; park = parkList; }
            if (unreadable > 0) Account?.Logger?.LogError(Reference, unreadable + " fiche(s) de monture illisible(s) ignorée(s) (ECK16).");
            MarkOpen();
            Log("Etable ouverte : " + shedList.Count + " monture(s) à l'étable, " + parkList.Count + " dans l'enclos.");
            Notify();
        }

        /// <summary><c>Ee&lt;~|+|-|E&gt;…</c> : étable (<c>Exchange.onMountStorage</c>).</summary>
        internal void OnShedMovement(string data) => Apply("Ee", data, true);

        /// <summary><c>Ef&lt;+|-|E&gt;…</c> : enclos (<c>Exchange.onMountPark</c>).</summary>
        internal void OnParkMovement(string data) => Apply("Ef", data, false);

        private void Apply(string prefix, string data, bool toShed)
        {
            data = data ?? string.Empty;
            try
            {
                if (data.Length == 0) { Malformed(prefix, data); return; }
                if (!IsOpen) { Unexpected(prefix, data); return; }
                char sign = data[0];
                string body = data.Substring(1);
                switch (sign)
                {
                    case '~' when toShed:
                    case '+':
                        Mount mount = Mount.Parse(body, sign == '~');
                        if (mount == null) { Malformed(prefix, data); return; }
                        lock (sync)
                        {
                            List<Mount> list = (toShed ? shed : park).Where(entry => entry.Id != mount.Id).ToList();
                            list.Add(mount);
                            if (toShed) shed = list; else park = list;
                        }
                        break;
                    case '-':
                        if (!int.TryParse(body, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) { Malformed(prefix, data); return; }
                        lock (sync)
                        {
                            if (toShed) shed = shed.Where(entry => entry.Id != id).ToList();
                            else park = park.Where(entry => entry.Id != id).ToList();
                        }
                        break;
                    case 'E':
                        // Le client n'affiche rien pour une erreur d'étable ou d'enclos.
                        LogError("Déplacement de monture refusé par le serveur (" + prefix + data + ").");
                        break;
                    default:
                        Malformed(prefix, data);
                        return;
                }
                Notify();
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                Account?.Logger?.LogError(Reference, "Paquet " + prefix + " non appliqué (" + error.GetType().Name + ").");
            }
        }

        private static List<Mount> ReadList(string list, ref int unreadable)
        {
            var mounts = new List<Mount>();
            foreach (string record in (list ?? string.Empty).Split(';'))
            {
                if (record.Length == 0) continue;
                Mount mount = Mount.Parse(record);
                if (mount == null) { unreadable++; continue; }
                if (mounts.All(entry => entry.Id != mount.Id)) mounts.Add(mount);
            }
            return mounts;
        }

        protected override void ResetState()
        {
            lock (sync) { shed = new List<Mount>(); park = new List<Mount>(); }
        }
    }
}
