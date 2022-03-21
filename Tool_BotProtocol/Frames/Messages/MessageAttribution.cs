using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tool_BotProtocol.Frames.Messages
{
     class MessageAttribution : Attribute
    {
        public string Packet;
        public MessageAttribution(string paquet) => Packet = paquet;
    }
}
