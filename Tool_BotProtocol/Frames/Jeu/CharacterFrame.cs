using System;
using System.Threading;
using System.Threading.Tasks;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Personnage : caractéristiques <c>As</c>, pods <c>Ow</c>, ping <c>pong</c>/<c>Bp</c>, régénération <c>ILS</c>/<c>ILF</c>.
    /// Les autres domaines ont chacun leur fichier : groupe, guilde, monture, échange, inventaire, métiers, émotes, session.
    /// </summary>
    internal class CharacterFrame : Frame
    {
        [MessageAttribution("As")]
        public void ActualiseStats(TcpClient client, string message) => client.account.Game.character.RefreshCaracs(message);

        [MessageAttribution("pong")]
        public void GetPingPong(TcpClient client, string message) => client.account.Logger.LogInfo("DOFUS", $"Ping: {client.GetPingAverage()} ms");

        [MessageAttribution("Bp")]
        public Task GetAllPing(TcpClient client, string message) => Task.Run(async () =>  await client.SendPacket($"Bp{client.GetPingAverage()}|{client.GetTotalPings()}|50"));

        [MessageAttribution("Ow")]

        public void GetPods(TcpClient client, string message)
        {
            string[] pods = message.Substring(2).Split('|');
            if (pods.Length < 2 || !int.TryParse(pods[0], out int actual_pods)
                || !int.TryParse(pods[1], out int Max_pods) || actual_pods < 0 || Max_pods < 0) return;
            CharacterClass perso = client.account.Game.character;

            perso.Inventory.Actual_pods = actual_pods;
            perso.Inventory.Pods_Max = Max_pods;
            client.account.Game.character.PodsRefreshEvent();
        }

        [MessageAttribution("ILS")]
        public void GetRegenTime(TcpClient client, string message)
        {
            string cut = message.Substring(3);
            int time = int.Parse(cut);
            Accounts A = client.account;
            CharacterClass perso = A.Game.character;

            if(perso.stats.VitalityActual < perso.stats.MaxVitality)
            {
                perso.Regen_Timer.Change(Timeout.Infinite, Timeout.Infinite);
                perso.Regen_Timer.Change(time, time);
                perso.DisplayRegen();
                A.Logger.LogInfo("DOFUS", $"Votre personnage récupère 1 pdv chaque {time / 1000} secondes");
            }
        }

        [MessageAttribution("ILF")]
        public void GetLifeRegen(TcpClient client, string message)
        {
            string cut = message.Substring(3);
            int life = int.Parse(cut);
            Accounts A = client.account;
            CharacterClass perso = A.Game.character;

            perso.stats.VitalityActual += life;
            A.Logger.LogInfo("DOFUS", $"Vous avez récupéré {life} points de vie");
        }
    }
}
