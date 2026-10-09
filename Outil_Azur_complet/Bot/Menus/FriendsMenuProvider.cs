using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Outil_Azur_complet.Bot.Controls;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.Social;

namespace Outil_Azur_complet.Bot.Menus
{
    /// <summary>
    /// Entrées d'amitié du menu d'un autre joueur de la carte, comme <c>GameManager.getPlayerPopupMenu</c> du client 1.34 (juste
    /// après « Informations ») : « Ajouter à mes amis » (<c>FA&lt;nom&gt;</c>) et « Ajouter à mes ennemis » (<c>iA&lt;nom&gt;</c>).
    /// Le client les propose pour tout joueur hors de la fenêtre Amis, même déjà listé : le serveur répond <c>FAEa</c> / <c>iAEA</c>.
    /// Le registre fusionne les fournisseurs par ordre : ces deux entrées forment un groupe séparé après celles de
    /// <c>PlayerMenuProvider</c>.
    /// </summary>
    [ActorMenuOrder(210)]
    public sealed class FriendsMenuProvider : IActorMenuProvider
    {
        public bool Handles(Entites a) => a is PlayerActor player && !player.IsSelf;

        public IEnumerable<MenuEntry> Entries(Entites a, GameClass g)
        {
            var player = a as PlayerActor;
            if (player == null || g?.Interactions?.Friends == null || string.IsNullOrEmpty(player.Name)) yield break;
            string name = player.Name;
            yield return new MenuEntry(FriendsTexts.Get("ADD_TO_FRIENDS", "Ajouter à mes amis"), context => Run(context, friends => friends.AddFriendAsync(name)))
            { Icône = ClientAssets.Icon("icone-amis", 16), Infobulle = "Ajouter ce joueur à votre liste d'amis (FA" + name + ")" };
            yield return new MenuEntry(FriendsTexts.Get("ADD_TO_ENEMY", "Ajouter à mes ennemis"), context => Run(context, friends => friends.AddEnemyAsync(name)))
            { Icône = ClientAssets.Icon("icone-pvp", 16), Infobulle = "Ajouter ce joueur à votre liste d'ennemis (iA" + name + ")" };
        }

        private static async Task<string> Run(ActorMenuContext context, Func<FriendsActions, Task<InteractionResult>> action)
        {
            FriendsActions friends = context?.Game?.Interactions?.Friends;
            if (friends == null) return "La session n’est plus disponible.";
            InteractionResult result = await action(friends);
            return result?.Message;
        }
    }
}
