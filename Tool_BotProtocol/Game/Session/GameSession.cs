using System;
using System.Collections.Generic;
using System.Diagnostics;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Utils.Interfaces;

namespace Tool_BotProtocol.Game.Session
{
    /// <summary>Invitation reçue (groupe ou guilde). Un abonné qui répond lui-même au serveur met <see cref="Handled"/> à vrai.</summary>
    public sealed class InvitationEventArgs : EventArgs
    {
        public InvitationEventArgs(string inviterName, string inviterId, string target, string guildName)
        {
            InviterName = inviterName ?? string.Empty;
            InviterId = inviterId ?? string.Empty;
            Target = target ?? string.Empty;
            GuildName = guildName ?? string.Empty;
        }
        public string InviterName { get; }
        /// <summary>Identifiant de l'invitant (guilde : nécessaire au refus <c>gJE&lt;id&gt;</c>) ; vide pour un groupe.</summary>
        public string InviterId { get; }
        /// <summary>Nom du joueur invité (groupe), vide pour une guilde.</summary>
        public string Target { get; }
        /// <summary>Nom de la guilde (guilde), vide pour un groupe.</summary>
        public string GuildName { get; }
        /// <summary>Faux par défaut : le bot refuse alors poliment (<c>PR</c> ou <c>gJE&lt;id&gt;</c>), il n'accepte jamais seul.</summary>
        public bool Handled { get; set; }
    }

    /// <summary>
    /// État de la session de jeu annoncé par le serveur (<c>GCK</c>, <c>AR</c>, <c>Ac</c>, <c>BT</c>, <c>AN</c>, <c>Im</c>) et
    /// événements des invitations. Les événements sont levés sur le fil de réception réseau : un abonné d'interface
    /// doit repasser sur son propre fil (<c>BeginInvoke</c>). Une exception d'abonné est journalisée, jamais propagée.
    /// </summary>
    public sealed class GameSession : IEliminable
    {
        private readonly Accounts.Accounts account;
        private readonly object sync = new object();
        private long referenceTimestamp;

        internal GameSession(Accounts.Accounts owner) { account = owner; }

        /// <summary>Type de partie de <c>GCK|&lt;type&gt;|&lt;nom&gt;</c> (1 = solo, seul type du client) ; 0 avant <c>GCK</c>.</summary>
        public int GameType { get; private set; }
        public bool IsGameCreated => GameType > 0;
        /// <summary>Nom renvoyé par StarLoco dans <c>GCK</c> (le client ne l'utilise pas).</summary>
        public string GameCharacterName { get; private set; } = string.Empty;
        /// <summary>Valeur brute de <c>AR</c> (base 36 décodée), -1 avant le premier <c>AR</c>.</summary>
        public int RawRestrictions { get; private set; } = -1;
        /// <summary>Restrictions du personnage joué (bits connus du client ; <c>AR6bk</c> = aucune).</summary>
        public PlayerRestrictions Restrictions { get; private set; }
        /// <summary>Communauté annoncée par <c>Ac&lt;id&gt;</c> (le client ignore une valeur négative).</summary>
        public int? CommunityId { get; private set; }
        /// <summary>Horloge de <c>BT&lt;ms&gt;</c> : millisecondes envoyées par le serveur (StarLoco ajoute 2 h à l'heure Unix).</summary>
        public long? ServerReferenceTime { get; private set; }
        /// <summary>Dernier message <c>Im</c> reçu.</summary>
        public ServerMessage LastServerMessage { get; private set; }

        public event Action GameCreated;
        public event Action<int> LevelUp;
        public event Action<ServerMessage> ServerMessageReceived;
        public event EventHandler<InvitationEventArgs> PartyInviteReceived;
        public event EventHandler<InvitationEventArgs> GuildInviteReceived;

        /// <summary>Heure du serveur estimée : référence <c>BT</c> + temps écoulé localement depuis sa réception.</summary>
        public DateTime? EstimatedServerTime
        {
            get
            {
                lock (sync)
                {
                    if (!ServerReferenceTime.HasValue) return null;
                    long elapsed = (Stopwatch.GetTimestamp() - referenceTimestamp) * 1000L / Stopwatch.Frequency;
                    try { return DateTimeOffset.FromUnixTimeMilliseconds(ServerReferenceTime.Value + elapsed).UtcDateTime; }
                    catch (ArgumentOutOfRangeException) { return null; }
                }
            }
        }

        internal void OnGameCreated(int type, string name)
        {
            GameType = type;
            GameCharacterName = name ?? string.Empty;
            Raise(GameCreated, handler => handler());
        }

        internal void OnRestrictions(int raw, PlayerRestrictions restrictions)
        {
            RawRestrictions = raw;
            Restrictions = restrictions;
        }

        internal void OnCommunity(int community) { if (community >= 0) CommunityId = community; }

        internal void OnServerTime(long milliseconds)
        {
            lock (sync)
            {
                ServerReferenceTime = milliseconds;
                referenceTimestamp = Stopwatch.GetTimestamp();
            }
        }

        internal void OnLevelUp(int level) => Raise(LevelUp, handler => handler(level));

        internal void OnServerMessages(IEnumerable<ServerMessage> messages)
        {
            foreach (ServerMessage message in messages)
            {
                LastServerMessage = message;
                Raise(ServerMessageReceived, handler => handler(message));
            }
        }

        /// <summary>Lève l'invitation de groupe ; renvoie vrai si un abonné s'en charge.</summary>
        internal bool OnPartyInvite(InvitationEventArgs invitation)
        {
            Raise(PartyInviteReceived, handler => handler(this, invitation));
            return invitation.Handled;
        }

        /// <summary>Lève l'invitation de guilde ; renvoie vrai si un abonné s'en charge.</summary>
        internal bool OnGuildInvite(InvitationEventArgs invitation)
        {
            Raise(GuildInviteReceived, handler => handler(this, invitation));
            return invitation.Handled;
        }

        private void Raise<T>(T subscribers, Action<T> invoke) where T : class
        {
            var multicast = subscribers as Delegate;
            if (multicast == null) return;
            foreach (Delegate subscriber in multicast.GetInvocationList())
            {
                try { invoke((T)(object)subscriber); }
                catch (Exception error)
                {
                    try { account?.Logger?.LogError("SESSION", "Un abonné aux événements de session a échoué : " + error.Message); }
                    catch { /* Journal fermé : le protocole continue. */ }
                }
            }
        }

        public void Clear()
        {
            lock (sync)
            {
                GameType = 0;
                GameCharacterName = string.Empty;
                RawRestrictions = -1;
                Restrictions = PlayerRestrictions.None;
                CommunityId = null;
                ServerReferenceTime = null;
                referenceTimestamp = 0;
                LastServerMessage = null;
            }
        }
    }
}
