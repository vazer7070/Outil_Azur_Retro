using System;
using Tool_BotProtocol.Game.Accounts;

namespace Tool_BotProtocol.Utils.Logger
{
    public static class BotPacketRedactor
    {
        public static string Redact(string packet, Accounts account)
        {
            if (string.IsNullOrEmpty(packet)) return string.Empty;
            foreach (string prefix in new[] { "HC", "AYK", "AXK", "AQ", "AD", "#1", "#Z" })
                if (packet.StartsWith(prefix, StringComparison.Ordinal)) return "[Authentification masquée]";
            if (packet.StartsWith("AT", StringComparison.Ordinal) && !packet.StartsWith("ATK", StringComparison.Ordinal))
                return "[Ticket de jeu masqué]";
            if (account != null && (packet == account.accountConfig?.Account || packet == account.accountConfig?.Password ||
                packet == account.GameTicket || packet.IndexOf("\n#1", StringComparison.Ordinal) >= 0 ||
                packet.IndexOf("\n#Z", StringComparison.Ordinal) >= 0))
                return "[Identifiants masqués]";
            // A hostile or unusually large frame must not flood the UI log.
            return packet.Length > 2048 ? packet.Substring(0, 2048) + "… [tronqué]" : packet;
        }
    }
}
