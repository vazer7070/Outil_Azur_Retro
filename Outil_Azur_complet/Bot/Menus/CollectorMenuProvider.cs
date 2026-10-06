using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Outil_Azur_complet.Bot.Controls;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Guildes;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Interfaces;

namespace Outil_Azur_complet.Bot.Menus
{
    /// <summary>
    /// Menu d'un percepteur, comme <c>GameManager.getTaxCollectorPopupMenu</c> du client 1.34 : « Parler » (<c>DC&lt;id&gt;</c>, StarLoco
    /// répond <c>DCK</c> et <c>DQ1;&lt;guilde&gt;,&lt;pods&gt;,&lt;prospection&gt;,&lt;sagesse&gt;,&lt;percepteurs&gt;</c>), puis pour un percepteur de sa
    /// propre guilde « Relever la collecte et retirer le percepteur » (<c>ER8|&lt;id&gt;</c>, droit 512) et « Retirer » (<c>gF&lt;id&gt;</c>
    /// après <c>DO_U_REMOVE_TAXCOLLECTOR</c>, droit 128), et « Attaquer » (<c>GA909&lt;id&gt;</c>, lot M4), grisé pour un percepteur de
    /// sa guilde comme dans le client et quand <c>AR</c> interdit les percepteurs. Rejoindre ou quitter la défense (<c>gTJ</c> / <c>gTV</c>)
    /// n'est pas proposé : StarLoco ne retrouve jamais le percepteur (matrice §2 n° 37).
    /// </summary>
    [ActorMenuOrder(240)]
    public sealed class CollectorMenuProvider : IActorMenuProvider
    {
        public bool Handles(Entites a) => a is CollectorActor;

        public IEnumerable<MenuEntry> Entries(Entites a, GameClass g)
        {
            var collector = a as CollectorActor;
            GuildActions guild = g?.Interactions?.Guild;
            MapActions actions = g?.Interactions?.MapActions;
            if (collector == null || guild == null || actions == null) yield break;
            long id = collector.Id;
            bool own = guild.IsOwnCollector(collector);
            bool blocked = g.Interactions.Npc?.CannotSpeakToNpc == true;
            yield return new MenuEntry(GuildTexts.Get("SPEAK", "Parler"), context => Run(context, game => game.Interactions.Npc.OpenAsync(unchecked((int)id))))
            {
                ParDéfaut = true, Activé = !blocked,
                Infobulle = blocked ? "Votre personnage ne peut pas parler aux PNJ pour le moment." : "Guilde, pods, prospection, sagesse et nombre de percepteurs (DC" + id + ")"
            };
            if (own)
            {
                bool canCollect = guild.CanCollect(collector);
                yield return new MenuEntry(GuildTexts.Get("COLLECT_TAX", "Relever la collecte et retirer le percepteur"), context => Run(context, game => game.Interactions.Guild.CollectAsync(id)))
                {
                    Icône = ClientAssets.Icon("kamas", 16), Activé = canCollect,
                    Infobulle = canCollect ? "Ouvrir le contenu du percepteur (ER8|" + id + ") ; fermer la fenêtre relève la collecte et retire le percepteur"
                        : GuildTexts.Get("NOT_ENOUGHT_RIGHTS_FROM_GUILD", "Tu n'as pas les droits suffisant dans ta guilde pour réaliser cette action.")
                };
            }
            bool canAttack = !own && actions.CanAttackCollector(collector);
            yield return new MenuEntry(GuildTexts.Get("ATTACK", "Attaquer"), context => Run(context, game => game.Interactions.MapActions.AttackCollectorAsync(id)))
            {
                Icône = ClientAssets.Icon("icone-pvp", 16), Activé = canAttack,
                Infobulle = own ? GuildTexts.Get("NOT_YOUR_TAXCOLLECTORS", "Ce percepteur n'appartient pas à votre guilde.").Replace("n'appartient pas à", "appartient à")
                    : canAttack ? "Attaquer ce percepteur (GA909" + id + ")" : "Votre personnage ne peut pas interagir avec les percepteurs."
            };
            if (own)
            {
                bool canRemove = guild.CanRemoveCollector(collector);
                yield return new MenuEntry(GuildTexts.Get("REMOVE", "Retirer"), context => AskRemove(context, id, collector.DisplayName))
                {
                    Activé = canRemove,
                    Infobulle = canRemove ? "Retirer ce percepteur après confirmation (gF" + id + ")"
                        : GuildTexts.Get("NOT_ENOUGHT_RIGHTS_FROM_GUILD", "Tu n'as pas les droits suffisant dans ta guilde pour réaliser cette action.")
                };
            }
        }

        /// <summary><c>DO_U_REMOVE_TAXCOLLECTOR</c> puis <c>gF&lt;id&gt;</c>.</summary>
        private static async Task<string> AskRemove(ActorMenuContext context, long id, string name)
        {
            GuildActions guild = context?.Game?.Interactions?.Guild;
            if (guild == null) return "La session n’est plus disponible.";
            if (context.Panels != null && !context.Panels.IsDisposed)
            {
                BotDialogResult answer = await BotDialogs.AskYesNoAsync(context.Panels, GuildTexts.Get("GUILD", "Guilde"),
                    GuildTexts.Get("DO_U_REMOVE_TAXCOLLECTOR", "Etes vous sur de vouloir retirer le percepteur {0} ?", name));
                if (answer != BotDialogResult.Yes) return "Retrait du percepteur annulé.";
            }
            InteractionResult result = await guild.RemoveCollectorAsync(id);
            return result?.Message;
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
