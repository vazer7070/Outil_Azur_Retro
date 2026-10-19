using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Jobs;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Tool_BotProtocol.Game.Exchanges
{
    /// <summary>Paiement de l'artisanat sécurisé : « Paiement » (type 1, dû même si la recette échoue) ou « Prime si réussite » (type 2).</summary>
    public enum CraftPayment
    {
        Pay = 1,
        PayIfSuccess = 2
    }

    /// <summary>
    /// Artisanat sécurisé (interface <c>SecureCraft</c> du client 1.34, <c>CraftSecure</c> de StarLoco ; lot X5). Le serveur l'ouvre après
    /// une demande <c>ER12</c> (de l'artisan) ou <c>ER13</c> (du client) acceptée par <c>EA</c> : <c>ECK12|&lt;cases&gt;;&lt;compétence&gt;</c> chez
    /// l'artisan, <c>ECK13|…</c> chez le client (<c>isClient</c> du client = type 13). Les deux posent des ingrédients par
    /// <c>EMO+&lt;objet&gt;|&lt;quantité&gt;</c> / <c>EMO-…</c>, confirmés par <c>EMKO±</c> (son côté) et <c>EmKO±</c> (l'autre). Seul le client paie :
    /// <c>EP&lt;1|2&gt;G&lt;kamas&gt;</c> et <c>EP&lt;1|2&gt;O±&lt;objet&gt;|&lt;quantité&gt;</c>, annoncés aux deux par <c>Ep&lt;type&gt;;G&lt;kamas&gt;</c>,
    /// <c>Ep&lt;type&gt;;O+&lt;objet&gt;|&lt;total&gt;[|&lt;modèle&gt;|&lt;effets&gt;]</c> (modèle et effets chez l'artisan seulement) et <c>Ep&lt;type&gt;;O-&lt;objet&gt;</c>.
    /// « Combiner » envoie <c>EK</c> ; tout changement remet les deux validations à zéro (<c>EK0</c>). Quand les deux ont validé, StarLoco
    /// fabrique : <c>EcK;&lt;modèle&gt;;T&lt;client&gt;;&lt;effets&gt;</c> à l'artisan et <c>EcK;&lt;modèle&gt;;B&lt;artisan&gt;;&lt;effets&gt;</c> au client (l'objet va
    /// dans le sac du client, pas de <c>EmKO+</c>), ou <c>EcEF</c> / <c>EcEI</c> aux deux ; ingrédients, paiements et validations sont alors
    /// vidés et la fenêtre reste ouverte. <c>EV</c> la ferme. Rien ne change localement avant la réponse du serveur.
    /// </summary>
    [ExchangeType(ExchangeTypes.SecureCraftArtisan)]
    [ExchangeType(ExchangeTypes.SecureCraftClient)]
    public sealed class CraftSecureExchange : ExchangeWindow
    {
        /// <summary>Délai du client entre la dernière modification et l'activation de « Combiner » (<c>DELAY_BEFORE_VALIDATE</c>).</summary>
        public static readonly TimeSpan ValidationDelay = PlayerExchange.ValidationDelay;

        private readonly object sync = new object();
        private List<ExchangeItem> local = new List<ExchangeItem>(), distant = new List<ExchangeItem>();
        private List<ExchangeItem> pay = new List<ExchangeItem>(), payIfSuccess = new List<ExchangeItem>();
        private long lastChange = Stopwatch.GetTimestamp();
        private CraftOutcome outcome;
        private int resultTemplateId;
        private string resultEffects = string.Empty;
        private bool validatePending, combinePending;

        internal CraftSecureExchange(Accounts.Accounts account) : base(account) { }

        protected override string Reference => "ARTISANAT";
        protected override AccountStates OpenState => AccountStates.EXCHANGE;

        /// <summary>Vrai côté client (<c>ECK13</c>) : c'est lui qui paie et reçoit l'objet.</summary>
        public bool IsClient => ExchangeType == ExchangeTypes.SecureCraftClient;
        /// <summary>Vrai côté artisan (<c>ECK12</c>) : ses cases d'atelier, son métier.</summary>
        public bool IsArtisan => ExchangeType == ExchangeTypes.SecureCraftArtisan;
        /// <summary>Cases de l'atelier de l'artisan (<c>maxItem</c>), 0 si illisible.</summary>
        public int Slots { get; private set; }
        /// <summary>Compétence d'atelier de la fabrication, 0 fenêtre fermée.</summary>
        public short SkillId { get; private set; }
        public string SkillName => SkillId > 0 ? JobCatalog.SkillName(SkillId) : string.Empty;
        public int PartnerId { get; private set; }
        public string PartnerName { get; private set; } = string.Empty;
        /// <summary>Ingrédients posés par le personnage (<c>EMKO+</c>).</summary>
        public IReadOnlyList<ExchangeItem> LocalIngredients { get { lock (sync) return local.Select(item => item.Copy()).ToArray(); } }
        /// <summary>Ingrédients posés par l'autre joueur (<c>EmKO+</c>).</summary>
        public IReadOnlyList<ExchangeItem> DistantIngredients { get { lock (sync) return distant.Select(item => item.Copy()).ToArray(); } }
        /// <summary>Objets du paiement (<c>Ep1;O+</c>).</summary>
        public IReadOnlyList<ExchangeItem> PayItems { get { lock (sync) return pay.Select(item => item.Copy()).ToArray(); } }
        /// <summary>Objets de la prime si réussite (<c>Ep2;O+</c>).</summary>
        public IReadOnlyList<ExchangeItem> PayIfSuccessItems { get { lock (sync) return payIfSuccess.Select(item => item.Copy()).ToArray(); } }
        public long PayKamas { get; private set; }
        public long PayIfSuccessKamas { get; private set; }
        /// <summary>État « prêt » du personnage tel que le serveur l'annonce (<c>EK1&lt;soi&gt;</c>).</summary>
        public bool LocalReady { get; private set; }
        public bool DistantReady { get; private set; }
        public CraftOutcome LastOutcome { get { lock (sync) return outcome; } }
        /// <summary>Modèle annoncé par le dernier <c>EcK;&lt;modèle&gt;</c>, 0 sinon.</summary>
        public int LastResultTemplateId { get { lock (sync) return resultTemplateId; } }
        /// <summary>Effets de l'objet créé (dernier champ de <c>EcK</c>), vide sinon.</summary>
        public string LastResultEffects { get { lock (sync) return resultEffects; } }
        /// <summary><c>EK</c> envoyé, réponse <c>EK&lt;0|1&gt;&lt;soi&gt;</c> pas encore reçue.</summary>
        public bool IsValidatePending { get { lock (sync) return validatePending; } }
        /// <summary>Les deux ont validé : la fabrication est en cours, réponse <c>Ec</c> attendue.</summary>
        public bool IsCombinePending { get { lock (sync) return combinePending; } }
        /// <summary>Temps restant avant que « Combiner » soit permis (zéro quand il l'est).</summary>
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

        /// <summary>Métier du personnage qui possède la compétence (artisan), ou <c>null</c>.</summary>
        public Jobs.Jobs Job
        {
            get
            {
                short skill = SkillId;
                var character = Account?.Game?.character;
                if (skill <= 0 || character == null) return null;
                return character.GetJobsSnapshot().FirstOrDefault(job => job?.Skills != null && job.Skills.ToArray().Any(entry => entry != null && entry.Id == skill));
            }
        }

        /// <summary>Recettes connues de la compétence qui tiennent dans les cases (textes du client), triées par nom.</summary>
        public IReadOnlyList<Recipe> Recipes => SkillId > 0 ? JobCatalog.Recipes(SkillId, Slots > 0 ? Slots : int.MaxValue) : new Recipe[0];

        /// <summary>Ingrédients des deux côtés regroupés par modèle (<c>GameManager.mergeUnicItemInInventory</c>) ; un modèle inconnu (0) n'est pas compté.</summary>
        public IReadOnlyDictionary<int, int> PlacedByTemplate()
        {
            var merged = new Dictionary<int, int>();
            foreach (ExchangeItem item in LocalIngredients.Concat(DistantIngredients))
            {
                if (item.TemplateId <= 0) continue;
                merged.TryGetValue(item.TemplateId, out int quantity);
                merged[item.TemplateId] = quantity + item.Quantity;
            }
            return merged;
        }

        /// <summary>Nombre d'exemplaires posés, des deux côtés (une pile par objet, comme les cases de <c>SecureCraft</c>).</summary>
        public int IngredientCount { get { lock (sync) return local.Count + distant.Count; } }

        /// <summary>Recette reconnue d'après tous les ingrédients posés, ou <c>null</c> (le client demande alors <c>WRONG_CRAFT_CONFIRM</c>).</summary>
        public Recipe ExpectedRecipe
        {
            get
            {
                if (SkillId <= 0) return null;
                IReadOnlyDictionary<int, int> placed = PlacedByTemplate();
                return placed.Count == 0 ? null : JobCatalog.Analyse(placed, SkillId, Slots > 0 ? Slots : int.MaxValue);
            }
        }

        /// <summary>Quantité de l'objet du sac déjà posée comme ingrédient.</summary>
        public int PlacedQuantity(uint inventoryId)
        {
            lock (sync) return local.FirstOrDefault(item => item.Id == inventoryId)?.Quantity ?? 0;
        }

        /// <summary>Quantité de l'objet du sac déjà proposée en paiement ou en prime.</summary>
        public int OfferedQuantity(uint inventoryId)
        {
            lock (sync) return pay.Concat(payIfSuccess).Where(item => item.Id == inventoryId).Sum(item => item.Quantity);
        }

        /// <summary>Pose un ingrédient : <c>EMO+&lt;objet&gt;|&lt;quantité&gt;</c> (objet non équipé du sac, quantité restante).</summary>
        public Task<InteractionResult> AddIngredientAsync(uint inventoryId, int quantity)
        {
            InteractionResult refused = CheckEditable();
            if (refused != null) return Task.FromResult(refused);
            InventoryObjects item = Account?.Game?.character?.Inventory?.GetByInventoryId(inventoryId);
            if (item == null) return Task.FromResult(Refuse("Cet objet n'est pas dans votre inventaire."));
            if (item.IsEquipped()) return Task.FromResult(Refuse("Déséquipez cet objet avant de le poser."));
            int placed = PlacedQuantity(inventoryId);
            int available = item.Qua - placed - OfferedQuantity(inventoryId);
            if (available <= 0) return Task.FromResult(Refuse("Tout cet objet est déjà posé ou proposé."));
            if (quantity <= 0 || quantity > available) return Task.FromResult(Refuse("Quantité invalide (1 à " + available + ")."));
            if (placed == 0 && Slots > 0 && IngredientCount >= Slots)
                return Task.FromResult(Refuse(JobCatalog.Text("NOT_ENOUGHT_CRAFT_SLOT", "Vous ne pouvez faire que des recettes à " + Slots + " cases.", Slots.ToString(CultureInfo.InvariantCulture))));
            return SendAsync("EMO+" + inventoryId.ToString(CultureInfo.InvariantCulture) + "|" + quantity.ToString(CultureInfo.InvariantCulture),
                "Ingrédient posé : " + quantity + " × " + item.Name + ".");
        }

        /// <summary>Retire un ingrédient posé par le personnage : <c>EMO-&lt;objet&gt;|&lt;quantité&gt;</c>.</summary>
        public Task<InteractionResult> RemoveIngredientAsync(uint inventoryId, int quantity)
        {
            InteractionResult refused = CheckEditable();
            if (refused != null) return Task.FromResult(refused);
            ExchangeItem placed;
            lock (sync) placed = local.FirstOrDefault(item => item.Id == inventoryId)?.Copy();
            if (placed == null) return Task.FromResult(Refuse("Cet objet n'est pas posé de votre côté."));
            if (quantity <= 0 || quantity > placed.Quantity) return Task.FromResult(Refuse("Quantité invalide (1 à " + placed.Quantity + ")."));
            return SendAsync("EMO-" + inventoryId.ToString(CultureInfo.InvariantCulture) + "|" + quantity.ToString(CultureInfo.InvariantCulture),
                "Ingrédient retiré : " + quantity + " × " + placed.Name + ".");
        }

        /// <summary>
        /// Montant du paiement ou de la prime : <c>EP&lt;type&gt;G&lt;kamas&gt;</c> (client seulement ; montant total, ramené aux kamas du
        /// personnage comme <c>PopupQuantity</c> du client, StarLoco n'acceptant qu'un entier de 32 bits).
        /// </summary>
        public Task<InteractionResult> SetPayKamasAsync(long kamas, CraftPayment type)
        {
            InteractionResult refused = CheckClient();
            if (refused != null) return Task.FromResult(refused);
            if (kamas < 0) return Task.FromResult(Refuse("Montant de kamas invalide."));
            long owned = Math.Max(0, Account?.Game?.character?.Kamas ?? 0);
            if (kamas > owned) kamas = owned;
            if (kamas > int.MaxValue) kamas = int.MaxValue;
            return SendAsync("EP" + ((int)type).ToString(CultureInfo.InvariantCulture) + "G" + kamas.ToString(CultureInfo.InvariantCulture),
                kamas + " kamas " + (type == CraftPayment.Pay ? "de paiement" : "de prime si réussite") + " proposés.");
        }

        /// <summary>Propose un objet du sac en paiement ou en prime : <c>EP&lt;type&gt;O+&lt;objet&gt;|&lt;quantité&gt;</c> (client seulement).</summary>
        public Task<InteractionResult> AddPayItemAsync(uint inventoryId, int quantity, CraftPayment type)
        {
            InteractionResult refused = CheckClient();
            if (refused != null) return Task.FromResult(refused);
            InventoryObjects item = Account?.Game?.character?.Inventory?.GetByInventoryId(inventoryId);
            if (item == null) return Task.FromResult(Refuse("Cet objet n'est pas dans votre inventaire."));
            if (item.IsEquipped()) return Task.FromResult(Refuse("Déséquipez cet objet avant de le proposer."));
            int available = item.Qua - PlacedQuantity(inventoryId) - OfferedQuantity(inventoryId);
            if (available <= 0) return Task.FromResult(Refuse("Tout cet objet est déjà posé ou proposé."));
            if (quantity <= 0 || quantity > available) return Task.FromResult(Refuse("Quantité invalide (1 à " + available + ")."));
            return SendAsync("EP" + ((int)type).ToString(CultureInfo.InvariantCulture) + "O+" + inventoryId.ToString(CultureInfo.InvariantCulture) + "|" + quantity.ToString(CultureInfo.InvariantCulture),
                quantity + " × " + item.Name + (type == CraftPayment.Pay ? " proposés en paiement." : " proposés en prime si réussite."));
        }

        /// <summary>Retire un objet du paiement ou de la prime : <c>EP&lt;type&gt;O-&lt;objet&gt;|&lt;quantité&gt;</c> (client seulement).</summary>
        public Task<InteractionResult> RemovePayItemAsync(uint inventoryId, int quantity, CraftPayment type)
        {
            InteractionResult refused = CheckClient();
            if (refused != null) return Task.FromResult(refused);
            ExchangeItem offered;
            lock (sync) offered = (type == CraftPayment.Pay ? pay : payIfSuccess).FirstOrDefault(item => item.Id == inventoryId)?.Copy();
            if (offered == null) return Task.FromResult(Refuse("Cet objet n'est pas proposé " + (type == CraftPayment.Pay ? "en paiement." : "en prime.")));
            if (quantity <= 0 || quantity > offered.Quantity) return Task.FromResult(Refuse("Quantité invalide (1 à " + offered.Quantity + ")."));
            return SendAsync("EP" + ((int)type).ToString(CultureInfo.InvariantCulture) + "O-" + inventoryId.ToString(CultureInfo.InvariantCulture) + "|" + quantity.ToString(CultureInfo.InvariantCulture),
                quantity + " × " + offered.Name + " retirés " + (type == CraftPayment.Pay ? "du paiement." : "de la prime."));
        }

        /// <summary>
        /// « Combiner » (<c>SecureCraft.validCraft</c>) : <c>EK</c>, qui valide ou retire sa validation. Refusé sans ingrédient, avec plus de piles
        /// que de cases (<c>NOT_ENOUGHT_CRAFT_SLOT</c>) et pendant les 3 secondes qui suivent une modification. L'appelant demande d'abord
        /// confirmation si <see cref="ExpectedRecipe"/> est <c>null</c> (<c>WRONG_CRAFT_CONFIRM</c>).
        /// </summary>
        public async Task<InteractionResult> CombineAsync()
        {
            if (!IsOpen) return Refuse("Aucun artisanat sécurisé ouvert.");
            lock (sync)
            {
                if (validatePending) return Refuse("Validation déjà envoyée ; le serveur confirme.");
                if (combinePending) return Refuse("Une fabrication est déjà en cours.");
            }
            if (!LocalReady)
            {
                int count = IngredientCount;
                if (count == 0) return Refuse("Posez au moins un ingrédient avant de combiner.");
                if (Slots > 0 && count > Slots)
                    return Refuse(JobCatalog.Text("NOT_ENOUGHT_CRAFT_SLOT", "Vous ne pouvez faire que des recettes à " + Slots + " cases.", Slots.ToString(CultureInfo.InvariantCulture)));
            }
            TimeSpan wait = ValidationWait;
            if (wait > TimeSpan.Zero) return Refuse("La fenêtre vient de changer : validation possible dans " + Math.Ceiling(wait.TotalSeconds) + " s.");
            lock (sync) validatePending = true;
            InteractionResult sent = await SendAsync("EK", LocalReady ? "Validation retirée ; le serveur confirme." : "Validation envoyée ; le serveur confirme.").ConfigureAwait(false);
            if (!sent.Sent) lock (sync) validatePending = false;
            Notify();
            return sent;
        }

        /// <summary>Envoie <c>EV</c> ; la fenêtre se ferme au <c>EV</c> du serveur (envoyé aux deux joueurs).</summary>
        public Task<InteractionResult> LeaveAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun artisanat sécurisé ouvert."));
            return SendAsync("EV", "Fermeture de l'artisanat sécurisé demandée.");
        }

        private InteractionResult CheckEditable()
        {
            if (!IsOpen) return Refuse("Aucun artisanat sécurisé ouvert.");
            if (IsCombinePending) return Refuse("Attendez la fin de la fabrication.");
            return null;
        }

        private InteractionResult CheckClient()
        {
            InteractionResult refused = CheckEditable();
            if (refused != null) return refused;
            if (!IsClient) return Refuse("Seul le client propose le paiement.");
            return null;
        }

        /// <summary><c>ECK12|&lt;cases&gt;;&lt;compétence&gt;</c> (artisan) ou <c>ECK13|…</c> (client) ; la demande en attente est consommée.</summary>
        protected override void HandleCreated(int type, string data)
        {
            string[] fields = data.Split(';');
            int slots = 0, skill = 0;
            bool readable = fields.Length >= 2 && TryInt(fields[0], out slots) && slots >= 0 && TryInt(fields[1], out skill) && skill > 0 && skill <= short.MaxValue;
            if (!readable) { Malformed("ECK" + type, data); slots = Math.Max(0, slots); skill = Math.Max(0, Math.Min(short.MaxValue, skill)); }
            Slots = slots;
            SkillId = (short)skill;
            PlayerExchange players = Account?.Game?.Interactions?.Exchanges?.Get<PlayerExchange>();
            ExchangeRequest request = players?.PendingRequest;
            players?.ForgetRequest();
            PartnerId = request?.PartnerId ?? 0;
            PartnerName = PartnerId == 0 ? "l'autre joueur" : ResolveName(PartnerId);
            Touch();
            MarkOpen();
            Log((IsClient ? "Artisanat sécurisé ouvert chez l'artisan " : "Artisanat sécurisé ouvert pour ") + PartnerName
                + (SkillId > 0 ? " : « " + SkillName + " »" : string.Empty) + (Slots > 0 ? ", " + Slots + " " + JobCatalog.SlotWord(Slots) : string.Empty) + ".");
            Notify();
        }

        /// <summary><c>EMKO+&lt;objet&gt;|&lt;quantité totale&gt;</c> ou <c>EMKO-&lt;objet&gt;</c> : ses ingrédients.</summary>
        protected override void HandleLocalMovement(string data)
        {
            if (!ApplyMovement(data, true)) Malformed("EMK", data);
        }

        /// <summary><c>EmKO+&lt;objet&gt;|&lt;quantité&gt;|&lt;modèle&gt;|&lt;effets&gt;</c> ou <c>EmKO-&lt;objet&gt;</c> : les ingrédients de l'autre.</summary>
        protected override void HandleDistantMovement(string data)
        {
            if (!ApplyMovement(data, false)) Malformed("EmK", data);
        }

        /// <summary><c>EK&lt;0|1&gt;&lt;acteur&gt;</c> ; les deux prêts : la fabrication est en cours.</summary>
        protected override void HandleReady(string data)
        {
            if (data.Length < 2 || (data[0] != '0' && data[0] != '1') || !TryInt(data.Substring(1), out int actor)) { Malformed("EK", data); return; }
            bool ready = data[0] == '1';
            int self = Account?.Game?.character?.id ?? 0;
            bool mine = actor == self && self != 0;
            if (mine) { LocalReady = ready; lock (sync) validatePending = false; }
            else DistantReady = ready;
            lock (sync) if (LocalReady && DistantReady) combinePending = true;
            Notify();
        }

        /// <summary>
        /// <c>Ep&lt;type&gt;;G&lt;kamas&gt;</c>, <c>Ep&lt;type&gt;;O+&lt;objet&gt;|&lt;total&gt;[|&lt;modèle&gt;|&lt;effets&gt;]</c>, <c>Ep&lt;type&gt;;O-&lt;objet&gt;</c>
        /// (<c>Exchange.onPaymentMovement</c> du client) ; type 1 paiement, 2 prime si réussite.
        /// </summary>
        internal void OnPayment(string data)
        {
            data = data ?? string.Empty;
            int separator = data.IndexOf(';');
            if (separator < 1 || !TryInt(data.Substring(0, separator), out int kind) || (kind != 1 && kind != 2)) { Malformed("Ep", data); return; }
            string body = data.Substring(separator + 1);
            bool ifSuccess = kind == 2;
            if (body.Length >= 2 && body[0] == 'G')
            {
                if (!TryLong(body.Substring(1), out long kamas) || kamas < 0) { Malformed("Ep", data); return; }
                if (ifSuccess) PayIfSuccessKamas = kamas; else PayKamas = kamas;
                Touch();
                Notify();
                return;
            }
            if (body.Length < 3 || body[0] != 'O' || (body[1] != '+' && body[1] != '-')) { Malformed("Ep", data); return; }
            string[] fields = body.Substring(2).Split('|');
            if (!TryId(fields[0], out uint id)) { Malformed("Ep", data); return; }
            if (body[1] == '-')
            {
                lock (sync)
                {
                    if (ifSuccess) payIfSuccess = payIfSuccess.Where(entry => entry.Id != id).ToList();
                    else pay = pay.Where(entry => entry.Id != id).ToList();
                }
                Touch();
                Notify();
                return;
            }
            if (fields.Length < 2 || !TryInt(fields[1], out int quantity) || quantity <= 0) { Malformed("Ep", data); return; }
            int template = 0;
            if (fields.Length > 2 && !TryInt(fields[2], out template)) { Malformed("Ep", data); return; }
            ExchangeItem item = Describe(id, quantity, template, fields.Length > 3 ? fields[3] : null);
            lock (sync)
            {
                List<ExchangeItem> side = (ifSuccess ? payIfSuccess : pay).ToList();
                int index = side.FindIndex(entry => entry.Id == id);
                if (index >= 0) side[index] = item; else side.Add(item);
                if (ifSuccess) payIfSuccess = side; else pay = side;
            }
            Touch();
            Notify();
        }

        /// <summary>
        /// <c>EcK;&lt;modèle&gt;;&lt;T|B&gt;&lt;nom&gt;;&lt;effets&gt;</c> (<paramref name="success"/> vrai, corps après <c>EcK</c>) ou <c>EcEI</c> / <c>EcEF</c> (corps <c>I</c> / <c>F</c>).
        /// Comme <c>Exchange.onCraft</c>, ingrédients, paiements et validations sont oubliés (StarLoco les a vidés sans paquet).
        /// </summary>
        /// <returns>Faux si le paquet est illisible (rien n'a changé).</returns>
        internal bool OnCraft(bool success, string body)
        {
            body = body ?? string.Empty;
            string message;
            if (success)
            {
                string[] fields = (body.StartsWith(";", StringComparison.Ordinal) ? body.Substring(1) : body).Split(';');
                if (!TryInt(fields[0], out int template) || template <= 0) { Malformed("EcK", body); return false; }
                string name = JobCatalog.ItemName(template);
                string other = fields.Length > 1 && fields[1].Length > 1 && (fields[1][0] == 'T' || fields[1][0] == 'B') ? fields[1].Substring(1) : PartnerName;
                message = IsClient
                    ? JobCatalog.Text("CRAFT_SUCCESS_OTHER", other + " vous a créé l'objet " + name + " !", other).Replace("°0", name)
                    : JobCatalog.Text("CRAFT_SUCCESS_TARGET", "Vous avez créé l'objet " + name + " pour " + other + " !", other).Replace("°0", name);
                lock (sync)
                {
                    outcome = CraftOutcome.Success; resultTemplateId = template; resultEffects = fields.Length > 2 ? fields[2] : string.Empty;
                    ClearRound();
                }
                Log(message);
                Inform(message, false);
            }
            else
            {
                char code = body.Length > 0 ? body[0] : '?';
                if (code != 'I' && code != 'F') { Malformed("EcE", body); return false; }
                bool failed = code == 'F';
                message = failed ? JobCatalog.Text("CRAFT_FAILED", "La recette est bonne mais a échoué !") : JobCatalog.Text("NO_CRAFT_RESULT", "Cette recette ne donne rien !");
                lock (sync)
                {
                    outcome = failed ? CraftOutcome.Failed : CraftOutcome.NoResult; resultTemplateId = 0; resultEffects = string.Empty;
                    ClearRound();
                }
                LogError(message);
                Inform(message, true);
            }
            Touch();
            Notify();
            return true;
        }

        private void ClearRound()
        {
            local = new List<ExchangeItem>(); distant = new List<ExchangeItem>();
            pay = new List<ExchangeItem>(); payIfSuccess = new List<ExchangeItem>();
            PayKamas = 0; PayIfSuccessKamas = 0;
            LocalReady = false; DistantReady = false;
            validatePending = false; combinePending = false;
        }

        protected override void HandleLeave(string suffix)
        {
            bool wasOpen = IsOpen;
            Reset();
            MarkClosed();
            if (wasOpen) Log(suffix == "a" ? ExchangeRegistry.Text("EXCHANGE_OK", "Échange effectué.") : "Artisanat sécurisé terminé.");
            Notify();
        }

        protected override void ResetState()
        {
            lock (sync)
            {
                ClearRound();
                outcome = CraftOutcome.None; resultTemplateId = 0; resultEffects = string.Empty;
            }
            Slots = 0; SkillId = 0; PartnerId = 0; PartnerName = string.Empty;
        }

        private bool ApplyMovement(string data, bool mine)
        {
            if (data.Length < 3 || data[0] != 'O' || (data[1] != '+' && data[1] != '-')) return false;
            string[] fields = data.Substring(2).Split('|');
            if (!TryId(fields[0], out uint id)) return false;
            if (data[1] == '-')
            {
                lock (sync)
                {
                    if (mine) local = local.Where(entry => entry.Id != id).ToList();
                    else distant = distant.Where(entry => entry.Id != id).ToList();
                }
                Touch();
                Notify();
                return true;
            }
            if (fields.Length < 2 || !TryInt(fields[1], out int quantity) || quantity <= 0) return false;
            int template = 0;
            if (!mine && (fields.Length < 3 || !TryInt(fields[2], out template))) return false;
            ExchangeItem item = Describe(id, quantity, template, fields.Length > 3 ? fields[3] : null);
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

        /// <summary>Fiche d'un objet : depuis le sac quand c'est un exemplaire du personnage, sinon d'après le modèle transmis.</summary>
        private ExchangeItem Describe(uint id, int quantity, int template, string effects)
        {
            InventoryObjects owned = Account?.Game?.character?.Inventory?.GetByInventoryId(id);
            if (owned != null && template <= 0) template = owned.ID;
            return new ExchangeItem
            {
                Id = id, Quantity = quantity, TemplateId = template,
                Effects = effects ?? owned?.Stats ?? string.Empty,
                Name = owned?.Name ?? (template > 0 ? InventoryObjects.DisplayName(template) : "Objet n° " + id.ToString(CultureInfo.InvariantCulture))
            };
        }

        private void Inform(string text, bool error)
        {
            try { Account?.Game?.Chat?.AddLocal(error ? ChatMessageKind.Error : ChatMessageKind.Info, text); }
            catch (Exception failure) { Account?.Logger?.LogException(Reference, failure); }
        }

        private void Touch() => Volatile.Write(ref lastChange, Stopwatch.GetTimestamp());

        private string ResolveName(int actorId)
        {
            var map = Account?.Game?.Map;
            if (map?.Entites != null && map.Entites.TryGetValue(actorId, out Entites entity) && !string.IsNullOrEmpty(entity?.Name)) return entity.Name;
            return "Joueur " + actorId.ToString(CultureInfo.InvariantCulture);
        }
    }
}
