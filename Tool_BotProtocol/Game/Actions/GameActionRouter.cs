using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Game.Actions
{
    /// <summary>
    /// Paquet <c>GA&lt;action de jeu&gt;;&lt;type&gt;;&lt;acteur&gt;;&lt;paramètres&gt;</c>, découpé comme <c>GameActions.onActions</c> :
    /// seuls les trois premiers <c>;</c> séparent, le reste forme les paramètres.
    /// </summary>
    public sealed class GameActionPacket
    {
        /// <summary>Identifiant de l'action de jeu (vide dans <c>GA;0</c>, <c>GA;2;…</c>).</summary>
        public string GameActionId { get; private set; }
        public int ActionId { get; private set; }
        /// <summary>Acteur brut (vide = personnage du compte dans le client).</summary>
        public string Actor { get; private set; }
        public long? ActorId { get; private set; }
        public string Parameters { get; private set; }
        public string Raw { get; private set; }

        /// <summary>Lit un paquet <c>GA…</c> (avec ou sans préfixe) ; null si le type d'action est illisible.</summary>
        public static GameActionPacket Parse(string message)
        {
            if (string.IsNullOrEmpty(message)) return null;
            string body = message.StartsWith("GA", StringComparison.Ordinal) ? message.Substring(2) : message;
            string[] parts = body.Split(new[] { ';' }, 4);
            if (parts.Length < 2 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int action)) return null;
            string actor = parts.Length > 2 ? parts[2] : string.Empty;
            long? actorId = long.TryParse(actor, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : (long?)null;
            return new GameActionPacket
            {
                GameActionId = parts[0], ActionId = action, Actor = actor, ActorId = actorId,
                Parameters = parts.Length > 3 ? parts[3] : string.Empty, Raw = message
            };
        }
    }

    /// <summary>Contexte transmis aux gestionnaires d'actions de jeu hors combat.</summary>
    public sealed class GameActionContext
    {
        public GameActionContext(TcpClient client, GameActionPacket packet)
        {
            Client = client; Packet = packet;
        }
        public TcpClient Client { get; }
        public Accounts.Accounts Account => Client?.account;
        public GameActionPacket Packet { get; }
        public int ActionId => Packet.ActionId;
        public string Parameters => Packet.Parameters;
    }

    /// <summary>Déclare un gestionnaire d'action <c>GA</c> hors combat, découvert automatiquement dans <c>Tool_BotProtocol</c>.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class GameActionHandlerAttribute : Attribute
    {
        public GameActionHandlerAttribute(int actionId) { ActionId = actionId; }
        public int ActionId { get; }
    }

    /// <summary>
    /// Table « type d'action <c>GA</c> → gestionnaire » pour les actions hors combat autres que 0 (refus), 1 (déplacement)
    /// et 2 (changement de carte), traitées par <c>MapFrame</c>. Chaque fonction ajoute les siennes sans éditer ce fichier :
    /// méthode statique <c>[GameActionHandler(id)] static Task|void Nom(GameActionContext)</c> dans <c>Tool_BotProtocol</c>,
    /// ou <see cref="Register"/> depuis une autre bibliothèque. Un type d'action n'a qu'un gestionnaire : un doublon lève une exception.
    /// </summary>
    public static class GameActionRouter
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<int, Func<GameActionContext, Task>> Handlers = new Dictionary<int, Func<GameActionContext, Task>>();
        private static bool discovered;

        /// <summary>Action reçue sans gestionnaire (diagnostic).</summary>
        public static event Action<GameActionContext> Unhandled;

        public static void Register(int actionId, Func<GameActionContext, Task> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            lock (Sync)
            {
                EnsureDiscovered();
                if (Handlers.ContainsKey(actionId))
                    throw new InvalidOperationException("L'action de jeu GA " + actionId + " a déjà un gestionnaire.");
                Handlers[actionId] = handler;
            }
        }

        public static bool Unregister(int actionId)
        {
            lock (Sync) { EnsureDiscovered(); return Handlers.Remove(actionId); }
        }

        public static bool IsRegistered(int actionId)
        {
            lock (Sync) { EnsureDiscovered(); return Handlers.ContainsKey(actionId); }
        }

        public static int[] RegisteredActions
        {
            get { lock (Sync) { EnsureDiscovered(); return Handlers.Keys.OrderBy(id => id).ToArray(); } }
        }

        /// <summary>Exécute le gestionnaire de l'action ; faux si aucun n'est enregistré.</summary>
        public static async Task<bool> DispatchAsync(GameActionContext context)
        {
            if (context?.Packet == null) return false;
            Func<GameActionContext, Task> handler;
            lock (Sync)
            {
                EnsureDiscovered();
                Handlers.TryGetValue(context.ActionId, out handler);
            }
            if (handler == null)
            {
                Unhandled?.Invoke(context);
                return false;
            }
            Task task = handler(context);
            if (task != null) await task.ConfigureAwait(false);
            return true;
        }

        private static void EnsureDiscovered()
        {
            if (discovered) return;
            var found = new Dictionary<int, Func<GameActionContext, Task>>();
            IEnumerable<MethodInfo> methods = typeof(GameActionRouter).Assembly.GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Where(method => method.IsDefined(typeof(GameActionHandlerAttribute), false))
                .OrderBy(method => method.MetadataToken);
            foreach (MethodInfo method in methods)
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1 || parameters[0].ParameterType != typeof(GameActionContext)
                    || (method.ReturnType != typeof(Task) && method.ReturnType != typeof(void)))
                    throw new InvalidOperationException(method.DeclaringType?.Name + "." + method.Name
                        + " : un gestionnaire GA doit être statique, recevoir GameActionContext et renvoyer Task ou void.");
                Func<GameActionContext, Task> handler = method.ReturnType == typeof(Task)
                    ? (Func<GameActionContext, Task>)Delegate.CreateDelegate(typeof(Func<GameActionContext, Task>), method)
                    : Wrap((Action<GameActionContext>)Delegate.CreateDelegate(typeof(Action<GameActionContext>), method));
                foreach (GameActionHandlerAttribute attribute in method.GetCustomAttributes(typeof(GameActionHandlerAttribute), false))
                {
                    if (found.ContainsKey(attribute.ActionId))
                        throw new InvalidOperationException("L'action de jeu GA " + attribute.ActionId + " a deux gestionnaires.");
                    found[attribute.ActionId] = handler;
                }
            }
            foreach (var pair in found)
            {
                if (Handlers.ContainsKey(pair.Key))
                    throw new InvalidOperationException("L'action de jeu GA " + pair.Key + " a deux gestionnaires.");
            }
            foreach (var pair in found) Handlers[pair.Key] = pair.Value;
            discovered = true;
        }

        private static Func<GameActionContext, Task> Wrap(Action<GameActionContext> action) => context =>
        {
            action(context);
            return Task.CompletedTask;
        };
    }
}
