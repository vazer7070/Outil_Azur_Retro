using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Session;

namespace Tool_BotProtocol.Game.Alignement
{
    /// <summary>
    /// Alignement, ailes, conquête et prismes selon <c>dofus.aks.Conquest</c>, <c>Specialization</c>, <c>Subareas</c>, <c>Game.enabledPVPMode</c>
    /// et <c>GameActions.usePrism</c> du client 1.34, lus comme StarLoco les envoie (<c>GameClient.parseConquestPacket</c>, <c>Player.toggleWings</c>,
    /// <c>Player.openPrismeMenu</c>). Envois : <c>GP+</c>, <c>GP-</c>, <c>GP*</c>, <c>Cb</c>, <c>CB</c>, <c>CWJ</c>, <c>CWV</c>, <c>CIJ</c>, <c>CIV</c>,
    /// <c>CFJ</c>, <c>GA512&lt;id&gt;</c> ; <c>Wp&lt;carte&gt;</c> et <c>Ww</c> par <see cref="PrismTravel"/>, <c>GA912&lt;id&gt;</c> par
    /// <c>MapActions.AttackPrismAsync</c>. Réceptions (via <c>AlignmentFrame</c>) : <c>ZS</c>, <c>ZC</c>, <c>al</c>, <c>am</c>, <c>aM</c>,
    /// <c>GIP</c>, <c>Cb</c>, <c>CB</c>, <c>CW</c>, <c>Cp</c>, <c>CP</c>, <c>CIJ</c>, <c>CIV</c>, <c>CA</c>, <c>CD</c>, <c>CS</c>, <c>Wp</c>, <c>Ww</c>.
    /// <c>CFS</c> et <c>CFV</c> ne sont jamais émis : StarLoco les ignore. La question <c>GIP</c> (honneur perdu) n'est jamais répondue seule :
    /// l'interface demande au joueur puis appelle <see cref="DisableWingsAsync"/>. Les événements sont levés sur le fil de réception réseau.
    /// </summary>
    public sealed class AlignmentActions
    {
        private const string Reference = "ALIGNEMENT";
        private readonly Accounts.Accounts account;
        private readonly object sync = new object();
        private readonly Dictionary<int, int> zoneSides = new Dictionary<int, int>();
        private readonly Dictionary<int, int> areaSides = new Dictionary<int, int>();
        private double? worldBalance, areaBalance;
        private ConquestBonus bonus;
        private ConquestWorld world = new ConquestWorld();
        private int? pendingWingsLoss;
        private string lastMessage = string.Empty;
        private volatile bool worldInfosJoined, prismInfosJoined;

        internal AlignmentActions(Accounts.Accounts owner)
        {
            account = owner;
            Alignment = new Alignment(() => account?.Game?.character?.stats);
            Prism = new PrismTravel(owner);
            Defense = new PrismDefense();
        }

        /// <summary>Alignement du personnage, lu dans <c>As</c>.</summary>
        public Alignment Alignment { get; }
        /// <summary>Liste des prismes de transport (<c>Wp</c> / <c>Ww</c>).</summary>
        public PrismTravel Prism { get; }
        /// <summary>Onglet « Défendre » : <c>CIJ</c>, <c>CP</c>, <c>Cp</c>.</summary>
        public PrismDefense Defense { get; }
        /// <summary>Camp de chaque sous-zone conquérable (<c>al</c>, puis <c>am</c>) : -1 ou 0 sans propriétaire.</summary>
        public IReadOnlyDictionary<int, int> ZoneSides { get { lock (sync) return new Dictionary<int, int>(zoneSides); } }
        /// <summary>Camp des zones annoncées par <c>aM</c>.</summary>
        public IReadOnlyDictionary<int, int> AreaSides { get { lock (sync) return new Dictionary<int, int>(areaSides); } }
        /// <summary>Camp d'une sous-zone : 0 inconnue ou neutre.</summary>
        public int ZoneSide(int subAreaId) { lock (sync) return zoneSides.TryGetValue(subAreaId, out int side) && side > 0 ? side : 0; }
        /// <summary>Balance mondiale et de la zone courante (<c>Cb</c>) ; <c>null</c> avant la réponse.</summary>
        public double? WorldBalance { get { lock (sync) return worldBalance; } }
        public double? AreaBalance { get { lock (sync) return areaBalance; } }
        /// <summary>Bonus de conquête (<c>CB</c>) ; <c>null</c> avant la réponse.</summary>
        public ConquestBonus Bonus { get { lock (sync) return bonus; } }
        /// <summary>Dernières données de conquête du monde (<c>CW</c>, le dernier paquet reçu l'emporte comme dans le client).</summary>
        public ConquestWorld World { get { lock (sync) return world; } }
        /// <summary>Honneur que le serveur annonce perdre si les ailes sont retirées (<c>GIP</c>), tant que la question est posée.</summary>
        public int? PendingWingsLoss { get { lock (sync) return pendingWingsLoss; } }
        /// <summary>Onglet « Régions » abonné (<c>CWJ</c> envoyé, <c>CWV</c> non encore envoyé).</summary>
        public bool WorldInfosJoined => worldInfosJoined;
        /// <summary>Onglet « Défendre » abonné (<c>CIJ</c> envoyé, <c>CIV</c> non encore envoyé).</summary>
        public bool PrismInfosJoined => prismInfosJoined;
        /// <summary>Dernier message (confirmation, annonce du serveur traduite, refus local).</summary>
        public string LastMessage { get { lock (sync) return lastMessage; } }

