using System;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Combats;

namespace Tool_BotProtocol.Game.Maps
{
    /// <summary>
    /// Effets visuels de la carte hors combat (lot AN1) : StarLoco envoie <c>GA;208</c> (ballons magiques) et <c>GA;228</c>
    /// (feux d'artifice, table <c>animations</c>) seulement hors combat, au format
    /// <c>&lt;cellule&gt;,&lt;fichier&gt;,&lt;type&gt;,&lt;animation&gt;,&lt;niveau&gt;</c> (voir <see cref="SpellLaunch.TryParseMapEffect"/>).
    /// En combat, ces actions passent par <c>FightActionTable</c> et <see cref="Fights.VisualEvent"/>.
    /// </summary>
    public partial class Map
    {
        /// <summary>Effet visuel hors combat (208, 228), levé sur le fil réseau, hors de tout verrou.</summary>
        public event Action<VisualEvent> VisualEvent;

        internal void RaiseVisual(VisualEvent visual, Accounts.Accounts account) => VisualEvents.Raise(VisualEvent, visual, account, "CARTE");
    }

    /// <summary>Gestionnaires hors combat de <c>GA;208</c> et <c>GA;228</c> (auparavant sans gestionnaire, rangés dans <c>Unhandled</c>).</summary>
    internal static class MapVisualActions
    {
        [GameActionHandler(208)]
        private static void OnBalloon(GameActionContext context) => Raise(context);

        [GameActionHandler(228)]
        private static void OnFirework(GameActionContext context) => Raise(context);

        private static void Raise(GameActionContext context)
        {
            Accounts.Accounts account = context?.Account;
            Map map = account?.Game?.Map;
            if (map == null || context.Packet == null) return;
            string[] fields = (context.Parameters ?? string.Empty).Split(',');
            long actor = context.Packet.ActorId ?? (context.Packet.Actor.Length == 0 ? account.Game.character?.id ?? 0 : long.MinValue);
            if (actor == long.MinValue || !SpellLaunch.TryParseMapEffect(fields, out SpellLaunch launch))
            {
                string raw = context.Packet.Raw ?? string.Empty;
                account.Logger?.LogDebug("CARTE", "Effet GA;" + context.ActionId + " illisible ignoré : " + (raw.Length > 80 ? raw.Substring(0, 80) + "…" : raw));
                return;
            }
            map.RaiseVisual(new VisualEvent(VisualSource.GameAction, context.ActionId, actor, 0, launch.CellId, fields,
                inFight: false, gameActionId: context.Packet.GameActionId), account);
        }
    }
}
