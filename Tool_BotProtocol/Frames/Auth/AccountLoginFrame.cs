using System;
using System.Globalization;
using System.Threading.Tasks;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Network;
using Tool_BotProtocol.Network.Enums;
using Tool_BotProtocol.Utils.Crypto;

namespace Tool_BotProtocol.Frames.Auth
{
    public class AccountLoginFrame : Frame
    {
        [MessageAttribution("HC")]
        public async Task GetWelcomeKey(TcpClient client, string message)
        {
            Accounts account = client.account;
            if (account == null) return;
            string key = message.Substring(2);
            if (key.Length == 0 || (!GlobalConfig.BYPASS && account.accountConfig.Password.Length > key.Length))
            {
                Fail(client, "La clé de connexion est absente ou trop courte pour ce mot de passe.");
                return;
            }
            account.WelcomeKey = key;
            string passwordPacket = null;
            if (!GlobalConfig.BYPASS)
            {
                try { passwordPacket = Hash.Crypt_Password(account.accountConfig.Password, key); }
                catch (ArgumentException error) { Fail(client, error.Message); return; }
                if (string.IsNullOrWhiteSpace(account.accountConfig.Account) ||
                    account.accountConfig.Account.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                {
                    Fail(client, "Le nom de compte contient des caractères interdits.");
                    return;
                }
            }
            await client.SendPacket(GlobalConfig.VERSION).ConfigureAwait(false);
            if (GlobalConfig.BYPASS)
                await client.SendPacket("#Z\n" + client.Token).ConfigureAwait(false);
            else
            {
                await client.SendPacket(account.accountConfig.Account).ConfigureAwait(false);
                await client.SendPacket(passwordPacket).ConfigureAwait(false);
            }
            await client.SendPacket("Af").ConfigureAwait(false);
        }

        [MessageAttribution("Af")]
        public void GetLoginQueue(TcpClient client, string message)
        {
            string[] fields = message.Substring(2).Split('|');
            long position, total;
            if (fields.Length < 2 || !long.TryParse(fields[0], out position) || !long.TryParse(fields[1], out total))
            {
                client.account?.Logger?.LogDanger("CONNEXION", "Informations de file d’attente invalides.");
                return;
            }
            client.account?.Logger?.LogInfo("File d’attente", "Position : " + position + " / " + total);
        }

        [MessageAttribution("Ad")]
        public void Getpseudo(TcpClient client, string message) { if (client.account != null) client.account.Name = message.Substring(2); }
        [MessageAttribution("ADE")]
        public void FailDeletePerso(TcpClient client, string message) => client.account?.Game.Server.deleteCharacter();
        [MessageAttribution("ASE")]
        public void SelectPersoFail(TcpClient client, string message) => client.account?.Game.Server.FailPeroSelect();
        [MessageAttribution("AAE")]
        public void FailCreateCharacter(TcpClient client, string message)
        {
            client.account?.Logger?.LogError("PERSONNAGE", "Création refusée : vérifiez le nom et le nombre de personnages disponibles.");
            client.account?.Game.Server.FailPersoCreate();
        }

        [MessageAttribution("AH")]
        public void GetServerState(TcpClient client, string message)
        {
            Accounts account = client.account;
            if (account == null) return;
            GameServer game = account.Game.Server;
            foreach (string entry in message.Substring(2).Split('|'))
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                string[] fields = entry.Split(';');
                int id;
                byte state;
                if (fields.Length < 2 || !int.TryParse(fields[0], out id) || id <= 0 ||
                    !byte.TryParse(fields[1], out state) || state > 2)
                {
                    account.Logger.LogDanger("SERVEURS", "Un serveur mal formé a été ignoré.");
                    continue;
                }
                game.Servers[id] = (ServerStates)state;
                if (game.ServerID == 0 || game.ServerID == id)
                {
                    game.ServerID = id;
                    game.ServerName = id.ToString(CultureInfo.InvariantCulture);
                    game.ServerStates = (ServerStates)state;
                }
            }
            game.AddServerMenu();
        }