        /// <summary>État changé (fil réseau ou appelant).</summary>
        public event Action Changed;
        /// <summary>Le serveur demande confirmation du retrait des ailes (<c>GIP&lt;honneur perdu&gt;</c>, boîte <c>ASK_DISABLE_PVP</c>) : jamais répondu seul.</summary>
        public event Action<int> WingsDisableAsked;
        public event Action BalanceChanged;
        public event Action BonusChanged;
        /// <summary><c>CW</c> reçu.</summary>
        public event Action WorldDataChanged;
        /// <summary><c>al</c>, <c>am</c> ou <c>aM</c> reçu.</summary>
        public event Action ZonesChanged;
        /// <summary><c>CIJ</c>, <c>CP</c> ou <c>Cp</c> reçu.</summary>
        public event Action DefenseChanged;
        /// <summary><c>CIV</c> reçu : le client ferme la fenêtre de conquête sans renvoyer <c>CIV</c>.</summary>
        public event Action DefenseClosed;
        /// <summary>Spécialisation changée (<c>ZC</c>) : texte de la boîte du client.</summary>
        public event Action<string> SpecializationChanged;
        /// <summary>Texte annoncé dans le canal d'alignement (<c>PVP_CHAT</c>) : prisme attaqué, zone conquise…</summary>
        public event Action<string> Alert;

        // ----- Envois -----

        /// <summary>« Activer » le mode joueur contre joueur après la boîte <c>ASK_ENABLED_PVP</c> : <c>GP+</c> (<c>Game.enabledPVPMode(true)</c>).</summary>
        public Task<InteractionResult> EnableWingsAsync()
        {
            if (!Alignment.IsAligned) return Refused("Un personnage neutre n'a pas d'ailes : StarLoco ignore GP+.");
            if (Alignment.WingsEnabled) return Refused("Le mode joueur contre joueur est déjà actif.");
            return SendAsync("GP+", "Activation du mode joueur contre joueur demandée.");
        }

        /// <summary>« Désactiver » : <c>GP*</c> (<c>Game.askDisablePVPMode</c>) ; le serveur répond <c>GIP&lt;honneur perdu&gt;</c>.</summary>
        public Task<InteractionResult> AskDisableWingsAsync()
        {
            if (!Alignment.IsAligned) return Refused("Un personnage neutre n'a pas d'ailes : StarLoco ignore GP*.");
            if (!Alignment.WingsEnabled) return Refused("Le mode joueur contre joueur est déjà inactif.");
            return SendAsync("GP*", "Honneur perdu demandé au serveur avant de retirer les ailes.");
        }

