using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.Montures;

namespace Outil_Azur_complet.Bot.Menus
{
    /// <summary>
    /// Monture d'enclos (acteur <c>-9</c>), comme <c>onSpriteRelease</c> du client 1.34 : le menu n'existe que dans un enclos de
    /// la guilde du personnage avec le droit « Gérer les montures des autres membres » (<c>canManageOtherMount</c>) ; il porte
    /// « Monture de &lt;propriétaire&gt; » et « Consulter la fiche de la monture » (<c>Rp&lt;id&gt;</c>, réponse <c>Rd</c> affichée par
    /// le volet Monture). Maj + clic envoie <c>Rp&lt;id&gt;</c> sans menu. Hors de ces conditions le client ignore le clic : le bot
    /// ne déplace pas non plus le personnage et indique seulement à qui appartient la monture.
    /// </summary>
    [ActorMenuOrder(260)]
    public sealed class ParkMountMenuProvider : IActorMenuProvider, IActorClickHandler
    {
        public bool Handles(Entites a) => a is ParkMountActor;

        public IEnumerable<MenuEntry> Entries(Entites a, GameClass g)
        {
            var mount = a as ParkMountActor;
            MountActions actions = g?.Interactions?.Mount;
            if (mount == null || actions == null) yield break;
            yield return MenuEntry.Statique(OwnerLine(mount));
            bool allowed = actions.CanManageParkMounts;
            long id = mount.Id;
            yield return new MenuEntry(MountTexts.Get("VIEW_MOUNT_DETAILS", "Consulter la fiche de la monture"), context => View(context, id))
            {
                Icône = ClientAssets.Icon("icone-monture", 16), Activé = allowed, Raccourci = "Maj + clic",
                Infobulle = allowed ? "Fiche de la monture (Rp" + id + ")" : "Réservé aux membres de la guilde de l'enclos ayant le droit « "
                    + MountTexts.Get("GUILD_RIGHTS_MANAGE_OTHER_MOUNT", "Gérer les montures des autres membres") + " »."
            };
        }

        public async Task<bool> OnActorClickAsync(ActorMenuContext context)
        {
            var mount = context?.Actor as ParkMountActor;
            if (mount == null) return false;
            MountActions actions = context.Game?.Interactions?.Mount;
            if (actions == null || !actions.CanManageParkMounts)
            {
                context.Feedback(OwnerLine(mount) + " : " + mount.DisplayName + ", niveau " + mount.Level + ".");
                return true;
            }
            if (context.Shift)
            {
                string message = await View(context, mount.Id);
                context.Feedback(message);
                return true;
            }
            context.Router?.ShowActorMenu(context.ActorsOnCell, context.CellId, context.Modifiers, false);
            return true;
        }

        private static string OwnerLine(ParkMountActor mount)
        {
            string owner = string.IsNullOrEmpty(mount.OwnerName) ? "?" : mount.OwnerName;
            return MountTexts.Get("MOUNT_OF", "Monture de " + owner, owner);
        }

        private static async Task<string> View(ActorMenuContext context, long id)
        {
            MountActions actions = context?.Game?.Interactions?.Mount;
            if (actions == null) return "La session n’est plus disponible.";
            InteractionResult result = await actions.ViewParkMountAsync(id);
            return result?.Message;
        }
    }
}
