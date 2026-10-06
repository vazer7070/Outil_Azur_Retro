using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Outil_Azur_complet.Bot.Controls;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Guildes;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Interfaces;

namespace Outil_Azur_complet.Bot.Menus
{
    /// <summary>
    /// Entrée de guilde du menu d'un autre joueur de la carte, comme <c>GameManager.getPlayerPopupMenu</c> du client 1.34 :
    /// « Inviter à rejoindre ma guilde » (<c>INVITE_IN_GUILD</c>, <c>gJR&lt;nom&gt;</c>), proposée seulement quand le personnage a une guilde
    /// et le droit d'inviter ; grisée si le joueur affiche déjà une guilde (StarLoco répondrait <c>gJEa</c>). Le registre fusionne
    /// les fournisseurs par ordre : l'entrée forme un groupe après celles des amis.
    /// </summary>
    [ActorMenuOrder(220)]
    public sealed class GuildMenuProvider : IActorMenuProvider
    {
        public bool Handles(Entites a) => a is PlayerActor player && !player.IsSelf;

        public IEnumerable<MenuEntry> Entries(Entites a, GameClass g)
        {
            var player = a as PlayerActor;
            GuildActions guild = g?.Interactions?.Guild;
            if (player == null || guild == null || string.IsNullOrEmpty(player.Name)) yield break;
            if (!guild.HasGuild || !guild.Guild.CanDo(GuildRight.Invite)) yield break;
            string name = player.Name;
            bool free = !player.HasGuild;
            yield return new MenuEntry(GuildTexts.Get("INVITE_IN_GUILD", "Inviter à rejoindre ma guilde"), context => Run(context, actions => actions.InviteAsync(name)))
            {
                Icône = ClientAssets.Icon("icone-guilde", 16), Activé = free,
                Infobulle = free ? "Inviter ce joueur dans votre guilde (gJR" + name + ")" : GuildTexts.Get("GUILD_JOIN_ALREADY_IN_GUILD", "Impossible, ce joueur est déjà dans une guilde")
            };
        }

        private static async Task<string> Run(ActorMenuContext context, Func<GuildActions, Task<InteractionResult>> action)
        {
            GuildActions guild = context?.Game?.Interactions?.Guild;
            if (guild == null) return "La session n’est plus disponible.";
            InteractionResult result = await action(guild);
            return result?.Message;
        }
    }
}
