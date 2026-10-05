using System;

namespace Tool_BotProtocol.Frames.Messages
{
    /// <summary>
    /// Associe une méthode <c>(TcpClient, string)</c> au préfixe de paquet qu'elle traite. Chaque préfixe n'a qu'un
    /// gestionnaire : <see cref="MessagesReception.Init()"/> refuse deux méthodes déclarant le même préfixe.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class MessageAttribution : Attribute
    {
        public string Packet;
        public MessageAttribution(string paquet) => Packet = paquet;
    }
}
