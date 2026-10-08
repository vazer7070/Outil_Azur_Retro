using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.NPC;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Tool_BotProtocol.Game.Interactions
{
    /// <summary>Article d'une boutique PNJ, lu dans <c>EL</c>.</summary>
    public sealed class ShopArticle
    {
        public int TemplateId { get; internal set; }
        public string Stats { get; internal set; } = string.Empty;
        /// <summary>Prix transmis en troisième champ ; null lorsque le serveur ne l'envoie pas (StarLoco omet un prix nul).</summary>
        public int? Price { get; internal set; }
        /// <summary>Nom depuis <c>BotObjets</c>, sinon « Objet n° X ».</summary>
        public string Name { get; internal set; } = string.Empty;
    }

    /// <summary>
    /// Boutique d'un PNJ selon <c>dofus.aks.Exchange</c> du client 1.34 : envoi <c>ER0|&lt;pnj&gt;</c>, réception
    /// <c>ECK0|&lt;pnj&gt;</c> puis <c>EL&lt;modèle&gt;;&lt;effets&gt;[;&lt;prix&gt;]|…</c>, <c>EBK</c>/<c>EBE</c>, <c>ESK</c>/<c>ESE</c>, <c>EV</c> ;
    /// envoi <c>EB&lt;modèle&gt;|&lt;quantité&gt;</c>, <c>ES&lt;objet&gt;|&lt;quantité&gt;</c> et <c>EV</c>. Type 0 de <see cref="ExchangeRegistry"/> :
    /// <c>ECK0</c>, <c>EL</c> et <c>EV</c> lui parviennent par le registre des échanges.
    /// </summary>
    [ExchangeType(ExchangeTypes.NpcShop)]
    public sealed class NpcShop : ExchangeWindow
    {
        /// <summary>Type d'échange « boutique PNJ » dans <c>ER</c>/<c>ECK</c> (<c>TRADING_WITH_NPC</c> chez StarLoco).</summary>
        public const int NpcExchangeType = ExchangeTypes.NpcShop;
        /// <summary>Quantité maximale acceptée par StarLoco dans <c>buy</c>.</summary>
        public const int MaxQuantity = 100000;
        private List<ShopArticle> articles = new List<ShopArticle>();

        public int NpcId { get; private set; } = -1;
        public string NpcName { get; private set; } = string.Empty;
        public IReadOnlyList<ShopArticle> Articles => articles;
        /// <summary>Vrai tant qu'un achat ou une vente attend la réponse du serveur.</summary>
        public bool IsPending { get; private set; }

        protected override string Reference => "BOUTIQUE";
        protected override AccountStates OpenState => AccountStates.BUYING;

        internal NpcShop(Accounts.Accounts account) : base(account) { }

        /// <summary>Envoie <c>ER0|&lt;pnj&gt;</c> ; StarLoco répond <c>ECK0|&lt;pnj&gt;</c> puis <c>EL</c>.</summary>
        public Task<InteractionResult> OpenAsync(int npcId)
        {
            InteractionResult refused = CheckCanOpen();
            if (refused != null) return Task.FromResult(refused);
            return SendAsync("ER" + NpcExchangeType + "|" + npcId, "Ouverture de la boutique de " + ResolveName(npcId) + " demandée.");
        }

        /// <summary>Envoie <c>EB&lt;modèle&gt;|&lt;quantité&gt;</c> pour un article de la liste.</summary>
        public Task<InteractionResult> BuyAsync(int templateId, int quantity)
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucune boutique ouverte."));
            if (IsPending) return Task.FromResult(Refuse("Attendez la réponse du serveur à l'opération précédente."));
            ShopArticle article = articles.FirstOrDefault(entry => entry.TemplateId == templateId);
            if (article == null) return Task.FromResult(Refuse("Cet article n'est pas proposé par la boutique."));
            if (quantity <= 0 || quantity > MaxQuantity) return Task.FromResult(Refuse("Quantité invalide (1 à " + MaxQuantity + ")."));
            int kamas = Account?.Game?.character?.Kamas ?? 0;
            if (article.Price.HasValue && (long)article.Price.Value * quantity > kamas)
                return Task.FromResult(Refuse("Kamas insuffisants : " + ((long)article.Price.Value * quantity) + " demandés, " + kamas + " disponibles."));
            IsPending = true;
            Notify();
            return SendAsync("EB" + templateId + "|" + quantity, "Achat de " + quantity + " × " + article.Name + " demandé ; le serveur confirme.");
        }

        /// <summary>Envoie <c>ES&lt;objet&gt;|&lt;quantité&gt;</c> pour un objet de l'inventaire.</summary>
        public Task<InteractionResult> SellAsync(uint inventoryId, int quantity)
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucune boutique ouverte."));
            if (IsPending) return Task.FromResult(Refuse("Attendez la réponse du serveur à l'opération précédente."));
            InventoryObjects item = Account?.Game?.character?.Inventory?.GetByInventoryId(inventoryId);
            if (item == null) return Task.FromResult(Refuse("Cet objet n'est pas dans votre inventaire."));
            if (quantity <= 0 || quantity > item.Qua) return Task.FromResult(Refuse("Quantité invalide (1 à " + item.Qua + ")."));
            IsPending = true;
            Notify();
            return SendAsync("ES" + inventoryId + "|" + quantity, "Vente de " + quantity + " × " + item.Name + " demandée ; le serveur confirme.");
        }

        /// <summary>Envoie <c>EV</c> ; le serveur confirme par <c>EV</c>.</summary>
        public Task<InteractionResult> LeaveAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucune boutique ouverte."));
            return SendAsync("EV", "Fermeture de la boutique demandée.");
        }

        private string ResolveName(int npcId)
        {
            var map = Account?.Game?.Map;
            if (map != null && map.Entites.TryGetValue(npcId, out Entites entity) && entity is PNJ npc && !string.IsNullOrEmpty(npc.Name))
                return npc.Name;
            return "PNJ " + npcId;
        }

        /// <summary><c>ECK0|&lt;pnj&gt;</c> : boutique ouverte ; la liste des articles suit dans <c>EL</c>.</summary>
        protected override void HandleCreated(int type, string data)
        {
            // Les identifiants de PNJ sur la carte sont négatifs chez StarLoco : seul l'échec de lecture rend le PNJ inconnu.
            bool known = TryInt(data.Split('|')[0], out int npcId);
            NpcId = known ? npcId : -1;
            NpcName = known ? ResolveName(NpcId) : "PNJ inconnu";
            MarkOpen();
            Log("Boutique de " + NpcName + " ouverte ; en attente de la liste des articles.");
            Notify();
        }

        /// <summary><c>EL&lt;modèle&gt;;&lt;effets&gt;[;&lt;prix&gt;]|…</c> : liste de la boutique, lue comme la branche PNJ de <c>Exchange.onList</c>.</summary>
        protected override void HandleList(string payload)
        {
            var list = new List<ShopArticle>();
            foreach (string entry in (payload ?? string.Empty).Split('|'))
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                string[] fields = entry.Split(';');
                if (!int.TryParse(fields[0], out int templateId)) continue;
                var article = new ShopArticle { TemplateId = templateId, Stats = fields.Length > 1 ? fields[1] : string.Empty,
                    Name = InventoryObjects.DisplayName(templateId) };
                if (fields.Length > 2 && int.TryParse(fields[2], out int price)) article.Price = price;
                list.Add(article);
            }
            articles = list;
            Log(list.Count + " article(s) en boutique.");
            Notify();
        }

        /// <summary><c>EBK</c>/<c>EBE</c> : achat accepté ou refusé ; les objets et kamas arrivent par OAK/OQ/As.</summary>
        internal void OnBuy(bool accepted)
        {
            if (!IsOpen)
            {
                // Lot F8 : EBK/EBE répondent aussi à l'achat d'un lot chez un marchand hors ligne (type 4).
                var merchant = Account?.Game?.Interactions?.Exchanges?.Current as MerchantExchange;
                if (merchant != null && merchant.IsOpen) { merchant.OnBuy(accepted); return; }
            }
            IsPending = false;
            if (accepted) Log("Achat accepté par le serveur.");
            else LogError("Achat refusé par le serveur (article, quantité ou kamas).");
            Notify();
        }

        /// <summary><c>ESK</c>/<c>ESE</c> : vente acceptée ou refusée ; l'inventaire est mis à jour par OR/OQ et As.</summary>
        internal void OnSell(bool accepted)
        {
            IsPending = false;
            if (accepted) Log("Vente acceptée par le serveur.");
            else LogError("Vente refusée par le serveur (objet absent ou quantité).");
            Notify();
        }

        /// <summary><c>EV</c> : fin de l'échange ; le client lit un suffixe « a » comme échange validé.</summary>
        protected override void HandleLeave(string suffix)
        {
            bool wasOpen = IsOpen;
            Reset();
            MarkClosed();
            if (wasOpen) Log(suffix == "a" ? "Échange validé, boutique fermée." : "Boutique fermée.");
            Notify();
        }

        protected override void ResetState()
        {
            NpcId = -1;
            NpcName = string.Empty;
            articles = new List<ShopArticle>();
            IsPending = false;
        }
    }
}
