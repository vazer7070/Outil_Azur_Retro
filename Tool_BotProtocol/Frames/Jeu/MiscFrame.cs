using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    class MiscFrame : Frame
    {
        [MessageAttribution("Im189")]
        public void WelcomeMessage(TcpClient client, string message) => client.account.Logger.LogError("[DOFUS]", "Bienvenue sur DOFUS, le Monde des Douze ! Attention Il est interdit de communiquer le nom d'utilisateur et le mot de passe de votre compte.");
        [MessageAttribution("Im039")]
        public void SpectatorMode_Disabled(TcpClient client, string message) => client.account.Logger.LogInfo("[COMBAT]", "Le mode spéctateur est désactivé.");
        [MessageAttribution("Im040")]
        public void SpectatorMode_Enabled(TcpClient client, string message) => client.account.Logger.LogInfo("[COMBAT]", "Le mode spéctateur est activé.");
        [MessageAttribution("Im0152")]
        public void GetMessageFromLastConnexion(TcpClient client, string message)
        {
            string M = message.Substring(3).Split(';')[1];
            client.account.Logger.LogInfo("[DOFUS]", $"Dernière connexion à votre compte effectuée le {M.Split('~')[0]}/{M.Split('~')[1]}/{M.Split('~')[2]} à {M.Split('~')[3]}:{M.Split('~')[4]} avec l'ip: {M.Split('~')[5]}");
        }
        [MessageAttribution("Im0153")]
        public Task NewConnexionMessage(TcpClient client, string message) => Task.Run(async () =>
        {
            client.account.Logger.LogInfo("[DOFUS]", $"Votre adresse ip actuelle est {message.Substring(3).Split(';')[1]}");
            if (client.account.IsGroupLeader == true && client.account.HasGroup == true)
            {
                foreach (var M in client.account.Groupe.Membres)
                {
                    await Task.Delay(780);

                    if ((M.AccountStates == Game.Accounts.AccountStates.CONNECTED || M.AccountStates == Game.Accounts.AccountStates.CONNECTED_INACTIVE))
                    {
                        client.account.Logger.LogInfo("[GROUPE]", $"Demande de groupe pour le joueur {M.Game.character.Name}");
                        client.SendPacket($"PI{M.Game.character.Name}");

                        await Task.Delay(1080);
                    }
                }
            }
            else if (client.account.HasGroup == true && (client.account.Groupe.leader.AccountStates == Game.Accounts.AccountStates.CONNECTED || client.account.Groupe.leader.AccountStates == Game.Accounts.AccountStates.CONNECTED_INACTIVE))
            {
                await Task.Delay(580);
                Accounts leader = client.account.Groupe.leader;
                await leader.Connexion.SendPacket($"PI{client.account.Game.character.Name}");
                leader.Logger.LogInfo("[GROUPE]", $"Demande de groupe pour le joueur {client.account.Game.character.Name}.");
                await Task.Delay(1080);
            }
        });
        [MessageAttribution("Im020")]
        public void OpenChestLoseKamas(TcpClient client, string message) => client.account.Logger.LogInfo("[DOFUS]", $"Ouvrir ce coffre vous a couté {message.Split(';')[1]} kamas.");
        [MessageAttribution("Im025")]
        public void CongratMascotte(TcpClient client, string message) => client.account.Logger.LogInfo("[DOFUS]", "Votre animal est heureux de vous revoir.");
        [MessageAttribution("Im0157")]
        public void UnavailableChat(TcpClient client, string message) => client.account.Logger.LogInfo("[DOFUS]", $"Ce canal est réservé aux abonnés niveau {message.Split(';')[1]}.");
        [MessageAttribution("Im037")]
        public void AFKMessage(TcpClient client, string message) => client.account.Logger.LogInfo("[DOFUS]", "Tu es maintenant considéré comme AFK.");
        [MessageAttribution("Im1165")]
        public void WorldSaveGood(TcpClient client, string message) => client.account.Logger.LogInfo("[DOFUS]", "Le monde a été correctement sauvegardé.!");
        [MessageAttribution("Im112")]
        public void CantHaveMorepods(TcpClient client, string message) => client.account.Logger.LogInfo("[DOFUS]", "Tu es trop chargé, libère de la place dans ton inventaire.");
        
    }
}
