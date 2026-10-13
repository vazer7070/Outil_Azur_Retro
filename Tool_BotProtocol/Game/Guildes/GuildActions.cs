using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Session;

namespace Tool_BotProtocol.Game.Guildes
{
    /// <summary>
    /// Guilde du personnage selon <c>dofus.aks.Guild</c> du client 1.34 et <c>GameClient.parseGuildPacket</c> de StarLoco.
    /// Envois : <c>gJR&lt;nom&gt;</c>, <c>gJK&lt;id&gt;</c>, <c>gJE&lt;id&gt;</c>, <c>gK&lt;nom&gt;</c>, <c>gP&lt;id&gt;|&lt;rang&gt;|&lt;xp&gt;|&lt;droits&gt;</c>,
    /// <c>gB&lt;p|x|o|k&gt;</c>, <c>gb&lt;sort&gt;</c>, <c>gH</c>, <c>gF&lt;id&gt;</c>, <c>gIG</c>, <c>gIM</c>, <c>gIB</c>, <c>gIT</c>, <c>gIF</c>, <c>gIH</c>,
    /// <c>gITV</c>, <c>gC&lt;fond&gt;|&lt;couleur&gt;|&lt;motif&gt;|&lt;couleur&gt;|&lt;nom&gt;</c>, <c>gV</c> et <c>ER8|&lt;id&gt;</c> (collecte, fenêtre
    /// <see cref="CollectorExchange"/>). Réceptions (via <c>GuildFrame</c>) : <c>gS</c>, <c>gI…</c>, <c>gJ…</c>, <c>gK…</c>, <c>gC…</c>,
    /// <c>gn</c>, <c>gV</c>, <c>gT…</c>, <c>gA…</c>, <c>gU…</c>, <c>gH…</c>. <c>gTJ</c> / <c>gTV</c> (rejoindre ou quitter la défense
    /// d'un percepteur) ne sont jamais émis : StarLoco les lit en base 36 et ne retrouve jamais le percepteur (matrice §2 n° 37).
    /// L'état ne change qu'à la réception des paquets du serveur ; une invitation reçue n'est jamais acceptée seule. Les événements
    /// sont levés sur le fil de réception réseau : l'interface repasse sur son fil.
    /// </summary>
    public sealed class GuildActions
    {
        private const string Reference = "GUILDE";
        /// <summary>Guildalogemme : l'objet que StarLoco exige sur la carte de création (<c>Im14</c> sans lui).</summary>
        public const int GuildalogemmeItemId = 1575;
        /// <summary>Carte du temple des guildes où StarLoco accepte <c>gC</c>.</summary>
        public const int CreationMapId = 2196;
        /// <summary>Pourcentage d'expérience donné à la guilde : 0 à 90 (octet lu par StarLoco, curseur du client).</summary>
        public const int MaxXpPercent = 90;
        private readonly Accounts.Accounts account;
        private readonly object sync = new object();
        private readonly HashSet<string> ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string pendingInviterId, pendingInviterName, pendingGuildName, outgoingInvitee, lastMessage = string.Empty;
        private volatile bool windowOpen, creationWindowOpen;

        internal GuildActions(Accounts.Accounts owner) { account = owner; Guild = new Guild(); }

        /// <summary>Guilde annoncée par le serveur (<c>gS</c> puis les onglets <c>gI…</c>).</summary>
        public Guild Guild { get; }
        public bool HasGuild => Guild.HasGuild;
        /// <summary>Identifiant de l'invitant d'une invitation reçue en attente (<c>gJr&lt;id&gt;|…</c>), celui que <c>gJK</c> / <c>gJE</c> renvoient ; <c>null</c> sinon.</summary>
        public string PendingInviterId { get { lock (sync) return pendingInviterId; } }
        public string PendingInviterName { get { lock (sync) return pendingInviterName; } }
        public string PendingGuildName { get { lock (sync) return pendingGuildName; } }
        /// <summary>Joueur invité par le personnage, en attente de sa réponse (<c>gJR&lt;nom&gt;</c> confirmé), ou <c>null</c>.</summary>
        public string OutgoingInvitee { get { lock (sync) return outgoingInvitee; } }
        /// <summary>Invitants ignorés jusqu'à la fin de la session (« Ignorer » du client) : leurs invitations sont refusées sans question.</summary>
        public IReadOnlyCollection<string> IgnoredInviters { get { lock (sync) return ignored.ToArray(); } }
        public bool IsIgnored(string name) { if (string.IsNullOrEmpty(name)) return false; lock (sync) return ignored.Contains(name); }
        /// <summary>Dernier message (confirmation, erreur du serveur traduite, refus local).</summary>
        public string LastMessage { get { lock (sync) return lastMessage; } }
        /// <summary>Fenêtre de guilde ouverte dans l'interface.</summary>
        public bool WindowOpen { get => windowOpen; set => windowOpen = value; }
        /// <summary>Panneau de création du client (<c>CreateGuild</c>) : ouvert par <c>gn</c>, fermé par <c>gV</c> ou <c>gCK</c>.</summary>
        public bool CreationWindowOpen => creationWindowOpen;

        /// <summary>Guilde, invitations ou message changés (fil réseau ou appelant).</summary>
        public event Action Changed;
        /// <summary>Invitation (reçue ou envoyée) close par le serveur : <c>gJC</c>, <c>gJEc</c>, <c>gJEr</c>, <c>gJKa</c>, <c>gJKj</c>.</summary>
        public event Action InvitationClosed;
        /// <summary>Erreur que le client montre dans une boîte (<c>ERROR_BOX</c> : création refusée) : titre, texte.</summary>
        public event Action<string, string> ErrorBox;
        /// <summary>Le serveur demande l'ouverture de la fenêtre sur un onglet (<c>gUT</c> maisons, <c>gUF</c> enclos, <c>gCK</c> membres).</summary>
        public event Action<GuildTab> OpenTabRequested;
        /// <summary>Percepteur attaqué, survivant ou mort (<c>gA…</c>) : texte du client.</summary>
        public event Action<string> CollectorAlert;

        // ----- Envois (formats de dofus.aks.Guild) -----

