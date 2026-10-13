using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Outil_Azur_complet.Bot.Controls;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Alignement;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Interfaces;

namespace Outil_Azur_complet.Bot.Menus
{
    /// <summary>
    /// Menu d'un prisme (acteur <c>-10</c>), comme le menu des prismes du client 1.34 : « Utiliser » (<c>GA512&lt;id&gt;</c>, actif
    /// pour un prisme de son camp, action par défaut du clic gauche comme <c>usePrism</c>) et « Attaquer » (<c>GA912&lt;id&gt;</c>, lot M4,
    /// actif pour un personnage aligné face à un prisme d'un autre camp, grisé quand <c>AR</c> interdit les prismes). Un personnage neutre
    /// n'a aucune entrée active : StarLoco ignore ses demandes.
    /// </summary>
    [ActorMenuOrder(250)]
    public sealed class PrismMenuProvider : IActorMenuProvider
    {
        public bool Handles(Entites a) => a is PrismActor;

        public IEnumerable<MenuEntry> Entries(Entites a, GameClass g)
        {
            var prism = a as PrismActor;
            AlignmentActions alignment = g?.Interactions?.Alignment;
            MapActions actions = g?.Interactions?.MapActions;
            if (prism == null || alignment == null || actions == null) yield break;
            long id = prism.Id;
            bool canUse = alignment.CanUsePrism(prism);
            bool neutral = !alignment.Alignment.IsAligned;
            yield return new MenuEntry(AlignmentTexts.Get("USE_WORD", "Utiliser"), context => Run(context, game => game.Interactions.Alignment.UsePrismAsync(id)))
            {
                ParDéfaut = canUse, Activé = canUse,
                Infobulle = canUse ? "Liste des prismes de votre camp (GA512" + id + ")" : neutral ? "Un personnage neutre ne peut pas utiliser un prisme." : "Ce prisme appartient à un autre camp."
            };
            bool canAttack = actions.CanAttackPrism(prism);
            yield return new MenuEntry(AlignmentTexts.Get("ATTACK", "Attaquer"), context => Run(context, game => game.Interactions.MapActions.AttackPrismAsync(id)))
            {
                Icône = ClientAssets.Icon("icone-pvp", 16), Activé = canAttack,
                Infobulle = canAttack ? "Attaquer ce prisme (GA912" + id + ")" : neutral ? "Un personnage neutre n'attaque pas les prismes."
                    : prism.AlignmentSide == alignment.Alignment.Side ? "Ce prisme est celui de votre camp." : "Votre personnage ne peut pas interagir avec les prismes."
            };
        }

        private static async Task<string> Run(ActorMenuContext context, Func<GameClass, Task<InteractionResult>> action)
        {
            GameClass game = context?.Game;
            if (game?.Interactions == null) return "La session n’est plus disponible.";
            InteractionResult result = await action(game);
            return result?.Message;
        }
    }
}
