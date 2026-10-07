using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Jobs;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Tool_BotProtocol.Game.Exchanges
{
    /// <summary>Issue de la dernière fabrication (<c>Exchange.onCraft</c> du client 1.34).</summary>
    public enum CraftOutcome
    {
        None,
        /// <summary><c>EcK;&lt;modèle&gt;</c> : objet créé.</summary>
        Success,
        /// <summary><c>EcEF</c> : « La recette est bonne mais a échoué ! » (<c>CRAFT_FAILED</c>).</summary>
        Failed,
        /// <summary><c>EcEI</c> : « Cette recette ne donne rien ! » (<c>NO_CRAFT_RESULT</c>), ingrédients manquants compris.</summary>
        NoResult
    }

    /// <summary>
    /// Atelier (type 3), l'interface <c>Craft</c> du client 1.34 (propriétaire : lot F6). Une compétence d'atelier utilisée par
    /// <c>GA500&lt;cellule&gt;;&lt;compétence&gt;</c> (lot M3) ouvre la fenêtre : StarLoco répond <c>ECK3|&lt;cases&gt;;&lt;compétence&gt;</c>
    /// (<c>JobAction.startAction</c>). Poser un ingrédient envoie <c>EMO+&lt;objet&gt;|&lt;quantité&gt;</c>, le retirer <c>EMO-&lt;objet&gt;|&lt;quantité&gt;</c> ;
    /// le serveur répond <c>EMKO+&lt;objet&gt;|&lt;quantité posée&gt;</c> ou <c>EMKO-&lt;objet&gt;</c> (<c>JobAction.addIngredient</c>). « Combiner »
    /// envoie <c>EK</c>, suivi de <c>EMR&lt;n-1&gt;</c> pour fabriquer n objets (<c>Craft.validCraft</c>, <c>Exchange.repeatCraft</c>) ; StarLoco ne
    /// renvoie pas <c>EK</c> : il retire les ingrédients du sac (<c>OR</c>/<c>OQ</c>), envoie <c>EmKO+&lt;objet&gt;|1|&lt;modèle&gt;|&lt;effets&gt;</c> et
    /// <c>EcK;&lt;modèle&gt;</c> (réussite), <c>EcEF</c> (échec) ou <c>EcEI</c> (recette inconnue), puis <c>JX</c>. Une série annonce chaque objet par
    /// <c>EA&lt;restants&gt;</c> et se termine par <c>Ea&lt;1 réussie|2 interrompue|3 ressources épuisées|4 recette invalide&gt;</c> ; <c>EMr</c> l'interrompt.
    /// <c>EL</c> (« Mémoire ») repose les ingrédients de la dernière recette. Fermer envoie <c>EV</c>. La forgemagie (compétence avec
    /// <c>SK.f</c>) s'ouvre ici aussi mais n'est pas assistée ; l'artisanat sécurisé (<c>ER12</c>/<c>ER13</c>, <c>EP</c>) n'est pas proposé.
    /// </summary>
    [ExchangeType(ExchangeTypes.Craft)]
    public sealed class CraftExchange : ExchangeWindow
    {
        private readonly object sync = new object();
        private List<ExchangeItem> ingredients = new List<ExchangeItem>();
        private ExchangeItem result;
        private CraftOutcome outcome;
        private int resultTemplateId;
        private bool looping;
        private int loopTotal, loopRemaining = -1, loopEndCode;
        private bool combinePending;

        internal CraftExchange(Accounts.Accounts account) : base(account) { }

        protected override string Reference => "ATELIER";
        protected override AccountStates OpenState => AccountStates.EXCHANGE;

        /// <summary>Nombre de cases de l'atelier (<c>maxItem</c>), 0 s'il n'a pas pu être lu.</summary>
        public int Slots { get; private set; }
        /// <summary>Compétence d'atelier utilisée (<c>skillId</c>), 0 fenêtre fermée.</summary>
        public short SkillId { get; private set; }
        /// <summary>Compétence de forgemagie (<c>Lang.getSkillForgemagus</c> &gt; 0) : le client ouvre alors <c>ForgemagusCraft</c>.</summary>
        public bool IsForgemagus { get; private set; }
        /// <summary>Nom de la compétence (textes du client) ou son numéro.</summary>
        public string SkillName => SkillId > 0 ? JobCatalog.SkillName(SkillId) : string.Empty;
        /// <summary>Ingrédients posés (<c>localGarbage</c>), dans l'ordre où le serveur les a acceptés.</summary>
        public IReadOnlyList<ExchangeItem> Ingredients { get { lock (sync) return ingredients.Select(item => item.Copy()).ToArray(); } }
        /// <summary>Dernier objet créé (<c>EmKO+</c>, côté « distant » de l'atelier), ou <c>null</c>.</summary>
        public ExchangeItem Result { get { lock (sync) return result?.Copy(); } }
        public CraftOutcome LastOutcome { get { lock (sync) return outcome; } }
        /// <summary>Modèle annoncé par le dernier <c>EcK;&lt;modèle&gt;</c>, 0 sinon.</summary>
        public int LastResultTemplateId { get { lock (sync) return resultTemplateId; } }
        /// <summary>Série en cours (<c>EMR</c> envoyé, <c>Ea</c> non encore reçu).</summary>
        public bool IsLooping { get { lock (sync) return looping; } }
        /// <summary>Objets demandés en plus du premier (<c>EMR&lt;n&gt;</c>), 0 hors série.</summary>
        public int LoopTotal { get { lock (sync) return loopTotal; } }
        /// <summary>Dernier compteur <c>EA&lt;n&gt;</c> (objets restant après celui en cours), -1 avant le premier.</summary>
        public int LoopRemaining { get { lock (sync) return loopRemaining; } }
        /// <summary>Dernier code <c>Ea</c> reçu (1 à 4), 0 sinon.</summary>
        public int LastLoopEnd { get { lock (sync) return loopEndCode; } }
        /// <summary>« Combiner » envoyé, réponse <c>Ec</c> pas encore reçue.</summary>
        public bool IsCombinePending { get { lock (sync) return combinePending; } }

        /// <summary>Métier du personnage qui possède la compétence de l'atelier, ou <c>null</c>.</summary>
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

        /// <summary>
        /// Recette correspondant aux ingrédients posés (<c>GameManager.analyseReceipts</c> sur les ingrédients fusionnés par modèle),
        /// ou <c>null</c> : le client demande alors confirmation (<c>WRONG_CRAFT_CONFIRM</c>) avant de combiner.
        /// </summary>
        public Recipe ExpectedRecipe
        {
            get
            {
                if (SkillId <= 0) return null;
                IReadOnlyDictionary<int, int> placed = PlacedByTemplate();
                return placed.Count == 0 ? null : JobCatalog.Analyse(placed, SkillId, Slots > 0 ? Slots : int.MaxValue);
            }
        }

        /// <summary>Ingrédients posés regroupés par modèle (<c>GameManager.mergeUnicItemInInventory</c>).</summary>
        public IReadOnlyDictionary<int, int> PlacedByTemplate()
        {
            var merged = new Dictionary<int, int>();
            foreach (ExchangeItem item in Ingredients)
            {
                if (item.TemplateId <= 0) continue;
                merged.TryGetValue(item.TemplateId, out int quantity);
                merged[item.TemplateId] = quantity + item.Quantity;
            }
            return merged;
        }

        /// <summary>
        /// Nombre maximal d'objets pour une série (bouton « Qté » de <c>Craft</c>) : pour chaque ingrédient posé, quantité restant dans le sac
        /// divisée par la quantité posée, plus un ; 0 sans ingrédient, 1 si un ingrédient n'est plus dans le sac.
        /// </summary>
        public int MaxRepeat
        {
            get
            {
                ExchangeItem[] placed = Ingredients.ToArray();
                if (placed.Length == 0) return 0;
                InventoryClass bag = Account?.Game?.character?.Inventory;
                long best = long.MaxValue;
                foreach (ExchangeItem item in placed)
                {
                    if (item.Quantity <= 0) return 1;
                    InventoryObjects owned = bag?.GetByInventoryId(item.Id);
                    long remaining = owned == null ? 0 : Math.Max(0, owned.Qua - item.Quantity);
                    best = Math.Min(best, remaining / item.Quantity);
                }
                return (int)Math.Min(int.MaxValue - 1, best) + 1;
            }
        }

        /// <summary>Quantité de l'objet du sac déjà posée sur l'atelier.</summary>
        public int PlacedQuantity(uint inventoryId)
        {
            lock (sync) return ingredients.FirstOrDefault(item => item.Id == inventoryId)?.Quantity ?? 0;
        }

        /// <summary>Pose un ingrédient : <c>EMO+&lt;objet&gt;|&lt;quantité&gt;</c> (objet non équipé du sac, case libre ou ingrédient déjà posé).</summary>
        public Task<InteractionResult> AddIngredientAsync(uint inventoryId, int quantity)
        {
            InteractionResult refused = CheckEditable();
            if (refused != null) return Task.FromResult(refused);
            InventoryObjects item = Account?.Game?.character?.Inventory?.GetByInventoryId(inventoryId);
            if (item == null) return Task.FromResult(Refuse("Cet objet n'est pas dans votre inventaire."));
            if (item.IsEquipped()) return Task.FromResult(Refuse("Déséquipez cet objet avant de le poser sur l'atelier."));
            int placed = PlacedQuantity(inventoryId);
            int available = item.Qua - placed;
            if (available <= 0) return Task.FromResult(Refuse("Tout cet objet est déjà posé sur l'atelier."));
            if (quantity <= 0 || quantity > available) return Task.FromResult(Refuse("Quantité invalide (1 à " + available + ")."));
            int used;
            lock (sync) used = ingredients.Count;
            if (placed == 0 && Slots > 0 && used >= Slots)
                return Task.FromResult(Refuse("L'atelier n'a que " + Slots + " " + JobCatalog.SlotWord(Slots) + " : retirez d'abord un ingrédient."));
            return SendAsync("EMO+" + inventoryId.ToString(CultureInfo.InvariantCulture) + "|" + quantity.ToString(CultureInfo.InvariantCulture),
                "Ingrédient posé : " + quantity + " × " + item.Name + ".");
        }

        /// <summary>Retire un ingrédient : <c>EMO-&lt;objet&gt;|&lt;quantité&gt;</c>.</summary>
        public Task<InteractionResult> RemoveIngredientAsync(uint inventoryId, int quantity)
        {
            InteractionResult refused = CheckEditable();
            if (refused != null) return Task.FromResult(refused);
            ExchangeItem placed;
            lock (sync) placed = ingredients.FirstOrDefault(item => item.Id == inventoryId)?.Copy();
            if (placed == null) return Task.FromResult(Refuse("Cet objet n'est pas posé sur l'atelier."));
            if (quantity <= 0 || quantity > placed.Quantity) return Task.FromResult(Refuse("Quantité invalide (1 à " + placed.Quantity + ")."));
            return SendAsync("EMO-" + inventoryId.ToString(CultureInfo.InvariantCulture) + "|" + quantity.ToString(CultureInfo.InvariantCulture),
                "Ingrédient retiré : " + quantity + " × " + placed.Name + ".");
        }

        /// <summary>
        /// « Combiner » : <c>EK</c>, puis <c>EMR&lt;n-1&gt;</c> si <paramref name="count"/> &gt; 1 (série, <c>CRAFT_LOOP_START</c>).
        /// L'appelant demande d'abord confirmation si <see cref="ExpectedRecipe"/> est <c>null</c> (comme l'option <c>AskForWrongCraft</c>).
        /// </summary>
        public async Task<InteractionResult> CombineAsync(int count = 1)
        {
            InteractionResult refused = CheckEditable();
            if (refused != null) return refused;
            int max = MaxRepeat;
            if (max <= 0) return Refuse("Posez au moins un ingrédient avant de combiner.");
            if (count <= 0 || count > max) return Refuse("Quantité invalide (1 à " + max + ").");
            lock (sync) { if (combinePending) return Refuse("Une fabrication est déjà en cours."); combinePending = true; }
            InteractionResult ready = await SendAsync("EK", count > 1
                ? JobCatalog.Text("CRAFT_LOOP_START", "La fabrication de " + count + " objets commence !", count.ToString(CultureInfo.InvariantCulture))
                : "Fabrication demandée.").ConfigureAwait(false);
            if (!ready.Sent) { lock (sync) combinePending = false; return ready; }
            if (count == 1) { Notify(); return ready; }
            int extra = count - 1;
            lock (sync) { looping = true; loopTotal = extra; loopRemaining = -1; loopEndCode = 0; }
            InteractionResult repeat = await SendAsync("EMR" + extra.ToString(CultureInfo.InvariantCulture), ready.Message).ConfigureAwait(false);
            if (!repeat.Sent) lock (sync) { looping = false; loopTotal = 0; }
            Notify();
            return repeat.Sent ? ready : repeat;
        }

        /// <summary>« Stop » pendant une série : <c>EMr</c> ; la série s'arrête au <c>Ea2</c> du serveur.</summary>
        public Task<InteractionResult> StopRepeatAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun atelier ouvert."));
            if (!IsLooping) return Task.FromResult(Refuse("Aucune série de fabrication en cours."));
            return SendAsync("EMr", "Interruption de la série demandée.");
        }

        /// <summary>« Mémoire » (<c>Exchange.replayCraft</c>) : <c>EL</c>, le serveur repose les ingrédients de la dernière recette (<c>EMKO+</c>).</summary>
        public Task<InteractionResult> ReplayAsync()
        {
            InteractionResult refused = CheckEditable();
            if (refused != null) return Task.FromResult(refused);
            lock (sync) if (ingredients.Count > 0) return Task.FromResult(Refuse("Retirez d'abord les ingrédients posés."));
            return SendAsync("EL", "Ingrédients de la dernière recette demandés.");
        }

        /// <summary>Envoie <c>EV</c> ; la fenêtre se ferme au <c>EV</c> du serveur.</summary>
        public Task<InteractionResult> LeaveAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun atelier ouvert."));
            return SendAsync("EV", "Fermeture de l'atelier demandée.");
        }

        private InteractionResult CheckEditable()
        {
            if (!IsOpen) return Refuse("Aucun atelier ouvert.");
            if (IsLooping) return Refuse("Attendez la fin de la série de fabrication (ou arrêtez-la).");
            return null;
        }

        /// <summary><c>ECK3|&lt;cases&gt;;&lt;compétence&gt;</c>.</summary>
        protected override void HandleCreated(int type, string data)
        {
            string[] fields = data.Split(';');
            int slots = 0, skill = 0;
            bool readable = fields.Length >= 2 && TryInt(fields[0], out slots) && slots >= 0 && TryInt(fields[1], out skill) && skill > 0 && skill <= short.MaxValue;
            if (!readable) { Malformed("ECK3", data); slots = Math.Max(0, slots); skill = Math.Max(0, Math.Min(short.MaxValue, skill)); }
            Slots = slots;
            SkillId = (short)skill;
            IsForgemagus = SkillId > 0 && JobCatalog.IsForgemagusSkill(SkillId);
            MarkOpen();
            Log((IsForgemagus ? "Atelier de forgemagie ouvert" : "Atelier ouvert") + (SkillId > 0 ? " : « " + SkillName + " »" : string.Empty)
                + (Slots > 0 ? ", " + Slots + " " + JobCatalog.SlotWord(Slots) : string.Empty) + ".");
            Notify();
        }

        /// <summary><c>EMKO+&lt;objet&gt;|&lt;quantité posée&gt;</c> ou <c>EMKO-&lt;objet&gt;</c> (les kamas n'ont pas de sens à l'atelier).</summary>
        protected override void HandleLocalMovement(string data)
        {
            if (data.Length < 3 || data[0] != 'O' || (data[1] != '+' && data[1] != '-')) { Malformed("EMK", data); return; }
            string[] fields = data.Substring(2).Split('|');
            if (!TryId(fields[0], out uint id)) { Malformed("EMK", data); return; }
            if (data[1] == '-')
            {
                lock (sync) ingredients = ingredients.Where(entry => entry.Id != id).ToList();
                Notify();
                return;
            }
            if (fields.Length < 2 || !TryInt(fields[1], out int quantity) || quantity <= 0) { Malformed("EMK", data); return; }
            InventoryObjects owned = Account?.Game?.character?.Inventory?.GetByInventoryId(id);
            int template = owned?.ID ?? 0;
            var item = new ExchangeItem { Id = id, Quantity = quantity, TemplateId = template, Effects = owned?.Stats ?? string.Empty,
                Name = owned?.Name ?? (template > 0 ? InventoryObjects.DisplayName(template) : "Objet n° " + id.ToString(CultureInfo.InvariantCulture)) };
            lock (sync)
            {
                var updated = ingredients.ToList();
                int index = updated.FindIndex(entry => entry.Id == id);
                if (index >= 0) updated[index] = item; else updated.Add(item);
                ingredients = updated;
            }
            Notify();
        }

        /// <summary><c>EmKO+&lt;objet&gt;|&lt;quantité&gt;|&lt;modèle&gt;|&lt;effets&gt;</c> : objet créé (<c>Exchange.modifyDistant</c>) ; <c>EmKO-&lt;objet&gt;</c> le retire.</summary>
        protected override void HandleDistantMovement(string data)
        {
            if (data.Length < 3 || data[0] != 'O' || (data[1] != '+' && data[1] != '-')) { Malformed("EmK", data); return; }
            string[] fields = data.Substring(2).Split('|');
            if (!TryId(fields[0], out uint id)) { Malformed("EmK", data); return; }
            if (data[1] == '-')
            {
                lock (sync) if (result != null && result.Id == id) result = null;
                Notify();
                return;
            }
            if (fields.Length < 3 || !TryInt(fields[1], out int quantity) || quantity <= 0 || !TryInt(fields[2], out int template) || template <= 0)
            {
                Malformed("EmK", data);
                return;
            }
            var created = new ExchangeItem { Id = id, Quantity = quantity, TemplateId = template,
                Effects = fields.Length > 3 ? fields[3] : string.Empty, Name = InventoryObjects.DisplayName(template) };
            lock (sync) result = created;
            Notify();
        }

        /// <summary>
        /// <c>EcK;&lt;modèle&gt;[;&lt;T|B&gt;&lt;nom&gt;;&lt;effets&gt;]</c> (<paramref name="success"/> vrai, corps après <c>EcK</c>), <c>EcEI</c> / <c>EcEF</c>
        /// (corps <c>I</c> / <c>F</c>). Comme <c>Exchange.onCraft</c>, les ingrédients posés sont oubliés hors série (le sac a déjà été mis à jour).
        /// </summary>
        internal void OnCraft(bool success, string body)
        {
            body = body ?? string.Empty;
            string message;
            lock (sync)
            {
                combinePending = false;
                if (IsForgemagus || !looping) ingredients = new List<ExchangeItem>();
            }
            if (success)
            {
                string[] fields = (body.StartsWith(";", StringComparison.Ordinal) ? body.Substring(1) : body).Split(';');
                if (!TryInt(fields[0], out int template) || template <= 0) { Malformed("EcK", body); Notify(); return; }
                string name = JobCatalog.ItemName(template);
                if (fields.Length > 1 && fields[1].Length > 0 && (fields[1][0] == 'T' || fields[1][0] == 'B'))
                {
                    string other = fields[1].Substring(1);
                    message = fields[1][0] == 'T'
                        ? JobCatalog.Text("CRAFT_SUCCESS_TARGET", "Vous avez créé l'objet " + name + " pour " + other + " !", other).Replace("°0", name)
                        : JobCatalog.Text("CRAFT_SUCCESS_OTHER", other + " t'a créé l'objet " + name + " !", other).Replace("°0", name);
                }
                else message = JobCatalog.Text("CRAFT_SUCCESS_SELF", "Vous avez créé l'objet '" + name + "' !", name);
                lock (sync) { outcome = CraftOutcome.Success; resultTemplateId = template; }
                Log(message);
            }
            else
            {
                char code = body.Length > 0 ? body[0] : '?';
                if (code != 'I' && code != 'F') { Malformed("EcE", body); Notify(); return; }
                bool failed = code == 'F';
                message = failed ? JobCatalog.Text("CRAFT_FAILED", "La recette est bonne mais a échoué !")
                    : JobCatalog.Text("NO_CRAFT_RESULT", "Cette recette ne donne rien !");
                lock (sync) { outcome = failed ? CraftOutcome.Failed : CraftOutcome.NoResult; resultTemplateId = 0; result = null; }
                LogError(message);
            }
            Notify();
        }

        /// <summary><c>EA&lt;n&gt;</c> : objet en cours d'une série (<c>CRAFT_LOOP_PROCESS</c>, n = objets restant après lui).</summary>
        internal void OnCraftLoop(string body)
        {
            if (!TryInt(body, out int remaining) || remaining < 0) { Malformed("EA", body); return; }
            int total;
            lock (sync) { loopRemaining = remaining; total = Math.Max(loopTotal, remaining); combinePending = false; }
            string current = (total - remaining + 1).ToString(CultureInfo.InvariantCulture), count = (total + 1).ToString(CultureInfo.InvariantCulture);
            Log(JobCatalog.Text("CRAFT_LOOP_PROCESS", "Fabrication de l'objet " + current + " sur " + count + "...", current, count));
            Notify();
        }

        /// <summary><c>Ea&lt;code&gt;</c> : fin de série — 1 réussie, 2 interrompue, 3 ressources épuisées, 4 recette invalide.</summary>
        internal void OnCraftLoopEnd(string body)
        {
            if (!TryInt(body, out int code)) { Malformed("Ea", body); return; }
            lock (sync)
            {
                looping = false; loopTotal = 0; loopRemaining = -1; loopEndCode = code; combinePending = false;
                if (!IsForgemagus) ingredients = new List<ExchangeItem>();
            }
            switch (code)
            {
                case 1: Log(JobCatalog.Text("CRAFT_LOOP_END_OK", "Tous les objets ont été fabriqués !")); break;
                case 2: LogError(JobCatalog.Text("CRAFT_LOOP_END_INTERRUPT", "La fabrication d'objets a été interrompue.")); break;
                case 3: LogError(JobCatalog.Text("CRAFT_LOOP_END_FAIL", "Vous n'avez plus assez de ressources, la fabrication d'objets a été interrompue.")); break;
                case 4: LogError(JobCatalog.Text("CRAFT_LOOP_END_INVALID", "Votre recette ne donnait rien, la fabrication d'objets a été interrompue.")); break;
                default: LogError("Fin de série de fabrication inconnue (Ea" + body + ")."); break;
            }
            Notify();
        }

        protected override void ResetState()
        {
            lock (sync)
            {
                ingredients = new List<ExchangeItem>();
                result = null; outcome = CraftOutcome.None; resultTemplateId = 0;
                looping = false; loopTotal = 0; loopRemaining = -1; loopEndCode = 0; combinePending = false;
            }
            Slots = 0; SkillId = 0; IsForgemagus = false;
        }
    }
}
