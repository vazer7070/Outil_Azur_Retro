using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Network.Enums;
using Tool_BotProtocol.Utils.Interfaces;

namespace Tool_BotProtocol.Network
{
    public class GameServer: IEliminable, IDisposable
    {
        public int ServerID;
        public string ServerName;
        public ServerStates ServerStates;
        private bool Disposed = false;

        public GameServer() => RefreshData(0, "UNDEFINED", ServerStates.OFFLINE);
        public void RefreshData(int S_Id, string S_Name, ServerStates S_states)
        {
            ServerID = S_Id;
            ServerName = S_Name;
            ServerStates = S_states;
        }
        public string GetState(ServerStates state)
        {
            switch (state)
            {
                case ServerStates.ONLINE:
                    return "En-Ligne";
                case ServerStates.SAVING:
                    return "Sauvegarde";
                case ServerStates.OFFLINE:
                    return "Hors-Ligne";
                default:
                    return "";
            }
        }
        public void Dispose() => Dispose(true);
        public void Clear()
        {
            ServerID = 0;
            ServerName = null;
            ServerStates = ServerStates.OFFLINE;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Disposed)
                return;

            ServerID = 0;
            ServerName = null;
            ServerStates = ServerStates.OFFLINE;
            Disposed = true;
        }
    }
}
