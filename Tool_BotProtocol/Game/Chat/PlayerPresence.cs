using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Session;

namespace Tool_BotProtocol.Game.Chat
{
    /// <summary>
    /// États « absent » et « invisible » du personnage chez StarLoco (<c>GameClient.chooseState</c>, ligne 934) :
    /// <c>BYA</c> et <c>BYI</c> sont des bascules, envoyées par le client 1.34 seulement pour <c>/away</c> et <c>/invisible</c>
    /// (<c>Basics.away</c>, <c>Basics.invisible</c>). Le serveur répond <c>Im037</c> (absent) ou <c>Im038</c> (présent),
    /// <c>Im050</c> (invisible) ou <c>Im051</c> (visible). Absent, le personnage ne reçoit plus aucun chuchotement (l'expéditeur
    /// lit <c>Im114</c>) ; invisible, seuls ses amis peuvent lui chuchoter. Le bot n'envoie jamais ces paquets de lui-même :
    /// uniquement sur une demande explicite (commande du chat ou volet des commandes). Le serveur remet les deux états à zéro
    /// à la déconnexion (<c>Player.resetVars</c>), donc à chaque entrée en jeu.
    /// </summary>
    public sealed class PlayerPresence
    {
        /// <summary>Délai après lequel une bascule sans réponse du serveur peut être redemandée.</summary>
        public const int PendingTimeoutMilliseconds = 5000;
        public const string AwayPacket = "BYA";
        public const string InvisiblePacket = "BYI";

        /// <summary>Avertissement affiché avant la bascule « absent ».</summary>
        public const string AwayWarning = "StarLoco refuse alors tous les messages privés adressés au personnage (l'expéditeur reçoit Im114) jusqu'à la prochaine bascule ou la déconnexion.";
        /// <summary>Avertissement affiché avant la bascule « invisible ».</summary>
        public const string InvisibleWarning = "StarLoco ne laisse alors que les amis du compte chuchoter au personnage, jusqu'à la prochaine bascule ou la déconnexion.";

        private readonly object sync = new object();
        private readonly Accounts.Accounts account;
        private bool away, invisible;
        private long awaySentAt, invisibleSentAt;

        internal PlayerPresence(Accounts.Accounts owner, GameSession session)
        {
            account = owner;
            if (session != null)
            {
                session.ServerMessageReceived += OnServerMessage;
                session.GameCreated += Clear;
            }
        }

        /// <summary>Dernier état « absent » annoncé par le serveur (faux à l'entrée en jeu).</summary>
        public bool IsAway { get { lock (sync) return away; } }
        /// <summary>Dernier état « invisible » annoncé par le serveur (faux à l'entrée en jeu).</summary>
        public bool IsInvisible { get { lock (sync) return invisible; } }
        /// <summary>Vrai entre l'envoi de <c>BYA</c> et la réponse du serveur (au plus <see cref="PendingTimeoutMilliseconds"/>).</summary>
        public bool IsAwayPending { get { lock (sync) return Pending(awaySentAt); } }
        public bool IsInvisiblePending { get { lock (sync) return Pending(invisibleSentAt); } }

        /// <summary>État modifié (réponse du serveur, envoi ou remise à zéro). Levé sur le fil réseau ou sur le fil appelant.</summary>
        public event Action Changed;

        /// <summary>Bascule « absent » : envoie <c>BYA</c> une seule fois, refusé tant que la bascule précédente attend sa réponse.</summary>
        public Task<ChatResult> ToggleAwayAsync() => ToggleAsync(true);

        /// <summary>Bascule « invisible » : envoie <c>BYI</c> une seule fois, refusé tant que la bascule précédente attend sa réponse.</summary>
        public Task<ChatResult> ToggleInvisibleAsync() => ToggleAsync(false);

        private async Task<ChatResult> ToggleAsync(bool isAway)
        {
            ChatService chat = account?.Game?.Chat;
            if (chat == null) return ChatResult.Refused("Session de jeu indisponible.");
            string packet = isAway ? AwayPacket : InvisiblePacket;
            bool current;
            lock (sync)
            {
                if (Pending(isAway ? awaySentAt : invisibleSentAt))
                    return chat.Refuse((isAway ? "/away" : "/invisible") + " : réponse du serveur attendue, " + packet + " n'est pas renvoyé.", true);
                if (isAway) awaySentAt = Now(); else invisibleSentAt = Now();
                // État avant l'envoi : la réponse Im peut arriver sur le fil réseau avant la fin de l'envoi.
                current = isAway ? away : invisible;
            }
            Raise();
            ChatResult result = await chat.SendPacketsAsync(() => ResetPending(isAway), packet).ConfigureAwait(false);
            if (result.Sent)
            {
                chat.AddLocal(ChatMessageKind.Info, packet + " envoyé : le personnage " + (isAway
                    ? (current ? "ne sera plus absent." : "sera absent. " + AwayWarning)
                    : (current ? "ne sera plus invisible." : "sera invisible. " + InvisibleWarning)));
            }
            else
            {
                ResetPending(isAway);
                Raise();
            }
            return result;
        }

        /// <summary><c>Im037/038/050/051</c> : état confirmé par le serveur.</summary>
        private void OnServerMessage(ServerMessage message)
        {
            if (message == null || message.Kind != ServerMessageKind.Info || !message.NumericId.HasValue) return;
            lock (sync)
            {
                switch (message.NumericId.Value)
                {
                    case 37: away = true; awaySentAt = 0; break;
                    case 38: away = false; awaySentAt = 0; break;
                    case 50: invisible = true; invisibleSentAt = 0; break;
                    case 51: invisible = false; invisibleSentAt = 0; break;
                    default: return;
                }
            }
            Raise();
        }

        /// <summary>Entrée en jeu ou fin de session : StarLoco a remis les deux états à zéro.</summary>
        public void Clear()
        {
            lock (sync)
            {
                away = invisible = false;
                awaySentAt = invisibleSentAt = 0;
            }
            Raise();
        }

        private void ResetPending(bool isAway) { lock (sync) { if (isAway) awaySentAt = 0; else invisibleSentAt = 0; } }

        private static long Now() => Math.Max(1, Stopwatch.GetTimestamp());
        private static bool Pending(long sentAt) => sentAt != 0 && (Stopwatch.GetTimestamp() - sentAt) * 1000L / Stopwatch.Frequency < PendingTimeoutMilliseconds;

        private void Raise()
        {
            Action handlers = Changed;
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                try { handler(); }
                catch (Exception error)
                {
                    try { account?.Logger?.LogError("CHAT", "Un abonné à l'état absent/invisible a échoué : " + error.Message); }
                    catch { /* Journal fermé : le protocole continue. */ }
                }
            }
        }
    }
}
