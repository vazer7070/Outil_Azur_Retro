using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Interfaces;

namespace Outil_Azur_complet.Bot.Menus
{
    /// <summary>
    /// Épées d'un combat de la carte (<c>Gc+</c>) : « Rejoindre » par équipe (<c>GA903&lt;combat&gt;;&lt;équipe&gt;</c>), active
    /// selon les règles du clic sur une épée du client (type de combat, type d'équipe, tables d'alignement <c>A.at</c>/<c>A.jo</c>),
    /// « Spectateur » (<c>GA903&lt;combat&gt;</c>) et le volet des combats de la carte. Maj + clic sur l'épée d'une équipe
    /// rejoignable la rejoint directement, comme le client ; sinon le clic ouvre ce menu.
    /// </summary>
    [ActorMenuOrder(160)]
    public sealed class FightSwordsMenuProvider : IActorMenuProvider, IActorClickHandler
    {
        public bool Handles(Entites a) => a is FightSwordsActor;

        public IEnumerable<MenuEntry> Entries(Entites a, GameClass g)
        {
            var swords = a as FightSwordsActor;
            MapActions actions = g?.Interactions?.MapActions;
            if (swords == null || actions == null) yield break;
            long fightId = swords.FightId;
            yield return MenuEntry.Statique(FightTypeName(swords.FightType));
            int number = 0;
            foreach (FightTeamFlag team in swords.Teams)
            {
                number++;
                long teamId = team.TeamId;
                bool joinable = actions.CanJoinTeam(swords, team);
                yield return new MenuEntry(MapActionTexts.Join + " · " + MapActionTexts.Team + " " + number + TeamKind(team.TeamType),
                    context => PlayerMenuProvider.Run(context, map => map.JoinFightAsync(fightId, teamId)))
                {
                    Icône = ClientAssets.Icon("icone-pvp", 16), Activé = joinable, Raccourci = joinable ? "Maj + clic" : null,
                    Infobulle = joinable ? "Rejoindre cette équipe (GA903)" : "Cette équipe n’accepte pas votre personnage"
                };
            }
            yield return new MenuEntry(MapActionTexts.Spectator, context => PlayerMenuProvider.Run(context, map => map.SpectateAsync(fightId)))
            { Icône = ClientAssets.Icon("drapeau", 16), Infobulle = "Regarder le combat sans y participer (GA903)" };
            yield return new MenuEntry(MapActionTexts.CurrentFights + "…", context => Task.FromResult(OpenList(context)))
            { Infobulle = "Liste des combats de la carte (fL)" };
        }

        public async Task<bool> OnActorClickAsync(ActorMenuContext context)
        {
            var swords = context?.Actor as FightSwordsActor;
            if (swords == null) return false;
            MapActions actions = context.Game?.Interactions?.MapActions;
            FightTeamFlag team = swords.Teams.FirstOrDefault(entry => entry.CellId == context.CellId);
            if (context.Shift && team != null && actions != null && actions.CanJoinTeam(swords, team))
            {
                InteractionResult result = await actions.JoinFightAsync(swords.FightId, team.TeamId);
                context.Feedback(result?.Message);
                return true;
            }
            context.Router?.ShowActorMenu(context.ActorsOnCell, context.CellId, context.Modifiers, false);
            return true;
        }

        private static string OpenList(ActorMenuContext context)
        {
            PanelHost host = context?.Panels;
            if (host == null || host.IsDisposed) return "Le tiroir des volets n’est pas disponible.";
            host.Show(host.Get<FightsListPanel>() ?? new FightsListPanel());
            return null;
        }

        /// <summary>Types de combat de StarLoco (<c>Constant.FIGHT_TYPE_*</c>), premier champ de <c>Gc+</c>.</summary>
        internal static string FightTypeName(int type)
        {
            switch (type)
            {
                case 0: return "Duel";
                case 1: return "Agression";
                case 2: return "Conquête (prisme)";
                case 3: return "Dopeul de temple";
                case 4: return "Combat contre des monstres";
                case 5: return "Attaque de percepteur";
                case 6: return "Combat à enjeux";
                default: return "Combat (type " + type + ")";
            }
        }

        private static string TeamKind(int teamType)
        {
            switch (teamType)
            {
                case 0: return " (joueurs)";
                case 1: return " (monstres)";
                case 3: return " (percepteur)";
                default: return string.Empty;
            }
        }
    }
}
