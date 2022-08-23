using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Game.Groupes;
using Tool_BotProtocol.Network;
using Tool_BotProtocol.Utils.Logger;

namespace Tool_BotProtocol.Game.Accounts
{
    public class Accounts :IDisposable
    {
        public string Name { get; set; } = string.Empty;
        public string WelcomeKey { get; set; } = string.Empty;
        public string GameTicket { get; set; } = string.Empty;
        public bool needToCapture { get; set; } = false;
        public bool capturelance { get; set; } = false;
        public Logger Logger { get; private set; }
        public TcpClient Connexion { get; set; }
        public GameClass Game { get;  private set; }
        public bool UseMasterCommands = false;
        public AccountConfig accountConfig { get; private set; }
        public bool isdisposed;
        private AccountStates _accountState = AccountStates.DISCONNECTED;
        public event Action AccountStateEvent;
        public event Action AccountDisconnectEvent;
        public bool HasGroup => Groupe != null;
        public bool IsGroupLeader => !HasGroup || Groupe.leader == this;
        public Groupe Groupe { get; set; }
        public bool CanUseMount = false;

        public Accounts(AccountConfig conf)
        {
            accountConfig = conf;
            Logger = new Logger();
            Game = new GameClass(this);

        }
        public void Connect()
        {
            Connexion = new TcpClient(this);
            Connexion.ConnectToServer(IPAddress.Parse(GlobalConfig.IP), int.Parse(GlobalConfig.AUTHPORT));
        }
        public void Disconnect()
        {
            Connexion?.Dispose();
            Connexion = null;

            Game.Clear();
            _accountState = AccountStates.DISCONNECTED;
            AccountDisconnectEvent?.Invoke();
        }
        public void SwitchToGameServer(string Coordinate)
        {
            Connexion.DisconnectSocket();
            Connexion.ConnectToServer(IPAddress.Parse(Coordinate.Split(':')[0]), int.Parse(Coordinate.Split(':')[1]));
           // Connexion.ConnectToServer(IPAddress.Parse("127.0.0.1"), 5555);
        }
    
        public AccountStates AccountStates
        {
            get => _accountState;
            set
            {
                _accountState = value;
                AccountStateEvent?.Invoke();
            }
        }

        public bool Isbusy() => _accountState != AccountStates.CONNECTED_INACTIVE && _accountState != AccountStates.REGENERATION;
        public bool Is_In_Dialog() => _accountState == AccountStates.STORAGE || _accountState == AccountStates.DIALOG || _accountState == AccountStates.EXCHANGE || _accountState == AccountStates.BUYING || _accountState == AccountStates.SELLING;
        public bool IsFighting() => _accountState == AccountStates.FIGHTING;
        public bool IsGathering() => _accountState == AccountStates.GATHERING;
        public bool IsMoving() => _accountState == AccountStates.MOVING;

        public void Dispose() => Dispose(true);
        ~Accounts() => Dispose(true);

        public virtual void Dispose(bool disposed)
        {
            if (!isdisposed)
            {
                if (disposed)
                {
                    Connexion?.Dispose();
                    Game.Dispose();
                }
                _accountState = AccountStates.DISCONNECTED;
                WelcomeKey = null;
                Connexion = null;
                Logger = null;
                Game = null;
                Name = null;
                accountConfig = null;
                isdisposed = true;
            }
        }
    }
}
