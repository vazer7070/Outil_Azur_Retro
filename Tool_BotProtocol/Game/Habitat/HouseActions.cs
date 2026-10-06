using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps.Entities;

namespace Tool_BotProtocol.Game.Habitat
{
    /// <summary>
    /// Maisons du personnage selon <c>dofus.aks.Houses</c> du client 1.34 et <c>House</c> de StarLoco. À l'arrivée sur une carte,
    /// StarLoco annonce chaque maison dont la porte s'y trouve (<c>hP&lt;id&gt;|&lt;pseudo&gt;;&lt;achetable&gt;[;&lt;guilde&gt;;&lt;emblème&gt;]</c>)
    /// puis, pour le propriétaire, <c>hL+|&lt;id&gt;;&lt;accès&gt;;&lt;en vente&gt;;&lt;prix&gt;</c>. Les compétences de la porte
    /// (acheter 97, vendre 98, modifier le prix 108 : <c>GA500&lt;cellule&gt;;&lt;compétence&gt;</c>, lot M3) et le menu intérieur
    /// (<c>GA507&lt;compétence&gt;</c>, interface <c>HouseIndoor</c>) ouvrent la fenêtre <c>HouseSale</c> sur <c>hCK&lt;id&gt;|&lt;prix&gt;</c> :
    /// acheter envoie <c>hB&lt;prix&gt;</c> (StarLoco ignore le suffixe, répond <c>hV</c> puis recharge <c>hP</c>/<c>hL</c>), vendre
    /// <c>hS&lt;prix&gt;</c> (<c>hV</c> puis <c>hSK&lt;id&gt;|&lt;prix&gt;</c> ; 0 annule la vente), fermer <c>hV</c>. La maison de guilde
    /// se règle par <c>hG</c> (état), <c>hG+</c>, <c>hG-</c> et <c>hG&lt;droits&gt;</c>, réponses <c>hG&lt;id&gt;[;&lt;guilde&gt;;&lt;emblème&gt;;&lt;droits&gt;]</c> ;
    /// <c>hQ&lt;joueur&gt;</c> expulse un visiteur. L'état local ne change qu'à la réception des paquets du serveur.
    /// </summary>
    public sealed class HouseActions : InteractionWindow
    {
        /// <summary>Compétences du menu intérieur que StarLoco exécute (<c>houseAction</c>) : 81 verrouiller, 97 acheter, 98 vendre, 108 modifier le prix.</summary>
        public static readonly short[] ServerIndoorSkills = { 81, 97, 98, 108 };
        private const short LockSkill = 81, BuySkill = 97, SellSkill = 98, ChangePriceSkill = 108;
        private readonly object sync = new object();
        private readonly Dictionary<int, HouseInfo> houses = new Dictionary<int, HouseInfo>();

        internal HouseActions(Accounts.Accounts account) : base(account) { }

        protected override string Reference => "MAISON";
        /// <summary>La fenêtre de vente (<c>HouseSale</c>) bloque le personnage comme un dialogue.</summary>
        protected override AccountStates OpenState => AccountStates.DIALOG;

        /// <summary>Maisons connues (copies), par identifiant.</summary>
        public IReadOnlyList<HouseInfo> Houses { get { lock (sync) return houses.Values.OrderBy(house => house.Id).Select(house => house.Copy()).ToArray(); } }
        /// <summary>Copie de la maison, ou <c>null</c> si le serveur n'en a rien dit.</summary>
        public HouseInfo Get(int id) { lock (sync) return houses.TryGetValue(id, out HouseInfo house) ? house.Copy() : null; }

        /// <summary>Maison de la fenêtre de vente ouverte par <c>hCK</c> ; -1 sans fenêtre.</summary>
        public int SaleHouseId { get; private set; } = -1;
        /// <summary>Prix transmis par <c>hCK</c> (0 : maison non mise en vente, cas du propriétaire).</summary>
        public int SalePrice { get; private set; }
        public HouseInfo SaleHouse => IsOpen && SaleHouseId >= 0 ? Get(SaleHouseId) ?? new HouseInfo { Id = SaleHouseId } : null;
        /// <summary>Vrai entre <c>hB</c>/<c>hS</c> et la réponse du serveur.</summary>
        public bool IsPending { get; private set; }

