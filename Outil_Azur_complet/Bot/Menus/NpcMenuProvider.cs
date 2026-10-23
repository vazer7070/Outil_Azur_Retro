using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Outil_Azur_complet.Bot.Controls;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.NPC;

namespace Outil_Azur_complet.Bot.Menus
{
    /// <summary>
    /// Menu des PNJ comme <c>onSpriteRelease</c> du client 1.34 : le clic gauche ouvre le menu des actions déclarées par les
    /// textes du client pour le modèle du PNJ (<c>N.d[modèle].a</c>, dans cet ordre, noms <c>N.a</c>) ; Maj + clic lance
    /// directement « Parler » (<c>DC&lt;pnj&gt;</c>) ; rien ne se passe si <c>AR</c> interdit de parler aux PNJ.
    /// Actions : 1 → <c>ER0|&lt;pnj&gt;</c>, 2 → <c>ER2</c>, 3 → <c>DC</c>, 4 → <c>ER9</c>, 5 → <c>ER10</c>, 6 → <c>ER11</c>,
    /// 7 → <c>ER17</c> ; l'action 8 (<c>ER18</c>) est masquée car StarLoco ne la traite pas. Sans textes du client
    /// (ou pour un modèle qu'ils ne connaissent pas), menu de repli « Parler » et « Acheter/Vendre ».
    /// </summary>
    [ActorMenuOrder(500)]
    public sealed class NpcMenuProvider : IActorMenuProvider, IActorClickHandler
    {
        public bool Handles(Entites a) => a is PNJ;

        /// <summary>Actions affichées pour ce PNJ, dans l'ordre du client, sans l'action 8.</summary>
        public static IReadOnlyList<int> ActionsFor(Entites actor, out bool fromClientTexts)
        {
            fromClientTexts = false;
            if (!(actor is PNJ npc)) return new int[0];
            int[] actions = NpcActions.ForTemplate(TemplateOf(npc), out fromClientTexts);
            return actions.Where(action => action != NpcActions.MountExchange).Distinct().ToArray();
        }

        public IEnumerable<MenuEntry> Entries(Entites a, GameClass g)
        {
            var npc = a as PNJ;
            if (npc == null) return new MenuEntry[0];
            int id = npc.id;
            bool blocked = g?.Interactions?.Npc?.CannotSpeakToNpc == true;
            var entries = new List<MenuEntry>();
            foreach (int action in ActionsFor(npc, out bool fromClientTexts))
            {
                int chosen = action;
                var entry = new MenuEntry(NpcActions.Label(action), context => Run(context, game => game.Interactions.Npc.RequestActionAsync(chosen, id)))
                { Infobulle = Describe(action, id) };
                if (action == NpcActions.Talk) { entry.ParDéfaut = true; entry.Raccourci = "Maj + clic"; }
                if (action == NpcActions.BuySell) entry.Icône = ClientAssets.Icon("kamas", 16);
                if (!NpcActions.ServerHandles(action)) { entry.Activé = false; entry.Infobulle = "Action sans effet dans le client 1.34."; }
                if (blocked) { entry.Activé = false; entry.Infobulle = "Votre personnage ne peut pas parler aux PNJ pour le moment."; }
                if (!fromClientTexts && entry.Activé) entry.Infobulle += " · menu de repli : actions du PNJ inconnues (npc.xml)";
                entries.Add(entry);
            }
            return entries;
        }

        /// <summary>Clic gauche sur un PNJ : Maj + clic parle directement, sinon le menu des actions s'ouvre sous la souris.</summary>
        public async Task<bool> OnActorClickAsync(ActorMenuContext context)
        {
            if (!(context?.Actor is PNJ npc)) return false;
            NpcDialog dialog = context.Game?.Interactions?.Npc;
            if (dialog == null) { context.Feedback("La session n’est plus disponible."); return true; }
            if (dialog.CannotSpeakToNpc) { context.Feedback("Votre personnage ne peut pas parler aux PNJ pour le moment."); return true; }
            IReadOnlyList<int> actions = ActionsFor(npc, out _);
            if (context.Shift && actions.Contains(NpcActions.Talk))
            {
                InteractionResult result = await dialog.OpenAsync(npc.id);
                context.Feedback(result?.Message);
                return true;
            }
            if (actions.Count == 0) { context.Feedback(ActorClassifier.DisplayName(npc) + " ne propose aucune action."); return true; }
            if (context.Router == null) return false;
            IReadOnlyList<Entites> actors = context.ActorsOnCell.Count > 0 && ReferenceEquals(context.ActorsOnCell[0], npc)
                ? context.ActorsOnCell : new Entites[] { npc };
            context.Router.ShowActorMenu(actors, context.CellId, context.Modifiers, false);
            return true;
        }

        private static int TemplateOf(PNJ npc) => npc is NpcActor actor ? actor.TemplateId : npc.NPc_ID;

        private static string Describe(int action, int npcId)
        {
            if (action == NpcActions.Talk) return "Engager le dialogue (DC" + npcId + ")";
            int? type = NpcActions.ExchangeType(action);
            if (type == null) return null;
            string packet = "ER" + type.Value + "|" + npcId;
            if (action == NpcActions.BuySell) return "Ouvrir la boutique du PNJ (" + packet + ")";
            return "Envoie " + packet + " ; le volet de cet échange n’existe pas encore dans le bot";
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
