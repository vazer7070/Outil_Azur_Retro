using System;
using Tool_BotProtocol.Game.Accounts;

namespace Tool_BotProtocol.Game.Session
{
    /// <summary>
    /// Changement de nom du personnage en jeu (lot X32, manque B-07) avec la potion de changement de nom (objet 10860, action
    /// d'objet 12 de StarLoco : <c>Player.setChangeName(true)</c> envoie <c>AlEr</c>, hors combat, sans consommer la potion). Le client
    /// 1.34 ouvre alors la même interface <c>ChooseNickName</c> que pour un compte neuf et envoie le nom en ligne brute ; StarLoco la
    /// lit dans le <c>default</c> de <c>GameClient.parsePacket</c> tant que <c>isChangeName</c> est vrai (<c>changeName</c>) : <c>AlEs</c>
    /// si le nom est pris ou invalide (3 à 20 lettres ou tiret, pas de lettre triplée, un seul tiret, ni « modo », « admin »…), sinon
    /// le personnage est renommé, <c>AlEr</c> est renvoyé malgré la réussite, la potion retirée (<c>OR</c> / <c>OQ</c>) et le sprite
    /// réémis à la carte (<c>GM|~</c>) avec le nouveau nom. Sans potion dans le sac, <c>AlEr</c> et un message de chat (<c>cs</c>).
    /// Le bot ferme la demande quand le <c>GM|~</c> de son personnage porte le nom envoyé (<see cref="OnRenamed"/>) ; « Annuler »
    /// ferme la boîte sans jamais déconnecter (le client, lui, se déconnecte : écart choisi, voir <c>docs/BOT_STARLOCO.md</c>).
    /// </summary>
    public sealed class NameChange : NicknamePrompt
    {
        /// <summary>
        /// Premières lettres des paquets que <c>GameClient.parsePacket</c> de StarLoco aiguille vers un autre analyseur : une ligne
        /// qui commence par l'une d'elles n'atteint jamais <c>changeName</c> (le nom n'est pas lu, et la ligne est prise pour un paquet).
        /// « ping », sans distinction de casse, est répondu par <c>pong</c>.
        /// </summary>
        public const string ServerPacketLetters = "ABCcDdEeFfGgJKhiOPRQSTW";

        internal NameChange(Accounts.Accounts account) : base(account) { }

        protected override string LogCategory => "SESSION";
        protected override string Subject => "nom";
        protected override string FirstRequestMessage => "Potion de changement de nom : le serveur demande le nouveau nom du personnage (AlEr).";
        protected override string AcceptedMessage(string name) => "Personnage renommé « " + name + " » (GM~) : la demande de nom est close.";
        /// <summary>
        /// <c>AlEs</c> : nom pris ou invalide (<c>NICKNAME_ALREADY_USED</c>, comme <c>nickAlreadyUsed</c> du client). <c>AlEr</c> après un
        /// envoi n'a pas de raison : c'est la réussite (le <c>GM|~</c> suit) ou la potion absente du sac (message de chat du serveur) ;
        /// le client rouvre alors une saisie vierge.
        /// </summary>
        protected override string RefusalReason(bool alreadyUsed) => alreadyUsed ? ConnectionTexts.NicknameAlreadyUsed : null;

        /// <summary>Contrôles du client, puis la règle de StarLoco sur la première lettre (<see cref="IsReadByStarLoco"/>).</summary>
        public override string Validate(string nickname)
        {
            string refusal = base.Validate(nickname);
            if (refusal != null) return refusal;
            return IsReadByStarLoco(nickname) ? null : UnreadableByServer(nickname);
        }

        /// <summary>
        /// Vrai si StarLoco lirait cette ligne comme un nom : sa première lettre n'est pas un préfixe de paquet
        /// (<see cref="ServerPacketLetters"/>) et la ligne ne commence pas par « ping ».
        /// </summary>
        public static bool IsReadByStarLoco(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.Length > 3 && string.Equals(name.Substring(0, 4), "ping", StringComparison.OrdinalIgnoreCase)) return false;
            return ServerPacketLetters.IndexOf(name[0]) < 0;
        }

        /// <summary>Texte du refus local d'un nom que StarLoco ne lirait pas (ajout du bot, voir <c>docs/BOT_STARLOCO.md</c>).</summary>
        public static string UnreadableByServer(string name) =>
            "Nom refusé : StarLoco ne lit pas un nom qui commence par « " + (string.IsNullOrEmpty(name) ? string.Empty : name.Substring(0, 1))
            + " » (lettres A B C c D d E e F f G g h i J K O P Q R S T W et « ping » : il le prend pour un paquet).";

        /// <summary>« Annuler » : la boîte se ferme, la session continue (StarLoco reste en attente d'un nom, sans effet).</summary>
        public override void Cancel()
        {
            if (!CloseRequest()) return;
            Account.Logger?.LogInfo("SESSION", "Changement de nom abandonné : la session continue, la potion reste dans le sac.");
        }

        /// <summary><c>GM|~</c> du personnage avec un autre nom : le serveur l'a renommé ; la demande en cours, s'il y en a une, est close.</summary>
        internal void OnRenamed(string name)
        {
            if (!IsPending) return;
            OnAccepted(name);
        }
    }
}
