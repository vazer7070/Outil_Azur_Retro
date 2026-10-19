using System.Globalization;
using System.Threading.Tasks;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Mini-jeux du serveur (lot X32) : <c>TC&lt;id&gt;|&lt;clé&gt;</c> (<c>Tutorial.onCreate</c> du client, qui charge
    /// <c>tutorials/&lt;id&gt;_&lt;clé&gt;.swf</c>). StarLoco ne l'envoie que pour la roulette du Bouftou (<c>Action.java</c>, cas 36).
    /// Le bot répond par la fin du mini-jeu (<see cref="Game.Session.TutorialPlay"/>). Propriétaire : lot X32.
    /// </summary>
    internal class TutorialFrame : Frame
    {
        [MessageAttribution("TC")]
        public async Task Create(TcpClient client, string message)
        {
            Accounts account = client.account;
            if (account?.Game == null) return;
            string body = message.Length > 2 ? message.Substring(2) : string.Empty;
            int separator = body.IndexOf('|');
            string idText = separator < 0 ? body : body.Substring(0, separator);
            string key = separator < 0 ? string.Empty : body.Substring(separator + 1);
            if (!int.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id < 0)
            {
                account.Logger?.LogDanger("SESSION", "Mini-jeu illisible ignoré : " + message);
                return;
            }
            await account.Game.Session.Tutorial.OnStartedAsync(id, key).ConfigureAwait(false);
        }
    }
}
