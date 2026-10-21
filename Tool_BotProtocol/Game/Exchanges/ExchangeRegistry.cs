using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Data;

namespace Tool_BotProtocol.Game.Exchanges
{
    /// <summary>
    /// Types d'échange de <c>ECK&lt;type&gt;</c>, tels que <c>dofus.aks.Exchange.onCreate</c> du client 1.34 les distingue
    /// et que StarLoco les émet (<c>GameClient.request</c>, <c>Trunk.open</c>, <c>Action</c> banquier, <c>JobAction</c>).
    /// </summary>
    public static class ExchangeTypes
    {
        public const int NpcShop = 0;
        public const int Player = 1;
        public const int NpcExchange = 2;
        public const int Craft = 3;
        public const int OfflineMerchant = 4;
        public const int Storage = 5;
        public const int MyShop = 6;
        public const int Collector = 8;
        public const int Pets = 9;
        public const int AuctionSell = 10;
        public const int AuctionBuy = 11;
        /// <summary>Artisanat sécurisé côté artisan (<c>CraftSecure.player1</c> de StarLoco ; <c>isClient</c> faux dans le client).</summary>
        public const int SecureCraftArtisan = 12;
        /// <summary>Artisanat sécurisé côté client (celui qui paie et reçoit l'objet ; <c>isClient</c> = type 13 dans le client).</summary>
        public const int SecureCraftClient = 13;
        /// <summary>Livre des artisans (interface <c>CrafterList</c> du client, liste de métiers ; StarLoco : <c>GameCase</c>).</summary>
        public const int CrafterList = 14;
        public const int MountStorage = 15;
        public const int Shed = 16;
        public const int PetsResurrection = 17;
        /// <summary>Type 18 : même fenêtre <c>Exchange</c> que les types 2, 9 et 17 chez le client (<c>Exchange.onCreate</c>) ; StarLoco ne l'envoie pas.</summary>
        public const int NpcExchangeWindow = 18;

