using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tool_BotProtocol.Utils.Logger
{
    /// <summary>
    /// Couleurs du journal et du chat. Les couleurs de chat reprennent celles du client 1.34 (<c>dofus.Constants.*_CHAT_COLOR</c>) :
    /// information, erreur, alignement, messages, chuchotements, groupe, guilde, recrutement, commerce, Incarnam, admin.
    /// </summary>
    public enum LogTypes
    {
        /// <summary><c>ERROR_CHAT_COLOR</c>.</summary>
        ERROR = 0xC10000,
        /// <summary><c>PVP_CHAT_COLOR</c> : avertissements et messages JcJ (<c>Im2…</c>).</summary>
        WARNING = 0xDD7700,
        /// <summary><c>INFO_CHAT_COLOR</c>.</summary>
        INFORMATION = 0x009900,
        DEBUG = 0x4f5051,
        /// <summary><c>MSG_CHAT_COLOR</c> : canal général.</summary>
        NORMAL = 0x111111,
        /// <summary><c>MSGCHUCHOTE_CHAT_COLOR</c>.</summary>
        PRIVATE = 0x0066FF,
        TCHATRECRUIT = 0x737373,
        TCHATCOMMERCE = 0x663300,
        TCHATADMIN = 0xFF00FF,
        TCHATPRIVATE = 0x0066FF,
        TCHATGUILD = 0x663399,
        /// <summary><c>GROUP_CHAT_COLOR</c> : canal de groupe.</summary>
        TCHATGROUP = 0x006699,
        /// <summary>Canal d'équipe : affiché comme un chuchotement par le client (<c>WHISP_CHAT</c>).</summary>
        TCHATTEAM = 0x0066FF,
        TCHATALIGNMENT = 0xDD7700,
        /// <summary><c>MEETIC_CHAT_COLOR</c> : canal d'Incarnam.</summary>
        TCHATINCARNAM = 0x0000CC,
        TCHATEMOTE = 0x222222,
        TCHATTHINK = 0x232323,
        /// <summary><c>COMMANDS_CHAT_COLOR</c> : sortie des commandes de la console.</summary>
        TCHATCOMMANDS = 0xE4287C,
    }
}