        /// <summary>« Oui » de la boîte <c>ASK_DISABLE_PVP</c> : <c>GP-</c> (<c>Game.enabledPVPMode(false)</c>) ; 5 % d'honneur perdus chez StarLoco.</summary>
        public async Task<InteractionResult> DisableWingsAsync()
        {
            if (!Alignment.IsAligned) return Refuse("Un personnage neutre n'a pas d'ailes : StarLoco ignore GP-.");
            if (!Alignment.WingsEnabled) return Refuse("Le mode joueur contre joueur est déjà inactif.");
            InteractionResult result = await SendAsync("GP-", "Retrait des ailes demandé.").ConfigureAwait(false);
            if (result.Sent) { lock (sync) pendingWingsLoss = null; RaiseChanged(); }
            return result;
        }

        /// <summary>« Non » de la boîte <c>ASK_DISABLE_PVP</c> : rien n'est envoyé, la question est oubliée.</summary>
        public void CancelWingsDisable()
        {
            lock (sync) pendingWingsLoss = null;
            SetMessage("Les ailes sont conservées.");
            RaiseChanged();
        }

        /// <summary>Ouverture de la fenêtre de conquête : <c>Cb</c> (<c>Conquest.requestBalance</c>).</summary>
        public Task<InteractionResult> RequestBalanceAsync() => SendAsync("Cb", "Balance de conquête demandée.");

        /// <summary>Onglet « Statistiques » : <c>CB</c> (<c>Conquest.getAlignedBonus</c>).</summary>
        public Task<InteractionResult> RequestBonusAsync() => SendAsync("CB", "Bonus de conquête demandés.");

        /// <summary>Onglet « Régions » ouvert : <c>CWJ</c> (<c>Conquest.worldInfosJoin</c>) ; StarLoco répond deux <c>CW</c>.</summary>
        public async Task<InteractionResult> JoinWorldInfosAsync()
        {
            InteractionResult result = await SendAsync("CWJ", "Données de conquête du monde demandées.").ConfigureAwait(false);
            if (result.Sent) worldInfosJoined = true;
            return result;
        }

        /// <summary>Onglet « Régions » fermé : <c>CWV</c> (<c>Conquest.worldInfosLeave</c>) ; StarLoco répond encore deux <c>CW</c>.</summary>
        public async Task<InteractionResult> LeaveWorldInfosAsync()
        {
            InteractionResult result = await SendAsync("CWV", "Fin de l'abonnement aux données de conquête.").ConfigureAwait(false);
            if (result.Sent) worldInfosJoined = false;
            return result;
        }

        /// <summary>Onglet « Défendre » ouvert : listes vidées puis <c>CIJ</c> (<c>Conquest.prismInfosJoin</c>) ; StarLoco envoie <c>Cp</c> / <c>CP</c> puis <c>CIJ</c>.</summary>
        public async Task<InteractionResult> JoinPrismInfosAsync()
        {
            Defense.Reset();
            InteractionResult result = await SendAsync("CIJ", "Informations du prisme de la sous-zone demandées.").ConfigureAwait(false);
            if (result.Sent) { prismInfosJoined = true; RaiseDefenseChanged(); }
            return result;
        }

        /// <summary>
        /// Onglet « Défendre » fermé : <c>CIV</c> (<c>Conquest.prismInfosLeave</c>), sauf quand <c>CIJ</c> a répondu une erreur, cas où le client
        /// ne se désabonne pas (<c>noUnsubscribe</c>). StarLoco accepte <c>CIV</c> sans effet.
        /// </summary>
        public async Task<InteractionResult> LeavePrismInfosAsync()
        {
            bool subscribed = prismInfosJoined;
            int? error = Defense.Error;
            prismInfosJoined = false;
            Defense.Reset();
            RaiseDefenseChanged();
            if (!subscribed) return Refuse("L'onglet de défense n'était pas abonné.");
            if (error.HasValue && error.Value != 0)
            {
                SetMessage("Fermeture sans désabonnement : le serveur avait répondu une erreur.");
                RaiseChanged();
                return new InteractionResult(false, LastMessage);
            }
            return await SendAsync("CIV", "Fin de l'abonnement aux informations du prisme.").ConfigureAwait(false);
        }