        /// <summary>Identifiant de la maison dont la carte courante est l'intérieur (<c>H.m</c> des textes du client), ou <c>null</c>.</summary>
        public int? CurrentHouseId
        {
            get
            {
                int mapId = Account?.Game?.Map?.MapID ?? 0;
                return mapId == 0 ? null : HouseTexts.HouseForIndoorMap(mapId);
            }
        }
        /// <summary>Maison dont la carte courante est l'intérieur ; une maison inconnue du serveur n'a que son identifiant.</summary>
        public HouseInfo CurrentHouse
        {
            get
            {
                int? id = CurrentHouseId;
                return id.HasValue ? Get(id.Value) ?? new HouseInfo { Id = id.Value } : null;
            }
        }
        /// <summary><c>isAtHome</c> du client : la carte courante est l'intérieur d'une maison que le compte possède (<c>hL+</c>).</summary>
        public bool IsAtHome => CurrentHouse?.LocalOwner == true;

        /// <summary>Maison dont la porte est sur cette cellule (<c>H.d</c>), avec ce que le serveur en a dit ; <c>null</c> si ce n'est pas une porte.</summary>
        public HouseInfo ForDoor(int mapId, short cellId)
        {
            int? id = HouseTexts.HouseForDoor(mapId, cellId);
            return id.HasValue ? Get(id.Value) ?? new HouseInfo { Id = id.Value } : null;
        }

        // ---- Envois du client -----------------------------------------------------------------------------

        /// <summary>
        /// Menu intérieur (<c>HouseIndoor</c>) : envoie <c>GA507&lt;compétence&gt;</c>. Aucun écho serveur : StarLoco n'agit que si le personnage
        /// est entré par la porte (<c>inHouse</c>) et répond <c>KCK1|8</c> (81) ou <c>hCK</c> (97, 98, 108) ; la compétence 100 n'y est pas traitée.
        /// </summary>
        public Task<InteractionResult> UseIndoorSkillAsync(short skillId)
        {
            InteractionResult refused = CheckCanAct();
            if (refused != null) return Task.FromResult(refused);
            if (!HouseTexts.IndoorSkills().Contains(skillId)) return Task.FromResult(Refuse("Cette compétence n'est pas dans le menu intérieur des maisons."));
            if (CurrentHouse == null) return Task.FromResult(Refuse("Le personnage n'est pas à l'intérieur d'une maison connue."));
            if (IsOpen && skillId != LockSkill) return Task.FromResult(Refuse("La fenêtre de vente est déjà ouverte."));
            string name;
            try { name = LangData.Skill.Name(skillId); }
            catch (Exception) { name = "compétence " + skillId.ToString(CultureInfo.InvariantCulture); }
            string note = ServerIndoorSkills.Contains(skillId) ? string.Empty : " StarLoco ne traite pas cette compétence : aucune réponse attendue.";
            return SendAsync("GA507" + skillId.ToString(CultureInfo.InvariantCulture), "« " + name + " » demandé (GA507" + skillId + ")." + note);
        }

        /// <summary>Fenêtre de vente, côté acheteur : envoie <c>hB&lt;prix&gt;</c> après le contrôle des kamas (<c>NOT_ENOUGH_RICH</c>).</summary>
        public Task<InteractionResult> BuyAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucune fenêtre d'achat ouverte."));
            if (IsPending) return Task.FromResult(Refuse("Attendez la réponse du serveur à l'opération précédente."));
            HouseInfo house = SaleHouse;
            if (house.LocalOwner) return Task.FromResult(Refuse("Cette maison vous appartient déjà."));
            if (SalePrice <= 0) return Task.FromResult(Refuse("Cette maison n'est pas en vente."));
            int kamas = Account?.Game?.character?.Kamas ?? 0;
            if (kamas < SalePrice) return Task.FromResult(Refuse(HouseTexts.Text("NOT_ENOUGH_RICH", "Tu n'as pas assez de kamas pour réaliser cette action.")
                + " (" + SalePrice.ToString(CultureInfo.InvariantCulture) + " demandés, " + kamas.ToString(CultureInfo.InvariantCulture) + " disponibles)"));
            IsPending = true;
            return SendAsync("hB" + SalePrice.ToString(CultureInfo.InvariantCulture),
                "Achat de " + house.Name + " pour " + SalePrice.ToString(CultureInfo.InvariantCulture) + " kamas demandé ; le serveur ferme la fenêtre (hV) puis renvoie les propriétés (hP, hL).");
        }

