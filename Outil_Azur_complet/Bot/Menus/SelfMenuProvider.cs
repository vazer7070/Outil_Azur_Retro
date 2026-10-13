using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Interfaces;

namespace Outil_Azur_complet.Bot.Menus
{
    /// <summary>
    /// Haut du menu de son propre personnage (<c>GameManager.getPlayerPopupMenu</c> du client 1.34, branche « soi ») :
    /// « Se frapper » (<c>HIT_HIMSELF</c>), qui dit la phrase <c>SLAP_SENTENCE</c> dans le canal général (<c>BM*|…|</c>),
    /// combat compris. Les entrées du magasin suivent (<see cref="MerchantMenuProvider"/>, ordre 250), puis celles de
    /// <see cref="SelfStateMenuProvider"/>. Comme <c>onSpriteRelease</c>, un clic gauche sur son personnage hors combat ouvre
    /// ce menu (Ctrl : un sous-menu par joueur de la cellule).
    /// </summary>
    [ActorMenuOrder(240)]
    public sealed class SelfMenuProvider : IActorMenuProvider, IActorClickHandler
    {
        public bool Handles(Entites a) => a.IsSelfActor();

        public IEnumerable<MenuEntry> Entries(Entites a, GameClass g)
        {
            if (g?.Chat == null || !a.IsSelfActor()) yield break;
            string sentence = MapActionTexts.SlapSentence;
            yield return new MenuEntry(MapActionTexts.HitHimself, Slap)
            {
                Activé = sentence != null,
                Infobulle = sentence != null
                    ? "Dire « " + sentence + " » dans le canal général (BM*|…|)"
                    : "Phrase SLAP_SENTENCE du client introuvable (lang.xml non chargé) : rien ne serait envoyé.",
            };
        }

        /// <summary>Clic gauche sur son personnage (hors combat : en combat, le clic va d'abord à la cellule) : son menu.</summary>
        public Task<bool> OnActorClickAsync(ActorMenuContext context)
        {
            if (context?.Router == null || context.Actor == null || !context.IsSelf) return Task.FromResult(false);
            IReadOnlyList<Entites> actors = context.ActorsOnCell.Count > 0 && ReferenceEquals(context.ActorsOnCell[0], context.Actor)
                ? context.ActorsOnCell : new[] { context.Actor };
            context.Router.ShowActorMenu(actors, context.CellId, context.Modifiers, context.Ctrl);
            return Task.FromResult(true);
        }

        private static async Task<string> Slap(ActorMenuContext context)
        {
            ChatService chat = context?.Game?.Chat;
            if (chat == null) return "La session n’est plus disponible.";
            ChatResult result = await chat.SlapAsync();
            // Envoyé : la phrase revient dans le chat par l'écho du serveur.
            return result == null || result.Sent ? null : result.Message;
        }
    }

    /// <summary>
    /// Fin du menu de son personnage, dans l'ordre du client : « Libérer son âme » (<c>FREE_MY_SOUL</c>, <c>GF</c>) pour une
    /// tombe, sinon « Changer son orientation » (<c>CHANGE_DIRECTION</c>) hors combat, qui ouvre le sélecteur d'orientation
    /// (<see cref="DirectionChooser"/>) sur le personnage. Le client envoie <c>GF</c> sans confirmation.
    /// </summary>
    [ActorMenuOrder(260)]
    public sealed class SelfStateMenuProvider : IActorMenuProvider
    {
        public bool Handles(Entites a) => a.IsSelfActor();

        public IEnumerable<MenuEntry> Entries(Entites a, GameClass g)
        {
            MapActions actions = g?.Interactions?.MapActions;
            if (actions == null || !a.IsSelfActor()) yield break;
            if (actions.CanFreeSoul)
            {
                yield return new MenuEntry(MapActionTexts.FreeMySoul, FreeSoul)
                { Infobulle = "Devenir fantôme au cimetière de la zone (GF) ; une statue de phénix rend ensuite la vie." };
                yield break;
            }
            if (!actions.CanChangeDirection) yield break;
            yield return new MenuEntry(MapActionTexts.ChangeDirection, ChooseDirection)
            {
                Infobulle = actions.AllowedDirections.Count == 8
                    ? "Choisir l'une des huit orientations (eD0 à eD7)"
                    : "Choisir l'une des quatre diagonales (eD1, eD3, eD5, eD7) : le bit 8192 de AR est absent",
            };
        }

        private static async Task<string> FreeSoul(ActorMenuContext context)
        {
            MapActions actions = context?.Game?.Interactions?.MapActions;
            if (actions == null) return "La session n’est plus disponible.";
            InteractionResult result = await actions.FreeSoulAsync();
            return result?.Message;
        }

        /// <summary>
        /// Ouvre le sélecteur après la fermeture du menu (un menu déroulant ouvert pendant que le précédent se ferme serait
        /// refermé aussitôt), centré sur le pied du personnage à l'écran.
        /// </summary>
        private static Task<string> ChooseDirection(ActorMenuContext context)
        {
            MapActions actions = context?.Game?.Interactions?.MapActions;
            if (actions == null) return Task.FromResult("La session n’est plus disponible.");
            if (!actions.CanChangeDirection)
                return Task.FromResult(actions.IsTomb ? "Une tombe ne peut pas changer d'orientation." : "L'orientation ne se change pas en combat.");
            Control owner = context.Router?.Owner;
            if (owner == null || owner.IsDisposed || !owner.IsHandleCreated) return Task.FromResult("La carte n'est plus affichée.");
            long actorId = context.Actor?.id ?? context.Game.character?.id ?? 0;
            int current = context.Game.character?.Orientation ?? (context.Game.Map?.Self?.Orientation ?? -1);
            IReadOnlyList<int> directions = actions.AllowedDirections;
            Action<string> report = context.Router.Report;
            try
            {
                owner.BeginInvoke((Action)(() =>
                {
                    if (owner.IsDisposed) return;
                    DirectionChooser.Open(owner, ScreenCenter(owner, actorId), actions, directions, current, report);
                }));
            }
            catch (InvalidOperationException) { return Task.FromResult("La carte n'est plus affichée."); }
            return Task.FromResult("Choisissez l'orientation du personnage.");
        }

        /// <summary>Pied de l'acteur à l'écran (la cellule sous le sprite), sinon la position de la souris.</summary>
        internal static Point ScreenCenter(Control owner, long actorId)
        {
            if (owner is UserMapControl map && map.TryGetActorAnchor(actorId, out ActorAnchor anchor) && anchor.IsVisible)
            {
                float x = anchor.WorldFoot.X * map.ViewScale + map.ViewOrigin.X;
                float y = anchor.WorldFoot.Y * map.ViewScale + map.ViewOrigin.Y;
                return map.PointToScreen(new Point((int)Math.Round(x), (int)Math.Round(y)));
            }
            return Cursor.Position;
        }
    }
}
