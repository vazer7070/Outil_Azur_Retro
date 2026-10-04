using System.Globalization;
using System.Threading.Tasks;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Combats et duels de la carte (lot M4) : <c>fC</c>, <c>fL</c>, <c>fD</c> (<c>dofus.aks.Fights</c>), <c>BWK</c>/<c>BWE</c>
    /// (<c>Basics.onWhoIs</c>) et les actions de jeu hors combat <c>GA;900</c> à <c>GA;909</c> (<c>GameActions.onActions</c>),
    /// enregistrées dans <see cref="GameActionRouter"/>. L'état vit dans <see cref="MapActions"/>. Un paquet illisible est
    /// journalisé et ignoré.
    /// </summary>
    internal class FightsFrame : Frame
    {
        [MessageAttribution("fC")]
        public void FightCount(TcpClient client, string message) => Actions(client)?.OnFightCount(message.Substring(2));

        [MessageAttribution("fL")]
        public void FightList(TcpClient client, string message) => Actions(client)?.OnFightList(message.Substring(2));

        [MessageAttribution("fD")]
        public void FightDetails(TcpClient client, string message) => Actions(client)?.OnFightDetails(message.Substring(2));

        [MessageAttribution("BWK")]
        public void Whois(TcpClient client, string message) => Actions(client)?.OnWhois(message.Substring(3));

        /// <summary><c>BWE&lt;nom&gt;</c> : <c>onWhoIs(false, nom)</c> du client (StarLoco répond plutôt <c>PIEn&lt;nom&gt;</c>).</summary>
        [MessageAttribution("BWE")]
        public void WhoisError(TcpClient client, string message) => Actions(client)?.ReportWhoisNotFound(message.Substring(3));

        /// <summary><c>GA;900;&lt;demandeur&gt;;&lt;cible&gt;</c>.</summary>
        [GameActionHandler(MapActions.ChallengeAction)]
        private static Task Challenge(GameActionContext context)
        {
            MapActions actions = Actions(context);
            if (actions == null || !TryPlayers(context, out long first, out long second)) return Task.CompletedTask;
            return actions.OnChallengeAsked(first, second);
        }

        /// <summary><c>GA;901;&lt;demandeur&gt;;&lt;cible&gt;</c> : duel accepté.</summary>
        [GameActionHandler(MapActions.AcceptChallengeAction)]
        private static void ChallengeAccepted(GameActionContext context)
        {
            MapActions actions = Actions(context);
            if (actions != null && TryPlayers(context, out long first, out long second)) actions.OnChallengeAccepted(first, second);
        }

        /// <summary><c>GA;902;&lt;joueur&gt;;&lt;joueur&gt;</c> : duel refusé ou annulé.</summary>
        [GameActionHandler(MapActions.RefuseChallengeAction)]
        private static void ChallengeCancelled(GameActionContext context)
        {
            MapActions actions = Actions(context);
            if (actions != null && TryPlayers(context, out long first, out long second)) actions.OnChallengeCancelled(first, second);
        }

        /// <summary><c>GA;903;&lt;id&gt;;&lt;code&gt;</c> : refus d'un duel ou d'un combat (code d'une lettre).</summary>
        [GameActionHandler(MapActions.JoinFightAction)]
        private static void JoinRefused(GameActionContext context) => Actions(context)?.OnJoinRefused(context.Parameters);

        /// <summary><c>GA;906;&lt;agresseur&gt;;&lt;cible&gt;</c>.</summary>
        [GameActionHandler(MapActions.AssaultAction)]
        private static void Assault(GameActionContext context)
        {
            MapActions actions = Actions(context);
            if (actions != null && TryPlayers(context, out long first, out long second)) actions.OnAssault(first, second);
        }

        /// <summary><c>GA;909;&lt;joueur&gt;;&lt;percepteur&gt;</c> (StarLoco l'envoie aussi pour un prisme).</summary>
        [GameActionHandler(MapActions.AttackCollectorAction)]
        private static void CollectorAttack(GameActionContext context)
        {
            MapActions actions = Actions(context);
            if (actions != null && TryPlayers(context, out long first, out long second)) actions.OnCollectorAttack(first, second);
        }

        private static MapActions Actions(TcpClient client) => client?.account?.Game?.Interactions?.MapActions;
        private static MapActions Actions(GameActionContext context) => context?.Account?.Game?.Interactions?.MapActions;

        /// <summary>Acteur et paramètre numériques ; sinon le paquet est journalisé et ignoré.</summary>
        private static bool TryPlayers(GameActionContext context, out long first, out long second)
        {
            second = 0;
            first = context.Packet.ActorId ?? 0;
            string parameter = (context.Parameters ?? string.Empty).Split(';')[0];
            if (context.Packet.ActorId.HasValue && long.TryParse(parameter, NumberStyles.Integer, CultureInfo.InvariantCulture, out second)) return true;
            context.Account?.Logger?.LogError("COMBATS", "Action de jeu illisible ignorée : " + Shorten(context.Packet.Raw));
            return false;
        }

        private static string Shorten(string value) => value == null ? string.Empty : value.Length <= 80 ? value : value.Substring(0, 80) + "…";
    }
}
