using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Session;

namespace Tool_BotProtocol.Game.Groupes
{
    /// <summary>
    /// Position annoncée par <c>IH</c> (<c>Infos.onInfoCoordinatespHighlight</c> du client) :
    /// <c>x;y;carte;type;idJoueur;nom</c> par entrée, entrées séparées par « | ». StarLoco l'envoie pour <c>PW</c>
    /// (type 2 = membre du groupe) et pour les phénix d'un fantôme (<c>x;y</c> seulement : les autres champs sont absents).
    /// </summary>
    public sealed class PartyLocation
    {
        /// <summary>Type des positions de membres du groupe (le client efface ce type en quittant le groupe).</summary>
        public const int PartyMemberType = 2;
        public int X { get; private set; }
        public int Y { get; private set; }
        public int? MapId { get; private set; }
        public int? Type { get; private set; }
        public long? PlayerId { get; private set; }
        public string PlayerName { get; private set; } = string.Empty;
        public bool IsPartyMember => Type == PartyMemberType;

        /// <summary>Lit une entrée de <c>IH</c> ; <c>false</c> si x ou y n'est pas un nombre. Ne lève jamais d'exception.</summary>
        public static bool TryParse(string entry, out PartyLocation location)
        {
            location = null;
            string[] fields = (entry ?? string.Empty).Split(';');
            if (fields.Length < 2 || !TryInt(fields[0], out int x) || !TryInt(fields[1], out int y)) return false;
            location = new PartyLocation
            {
                X = x,
                Y = y,
                MapId = fields.Length > 2 && TryInt(fields[2], out int map) ? map : (int?)null,
                Type = fields.Length > 3 && TryInt(fields[3], out int type) ? type : (int?)null,
                PlayerId = fields.Length > 4 && long.TryParse(fields[4], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long id) ? id : (long?)null,
                PlayerName = fields.Length > 5 ? fields[5] : string.Empty,
            };
            return true;
        }

        private static bool TryInt(string text, out int value) =>
            int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

        public override string ToString() => "[" + X.ToString(CultureInfo.InvariantCulture) + "," + Y.ToString(CultureInfo.InvariantCulture) + "]"
            + (PlayerName.Length > 0 ? " " + PlayerName : string.Empty);
    }

    /// <summary>
    /// Textes du groupe : clé de <c>lang_fr</c> du client quand les fichiers de langue sont chargés (<see cref="LangData"/>),
    /// sinon un texte de repli propre au bot (formaté avec <see cref="string.Format(string, object[])"/>). Le HTML est retiré.
    /// </summary>
    public static class PartyTexts
    {
        public static string Get(string key, string fallback, params string[] args)
        {
            args = args ?? new string[0];
            try
            {
                if (key != null && LangData.Text.Has(key)) return LangData.Text.Plain(LangData.Text.Get(key, args));
            }
            catch (Exception) { /* Fichier de langue illisible : texte de repli. */ }
            try { return string.Format(CultureInfo.CurrentCulture, fallback ?? key ?? string.Empty, args.Cast<object>().ToArray()); }
            catch (FormatException) { return fallback ?? key ?? string.Empty; }
        }
    }

    /// <summary>
    /// Groupe du personnage selon <c>dofus.aks.Party</c> du client 1.34 et <c>GameClient.parseGroupPacket</c> de StarLoco.
    /// Envois : <c>PI&lt;nom&gt;</c>, <c>PA</c>, <c>PR</c>, <c>PV[&lt;id&gt;]</c>, <c>PF±&lt;id&gt;</c>, <c>PG±&lt;id&gt;</c>, <c>PW</c>.
    /// Réceptions (via <c>PartyFrame</c>) : <c>PIK</c>, <c>PIE</c>, <c>PCK</c>/<c>PCE</c>, <c>PL</c>, <c>PM±~</c>, <c>PV</c>, <c>PR</c>/<c>PA</c>,
    /// <c>PF</c>, <c>IC</c> (boussole) et <c>IH</c> (positions). L'état ne change qu'à la réception des paquets du serveur ;
    /// une invitation reçue n'est jamais acceptée seule, sauf celle du chef d'une équipe de comptes du bot (<see cref="Regroupement"/>).
    /// Les événements sont levés sur le fil de réception réseau : l'interface doit repasser sur son propre fil.
    /// </summary>
    public sealed class PartyActions
    {
        private const string Reference = "GROUPE";
        private readonly Accounts.Accounts account;
        private readonly object sync = new object();
        private readonly HashSet<string> ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string pendingInviter, outgoingInvitee, lastMessage = string.Empty;
        private PartyLocation[] locations = new PartyLocation[0];
        private Point? compass;
        private long? followAllTarget;

