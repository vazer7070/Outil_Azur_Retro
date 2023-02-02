using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Messages
{
    public static class MessagesReception
    {
        public static readonly List<MessagesData> messagesDatas = new List<MessagesData>();

        public static void Init()
        {
            Assembly A = typeof(Frame).GetTypeInfo().Assembly;
            foreach (MethodInfo MI in A.GetTypes().SelectMany(x => x.GetMethods()).Where(m => m.GetCustomAttributes(typeof(MessageAttribution), false).Length > 0))
            {
                MessageAttribution MessageAttribut = MI.GetCustomAttributes(typeof(MessageAttribution), true)[0] as MessageAttribution;
                Type Typ = Type.GetType(MI.DeclaringType.FullName);

                object Instance = Activator.CreateInstance(Typ, null);
                MessagesData MD = new MessagesData(Instance, MessageAttribut.Packet, MI);
                if(!messagesDatas.Contains(MD))
                {
                    messagesDatas.Add(MD);
                }
            }
        }
        public static void Reception(TcpClient client, string message)
        {
            MessagesData Data = messagesDatas.Find(x => message.StartsWith(x.MessageName));
            try
            {
                if(Data != null)
                {
                    Data.Info.Invoke(Data.Instance, new object[2] { client, message });
                }
            }catch(Exception ex)
            {
                client.account.Logger.LogException("NETWORK", ex);
            }
        }
    }

}
