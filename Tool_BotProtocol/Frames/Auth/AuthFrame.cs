using System;
using System.Collections.Generic;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Auth
{
    internal class AuthFrame : Frame
    {
        [MessageAttribution("AlEf")]
        public void WrongCredential(TcpClient client, string message) => Refuse(client,
            "Nom de compte ou mot de passe incorrect.", () => client.account?.Game.Server.WrongCred());
        [MessageAttribution("AlEa")]
        public void AlreadyConnected(TcpClient client, string message) => Refuse(client, "Ce compte est déjà en cours de connexion.");
        [MessageAttribution("AlEv")]
        public void WrongVersion(TcpClient client, string message) => Refuse(client,
            "Version du client refusée par le serveur.", () => client.account?.Game.Server.WrongVer(GlobalConfig.VERSION));
        [MessageAttribution("AlEc")]
        public void AccountAlreadyInGame(TcpClient client, string message) => PlayerAlreadyInGame(client, message);
        [MessageAttribution("AlEd")]
        public void PlayerAlreadyInGame(TcpClient client, string message) => Refuse(client,
            "Un personnage de ce compte est déjà connecté. Reconnectez-vous après sa déconnexion.",
            () => client.account?.Game.Server.DisplayErrorConnected());
        [MessageAttribution("AlEb")]
        public void PermanentlyBanned(TcpClient client, string message) => Refuse(client,
            "Connexion refusée : ce compte est banni.", () => client.account?.Game.Server.DisplayIsBanned("Ce compte est banni."));
        [MessageAttribution("AlEk")]
        public void AccountBanned(TcpClient client, string message)
        {
            string[] fields = message.Substring(4).Split('|');
            long days, hours, minutes;
            string reason = "Connexion refusée : ce compte est temporairement banni.";
            if (fields.Length >= 3 && long.TryParse(fields[0], out days) && days >= 0 &&
                long.TryParse(fields[1], out hours) && hours >= 0 && long.TryParse(fields[2], out minutes) && minutes >= 0)
            {
                var durations = new List<string>();
                if (days > 0) durations.Add(days + " jour(s)");
                if (hours > 0) durations.Add(hours + " heure(s)");
                if (minutes > 0) durations.Add(minutes + " minute(s)");
                if (durations.Count > 0) reason = "Ce compte est banni pendant " + string.Join(", ", durations) + ".";
            }
            Refuse(client, reason, () => client.account?.Game.Server.DisplayIsBanned(reason));
        }
        /// <summary>
        /// <c>AlEr</c> : pendant la connexion, le compte n'a pas encore de pseudo (première demande) ou le pseudo envoyé est le nom
        /// du compte. Le Login attend alors le pseudo en ligne brute (<see cref="NicknameChoice"/>, interface <c>ChooseNickName</c>
        /// du client). En jeu, le même paquet accompagne la potion de changement de nom (manque B-07, lot X32).
        /// </summary>
        [MessageAttribution("AlEr")]
        public void NicknameRequired(TcpClient client, string message) => RequestNickname(client, false);
        /// <summary><c>AlEs</c> : pseudo envoyé déjà pris ou fait d'un caractère interdit ; le Login attend un autre pseudo.</summary>
        [MessageAttribution("AlEs")]
        public void NicknameUnavailable(TcpClient client, string message) => RequestNickname(client, true);

        /// <summary>
        /// Pendant la connexion (avant <c>ASK</c>), la demande va au pseudo du compte ; sans interface abonnée à
        /// <see cref="NicknamePrompt.Requested"/>, personne ne peut répondre : la connexion est refusée plutôt que de laisser le Login
        /// attendre un pseudo qui ne viendra pas. En jeu (potion de changement de nom, lot X32), la demande va à
        /// <see cref="Tool_BotProtocol.Game.Session.NameChange"/> et une session n'est jamais déconnectée par ces paquets.
        /// </summary>
        private static void RequestNickname(TcpClient client, bool alreadyUsed)
        {
            Accounts account = client.account;
            if (account == null) return;
            if (account.Game?.character != null && account.Game.character.id > 0)
            {
                if (!account.Game.Session.NameChange.OnRequested(alreadyUsed))
                    account.Logger?.LogDanger("SESSION", (alreadyUsed ? "AlEs" : "AlEr")
                        + " reçu en jeu (changement de nom, lot X32) : aucune interface pour saisir le nom, la session continue.");
                return;
            }
            if (!account.Nickname.OnRequested(alreadyUsed))
                Refuse(client, "Le compte n'a pas encore de pseudo et rien ne permet d'en saisir un ici : "
                    + "choisissez-le depuis l'écran de connexion du bot ou dans le client de jeu.");
        }
        [MessageAttribution("AXEr")]
        public void ServerUnauthorized(TcpClient client, string message) => Refuse(client, "Vous n’êtes pas autorisé à rejoindre ce serveur.");
        [MessageAttribution("AXEd")]
        public void ServerUnavailable(TcpClient client, string message) => Refuse(client, "Le serveur choisi est indisponible.");
        [MessageAttribution("AXEf")]
        public void ServerFull(TcpClient client, string message) => Refuse(client, "Le serveur choisi est complet ou exige un abonnement.");
        [MessageAttribution("AXEs")]
        public void MerchantCharacter(TcpClient client, string message) => Refuse(client, "Le personnage est en mode marchand sur un autre serveur.");
        /// <summary>
        /// StarLoco répond <c>ATE</c> puis ferme la session quand le compte en attente (<c>WA</c> du Login) n'est pas encore arrivé :
        /// tant qu'un ticket est en cours et qu'il reste des essais, c'est la même course qu'une fermeture avant <c>ATK</c>, et le
        /// ticket est renvoyé sur une nouvelle connexion. Sinon le refus est définitif.
        /// </summary>
        [MessageAttribution("ATE")]
        public void InvalidTicket(TcpClient client, string message)
        {
            if (client.RetryTicketAfterRefusal()) return;
            Refuse(client, "Le serveur de jeu a refusé le ticket de connexion.");
        }
        [MessageAttribution("AlE")]
        public void OtherLoginRefusal(TcpClient client, string message) => Refuse(client, "La connexion a été refusée par le serveur d’authentification.");
        [MessageAttribution("AXE")]
        public void OtherServerRefusal(TcpClient client, string message) => Refuse(client, "La sélection du serveur de jeu a été refusée.");

        private static void Refuse(TcpClient client, string reason, Action notify = null)
        {
            try { notify?.Invoke(); }
            finally { AccountLoginFrame.Fail(client, reason); }
        }
    }
}
