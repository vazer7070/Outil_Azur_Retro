using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Tool_BotProtocol.Frames.Messages
{
    public class MessagesData
    {
        public object Instance { get; set; }
        public string MessageName { get; set; }
        public MethodInfo Info { get; set; }

        public MessagesData(object I, string N, MethodInfo M)
        {
            Instance = I;
            MessageName = N;
            Info = M;
        }
    }
}
