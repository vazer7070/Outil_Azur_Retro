using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    internal sealed class CombatFrame : Frame
    {
        [MessageAttribution("GJK")]
        public void Join(TcpClient client, string message) => client.account.Game.Fight.Join(message.Substring(3));
        [MessageAttribution("GP")]
        public void Places(TcpClient client, string message) => client.account.Game.Fight.SetPlaces(message.Substring(2));
        [MessageAttribution("GR")]
        public void Ready(TcpClient client, string message) => client.account.Game.Fight.SetReady(message.Substring(2));
        [MessageAttribution("GS")]
        public void Start(TcpClient client, string message) => client.account.Game.Fight.Start();
        [MessageAttribution("GTM")]
        public void Stats(TcpClient client, string message) => client.account.Game.Fight.UpdateTeamStats(message.Substring(3));
        [MessageAttribution("GTS")]
        public void TurnStart(TcpClient client, string message) => client.account.Game.Fight.StartTurn(message.Substring(3));
        [MessageAttribution("GTF")]
        public void TurnEnd(TcpClient client, string message) => client.account.Game.Fight.EndTurn(message.Substring(3));
        [MessageAttribution("GIC")]
        public void Positions(TcpClient client, string message) => client.account.Game.Fight.UpdatePositions(message.Substring(3));
        [MessageAttribution("GE")]
        public void End(TcpClient client, string message) => client.account.Game.Fight.Finish();
        [MessageAttribution("Im1170")]
        public void InsufficientPa(TcpClient client, string message) => client.account.Game.Fight.Refuse("PA insuffisants.");
        [MessageAttribution("Im1171")]
        public void Range(TcpClient client, string message) => client.account.Game.Fight.Refuse("La cible est hors portée.");
        [MessageAttribution("Im1172")]
        public void Cell(TcpClient client, string message) => client.account.Game.Fight.Refuse("La cellule ciblée est invalide.");
        [MessageAttribution("Im1173")]
        public void Line(TcpClient client, string message) => client.account.Game.Fight.Refuse("Ce sort doit être lancé en ligne.");
        [MessageAttribution("Im1174")]
        public void Sight(TcpClient client, string message) => client.account.Game.Fight.Refuse("La ligne de vue est bloquée.");
        [MessageAttribution("Im1175")]
        public void Turn(TcpClient client, string message) => client.account.Game.Fight.Refuse("Ce n’est pas votre tour.");
    }
}
