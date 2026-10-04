using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Interfaces;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Interfaces;

namespace Outil_Azur_complet.Bot.Menus
{
    /// <summary>
    /// Groupe de monstres : « Attaquer » marche jusqu'à la cellule du groupe (<c>onCellRelease</c> du client) et le serveur
    /// lance le combat à l'arrivée ; suivent le niveau total et les monstres du groupe. Clic gauche : ce menu, comme la
    /// bulle « Attaquer » du client avec l'option <c>ViewAllMonsterInGroup</c> (active par défaut) ; Maj + clic ou groupe
    /// posé sur une cellule de changement de carte : attaque directe, comme le client.
    /// </summary>
    [ActorMenuOrder(150)]
    public sealed class MonsterGroupMenuProvider : IActorMenuProvider, IActorClickHandler
    {
        public bool Handles(Entites a) => a is MonsterGroupActor;

        public IEnumerable<MenuEntry> Entries(Entites a, GameClass g)
        {
            var group = a as MonsterGroupActor;
            if (group == null) yield break;
            long id = group.Id;
            yield return new MenuEntry(MapActionTexts.Attack, context => Attack(context, id))
            {
                Icône = ClientAssets.Icon("icone-pvp", 16), Raccourci = "Maj + clic",
                Infobulle = "Marcher jusqu’au groupe : le serveur lance le combat à l’arrivée"
            };
            IReadOnlyList<MonsterGroupMember> members = group.Members;
            if (members.Count == 0) yield break;
            yield return MenuEntry.Séparateur();
            yield return MenuEntry.Statique(MapActionTexts.Level + " " + group.TotalLevel);
            string level = MapActionTexts.Get("LEVEL_SMALL", "niv.");
            foreach (MonsterGroupMember member in members.OrderByDescending(member => member.Level))
                yield return MenuEntry.Statique(member.Name + " (" + level + " " + member.Level + ")");
        }

        public async Task<bool> OnActorClickAsync(ActorMenuContext context)
        {
            var group = context?.Actor as MonsterGroupActor;
            if (group == null) return false;
            bool trigger = context.Game?.Map?.Triggers?.ContainsKey(group.CellId) == true;
            if (context.Shift || trigger)
            {
                string message = await Attack(context, group.Id);
                if (!string.IsNullOrEmpty(message)) context.Feedback(message);
                return true;
            }
            context.Router?.ShowActorMenu(context.ActorsOnCell, context.CellId, context.Modifiers, false);
            return true;
        }

        /// <summary>Déplacement vers la cellule du groupe par le routeur (événement <c>MoveRequested</c>, puis l'action de la carte).</summary>
        private static async Task<string> Attack(ActorMenuContext context, long groupId)
        {
            MapActions actions = context?.Game?.Interactions?.MapActions;
            InteractionRouter router = context?.Router;
            if (actions == null || router == null) return "La session n’est plus disponible.";
            InteractionResult result = await actions.AttackGroupAsync(groupId, cell => router.RequestMoveAsync(cell, Keys.None));
            // Le déplacement affiche déjà son propre résultat dans le bandeau.
            return result.Sent ? null : result.Message;
        }
    }
}
