using System;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Game.Accounts
{
    /// <summary>Demande de pseudo du serveur Login (<c>AlEr</c>, <c>AlEs</c>) en attente d'une réponse.</summary>
    public sealed class NicknameRequest
    {
        internal NicknameRequest(int attempt, string refused, string error)
        {
            Attempt = attempt;
            Refused = refused;
            Error = error;
        }

        /// <summary>Numéro de la demande depuis le premier <c>AlEr</c> (1 pour la première).</summary>
        public int Attempt { get; }
        /// <summary>Pseudo envoyé que le serveur vient de refuser, ou <c>null</c> pour la première demande.</summary>
        public string Refused { get; }
        /// <summary>Raison du refus à afficher sous la saisie (<c>NICKNAME_ALREADY_USED</c>…), ou <c>null</c>.</summary>
        public string Error { get; }
    }

    /// <summary>
    /// Pseudo d'un compte neuf (lot X3, manque B-05), comme l'interface <c>ChooseNickName</c> du client 1.34. StarLoco répond
    /// <c>AlEr</c> à <c>Af</c> quand le compte n'a pas de pseudo (<c>AccountQueue.sendInformation</c>) et attend alors une ligne brute :
    /// le pseudo lui-même, sans préfixe (<c>Account.setNickName</c> du client). Il renvoie <c>AlEr</c> si le pseudo est le nom du compte,
    /// <c>AlEs</c> s'il contient un caractère interdit ou s'il est pris ; sinon il l'enregistre et reprend la connexion (<c>Af</c>,
    /// <c>Ad&lt;pseudo&gt;</c>, <c>Ac</c>, <c>AH</c>, <c>AlK</c>, <c>AQ</c>). Rien d'autre ne doit partir pendant l'attente : le Login lirait
    /// n'importe quelle ligne comme le pseudo. Seule la phase de connexion est traitée : en jeu, <c>AlEr</c> accompagne la potion de
    /// changement de nom (manque B-07, lot X32), que le bot journalise sans rien envoyer ni se déconnecter.
    /// </summary>
    public sealed class NicknameChoice
    {
        private readonly Accounts account;
        private readonly object sync = new object();
        private NicknameRequest pending;
        private string lastSent;
        private int attempts;

        internal NicknameChoice(Accounts account) { this.account = account; }

        /// <summary>Levé (fil réseau) à chaque demande du serveur : première demande ou refus du pseudo envoyé.</summary>
        public event Action<NicknameRequest> Requested;
        /// <summary>Levé (fil réseau) quand la connexion reprend après un pseudo accepté (<c>Ad</c> ou <c>AlK</c>).</summary>
        public event Action<string> Accepted;

        /// <summary>Demande en cours, ou <c>null</c>.</summary>
        public NicknameRequest Pending { get { lock (sync) return pending; } }
        public bool IsPending => Pending != null;
        /// <summary>Dernier pseudo envoyé au serveur, ou <c>null</c>.</summary>
        public string LastSent { get { lock (sync) return lastSent; } }

        /// <summary>
        /// Envoie le pseudo saisi, en ligne brute. Refus locaux du client, sans paquet : moins de trois caractères, pseudo égal au nom
        /// du compte (<c>NICKNAME_EQUALS_LOGIN</c>, sans distinction de casse) ; le bot refuse aussi un pseudo fait d'espaces et une
        /// ligne qui ne tiendrait pas dans un seul paquet.
        /// </summary>
        public async Task<InteractionResult> ChooseAsync(string nickname)
        {
            if (!IsPending) return new InteractionResult(false, "Le serveur ne demande pas de pseudo.");
            string refusal = Validate(nickname);
            if (refusal != null) return new InteractionResult(false, refusal);
            TcpClient connection = account.Connexion;
            if (connection == null || !connection.IsConnected()) return new InteractionResult(false, "La connexion au serveur est fermée.");
            // Une seule proposition à la fois : le Login lirait une deuxième ligne comme le pseudo suivant.
            lock (sync)
            {
                if (lastSent != null) return new InteractionResult(false, "Pseudo « " + lastSent + " » déjà envoyé : réponse du serveur en attente.");
                lastSent = nickname;
            }
            try { await connection.SendPacket(nickname).ConfigureAwait(false); }
            catch
            {
                lock (sync) if (lastSent == nickname) lastSent = null;
                throw;
            }
            account.Logger?.LogInfo("CONNEXION", "Pseudo « " + nickname + " » envoyé au serveur.");
            return new InteractionResult(true, "Pseudo « " + nickname + " » envoyé : réponse du serveur en attente.");
        }

        /// <summary>
        /// Contrôles locaux de <see cref="ChooseAsync"/>, faits par le client au premier « OK » avant la confirmation : texte du
        /// refus, ou <c>null</c> si le pseudo peut être proposé au serveur (qui garde ses propres règles).
        /// </summary>
        public string Validate(string nickname)
        {
            if (string.IsNullOrWhiteSpace(nickname) || nickname.Length < 3) return ConnectionTexts.NicknameTooShort;
            string login = account.accountConfig?.Account ?? string.Empty;
            if (string.Equals(nickname, login, StringComparison.OrdinalIgnoreCase)) return ConnectionTexts.NicknameEqualsLogin;
            string violation = TcpClient.SinglePacketViolation(nickname);
            return violation == null ? null : "Pseudo refusé : " + violation + ".";
        }

        /// <summary>« Annuler » du client : déconnexion et retour à l'écran de connexion.</summary>
        public void Cancel()
        {
            if (!IsPending) return;
            account.Logger?.LogInfo("CONNEXION", "Choix du pseudo annulé : déconnexion.");
            account.Disconnect();
        }

        /// <summary>
        /// <c>AlEr</c> (première demande, ou pseudo égal au nom du compte) et <c>AlEs</c> (pseudo pris ou interdit). Faux, sans rien
        /// changer, quand personne n'est abonné à <see cref="Requested"/> : la connexion doit alors être refusée.
        /// </summary>
        internal bool OnRequested(bool alreadyUsed)
        {
            if (Requested == null) return false;
            NicknameRequest request;
            lock (sync)
            {
                string refused = lastSent;
                string error = refused == null ? null : alreadyUsed ? ConnectionTexts.NicknameAlreadyUsed : ConnectionTexts.NicknameEqualsLogin;
                request = new NicknameRequest(++attempts, refused, error);
                pending = request;
                lastSent = null;
            }
            account.SetConnectionStatus(ConnectionTexts.NicknameTitle);
            account.Logger?.LogInfo("CONNEXION", request.Refused == null
                ? "Le compte n'a pas encore de pseudo : le serveur en demande un."
                : "Pseudo « " + request.Refused + " » refusé par le serveur : " + request.Error + ".");
            Raise(Requested, request);
            return true;
        }

        /// <summary>La connexion reprend (<c>Ad</c> ou <c>AlK</c>) : la demande en cours est close.</summary>
        internal void OnAccepted(string nickname)
        {
            lock (sync)
            {
                if (pending == null) return;
                pending = null;
                lastSent = null;
            }
            account.Logger?.LogInfo("CONNEXION", "Pseudo accepté par le serveur" + (string.IsNullOrEmpty(nickname) ? "." : " : « " + nickname + " »."));
            Raise(Accepted, nickname ?? string.Empty);
        }

        /// <summary>Déconnexion : plus rien n'est attendu.</summary>
        internal void Reset()
        {
            lock (sync) { pending = null; lastSent = null; attempts = 0; }
        }

        private void Raise<T>(Action<T> handler, T value)
        {
            if (handler == null) return;
            foreach (Action<T> single in handler.GetInvocationList())
            {
                try { single(value); }
                catch (Exception error) { account.Logger?.LogException("CONNEXION", error); }
            }
        }
    }
}
