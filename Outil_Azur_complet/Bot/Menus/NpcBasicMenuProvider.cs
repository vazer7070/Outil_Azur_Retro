using System.Collections.Generic;
using System.Threading.Tasks;
using Outil_Azur_complet.Bot.Controls;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.NPC;

namespace Outil_Azur_complet.Bot.Menus
{
    /// <summary>
    /// Menu provisoire des PNJ, avec les deux actions déjà prises en charge par la couche protocole : « Parler »
    /// (action 3 du client, <c>DC&lt;pnj&gt;</c>, aussi déclenchée par le clic gauche) et « Acheter/Vendre » (action 1,
    /// <c>ER0|&lt;pnj&gt;</c>, à laquelle StarLoco répond <c>ECK0|&lt;pnj&gt;</c> puis <c>EL</c>). Le menu complet des PNJ
    /// (actions déclarées dans les données du client) le remplacera.
    /// </summary>
    [ActorMenuOrder(500)]
    public sealed class NpcBasicMenuProvider : IActorMenuProvider
    {
        public bool Handles(Entites a) => a is PNJ;

        public IEnumerable<MenuEntry> Entries(Entites a, GameClass g)
        {
            var npc = a as PNJ;
            if (npc == null) yield break;
            int id = npc.id;
            yield return new MenuEntry("Parler", context => Run(context, game => game.Interactions.Npc.OpenAsync(id)))
            { ParDéfaut = true, Raccourci = "Clic gauche", Infobulle = "Engager le dialogue (DC)" };
            yield return new MenuEntry("Acheter/Vendre", context => Run(context, game => game.Interactions.Shop.OpenAsync(id)))
            { Icône = ClientAssets.Icon("kamas", 16), Infobulle = "Ouvrir la boutique du PNJ (ER0)" };
        }

        private static async Task<string> Run(ActorMenuContext context, System.Func<GameClass, Task<InteractionResult>> action)
        {
            GameClass game = context?.Game;
            if (game?.Interactions == null) return "La session n’est plus disponible.";
            InteractionResult result = await action(game);
            return result?.Message;
        }
    }
}