        /// <summary>Fenêtre de vente, côté propriétaire : envoie <c>hS&lt;prix&gt;</c> ; 0 annule la vente (<c>CANCEL_THE_SALE</c>).</summary>
        public Task<InteractionResult> SellAsync(int price)
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucune fenêtre de vente ouverte."));
            if (IsPending) return Task.FromResult(Refuse("Attendez la réponse du serveur à l'opération précédente."));
            if (price < 0) return Task.FromResult(Refuse("Prix invalide."));
            HouseInfo house = SaleHouse;
            if (!house.LocalOwner) return Task.FromResult(Refuse("Seul le propriétaire peut mettre cette maison en vente."));
            IsPending = true;
            return SendAsync("hS" + price.ToString(CultureInfo.InvariantCulture), price == 0 ? "Annulation de la vente de " + house.Name + " demandée."
                : "Mise en vente de " + house.Name + " à " + price.ToString(CultureInfo.InvariantCulture) + " kamas demandée ; le serveur confirme par hSK.");
        }

        public Task<InteractionResult> CancelSaleAsync() => SellAsync(0);

        /// <summary>Envoie <c>hV</c> ; le serveur ferme la fenêtre par <c>hV</c>.</summary>
        public Task<InteractionResult> LeaveAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucune fenêtre de vente ouverte."));
            return SendAsync("hV", "Fermeture de la fenêtre de vente demandée.");
        }

        /// <summary>Envoie <c>hG</c> : StarLoco répond <c>hG&lt;id&gt;</c> (maison privée) ou <c>hG&lt;id&gt;;&lt;guilde&gt;;&lt;emblème&gt;;&lt;droits&gt;</c>.</summary>
        public Task<InteractionResult> RequestGuildStateAsync() => SendGuildAsync("hG", "État de la maison de guilde demandé.");
        /// <summary>Envoie <c>hG+</c> : partage la maison avec la guilde (refus <c>Im1151</c> au-delà du quota de la guilde).</summary>
        public Task<InteractionResult> ShareWithGuildAsync() => SendGuildAsync("hG+", "Partage de la maison avec la guilde demandé.");
        /// <summary>Envoie <c>hG-</c> : retire la maison de la guilde.</summary>
        public Task<InteractionResult> UnshareAsync() => SendGuildAsync("hG-", "Retrait de la maison de la guilde demandé.");
        /// <summary>Envoie <c>hG&lt;droits&gt;</c> (somme des <see cref="HouseGuildRights"/>) ; StarLoco n'y répond pas.</summary>
        public Task<InteractionResult> SetGuildRightsAsync(HouseGuildRights rights)
        {
            int value = (int)rights;
            if (value < 0) return Task.FromResult(Refuse("Droits invalides."));
            return SendGuildAsync("hG" + value.ToString(CultureInfo.InvariantCulture), "Droits de la maison de guilde envoyés (" + value + ") ; StarLoco ne confirme pas, redemandez l'état.");
        }

        /// <summary>Envoie <c>hQ&lt;joueur&gt;</c> (<c>Houses.kick</c>) ; StarLoco l'ignore hors de sa propre maison.</summary>
        public Task<InteractionResult> KickAsync(long playerId)
        {
            InteractionResult refused = CheckCanAct();
            if (refused != null) return Task.FromResult(refused);
            if (playerId == 0 || playerId == (Account?.Game?.character?.id ?? 0)) return Task.FromResult(Refuse("Choisissez un visiteur de la maison."));
            if (!IsAtHome) return Task.FromResult(Refuse("Le personnage n'est pas chez lui."));
            return SendAsync("hQ" + playerId.ToString(CultureInfo.InvariantCulture), "Expulsion du joueur " + playerId + " demandée.");
        }

        private Task<InteractionResult> SendGuildAsync(string packet, string message)
        {
            InteractionResult refused = CheckCanAct();
            if (refused != null) return Task.FromResult(refused);
            if (CurrentHouse == null) return Task.FromResult(Refuse("Le personnage n'est pas à l'intérieur d'une maison connue."));
            string guild = (Account?.Game?.Map?.Self as PlayerActor)?.GuildName;
            if (string.IsNullOrEmpty(guild)) return Task.FromResult(Refuse("Le personnage n'a pas de guilde : StarLoco ignore ces demandes."));
            return SendAsync(packet, message);
        }

        private InteractionResult CheckCanAct()
        {
            if (Account?.Connexion == null || !Account.Connexion.IsConnected()) return Refuse("Connectez le personnage avant cette action.");
            if (Account.IsFighting()) return Refuse("Action impossible pendant un combat.");
            return null;
        }

        // ---- Réceptions ------------------------------------------------------------------------------------

        /// <summary><c>hP&lt;id&gt;|&lt;pseudo&gt;;&lt;achetable&gt;[;&lt;guilde&gt;;&lt;emblème&gt;]</c> (<c>Houses.onProperties</c>) : propriétaire, vente, guilde.</summary>
        internal void OnProperties(string payload)
        {
            try
            {
                string[] parts = (payload ?? string.Empty).Split('|');
                if (parts.Length < 2 || !TryInt(parts[0], out int id)) { Malformed("hP", payload); return; }
                string[] fields = parts[1].Split(';');
                lock (sync)
                {
                    HouseInfo house = GetOrAdd(id);
                    house.OwnerName = fields[0].Trim();
                    house.IsForSale = fields.Length > 1 && fields[1].Trim() == "1";
                    house.GuildName = fields.Length > 2 ? fields[2].Trim() : string.Empty;
                    house.GuildEmblem = fields.Length > 3 ? fields[3].Trim() : string.Empty;
                    if (!house.IsForSale) house.Price = 0;
                }
                Account?.Logger?.LogDebug(Reference, "Maison " + id + " : " + Get(id).OwnerLine() + (fields.Length > 1 && fields[1] == "1" ? ", en vente." : "."));
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { Account?.Logger?.LogError(Reference, "Paquet hP non appliqué : " + error.Message); }
            Notify();
        }

        /// <summary><c>hL</c> (liste vidée), <c>hL+|&lt;id&gt;;&lt;accès&gt;;&lt;en vente&gt;;&lt;prix&gt;|…</c> (ses maisons), <c>hL-|&lt;id&gt;|…</c> (<c>Houses.onList</c>).</summary>
        internal void OnList(string payload)
        {
            try
            {
                string body = payload ?? string.Empty;
                if (body.Length == 0)
                {
                    lock (sync) foreach (HouseInfo house in houses.Values) house.LocalOwner = false;
                    Notify();
                    return;
                }
                char sign = body[0];
                if (sign != '+' && sign != '-') { Malformed("hL", payload); return; }
                string entries = body.Length > 1 && body[1] == '|' ? body.Substring(2) : body.Substring(1);
                int unreadable = 0, applied = 0;
                foreach (string entry in entries.Split('|'))
                {
                    if (entry.Length == 0) continue;
                    string[] fields = entry.Split(';');
                    if (!TryInt(fields[0], out int id)) { unreadable++; continue; }
                    lock (sync)
                    {
                        if (sign == '+')
                        {
                            HouseInfo house = GetOrAdd(id);
                            house.LocalOwner = true;
                            house.IsLocked = fields.Length > 1 && fields[1].Trim() == "1";
                            house.IsForSale = fields.Length > 2 && fields[2].Trim() == "1";
                            if (fields.Length > 3 && TryInt(fields[3], out int price)) house.Price = Math.Max(0, price);
                            else if (!house.IsForSale) house.Price = 0;
                        }
                        else if (houses.TryGetValue(id, out HouseInfo house)) house.LocalOwner = false;
                    }
                    applied++;
                }
                if (unreadable > 0) Account?.Logger?.LogError(Reference, unreadable + " entrée(s) illisible(s) dans hL ignorée(s).");
                if (applied > 0) Account?.Logger?.LogDebug(Reference, (sign == '+' ? "Maison(s) possédée(s) : " : "Maison(s) retirée(s) : ") + applied + ".");
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { Account?.Logger?.LogError(Reference, "Paquet hL non appliqué : " + error.Message); }
            Notify();
        }

        /// <summary><c>hCK&lt;id&gt;|&lt;prix&gt;</c> (<c>buyIt</c>/<c>sellIt</c>) : ouvre la fenêtre <c>HouseSale</c>.</summary>
        internal void OnSaleWindow(string payload)
        {
            try
            {
                string[] parts = (payload ?? string.Empty).Split('|');
                if (parts.Length < 2 || !TryInt(parts[0], out int id) || !TryInt(parts[1], out int price)) { Malformed("hCK", payload); return; }
                price = Math.Max(0, price);
                lock (sync)
                {
                    HouseInfo house = GetOrAdd(id);
                    house.Price = price;
                    house.IsForSale = price > 0;
                }
                SaleHouseId = id;
                SalePrice = price;
                IsPending = false;
                MarkOpen();
                HouseInfo opened = Get(id);
                Log(opened.LocalOwner ? HouseTexts.Text("HOUSE_SALE", "Mise en vente de la maison") + " : " + opened.Name + (price > 0 ? " (en vente à " + price + " kamas)." : " (pas encore en vente).")
                    : opened.Name + " est proposée à " + price + " kamas.");
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { Account?.Logger?.LogError(Reference, "Paquet hCK non appliqué : " + error.Message); }
            Notify();
        }

        /// <summary><c>hSK&lt;id&gt;|&lt;prix&gt;</c> : mise en vente enregistrée (prix 0 : vente annulée) ; <c>hSE</c> : refus.</summary>
        internal void OnSold(bool accepted, string payload)
        {
            IsPending = false;
            try
            {
                if (!accepted) { LogError("Mise en vente refusée par le serveur (hSE)."); Notify(); return; }
                string[] parts = (payload ?? string.Empty).Split('|');
                if (parts.Length < 2 || !TryInt(parts[0], out int id) || !TryInt(parts[1], out int price)) { Malformed("hSK", payload); Notify(); return; }
                price = Math.Max(0, price);
                lock (sync)
                {
                    HouseInfo house = GetOrAdd(id);
                    house.Price = price;
                    house.IsForSale = price > 0;
                }
                Log(price > 0 ? Get(id).Name + " mise en vente à " + price + " kamas." : "Vente de " + Get(id).Name + " annulée.");
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { Account?.Logger?.LogError(Reference, "Paquet hSK non appliqué : " + error.Message); }
            Notify();
        }

        /// <summary><c>hG&lt;id&gt;</c> (maison privée) ou <c>hG&lt;id&gt;;&lt;guilde&gt;;&lt;emblème&gt;;&lt;droits&gt;</c> (<c>parseHG</c>).</summary>
        internal void OnGuildInfos(string payload)
        {
            try
            {
                string[] fields = (payload ?? string.Empty).Split(';');
                if (!TryInt(fields[0], out int id)) { Malformed("hG", payload); return; }
                bool shared = fields.Length >= 4;
                int rights = 0;
                if (shared && !TryInt(fields[3], out rights)) { Malformed("hG", payload); return; }
                lock (sync)
                {
                    HouseInfo house = GetOrAdd(id);
                    house.GuildRightsKnown = true;
                    house.GuildName = shared ? fields[1].Trim() : string.Empty;
                    house.GuildEmblem = shared ? fields[2].Trim() : string.Empty;
                    house.GuildRights = shared ? Math.Max(0, rights) : 0;
                }
                Log(shared ? Get(id).Name + " partagée avec la guilde " + fields[1].Trim() + " (droits " + Math.Max(0, rights) + ")." : Get(id).Name + " n'est pas partagée avec une guilde.");
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { Account?.Logger?.LogError(Reference, "Paquet hG non appliqué : " + error.Message); }
            Notify();
        }

        /// <summary><c>hV</c> : la fenêtre de vente est fermée (<c>closeBuy</c>, après <c>hB</c>, <c>hS</c> ou <c>hV</c>).</summary>
        internal void OnLeave()
        {
            bool wasOpen = IsOpen;
            Reset();
            MarkClosed();
            if (wasOpen) Log("Fenêtre de vente fermée.");
            Notify();
        }

        /// <summary>Oublie les maisons et la fenêtre (déconnexion, changement de personnage).</summary>
        public new void Clear()
        {
            lock (sync) houses.Clear();
            base.Clear();
        }

        protected override void Reset()
        {
            SaleHouseId = -1;
            SalePrice = 0;
            IsPending = false;
        }

        private HouseInfo GetOrAdd(int id)
        {
            if (!houses.TryGetValue(id, out HouseInfo house)) houses[id] = house = new HouseInfo { Id = id };
            return house;
        }

        private void Malformed(string prefix, string data)
        {
            data = data ?? string.Empty;
            Account?.Logger?.LogError(Reference, "Paquet " + prefix + " illisible ignoré : " + (data.Length > 120 ? data.Substring(0, 120) + "…" : data));
        }

        private static bool TryInt(string value, out int result) =>
            int.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }
}
