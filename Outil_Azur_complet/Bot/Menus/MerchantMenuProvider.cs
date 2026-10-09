using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Game.Habitat;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Interfaces;

namespace Outil_Azur_complet.Bot.Menus
{
    /// <summary>
    /// Marchands hors ligne et magasin du personnage (<c>GameManager</c> du client 1.34) : sur un marchand de la carte,
    /// « Acheter » envoie <c>ER4|&lt;marchand&gt;|&lt;cellule&gt;</c> ; sur soi-même, si <c>Player.canBeMerchant</c> (bit 32 de <c>AR</c>
    /// absent), « Organiser mon magasin » (<c>ORGANIZE_SHOP</c>, <c>ER6|</c>) et « Passer en mode 'marchand' » (<c>MERCHANT_MODE</c>,
    /// <c>Eq</c> puis la boîte <c>DO_U_OFFLINEEXCHANGE</c> posée par <see cref="MerchantPanel"/> à la réception de <c>Eq1</c>) ; ces deux entrées
    /// sont grisées tant qu'un magasin est ouvert (StarLoco ignore <c>Eq</c> pendant un échange, <c>ER6|</c> y est refusé).
    /// </summary>
    [ActorMenuOrder(250)]
    public sealed class MerchantMenuProvider : IActorMenuProvider
    {
        public bool Handles(Entites a) => a is MerchantActor || (a is PlayerActor player && player.IsSelf);

        public IEnumerable<MenuEntry> Entries(Entites a, GameClass g)
        {
            MerchantExchange shop = g?.Interactions?.Merchant;
            if (shop == null) yield break;
            if (a is MerchantActor merchant)
            {
                long id = merchant.Id;
                short cell = (short)merchant.CellId;
                yield return new MenuEntry(HouseTexts.Text("BUY", "Acheter"), context => Run(context, m => m.OpenAsync(id, cell)))
                { Icône = ClientAssets.Icon("kamas", 16), ParDéfaut = true, Raccourci = "Clic gauche", Infobulle = "Ouvrir le magasin de ce marchand (ER4|" + id + "|" + cell + ")" };
                yield break;
            }
            if (!ActorClassifier.IsSelf(a, g) || !shop.CanBeMerchant) yield break;
            yield return new MenuEntry(HouseTexts.Text("ORGANIZE_SHOP", "Organiser mon magasin"), context => Run(context, m => m.OrganizeAsync()))
            { Icône = ClientAssets.Icon("kamas", 16), Activé = !shop.IsOpen, Infobulle = "Ouvrir son magasin pour y mettre des objets en vente (ER6|)" };
            yield return new MenuEntry(HouseTexts.Text("MERCHANT_MODE", "Passer en mode 'marchand'"), AskMerchantMode)
            { Activé = !shop.IsOpen, Infobulle = "Demander la taxe du mode marchand (Eq) hors de toute fenêtre ; la boîte de confirmation s'ouvre à la réponse du serveur (Eq1)" };
        }

        /// <summary><c>Eq</c> ; le volet Marchand pose la question <c>DO_U_OFFLINEEXCHANGE</c> à la réception de <c>Eq1</c> et n'envoie <c>EQ</c> qu'après « Oui ».</summary>
        private static Task<string> AskMerchantMode(ActorMenuContext context)
        {
            MerchantPanel panel = context?.Panels?.Get<MerchantPanel>();
            if (panel != null) return panel.AskMerchantModeAsync();
            return Run(context, m => m.AskMerchantModeAsync());
        }

        private static async Task<string> Run(ActorMenuContext context, Func<MerchantExchange, Task<InteractionResult>> action)
        {
            MerchantExchange shop = context?.Game?.Interactions?.Merchant;
            if (shop == null) return "La session n’est plus disponible.";
            InteractionResult result = await action(shop);
            return result?.Message;
        }
    }
}
