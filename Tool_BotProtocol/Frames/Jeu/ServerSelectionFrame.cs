using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tool_BotProtocol.Frames.Auth;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    internal class ServerSelectionFrame : Frame
    {
        [MessageAttribution("HG")]
        public Task WelcomeInGame(TcpClient client, string message)
        {
            if (string.IsNullOrEmpty(client.account?.GameTicket))
            {
                AccountLoginFrame.Fail(client, "Le ticket de connexion au serveur de jeu est absent.");
                return Task.CompletedTask;
            }
            return client.SendPacket("AT" + client.account.GameTicket);
        }
        [MessageAttribution("ATK0")]
        public async Task ServerSelected(TcpClient client, string message)
        {
            client.account?.TicketAccepted();
            await client.SendPacket("Ak0").ConfigureAwait(false);
            await client.SendPacket("AV").ConfigureAwait(false);
        }
        [MessageAttribution("ATK")]
        public Task ServerSelectionned(TcpClient client, string message)
        {
            client.account?.TicketAccepted();
            return client.SendPacket("AV");
        }
        [MessageAttribution("AV0")]
        public async Task List_Perso(TcpClient client, string message)
        {
            await client.SendPacket("Agfr").ConfigureAwait(false);
            await client.SendPacket("AL").ConfigureAwait(false);
            await client.SendPacket("Af").ConfigureAwait(false);
        }
        [MessageAttribution("ALK")]
        public async Task Perso_Selection(TcpClient client, string message)
        {
            Accounts account = client.account;
            if (account == null) return;
            string[] fields = message.Substring(3).Split('|');
            long subscription;
            int advertised;
            if (fields.Length < 2 || !long.TryParse(fields[0], out subscription) ||
                !int.TryParse(fields[1], out advertised) || advertised < 0)
            {
                account.Logger.LogError("PERSONNAGES", "Liste de personnages invalide.");
                return;
            }
            var characters = new Dictionary<int, string>();
            int createdId = 0;
            for (int index = 2; index < fields.Length; index++)
            {
                if (string.IsNullOrEmpty(fields[index])) continue;
                string[] character = fields[index].Split(';');
                int id, level, appearance;
                if (character.Length < 4 || !int.TryParse(character[0], out id) || id <= 0 ||
                    string.IsNullOrWhiteSpace(character[1]) || !int.TryParse(character[2], out level) || level < 1 ||
                    !int.TryParse(character[3], out appearance))
                {
                    account.Logger.LogDanger("PERSONNAGES", "Un personnage mal formé a été ignoré.");
                    continue;
                }
                characters[id] = character[1] + "|" + level + "|" + appearance + "|";
                if (string.Equals(character[1], account.Game.Server.NameNewCharacter, StringComparison.Ordinal)) createdId = id;
            }
            account.AboTime = Math.Max(0, subscription);
            account.AccountCharactersInfo.Clear();
            foreach (var character in characters) account.AccountCharactersInfo.TryAdd(character.Key, character.Value);
            if (characters.Count != advertised)
                account.Logger.LogDanger("PERSONNAGES", "Le nombre de personnages reçu diffère du nombre annoncé.");
            if (account.Game.Server.ExitCreationMenu && createdId > 0)
            {
                account.Game.Server.ExitCreationMenu = false;
                // AS seul : « AF » est la recherche d'ami du client (AF<nom>) et StarLoco l'ignore sur le serveur de jeu.
                await client.SendPacket("AS" + createdId).ConfigureAwait(false);
            }
            else
            {
                account.Game.Server.ExitCreationMenu = false;
                account.SetConnectionStatus("Choisissez un personnage");
                account.Game.Server.AddCharacterMenu();
            }
        }
        [MessageAttribution("ASK")]
        public async Task HaveSelectedPerso(TcpClient client, string message)
        {
            Accounts account = client.account;
            if (account == null) return;
            if (!message.StartsWith("ASK|", StringComparison.Ordinal))
            {
                AccountLoginFrame.Fail(client, "Sélection de personnage invalide.");
                return;
            }
            string[] fields = message.Substring(4).Split('|');
            int id, race;
            byte level, sex;
            if (fields.Length < 10 || !int.TryParse(fields[0], out id) || id <= 0 ||
                string.IsNullOrWhiteSpace(fields[1]) || !byte.TryParse(fields[2], out level) || level == 0 ||
                !int.TryParse(fields[3], out race) || race < -1 || race > byte.MaxValue ||
                !byte.TryParse(fields[4], out sex) || sex > 1)
            {
                AccountLoginFrame.Fail(client, "Le serveur a envoyé un personnage incomplet ou invalide.");
                return;
            }
            account.Game.character.SetPerso_Data(id, fields[1], level, sex, race < 0 ? (byte)0 : (byte)race);
            try { account.Game.character.Inventory.Add_Items(fields[9]); }
            catch (Exception error) when (error is AggregateException || error is FormatException ||
                error is OverflowException || error is IndexOutOfRangeException)
            {
                AccountLoginFrame.Fail(client, "Le serveur a envoyé un inventaire de personnage invalide.");
                return;
            }
            account.Game.character.PersoSelectedEvent();
            account.Game.character.AFK_Timer.Change(1200000, 1200000);
            account.SetConnectionStatus(Accounts.LoadingMapStatus);
            account.AccountStates = AccountStates.CONNECTED_INACTIVE;
            // Comme le client 1.34 : GC1 seul. « BYA » est la bascule « absent » (Basics.away) : StarLoco marquerait
            // le personnage absent et répondrait Im037 (matrice §2 n° 35).
            await client.SendPacket("GC1").ConfigureAwait(false);
        }
    }
}