        /// <summary>Nom lisible d'un type pour le journal.</summary>
        public static string Describe(int type)
        {
            switch (type)
            {
                case NpcShop: return "boutique de PNJ";
                case Player: return "échange entre joueurs";
                case NpcExchange: return "échange avec un PNJ";
                case Craft: return "atelier";
                case OfflineMerchant: return "marchand";
                case Storage: return "coffre ou banque";
                case MyShop: return "organisation du magasin";
                case Collector: return "percepteur";
                case Pets: return "familiers";
                case AuctionSell: return "hôtel de vente (vente)";
                case AuctionBuy: return "hôtel de vente (achat)";
                case SecureCraftArtisan: return "artisanat sécurisé (artisan)";
                case SecureCraftClient: return "artisanat sécurisé (client)";
                case CrafterList: return "livre des artisans";
                case MountStorage: return "sacoches de la monture";
                case Shed: return "étable";
                case PetsResurrection: return "résurrection de familier";
                case NpcExchangeWindow: return "échange (type 18)";
                default: return "type " + type.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>
    /// Aiguillage des échanges d'un compte. <c>ECK&lt;type&gt;</c> ouvre la fenêtre enregistrée pour ce type et en fait
    /// l'échange en cours ; <c>EL</c>, <c>EMK</c>, <c>EmK</c>, <c>EsK</c>, <c>EK</c>, <c>Ew</c> lui sont transmis ; <c>EV</c> la ferme.
    /// Un type sans fenêtre est journalisé et refermé proprement (<c>EV</c>), au lieu de laisser le personnage bloqué.
    /// Chaque fonction ajoute ses types sans modifier ce fichier : attribut <see cref="ExchangeTypeAttribute"/> sur sa classe
    /// dans <c>Tool_BotProtocol</c>, ou <see cref="Register"/> depuis une autre bibliothèque. Un type n'a qu'une fenêtre.
    /// </summary>
    public sealed class ExchangeRegistry
    {
        private const string Reference = "ÉCHANGE";

        private sealed class Registration
        {
            internal Registration(object key, Func<Accounts.Accounts, IExchange> factory) { Key = key; Factory = factory; }
            /// <summary>Clé de l'instance par compte : la classe découverte, ou la fabrique enregistrée.</summary>
            internal object Key { get; }
            internal Func<Accounts.Accounts, IExchange> Factory { get; }
        }

        private static readonly object StaticSync = new object();
        private static readonly Dictionary<int, Registration> Registrations = new Dictionary<int, Registration>();
        private static bool discovered;

        private readonly object sync = new object();
        private readonly Accounts.Accounts account;
        private readonly Dictionary<object, IExchange> instances = new Dictionary<object, IExchange>();
        private IExchange current;

        internal ExchangeRegistry(Accounts.Accounts owner)
        {
            account = owner;
            Registration[] known;
            lock (StaticSync) { EnsureDiscovered(); known = Registrations.Values.Distinct().ToArray(); }
            // Fenêtres créées d'avance (propriétés Shop, Exchange, Storage) ; une fabrique extérieure défaillante est
            // journalisée et retentée à son premier ECK, sans empêcher la création du compte.
            foreach (Registration registration in known)
            {
                try { Resolve(registration); }
                catch (Exception error) when (!(error is OutOfMemoryException)) { account?.Logger?.LogException(Reference, error); }
            }
        }

        /// <summary>Déclare la fenêtre d'un type depuis une autre bibliothèque ; la fabrique est appelée une fois par compte.</summary>
        public static void Register(int type, Func<Accounts.Accounts, IExchange> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            lock (StaticSync)
            {
                EnsureDiscovered();
                if (Registrations.ContainsKey(type))
                    throw new InvalidOperationException("Le type d'échange " + type + " a déjà une fenêtre.");
                Registrations[type] = new Registration(factory, factory);
            }
        }

        public static bool IsRegistered(int type)
        {
            lock (StaticSync) { EnsureDiscovered(); return Registrations.ContainsKey(type); }
        }

        public static int[] RegisteredTypes
        {
            get { lock (StaticSync) { EnsureDiscovered(); return Registrations.Keys.OrderBy(type => type).ToArray(); } }
        }

        /// <summary>Échange ouvert par le dernier <c>ECK</c>, ou <c>null</c>.</summary>
        public IExchange Current { get { lock (sync) return current; } }
        /// <summary>Dernier message utile (type non pris en charge, refus de demande…).</summary>
        public string LastMessage { get; private set; } = string.Empty;
        /// <summary>Levé après chaque ouverture ou fermeture (fil de réception réseau).</summary>
        public event Action CurrentChanged;

        /// <summary>Fenêtre de ce compte pour un type, ou <c>null</c> si aucun lot ne gère ce type.</summary>
        public IExchange For(int type)
        {
            Registration registration;
            lock (StaticSync) { EnsureDiscovered(); Registrations.TryGetValue(type, out registration); }
            return registration == null ? null : Resolve(registration);
        }

        /// <summary>Instance de ce compte pour une classe de fenêtre découverte (par exemple <c>Get&lt;PlayerExchange&gt;()</c>).</summary>
        public T Get<T>() where T : class, IExchange
        {
            lock (sync)
            {
                if (instances.TryGetValue(typeof(T), out IExchange found)) return (T)found;
            }
            Registration registration;
            lock (StaticSync) { EnsureDiscovered(); registration = Registrations.Values.FirstOrDefault(entry => Equals(entry.Key, typeof(T))); }
            return registration == null ? null : Resolve(registration) as T;
        }

        /// <summary><c>ECK&lt;type&gt;[|&lt;données&gt;]</c>.</summary>
        internal Task OnCreatedAsync(string payload)
        {
            string body = payload ?? string.Empty;
            int separator = body.IndexOf('|');
            string head = separator < 0 ? body : body.Substring(0, separator);
            string data = separator < 0 ? string.Empty : body.Substring(separator + 1);
            if (!int.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out int type))
                return RefuseAsync("Échange de type illisible (ECK" + body + ") : fermeture demandée au serveur.");
            IExchange window;
            try { window = For(type); }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                account?.Logger?.LogException(Reference, error);
                return RefuseAsync("La fenêtre de l'échange « " + ExchangeTypes.Describe(type) + " » n'a pas pu être créée : fermeture demandée au serveur.");
            }
            if (window == null)
                return RefuseAsync("Échange « " + ExchangeTypes.Describe(type) + " » (ECK" + type + ") non pris en charge par le bot : fermeture demandée au serveur.");
            IExchange previous;
            lock (sync) { previous = current; current = window; }
            if (previous != null && !ReferenceEquals(previous, window) && previous.IsOpen)
            {
                account?.Logger?.LogDanger(Reference, "Nouvel échange ouvert alors que le précédent n'était pas fermé : il est oublié.");
                Safe(previous, "ECK", w => w.Clear());
            }
            if (!Safe(window, "ECK", w => w.OnCreated(type, data)))
            {
                lock (sync) { if (ReferenceEquals(current, window)) current = null; }
                Safe(window, "ECK", w => w.Clear());
                return RefuseAsync("L'échange « " + ExchangeTypes.Describe(type) + " » n'a pas pu être ouvert : fermeture demandée au serveur.");
            }
            RaiseChanged();
            return Task.CompletedTask;
        }

        /// <summary>
        /// <c>ERE&lt;code&gt;</c> : demande refusée (codes de <c>Exchange.onRequest</c>). Exception (lot X4-CORR) : StarLoco envoie
        /// <c>EREO</c> juste après le <c>ECK9</c> qui répond à <c>ER17</c> (<c>GameClient.request</c>, branche « 17 » sans <c>return</c>,
        /// puis le contrôle « déjà en échange ») ; le client 1.34 afficherait ALREADY_EXCHANGE alors que l'échange est bien ouvert.
        /// Ce seul <c>EREO</c>, annoncé par la fenêtre des familiers, est journalisé en débogage et ne change ni
        /// <see cref="LastMessage"/> ni l'échange en cours ; tout autre <c>ERE</c> reste une erreur.
        /// </summary>
        internal void OnRequestError(string code)
        {
            string reason;
            if (!string.IsNullOrEmpty(code) && code[0] == 'O' && Current is PetsExchange pets && pets.IsOpen && pets.ConsumeResurrectionEreo())
            {
                account?.Logger?.LogDebug(Reference, "EREO reçu juste après le ECK9 qui répond à ER17 : StarLoco l'envoie à chaque résurrection (GameClient.request, branche « 17 » sans return) ; l'échange reste ouvert.");
                return;
            }
            switch ((code ?? string.Empty).Length > 0 ? code[0] : '\0')
            {
                case 'O': reason = Text("ALREADY_EXCHANGE", "Ce joueur (ou vous-même) est déjà occupé par un échange."); break;
                case 'T': reason = Text("NOT_NEAR_CRAFT_TABLE", "Vous n'êtes pas à côté de l'atelier."); break;
                case 'J': reason = Text("ERROR_85", "Demande impossible (erreur 85)."); break;
                case 'o': reason = Text("ERROR_70", "Demande impossible : trop de pods (erreur 70)."); break;
                case 'S': reason = Text("ERROR_62", "Demande impossible (erreur 62)."); break;
                default: reason = Text("CANT_EXCHANGE", "Échange impossible."); break;
            }
            LastMessage = reason;
            account?.Logger?.LogError(Reference, reason + " (ERE" + code + ")");
            // Le client affiche chaque refus par showMessage(…, "ERROR_CHAT") (Exchange.onRequest) : ligne locale du chat (lot X6, ERET compris).
            try { account?.Game?.Chat?.AddLocal(Chat.ChatMessageKind.Error, reason); }
            catch (Exception error) when (!(error is OutOfMemoryException)) { account?.Logger?.LogException(Reference, error); }
            Get<PlayerExchange>()?.OnRequestRefused(reason);
            RaiseChanged();
        }

        /// <summary><c>EL</c> : transmis à l'échange en cours ; ignoré hors échange.</summary>
        internal void OnList(string payload) => Dispatch("EL", payload, window => window.OnList(payload));
        internal void OnLocalMovement(string data) => Dispatch("EMK", data, window => window.OnLocalMovement(data));
        internal void OnDistantMovement(string data) => Dispatch("EmK", data, window => window.OnDistantMovement(data));
        internal void OnStorageMovement(string data) => Dispatch("EsK", data, window => window.OnStorageMovement(data));
        internal void OnReady(string data) => Dispatch("EK", data, window => window.OnReady(data));

        /// <summary><c>Ep&lt;type&gt;;…</c> : paiement de l'artisanat sécurisé (lot X5), transmis à sa fenêtre seulement.</summary>
        internal void OnPayment(string data)
        {
            IExchange window = Current;
            if (window is CraftSecureExchange secure && secure.IsOpen) Safe(secure, "Ep", w => secure.OnPayment(data));
            else account?.Logger?.LogDebug(Reference, "Paquet Ep" + (data ?? string.Empty) + " reçu hors artisanat sécurisé : ignoré.");
        }
        internal void OnPods(string data) => Dispatch("Ew", data, window => window.OnPods(data));

        /// <summary>
        /// <c>EV[a]</c> : ferme l'échange en cours. Sans échange ouvert, il peut s'agir de la réponse à une demande d'échange
        /// entre joueurs (refus ou annulation par l'autre joueur) : la demande en attente est alors oubliée.
        /// </summary>
        internal void OnLeave(string suffix)
        {
            IExchange window;
            lock (sync) { window = current; current = null; }
            if (window != null) Safe(window, "EV", w => w.OnLeave(suffix));
            else
            {
                PlayerExchange players = Get<PlayerExchange>();
                if (players != null && players.PendingRequest != null) Safe(players, "EV", w => w.OnLeave(suffix));
                else account?.Logger?.LogDebug(Reference, "EV" + suffix + " reçu sans échange ouvert.");
            }
            ResetLegacyState();
            RaiseChanged();
        }

        /// <summary>Oublie toutes les fenêtres d'échange sans rien envoyer (déconnexion).</summary>
        public void Clear()
        {
            IExchange[] all;
            lock (sync) { current = null; all = instances.Values.Distinct().ToArray(); }
            foreach (IExchange window in all) Safe(window, "Clear", w => w.Clear());
            LastMessage = string.Empty;
        }

        /// <summary>
        /// Texte du client (<c>lang.xml</c> chargé par <c>LangData</c>) avec ses paramètres <c>%1</c>…, ou le texte de repli
        /// rédigé pour le bot quand la clé n'est pas disponible.
        /// </summary>
        public static string Text(string key, string fallback, params string[] args)
        {
            try
            {
                if (LangData.Text.Has(key))
                {
                    string value = LangData.Text.Get(key, args);
                    if (!string.IsNullOrWhiteSpace(value) && !value.StartsWith("!", StringComparison.Ordinal)) return value;
                }
            }
            catch (Exception) { /* Textes du client illisibles : le texte du bot suffit. */ }
            return fallback;
        }

        private void Dispatch(string prefix, string data, Action<IExchange> action)
        {
            IExchange window = Current;
            if (window == null || !window.IsOpen)
            {
                account?.Logger?.LogDebug(Reference, "Paquet " + prefix + (data ?? string.Empty) + " reçu hors échange : ignoré.");
                return;
            }
            Safe(window, prefix, action);
        }

        /// <summary>Appel protégé d'une fenêtre (celles d'autres bibliothèques ne passent pas par <see cref="ExchangeWindow"/>).</summary>
        private bool Safe(IExchange window, string prefix, Action<IExchange> action)
        {
            try { action(window); return true; }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                account?.Logger?.LogError(Reference, "Paquet " + prefix + " non appliqué par " + window.GetType().Name + " : " + error.Message);
                return false;
            }
        }

