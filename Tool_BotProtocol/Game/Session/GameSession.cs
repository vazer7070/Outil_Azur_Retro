using System;
using System.Collections.Generic;
using System.Diagnostics;
using Tool_BotProtocol.Game.Maps.Entities;
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

        internal GameSession(Accounts.Accounts owner)
        {
            account = owner;
            NameChange = new NameChange(owner);
            Tutorial = new TutorialPlay(owner);
        }

        /// <summary>Changement de nom du personnage en jeu après la potion (<c>AlEr</c> / <c>AlEs</c> en jeu, lot X32).</summary>
        public NameChange NameChange { get; }
        /// <summary>Mini-jeux lancés par <c>TC</c> et clos par <c>TV</c> (roulette du Bouftou, lot X32).</summary>
        public TutorialPlay Tutorial { get; }

        /// <summary>Type de partie de <c>GCK|&lt;type&gt;|&lt;nom&gt;</c> (1 = solo, seul type du client) ; 0 avant <c>GCK</c>.</summary>
        public int GameType { get; private set; }
        public bool IsGameCreated => GameType > 0;
        /// <summary>Nom renvoyé par StarLoco dans <c>GCK</c> (le client ne l'utilise pas).</summary>
        public string GameCharacterName { get; private set; } = string.Empty;
        /// <summary>
        /// Valeur brute de <c>AR</c> (base 36 décodée), -1 avant le premier <c>AR</c>. Ses bits sont ceux de
        /// <c>dofus.datacenter.Player</c> (1 = <c>!canAssault</c>, 128 = <c>cantInteractWithTaxCollector</c>,
        /// 8192 = <c>canMoveInAllDirections</c>…), lus par <see cref="Actions.MapActions.HasOwnRestriction"/>.
        /// </summary>
        public int RawRestrictions { get; private set; } = -1;
        /// <summary>
        /// Les neuf premiers bits de <c>AR</c> nommés comme ceux du sprite (<see cref="PlayerRestrictions"/>). Les mêmes bits
        /// n'ont pas le même sens dans <c>AR</c> : <c>AR3K</c> (128) que StarLoco envoie à toute transformation, fantôme
        /// compris, n'est pas une tombe. L'état tombe se lit dans le sprite du personnage (<see cref="IsTomb"/>).
        /// </summary>
        public PlayerRestrictions Restrictions { get; private set; }
        /// <summary>Bit 8192 de <c>AR</c> (<c>Player.canMoveInAllDirections</c>) : huit orientations au lieu des quatre diagonales.</summary>
        public bool CanMoveInAllDirections => RawRestrictions >= 0 && (RawRestrictions & 8192) != 0;
        /// <summary>
        /// Tombe : bit 128 des restrictions du sprite <c>GM</c> du personnage (<c>Player.data.isTomb</c> du client). StarLoco
        /// écrit « -10 » dans ce champ après une défaite à 0 d'énergie (<c>Player.setFuneral</c>) et à chaque connexion d'une
        /// tombe. Faux tant que le personnage n'est pas sur la carte, et en combat.
        /// </summary>
        public bool IsTomb => (SelfSprite?.Restrictions & PlayerRestrictions.Tomb) == PlayerRestrictions.Tomb;
        /// <summary>
        /// Fantôme : bit 32 (ralenti) du sprite, que la commande <c>/release</c> du client lit pour répondre
        /// <c>ERROR_ALREADY_A_GHOST</c> ; StarLoco écrit « -40 » (vitesse du fantôme) après <c>GF</c>.
        /// </summary>
        public bool IsGhost => !IsTomb && (SelfSprite?.Restrictions & PlayerRestrictions.Slow) == PlayerRestrictions.Slow;

        private PlayerActor SelfSprite
        {
            get
            {
                var self = account?.Game?.Map?.Self as PlayerActor;
                return self == null || self.InFight || !self.RestrictionsValue.HasValue ? null : self;
            }
        }
        /// <summary>Communauté annoncée par <c>Ac&lt;id&gt;</c> (le client ignore une valeur négative).</summary>
        public int? CommunityId { get; private set; }
        /// <summary>Horloge de <c>BT&lt;ms&gt;</c> : millisecondes envoyées par le serveur (StarLoco ajoute 2 h à l'heure Unix).</summary>
        public long? ServerReferenceTime { get; private set; }
        /// <summary>Dernier message <c>Im</c> reçu.</summary>
        public ServerMessage LastServerMessage { get; private set; }

        public event Action GameCreated;
        public event Action<int> LevelUp;
        /// <summary>
        /// <c>GO</c> : fin de partie du mode héroïque (<c>Game.onGameOver</c> : déconnexion douce puis écran <c>GameOver</c>).
        /// Levé avant la déconnexion, que <see cref="Accounts.Accounts.Disconnect()"/> fait juste après.
        /// </summary>
        public event Action GameOver;
        public event Action<ServerMessage> ServerMessageReceived;
        /// <summary>Le <c>GM|~</c> du personnage porte un autre nom que celui connu : renommé par le serveur (potion, lot X32).</summary>
        public event Action<string> CharacterRenamed;
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

        internal void OnGameOver() => Raise(GameOver, handler => handler());

        /// <summary>Nom du personnage changé par le serveur (lu dans son sprite <c>GM</c>) : clôt la demande de nom en cours.</summary>
        internal void OnSelfRenamed(string previous, string name)
        {
            account?.Logger?.LogInfo("SESSION", "Le serveur renomme le personnage « " + previous + " » en « " + name + " ».");
            NameChange.OnRenamed(name);
            Raise(CharacterRenamed, handler => handler(name));
        }

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
            NameChange.Reset();
            Tutorial.Clear();
        }
    }
}
