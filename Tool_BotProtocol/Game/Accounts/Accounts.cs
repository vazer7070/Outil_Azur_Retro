using System;
using System.Collections.Concurrent;
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
        public string SecretQuestion { get; set; } = string.Empty;
        public bool needToCapture { get; set; } = false;
        public bool capturelance { get; set; } = false;
        public Logger Logger { get; private set; }
        public TcpClient Connexion { get; set; }
        public GameClass Game { get;  private set; }
        public bool UseMasterCommands = false;
        public AccountConfig accountConfig { get; private set; }
        public bool isdisposed;
        private AccountStates _accountState = AccountStates.DISCONNECTED;
        private readonly object _connectionSync = new object();
        private TaskCompletionSource<bool> _disconnectCompletion;
        private Task _connectTask;
        // Serveur de jeu annoncé par AYK/AXK tant que le ticket (AT) n'a pas été accepté (ATK) : voir ReserveTicketRetry.
        private string _ticketHost;
        private int _ticketPort, _ticketRetries;
        /// <summary>Nouvelles connexions au serveur de jeu quand il ferme la connexion avant <c>ATK</c>.</summary>
        public const int TicketRetryLimit = 2;
        /// <summary>Attente avant chaque nouvel essai (multipliée par le numéro de l'essai).</summary>
        public static int TicketRetryDelayMs = 400;
        public event Action AccountStateEvent;
        public event Action AccountDisconnectEvent;
        /// <summary>Vrai si le compte appartient à une équipe de comptes du bot (<see cref="Regroupement"/>), pas au groupe du jeu.</summary>
        public bool HasGroup => Regroupement != null;
        public bool IsGroupLeader => !HasGroup || Regroupement.IsLeader(this);
        /// <summary>Équipe de comptes du bot (multi-compte), posée par <see cref="Regroupement"/> ; le groupe du jeu est <c>Game.Interactions.Party</c>.</summary>
        public Regroupement Regroupement { get; internal set; }
        public bool CanUseMount = false;
        public ConcurrentDictionary<int, string> AccountCharactersInfo;
        // Remaining subscription time, in milliseconds on the Retro wire protocol.
        public long AboTime;
        public string ConnectionStatus { get; private set; } = "Déconnecté";
        /// <summary>État de connexion posé à la sélection du personnage (<c>ASK</c>), jusqu'à la première carte chargée.</summary>
        public const string LoadingMapStatus = "Chargement de la carte…";
        /// <summary>État de connexion une fois la première carte chargée (<c>GDM</c>).</summary>
        public const string InGameStatus = "En jeu";

        public Accounts(AccountConfig conf)
        {
            accountConfig = conf;
            Logger = new Logger();
            Game = new GameClass(this);
            AccountCharactersInfo = new ConcurrentDictionary<int, string>();
            Connexion = new TcpClient(this);

        }
        public async void Connect() => await ConnectAsync();

        public Task ConnectAsync()
        {
            lock (_connectionSync)
            {
                if (isdisposed) return Task.CompletedTask;
                if (_connectTask != null && !_connectTask.IsCompleted) return _connectTask;
                // UI callbacks and transport work must never run under _connectionSync.
                _connectTask = Task.Run(ConnectCoreAsync);
                return _connectTask;
            }
        }

        private async Task ConnectCoreAsync()
        {
            TcpClient connection;
            while (true)
            {
                Task pending;
                lock (_connectionSync)
                {
                    if (isdisposed) return;
                    pending = _disconnectCompletion?.Task;
                    if (pending == null)
                    {
                        connection = Connexion ?? (Connexion = new TcpClient(this));
                        break;
                    }
                }
                await pending;
            }
            try
            {
                // InitializeConfig is idempotent; UI and non-UI callers share the same defaults.
                if (string.IsNullOrWhiteSpace(GlobalConfig.IP)) GlobalConfig.InitializeConfig();
                SetConnectionStatus("Connexion au serveur d’authentification…");
                // Résultat de l'ouverture, pas l'état présent : le serveur peut déjà avoir refermé la connexion (voir ConnectToServer).
                bool connected = await ConnectEndpointAsync(connection, GlobalConfig.IP, int.Parse(GlobalConfig.AUTHPORT));
                lock (_connectionSync)
                {
                    if (isdisposed || !ReferenceEquals(Connexion, connection)) return;
                    if (!connected)
                    {
                        ConnectionStatus = "Serveur d’authentification inaccessible";
                        _accountState = AccountStates.DISCONNECTED;
                    }
                    else if (_accountState == AccountStates.DISCONNECTED)
                    {
                        ConnectionStatus = "Authentification en cours…";
                        _accountState = AccountStates.CONNECTED;
                    }
                }
                AccountStateEvent?.Invoke();
                if (!connected) Logger?.LogError("Connexion", "Serveur d’authentification inaccessible. Vérifiez l’adresse et le port.");
            }
            catch (ObjectDisposedException) { }
            catch (Exception error)
            {
                Logger?.LogException("Connexion", error);
                SetConnectionStatus("Connexion impossible : " + error.Message);
                Disconnect(connection);
            }
        }
        public void Disconnect()
        {
            TcpClient current;
            lock (_connectionSync) { current = Connexion; _ticketHost = null; }
            Disconnect(current);
        }
        internal void Disconnect(TcpClient expectedConnection)
        {
            if (expectedConnection == null) return;
            TaskCompletionSource<bool> completion;
            lock (_connectionSync)
            {
                if (isdisposed || !ReferenceEquals(Connexion, expectedConnection)) return;
                Connexion = null;
                _accountState = AccountStates.DISCONNECTED;
                if (!ConnectionStatus.StartsWith("Connexion impossible", StringComparison.Ordinal))
                    ConnectionStatus = "Déconnecté";
                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _disconnectCompletion = completion;
            }
            try
            {
                expectedConnection.Dispose();
                Game?.Clear();
                WelcomeKey = string.Empty;
                GameTicket = string.Empty;
                SecretQuestion = string.Empty;
                AccountCharactersInfo.Clear();
                if (accountConfig != null) lock (accountConfig.Servers) accountConfig.Servers.Clear();
                AccountDisconnectEvent?.Invoke();
                AccountStateEvent?.Invoke();
            }
            finally
            {
                lock (_connectionSync)
                    if (ReferenceEquals(_disconnectCompletion, completion)) _disconnectCompletion = null;
                completion.TrySetResult(true);
            }
        }
        public async void SwitchToGameServer(string coordinate)
        {
            try
            {
                string host;
                int port;
                ParseEndpoint(coordinate, out host, out port);
                await SwitchToGameServerAsync(host, port);
            }
            catch (Exception error)
            {
                Logger?.LogException("Redirection", error);
                Disconnect();
            }
        }

        public async Task SwitchToGameServerAsync(string host, int port)
        {
            TcpClient connection;
            lock (_connectionSync)
            {
                if (isdisposed || Connexion == null) return;
                connection = Connexion;
                _ticketHost = host; _ticketPort = port; _ticketRetries = 0;
            }
            await ConnectToGameServerAsync(connection, host, port).ConfigureAwait(false);
        }

        /// <summary><c>ATK</c> : le serveur de jeu a accepté le ticket ; une fermeture ultérieure n'est plus un refus de ticket.</summary>
        public void TicketAccepted() { lock (_connectionSync) _ticketHost = null; }

        /// <summary>
        /// Le serveur de jeu a fermé la connexion entre <c>AYK</c> et <c>ATK</c>, ou a répondu <c>ATE</c>. StarLoco n'accepte <c>AT</c>
        /// qu'après avoir reçu du Login le compte en attente (<c>WA</c>, traité de son côté de façon asynchrone) : un client plus rapide
        /// que ce message est éconduit (<c>getWaitingAccount</c> nul : <c>ATE</c>, puis <c>kick</c>). Comme le compte reste en attente,
        /// le bot se reconnecte au même serveur (au plus <see cref="TicketRetryLimit"/> fois) et renvoie le ticket sur le nouveau <c>HG</c>.
        /// Réserve l'essai sans rien appeler d'autre : le transport l'appelle sous son propre verrou, au moment où il remet sa session
        /// à zéro (ordre des verrous : transport, puis compte). Faux si la fermeture doit déconnecter le compte.
        /// </summary>
        internal bool ReserveTicketRetry(TcpClient connection, out string host, out int port, out int attempt)
        {
            host = null; port = 0; attempt = 0;
            lock (_connectionSync)
            {
                if (isdisposed || connection == null || !ReferenceEquals(Connexion, connection) || _ticketHost == null
                    || string.IsNullOrEmpty(GameTicket) || _ticketRetries >= TicketRetryLimit) return false;
                attempt = ++_ticketRetries; host = _ticketHost; port = _ticketPort;
                return true;
            }
        }

        /// <summary>Lance l'essai réservé par <see cref="ReserveTicketRetry"/>, hors de tout verrou (journal, puis reconnexion différée).</summary>
        internal void StartTicketRetry(TcpClient connection, string host, int port, int attempt, bool refused)
        {
            Logger?.LogDanger("Connexion", (refused
                ? "Le serveur de jeu a refusé le ticket (ATE) avant de l’avoir reçu du serveur d’authentification"
                : "Le serveur de jeu a fermé la connexion avant d’accepter le ticket")
                + " : nouvel essai " + attempt + "/" + TicketRetryLimit + ".");
            _ = RetryGameServerAsync(connection, host, port, attempt);
        }

        private async Task RetryGameServerAsync(TcpClient connection, string host, int port, int attempt)
        {
            await Task.Delay(TicketRetryDelayMs * attempt).ConfigureAwait(false);
            lock (_connectionSync) if (isdisposed || !ReferenceEquals(Connexion, connection) || _ticketHost == null) return;
            await ConnectToGameServerAsync(connection, host, port).ConfigureAwait(false);
        }

        private async Task ConnectToGameServerAsync(TcpClient connection, string host, int port)
        {
            try
            {
                SetConnectionStatus("Connexion au serveur de jeu…");
                connection.DisconnectSocket();
                // Connexion ouverte : le serveur peut l'avoir déjà refermée (HG, AT puis kick avant ATK). Cette fermeture a été
                // traitée par le transport (nouvel essai du ticket ou déconnexion) ; la lire ici comme un serveur injoignable
                // déconnecterait le compte et abandonnerait le nouvel essai.
                if (await ConnectEndpointAsync(connection, host, port)) return;
                SetConnectionStatus("Connexion impossible : serveur de jeu inaccessible");
                Logger?.LogError("Connexion", "Impossible de joindre le serveur de jeu. Vérifiez l’adresse annoncée par le Login.");
                Disconnect(connection);
            }
            catch (ObjectDisposedException) { }
            catch (Exception error)
            {
                Logger?.LogException("Redirection", error);
                SetConnectionStatus("Connexion impossible : " + error.Message);
                Disconnect(connection);
            }
        }

        /// <summary>Vrai dès qu'une des adresses de l'hôte a accepté la connexion, même si elle est déjà refermée.</summary>
        private static async Task<bool> ConnectEndpointAsync(TcpClient connection, string host, int port)
        {
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            IPAddress address;
            IPAddress[] addresses = IPAddress.TryParse(host, out address)
                ? new[] { address } : await Dns.GetHostAddressesAsync(host);
            foreach (var candidate in addresses.OrderBy(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 0 : 1))
            {
                if (await connection.ConnectToServer(candidate, port)) return true;
            }
            return false;
        }

        public static void ParseEndpoint(string endpoint, out string host, out int port)
        {
            if (string.IsNullOrWhiteSpace(endpoint)) throw new FormatException("Adresse du serveur manquante.");
            endpoint = endpoint.Trim();
            int split = endpoint.LastIndexOf(':');
            if (split < 1 || !int.TryParse(endpoint.Substring(split + 1), out port) || port < 1 || port > 65535)
                throw new FormatException("Adresse attendue : hôte:port ou [IPv6]:port.");
            host = endpoint.Substring(0, split).Trim();
            if (host.StartsWith("[", StringComparison.Ordinal) && host.EndsWith("]", StringComparison.Ordinal))
                host = host.Substring(1, host.Length - 2);
            if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
                throw new FormatException("Le nom ou l’adresse du serveur est invalide.");
        }

        public void SetConnectionStatus(string status)
        {
            ConnectionStatus = status ?? string.Empty;
            AccountStateEvent?.Invoke();
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
        public bool Is_In_Dialog() => _accountState == AccountStates.STORAGE || _accountState == AccountStates.DIALOG || _accountState == AccountStates.EXCHANGE || _accountState == AccountStates.BUYING || _accountState == AccountStates.SELLING || _accountState == AccountStates.ZAAP;
        public bool IsFighting() => _accountState == AccountStates.FIGHTING;
        public bool IsGathering() => _accountState == AccountStates.GATHERING;
        public bool IsMoving() => _accountState == AccountStates.MOVING;

        public void Dispose() => Dispose(true);
        ~Accounts() => Dispose(true);

        public virtual void Dispose(bool disposed)
        {
            TcpClient connection;
            GameClass game;
            lock (_connectionSync)
            {
                if (isdisposed) return;
                isdisposed = true;
                connection = Connexion;
                game = Game;
                _accountState = AccountStates.DISCONNECTED;
                ConnectionStatus = "Déconnecté";
                WelcomeKey = string.Empty;
                GameTicket = string.Empty;
                SecretQuestion = string.Empty;
                Connexion = null;
                AccountStateEvent = null;
                AccountDisconnectEvent = null;
            }
            connection?.Dispose();
            if (disposed) game?.Dispose();
            if (disposed) GC.SuppressFinalize(this);
        }
    }
}
