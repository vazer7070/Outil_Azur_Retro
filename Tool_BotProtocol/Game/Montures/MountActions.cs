using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Tool_BotProtocol.Game.Montures
{
    /// <summary>
    /// Monture du personnage et enclos de la carte selon <c>dofus.aks.Mount</c> du client 1.34 et StarLoco
    /// (<c>parseMountPacket</c>, <c>toogleOnMount</c>). Reçus : <c>Re+&lt;fiche&gt;</c>/<c>Re-</c>/<c>ReE&lt;code&gt;</c> (monture équipée),
    /// <c>Rx&lt;%&gt;</c> (part d'expérience donnée), <c>Rn&lt;nom&gt;</c>, <c>Rr+</c>/<c>Rr-</c> (sur la monture ou non),
    /// <c>Rd&lt;fiche&gt;</c> (fiche consultée), <c>Rp&lt;propriétaire;prix;taille;objets;guilde;emblème&gt;</c> (enclos de la carte,
    /// transmis à <see cref="InteractiveActions.SetMountPark"/>), <c>RD&lt;prix&gt;|&lt;prix&gt;</c> (fenêtre d'achat ou de vente de
    /// l'enclos, compétences 176 à 178) et <c>Rv</c> (fermeture). Envois, aux formats du client : <c>Rr</c>, <c>Rn&lt;nom&gt;</c>,
    /// <c>Rx&lt;0-90&gt;</c>, <c>Rc</c>, <c>Rf</c>, <c>ER15|</c> (sacoches, <see cref="MountExchange"/>), <c>Rd&lt;p1&gt;|&lt;p2&gt;</c>
    /// (certificat), <c>Rp&lt;monture&gt;</c> (monture d'enclos), <c>Ro&lt;cellule&gt;</c>, <c>Rb&lt;prix&gt;</c>, <c>Rs&lt;prix&gt;</c>, <c>Rv</c>.
    /// L'état local ne change qu'à la réception des paquets du serveur ; un paquet illisible est journalisé, jamais propagé.
    /// </summary>
    public sealed class MountActions : InteractionWindow
    {
        /// <summary>Effet des certificats de monture (<c>Mount.data(param1, param2)</c> de l'<c>ItemViewer</c>).</summary>
        public const int CertificateEffect = 995;
        /// <summary>Caractères refusés dans un nom : ils couperaient la fiche (« : ») ou les listes de l'étable (« ; », « ~ », « | »).</summary>
        private static readonly char[] ForbiddenNameChars = { ':', ';', '~', '|', ',' };

        private readonly object sync = new object();
        private Mount current;
        private Mount viewed;

        internal MountActions(Accounts.Accounts account) : base(account) { }

        protected override string Reference => "MONTURE";
        /// <summary>La fenêtre d'achat ou de vente de l'enclos (<c>MountParkSale</c>) bloque le personnage comme un dialogue.</summary>
        protected override AccountStates OpenState => AccountStates.DIALOG;

        /// <summary>Monture équipée (<c>Re+</c>), ou <c>null</c>.</summary>
        public Mount Current { get { lock (sync) return current; } }
        /// <summary>Le serveur a annoncé une monture équipée (<c>Re+</c>), même si sa fiche était illisible.</summary>
        public bool HasMount => Account != null && Account.CanUseMount;
        /// <summary>Sur la monture (<c>Rr+</c>).</summary>
        public bool IsRiding { get; private set; }
        /// <summary>Part de l'expérience donnée à la monture (<c>Rx</c>), null tant qu'elle n'a pas été reçue.</summary>
        public int? XpPercent { get; private set; }
        /// <summary>Dernière fiche reçue par <c>Rd</c> (certificat ou monture d'enclos), ou <c>null</c>.</summary>
        public Mount Viewed { get { lock (sync) return viewed; } }
        /// <summary>Incrémenté à chaque <c>Rd</c> : le volet s'en sert pour afficher la nouvelle fiche.</summary>
        public int ViewedSerial { get; private set; }
        /// <summary>Prix proposé par <c>RD</c> (prix courant de l'enclos) tant que la fenêtre de vente est ouverte.</summary>
        public int SaleDefaultPrice { get; private set; }
        /// <summary>Enclos de la carte (<c>Rp</c>), partagé avec le menu des objets interactifs.</summary>
        public MountParkInfo Park => Account?.Game?.Interactions?.Interactive?.MountPark;
        /// <summary>Étable ouverte (type 16).</summary>
        public ShedExchange Shed => Account?.Game?.Interactions?.Exchanges?.Get<ShedExchange>();
        /// <summary>Sacoches (type 15).</summary>
        public MountExchange Inventory => Account?.Game?.Interactions?.Exchanges?.Get<MountExchange>();
        /// <summary>Guilde du personnage (<c>Player.guildInfos.name</c>) : celle du lot des guildes, sinon celle de son <c>GM</c>.</summary>
        public string OwnGuild
        {
            get
            {
                Guildes.Guild guild = Account?.Game?.Interactions?.Guild?.Guild;
                if (guild != null && guild.HasGuild) return guild.Name;
                return (Account?.Game?.Map?.Self as PlayerActor)?.GuildName;
            }
        }
        /// <summary>L'enclos de la carte appartient à la guilde du personnage (<c>MountPark.isMine</c>).</summary>
        public bool IsParkMine => Park?.IsMine(OwnGuild) == true;
        /// <summary>
        /// Condition du menu des montures d'enclos (<c>onSpriteRelease</c>) : enclos de la guilde du personnage et droit
        /// « Gérer les montures des autres membres » (<c>canManageOtherMount</c>).
        /// </summary>
        public bool CanManageParkMounts => IsParkMine && Account?.Game?.Interactions?.Guild?.Guild?.CanDo(Guildes.GuildRight.ManageOtherMounts) == true;

        // ---- Envois du client -----------------------------------------------------------------------------

        /// <summary>
        /// <c>Rr</c> : monter ou descendre (<c>Mount.ride</c>). Refusé localement sans monture équipée ou sous le niveau 60
        /// (StarLoco ignore alors la demande sans réponse) ; les autres refus viennent du serveur (<c>ReEr</c>, <c>Im1113</c>…).
        /// </summary>
        public Task<InteractionResult> RideAsync()
        {
            if (!HasMount) return Task.FromResult(Refuse(Mount.Text("MOUNT_NO_EQUIP", "Tu n'as pas de monture équipée.")));
            int level = Account?.Game?.character?.Level ?? 0;
            if (!IsRiding && level < Mount.RideLevel)
                return Task.FromResult(Refuse(Mount.Text("MOUNT_ERROR_RIDE", "Impossible de monter sur la monture") + " (niveau " + Mount.RideLevel + " requis)."));
            return SendAsync("Rr", IsRiding ? "Descente de la monture demandée." : "Montée sur la monture demandée.");
        }

        /// <summary><c>Rn&lt;nom&gt;</c> (<c>Mount.rename</c>) ; un nom vide n'est pas envoyé, comme dans l'interface <c>Mount</c>.</summary>
        public Task<InteractionResult> RenameAsync(string name)
        {
            if (!HasMount) return Task.FromResult(Refuse(Mount.Text("MOUNT_NO_EQUIP", "Tu n'as pas de monture équipée.")));
            if (string.IsNullOrEmpty(name)) return Task.FromResult(Refuse("Indiquez le nouveau nom de la monture."));
            if (name.IndexOfAny(ForbiddenNameChars) >= 0 || name.Any(char.IsControl))
                return Task.FromResult(Refuse("Le nom ne peut contenir ni « : », « ; », « , », « ~ », « | » ni caractère de contrôle."));
            return SendAsync("Rn" + name, "Nouveau nom « " + name + " » demandé pour la monture.");
        }

        /// <summary><c>Rx&lt;pourcentage&gt;</c> (<c>Mount.setXP</c>) : 0 à 90, bornes de la fenêtre <c>PopupQuantity</c> du client.</summary>
        public Task<InteractionResult> SetXpRatioAsync(int percent)
        {
            if (percent < 0 || percent > Mount.MaxXpPercent)
                return Task.FromResult(Refuse("La part d'expérience donnée à la monture va de 0 à " + Mount.MaxXpPercent + " %."));
            if (!HasMount) return Task.FromResult(Refuse(Mount.Text("MOUNT_NO_EQUIP", "Tu n'as pas de monture équipée.")));
            return SendAsync("Rx" + percent.ToString(CultureInfo.InvariantCulture), percent + " % de l'expérience pour la monture demandés.");
        }

        /// <summary><c>Rc</c> : castrer la monture équipée (<c>Mount.castrate</c>, après confirmation dans l'interface).</summary>
        public Task<InteractionResult> CastrateAsync()
        {
            if (!HasMount) return Task.FromResult(Refuse(Mount.Text("MOUNT_NO_EQUIP", "Tu n'as pas de monture équipée.")));
            return SendAsync("Rc", "Castration de la monture demandée.");
        }

        /// <summary>
        /// <c>Rf</c> : libérer la monture équipée (<c>Mount.kill</c>). Refusé sans monture (StarLoco lèverait une exception) ou
        /// quand les sacoches reçues ne sont pas vides (StarLoco répond <c>Im1106</c>).
        /// </summary>
        public Task<InteractionResult> FreeAsync()
        {
            if (!HasMount) return Task.FromResult(Refuse(Mount.Text("MOUNT_NO_EQUIP", "Tu n'as pas de monture équipée.")));
            MountExchange bags = Inventory;
            Mount mount = Current;
            if (bags != null && bags.ContentReceived && mount != null && bags.MountId == mount.Id && bags.Items.Count > 0)
                return Task.FromResult(Refuse(Mount.Text("MOUNT_ERROR_INVENTORY_NOT_EMPTY", "Videz les sacoches de la monture avant de la libérer.")));
            return SendAsync("Rf", "Libération de la monture demandée.");
        }

        /// <summary><c>ER15|</c> : sacoches de la monture (<c>Exchange.request(15)</c>) ; StarLoco répond <c>ECK15</c>, <c>EL</c> et <c>Ew</c>.</summary>
        public Task<InteractionResult> OpenInventoryAsync()
        {
            if (!HasMount) return Task.FromResult(Refuse(Mount.Text("MOUNT_NO_EQUIP", "Tu n'as pas de monture équipée.")));
            InteractionResult refused = CheckCanOpen();
            if (refused != null) return Task.FromResult(refused);
            return SendAsync("ER" + ExchangeTypes.MountStorage.ToString(CultureInfo.InvariantCulture) + "|", "Ouverture des sacoches de la monture demandée.");
        }

        /// <summary><c>Rp&lt;monture&gt;</c> : fiche d'une monture de l'enclos (<c>Mount.parkMountData</c>), réponse <c>Rd</c>.</summary>
        public Task<InteractionResult> ViewParkMountAsync(long mountId)
        {
            if (mountId <= 0) return Task.FromResult(Refuse("Monture d'enclos inconnue."));
            return SendAsync("Rp" + mountId.ToString(CultureInfo.InvariantCulture), "Fiche de la monture d'enclos demandée.");
        }

        /// <summary>
        /// <c>Rd&lt;p1&gt;|&lt;p2&gt;</c> : fiche de la monture d'un certificat (<c>Mount.data(param1, param2)</c> de l'effet 995), réponse
        /// <c>Rd</c>. Les paramètres sont lus comme le client (<c>parseInt(…, 16)</c>, « 0 » ou absent : 0).
        /// </summary>
        public Task<InteractionResult> ViewCertificateAsync(InventoryObjects item)
        {
            if (item == null) return Task.FromResult(Refuse("Aucun certificat sélectionné."));
            if (!TryCertificateParameters(item.Stats, out long first, out long second))
                return Task.FromResult(Refuse("Cet objet n'est pas un certificat de monture (effet 995 absent)."));
            return SendAsync(CertificatePacket(first, second), "Fiche de la monture du certificat demandée.");
        }

        /// <summary>Paquet <c>Rd</c> envoyé pour un certificat.</summary>
        public static string CertificatePacket(long first, long second) =>
            "Rd" + first.ToString(CultureInfo.InvariantCulture) + "|" + second.ToString(CultureInfo.InvariantCulture);

        /// <summary>Paramètres 1 et 2 de l'effet 995 d'une fiche d'objet (hexadécimal non signé, comme <c>parseInt</c>).</summary>
        public static bool TryCertificateParameters(string stats, out long first, out long second)
        {
            first = second = 0;
            if (string.IsNullOrEmpty(stats)) return false;
            foreach (string field in stats.Split(','))
            {
                string[] parts = field.Split('#');
                if (!long.TryParse(parts[0].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long type) || type != CertificateEffect) continue;
                first = Hex(parts, 1); second = Hex(parts, 2);
                return true;
            }
            return false;
        }

        /// <summary>
        /// <c>Ro&lt;cellule&gt;</c> : retirer un objet d'élevage (<c>Mount.removeObjectInPark</c>). Comme <c>onObjectRelease</c>, seulement
        /// pour un objet à durabilité posé dans un enclos de la guilde du personnage.
        /// </summary>
        public Task<InteractionResult> RemoveParkObjectAsync(int cellId)
        {
            if (Park == null) return Task.FromResult(Refuse("Aucun enclos sur cette carte."));
            if (!IsParkMine) return Task.FromResult(Refuse("Cet enclos n'appartient pas à votre guilde."));
            GroundObject placed = ParkObjectAt(cellId);
            if (placed == null) return Task.FromResult(Refuse("Aucun objet d'élevage sur cette cellule."));
            return SendAsync("Ro" + cellId.ToString(CultureInfo.InvariantCulture), "Retrait de l'objet d'élevage demandé.");
        }

        /// <summary>Objet d'élevage (avec durabilité) posé sur cette cellule de la carte, ou <c>null</c>.</summary>
        public GroundObject ParkObjectAt(int cellId)
        {
            Map map = Account?.Game?.Map;
            if (map == null) return null;
            return map.GroundObjects.TryGetValue(cellId, out GroundObject placed) && placed != null && placed.Durability.HasValue ? placed : null;
        }

        /// <summary>
        /// <c>Rb&lt;prix&gt;</c> : acheter l'enclos (<c>MountParkSale</c>, enclos d'une autre guilde, après confirmation). Refusé si
        /// l'enclos n'est pas en vente ou si les kamas manquent (<c>NOT_ENOUGH_RICH</c>).
        /// </summary>
        public Task<InteractionResult> BuyParkAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("La fenêtre d'achat de l'enclos n'est pas ouverte."));
            MountParkInfo park = Park;
            if (park == null || IsParkMine) return Task.FromResult(Refuse("Cet enclos ne peut pas être acheté."));
            if (park.Price <= 0) return Task.FromResult(Refuse("Cet enclos n'est pas en vente."));
            int kamas = Account?.Game?.character?.Kamas ?? 0;
            if (park.Price > kamas) return Task.FromResult(Refuse(Mount.Text("NOT_ENOUGH_RICH", "Tu n'as pas assez de kamas pour réaliser cette action.")));
            return SendAsync("Rb" + park.Price.ToString(CultureInfo.InvariantCulture), "Achat de l'enclos pour " + park.Price + " kamas demandé.");
        }

        /// <summary><c>Rs&lt;prix&gt;</c> : mettre en vente l'enclos de sa guilde ; 0 annule la vente (<c>CANCEL_THE_SALE</c>).</summary>
        public Task<InteractionResult> SellParkAsync(int price)
        {
            if (!IsOpen) return Task.FromResult(Refuse("La fenêtre de vente de l'enclos n'est pas ouverte."));
            if (!IsParkMine) return Task.FromResult(Refuse("Cet enclos n'appartient pas à votre guilde."));
            if (price < 0) return Task.FromResult(Refuse("Prix invalide."));
            return SendAsync("Rs" + price.ToString(CultureInfo.InvariantCulture),
                price == 0 ? "Annulation de la vente de l'enclos demandée." : "Mise en vente de l'enclos à " + price + " kamas demandée.");
        }

        /// <summary><c>Rv</c> : fermer la fenêtre d'achat ou de vente (<c>Mount.leave</c>) ; StarLoco répond <c>Rv</c>.</summary>
        public Task<InteractionResult> CloseSaleAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("La fenêtre de l'enclos n'est pas ouverte."));
            return SendAsync("Rv", "Fermeture de la fenêtre de l'enclos demandée.");
        }

        // ---- Réceptions (MountFrame) ------------------------------------------------------------------------

        /// <summary><c>Re+&lt;fiche&gt;</c>, <c>Re-</c>, <c>ReE&lt;code&gt;</c> (<c>Mount.onEquip</c>).</summary>
        internal void OnEquip(string data)
        {
            if (data.Length == 0) { LogError("Paquet Re vide ignoré."); return; }
            switch (data[0])
            {
                case '+':
                    if (Account != null) Account.CanUseMount = true;
                    Mount mount = Mount.Parse(data.Substring(1));
                    lock (sync) current = mount;
                    if (mount == null) LogError("Fiche de la monture équipée illisible ; la monture reste annoncée par le serveur.");
                    else Log("Monture équipée : " + mount.Describe() + ".");
                    break;
                case '-':
                    if (Account != null) Account.CanUseMount = false;
                    lock (sync) current = null;
                    SetRiding(false);
                    Log("Plus de monture équipée.");
                    break;
                case 'E':
                    LogError(EquipError(data.Length > 1 ? data[1] : ' '));
                    break;
                default:
                    LogError("Paquet Re" + data + " ignoré (signe inconnu).");
                    return;
            }
            Notify();
        }

        /// <summary>Texte de <c>Mount.equipError</c> : « - » sacoches non vides, « + » déjà une monture, « r » montée impossible.</summary>
        public static string EquipError(char code)
        {
            switch (code)
            {
                case '-': return Mount.Text("MOUNT_ERROR_INVENTORY_NOT_EMPTY", "Impossible de déséquiper la monture car son inventaire n'est pas vide.");
                case '+': return Mount.Text("MOUNT_ERROR_ALREADY_HAVE_ONE", "Impossible d'équiper la monture car vous en possédez déjà une.");
                case 'r': return Mount.Text("MOUNT_ERROR_RIDE", "Impossible de monter sur la monture.");
                default: return "Action sur la monture refusée par le serveur (ReE" + code + ").";
            }
        }

        /// <summary><c>Rx&lt;%&gt;</c> (<c>Mount.onXP</c>) : ignoré s'il n'est pas numérique, comme le client.</summary>
        internal void OnXp(string data)
        {
            if (!int.TryParse(data, NumberStyles.Integer, CultureInfo.InvariantCulture, out int percent)) { LogError("Paquet Rx illisible ignoré : " + data); return; }
            XpPercent = percent;
            Notify();
        }

        /// <summary><c>Rn&lt;nom&gt;</c> (<c>Mount.onName</c>).</summary>
        internal void OnName(string name)
        {
            lock (sync) current = current?.WithName(name);
            Log("La monture s'appelle désormais « " + (string.IsNullOrEmpty(name) ? Mount.Text("NO_NAME", "Sans nom") : name) + " ».");
            Notify();
        }

        /// <summary><c>Rr+</c> / <c>Rr-</c> (<c>Mount.onRidingState</c>) : le déplacement du personnage en tient compte.</summary>
        internal void OnRidingState(string data)
        {
            SetRiding(data.StartsWith("+", StringComparison.Ordinal));
            Log(IsRiding ? "Sur la monture." : "Descendu de la monture.");
            Notify();
        }

        /// <summary><c>Rd&lt;fiche&gt;</c> (<c>Mount.onData</c>) : fiche d'une monture d'enclos ou d'un certificat.</summary>
        internal void OnData(string data)
        {
            Mount mount = Mount.Parse(data);
            if (mount == null) { LogError("Fiche de monture illisible ignorée (Rd)."); return; }
            lock (sync) viewed = mount;
            ViewedSerial++;
            Log("Fiche reçue : " + mount.Describe() + ".");
            Notify();
        }

        /// <summary><c>Rp&lt;propriétaire;prix;taille;objets;guilde;emblème&gt;</c> (<c>Mount.onMountPark</c>).</summary>
        internal void OnMountPark(string data)
        {
            if (!MountParkInfo.TryParse(data, out MountParkInfo info)) { LogError("Paquet Rp illisible ignoré : " + data); return; }
            Account?.Game?.Interactions?.Interactive?.SetMountPark(info);
            Notify();
        }

        /// <summary><c>RD&lt;prix&gt;|&lt;prix par défaut&gt;</c> (<c>Mount.onMountParkBuy</c>) : ouvre la fenêtre <c>MountParkSale</c>.</summary>
        internal void OnParkSale(string data)
        {
            string[] fields = data.Split('|');
            int price = 0;
            if (fields.Length > 1 && !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out price))
            {
                LogError("Paquet RD illisible ignoré : " + data);
                return;
            }
            SaleDefaultPrice = Math.Max(0, price);
            MarkOpen();
            Log(IsParkMine ? Mount.Text("MOUNTPARK_SALE", "Mise en vente de l'enclos") : Mount.Text("MOUNTPARK_PURCHASE", "Achat d'un enclos"));
            Notify();
        }

        /// <summary><c>Rv</c> (<c>Mount.onLeave</c>) : ferme la fenêtre de l'enclos.</summary>
        internal void OnLeave()
        {
            bool wasOpen = IsOpen;
            SaleDefaultPrice = 0;
            MarkClosed();
            if (wasOpen) Log("Fenêtre de l'enclos fermée.");
            Notify();
        }

        /// <summary><c>Ew</c> des sacoches : charge reportée sur la fiche de la monture équipée.</summary>
        internal void OnPods(int pods, int podsMax)
        {
            lock (sync) current = current?.WithPods(pods, podsMax);
            Notify();
        }

        private void SetRiding(bool riding)
        {
            IsRiding = riding;
            var character = Account?.Game?.character;
            if (character != null) character.UseMount = riding;
        }

        private static long Hex(string[] parts, int index)
        {
            if (parts.Length <= index) return 0;
            string text = parts[index].Trim();
            return text.Length > 0 && long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long value) ? value : 0;
        }

        protected override void Reset()
        {
            lock (sync) { current = null; viewed = null; }
            IsRiding = false;
            XpPercent = null;
            SaleDefaultPrice = 0;
            if (Account != null) Account.CanUseMount = false;
            var character = Account?.Game?.character;
            if (character != null) character.UseMount = false;
        }
    }
}
