using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls.Chat;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>Tri choisi dans le menu « plus de choix » ; <see cref="None"/> garde l'ordre du bot (catégorie puis nom).</summary>
    public enum InventorySortKey { None, Date, Name, Type, Level, Weight, Quantity }

    /// <summary>
    /// Les six tris du bouton « plus de choix » (<c>_btnMoreChoice</c>) d'<c>InventoryViewer</c> du client 1.34 (lot X12), dans son
    /// ordre et avec ses textes : <c>INVENTORY_DATE_SORT</c> (<c>_itemDateId</c>), <c>INVENTORY_NAME_SORT</c> (<c>_itemName</c>),
    /// <c>INVENTORY_TYPE_SORT</c> (<c>_itemType</c>), <c>INVENTORY_LEVEL_SORT</c> (<c>_itemLevel</c>), <c>INVENTORY_POD_SORT</c>
    /// (<c>_itemWeight</c>, poids d'un exemplaire) et <c>INVENTORY_QTY_SORT</c> (<c>_nQuantity</c>). Le client appelle
    /// <c>sortOn(champ, Array.NUMERIC)</c> : ordre croissant ; la date est un compteur qui décroît à chaque fiche reçue, la dernière
    /// arrive donc en tête. Sur le nom, <c>Array.NUMERIC</c> ne donne aucun ordre défini (texte non numérique) : le bot range par
    /// ordre alphabétique. Le tri est stable : à valeur égale, l'ordre du bot (catégorie puis nom) reste. Aucun paquet n'est envoyé.
    /// La recherche (<c>INVENTORY_SEARCH</c>, première entrée du même menu) relève du lot X36.
    /// </summary>
    public static class InventorySort
    {
        /// <summary>Tris dans l'ordre du menu du client.</summary>
        public static IReadOnlyList<InventorySortKey> Keys { get; } = new[]
        {
            InventorySortKey.Date, InventorySortKey.Name, InventorySortKey.Type, InventorySortKey.Level, InventorySortKey.Weight, InventorySortKey.Quantity,
        };

        /// <summary>Texte du client pour un tri (<c>INVENTORY_*_SORT</c>).</summary>
        public static string Label(InventorySortKey key)
        {
            switch (key)
            {
                case InventorySortKey.Date: return ChatUiText.Get("INVENTORY_DATE_SORT", "Trier par date");
                case InventorySortKey.Name: return ChatUiText.Get("INVENTORY_NAME_SORT", "Trier par nom");
                case InventorySortKey.Type: return ChatUiText.Get("INVENTORY_TYPE_SORT", "Trier par type");
                case InventorySortKey.Level: return ChatUiText.Get("INVENTORY_LEVEL_SORT", "Trier par niveau");
                case InventorySortKey.Weight: return ChatUiText.Get("INVENTORY_POD_SORT", "Trier par poids");
                case InventorySortKey.Quantity: return ChatUiText.Get("INVENTORY_QTY_SORT", "Trier par quantité").Trim();
                default: return string.Empty;
            }
        }

        /// <summary>Infobulle du bouton (<c>SEARCH_AND_SORT</c>).</summary>
        public static string ButtonTip => ChatUiText.Get("SEARCH_AND_SORT", "Tri et recherche");

        /// <summary>Ordre du bot sans tri choisi : catégorie des filtres, puis nom.</summary>
        public static IEnumerable<InventoryObjects> Default(IEnumerable<InventoryObjects> items) =>
            (items ?? Enumerable.Empty<InventoryObjects>()).Where(item => item != null)
                .OrderBy(item => ItemSlots.CategoryOf(item.Type)).ThenBy(item => item.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase);

        /// <summary>Applique le tri choisi sur l'ordre du bot (tri stable, croissant comme <c>sortOn</c>).</summary>
        public static IEnumerable<InventoryObjects> Apply(IEnumerable<InventoryObjects> items, InventorySortKey key)
        {
            IEnumerable<InventoryObjects> ordered = Default(items);
            switch (key)
            {
                case InventorySortKey.Date: return ordered.OrderBy(item => item.DateId);
                case InventorySortKey.Name: return ordered.OrderBy(item => item.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase);
                case InventorySortKey.Type: return ordered.OrderBy(item => item.Type);
                case InventorySortKey.Level: return ordered.OrderBy(item => item.Level);
                case InventorySortKey.Weight: return ordered.OrderBy(item => item.pods);
                case InventorySortKey.Quantity: return ordered.OrderBy(item => item.Qua);
                default: return ordered;
            }
        }

        /// <summary>Remplit <paramref name="menu"/> des six tris ; le tri courant est coché, un clic appelle <paramref name="choose"/>.</summary>
        public static void Fill(ContextMenuStrip menu, InventorySortKey current, Action<InventorySortKey> choose)
        {
            if (menu == null) return;
            menu.Items.Clear();
            foreach (InventorySortKey key in Keys)
            {
                InventorySortKey value = key;
                menu.Items.Add(new ToolStripMenuItem(Label(value), null, (s, e) => choose?.Invoke(value))
                    { Checked = value == current, Name = "sort-" + value.ToString().ToLowerInvariant() });
            }
        }
    }
}