        internal PartyActions(Accounts.Accounts owner)
        {
            account = owner;
            Group = new Groupe(() => account?.Game?.character?.id ?? 0,
                error => account?.Logger?.LogError(Reference, "Un abonné du groupe a échoué : " + error.Message));
        }

        /// <summary>Groupe annoncé par le serveur (membres, chef, suivi).</summary>
        public Groupe Group { get; }
        /// <summary>Invitant d'une invitation reçue en attente de réponse (<c>PIK&lt;invitant&gt;|&lt;soi&gt;</c>), ou <c>null</c>.</summary>
        public string PendingInviter { get { lock (sync) return pendingInviter; } }
        /// <summary>Joueur invité par le personnage, en attente de sa réponse (<c>PIK&lt;soi&gt;|&lt;invité&gt;</c>), ou <c>null</c>.</summary>
        public string OutgoingInvitee { get { lock (sync) return outgoingInvitee; } }
        /// <summary>Invitants ignorés jusqu'à la fin de la session (« Ignorer » du client) : leurs invitations sont refusées sans question.</summary>
        public IReadOnlyCollection<string> IgnoredInviters { get { lock (sync) return ignored.ToArray(); } }
        public bool IsIgnored(string name) { if (string.IsNullOrEmpty(name)) return false; lock (sync) return ignored.Contains(name); }
        /// <summary>Dernières positions reçues par <c>IH</c> (réponse à <c>PW</c>, phénix…).</summary>
        public IReadOnlyList<PartyLocation> Locations { get { lock (sync) return locations; } }
        /// <summary>
        /// Coordonnées de la boussole (<c>IC&lt;x&gt;|&lt;y&gt;</c>), <c>null</c> après <c>IC|</c> ou la fin du suivi. StarLoco envoie
        /// aussi <c>IC</c> hors groupe (conjoint, objets, actions de PNJ) : c'est la cible générale de la boussole du bandeau.
        /// </summary>
        public Point? Compass { get { lock (sync) return compass; } }
        /// <summary>
        /// Membre que le chef a demandé à tout le groupe de suivre (<c>PG+&lt;id&gt;</c>), état local : StarLoco n'en accuse
        /// réception qu'aux suiveurs (<c>PF+&lt;id&gt;</c>).
        /// </summary>
        public long? FollowAllTargetId { get { lock (sync) return followAllTarget; } }
        /// <summary>Dernier message (confirmation, refus local, message du serveur traduit).</summary>
        public string LastMessage { get { lock (sync) return lastMessage; } }

        /// <summary>Invitations, liste d'ignorés ou message changés (fil réseau ou appelant).</summary>
        public event Action Changed;
        /// <summary>Positions <c>IH</c> reçues (liste vide = effacement).</summary>
        public event Action<IReadOnlyList<PartyLocation>> LocationsReceived;
        /// <summary>Boussole <c>IC</c> modifiée (<c>null</c> = plus de cible).</summary>
        public event Action<Point?> CompassChanged;
        /// <summary>Invitation (reçue ou envoyée) close par le serveur : <c>PR</c>, <c>PA</c>, <c>PCK</c> ou <c>PV</c>.</summary>
        public event Action InvitationClosed;

        // ----- Envois (formats de dofus.aks.Party) -----