        /// <summary>Clic sur une place libre de l'équipe : <c>CFJ</c> (<c>Conquest.prismFightJoin</c>) ; StarLoco place le personnage dans le combat du prisme.</summary>
        public Task<InteractionResult> JoinPrismFightAsync()
        {
            if (!Alignment.IsAligned) return Refused("Un personnage neutre ne défend pas de prisme.");
            if (!Defense.IsJoinable) return Refused(Defense.Error.HasValue ? JoinErrorText(Defense.Error.Value) : "Ouvrez d'abord l'onglet de défense (CIJ).");
            if (account?.Game?.Fight?.IsInFight == true) return Refused("Impossible en combat.");
            return SendAsync("CFJ", "Participation à la défense du prisme demandée.");
        }

        /// <summary>« Utiliser » un prisme de la carte : <c>GA512&lt;id&gt;</c> (<c>GameActions.usePrism</c>), permis par le client pour un prisme de son camp seulement.</summary>
        public Task<InteractionResult> UsePrismAsync(long prismId)
        {
            var prism = account?.Game?.Map?.GetActor(prismId) as PrismActor;
            if (prism == null) return Refused("Aucun prisme avec cet identifiant sur la carte.");
            if (!Alignment.IsAligned) return Refused("Un personnage neutre ne peut pas utiliser un prisme : le client grise « Utiliser » et StarLoco ignore la demande.");
            if (prism.AlignmentSide != Alignment.Side) return Refused("Ce prisme appartient à un autre camp : « Utiliser » est grisé.");
            if (account?.Game?.Fight?.IsInFight == true) return Refused("Impossible en combat.");
            return Prism.RequestAsync(prismId);
        }

        /// <summary>Règle du menu du client : « Utiliser » actif quand le prisme est du camp du personnage.</summary>
        public bool CanUsePrism(PrismActor prism) => prism != null && Alignment.IsAligned && prism.AlignmentSide == Alignment.Side;

        // ----- Réceptions -----

        /// <summary><c>ZS&lt;id&gt;</c> (<c>Specialization.onSet</c>) ou <c>ZC&lt;id&gt;</c> (<c>onChange</c>) ; StarLoco y met l'identifiant du camp.</summary>
        internal void OnSpecialization(string payload, bool changed)
        {
            string text = (payload ?? string.Empty).Trim();
            int id = 0;
            if (text.Length > 0 && !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) { Malformed("Z", payload); return; }
            Alignment.SpecializationId = id;
            if (changed)
            {
                string message = id == 0
                    ? AlignmentTexts.Get("YOU_HAVE_NO_SPECIALIZATION", "Tu n'as plus de spécialisation.")
                    : AlignmentTexts.Get("YOUR_SPECIALIZATION_CHANGED", "Ta spécialisation est maintenant : {0}.", AlignmentTexts.SpecializationName(id));
                Inform(message);
                Raise(SpecializationChanged, handler => handler(message));
            }
            else Debug("Spécialisation : " + id.ToString(CultureInfo.InvariantCulture));
            RaiseChanged();
        }

        /// <summary><c>al|&lt;sous-zone&gt;;&lt;camp&gt;|…</c> (<c>Subareas.onList</c>) : sous-zones conquérables et leur camp.</summary>
        internal void OnZoneList(string payload)
        {
            string body = (payload ?? string.Empty).TrimStart('|');
            var sides = new Dictionary<int, int>();
            foreach (string entry in body.Split('|'))
            {
                if (entry.Length == 0) continue;
                string[] fields = entry.Split(';');
                if (fields.Length < 2 || !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                    || !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int side)) { Malformed("al", payload); return; }
                sides[id] = side;
            }
            lock (sync) { zoneSides.Clear(); foreach (var pair in sides) zoneSides[pair.Key] = pair.Value; }
            Debug(sides.Count + " sous-zone(s) conquérable(s).");
            Raise(ZonesChanged, handler => handler());
            RaiseChanged();
        }

