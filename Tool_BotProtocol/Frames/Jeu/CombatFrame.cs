using System.Threading.Tasks;
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
        /// <summary><c>GTL|id|id…</c> : ordre des tours (aussi reçu dans <c>GA;999</c> après une invocation ou une mort).</summary>
        [MessageAttribution("GTL")]
        public void TurnList(TcpClient client, string message) => client.account.Game.Fight.SetTurnList(message.Substring(3));
        /// <summary><c>GTR&lt;id&gt;</c> : le client répond <c>GT</c>.</summary>
        [MessageAttribution("GTR")]
        public Task TurnReady(TcpClient client, string message) => client.account.Game.Fight.TurnReadyAsync(client, message.Substring(3));
        /// <summary><c>GTS&lt;id&gt;|&lt;durée&gt;[|&lt;tour&gt;]</c> : StarLoco n'envoie que deux champs (matrice §2 n° 16).</summary>
        [MessageAttribution("GTS")]
        public void TurnStart(TcpClient client, string message) => client.account.Game.Fight.StartTurn(message.Substring(3));
        [MessageAttribution("GTF")]
        public void TurnEnd(TcpClient client, string message) => client.account.Game.Fight.EndTurn(message.Substring(3));
        [MessageAttribution("GIC")]
        public void Positions(TcpClient client, string message) => client.account.Game.Fight.UpdatePositions(message.Substring(3));
        /// <summary><c>GIE&lt;effet&gt;;&lt;cibles&gt;;…</c> : effet affiché sur un combattant.</summary>
        [MessageAttribution("GIE")]
        public void Effect(TcpClient client, string message) => client.account.Game.Fight.ApplyEffectPacket(message.Substring(3));
        /// <summary><c>GIe</c> : tous les effets sont retirés.</summary>
        [MessageAttribution("GIe")]
        public void ClearEffects(TcpClient client, string message) => client.account.Game.Fight.ClearAllEffects();
        /// <summary><c>GDZ±&lt;cellule&gt;;&lt;taille&gt;;&lt;couleur&gt;</c> : zone de glyphe ou de piège.</summary>
        [MessageAttribution("GDZ")]
        public void Zones(TcpClient client, string message) => client.account.Game.Fight.ApplyZones(message.Substring(3));
        /// <summary><c>Go±&lt;A|S|P|H&gt;&lt;équipe&gt;</c> : option d'une équipe (combats de la carte compris).</summary>
        [MessageAttribution("Go")]
        public void Option(TcpClient client, string message) => client.account.Game.Fight.ApplyFightOption(message.Substring(2));
        /// <summary><c>Gf&lt;combattant&gt;|&lt;cellule&gt;</c> : cellule signalée par un coéquipier.</summary>
        [MessageAttribution("Gf")]
        public void Flag(TcpClient client, string message) => client.account.Game.Fight.ShowFlag(message.Substring(2));
        /// <summary><c>GE&lt;durée&gt;[;&lt;étoiles&gt;]|&lt;initiateur&gt;|&lt;type&gt;|&lt;ligne&gt;…</c> : fin du combat et résultat (matrice §2 n° 23).</summary>
        [MessageAttribution("GE")]
        public void End(TcpClient client, string message) => client.account.Game.Fight.Finish(message.Substring(2));
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