        [MessageAttribution("AQ")]
        public Task GetSecretQuestion(TcpClient client, string message)
        {
            if (client.account != null) client.account.SecretQuestion = Uri.UnescapeDataString(message.Substring(2));
            return client.SendPacket("Ax", true);
        }
        [MessageAttribution("AxK")]
        public void GetServerList(TcpClient client, string message)
        {
            Accounts account = client.account;
            if (account == null) return;
            string[] fields = message.Substring(3).Split('|');
            long remaining;
            if (!long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out remaining))
            {
                account.Logger.LogDanger("SERVEURS", "Durée d’abonnement invalide.");
                return;
            }
            account.AboTime = Math.Max(0, remaining);
            lock (account.accountConfig.Servers)
            {
                account.accountConfig.Servers.Clear();
                for (int index = 1; index < fields.Length; index++)
                {
                    string[] server = fields[index].Split(',');
                    int id, count;
                    if (server.Length == 2 && int.TryParse(server[0], out id) && id > 0 &&
                        int.TryParse(server[1], out count) && count >= 0)
                        account.accountConfig.Servers.Add(id.ToString(CultureInfo.InvariantCulture) + "," + count);
                }
            }
            // AH includes servers without characters, unlike StarLoco's AxK list.
            account.SetConnectionStatus("Choisissez un serveur de jeu");
            account.Game.Server.AddServerMenu();
        }

        [MessageAttribution("AXK")]
        public async Task GetServerSelection(TcpClient client, string message)
        {
            if (message.Length <= 14) { Fail(client, "Redirection de jeu incomplète : ticket absent."); return; }
            try
            {
                int port = Hash.Decrypt_Port(message.Substring(11, 3).ToCharArray());
                if (port < 1 || port > 65535) { Fail(client, "Le port du serveur de jeu est invalide."); return; }
                Accounts account = client.account;
                if (account == null) return;
                account.GameTicket = message.Substring(14);
                await account.SwitchToGameServerAsync(Hash.Decrypt_IP(message.Substring(3, 8)), port).ConfigureAwait(false);
            }
            catch (ArgumentException) { Fail(client, "Redirection de jeu mal formée."); }
            catch (IndexOutOfRangeException) { Fail(client, "Redirection de jeu mal formée."); }
        }

        [MessageAttribution("AYK")]
        public async Task GetServerSelectionRemaster(TcpClient client, string message)
        {
            int separator = message.IndexOf(';', 3);
            string host;
            int port;
            if (separator < 0 || separator == message.Length - 1 ||
                !TryParseEndpoint(message.Substring(3, separator - 3), out host, out port))
            {
                Fail(client, "Redirection de jeu invalide : adresse, port ou ticket absent.");
                return;
            }
            Accounts account = client.account;
            if (account == null) return;
            account.GameTicket = message.Substring(separator + 1);
            await account.SwitchToGameServerAsync(host, port).ConfigureAwait(false);
        }

        public static bool TryParseEndpoint(string endpoint, out string host, out int port)
        {
            host = null;
            port = 0;
            if (string.IsNullOrWhiteSpace(endpoint)) return false;
            int separator;
            if (endpoint[0] == '[')
            {
                int closing = endpoint.IndexOf(']');
                if (closing <= 1 || closing + 1 >= endpoint.Length || endpoint[closing + 1] != ':') return false;
                host = endpoint.Substring(1, closing - 1);
                separator = closing + 1;
            }
            else
            {
                separator = endpoint.LastIndexOf(':');
                if (separator <= 0) return false;
                host = endpoint.Substring(0, separator);
            }
            if (host.IndexOfAny(new[] { ' ', '\r', '\n', '\0', ';', '[', ']' }) >= 0 ||
                !int.TryParse(endpoint.Substring(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out port)) return false;
            return port > 0 && port <= 65535 && Uri.CheckHostName(host) != UriHostNameType.Unknown;
        }

        internal static void Fail(TcpClient client, string reason)
        {
            Accounts account = client.account;
            if (account == null) return;
            try
            {
                account.SetConnectionStatus("Connexion impossible : " + reason);
                account.Logger?.LogError("CONNEXION", reason);
            }
            finally { account.Disconnect(); }
        }
        [MessageAttribution("AF")]
        public void SearchFriends(TcpClient client, string message)
        {
            if (message.Contains(";")) client.account?.Game.Server.SearchFriend(message.Substring(2));
        }
        [MessageAttribution("APK")]
        public void CallRandomName(TcpClient client, string message) => client.account?.Game.Server.HaveRandomName(message.Substring(3));
    }
}
