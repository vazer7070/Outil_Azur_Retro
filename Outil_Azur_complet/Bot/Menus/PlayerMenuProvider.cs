using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Outil_Azur_complet.Bot.Controls;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Jobs;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Interfaces;

namespace Outil_Azur_complet.Bot.Menus
{
    /// <summary>
    /// Menu d'un autre joueur, dans l'ordre de <c>GameManager.getPlayerPopupMenu</c> du client 1.34 : « Informations »
    /// (<c>BW&lt;nom&gt;</c>), « Message privé » (console <c>/w &lt;nom&gt; </c>), « Inviter dans le groupe » (<c>PI&lt;nom&gt;</c>),
    /// « Échanger » (<c>ER1|&lt;id&gt;</c>, si les deux joueurs peuvent échanger), « Défier » (<c>GA900&lt;id&gt;</c>, si le joueur
    /// peut être défié ; grisé si la carte l'interdit) et « Agresser » (confirmation puis <c>GA906&lt;id&gt;</c>, si l'alignement
    /// le permet ; grisé si la carte l'interdit). Amis, ennemis et guilde sont ajoutés par leurs propres fournisseurs.
    /// Artisanat sécurisé (lot X6) : si ce joueur a annoncé des compétences d'atelier (<c>EW+&lt;id&gt;|&lt;compétences&gt;</c>),
    /// une entrée « Demander à &lt;compétence&gt; » par compétence (<c>ER13|&lt;id&gt;|&lt;compétence&gt;</c>) ; sinon, si son propre
    /// personnage en a annoncé, une entrée « Inviter à &lt;compétence&gt; » par compétence (<c>ER12|&lt;id&gt;|&lt;compétence&gt;</c>).
    /// Comme <c>onSpriteRelease</c>, un clic gauche sur un autre joueur hors combat ouvre ce menu (Ctrl : un sous-menu par
    /// joueur de la cellule) ; en combat, le clic va à la cellule.
    /// </summary>
    [ActorMenuOrder(200)]
    public sealed class PlayerMenuProvider : IActorMenuProvider, IActorClickHandler
    {
        public bool Handles(Entites a) => a is PlayerActor player && !player.IsSelf;

        public IEnumerable<MenuEntry> Entries(Entites a, GameClass g)
        {
            var player = a as PlayerActor;
            MapActions actions = g?.Interactions?.MapActions;
            if (player == null || actions == null || string.IsNullOrEmpty(player.Name)) yield break;
            long id = player.Id;
            string name = player.Name;
            yield return new MenuEntry(MapActionTexts.Whois, context => Run(context, map => map.WhoisAsync(name)))
            { Icône = ClientAssets.Icon("icone-carte", 16), Infobulle = "Demander au serveur où se trouve ce joueur (BW)" };
            yield return new MenuEntry(MapActionTexts.PrivateMessage, context => Task.FromResult(
                context?.Game?.Interactions?.MapActions?.RequestPrivateMessage(name)?.Message ?? "La session n’est plus disponible."))
            { Infobulle = "Préparer « /w " + name + " » dans la console de discussion" };
            yield return new MenuEntry(MapActionTexts.InviteToParty, context => Run(context, map => map.InviteToPartyAsync(name)))
            { Icône = ClientAssets.Icon("icone-amis", 16), Infobulle = "Inviter ce joueur dans votre groupe (PI)" };
            if (actions.CanExchangeWith(player))
                yield return new MenuEntry(MapActionTexts.Exchange, context => Run(context, map => map.RequestExchangeAsync(id)))
                { Icône = ClientAssets.Icon("kamas", 16), Infobulle = "Proposer un échange (ER1)" };
            if (actions.CanChallengePlayer(player))
                yield return new MenuEntry(MapActionTexts.Challenge, context => Run(context, map => map.ChallengeAsync(id)))
                {
                    Icône = ClientAssets.Icon("icone-pvp", 16), Activé = g.Map.CanChallenge,
                    Infobulle = g.Map.CanChallenge ? "Proposer un duel (GA900)" : "Les duels sont interdits sur cette carte"
                };
            if (actions.CanAssaultPlayer(player))
                yield return new MenuEntry(MapActionTexts.Assault, context => AskAssault(context, id))
                {
                    Icône = ClientAssets.Icon("alerte", 16), Activé = g.Map.CanAttack,
                    Infobulle = g.Map.CanAttack ? "Agresser ce joueur après confirmation (GA906)" : "Les agressions sont interdites sur cette carte"
                };
            foreach (MenuEntry entry in SecureCraftEntries(player, g, actions)) yield return entry;
        }

