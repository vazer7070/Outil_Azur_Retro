using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tool_BotProtocol.Game.Accounts
{
    public enum AccountStates
    {
        CONNECTED,
        CONNECTED_INACTIVE,
        DISCONNECTED,
        MOVING,
        FIGHTING,
        GATHERING,
        DIALOG,
        STORAGE,
        EXCHANGE,
        BUYING,
        SELLING,
        REGENERATION
    }
}
