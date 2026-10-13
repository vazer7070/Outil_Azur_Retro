using System;
using System.Globalization;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Monstres;

namespace Tool_BotProtocol.Game.Perso.Inventory
{
    /// <summary>Résultat d'une utilisation demandée depuis l'inventaire : paquet envoyé ou raison du refus local.</summary>
    public sealed class ItemUseResult
    {
        internal ItemUseResult(bool sent, string message) { Sent = sent; Message = message ?? string.Empty; }
        public bool Sent { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Objets du sac dont l'utilisation dépend d'une règle du serveur : pierres d'âme pleines (type 85, invocation
    /// seulement dans les arènes, <see cref="SoulStones"/>) et documents (type 25). Pour un document, le client 1.34 envoie
    /// le même <c>OU&lt;objet&gt;|</c> que pour tout objet et n'émet jamais <c>dC</c> : c'est le serveur qui ouvrirait la
    /// fenêtre par <c>dCK&lt;document&gt;</c>. StarLoco n'envoie <c>dCK</c> que pour les pancartes
    /// (<c>InteractiveObject.getSignIO</c>) et n'a aucune action d'objet pour les livres : la demande part, sans réponse
    /// attendue ; si un serveur répond <c>dCK</c>, le volet Document (lot M3) s'ouvre comme pour une pancarte.
    /// </summary>
    public static class SpecialItems
    {
        /// <summary><c>ITEM_TYPE_DOCUMENT</c> de StarLoco (livres, parchemins de texte).</summary>
        public const int DocumentType = 25;

        public static bool IsFullSoulStone(InventoryObjects item) => item != null && item.Type == SoulStones.FullStoneType;
        public static bool IsEmptySoulStone(InventoryObjects item) => item != null && item.Type == SoulStones.EmptyStoneType;
        public static bool IsDocument(InventoryObjects item) => item != null && item.Type == DocumentType;

        /// <summary>
        /// Remarque affichée sous l'objet choisi (null pour un objet ordinaire). <paramref name="mapId"/> : carte actuelle,
        /// 0 si elle n'est pas encore connue.
        /// </summary>
        public static string Note(InventoryObjects item, int mapId)
        {
            if (IsFullSoulStone(item))
                return mapId > 0 && !SoulStones.IsArenaMap(mapId)
                    ? "Pierre d'âme pleine : StarLoco n'accepte l'invocation que dans les arènes (cartes 10131 à 10138) ; ici, « Utiliser » est refusé."
                    : "Pierre d'âme pleine : dans une arène, « Utiliser » fait apparaître le groupe capturé (Im022).";
            if (IsEmptySoulStone(item))
                return "Pierre d'âme vide : à équiper comme arme pour capturer les âmes d'un groupe capturable.";
            if (IsDocument(item))
                return "Document : le bot envoie OU comme le client ; StarLoco n'ouvre pas les livres du sac (aucun dCK hors pancartes).";
            return null;
        }

        /// <summary>
        /// « Utiliser » depuis l'inventaire : <c>OU&lt;objet&gt;|</c> par <see cref="InventoryClass.Use_Item"/>. Une pierre d'âme
        /// pleine hors d'une arène connue n'est pas envoyée : StarLoco l'ignorerait sans répondre.
        /// </summary>
        public static async Task<ItemUseResult> UseAsync(GameClass game, InventoryObjects item)
        {
            if (game?.character?.Inventory == null || item == null) return new ItemUseResult(false, "Aucun objet sélectionné.");
            int mapId = game.Map?.MapID ?? 0;
            if (IsFullSoulStone(item) && mapId > 0 && !SoulStones.IsArenaMap(mapId))
                return new ItemUseResult(false, "Pierre d'âme non utilisée : StarLoco n'invoque le groupe que dans les arènes 10131 à 10138 (carte actuelle "
                    + mapId.ToString(CultureInfo.InvariantCulture) + ").");
            bool sent = await game.character.Inventory.Use_Item(item).ConfigureAwait(false);
            if (!sent) return new ItemUseResult(false, "Demande refusée : voir le journal.");
            if (IsFullSoulStone(item)) return new ItemUseResult(true, "Invocation demandée (OU) ; le serveur répond Im022 et retire la pierre.");
            if (IsDocument(item)) return new ItemUseResult(true, "Lecture demandée (OU) ; StarLoco ne répond pas pour les livres du sac.");
            return new ItemUseResult(true, "Utilisation demandée ; le serveur confirme.");
        }
    }
}