        private Task RefuseAsync(string message)
        {
            LastMessage = message;
            account?.Logger?.LogDanger(Reference, message);
            ResetLegacyState();
            RaiseChanged();
            var connection = account?.Connexion;
            if (connection == null || !connection.IsConnected()) return Task.CompletedTask;
            return SendLeaveAsync(connection);
        }

        private async Task SendLeaveAsync(Network.TcpClient connection)
        {
            try { await connection.SendPacket("EV", true).ConfigureAwait(false); }
            catch (Exception error) { account?.Logger?.LogException(Reference, error); }
        }

        /// <summary>L'ancien état « stockage » posé par les versions précédentes pour les échanges non gérés ne doit pas survivre à <c>EV</c>.</summary>
        private void ResetLegacyState()
        {
            if (account != null && account.AccountStates == Accounts.AccountStates.STORAGE && !(Current?.IsOpen ?? false))
                account.AccountStates = Accounts.AccountStates.CONNECTED_INACTIVE;
        }

        private void RaiseChanged()
        {
            Action handlers = CurrentChanged;
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                try { handler(); }
                catch (Exception error) { account?.Logger?.LogException(Reference, error); }
            }
        }

        private IExchange Resolve(Registration registration)
        {
            lock (sync)
            {
                if (instances.TryGetValue(registration.Key, out IExchange existing)) return existing;
            }
            IExchange created = registration.Factory(account);
            if (created == null) throw new InvalidOperationException("La fabrique d'un échange a renvoyé null.");
            lock (sync)
            {
                if (instances.TryGetValue(registration.Key, out IExchange existing)) return existing;
                instances[registration.Key] = created;
                return created;
            }
        }