        /// <summary><c>PI&lt;nom&gt;</c> ; le serveur répond <c>PIK&lt;soi&gt;|&lt;nom&gt;</c> ou <c>PIE&lt;n|a|f&gt;</c>.</summary>
        public Task<InteractionResult> InviteAsync(string name)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0) return Refused("Indiquez le nom du personnage à inviter.");
            if (name.IndexOf('|') >= 0 || name.Any(char.IsControl)) return Refused("Nom de personnage invalide : « " + name + " ».");
            string self = account?.Game?.character?.Name;
            if (!string.IsNullOrEmpty(self) && string.Equals(self, name, StringComparison.OrdinalIgnoreCase))
                return Refused("Le personnage ne peut pas s'inviter lui-même.");
            if (Group.Contains(name)) return Refused(name + " fait déjà partie du groupe.");
            if (Group.IsActive && Group.IsFull) return Refused(PartyTexts.Get("PARTY_FULL", "Le groupe est complet ({0} membres).", Groupe.MaxMembers.ToString(CultureInfo.InvariantCulture)));
            return SendAsync("PI" + name, "Invitation dans le groupe envoyée à " + name + ".");
        }

        /// <summary><c>PA</c> : accepte l'invitation reçue (le serveur répond <c>PCK</c>, <c>PL</c>, <c>PM+</c>).</summary>
        public async Task<InteractionResult> AcceptAsync()
        {
            string inviter = PendingInviter;
            if (inviter == null) return Refuse("Aucune invitation de groupe en attente.");
            InteractionResult result = await SendAsync("PA", "Invitation de " + inviter + " acceptée.").ConfigureAwait(false);
            if (result.Sent) ClearPending(inviter);
            return result;
        }

        /// <summary><c>PR</c> : refuse l'invitation reçue.</summary>
        public async Task<InteractionResult> RefuseAsync()
        {
            string inviter = PendingInviter;
            if (inviter == null) return Refuse("Aucune invitation de groupe en attente.");
            InteractionResult result = await SendAsync("PR", "Invitation de " + inviter + " refusée.").ConfigureAwait(false);
            if (result.Sent) ClearPending(inviter);
            return result;
        }

        /// <summary>« Ignorer » du client : refus <c>PR</c> et invitant ignoré pour la session (ses invitations suivantes sont refusées).</summary>
        public async Task<InteractionResult> IgnoreAsync()
        {
            string inviter = PendingInviter;
            if (inviter == null) return Refuse("Aucune invitation de groupe en attente.");
            lock (sync) ignored.Add(inviter);
            string text = PartyTexts.Get("TEMPORARY_BLACKLISTED", "{0} est ignoré(e) jusqu'à la fin de la session.", inviter);
            InteractionResult result = await SendAsync("PR", text).ConfigureAwait(false);
            if (result.Sent) ClearPending(inviter); else RaiseChanged();
            return result;
        }

        /// <summary>Ne plus ignorer un invitant.</summary>
        public bool Unignore(string name)
        {
            bool removed;
            lock (sync) removed = !string.IsNullOrEmpty(name) && ignored.Remove(name);
            if (removed) RaiseChanged();
            return removed;
        }

        /// <summary><c>PR</c> envoyé par l'invitant : annule l'invitation en attente (bouton « Annuler » du client).</summary>
        public async Task<InteractionResult> CancelInvitationAsync()
        {
            string invitee = OutgoingInvitee;
            if (invitee == null) return Refuse("Aucune invitation envoyée en attente.");
            InteractionResult result = await SendAsync("PR", "Invitation de " + invitee + " annulée.").ConfigureAwait(false);
            if (result.Sent) { lock (sync) if (outgoingInvitee == invitee) outgoingInvitee = null; RaiseChanged(); }
            return result;
        }

        /// <summary><c>PV</c> : quitter le groupe.</summary>
        public Task<InteractionResult> LeaveAsync()
        {
            if (!Group.IsActive) return Refused(NotInParty);
            return SendAsync("PV", "Départ du groupe demandé.");
        }

        /// <summary><c>PV&lt;id&gt;</c> : exclure un membre (chef seulement, comme le menu du client et <c>Party.isChief</c> de StarLoco).</summary>
        public Task<InteractionResult> KickAsync(long memberId)
        {
            InteractionResult refused = CheckMember(memberId, false, true);
            if (refused != null) return Task.FromResult(refused);
            return SendAsync("PV" + memberId.ToString(CultureInfo.InvariantCulture), "Exclusion de " + Group.Find(memberId)?.Name + " demandée.");
        }

        /// <summary><c>PF+&lt;id&gt;</c> : suivre un membre ; le serveur répond <c>IC&lt;x&gt;|&lt;y&gt;</c> puis <c>PF+&lt;id&gt;</c>.</summary>
        public Task<InteractionResult> FollowAsync(long memberId)
        {
            InteractionResult refused = CheckMember(memberId, false, false);
            if (refused != null) return Task.FromResult(refused);
            return SendAsync("PF+" + memberId.ToString(CultureInfo.InvariantCulture), "Suivi de " + Group.Find(memberId)?.Name + " demandé.");
        }

        /// <summary><c>PF-&lt;id&gt;</c> : ne plus suivre ce membre ; le serveur répond <c>IC|</c> puis <c>PF-</c>.</summary>
        public Task<InteractionResult> StopFollowingAsync(long memberId)
        {
            InteractionResult refused = CheckMember(memberId, false, false);
            if (refused != null) return Task.FromResult(refused);
            return SendAsync("PF-" + memberId.ToString(CultureInfo.InvariantCulture), "Fin du suivi de " + Group.Find(memberId)?.Name + " demandée.");
        }

        /// <summary><c>PG+&lt;id&gt;</c> : tout le groupe suit ce membre (chef seulement, comme le menu du client).</summary>
        public async Task<InteractionResult> FollowAllAsync(long memberId)
        {
            InteractionResult refused = CheckMember(memberId, true, true);
            if (refused != null) return refused;
            InteractionResult result = await SendAsync("PG+" + memberId.ToString(CultureInfo.InvariantCulture),
                "Le groupe est invité à suivre " + Group.Find(memberId)?.Name + ".").ConfigureAwait(false);
            if (result.Sent) { lock (sync) followAllTarget = memberId; RaiseChanged(); }
            return result;
        }

        /// <summary><c>PG-&lt;id&gt;</c> : plus personne ne suit ce membre (chef seulement).</summary>
        public async Task<InteractionResult> StopFollowAllAsync(long memberId)
        {
            InteractionResult refused = CheckMember(memberId, true, true);
            if (refused != null) return refused;
            InteractionResult result = await SendAsync("PG-" + memberId.ToString(CultureInfo.InvariantCulture),
                "Le groupe cesse de suivre " + Group.Find(memberId)?.Name + ".").ConfigureAwait(false);
            if (result.Sent) { lock (sync) if (followAllTarget == memberId) followAllTarget = null; RaiseChanged(); }
            return result;
        }

        /// <summary><c>PW</c> : localiser les membres ; le serveur répond <c>IH&lt;x;y;carte;2;id;nom&gt;|…</c>.</summary>
        public Task<InteractionResult> LocateAsync()
        {
            if (!Group.IsActive) return Refused(NotInParty);
            return SendAsync("PW", "Localisation des membres du groupe demandée.");
        }

        // ----- Réceptions (PartyFrame, fil réseau) -----

        /// <summary><c>PIK&lt;invitant&gt;|&lt;invité&gt;</c>, envoyé par StarLoco à l'invitant et à l'invité (<c>Party.onInvite</c>).</summary>
        internal Task OnInvitePacket(string message)
        {
            string[] names = message.Length > 3 ? message.Substring(3).Split('|') : new string[0];
            if (names.Length < 2 || names[0].Length == 0 || names[1].Length == 0)
            {
                // Comme le client : invitation incomplète → refus.
                Warn("Invitation de groupe incomplète : refusée.");
                return SendRawAsync("PR");
            }
            string inviter = names[0], target = names[1];
            string self = account?.Game?.character?.Name;
            if (string.IsNullOrEmpty(self)) return Task.CompletedTask;
            if (string.Equals(inviter, self, StringComparison.Ordinal))
            {
                lock (sync) outgoingInvitee = target;
                Inform(PartyTexts.Get("YOU_INVITE_B_IN_PARTY", "Invitation envoyée à {0} : en attente de sa réponse.", target));
                return Task.CompletedTask;
            }
            // L'invité est le nom tapé par l'invitant (StarLoco le retrouve sans tenir compte de la casse) : même règle ici.
            if (!string.Equals(target, self, StringComparison.OrdinalIgnoreCase)) return Task.CompletedTask;
            if (IsIgnored(inviter))
            {
                account?.Logger?.LogInfo(Reference, "Invitation de groupe de " + inviter + " refusée : joueur ignoré pour la session.");
                return SendRawAsync("PR");
            }
            lock (sync) pendingInviter = inviter;
            Inform(PartyTexts.Get("CHAT_A_INVITE_YOU_IN_PARTY", "{0} vous invite dans son groupe.", inviter));
            if (account?.Regroupement?.ShouldAutoAccept(account, inviter) == true)
            {
                account.Logger?.LogInfo(Reference, "Invitation de " + inviter + " acceptée automatiquement : chef de l'équipe de comptes du bot.");
                return AcceptAsync();
            }
            GameSession session = account?.Game?.Session;
            if (session != null && session.OnPartyInvite(new InvitationEventArgs(inviter, string.Empty, target, string.Empty))) return Task.CompletedTask;
            account?.Logger?.LogInfo(Reference, "Invitation de groupe de " + inviter + " refusée : le bot n'accepte pas d'invitation sans réponse de l'utilisateur.");
            lock (sync) if (pendingInviter == inviter) pendingInviter = null;
            RaiseChanged();
            return SendRawAsync("PR");
        }

        /// <summary><c>PIE&lt;n|a|f&gt;[nom]</c> : invitation impossible (<c>Party.onInvite(false, …)</c>).</summary>
        internal void OnInviteErrorPacket(string message)
        {
            string body = message.Length > 3 ? message.Substring(3) : string.Empty;
            lock (sync) outgoingInvitee = null;
            char code = body.Length > 0 ? body[0] : '\0';
            string name = body.Length > 1 ? body.Substring(1) : string.Empty;
            if (code == 'a') Fail(PartyTexts.Get("PARTY_ALREADY_IN_GROUP", "Ce joueur appartient déjà à un groupe."));
            else if (code == 'f') Fail(PartyTexts.Get("PARTY_FULL", "Le groupe est complet ({0} membres).", Groupe.MaxMembers.ToString(CultureInfo.InvariantCulture)));
            else Fail(PartyTexts.Get("CANT_FIND_ACCOUNT_OR_CHARACTER", "{0} est introuvable ou n'est pas connecté.", name));
            RaiseInvitationClosed();
        }

        /// <summary><c>PCK&lt;chef&gt;</c> : le personnage entre dans un groupe (<c>Party.onCreate</c>).</summary>
        internal void OnCreatePacket(string message)
        {
            string leader = message.Length > 3 ? message.Substring(3) : string.Empty;
            lock (sync) { pendingInviter = null; outgoingInvitee = null; }
            Group.Create(leader);
            SyncLegacy();
            string self = account?.Game?.character?.Name;
            if (!string.Equals(leader, self, StringComparison.Ordinal))
                Inform(PartyTexts.Get("U_ARE_IN_GROUP", "Vous avez rejoint le groupe de {0}.", leader));
            else RaiseChanged();
            RaiseInvitationClosed();
        }

        /// <summary><c>PCE&lt;a|f&gt;</c> : création impossible (StarLoco ne l'envoie pas ; lu comme le client).</summary>
        internal void OnCreateErrorPacket(string message)
        {
            char code = message.Length > 3 ? message[3] : '\0';
            Fail(code == 'a' ? PartyTexts.Get("PARTY_ALREADY_IN_GROUP", "Ce joueur appartient déjà à un groupe.")
                : PartyTexts.Get("PARTY_FULL", "Le groupe est complet ({0} membres).", Groupe.MaxMembers.ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary><c>PL&lt;id&gt;</c> : chef du groupe (<c>Party.onLeader</c>, message seulement si le membre est déjà connu).</summary>
        internal void OnLeaderPacket(string message)
        {
            if (!TryId(message.Length > 2 ? message.Substring(2) : string.Empty, out long id))
            {
                Debug("Chef de groupe illisible : " + Preview(message));
                return;
            }
            string name = Group.Find(id)?.Name;
            Group.SetLeader(id);
            SyncLegacy();
            if (!string.IsNullOrEmpty(name)) Inform(PartyTexts.Get("NEW_GROUP_LEADER", "{0} dirige désormais le groupe.", name));
        }

        /// <summary><c>PM+…|…</c>, <c>PM~…</c>, <c>PM-&lt;id&gt;|…</c> : l'opérateur (premier caractère) vaut pour toutes les entrées, comme <c>Party.onMovement</c>.</summary>
        internal void OnMembersPacket(string message)
        {
            string body = message.Length > 2 ? message.Substring(2) : string.Empty;
            if (body.Length == 0) { Debug("Paquet PM vide ignoré."); return; }
            char operation = body[0];
            string[] entries = body.Substring(1).Split('|');
            if (operation == '+' || operation == '~')
            {
                var parsed = new List<PartyMember>();
                foreach (string entry in entries)
                {
                    if (entry.Length == 0) continue;
                    if (PartyMember.TryParse(entry, out PartyMember member, out string error)) parsed.Add(member);
                    else Debug("Membre de groupe ignoré (" + error + ").");
                }
                if (parsed.Count > 0) Group.Upsert(parsed, operation == '+');
            }
            else if (operation == '-')
            {
                foreach (string entry in entries)
                {
                    if (!TryId(entry, out long id)) { if (entry.Length > 0) Debug("Retrait de membre illisible : " + Preview(entry)); continue; }
                    Group.Remove(id, out bool wasFollowed);
                    if (wasFollowed) SetCompass(null);
                    lock (sync) if (followAllTarget == id) followAllTarget = null;
                }
            }
            else { Debug("Opération de groupe inconnue : " + Preview(message)); return; }
            SyncLegacy();
        }

        /// <summary><c>PV[&lt;id de l'exclueur&gt;]</c> : le personnage n'est plus dans le groupe (<c>Party.onLeave</c>).</summary>
        internal void OnLeavePacket(string message)
        {
            string body = message.Length > 2 ? message.Substring(2) : string.Empty;
            string kicker = TryId(body, out long id) ? Group.Find(id)?.Name : null;
            bool wasFollowing = Group.FollowedId != null;
            Group.Leave();
            PartyLocation[] remaining;
            lock (sync)
            {
                pendingInviter = null; outgoingInvitee = null; followAllTarget = null;
                remaining = locations.Where(location => !location.IsPartyMember).ToArray();
                bool changed = remaining.Length != locations.Length;
                locations = remaining;
                if (!changed) remaining = null;
            }
            if (remaining != null) RaiseLocations(remaining);
            if (wasFollowing) SetCompass(null);
            SyncLegacy();
            Inform(!string.IsNullOrEmpty(kicker)
                ? PartyTexts.Get("A_KICK_FROM_PARTY", "{0} vous a exclu du groupe.", kicker)
                : PartyTexts.Get("LEAVE_GROUP", "Vous ne faites plus partie d'un groupe."));
            RaiseInvitationClosed();
        }

        /// <summary><c>PR</c> / <c>PA</c> : l'invitation en cours est close (réponse donnée, annulée ou acceptée par l'invité).</summary>
        internal void OnInvitationAnsweredPacket(string message)
        {
            string invitee, inviter;
            lock (sync) { invitee = outgoingInvitee; inviter = pendingInviter; outgoingInvitee = null; pendingInviter = null; }
            if (invitee != null) account?.Logger?.LogInfo(Reference, "Invitation de " + invitee + " close sans entrée dans le groupe.");
            if (inviter != null) account?.Logger?.LogInfo(Reference, "Invitation de " + inviter + " annulée par le serveur.");
            RaiseChanged();
            RaiseInvitationClosed();
        }

        /// <summary><c>PF+&lt;id&gt;</c> (suivi), <c>PF-</c> (fin du suivi), <c>PFE</c> (refus) : <c>Party.onFollow</c>.</summary>
        internal void OnFollowPacket(string message)
        {
            if (message.Length > 2 && message[2] == 'E')
            {
                Fail(PartyTexts.Get("PARTY_NOT_IN_IN_GROUP", "Ce joueur n'est pas membre du groupe."));
                return;
            }
            string value = message.Length > 3 ? message.Substring(3) : string.Empty;
            if (value.Length == 0) { Group.SetFollowed(null); return; }
            if (!TryId(value, out long id)) { Debug("Suivi de membre illisible : " + Preview(message)); return; }
            Group.SetFollowed(id);
        }

        /// <summary><c>IC&lt;x&gt;|&lt;y&gt;</c> : cible de la boussole ; <c>IC|</c> (valeurs non numériques) l'efface (<c>Infos.onInfoCompass</c>).</summary>
        internal void OnCompassPacket(string message)
        {
            string[] parts = (message.Length > 2 ? message.Substring(2) : string.Empty).Split('|');
            int y = 0;
            bool hasX = int.TryParse(parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int x);
            bool hasY = parts.Length > 1 && int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out y);
            if (hasX && hasY) SetCompass(new Point(x, y));
            else
            {
                if (hasX || hasY) Debug("Boussole incomplète effacée : " + Preview(message));
                SetCompass(null);
            }
        }

        /// <summary><c>IH…</c> : positions à mettre en évidence ; vide = effacement (<c>Infos.onInfoCoordinatespHighlight</c>).</summary>
        internal void OnHighlightPacket(string message)
        {
            string body = message.Length > 2 ? message.Substring(2) : string.Empty;
            var parsed = new List<PartyLocation>();
            if (body.Length > 0)
                foreach (string entry in body.Split('|'))
                {
                    if (PartyLocation.TryParse(entry, out PartyLocation location)) parsed.Add(location);
                    else if (entry.Length > 0) Debug("Position IH ignorée : " + Preview(entry));
                }
            PartyLocation[] snapshot = parsed.ToArray();
            lock (sync) locations = snapshot;
            RaiseLocations(snapshot);
            RaiseChanged();
        }

        /// <summary>Oublie l'état de session (déconnexion, changement de personnage) sans rien envoyer.</summary>
        public void Clear()
        {
            Group.Leave();
            bool hadCompass;
            lock (sync)
            {
                pendingInviter = null; outgoingInvitee = null; followAllTarget = null;
                ignored.Clear();
                locations = new PartyLocation[0];
                hadCompass = compass != null;
                compass = null;
                lastMessage = string.Empty;
            }
            SyncLegacy();
            if (hadCompass) RaiseCompass(null);
            RaiseChanged();
            RaiseInvitationClosed();
        }

        // ----- Outils -----

        private const string NotInParty = "Le personnage ne fait partie d'aucun groupe.";

        private InteractionResult CheckMember(long memberId, bool allowSelf, bool leaderOnly)
        {
            if (!Group.IsActive) return Refuse(NotInParty);
            PartyMember member = Group.Find(memberId);
            if (member == null) return Refuse(PartyTexts.Get("PARTY_NOT_IN_IN_GROUP", "Ce joueur n'est pas membre du groupe."));
            long self = account?.Game?.character?.id ?? 0;
            if (!allowSelf && memberId == self) return Refuse("Action impossible sur son propre personnage.");
            if (leaderOnly && !Group.IsLeader) return Refuse("Seul le chef du groupe peut faire cette action.");
            return null;
        }

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

        private Task SendRawAsync(string packet)
        {
            var connection = account?.Connexion;
            return connection == null ? Task.CompletedTask : connection.SendPacket(packet);
        }

        private InteractionResult Refuse(string message) { SetMessage(message); return new InteractionResult(false, message); }
        private Task<InteractionResult> Refused(string message) => Task.FromResult(Refuse(message));

        private void ClearPending(string inviter)
        {
            lock (sync) if (pendingInviter == inviter) pendingInviter = null;
            RaiseChanged();
        }

        private void SetMessage(string message) { lock (sync) lastMessage = message ?? string.Empty; }

        /// <summary>Message d'information du groupe : journal, ligne locale du chat (<c>INFO_CHAT</c> du client) et <see cref="LastMessage"/>.</summary>
        private void Inform(string text)
        {
            SetMessage(text);
            account?.Logger?.LogInfo(Reference, text);
            try { account?.Game?.Chat?.AddLocal(ChatMessageKind.Info, text); }
            catch (Exception error) { account?.Logger?.LogException(Reference, error); }
            RaiseChanged();
        }

        /// <summary>Erreur du groupe (<c>ERROR_CHAT</c> du client).</summary>
        private void Fail(string text)
        {
            SetMessage(text);
            account?.Logger?.LogError(Reference, text);
            try { account?.Game?.Chat?.AddLocal(ChatMessageKind.Error, text); }
            catch (Exception error) { account?.Logger?.LogException(Reference, error); }
            RaiseChanged();
        }

        private void Warn(string text) { SetMessage(text); account?.Logger?.LogDanger(Reference, text); }
        private void Debug(string text) => account?.Logger?.LogDebug(Reference, text);

        private void SetCompass(Point? value)
        {
            bool changed;
            lock (sync) { changed = compass != value; compass = value; }
            if (changed) RaiseCompass(value);
        }

        /// <summary>Recopie le groupe dans les champs historiques du personnage (<c>InGroupe</c>, lu par le canal <c>/p</c> du chat).</summary>
        private void SyncLegacy()
        {
            var character = account?.Game?.character;
            if (character == null) return;
            IReadOnlyList<PartyMember> members = Group.Members;
            bool active = Group.IsActive;
            character.InGroupe = active;
            character.EquipLeader = active ? Group.LeaderName : string.Empty;
            foreach (int id in character.GroupMembers.Keys.ToArray())
                if (!members.Any(member => unchecked((int)member.Id) == id)) character.GroupMembers.TryRemove(id, out _);
            foreach (string name in character.InEquip.Keys.ToArray())
                if (!members.Any(member => member.Name == name)) character.InEquip.TryRemove(name, out _);
            foreach (PartyMember member in members)
            {
                character.GroupMembers[unchecked((int)member.Id)] = member.Name;
                character.InEquip.TryAdd(member.Name, false);
            }
        }

        private static bool TryId(string text, out long id) =>
            long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out id);

        private static string Preview(string text) => text == null ? string.Empty : text.Length > 80 ? text.Substring(0, 80) + "…" : text;

        private void RaiseChanged() => Raise(Changed, handler => handler());
        private void RaiseInvitationClosed() => Raise(InvitationClosed, handler => handler());
        private void RaiseCompass(Point? value) => Raise(CompassChanged, handler => handler(value));
        private void RaiseLocations(IReadOnlyList<PartyLocation> value) => Raise(LocationsReceived, handler => handler(value));

        private void Raise<T>(T subscribers, Action<T> invoke) where T : class
        {
            var multicast = subscribers as Delegate;
            if (multicast == null) return;
            foreach (Delegate subscriber in multicast.GetInvocationList())
            {
                try { invoke((T)(object)subscriber); }
                catch (Exception error)
                {
                    try { account?.Logger?.LogError(Reference, "Un abonné du groupe a échoué : " + error.Message); }
                    catch (Exception) { /* Journal fermé : la lecture des paquets continue. */ }
                }
            }
        }
    }
}