        /// <summary>« Inviter dans la guilde » du menu d'un joueur : <c>gJR&lt;nom&gt;</c> (réponses <c>gJR&lt;nom&gt;</c> ou <c>gJE&lt;u|o|a|d&gt;</c>).</summary>
        public Task<InteractionResult> InviteAsync(string name)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0) return Refused("Indiquez le nom du personnage à inviter.");
            if (!IsSendable(name)) return Refused("Nom de personnage invalide : « " + name + " ».");
            if (!HasGuild) return Refused(GuildTexts.Get("UI_ONLY_FOR_GUILD", "Pour accéder à cette interface, il faut que tu fasses partie d'une guilde."));
            string self = account?.Game?.character?.Name;
            if (!string.IsNullOrEmpty(self) && string.Equals(self, name, StringComparison.OrdinalIgnoreCase)) return Refused("Le personnage ne peut pas s'inviter lui-même.");
            if (!Guild.CanDo(GuildRight.Invite)) return Refused(GuildTexts.Get("GUILD_JOIN_NO_RIGHTS", "Tu n'as pas le droit d'inviter des joueurs dans ta guilde."));
            return SendAsync("gJR" + name, "Invitation dans la guilde envoyée à " + name + ".");
        }

        /// <summary>« Oui » de la boîte d'invitation : <c>gJK&lt;id invitant&gt;</c> (le serveur répond <c>gS</c> puis <c>gJKj</c>).</summary>
        public async Task<InteractionResult> AcceptAsync()
        {
            string id = PendingInviterId, inviter = PendingInviterName;
            if (id == null) return Refuse("Aucune invitation de guilde en attente.");
            InteractionResult result = await SendAsync("gJK" + id, "Invitation de " + inviter + " acceptée.").ConfigureAwait(false);
            if (result.Sent) ClearPending(id);
            return result;
        }

        /// <summary>« Non » de la boîte : <c>gJE&lt;id invitant&gt;</c>.</summary>
        public async Task<InteractionResult> RefuseAsync()
        {
            string id = PendingInviterId, inviter = PendingInviterName;
            if (id == null) return Refuse("Aucune invitation de guilde en attente.");
            InteractionResult result = await SendAsync("gJE" + id, "Invitation de " + inviter + " refusée.").ConfigureAwait(false);
            if (result.Sent) ClearPending(id);
            return result;
        }

        /// <summary>« Ignorer » de la boîte : <c>gJE&lt;id&gt;</c> et l'invitant est ignoré jusqu'à la fin de la session (liste noire du chat du client).</summary>
        public async Task<InteractionResult> IgnoreAsync()
        {
            string id = PendingInviterId, inviter = PendingInviterName;
            if (id == null) return Refuse("Aucune invitation de guilde en attente.");
            InteractionResult result = await SendAsync("gJE" + id, GuildTexts.Get("TEMPORARY_BLACKLISTED", "{0} est maintenant ignoré jusqu'à la fin de la session.", inviter)).ConfigureAwait(false);
            if (result.Sent)
            {
                lock (sync) if (!string.IsNullOrEmpty(inviter)) ignored.Add(inviter);
                ClearPending(id);
            }
            return result;
        }

        /// <summary>Bouton × d'un membre (<c>Guild.bann</c>) : <c>gK&lt;nom&gt;</c> ; son propre nom vaut un départ (<see cref="LeaveAsync"/>).</summary>
        public Task<InteractionResult> KickAsync(string name)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0) return Refused("Indiquez le nom du membre à exclure.");
            if (!IsSendable(name)) return Refused("Nom de personnage invalide : « " + name + " ».");
            if (!HasGuild) return Refused(GuildTexts.Get("UI_ONLY_FOR_GUILD", "Pour accéder à cette interface, il faut que tu fasses partie d'une guilde."));
            string self = account?.Game?.character?.Name;
            if (!string.IsNullOrEmpty(self) && string.Equals(self, name, StringComparison.OrdinalIgnoreCase)) return LeaveAsync();
            if (!Guild.CanDo(GuildRight.Ban)) return Refused(GuildTexts.Get("NOT_ENOUGHT_RIGHTS_FROM_GUILD", "Tu n'as pas les droits suffisant dans ta guilde pour réaliser cette action."));
            GuildMember member = Guild.FindMember(name);
            if (member != null && member.IsBoss) return Refused("Le meneur ne peut pas être exclu de la guilde.");
            return SendAsync("gK" + name, "Exclusion de " + name + " de la guilde demandée.");
        }

        /// <summary>Quitter la guilde (× sur sa propre ligne, <c>DO_U_DELETE_YOU</c>) : <c>gK&lt;soi&gt;</c>, réponse <c>gKK&lt;soi&gt;|&lt;soi&gt;</c>.</summary>
        public Task<InteractionResult> LeaveAsync()
        {
            if (!HasGuild) return Refused(GuildTexts.Get("UI_ONLY_FOR_GUILD", "Pour accéder à cette interface, il faut que tu fasses partie d'une guilde."));
            string self = account?.Game?.character?.Name;
            if (string.IsNullOrEmpty(self)) return Refused("Le nom du personnage n'est pas connu.");
            // StarLoco refuse le départ du meneur tant que la guilde compte d'autres membres (message du serveur) : même règle ici.
            if (Guild.IsBoss && Guild.Members.Count > 1)
                return Refused(GuildTexts.Get("GUILD_BOSS_CANT_BE_BANN", "Le 'Meneur' ne peut pas quitter la guilde sans donner ses pouvoirs à un autre membre."));
            return SendAsync("gK" + self, "Départ de la guilde demandé.");
        }

        /// <summary>
        /// Fiche d'un membre (<c>Guild.changeMemberProfil</c>) : <c>gP&lt;id&gt;|&lt;rang&gt;|&lt;xp %&gt;|&lt;droits&gt;</c>. StarLoco ne garde que ce que
        /// les droits de l'envoyeur permettent (rangs, droits, xp des autres ou la sienne) et répond par <c>gS</c> aux deux membres.
        /// </summary>
        public Task<InteractionResult> SetProfileAsync(long memberId, int rank, int xpPercent, int rights)
        {
            if (!HasGuild) return Refused(GuildTexts.Get("UI_ONLY_FOR_GUILD", "Pour accéder à cette interface, il faut que tu fasses partie d'une guilde."));
            GuildMember member = Guild.FindMember(memberId);
            if (member == null) return Refused("Ce membre n'est pas dans la liste reçue de la guilde (onglet Membres à recharger).");
            if (rank < 0) return Refused("Rang invalide.");
            if (xpPercent < 0 || xpPercent > MaxXpPercent) return Refused("Pourcentage d'expérience invalide (0 à " + MaxXpPercent.ToString(CultureInfo.InvariantCulture) + ").");
            if (rights < 0) return Refused("Droits invalides.");
            long self = account?.Game?.character?.id ?? 0;
            if (member.Id != self && !Guild.CanDo(GuildRight.ManageRanks) && !Guild.CanDo(GuildRight.ManageRights) && !Guild.CanDo(GuildRight.ManageAllXp))
                return Refused(GuildTexts.Get("NOT_ENOUGHT_RIGHTS_FROM_GUILD", "Tu n'as pas les droits suffisant dans ta guilde pour réaliser cette action."));
            string packet = "gP" + memberId.ToString(CultureInfo.InvariantCulture) + "|" + rank.ToString(CultureInfo.InvariantCulture) + "|"
                + xpPercent.ToString(CultureInfo.InvariantCulture) + "|" + rights.ToString(CultureInfo.InvariantCulture);
            return SendAsync(packet, "Profil de " + member.Name + " envoyé : " + GuildTexts.RankName(rank) + ", " + xpPercent.ToString(CultureInfo.InvariantCulture) + " % d'xp, droits " + rights.ToString(CultureInfo.InvariantCulture) + ".");
        }

        /// <summary>
        /// Bouton + d'une caractéristique des percepteurs (<c>Guild.boostCharacteristic</c>) : <c>gBp</c> prospection, <c>gBx</c> sagesse,
        /// <c>gBo</c> pods, <c>gBk</c> nombre de percepteurs. StarLoco vérifie le droit, le capital et le maximum puis renvoie <c>gIB</c>.
        /// </summary>
        public Task<InteractionResult> BoostAsync(GuildBoostKind kind)
        {
            if (!HasGuild) return Refused(GuildTexts.Get("UI_ONLY_FOR_GUILD", "Pour accéder à cette interface, il faut que tu fasses partie d'une guilde."));
            if (!Guild.CanDo(GuildRight.Boost)) return Refused(GuildTexts.Get("NOT_ENOUGHT_RIGHTS_FROM_GUILD", "Tu n'as pas les droits suffisant dans ta guilde pour réaliser cette action."));
            GuildBoostRule rule = GuildTexts.BoostRule(kind);
            GuildBoosts boosts = Guild.Boosts;
            if (boosts != null)
            {
                if (boosts.Points < rule.Cost) return Refused("Points insuffisants : " + rule.Cost.ToString(CultureInfo.InvariantCulture) + " requis, " + boosts.Points.ToString(CultureInfo.InvariantCulture) + " à répartir.");
                if (boosts.Value(kind) >= rule.Max) return Refused(GuildTexts.BoostName(kind) + " au maximum (" + rule.Max.ToString(CultureInfo.InvariantCulture) + ").");
            }
            return SendAsync("gB" + (char)kind, "Augmentation de « " + GuildTexts.BoostName(kind) + " » demandée.");
        }

        /// <summary>Bouton + d'un sort de percepteur (<c>Guild.boostSpell</c>) : <c>gb&lt;sort&gt;</c> (5 points par niveau).</summary>
        public Task<InteractionResult> BoostSpellAsync(int spellId)
        {
            if (!HasGuild) return Refused(GuildTexts.Get("UI_ONLY_FOR_GUILD", "Pour accéder à cette interface, il faut que tu fasses partie d'une guilde."));
            if (!Guild.CanDo(GuildRight.Boost)) return Refused(GuildTexts.Get("NOT_ENOUGHT_RIGHTS_FROM_GUILD", "Tu n'as pas les droits suffisant dans ta guilde pour réaliser cette action."));
            if (spellId <= 0) return Refused("Sort invalide.");
            GuildBoostRule rule = GuildTexts.SpellRule();
            GuildBoosts boosts = Guild.Boosts;
            if (boosts != null)
            {
                if (boosts.Points < rule.Cost) return Refused("Points insuffisants : " + rule.Cost.ToString(CultureInfo.InvariantCulture) + " requis, " + boosts.Points.ToString(CultureInfo.InvariantCulture) + " à répartir.");
                GuildSpell spell = boosts.Spells.FirstOrDefault(entry => entry.Id == spellId);
                if (spell != null && spell.Level >= rule.Max) return Refused("Ce sort est déjà au niveau " + rule.Max.ToString(CultureInfo.InvariantCulture) + ".");
            }
            return SendAsync("gb" + spellId.ToString(CultureInfo.InvariantCulture), "Augmentation du sort " + spellId.ToString(CultureInfo.InvariantCulture) + " demandée.");
        }

        /// <summary>
        /// « Poser un percepteur » (<c>Guild.hireTaxCollector</c>) : <c>gH</c>, après la question <c>DO_YOU_HIRE_TAXCOLLECTOR</c> posée par
        /// l'interface. StarLoco répond par <c>GM</c>, <c>gITM</c> et <c>gTS</c>, ou <c>Im182</c>, <c>Im1168;1</c>, <c>Im113</c>, <c>Im1167</c>.
        /// </summary>
        public Task<InteractionResult> HireCollectorAsync()
        {
            if (!HasGuild) return Refused(GuildTexts.Get("UI_ONLY_FOR_GUILD", "Pour accéder à cette interface, il faut que tu fasses partie d'une guilde."));
            if (!Guild.CanDo(GuildRight.HireCollector)) return Refused(GuildTexts.Get("NOT_ENOUGHT_RIGHTS_FROM_GUILD", "Tu n'as pas les droits suffisant dans ta guilde pour réaliser cette action."));
            if (account?.IsFighting() == true) return Refused("Action impossible pendant un combat.");
            GuildBoosts boosts = Guild.Boosts;
            string cost = boosts != null ? " (" + boosts.HireCost.ToString(CultureInfo.InvariantCulture) + " kamas)" : string.Empty;
            return SendAsync("gH", "Pose d'un percepteur demandée" + cost + ".");
        }

        /// <summary>« Retirer » d'un percepteur de la guilde (<c>Guild.removeTaxCollector</c>) : <c>gF&lt;id&gt;</c> ; droit de pose exigé par StarLoco.</summary>
        public Task<InteractionResult> RemoveCollectorAsync(long collectorId)
        {
            if (!HasGuild) return Refused(GuildTexts.Get("UI_ONLY_FOR_GUILD", "Pour accéder à cette interface, il faut que tu fasses partie d'une guilde."));
            if (!Guild.CanDo(GuildRight.HireCollector)) return Refused(GuildTexts.Get("NOT_ENOUGHT_RIGHTS_FROM_GUILD", "Tu n'as pas les droits suffisant dans ta guilde pour réaliser cette action."));
            return SendAsync("gF" + collectorId.ToString(CultureInfo.InvariantCulture), "Retrait du percepteur demandé.");
        }

        /// <summary>Ouverture de la fenêtre (<c>Guild.getInfosGeneral</c>) : <c>gIG</c> ; les onglets suivent par <see cref="RequestAsync"/>.</summary>
        public Task<InteractionResult> OpenAsync()
        {
            if (!HasGuild) return Refused(GuildTexts.Get("UI_ONLY_FOR_GUILD", "Pour accéder à cette interface, il faut que tu fasses partie d'une guilde."));
            return SendAsync("gIG", "Informations générales de la guilde demandées.");
        }

        /// <summary>Contenu d'un onglet : <c>gIM</c>, <c>gIB</c>, <c>gIT</c>, <c>gIF</c> ou <c>gIH</c>.</summary>
        public Task<InteractionResult> RequestAsync(GuildTab tab)
        {
            if (!HasGuild) return Refused(GuildTexts.Get("UI_ONLY_FOR_GUILD", "Pour accéder à cette interface, il faut que tu fasses partie d'une guilde."));
            string packet;
            switch (tab)
            {
                case GuildTab.Boosts: packet = "gIB"; break;
                case GuildTab.Collectors: packet = "gIT"; break;
                case GuildTab.Paddocks: packet = "gIF"; break;
                case GuildTab.Houses: packet = "gIH"; break;
                default: packet = "gIM"; break;
            }
            return SendAsync(packet, "Onglet « " + GuildTexts.Tab(tab) + " » demandé.");
        }

        /// <summary>Fermeture de la fenêtre (<c>Guild.leaveTaxInterface</c>) : <c>gITV</c>, que StarLoco lit comme <c>gIT</c> (il renvoie les percepteurs).</summary>
        public Task<InteractionResult> CloseAsync()
        {
            if (!HasGuild) return Refused("Aucune guilde.");
            return SendAsync("gITV", "Fermeture de la fenêtre de guilde signalée.");
        }

        /// <summary>
        /// « Collecter » du menu d'un percepteur de sa guilde : <c>ER8|&lt;id&gt;</c> (<c>Exchange.request</c>). StarLoco exige la même guilde,
        /// la même carte, un percepteur hors combat et le droit de collecte (<c>Im1101</c>), puis ouvre <c>ECK8|&lt;id&gt;</c> et <c>EL</c>.
        /// </summary>
        public Task<InteractionResult> CollectAsync(long collectorId)
        {
            if (!HasGuild) return Refused(GuildTexts.Get("UI_ONLY_FOR_GUILD", "Pour accéder à cette interface, il faut que tu fasses partie d'une guilde."));
            var collector = account?.Game?.Map?.GetActor(collectorId) as CollectorActor;
            if (collector == null) return Refused("Ce percepteur n'est plus sur la carte.");
            if (!IsOwnCollector(collector)) return Refused(GuildTexts.Get("NOT_YOUR_TAXCOLLECTORS", "Ce percepteur n'appartient pas à votre guilde."));
            if (!Guild.CanDo(GuildRight.Collect)) return Refused(GuildTexts.Get("NOT_ENOUGHT_RIGHTS_FROM_GUILD", "Tu n'as pas les droits suffisant dans ta guilde pour réaliser cette action."));
            CollectorExchange window = CollectorWindow;
            if (window == null) return Refused("La fenêtre de collecte n'est pas disponible.");
            return window.OpenAsync(collectorId);
        }

        /// <summary>Fenêtre d'échange de type 8 (contenu du percepteur), créée par le registre des échanges.</summary>
        public CollectorExchange CollectorWindow => account?.Game?.Interactions?.Exchanges?.Get<CollectorExchange>();

        /// <summary>Percepteur de la guilde du personnage (nom de guilde de <c>GM</c>) : le client grise « Attaquer » et propose « Collecter » / « Retirer ».</summary>
        public bool IsOwnCollector(CollectorActor collector) =>
            collector != null && HasGuild && string.Equals(collector.GuildName, Guild.Name, StringComparison.Ordinal);
        public bool CanCollect(CollectorActor collector) => IsOwnCollector(collector) && Guild.CanDo(GuildRight.Collect);
        public bool CanRemoveCollector(CollectorActor collector) => IsOwnCollector(collector) && Guild.CanDo(GuildRight.HireCollector);

        /// <summary>
        /// Création (<c>Guild.create</c>) : <c>gC&lt;fond&gt;|&lt;couleur fond&gt;|&lt;motif&gt;|&lt;couleur motif&gt;|&lt;nom&gt;</c> (décimal). Possible
        /// seulement quand le serveur a ouvert le panneau (<c>gn</c>, guildalogemme utilisée sur la carte 2196). Réponses :
        /// <c>gS</c> + <c>gCK</c>, ou <c>gCEa</c> / <c>gCEan</c> / <c>gCEae</c>. StarLoco limite le nom à 20 lettres, tirets et apostrophes.
        /// </summary>
        public Task<InteractionResult> CreateAsync(int backId, int backColor, int upId, int upColor, string name)
        {
            name = (name ?? string.Empty).Trim();
            if (HasGuild) return Refused(GuildTexts.Get("GUILD_CREATE_ALLREADY_IN_GUILD", "Tu es déjà membre d'une guilde. Il t'est impossible d'en créer une nouvelle sans quitter la première."));
            if (!creationWindowOpen) return Refused("Utilisez une guildalogemme au temple des guildes (carte " + CreationMapId.ToString(CultureInfo.InvariantCulture) + ") : le serveur ouvre alors le panneau de création (gn).");
            if (name.Length == 0 || name.Length > 20 || name.Count(character => character == '-') > 2
                || !name.All(character => (character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z') || character == '-' || character == '\''))
                return Refused("Nom de guilde invalide : 1 à 20 lettres sans accent ou apostrophes, deux tirets au plus.");
            if (backId < 1 || backColor < 0 || upId < 1 || upColor < 0) return Refused("Emblème invalide.");
            return SendAsync("gC" + backId.ToString(CultureInfo.InvariantCulture) + "|" + backColor.ToString(CultureInfo.InvariantCulture) + "|"
                + upId.ToString(CultureInfo.InvariantCulture) + "|" + upColor.ToString(CultureInfo.InvariantCulture) + "|" + name, "Création de la guilde « " + name + " » demandée.");
        }

        /// <summary>Fermeture du panneau de création (<c>Guild.leave</c>) : <c>gV</c> ; le serveur répond <c>gV</c>.</summary>
        public Task<InteractionResult> LeaveCreationAsync()
        {
            if (!creationWindowOpen) return Refused("Le panneau de création n'est pas ouvert.");
            return SendAsync("gV", "Fermeture du panneau de création demandée.");
        }

        // ----- Réceptions (GuildFrame, fil réseau) -----

        /// <summary><c>gS&lt;nom&gt;|&lt;fond&gt;|&lt;couleur&gt;|&lt;motif&gt;|&lt;couleur&gt;|&lt;droits&gt;</c>, tout en base 36 sauf le nom (<c>Guild.onStats</c>).</summary>
        internal void OnStatsPacket(string message)
        {
            string[] fields = Body(message, 2).Split('|');
            if (fields.Length < 2 || fields[0].Length == 0) { Malformed(message); return; }
            long back, backColor, up, upColor, rights;
            if (!GuildTexts.TryBase36(fields[1], out back)) back = 0;
            if (fields.Length < 3 || !GuildTexts.TryBase36(fields[2], out backColor)) backColor = 0;
            if (fields.Length < 4 || !GuildTexts.TryBase36(fields[3], out up)) up = 0;
            if (fields.Length < 5 || !GuildTexts.TryBase36(fields[4], out upColor)) upColor = 0;
            if (fields.Length < 6 || !GuildTexts.TryBase36(fields[5], out rights)) rights = 0;
            Guild.SetStats(fields[0], (int)back, (int)backColor, (int)up, (int)upColor, (int)rights);
            SetCharacterGuild(true);
            Debug("Guilde « " + fields[0] + " », droits " + rights.ToString(CultureInfo.InvariantCulture) + " (" + GuildRights.Describe((int)rights) + ").");
            RaiseChanged();
        }

        /// <summary><c>gI&lt;G|M|B|T|F|H&gt;…</c> : aiguillage sur la troisième lettre comme le client.</summary>
        internal void OnInfosPacket(string message)
        {
            switch (message.Length > 2 ? message[2] : '\0')
            {
                case 'G': OnGeneralPacket(message); break;
                case 'M': OnMembersPacket(message); break;
                case 'B': OnBoostsPacket(message); break;
                case 'T': OnCollectorsPacket(message); break;
                case 'F': OnPaddocksPacket(message); break;
                case 'H': OnHousesPacket(message); break;
                default: Debug("Paquet gI inconnu ignoré : " + Preview(message)); break;
            }
        }

        /// <summary><c>gIG&lt;1|0&gt;|&lt;niveau&gt;|&lt;xp min&gt;|&lt;xp&gt;|&lt;xp max&gt;</c> (<c>Guild.onInfosGeneral</c>).</summary>
        private void OnGeneralPacket(string message)
        {
            string[] fields = Body(message, 3).Split('|');
            if (fields.Length < 5) { Malformed(message); return; }
            Guild.SetGeneral(fields[0] == "1", GuildTexts.Int(fields, 1), GuildTexts.Long(fields, 2), GuildTexts.Long(fields, 3), GuildTexts.Long(fields, 4, -1));
            RaiseChanged();
        }

        /// <summary><c>gIM+&lt;membre&gt;|…</c> (ajout ou mise à jour) / <c>gIM-&lt;id&gt;|…</c> (retrait) (<c>Guild.onInfosMembers</c>).</summary>
        private void OnMembersPacket(string message)
        {
            string body = Body(message, 3);
            if (body.Length == 0) { Malformed(message); return; }
            bool add = body[0] == '+';
            var entries = new List<GuildMember>();
            int unreadable = 0;
            if (body.Length > 1)
                foreach (string line in body.Substring(1).Split('|'))
                {
                    GuildMember member;
                    if (line.Length == 0) continue;
                    // Retrait (gIM-) : le client ne lit que l'identifiant de chaque entrée ; StarLoco n'envoie que gIM+ (liste entière).
                    string entry = add ? line : line.Split(';')[0] + ";";
                    if (GuildMember.TryParse(entry, out member)) entries.Add(member); else unreadable++;
                }
            Guild.ApplyMembers(add, entries);
            if (unreadable > 0) Debug(unreadable.ToString(CultureInfo.InvariantCulture) + " ligne(s) de membre illisible(s) ignorée(s) : " + Preview(message));
            RaiseChanged();
        }

        /// <summary><c>gIB…</c> (<c>Guild.onInfosBoosts</c>) ; vide = aucun boost (<c>setNoBoosts</c>).</summary>
        private void OnBoostsPacket(string message)
        {
            string body = Body(message, 3);
            GuildBoosts boosts = null;
            if (body.Length > 0 && !GuildBoosts.TryParse(body, out boosts)) { Malformed(message); return; }
            Guild.SetBoosts(boosts);
            RaiseChanged();
        }

        /// <summary>
        /// <c>gITM±…</c> percepteurs (<c>onInfosTaxCollectorsMovement</c> ; vide ou <c>null</c> = aucun), <c>gITp±&lt;id36&gt;|…</c> attaquants
        /// (<c>onInfosTaxCollectorsAttackers</c>), <c>gITP±&lt;id36&gt;|…</c> défenseurs (<c>onInfosTaxCollectorsPlayers</c>).
        /// </summary>
        private void OnCollectorsPacket(string message)
        {
            char kind = message.Length > 3 ? message[3] : '\0';
            string body = Body(message, 4);
            if (kind == 'M')
            {
                if (body.Length == 0 || body == "null") { Guild.ClearCollectors(); RaiseChanged(); return; }
                if (body[0] != '+' && body[0] != '-') { Malformed(message); return; }
                var entries = new List<GuildCollector>();
                var removed = new List<long>();
                int unreadable = 0;
                foreach (string line in body.Substring(1).Split('|'))
                {
                    GuildCollector collector; long id;
                    if (line.Length == 0) continue;
                    // Retrait : le client ne lit que l'identifiant (base 36) de chaque entrée (removeTaxCollector).
                    if (body[0] == '-') { if (GuildTexts.TryBase36(line.Split(';')[0], out id)) removed.Add(id); else unreadable++; }
                    else if (GuildCollector.TryParse(line, out collector)) entries.Add(collector); else unreadable++;
                }
                if (body[0] == '+') Guild.ApplyCollectors(true, entries); else Guild.RemoveCollectors(removed);
                if (unreadable > 0) Debug(unreadable.ToString(CultureInfo.InvariantCulture) + " percepteur(s) illisible(s) ignoré(s) : " + Preview(message));
                RaiseChanged();
                return;
            }
            if (kind != 'p' && kind != 'P') { Debug("Paquet gIT inconnu ignoré : " + Preview(message)); return; }
            if (body.Length < 2 || (body[0] != '+' && body[0] != '-')) { Malformed(message); return; }
            string[] parts = body.Substring(1).Split('|');
            long collectorId;
            if (!GuildTexts.TryBase36(parts[0], out collectorId)) { Malformed(message); return; }
            bool defenders = kind == 'P';
            var fighters = new List<GuildCollectorFighter>();
            for (int index = 1; index < parts.Length; index++)
            {
                GuildCollectorFighter fighter;
                if (parts[index].Length == 0) continue;
                if (GuildCollectorFighter.TryParse(parts[index], defenders, out fighter)) fighters.Add(fighter);
                else if (body[0] == '-')
                {
                    // Retrait : seul l'identifiant est transmis.
                    long id;
                    if (GuildTexts.TryBase36(parts[index].Split(';')[0], out id) && GuildCollectorFighter.TryParse(GuildTexts.Base36(id) + ";", defenders, out fighter)) fighters.Add(fighter);
                }
            }
            if (!Guild.ApplyFighters(collectorId, body[0] == '+', defenders, fighters))
                Debug("[gIT" + kind + "] impossible de trouver le percepteur " + collectorId.ToString(CultureInfo.InvariantCulture) + ".");
            RaiseChanged();
        }

        /// <summary><c>gIF&lt;enclos max&gt;|&lt;enclos&gt;|…</c> (<c>Guild.onInfosMountPark</c>).</summary>
        private void OnPaddocksPacket(string message)
        {
            string[] fields = Body(message, 3).Split('|');
            var paddocks = new List<GuildPaddock>();
            int unreadable = 0;
            for (int index = 1; index < fields.Length; index++)
            {
                GuildPaddock paddock;
                if (fields[index].Length == 0) continue;
                if (GuildPaddock.TryParse(fields[index], out paddock)) paddocks.Add(paddock); else unreadable++;
            }
            Guild.SetPaddocks(GuildTexts.Int(fields, 0), paddocks);
            if (unreadable > 0) Debug(unreadable.ToString(CultureInfo.InvariantCulture) + " enclos illisible(s) ignoré(s) : " + Preview(message));
            RaiseChanged();
        }

        /// <summary><c>gIH+&lt;maison&gt;|…</c> (<c>Guild.onInfosHouses</c>) ; un seul caractère ou moins = aucune maison.</summary>
        private void OnHousesPacket(string message)
        {
            string body = Body(message, 3);
            var houses = new List<GuildHouse>();
            int unreadable = 0;
            if (body.Length > 1)
                foreach (string line in body.Substring(1).Split('|'))
                {
                    GuildHouse house;
                    if (line.Length == 0) continue;
                    if (GuildHouse.TryParse(line, out house)) houses.Add(house); else unreadable++;
                }
            Guild.SetHouses(houses);
            if (unreadable > 0) Debug(unreadable.ToString(CultureInfo.InvariantCulture) + " maison(s) illisible(s) ignorée(s) : " + Preview(message));
            RaiseChanged();
        }

        /// <summary><c>gJR</c>, <c>gJr</c>, <c>gJE</c>, <c>gJK</c>, <c>gJC</c> : invitations (<c>Guild.onRequestLocal</c>, <c>onRequestDistant</c>, <c>onJoinError</c>, <c>onJoinOk</c>, <c>onJoinDistantOk</c>).</summary>
        internal Task OnJoinPacket(string message)
        {
            char code = message.Length > 2 ? message[2] : '\0';
            string data = Body(message, 3);
            switch (code)
            {
                case 'R':
                    lock (sync) outgoingInvitee = data;
                    Inform(GuildTexts.Get("YOU_INVIT_B_IN_GUILD", "Tu invites {0} à rejoindre ta guilde...", data));
                    break;
                case 'r':
                    return OnInviteReceived(message, data);
                case 'E':
                    OnJoinError(data);
                    break;
                case 'K':
                    if (data.StartsWith("a", StringComparison.Ordinal))
                    {
                        lock (sync) outgoingInvitee = null;
                        Inform(GuildTexts.Get("A_JOIN_YOUR_GUILD", "{0} a rejoint ta guilde.", data.Substring(1)));
                    }
                    else
                    {
                        // gJKj : le gS qui précède a posé le nom de la guilde.
                        ClearPending(null);
                        SetCharacterGuild(true);
                        Inform(GuildTexts.Get("YOUR_R_NEW_IN_GUILD", "Tu viens d'intégrer la guilde {0}.", Guild.Name));
                    }
                    RaiseInvitationClosed();
                    break;
                case 'C':
                    ClearPending(null);
                    RaiseInvitationClosed();
                    break;
                default:
                    Debug("Paquet gJ inconnu ignoré : " + Preview(message));
                    break;
            }
            return Task.CompletedTask;
        }

        private void OnJoinError(string data)
        {
            switch (data.Length > 0 ? data[0] : '\0')
            {
                case 'a': Fail(GuildTexts.Get("GUILD_JOIN_ALREADY_IN_GUILD", "Impossible, ce joueur est déjà dans une guilde")); break;
                case 'd': Fail(GuildTexts.Get("GUILD_JOIN_NO_RIGHTS", "Tu n'as pas le droit d'inviter des joueurs dans ta guilde.")); break;
                case 'u': Fail(GuildTexts.Get("GUILD_JOIN_UNKNOW", "Impossible d'inviter, ce joueur est inconnu ou non connecté.")); break;
                case 'o': Fail(GuildTexts.Get("GUILD_JOIN_OCCUPED", "Ce joueur est occupé. Impossible de l'inviter.")); break;
                case 'r':
                    lock (sync) outgoingInvitee = null;
                    Fail(GuildTexts.Get("GUILD_JOIN_REFUSED", "{0} refuse d'intégrer ta guilde.", data.Substring(1)));
                    RaiseInvitationClosed();
                    break;
                case 'c':
                    // StarLoco répond gJEc à l'invitant quand l'invité refuse : les deux boîtes du client se ferment.
                    string invitee = OutgoingInvitee;
                    lock (sync) outgoingInvitee = null;
                    ClearPending(null);
                    if (!string.IsNullOrEmpty(invitee)) Fail(GuildTexts.Get("GUILD_JOIN_REFUSED", "{0} refuse d'intégrer ta guilde.", invitee));
                    RaiseInvitationClosed();
                    break;
                default: Debug("Erreur d'invitation inconnue ignorée : " + Preview(data)); break;
            }
        }

        /// <summary>
        /// <c>gJr&lt;id invitant&gt;|&lt;nom&gt;|&lt;guilde&gt;</c> (<c>Guild.onRequestDistant</c>) : proposée à <c>GameSession.GuildInviteReceived</c> ;
        /// sans abonné qui s'en charge, le bot refuse comme le client : <c>gJE&lt;id&gt;</c>, l'identifiant étant ce que StarLoco compare
        /// (<c>GameClient.invitationGuild</c>). Un invitant ignoré est refusé sans question, comme la liste noire du chat du client.
        /// </summary>
        private Task OnInviteReceived(string message, string data)
        {
            string[] fields = data.Split('|');
            string inviterId = fields[0];
            int parsed;
            if (!int.TryParse(inviterId, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                account?.Logger?.LogDanger(Reference, "Invitation de guilde sans identifiant d'invitant ignorée : " + Preview(message));
                return Task.CompletedTask;
            }
            string inviter = fields.Length > 1 && fields[1].Length > 0 ? fields[1] : inviterId, guild = fields.Length > 2 ? fields[2] : string.Empty;
            if (IsIgnored(inviter))
            {
                account?.Logger?.LogInfo(Reference, "Invitation de guilde de " + inviter + " refusée : joueur ignoré pour la session.");
                return SendRawAsync("gJE" + inviterId);
            }
            lock (sync) { pendingInviterId = inviterId; pendingInviterName = inviter; pendingGuildName = guild; }
            Inform(GuildTexts.Get("CHAT_A_INVIT_YOU_IN_GUILD", "{0} t'invite à rejoindre sa guilde ({1})", inviter, guild));
            GameSession session = account?.Game?.Session;
            if (session != null && session.OnGuildInvite(new InvitationEventArgs(inviter, inviterId, string.Empty, guild))) return Task.CompletedTask;
            ClearPending(inviterId);
            account?.Logger?.LogInfo(Reference, "Invitation de " + inviter + " dans la guilde " + guild + " refusée : le bot n'accepte pas d'invitation automatiquement.");
            return SendRawAsync("gJE" + inviterId);
        }

        /// <summary><c>gKK&lt;a&gt;|&lt;b&gt;</c> / <c>gKK&lt;a&gt;</c> (<c>Guild.onBann</c>) ; <c>gKE&lt;d|a&gt;</c> : pas le droit, pas membre (aussi pour <c>gP</c>).</summary>
        internal void OnKickPacket(string message)
        {
            char code = message.Length > 2 ? message[2] : '\0';
            string data = Body(message, 3);
            if (code == 'E')
            {
                if (data.StartsWith("d", StringComparison.Ordinal)) Fail(GuildTexts.Get("NOT_ENOUGHT_RIGHTS_FROM_GUILD", "Tu n'as pas les droits suffisant dans ta guilde pour réaliser cette action."));
                else if (data.StartsWith("a", StringComparison.Ordinal)) Fail(GuildTexts.Get("CANT_BANN_FROM_GUILD_NOT_MEMBER", "Impossible, ce personnage ne fait pas partie de la guilde."));
                else Debug("Erreur de bannissement inconnue ignorée : " + Preview(message));
                return;
            }
            if (code != 'K' || data.Length == 0) { Malformed(message); return; }
            string[] names = data.Split('|');
            string kicker = names[0], target = names.Length > 1 ? names[1] : null;
            string self = account?.Game?.character?.Name ?? string.Empty;
            if (string.Equals(kicker, self, StringComparison.Ordinal))
            {
                if (target == null) { Malformed(message); return; }
                if (string.Equals(kicker, target, StringComparison.Ordinal))
                {
                    LeaveGuildLocally();
                    Inform(GuildTexts.Get("YOU_BANN_YOU_FROM_GUILD", "Tu as quitté ta guilde"));
                    return;
                }
                GuildMember member = Guild.FindMember(target);
                if (member != null) Guild.ApplyMembers(false, new[] { member });
                Inform(GuildTexts.Get("YOU_BANN_A_FROM_GUILD", "Tu as banni {0} de ta guilde.", target));
                return;
            }
            LeaveGuildLocally();
            Inform(GuildTexts.Get("YOU_ARE_BANN_BY_A_FROM_GUILD", "{0} t'a banni de la guilde", kicker));
        }

        /// <summary><c>gV</c> (<c>Guild.onLeave</c>) : le panneau de création se ferme.</summary>
        internal void OnLeavePacket()
        {
            creationWindowOpen = false;
            RaiseChanged();
        }

        /// <summary><c>gn</c> (<c>Guild.onNew</c>) : le serveur ouvre le panneau de création (guildalogemme sur la carte 2196).</summary>
        internal void OnNewPacket()
        {
            creationWindowOpen = true;
            Inform("Panneau de création de guilde ouvert par le serveur : choisissez un nom et un emblème.");
        }

        /// <summary><c>gCK</c> : guilde créée (le <c>gS</c> précède) ; <c>gCE&lt;a|an|ae&gt;</c> : déjà en guilde, nom ou emblème déjà pris (<c>Guild.onCreate</c>).</summary>
        internal void OnCreatePacket(string message)
        {
            char code = message.Length > 2 ? message[2] : '\0';
            string data = Body(message, 3);
            if (code == 'K')
            {
                creationWindowOpen = false;
                SetCharacterGuild(true);
                Inform(GuildTexts.Get("GUILD_CREATED", "Guilde créée"));
                RaiseOpenTab(GuildTab.Members);
                return;
            }
            if (code != 'E') { Malformed(message); return; }
            string text;
            switch (data)
            {
                case "an": text = GuildTexts.Get("GUILD_CREATE_ALLREADY_USE_NAME", "Le nom de guilde est déjà utilisé."); break;
                case "ae": text = GuildTexts.Get("GUILD_CREATE_ALLREADY_USE_EMBLEM", "Le blason est déjà utilisé."); break;
                case "a": text = GuildTexts.Get("GUILD_CREATE_ALLREADY_IN_GUILD", "Tu es déjà membre d'une guilde. Il t'est impossible d'en créer une nouvelle sans quitter la première."); break;
                default: Debug("Erreur de création inconnue ignorée : " + Preview(message)); return;
            }
            Fail(text);
            RaiseErrorBox(GuildTexts.Get("GUILD", "Guilde"), text);
        }

        /// <summary><c>gHE&lt;d|a|k|m|b|y|h&gt;</c> (<c>Guild.onHireTaxCollector</c>) ; StarLoco n'envoie jamais <c>gH…</c> (il répond par <c>Im…</c>).</summary>
        internal void OnHirePacket(string message)
        {
            char code = message.Length > 2 ? message[2] : '\0';
            string data = Body(message, 3);
            if (code != 'E') { Debug("Paquet gH ignoré : " + Preview(message)); return; }
            switch (data.Length > 0 ? data[0] : '\0')
            {
                case 'd': Fail(GuildTexts.Get("NOT_ENOUGHT_RIGHTS_FROM_GUILD", "Tu n'as pas les droits suffisant dans ta guilde pour réaliser cette action.")); break;
                case 'a': Fail(GuildTexts.Get("ALREADY_TAXCOLLECTOR_ON_MAP", "Il y a déjà un percepteur sur cette carte. Impossible d'en poser un autre.")); break;
                case 'k': Fail(GuildTexts.Get("NOT_ENOUGTH_RICH_TO_HIRE_TAX", "Tu n'as pas assez de kamas pour poser un percepteur.")); break;
                case 'm': Fail(GuildTexts.Get("CANT_HIRE_MAX_TAXCOLLECTORS", "Le nombre maximum de percepteur pour ta guilde est déjà atteint.")); break;
                case 'b': Fail(GuildTexts.Get("NOT_YOUR_TAXCOLLECTORS", "Ce percepteur n'appartient pas à votre guilde.")); break;
                case 'y': Fail(GuildTexts.Get("CANT_HIRE_TAXCOLLECTORS_TOO_TIRED", "Impossible de poser le percepteur maintenant, il doit se reposer.")); break;
                case 'h': Fail(GuildTexts.Get("CANT_HIRE_TAXCOLLECTORS_HERE", "Vous n'êtes pas autorisé à poser un percepteur sur cette carte.")); break;
                default: Debug("Erreur de pose inconnue ignorée : " + Preview(message)); break;
            }
        }

        /// <summary>
        /// <c>gT&lt;S|R|G&gt;&lt;prénom36,nom36&gt;|&lt;carte ou .&gt;|&lt;x&gt;|&lt;y&gt;|&lt;joueur&gt;[|&lt;xp&gt;;&lt;objet,quantité&gt;;…]</c> (<c>Guild.onTaxCollectorInfo</c>) :
        /// percepteur posé, retiré ou récolté, écrit dans le chat. <c>gTK</c> / <c>gTE</c> répondraient à <c>gTJ</c> / <c>gTV</c>, jamais envoyés.
        /// </summary>
        internal void OnCollectorInfoPacket(string message)
        {
            string[] fields = Body(message, 2).Split('|');
            if (fields.Length < 1 || fields[0].Length == 0) { Malformed(message); return; }
            char kind = fields[0][0];
            if (kind == 'K' || kind == 'E') { Debug("Réponse gT" + kind + " ignorée (gTJ/gTV ne sont jamais émis) : " + Preview(message)); return; }
            if (fields.Length < 5) { Malformed(message); return; }
            string name = CollectorName(fields[0].Substring(1)), where = "(" + fields[2] + ", " + fields[3] + ")", who = fields[4];
            switch (kind)
            {
                case 'S': Inform(GuildTexts.Get("TAXCOLLECTOR_ADDED", "Le percepteur {0} a été posé en {1} par {2}.", name, where, who)); break;
                case 'R': Inform(GuildTexts.Get("TAXCOLLECTOR_REMOVED", "Le percepteur {0} en {1} a été retiré par {2}.", name, where, who)); break;
                case 'G':
                    string[] loot = (fields.Length > 5 ? fields[5] : string.Empty).Split(';');
                    string details = GuildTexts.Long(loot, 0).ToString(CultureInfo.InvariantCulture) + " " + GuildTexts.Get("EXPERIENCE_POINT", "Points d'expérience");
                    for (int index = 1; index < loot.Length; index++)
                    {
                        string[] pair = loot[index].Split(',');
                        if (pair.Length < 2) continue;
                        int itemId;
                        string itemName = int.TryParse(pair[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out itemId) && LangData.Item.Has(itemId)
                            ? LangData.Item.Name(itemId) : "objet n° " + pair[0];
                        details += ", " + pair[1] + " x " + itemName;
                    }
                    Inform(GuildTexts.Get("TAXCOLLECTOR_RECOLTED", "{2} a relevé la collecte sur le percepteur {0} en {1} et recolté : {3}", name, where, who, details + "."));
                    break;
                default: Debug("Paquet gT inconnu ignoré : " + Preview(message)); break;
            }
        }

        /// <summary><c>gA&lt;A|S|D&gt;&lt;prénom36,nom36&gt;|&lt;niveau&gt;|&lt;x&gt;|&lt;y&gt;</c> (<c>Guild.onTaxCollectorAttacked</c>) : attaqué, survivant, mort.</summary>
        internal void OnCollectorAttackedPacket(string message)
        {
            string[] fields = Body(message, 2).Split('|');
            if (fields.Length < 4 || fields[0].Length == 0) { Malformed(message); return; }
            string name = CollectorName(fields[0].Substring(1)), where = "(" + fields[2] + ", " + fields[3] + ")";
            string text;
            switch (fields[0][0])
            {
                case 'A': text = GuildTexts.Get("TAX_ATTACKED", "Le percepteur {0} est attaqué en {1}", name, where); break;
                case 'S': text = GuildTexts.Get("TAX_ATTACKED_SUVIVED", "Le percepteur {0} attaqué en {1} a survécu !", name, where); break;
                case 'D': text = GuildTexts.Get("TAX_ATTACKED_DIED", "Le percepteur {0} attaqué en {1} n'a pas survécu !", name, where); break;
                default: Debug("Paquet gA inconnu ignoré : " + Preview(message)); return;
            }
            Inform(text);
            Raise(CollectorAlert, handler => handler(text));
        }

        /// <summary><c>gUT</c> / <c>gUF</c> (<c>Guild.onUserInterfaceOpen</c>) : objet qui ouvre l'onglet Maisons ou Enclos ; sans guilde, <c>ITEM_NEED_GUILD</c>.</summary>
        internal void OnUserInterfacePacket(string message)
        {
            char code = message.Length > 2 ? message[2] : '\0';
            if (code != 'T' && code != 'F') { Debug("Paquet gU inconnu ignoré : " + Preview(message)); return; }
            if (!HasGuild) { Fail(GuildTexts.Get("ITEM_NEED_GUILD", "Tu dois faire parti d'une guilde pour pouvoir utiliser cet objet")); return; }
            RaiseOpenTab(code == 'T' ? GuildTab.Houses : GuildTab.Paddocks);
        }

        /// <summary>Oublie l'état de session (déconnexion, changement de personnage) sans rien envoyer.</summary>
        public void Clear()
        {
            Guild.Clear();
            lock (sync)
            {
                pendingInviterId = pendingInviterName = pendingGuildName = outgoingInvitee = null;
                ignored.Clear();
                lastMessage = string.Empty;
            }
            creationWindowOpen = false;
            SetCharacterGuild(false);
            RaiseChanged();
        }

        // ----- Outils -----

        private void LeaveGuildLocally()
        {
            Guild.Clear();
            lock (sync) outgoingInvitee = null;
            SetCharacterGuild(false);
        }

        /// <summary><c>CharacterClass.HasGuild</c> (bouton Guilde du bandeau, canal <c>/g</c>) suit la guilde annoncée par le serveur.</summary>
        private void SetCharacterGuild(bool value)
        {
            try
            {
                var character = account?.Game?.character;
                if (character != null) character.HasGuild = value;
            }
            catch (Exception error) { account?.Logger?.LogException(Reference, error); }
        }

        private void ClearPending(string id)
        {
            lock (sync)
            {
                if (id != null && pendingInviterId != null && !string.Equals(pendingInviterId, id, StringComparison.Ordinal)) return;
                pendingInviterId = pendingInviterName = pendingGuildName = null;
            }
        }

        private static string CollectorName(string ids)
        {
            string[] parts = (ids ?? string.Empty).Split(',');
            long first, last = 0;
            if (!GuildTexts.TryBase36(parts[0], out first)) return "Percepteur";
            if (parts.Length > 1) GuildTexts.TryBase36(parts[1], out last);
            return GuildTexts.CollectorName((int)first, (int)last);
        }

        private static string Body(string message, int start) => message != null && message.Length > start ? message.Substring(start) : string.Empty;
        private static bool IsSendable(string text) => text.IndexOf('|') < 0 && !text.Any(char.IsControl);

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

        private async Task SendRawAsync(string packet)
        {
            var connection = account?.Connexion;
            if (connection == null || !connection.IsConnected()) return;
            try { await connection.SendPacket(packet).ConfigureAwait(false); }
            catch (Exception error) { account?.Logger?.LogException(Reference, error); }
        }

        private InteractionResult Refuse(string message) { SetMessage(message); RaiseChanged(); return new InteractionResult(false, message); }
        private Task<InteractionResult> Refused(string message) => Task.FromResult(Refuse(message));
        private void SetMessage(string message) { lock (sync) lastMessage = message ?? string.Empty; }
        private void Malformed(string message) => account?.Logger?.LogError(Reference, "Paquet de guilde illisible ignoré : " + Preview(message));

        /// <summary>Information (<c>INFO_CHAT</c> / <c>GUILD_CHAT</c> du client) : journal, ligne locale du chat et <see cref="LastMessage"/>.</summary>
        private void Inform(string text)
        {
            SetMessage(text);
            account?.Logger?.LogInfo(Reference, text);
            try { account?.Game?.Chat?.AddLocal(ChatMessageKind.Info, text); }
            catch (Exception error) { account?.Logger?.LogException(Reference, error); }
            RaiseChanged();
        }

        /// <summary>Erreur (<c>ERROR_CHAT</c> du client).</summary>
        private void Fail(string text)
        {
            SetMessage(text);
            account?.Logger?.LogError(Reference, text);
            try { account?.Game?.Chat?.AddLocal(ChatMessageKind.Error, text); }
            catch (Exception error) { account?.Logger?.LogException(Reference, error); }
            RaiseChanged();
        }

        private void Debug(string text) => account?.Logger?.LogDebug(Reference, text);

        private static string Preview(string text) => text == null ? string.Empty : text.Length > 80 ? text.Substring(0, 80) + "…" : text;

        private void RaiseChanged() => Raise(Changed, handler => handler());
        private void RaiseInvitationClosed() => Raise(InvitationClosed, handler => handler());
        private void RaiseErrorBox(string title, string text) => Raise(ErrorBox, handler => handler(title, text));
        private void RaiseOpenTab(GuildTab tab) => Raise(OpenTabRequested, handler => handler(tab));

        private void Raise<T>(T subscribers, Action<T> invoke) where T : class
        {
            var multicast = subscribers as Delegate;
            if (multicast == null) return;
            foreach (Delegate subscriber in multicast.GetInvocationList())
            {
                try { invoke((T)(object)subscriber); }
                catch (Exception error)
                {
                    try { account?.Logger?.LogError(Reference, "Un abonné de la guilde a échoué : " + error.Message); }
                    catch (Exception) { /* Journal fermé : la lecture des paquets continue. */ }
                }
            }
        }
    }
}