        private static void EnsureDiscovered()
        {
            if (discovered) return;
            var found = new Dictionary<int, Registration>();
            IEnumerable<Type> types = typeof(ExchangeRegistry).Assembly.GetTypes()
                .Where(type => type.IsDefined(typeof(ExchangeTypeAttribute), false))
                .OrderBy(type => type.MetadataToken);
            foreach (Type type in types)
            {
                ConstructorInfo constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(Accounts.Accounts) }, null);
                if (type.IsAbstract || !typeof(IExchange).IsAssignableFrom(type) || constructor == null)
                    throw new InvalidOperationException(type.Name + " : une fenêtre d'échange doit être concrète, implémenter IExchange et avoir un constructeur (Accounts).");
                Func<Accounts.Accounts, IExchange> factory = owner => (IExchange)constructor.Invoke(new object[] { owner });
                var registration = new Registration(type, factory);
                foreach (ExchangeTypeAttribute attribute in type.GetCustomAttributes(typeof(ExchangeTypeAttribute), false))
                {
                    if (found.ContainsKey(attribute.Type) || Registrations.ContainsKey(attribute.Type))
                        throw new InvalidOperationException("Le type d'échange " + attribute.Type + " a deux fenêtres.");
                    found[attribute.Type] = registration;
                }
            }
            foreach (var pair in found) Registrations[pair.Key] = pair.Value;
            discovered = true;
        }
    }
}
