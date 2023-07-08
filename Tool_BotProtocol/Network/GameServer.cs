using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Accounts;
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
        public event Action<string> FoundOrNotFriend;
        public event Action<string> RandomName;
        public Action<string> WrongVersion;
        public Action<string> IsBanned;
        public event Action AlreadyConnected;
        public event Action UpdateCharacterMenu;
        public event Action AddServerInMenu;
        public event Action CharacterDeleteFail;
        public event Action FailCreatePerso;
        public event Action FailSelectPerso;
        public event Action WrongCredential;
        public bool ExitCreationMenu = false;
        public string NameNewCharacter;
        public Dictionary<int, ServerStates> Servers = new Dictionary<int, ServerStates>();
        public void RefreshData(int S_Id, string S_Name, ServerStates S_states)
        {
            ServerID = S_Id;
            ServerName = S_Name;
            ServerStates = S_states;
            Servers.Add(S_Id,S_states);
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
        public void DisplayIsBanned(string time)
        {
            IsBanned?.Invoke(time);
        }
        public void WrongVer(string version)
        {
            WrongVersion?.Invoke(version);
        }
        public void WrongCred()
        {
            WrongCredential?.Invoke();
        }
        public void FailPeroSelect()
        {
            FailSelectPerso?.Invoke();
        }
        public void FailPersoCreate()
        {
            FailCreatePerso?.Invoke();
        }
        public void deleteCharacter()
        {
            CharacterDeleteFail?.Invoke();
        }
        public void DisplayErrorConnected()
        {
            AlreadyConnected?.Invoke();
        }
        public void AddServerMenu()
        {
            AddServerInMenu?.Invoke();
        }
        public void AddCharacterMenu()
        {
            UpdateCharacterMenu?.Invoke();
        }
        public void HaveRandomName(string name)
        {
            RandomName?.Invoke(name);
        }
        public void SearchFriend(string friend)
        {
            FoundOrNotFriend?.Invoke(friend);
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
