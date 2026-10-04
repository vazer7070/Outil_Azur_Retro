using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;

namespace Tool_BotProtocol.Game.Groupes
{
    /// <summary>
    /// Équipe de comptes du bot (multi-compte) : un compte chef et jusqu'à <see cref="MaxMembers"/> comptes membres ouverts
    /// dans le même processus. Elle remplace l'ancienne acceptation automatique de toute invitation (<c>PIK</c>) : un membre
    /// n'accepte seul (<c>PA</c>) que l'invitation dont l'invitant est le personnage actuel du chef ; toute autre invitation
    /// suit le chemin normal (boîte de dialogue, sinon refus <c>PR</c>). Le chef peut inviter les membres connectés
    /// (<c>PI&lt;nom&gt;</c>, un envoi par membre, espacés). Aucun compte n'appartient à une équipe sans appel explicite.
    /// </summary>
    public sealed class Regroupement : IDisposable
    {
        /// <summary>Membres en plus du chef : le groupe du jeu compte au plus <see cref="Groupe.MaxMembers"/> personnages.</summary>
        public const int MaxMembers = Groupe.MaxMembers - 1;
        /// <summary>Délai par défaut entre deux invitations du chef (l'ancien code espaçait déjà ses envois d'environ une seconde).</summary>
        public static readonly TimeSpan DefaultInviteDelay = TimeSpan.FromSeconds(1);

        private readonly object sync = new object();
        private readonly List<Accounts.Accounts> members = new List<Accounts.Accounts>();
        private bool disposed;

        /// <summary>Crée l'équipe et la rattache au compte chef (<see cref="Accounts.Accounts.Regroupement"/>).</summary>
        public Regroupement(Accounts.Accounts leader)
        {
            Leader = leader ?? throw new ArgumentNullException(nameof(leader));
            if (leader.Regroupement != null && !ReferenceEquals(leader.Regroupement, this))
                throw new InvalidOperationException("Ce compte appartient déjà à une équipe du bot.");
            leader.Regroupement = this;
        }

        public Accounts.Accounts Leader { get; }
        /// <summary>Comptes membres, sans le chef, dans l'ordre d'ajout.</summary>
        public IReadOnlyList<Accounts.Accounts> Members { get { lock (sync) return members.ToArray(); } }

        public bool IsLeader(Accounts.Accounts account) => account != null && ReferenceEquals(account, Leader);
        public bool Contains(Accounts.Accounts account)
        {
            if (account == null) return false;
            if (IsLeader(account)) return true;
            lock (sync) return members.Contains(account);
        }

        /// <summary>Ajoute un compte membre ; refusé s'il est le chef, déjà dans une équipe, ou si l'équipe est complète.</summary>
        public bool Add(Accounts.Accounts member)
        {
            if (member == null || IsLeader(member)) return false;
            lock (sync)
            {
                if (disposed || members.Contains(member) || members.Count >= MaxMembers) return false;
                if (member.Regroupement != null && !ReferenceEquals(member.Regroupement, this)) return false;
                members.Add(member);
                member.Regroupement = this;
                return true;
            }
        }

        public bool Remove(Accounts.Accounts member)
        {
            if (member == null) return false;
            lock (sync)
            {
                if (!members.Remove(member)) return false;
                if (ReferenceEquals(member.Regroupement, this)) member.Regroupement = null;
                return true;
            }
        }

        /// <summary>
        /// Vrai si <paramref name="member"/> doit accepter seul l'invitation de <paramref name="inviterName"/> : membre de l'équipe
        /// (pas le chef) et invitant = nom du personnage actuellement joué par le chef (casse ignorée comme
        /// <c>World.getPlayerByName</c> de StarLoco). Faux dans tous les autres cas, et pour un nom vide.
        /// </summary>
        public bool ShouldAutoAccept(Accounts.Accounts member, string inviterName)
        {
            if (member == null || IsLeader(member) || string.IsNullOrEmpty(inviterName)) return false;
            lock (sync) if (disposed || !members.Contains(member)) return false;
            string leaderName = Leader.Game?.character?.Name;
            return !string.IsNullOrEmpty(leaderName) && string.Equals(leaderName, inviterName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Le chef invite chaque membre connecté dont le personnage est connu et n'est pas déjà dans son groupe :
        /// <c>PI&lt;nom&gt;</c> par membre, <paramref name="delay"/> entre deux envois (<see cref="DefaultInviteDelay"/> par défaut).
        /// Renvoie le nombre d'invitations envoyées. À appeler hors du fil de l'interface et du fil de réception (attente).
        /// </summary>
        public async Task<int> InviteMembersAsync(TimeSpan? delay = null, CancellationToken cancellation = default(CancellationToken))
        {
            PartyActions party = Leader.Game?.Interactions?.Party;
            if (party == null || !IsUsable(Leader)) return 0;
            TimeSpan wait = delay ?? DefaultInviteDelay;
            int sent = 0;
            foreach (Accounts.Accounts member in Members)
            {
                cancellation.ThrowIfCancellationRequested();
                string name = member.Game?.character?.Name;
                if (!IsUsable(member) || string.IsNullOrEmpty(name) || party.Group.Contains(name)) continue;
                if (sent > 0 && wait > TimeSpan.Zero) await Task.Delay(wait, cancellation).ConfigureAwait(false);
                var result = await party.InviteAsync(name).ConfigureAwait(false);
                if (result.Sent) sent++;
            }
            return sent;
        }

        /// <summary>Connecte le chef puis chaque membre (comportement de l'ancienne classe multi-compte).</summary>
        public void ConnectAll()
        {
            Leader.Connect();
            foreach (Accounts.Accounts member in Members) member.Connect();
        }

        public void DisconnectAll()
        {
            foreach (Accounts.Accounts member in Members) member?.Disconnect();
        }

        private static bool IsUsable(Accounts.Accounts account) =>
            account != null && !account.isdisposed && account.Connexion != null && account.Connexion.IsConnected();

        /// <summary>Dissout l'équipe : plus aucun compte n'y est rattaché, plus aucune acceptation automatique.</summary>
        public void Dispose()
        {
            Accounts.Accounts[] detached;
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                detached = members.ToArray();
                members.Clear();
            }
            foreach (Accounts.Accounts member in detached)
                if (ReferenceEquals(member.Regroupement, this)) member.Regroupement = null;
            if (ReferenceEquals(Leader.Regroupement, this)) Leader.Regroupement = null;
        }
    }
}