        /// <summary><c>am&lt;sous-zone&gt;|&lt;camp&gt;|&lt;1 silencieux|0 annoncé&gt;</c> (<c>Subareas.onSet</c>) : -1 prisme retiré, sinon <c>SUBAREA_ALIGNMENT_IS</c> dans le canal d'alignement.</summary>
        internal void OnZoneChanged(string payload)
        {
            string[] fields = (payload ?? string.Empty).Split('|');
            if (fields.Length < 2 || !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                || !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int side)) { Malformed("am", payload); return; }
            bool silent = fields.Length > 2 && fields[2].Trim() == "1";
            lock (sync) zoneSides[id] = side;
            if (!silent)
            {
                string name = AlignmentTexts.ZoneName(id);
                if (side == -1) AnnouncePvp("SUBAREA_ALIGNMENT_PRISM_REMOVED", "Le prisme de la sous-zone {0} a été retiré.", name);
                else AnnouncePvp("SUBAREA_ALIGNMENT_IS", "La sous-zone {0} appartient maintenant à l'alignement {1}.", name, AlignmentTexts.Name(side));
            }
            Raise(ZonesChanged, handler => handler());
            RaiseChanged();
        }

        /// <summary><c>aM&lt;zone&gt;|&lt;camp&gt;</c> (<c>Conquest.onAreaAlignmentChanged</c>) : toujours annoncé.</summary>
        internal void OnAreaChanged(string payload)
        {
            string[] fields = (payload ?? string.Empty).Split('|');
            if (fields.Length < 2 || !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                || !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int side)) { Malformed("aM", payload); return; }
            lock (sync) areaSides[id] = side;
            string name = AlignmentTexts.AreaName(id);
            if (side == -1) AnnouncePvp("AREA_ALIGNMENT_PRISM_REMOVED", "Le prisme de la zone {0} a été retiré.", name);
            else AnnouncePvp("AREA_ALIGNMENT_IS", "La zone {0} appartient maintenant à l'alignement {1}.", name, AlignmentTexts.Name(side));
            Raise(ZonesChanged, handler => handler());
            RaiseChanged();
        }

        /// <summary><c>GIP&lt;honneur perdu&gt;</c> (<c>Game.onPVP</c>) : question <c>ASK_DISABLE_PVP</c> ; « Oui » appelle <see cref="DisableWingsAsync"/>.</summary>
        internal void OnWingsQuestion(string payload)
        {
            if (!int.TryParse((payload ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int loss)) { Malformed("GIP", payload); return; }
            lock (sync) pendingWingsLoss = loss;
            string text = AlignmentTexts.Get("ASK_DISABLE_PVP", "Si tu désactives le mode joueur contre joueur, tu perdras {0} points d'honneur. Continuer ?", loss.ToString(CultureInfo.InvariantCulture));
            SetMessage(text);
            account?.Logger?.LogInfo(Reference, text);
            Raise(WingsDisableAsked, handler => handler(loss));
            RaiseChanged();
        }

        /// <summary><c>Cb&lt;balance monde&gt;;&lt;balance zone&gt;</c> (<c>Conquest.onConquestBalance</c>).</summary>
        internal void OnBalance(string payload)
        {
            string[] fields = (payload ?? string.Empty).Split(';');
            if (fields.Length < 2 || !double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double worldValue)
                || !double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double areaValue)) { Malformed("Cb", payload); return; }
            lock (sync) { worldBalance = worldValue; areaBalance = areaValue; }
            Debug("Balance : monde " + worldValue.ToString(CultureInfo.InvariantCulture) + ", zone " + areaValue.ToString(CultureInfo.InvariantCulture));
            Raise(BalanceChanged, handler => handler());
            RaiseChanged();
        }

        /// <summary><c>CB&lt;xp,butin,récolte&gt;;&lt;xp,butin,récolte&gt;;&lt;xp,butin,récolte&gt;</c> : bonus d'alignement, multiplicateur de grade, malus (<c>Conquest.onConquestBonus</c>).</summary>
        internal void OnBonus(string payload)
        {
            string[] groups = (payload ?? string.Empty).Split(';');
            if (groups.Length < 3) { Malformed("CB", payload); return; }
            var parsed = new ConquestModifier[3];
            for (int index = 0; index < 3; index++)
            {
                string[] values = groups[index].Split(',');
                if (values.Length < 3 || !double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double xp)
                    || !double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double drop)
                    || !double.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double collect)) { Malformed("CB", payload); return; }
                parsed[index] = new ConquestModifier { Xp = xp, Drop = drop, Collect = collect };
            }
            lock (sync) bonus = new ConquestBonus { AlignBonus = parsed[0], RankMultiplier = parsed[1], AlignMalus = parsed[2] };
            Raise(BonusChanged, handler => handler());
            RaiseChanged();
        }

        /// <summary>
        /// <c>CW&lt;possédées&gt;|&lt;total&gt;|&lt;possibles&gt;|&lt;sous-zones&gt;|&lt;villages possédés&gt;|&lt;total&gt;|&lt;villages&gt;</c> (<c>Conquest.onWorldData</c>) :
        /// sous-zones <c>id,camp,combat,carte du prisme,1</c> et villages <c>id,camp,1,prisme</c> séparés par « ; ». Pour un personnage neutre StarLoco
        /// n'envoie que <c>&lt;sous-zones&gt;|&lt;nombre&gt;|&lt;villages&gt;</c>. Le dernier paquet reçu remplace le précédent.
        /// </summary>
        internal void OnWorldData(string payload)
        {
            string[] parts = (payload ?? string.Empty).Split('|');
            var data = new ConquestWorld();
            string zones, villages;
            if (parts.Length >= 7)
            {
                data.OwnedAreas = Int(parts[0]); data.TotalAreas = Int(parts[1]); data.PossibleAreas = Int(parts[2]);
                zones = parts[3];
                data.OwnedVillages = Int(parts[4]); data.TotalVillages = Int(parts[5]);
                villages = parts[6];
            }
            else if (parts.Length == 3)
            {
                zones = parts[0]; data.TotalAreas = Int(parts[1]); villages = parts[2]; data.CountsUnknown = true;
            }
            else { Malformed("CW", payload); return; }
            var zoneList = new List<ConquestZone>();
            foreach (string row in zones.Split(';'))
            {
                string[] fields = row.Split(',');
                if (fields.Length < 5) continue;
                zoneList.Add(new ConquestZone { Id = Int(fields[0]), Side = Math.Max(0, Int(fields[1])), Fighting = fields[2] == "1", PrismMapId = Math.Max(0, Int(fields[3])) });
            }
            var villageList = new List<ConquestVillage>();
            foreach (string row in villages.Split(';'))
            {
                string[] fields = row.Split(',');
                if (fields.Length != 4) continue;
                villageList.Add(new ConquestVillage { Id = Int(fields[0]), Side = Math.Max(0, Int(fields[1])), Fighting = fields[2] == "1", HasPrism = fields[3] == "1" });
            }
            data.Zones = zoneList.OrderBy(zone => zone.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            data.Villages = villageList.OrderBy(village => village.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            lock (sync) world = data;
            Debug("Conquête : " + zoneList.Count + " sous-zone(s), " + villageList.Count + " village(s).");
            Raise(WorldDataChanged, handler => handler());
            RaiseChanged();
        }

        /// <summary><c>CIJ&lt;0;délai;durée;places&gt;</c> ou <c>CIJ&lt;-1|-2|-3&gt;</c> (<c>Conquest.onPrismInfosJoined</c>).</summary>
        internal void OnPrismInfos(string payload)
        {
            string[] fields = (payload ?? string.Empty).Split(';');
            if (!int.TryParse(fields[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int error)) { Malformed("CIJ", payload); return; }
            if (error == 0)
            {
                if (fields.Length < 4) { Malformed("CIJ", payload); return; }
                Defense.Timer = Int(fields[1]); Defense.MaxTimer = Int(fields[2]); Defense.MaxTeamPositions = Int(fields[3]);
            }
            else { Defense.Timer = Defense.MaxTimer = Defense.MaxTeamPositions = 0; }
            Defense.Error = error;
            Defense.ReceivedAt = DateTime.UtcNow;
            if (error == 0) Debug("Combat de prisme rejoignable : " + Defense.Timer + " ms restantes, " + Defense.MaxTeamPositions + " place(s).");
            else { SetMessage(JoinErrorText(error)); account?.Logger?.LogInfo(Reference, LastMessage); }
            RaiseDefenseChanged();
        }

        /// <summary><c>CIV</c> (<c>Conquest.onPrismInfosClosing</c>) : la fenêtre se ferme sans renvoyer <c>CIV</c>.</summary>
        internal void OnPrismInfosClosing()
        {
            prismInfosJoined = false;
            Defense.Reset();
            Log("Le serveur ferme les informations du prisme.");
            Raise(DefenseClosed, handler => handler());
            RaiseDefenseChanged();
        }

        /// <summary>
        /// <c>CP+&lt;prisme36&gt;|&lt;id36&gt;;&lt;nom&gt;;&lt;gfx&gt;;&lt;niveau&gt;;&lt;c1_36&gt;;&lt;c2_36&gt;;&lt;c3_36&gt;;&lt;réserviste&gt;|…</c> (défenseurs,
        /// <c>Conquest.onPrismFightAddPlayer</c>) ; <c>Cp+&lt;prisme36&gt;|&lt;id36&gt;;&lt;nom&gt;;&lt;niveau&gt;|…</c> (attaquants, <c>onPrismFightAddEnemy</c>) ;
        /// <c>-</c> retire les identifiants listés.
        /// </summary>
        internal void OnPrismFighters(string payload, bool defenders)
        {
            string text = payload ?? string.Empty;
            if (text.Length == 0 || (text[0] != '+' && text[0] != '-')) { Malformed(defenders ? "CP" : "Cp", payload); return; }
            bool add = text[0] == '+';
            string[] parts = text.Substring(1).Split('|');
            for (int index = 1; index < parts.Length; index++)
            {
                string[] fields = parts[index].Split(';');
                if (fields.Length == 0 || !Guildes.GuildTexts.TryBase36(fields[0], out long id)) continue;
                if (!add) { Defense.Remove(id, defenders); continue; }
                var fighter = new PrismFighter { Id = id, IsDefender = defenders, Name = fields.Length > 1 ? fields[1] : string.Empty };
                if (defenders)
                {
                    fighter.Gfx = fields.Length > 2 ? Int(fields[2]) : 0;
                    fighter.Level = fields.Length > 3 ? Int(fields[3]) : 0;
                    fighter.Color1 = fields.Length > 4 && Guildes.GuildTexts.TryBase36(fields[4], out long c1) ? (int)c1 : -1;
                    fighter.Color2 = fields.Length > 5 && Guildes.GuildTexts.TryBase36(fields[5], out long c2) ? (int)c2 : -1;
                    fighter.Color3 = fields.Length > 6 && Guildes.GuildTexts.TryBase36(fields[6], out long c3) ? (int)c3 : -1;
                    fighter.Reservist = fields.Length > 7 && fields[7] == "1";
                }
                else fighter.Level = fields.Length > 2 ? Int(fields[2]) : 0;
                Defense.Upsert(fighter);
            }
            RaiseDefenseChanged();
        }

        /// <summary><c>CA</c> / <c>CS</c> / <c>CD</c> <c>&lt;carte&gt;|&lt;x&gt;|&lt;y&gt;</c> : prisme attaqué, survivant, mort (<c>Conquest.onPrismAttacked</c>…) dans le canal d'alignement.</summary>
        internal void OnPrismAlert(char kind, string payload)
        {
            string[] fields = (payload ?? string.Empty).Split('|');
            if (fields.Length < 3 || !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mapId)) { Malformed("C" + kind, payload); return; }
            string where = "[" + fields[1] + ", " + fields[2] + "]";
            int? subArea = null;
            try { if (LangData.IsLoaded("maps")) subArea = LangData.Map.SubAreaId(mapId); } catch (Exception) { /* repli */ }
            string name = subArea.HasValue ? AlignmentTexts.ZoneName(subArea.Value) : AlignmentTexts.MapLabel(mapId);
            switch (kind)
            {
                case 'A': AnnouncePvp("PRISM_ATTACKED", "Le prisme de {0} est attaqué en {1} !", name, where); break;
                case 'S': AnnouncePvp("PRISM_ATTACKED_SUVIVED", "Le prisme de {0} attaqué en {1} a survécu !", name, where); break;
                case 'D': AnnouncePvp("PRISM_ATTACKED_DIED", "Le prisme de {0} attaqué en {1} n'a pas survécu !", name, where); break;
                default: Debug("Paquet C" + kind + " inconnu ignoré : " + Preview(payload)); return;
            }
            RaiseChanged();
        }

        /// <summary>Texte du client pour une réponse <c>CIJ</c>.</summary>
        public static string JoinErrorText(int error)
        {
            switch (error)
            {
                case 0: return AlignmentTexts.Get("CONQUEST_JOIN_FIGHT", "Rejoindre un combat de prisme");
                case -1: return AlignmentTexts.Get("CONQUEST_JOIN_FIGHT_NOFIGHT", "Aucun combat de prisme n'est en cours dans cette sous-zone.");
                case -2: return AlignmentTexts.Get("CONQUEST_JOIN_FIGHT_INFIGHT", "Le combat de prisme a déjà commencé.");
                case -3: return AlignmentTexts.Get("CONQUEST_JOIN_FIGHT_NONE", "Aucun prisme dans cette sous-zone.");
                default: return "Réponse CIJ inconnue : " + error.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Déconnexion : tout est oublié.</summary>
        public void Clear()
        {
            lock (sync)
            {
                zoneSides.Clear(); areaSides.Clear();
                worldBalance = areaBalance = null; bonus = null; world = new ConquestWorld();
                pendingWingsLoss = null; lastMessage = string.Empty;
            }
            worldInfosJoined = prismInfosJoined = false;
            Alignment.SpecializationId = 0;
            Defense.Reset();
            Prism.Clear();
            RaiseChanged();
        }

        // ----- Outils -----

        private static int Int(string text) => int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;

        private async Task<InteractionResult> SendAsync(string packet, string message)
        {
            var connection = account?.Connexion;
            if (connection == null || !connection.IsConnected()) return Refuse("Connectez le personnage avant cette action.");
            try { await connection.SendPacket(packet).ConfigureAwait(false); }
            catch (Exception error)
            {
                account?.Logger?.LogException(Reference, error);
                return Refuse("Envoi impossible : " + error.Message);
            }
            SetMessage(message);
            account?.Logger?.LogInfo(Reference, message);
            RaiseChanged();
            return new InteractionResult(true, message);
        }

        private InteractionResult Refuse(string message) { SetMessage(message); RaiseChanged(); return new InteractionResult(false, message); }
        private Task<InteractionResult> Refused(string message) => Task.FromResult(Refuse(message));
        private void SetMessage(string message) { lock (sync) lastMessage = message ?? string.Empty; }
        private void Log(string text) { SetMessage(text); account?.Logger?.LogInfo(Reference, text); }
        private void Debug(string text) => account?.Logger?.LogDebug(Reference, text);
        private void Malformed(string prefix, string payload) => account?.Logger?.LogError(Reference, "Paquet " + prefix + " illisible ignoré : " + Preview(payload));
        private static string Preview(string text) => text == null ? string.Empty : text.Length > 80 ? text.Substring(0, 80) + "…" : text;

        /// <summary>Information du client (<c>INFO_CHAT</c>) : journal, ligne locale du chat et <see cref="LastMessage"/>.</summary>
        private void Inform(string text)
        {
            SetMessage(text);
            account?.Logger?.LogInfo(Reference, text);
            try { account?.Game?.Chat?.AddLocal(ChatMessageKind.Info, text); }
            catch (Exception error) { account?.Logger?.LogException(Reference, error); }
        }

        /// <summary>Annonce du canal d'alignement (<c>PVP_CHAT</c>) : message serveur de type <c>Pvp</c> pour le chat, journal et <see cref="Alert"/>.</summary>
        private void AnnouncePvp(string key, string fallback, params string[] args)
        {
            string text = AlignmentTexts.Get(key, fallback, args);
            bool resolved = false;
            try { resolved = LangData.Text.Has(key); } catch (Exception) { /* textes absents */ }
            SetMessage(text);
            account?.Logger?.LogInfo(Reference, text);
            try { account?.Game?.Session?.OnServerMessages(new[] { new ServerMessage(ServerMessageKind.Pvp, key, args, text, resolved) }); }
            catch (Exception error) { account?.Logger?.LogException(Reference, error); }
            Raise(Alert, handler => handler(text));
        }

        private void RaiseChanged() => Raise(Changed, handler => handler());
        private void RaiseDefenseChanged() { Raise(DefenseChanged, handler => handler()); RaiseChanged(); }

        private void Raise<T>(T subscribers, Action<T> invoke) where T : class
        {
            var multicast = subscribers as Delegate;
            if (multicast == null) return;
            foreach (Delegate subscriber in multicast.GetInvocationList())
            {
                try { invoke((T)(object)subscriber); }
                catch (Exception error) { account?.Logger?.LogException(Reference, error); }
            }
        }
    }
}
