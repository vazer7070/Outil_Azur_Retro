using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Tool_BotProtocol.Network;
using Tool_BotProtocol.Utils.Logger;

namespace Tool_BotProtocol.Frames.Messages
{
    public static class MessagesReception
    {
        // Kept public for the existing transport diagnostics and extensions.
        public static readonly List<MessagesData> messagesDatas = new List<MessagesData>();
        private static readonly object Sync = new object();
        private const int UnhandledPreviewLength = 160;

        /// <summary>Enregistre les gestionnaires de <c>Tool_BotProtocol</c>. Peut être rappelée : les méthodes déjà enregistrées sont ignorées.</summary>
        public static void Init() => Init(typeof(Frame).Assembly);

        /// <summary>
        /// Enregistre toute méthode publique <c>[MessageAttribution("préfixe")] (TcpClient, string)</c> de l'assemblage,
        /// renvoyant <c>void</c> ou <c>Task</c>. Lève <see cref="InvalidOperationException"/> sans rien enregistrer si
        /// un préfixe est vide, si une signature est incorrecte ou si un préfixe est déjà pris par une autre méthode
        /// (le plus long préfixe gagnant, le second gestionnaire deviendrait inerte sans le dire).
        /// </summary>
        public static void Init(Assembly assembly)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));
            lock (Sync)
            {
                var pending = new List<KeyValuePair<string, MethodInfo>>();
                foreach (MethodInfo method in assembly.GetTypes().SelectMany(type => type.GetMethods())
                    .Where(method => method.IsDefined(typeof(MessageAttribution), false))
                    .OrderBy(method => method.MetadataToken))
                {
                    string prefix = ((MessageAttribution)method.GetCustomAttributes(typeof(MessageAttribution), false)[0]).Packet;
                    if (string.IsNullOrEmpty(prefix))
                        throw new InvalidOperationException("Préfixe de paquet vide sur " + Describe(method) + ".");
                    CheckSignature(method, prefix);
                    if (messagesDatas.Any(data => data.Info == method && data.MessageName == prefix)) continue;
                    pending.Add(new KeyValuePair<string, MethodInfo>(prefix, method));
                }

                IEnumerable<KeyValuePair<string, MethodInfo>> registered = messagesDatas
                    .Select(data => new KeyValuePair<string, MethodInfo>(data.MessageName, data.Info));
                foreach (var group in registered.Concat(pending).GroupBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    MethodInfo[] owners = group.Select(entry => entry.Value).Distinct().ToArray();
                    if (owners.Length > 1)
                        throw new InvalidOperationException("Le préfixe de paquet « " + group.Key + " » est déclaré par plusieurs gestionnaires (" +
                            string.Join(", ", owners.Select(Describe)) + ") : un seul gestionnaire par préfixe est autorisé.");
                }

                var instances = new Dictionary<Type, object>();
                foreach (var entry in pending)
                {
                    MethodInfo method = entry.Value;
                    object instance = null;
                    if (!method.IsStatic && !instances.TryGetValue(method.DeclaringType, out instance))
                    {
                        instance = messagesDatas.Where(data => data.Info.DeclaringType == method.DeclaringType)
                            .Select(data => data.Instance).FirstOrDefault() ?? Activator.CreateInstance(method.DeclaringType);
                        instances.Add(method.DeclaringType, instance);
                    }
                    messagesDatas.Add(new MessagesData(instance, entry.Key, method));
                }
            }
        }

        private static void CheckSignature(MethodInfo method, string prefix)
        {
            ParameterInfo[] parameters = method.GetParameters();
            bool valid = parameters.Length == 2 && parameters[0].ParameterType == typeof(TcpClient) &&
                parameters[1].ParameterType == typeof(string) &&
                (method.ReturnType == typeof(void) || typeof(Task).IsAssignableFrom(method.ReturnType));
            if (!valid)
                throw new InvalidOperationException("Le gestionnaire " + Describe(method) + " du préfixe « " + prefix +
                    " » doit avoir la signature (TcpClient, string) et renvoyer void ou Task.");
        }

        private static string Describe(MethodInfo method) => method.DeclaringType?.Name + "." + method.Name;

        public static void Reception(TcpClient client, string message)
        {
            ReceptionAsync(client, message).GetAwaiter().GetResult();
        }

        public static async Task ReceptionAsync(TcpClient client, string message)
        {
            if (client == null || string.IsNullOrEmpty(message)) return;
            MessagesData handler;
            lock (Sync)
                handler = messagesDatas.Where(data => !string.IsNullOrEmpty(data.MessageName) &&
                    message.StartsWith(data.MessageName, StringComparison.Ordinal))
                    .OrderByDescending(data => data.MessageName.Length).FirstOrDefault();
            if (handler == null)
            {
                ReportUnhandled(client, message);
                return;
            }
            try
            {
                object result = handler.Info.Invoke(handler.Instance, new object[] { client, message });
                Task task = result as Task;
                if (task != null) await task.ConfigureAwait(false);
            }
            catch (Exception error)
            {
                var invocation = error as TargetInvocationException;
                client.account?.Logger?.LogException("PROTOCOLE", invocation?.InnerException ?? error);
            }
        }

        /// <summary>Un paquet sans gestionnaire n'est plus perdu en silence : il est journalisé en niveau debug, masqué comme le journal des paquets.</summary>
        private static void ReportUnhandled(TcpClient client, string message)
        {
            var account = client.account;
            Logger logger = account?.Logger;
            if (logger == null) return;
            string shown = BotPacketRedactor.Redact(message, account);
            if (shown.Length > UnhandledPreviewLength) shown = shown.Substring(0, UnhandledPreviewLength) + "…";
            logger.LogDebug("PROTOCOLE", "Paquet sans gestionnaire ignoré (" + message.Length + " caractères) : " + shown);
        }
    }
}
