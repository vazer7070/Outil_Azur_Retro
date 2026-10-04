using System;
using System.Threading;
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

        /// <summary>
        /// <c>pong</c> : réponse de StarLoco à <c>ping</c> (le client 1.34 utilise <c>rping/qping</c>, que StarLoco ne connaît pas).
        /// Mesure l'aller-retour du <c>ping</c> correspondant.
        /// </summary>
        [MessageAttribution("pong")]
        public void GetPingPong(TcpClient client, string message)
        {
            int roundTrip = client.NotifyPong();
            if (roundTrip < 0)
                client.account?.Logger.LogDebug("DOFUS", "pong reçu sans ping en attente.");
            else
                client.account?.Logger.LogInfo("DOFUS", $"Ping : {roundTrip} ms (moyenne {client.GetPingAverage()} ms sur {client.GetTotalPings()} mesure(s)).");
        }

        /// <summary>
        /// <c>Bp</c> : le client 1.34 y répond par sa moyenne de ping. StarLoco ne l'envoie jamais et ignore la réponse
        /// (matrice §2 n° 33) : le bot ne répond plus (il renvoyait des valeurs vides).
        /// </summary>
        [MessageAttribution("Bp")]
        public void GetAllPing(TcpClient client, string message) => client.account?.Logger.LogDebug("DOFUS", "Bp reçu : aucune réponse, StarLoco l'ignore.");

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
            Accounts A = client.account;
            if (A == null) return;
            if (!int.TryParse(message.Substring(3), out int time) || time <= 0)
            {
                A.Logger.LogDebug("DOFUS", "Intervalle de régénération illisible ignoré : " + message);
                return;
            }
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
            Accounts A = client.account;
            if (A == null) return;
            if (!int.TryParse(message.Substring(3), out int life))
            {
                A.Logger.LogDebug("DOFUS", "Points de vie regagnés illisibles ignorés : " + message);
                return;
            }
            CharacterClass perso = A.Game.character;

            perso.stats.VitalityActual += life;
            A.Logger.LogInfo("DOFUS", $"Vous avez récupéré {life} points de vie");
        }
    }
}
