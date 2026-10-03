using System;
using System.Collections.Generic;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
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
        [MessageAttribution("AlEr")]
        public void MissingNickname(TcpClient client, string message) => Refuse(client,
            "Un pseudonyme doit être défini pour ce compte dans le client de jeu avant de connecter le bot.");
        [MessageAttribution("AlEs")]
        public void InvalidNickname(TcpClient client, string message) => Refuse(client, "Le pseudonyme du compte a été refusé.");
        [MessageAttribution("AXEr")]
        public void ServerUnauthorized(TcpClient client, string message) => Refuse(client, "Vous n’êtes pas autorisé à rejoindre ce serveur.");
        [MessageAttribution("AXEd")]
        public void ServerUnavailable(TcpClient client, string message) => Refuse(client, "Le serveur choisi est indisponible.");
        [MessageAttribution("AXEf")]
        public void ServerFull(TcpClient client, string message) => Refuse(client, "Le serveur choisi est complet ou exige un abonnement.");
        [MessageAttribution("AXEs")]
        public void MerchantCharacter(TcpClient client, string message) => Refuse(client, "Le personnage est en mode marchand sur un autre serveur.");
        [MessageAttribution("ATE")]
        public void InvalidTicket(TcpClient client, string message) => Refuse(client, "Le serveur de jeu a refusé le ticket de connexion.");
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
