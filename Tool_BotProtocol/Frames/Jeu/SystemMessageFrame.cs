using System.Globalization;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Messages système <c>M0&lt;id&gt;[|&lt;a;b;…&gt;]</c> (lot X3, manque V-20). Comme <c>Basics.onServerMessage</c> du client
    /// (branche « 0 »), le message n'est pas affiché à sa réception : il annonce une coupure (<c>M04</c> maintenance, <c>M018</c>
    /// expulsion) et le compte le garde pour la boîte de perte de connexion qui suit (<see cref="Accounts.ConnectionLost"/>).
    /// Les refus <c>M030</c>, <c>M031</c> et <c>M032</c> de la connexion au serveur de jeu gardent leurs gestionnaires
    /// (<see cref="AuthInGame"/>), dont le préfixe plus long l'emporte.
    /// </summary>
    internal sealed class SystemMessageFrame : Frame
    {
        [MessageAttribution("M0")]
        public void Received(TcpClient client, string message)
        {
            Accounts account = client.account;
            if (account == null) return;
            string body = message.Substring(2);
            int separator = body.IndexOf('|');
            string idText = separator < 0 ? body : body.Substring(0, separator);
            if (!int.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out int id))
            {
                account.Logger?.LogDebug("CONNEXION", "Message système illisible ignoré : " + message);
                return;
            }
            // Comme le client : le deuxième champ porte les paramètres %1, %2… séparés par « ; » (M018|<modérateur>;<raison>).
            string[] args = separator < 0 ? new string[0] : body.Substring(separator + 1).Split('|')[0].Split(';');
            var systemMessage = new SystemMessage(id, args);
            account.OnSystemMessage(systemMessage);
            account.Logger?.LogDanger("CONNEXION", "Message du serveur annonçant une coupure (M0" + idText + ") : " + systemMessage.Text);
        }
    }
}