        /// <summary>
        /// Entrées de l'artisanat sécurisé, après « Agresser » comme dans le client : les compétences annoncées par
        /// <c>EW+</c> pour ce joueur (« Demander à … », <c>ER13</c>), sinon celles de son propre personnage (« Inviter à … »,
        /// <c>ER12</c>). Aucune en combat ni sans personnage.
        /// </summary>
        internal static IEnumerable<MenuEntry> SecureCraftEntries(PlayerActor player, GameClass g, MapActions actions)
        {
            if (player == null || actions == null || g?.character == null || g.Fight?.IsInFight == true) yield break;
            long id = player.Id;
            IReadOnlyList<short> theirs = actions.PublicSkillsOf(id);
            if (theirs.Count > 0)
            {
                foreach (short skill in theirs)
                {
                    short chosen = skill;
                    yield return new MenuEntry(MapActionTexts.AskTo(JobCatalog.SkillName(skill)), context => Run(context, map => map.RequestSecureCraftAsync(id, chosen)))
                    { Infobulle = "Demander à cet artisan de fabriquer pour vous (ER13|" + id + "|" + skill + ")" };
                }
                yield break;
            }
            foreach (short skill in actions.PublicSkillsOf(g.character.id))
            {
                short chosen = skill;
                yield return new MenuEntry(MapActionTexts.InviteTo(JobCatalog.SkillName(skill)), context => Run(context, map => map.InviteToSecureCraftAsync(id, chosen)))
                { Infobulle = "Inviter ce joueur à votre atelier (ER12|" + id + "|" + skill + ")" };
            }
        }

        /// <summary>
        /// Clic gauche sur un autre joueur (<c>onSpriteRelease</c>) : en combat, rien ici (le clic va à la cellule) ; sinon son
        /// menu s'ouvre sous la souris, et avec Ctrl un sous-menu par joueur de la cellule (<c>showCellPlayersPopupMenu</c>).
        /// </summary>
        public Task<bool> OnActorClickAsync(ActorMenuContext context)
        {
            if (context?.Router == null || !(context.Actor is PlayerActor player) || player.IsSelf) return Task.FromResult(false);
            if (context.Game?.Fight?.IsInFight == true) return Task.FromResult(false);
            IReadOnlyList<Entites> actors;
            if (context.Ctrl)
            {
                var players = new List<Entites> { player };
                players.AddRange(context.ActorsOnCell.Where(actor => actor is PlayerActor && !ReferenceEquals(actor, player)));
                actors = players;
            }
            else actors = context.ActorsOnCell.Count > 0 && ReferenceEquals(context.ActorsOnCell[0], player) ? context.ActorsOnCell : new Entites[] { player };
            context.Router.ShowActorMenu(actors, context.CellId, context.Modifiers, context.Ctrl);
            return Task.FromResult(true);
        }

        /// <summary><c>GameManager.askAttack</c> : avertissements puis <c>DO_U_ATTACK</c> ; « Oui » envoie <c>GA906&lt;id&gt;</c>.</summary>
        private static async Task<string> AskAssault(ActorMenuContext context, long id)
        {
            GameClass game = context?.Game;
            MapActions actions = game?.Interactions?.MapActions;
            var target = game?.Map?.GetActor(id) as PlayerActor;
            if (actions == null) return "La session n’est plus disponible.";
            if (target == null) return "Ce joueur n’est plus sur la carte.";
            bool pvpDisabled = !(game.character?.stats?.HasWings ?? false);
            bool neutral = (target.Alignment?.Grade ?? 0) == 0;
            BotDialogResult answer = await BotDialogs.AskYesNoAsync(context.Panels, MapActionTexts.Assault,
                MapActionTexts.AskAttack(target.DisplayName, pvpDisabled, neutral));
            if (answer != BotDialogResult.Yes) return null;
            InteractionResult result = await actions.AssaultAsync(id);
            return result?.Message;
        }

        internal static async Task<string> Run(ActorMenuContext context, Func<MapActions, Task<InteractionResult>> action)
        {
            MapActions actions = context?.Game?.Interactions?.MapActions;
            if (actions == null) return "La session n’est plus disponible.";
            InteractionResult result = await action(actions);
            return result?.Message;
        }
    }
}
