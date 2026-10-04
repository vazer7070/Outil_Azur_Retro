using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Tool_BotProtocol.Game.Groupes
{
    /// <summary>
    /// Membre du groupe tel que StarLoco l'annonce dans <c>PM+</c> / <c>PM~</c> (<c>Player.parseToPM</c>) et que le client 1.34
    /// le lit (<c>Party.onMovement</c>) : <c>id;nom;gfx;couleur1;couleur2;couleur3;accessoires;pdv,pdvMax;niveau;initiative;prospection;côté</c>.
    /// Les champs absents prennent leur valeur par défaut (le client lirait <c>NaN</c>) : un membre incomplet reste affiché.
    /// </summary>
    public sealed class PartyMember
    {
        public long Id { get; private set; }
        public string Name { get; private set; } = string.Empty;
        /// <summary>Sprite du personnage (<c>clips/sprites/&lt;gfx&gt;.swf</c> dans le client) ; 0 si absent.</summary>
        public int Gfx { get; private set; }
        /// <summary>Couleurs décimales du sprite (-1 = couleur d'origine), non appliquées par le bot (sprites PNG statiques).</summary>
        public int Color1 { get; private set; } = -1;
        public int Color2 { get; private set; } = -1;
        public int Color3 { get; private set; } = -1;
        /// <summary>Champ brut des accessoires (même format que dans <c>GM</c>).</summary>
        public string Accessories { get; private set; } = string.Empty;
        public int Life { get; private set; }
        public int MaxLife { get; private set; }
        public int Level { get; private set; }
        public int Initiative { get; private set; }
        public int Prospection { get; private set; }
        /// <summary>Dernier champ (« side ») : StarLoco envoie toujours 0.</summary>
        public int Side { get; private set; }

        /// <summary>Classe déduite du gfx de base d'un personnage (classe × 10 + sexe), 0 pour un autre gfx.</summary>
        public int Breed => Gfx >= 10 && Gfx <= 129 ? Gfx / 10 : 0;
        /// <summary>Part des points de vie (0 à 1) ; 0 si le maximum est inconnu.</summary>
        public double LifeRatio => MaxLife <= 0 ? 0 : Math.Max(0, Math.Min(1, Life / (double)MaxLife));

        private PartyMember() { }

        /// <summary>
        /// Lit une entrée de <c>PM</c> (sans l'opérateur). Seul l'identifiant est obligatoire, comme pour le client qui retrouve
        /// le membre par <c>id</c> ; renvoie <c>false</c> (et la raison) s'il manque ou n'est pas un nombre. Ne lève jamais d'exception.
        /// </summary>
        public static bool TryParse(string entry, out PartyMember member, out string error)
        {
            member = null; error = null;
            string[] fields = (entry ?? string.Empty).Split(';');
            if (!long.TryParse(fields[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long id))
            {
                error = "identifiant de membre illisible « " + fields[0] + " »";
                return false;
            }
            string Field(int index) => index < fields.Length ? fields[index] : string.Empty;
            int Number(int index, int fallback) =>
                int.TryParse(Field(index), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) ? value : fallback;
            string[] life = Field(7).Split(',');
            member = new PartyMember
            {
                Id = id,
                Name = Field(1),
                Gfx = Number(2, 0),
                Color1 = Number(3, -1),
                Color2 = Number(4, -1),
                Color3 = Number(5, -1),
                Accessories = Field(6),
                Life = int.TryParse(life[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int current) ? current : 0,
                MaxLife = life.Length > 1 && int.TryParse(life[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int maximum) ? maximum : 0,
                Level = Number(8, 0),
                Initiative = Number(9, 0),
                Prospection = Number(10, 0),
                Side = Number(11, 0),
            };
            return true;
        }

        public override string ToString() => Name + " (" + Id.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// Groupe du personnage tel que le serveur l'annonce : création <c>PCK&lt;chef&gt;</c>, chef <c>PL&lt;id&gt;</c>, membres
    /// <c>PM+</c>/<c>PM~</c>/<c>PM-</c>, suivi <c>PF</c>, départ <c>PV</c>. Une instance par compte
    /// (<c>Game.Interactions.Party.Group</c>), remplie sur le fil de réception et lue par l'interface : chaque lecture
    /// renvoie un instantané, <see cref="Changed"/> est levé hors verrou sur le fil réseau.
    /// Les membres sont triés par initiative décroissante, comme le volet <c>Party</c> du client.
    /// </summary>
    public sealed class Groupe
    {
        /// <summary>Taille maximale d'un groupe (<c>MEMBERS_COUNT_IN_PARTY</c> du client, refus <c>PIEf</c> de StarLoco à 8).</summary>
        public const int MaxMembers = 8;

        private readonly object sync = new object();
        private readonly Func<long> selfId;
        private readonly Action<Exception> onSubscriberError;
        private List<PartyMember> members = new List<PartyMember>();
        private bool active;
        private string leaderName = string.Empty;
        private long? leaderId, followedId;

        /// <param name="selfIdentifier">Identifiant du personnage du compte (pour <see cref="IsLeader"/>).</param>
        /// <param name="subscriberError">Journal des exceptions levées par un abonné de <see cref="Changed"/> (facultatif).</param>
        public Groupe(Func<long> selfIdentifier, Action<Exception> subscriberError = null)
        {
            selfId = selfIdentifier ?? (() => 0);
            onSubscriberError = subscriberError;
        }

        /// <summary>Levé après chaque changement (fil de réception réseau).</summary>
        public event Action Changed;

        /// <summary>Vrai entre <c>PCK</c> et <c>PV</c>.</summary>
        public bool IsActive { get { lock (sync) return active; } }
        /// <summary>Membres, chef compris, par initiative décroissante.</summary>
        public IReadOnlyList<PartyMember> Members { get { lock (sync) return members.ToArray(); } }
        public int Count { get { lock (sync) return members.Count; } }
        public bool IsFull { get { lock (sync) return members.Count >= MaxMembers; } }
        /// <summary>Nom du chef annoncé par <c>PCK</c>.</summary>
        public string LeaderName { get { lock (sync) return leaderName; } }
        /// <summary>Identifiant du chef annoncé par <c>PL</c>, ou <c>null</c>.</summary>
        public long? LeaderId { get { lock (sync) return leaderId; } }
        public PartyMember Leader { get { lock (sync) return leaderId == null ? null : members.FirstOrDefault(m => m.Id == leaderId.Value); } }
        /// <summary>Vrai si le personnage du compte est le chef (<c>PL</c> = son identifiant).</summary>
        public bool IsLeader { get { long self = selfId(); lock (sync) return active && leaderId.HasValue && self != 0 && leaderId.Value == self; } }
        /// <summary>Membre suivi par le personnage (<c>PF+&lt;id&gt;</c>), <c>null</c> après <c>PF-</c>.</summary>
        public long? FollowedId { get { lock (sync) return followedId; } }
        /// <summary>Somme des niveaux (infobulle d'information du volet du client).</summary>
        public int TotalLevel { get { lock (sync) return members.Sum(m => m.Level); } }
        /// <summary>Somme des prospections (infobulle d'information du volet du client).</summary>
        public int TotalProspection { get { lock (sync) return members.Sum(m => m.Prospection); } }

        public PartyMember Find(long id) { lock (sync) return members.FirstOrDefault(m => m.Id == id); }
        public PartyMember Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            lock (sync) return members.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
        }
        public bool Contains(long id) => Find(id) != null;
        public bool Contains(string name) => Find(name) != null;

        /// <summary><c>PCK&lt;chef&gt;</c> : nouveau groupe (les membres arrivent ensuite par <c>PL</c> et <c>PM+</c>).</summary>
        internal void Create(string leader)
        {
            lock (sync)
            {
                active = true;
                leaderName = leader ?? string.Empty;
                leaderId = null; followedId = null;
                members = new List<PartyMember>();
            }
            Raise();
        }

        /// <summary><c>PL&lt;id&gt;</c>.</summary>
        internal void SetLeader(long id)
        {
            lock (sync)
            {
                leaderId = id;
                PartyMember leader = members.FirstOrDefault(m => m.Id == id);
                if (leader != null && leader.Name.Length > 0) leaderName = leader.Name;
            }
            Raise();
        }

        /// <summary>
        /// <c>PM+</c> : ajoute les membres (un identifiant déjà connu est remplacé, jamais dupliqué) ;
        /// <c>PM~</c> (<paramref name="addMissing"/> faux) : met à jour les membres connus seulement, comme
        /// <c>Party.updateData</c> du client qui ne remplace que l'entrée de même identifiant.
        /// </summary>
        internal void Upsert(IEnumerable<PartyMember> updated, bool addMissing = true)
        {
            lock (sync)
            {
                var copy = new List<PartyMember>(members);
                foreach (PartyMember member in updated)
                {
                    int index = copy.FindIndex(m => m.Id == member.Id);
                    if (index >= 0) copy[index] = member; else if (addMissing) copy.Add(member);
                }
                // Tri stable par initiative décroissante, comme le tri à bulles de Party.updateData.
                members = copy.Select((m, i) => new { m, i }).OrderByDescending(x => x.m.Initiative).ThenBy(x => x.i).Select(x => x.m).ToList();
                active = true;
                PartyMember leader = leaderId == null ? null : members.FirstOrDefault(m => m.Id == leaderId.Value);
                if (leader != null && leader.Name.Length > 0) leaderName = leader.Name;
            }
            Raise();
        }

        /// <summary><c>PM-&lt;id&gt;</c> : renvoie le membre retiré (ou <c>null</c>) ; le suivi de ce membre s'arrête.</summary>
        internal PartyMember Remove(long id, out bool wasFollowed)
        {
            PartyMember removed;
            lock (sync)
            {
                removed = members.FirstOrDefault(m => m.Id == id);
                wasFollowed = followedId == id;
                if (wasFollowed) followedId = null;
                if (removed != null) members = members.Where(m => m.Id != id).ToList();
            }
            if (removed != null || wasFollowed) Raise();
            return removed;
        }

        /// <summary><c>PF+&lt;id&gt;</c> / <c>PF-</c>.</summary>
        internal void SetFollowed(long? id)
        {
            lock (sync) followedId = id;
            Raise();
        }

        /// <summary><c>PV</c> ou fin de session : plus de groupe.</summary>
        internal void Leave()
        {
            bool changed;
            lock (sync)
            {
                changed = active || members.Count > 0 || leaderId != null || followedId != null;
                active = false;
                leaderName = string.Empty;
                leaderId = null; followedId = null;
                members = new List<PartyMember>();
            }
            if (changed) Raise();
        }

        private void Raise()
        {
            Action handlers = Changed;
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                // Un abonné d'interface ne doit pas interrompre la lecture des paquets : son erreur est seulement journalisée.
                try { handler(); }
                catch (Exception error)
                {
                    try { onSubscriberError?.Invoke(error); }
                    catch (Exception) { /* Journal fermé. */ }
                }
            }
        }
    }
}
