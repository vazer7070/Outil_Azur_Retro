using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Messages
{
    public static class MessagesReception
    {
        // Kept public for the existing transport diagnostics and extensions.
        public static readonly List<MessagesData> messagesDatas = new List<MessagesData>();
        private static readonly object Sync = new object();

        public static void Init()
        {
            lock (Sync)
            {
                Assembly assembly = typeof(Frame).Assembly;
                var instances = new Dictionary<Type, object>();
                foreach (MethodInfo method in assembly.GetTypes().SelectMany(type => type.GetMethods())
                    .Where(method => method.IsDefined(typeof(MessageAttribution), false))
                    .OrderBy(method => method.MetadataToken))
                {
                    string prefix = ((MessageAttribution)method.GetCustomAttributes(typeof(MessageAttribution), false)[0]).Packet;
                    if (messagesDatas.Any(data => data.Info == method && data.MessageName == prefix)) continue;
                    object instance;
                    if (!instances.TryGetValue(method.DeclaringType, out instance))
                    {
                        instance = messagesDatas.Where(data => data.Info.DeclaringType == method.DeclaringType)
                            .Select(data => data.Instance).FirstOrDefault() ?? Activator.CreateInstance(method.DeclaringType);
                        instances.Add(method.DeclaringType, instance);
                    }
                    messagesDatas.Add(new MessagesData(instance, prefix, method));
                }
            }
        }

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
            if (handler == null) return;
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
    }
}
