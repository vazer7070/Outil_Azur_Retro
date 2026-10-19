namespace Tool_BotProtocol.Game.Accounts
{
    /// <summary>Demande de pseudo ou de nom du serveur (<c>AlEr</c>, <c>AlEs</c>) en attente d'une réponse.</summary>
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
        /// <summary>Nom envoyé que le serveur vient de refuser ou de redemander, ou <c>null</c> pour la première demande.</summary>
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
    /// n'importe quelle ligne comme le pseudo. Seule la phase de connexion est traitée ici : en jeu, <c>AlEr</c> accompagne la potion
    /// de changement de nom (manque B-07, lot X32, <see cref="Session.NameChange"/>).
    /// </summary>
    public sealed class NicknameChoice : NicknamePrompt
    {
        internal NicknameChoice(Accounts account) : base(account) { }

        protected override string LogCategory => "CONNEXION";
        protected override string Subject => "pseudo";
        protected override string FirstRequestMessage => "Le compte n'a pas encore de pseudo : le serveur en demande un.";
        protected override string AcceptedMessage(string name) =>
            "Pseudo accepté par le serveur" + (string.IsNullOrEmpty(name) ? "." : " : « " + name + " ».");
        /// <summary><c>AlEr</c> après un envoi : pseudo égal au nom du compte ; <c>AlEs</c> : pris ou caractère interdit.</summary>
        protected override string RefusalReason(bool alreadyUsed) => alreadyUsed ? ConnectionTexts.NicknameAlreadyUsed : ConnectionTexts.NicknameEqualsLogin;
        protected override void OnRequestOpened(NicknameRequest request) => Account.SetConnectionStatus(ConnectionTexts.NicknameTitle);

        /// <summary>« Annuler » du client : déconnexion et retour à l'écran de connexion.</summary>
        public override void Cancel()
        {
            if (!IsPending) return;
            Account.Logger?.LogInfo("CONNEXION", "Choix du pseudo annulé : déconnexion.");
            Account.Disconnect();
        }
    }
}
