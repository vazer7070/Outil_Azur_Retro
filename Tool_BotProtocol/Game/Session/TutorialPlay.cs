using System;
using System.Globalization;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Game.Session
{
    /// <summary>Issue d'un mini-jeu du serveur, telle que le fichier de tutoriel du client la fixe.</summary>
    public enum TutorialOutcome { Unsupported, Lost, Won }

    /// <summary>Mini-jeu lancé par <c>TC&lt;id&gt;|&lt;clé&gt;</c> et sa fin envoyée par le bot.</summary>
    public sealed class TutorialRequest
    {
        internal TutorialRequest(int id, string key, string name, TutorialOutcome outcome, int rank)
        {
            Id = id;
            Key = key ?? string.Empty;
            Name = name;
            Outcome = outcome;
            Rank = rank;
        }

        /// <summary>Numéro du tutoriel (fichier <c>data/tutorials/&lt;id&gt;_&lt;clé&gt;.swf</c> du client).</summary>
        public int Id { get; }
        public string Key { get; }
        /// <summary>Nom du mini-jeu quand le bot le connaît (table <c>tutoriel</c> de StarLoco), sinon <c>null</c>.</summary>
        public string Name { get; }
        public TutorialOutcome Outcome { get; }
        /// <summary>Paramètre du bloc <c>END</c> du tutoriel, envoyé dans <c>TV</c> (0 = rien, 1 à 3 = récompense de ce rang).</summary>
        public int Rank { get; }
        /// <summary>Paquet <c>TV</c> envoyé pour clore le mini-jeu, ou <c>null</c> tant qu'il ne l'est pas.</summary>
        public string EndPacket { get; internal set; }

        /// <summary>Texte de la boîte d'information du bot.</summary>
        public string Describe()
        {
            string label = (Name ?? "Mini-jeu") + " (tutoriel n° " + Id.ToString(CultureInfo.InvariantCulture) + ")";
            string result = Outcome == TutorialOutcome.Won ? "gagné : le serveur applique la récompense de rang " + Rank.ToString(CultureInfo.InvariantCulture)
                : Outcome == TutorialOutcome.Lost ? "perdu" : "mini-jeu inconnu du bot, abandonné sans récompense";
            return label + " : le bot ne joue pas l'animation du client ; résultat tiré par le serveur : " + result + "."
                + (EndPacket == null ? string.Empty : "\nFin envoyée au serveur : " + EndPacket + ".");
        }
    }

    /// <summary>
    /// Mini-jeux de StarLoco (lot X32, manque V-45). Le serveur n'envoie <c>TC</c> que pour la roulette du Bouftou (PNJ 408, carte
    /// 3336 : réponse « Payer 100 kamas », action 36 « 100;30 » de <c>Action.java</c>) : 100 kamas retirés, <c>Im046;100</c>, puis 1,5 s
    /// plus tard <c>TC30|7001010000</c> (ou <c>TC31</c> une fois sur 200), le joueur passant « absent » et <c>IN_TUTORIAL</c> jusqu'à
    /// son <c>TV</c> (dialogues, objets interactifs et échanges refusés). Le client charge <c>data/tutorials/&lt;id&gt;_&lt;clé&gt;.swf</c>,
    /// joue l'animation, puis <c>TutorialManager.terminate</c> envoie <c>TV&lt;rang&gt;|&lt;cellule&gt;|&lt;orientation&gt;</c> avec la
    /// cellule et l'orientation du joueur ; le rang est le paramètre du bloc <c>END</c> du fichier : 0 pour le tutoriel 30 (perdu),
    /// 1 pour le 31 (gagné). <c>GameClient.parseFoireTroll</c> applique la récompense de ce rang (sauf 0 et 4), l'action de fin, lève
    /// « absent » et replace le joueur. Le bot ne joue pas l'animation : il envoie la fin dès <c>TC</c> avec le rang du fichier, ou 0
    /// pour un tutoriel qu'il ne connaît pas (comme <c>TutorialManager.cancel</c> sans bloc de sortie).
    /// </summary>
    public sealed class TutorialPlay
    {
        private readonly Accounts.Accounts account;
        private readonly object sync = new object();

        internal TutorialPlay(Accounts.Accounts owner) { account = owner; }

        /// <summary>Levé (fil réseau) à chaque <c>TC</c>, avant l'envoi de la fin ; <see cref="TutorialRequest.EndPacket"/> est déjà posé.</summary>
        public event Action<TutorialRequest> Started;

        /// <summary>Dernier mini-jeu lancé par le serveur, ou <c>null</c>.</summary>
        public TutorialRequest Last { get; private set; }

        /// <summary>Tutoriels que StarLoco peut lancer, d'après la table <c>tutoriel</c> du kit et les blocs <c>END</c> de leurs fichiers.</summary>
        public static TutorialRequest Describe(int id, string key)
        {
            switch (id)
            {
                case 30: return new TutorialRequest(id, key, "Roulette du Bouftou", TutorialOutcome.Lost, 0);
                case 31: return new TutorialRequest(id, key, "Roulette du Bouftou", TutorialOutcome.Won, 1);
                default: return new TutorialRequest(id, key, null, TutorialOutcome.Unsupported, 0);
            }
        }

        /// <summary><c>TC&lt;id&gt;|&lt;clé&gt;</c> reçu : la fin est envoyée aussitôt, puis les abonnés prévenus.</summary>
        internal async Task OnStartedAsync(int id, string key)
        {
            TutorialRequest request = Describe(id, key);
            lock (sync) Last = request;
            request.EndPacket = EndPacketFor(request.Rank);
            account.Logger?.LogInfo("SESSION", "Mini-jeu " + (request.Name ?? "inconnu") + " (TC" + id.ToString(CultureInfo.InvariantCulture)
                + ") : " + (request.Outcome == TutorialOutcome.Won ? "gagné" : request.Outcome == TutorialOutcome.Lost ? "perdu" : "non pris en charge")
                + ", fin envoyée : " + request.EndPacket + ".");
            Raise(request);
            InteractionResult sent = await SendEndAsync(request.EndPacket).ConfigureAwait(false);
            if (!sent.Sent) account.Logger?.LogDanger("SESSION", "Fin du mini-jeu non envoyée : " + sent.Message);
        }

        /// <summary>Renvoie la fin d'un mini-jeu avec ce rang (<c>TV&lt;rang&gt;|&lt;cellule&gt;|&lt;orientation&gt;</c>), pour un usage manuel.</summary>
        public Task<InteractionResult> EndAsync(int rank) => SendEndAsync(EndPacketFor(rank));

        /// <summary>
        /// <c>TV&lt;rang&gt;|&lt;cellule&gt;|&lt;orientation&gt;</c> comme <c>Tutorial.end(rang, cellNum, direction)</c> du client ; sans
        /// cellule connue, <c>TV&lt;rang&gt;</c> seul, la forme que le client envoie quand cellule ou orientation manquent.
        /// </summary>
        private string EndPacketFor(int rank)
        {
            if (rank < 0) rank = 0;
            var character = account.Game?.character;
            int cell = character?.Cell?.CellID ?? -1;
            string packet = "TV" + rank.ToString(CultureInfo.InvariantCulture);
            if (cell < 0) return packet;
            return packet + "|" + cell.ToString(CultureInfo.InvariantCulture) + "|" + (character.Orientation).ToString(CultureInfo.InvariantCulture);
        }

        private async Task<InteractionResult> SendEndAsync(string packet)
        {
            TcpClient connection = account.Connexion;
            if (connection == null || !connection.IsConnected()) return new InteractionResult(false, "La connexion au serveur est fermée.");
            try { await connection.SendPacket(packet).ConfigureAwait(false); }
            catch (Exception error) { return new InteractionResult(false, error.Message); }
            return new InteractionResult(true, "Fin du mini-jeu envoyée : " + packet + ".");
        }

        internal void Clear()
        {
            lock (sync) Last = null;
        }

        private void Raise(TutorialRequest request)
        {
            Action<TutorialRequest> handler = Started;
            if (handler == null) return;
            foreach (Action<TutorialRequest> single in handler.GetInvocationList())
            {
                try { single(request); }
                catch (Exception error) { account.Logger?.LogException("SESSION", error); }
            }
        }
    }
}
