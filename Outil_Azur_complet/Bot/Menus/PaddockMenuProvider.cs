using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Interfaces;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Montures;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Outil_Azur_complet.Bot.Menus
{
    /// <summary>
    /// Objets d'élevage d'un enclos (abreuvoirs, mangeoires… posés par <c>GDO</c> avec une durabilité), comme le début de
    /// <c>onObjectRelease</c> du client 1.34 : dans un enclos de la guilde du personnage (<c>mountPark.isMine</c>), un clic gauche
    /// sur un tel objet ouvre un menu titré du nom de l'objet avec « Retirer » (<c>Ro&lt;cellule&gt;</c>) au lieu de déplacer le
    /// personnage. Le menu de l'objet interactif « enclos » (type 13 : Accéder, Acheter, Vendre, Modifier le prix) reste celui
    /// de <see cref="InteractiveMenuProvider"/> (lot M3) : ce fournisseur n'en ajoute pas un second. Le branchement se défait
    /// quand la carte est libérée.
    /// </summary>
    public static class PaddockMenuProvider
    {
        public const string MenuName = "paddock-object-menu";
        private static readonly RetroMenuRenderer renderer = new RetroMenuRenderer();
        private static readonly ConditionalWeakTable<MapControl, Attachment> attachments = new ConditionalWeakTable<MapControl, Attachment>();

        /// <summary>Vrai si un clic sur cette cellule ouvre le menu de l'objet d'élevage (objet à durabilité, enclos de sa guilde).</summary>
        public static bool HasMenu(GameClass game, int cellId)
        {
            MountActions mount = game?.Interactions?.Mount;
            return mount != null && mount.IsParkMine && mount.ParkObjectAt(cellId) != null;
        }

        /// <summary>Entrées du menu (sans le titre) : « Retirer ».</summary>
        public static IReadOnlyList<MenuEntry> Entries(GameClass game, int cellId)
        {
            var entries = new List<MenuEntry>();
            if (!HasMenu(game, cellId)) return entries;
            GroundObject placed = game.Interactions.Mount.ParkObjectAt(cellId);
            entries.Add(new MenuEntry(MountTexts.Get("REMOVE", "Retirer"), context => RemoveAsync(context?.Game ?? game, cellId))
            {
                Infobulle = "Retirer l'objet d'élevage (Ro" + cellId + ")" + (placed?.Durability != null
                    ? " — durabilité " + placed.Durability.Value + " / " + (placed.DurabilityMax ?? 0) : string.Empty)
            });
            return entries;
        }

        /// <summary>Nom de l'objet posé sur la cellule (titre du menu).</summary>
        public static string Title(GameClass game, int cellId)
        {
            GroundObject placed = game?.Interactions?.Mount?.ParkObjectAt(cellId);
            return placed == null ? "Objet d'élevage" : InventoryObjects.DisplayName(placed.ItemTemplateId);
        }

        private static async Task<string> RemoveAsync(GameClass game, int cellId)
        {
            MountActions mount = game?.Interactions?.Mount;
            if (mount == null) return "La session n’est plus disponible.";
            InteractionResult result = await mount.RemoveParkObjectAsync(cellId);
            return result?.Message;
        }

        /// <summary>Menu Retro de l'objet (titre, entrées), sans l'afficher ; l'appelant le libère.</summary>
        public static ContextMenuStrip BuildMenu(string title, IReadOnlyList<MenuEntry> entries, InteractionRouter router, ActorMenuContext context)
        {
            var strip = new ContextMenuStrip { Renderer = renderer, Font = BotFonts.Get(8.25f), BackColor = BotUi.Paper, ForeColor = BotUi.Ink,
                ShowImageMargin = true, AccessibleName = "Menu de l'objet d'élevage", Name = MenuName };
            strip.Items.Add(new ToolStripLabel((title ?? string.Empty).Replace("&", "&&")) { Tag = ActorContextMenu.HeaderTag, Font = BotFonts.Get(8.25f, FontStyle.Bold),
                ForeColor = BotUi.Ink, AccessibleName = "Objet : " + title });
            foreach (MenuEntry entry in entries ?? new MenuEntry[0])
            {
                var item = new ToolStripMenuItem((entry.Texte ?? string.Empty).Replace("&", "&&"), entry.Icône)
                { Tag = entry, ToolTipText = entry.Infobulle, AccessibleName = entry.Texte, Enabled = entry.Activé && entry.Action != null };
                if (entry.Action != null && router != null) item.Click += async (s, e) => await router.ExecuteAsync(entry, context);
                strip.Items.Add(item);
            }
            return strip;
        }

        /// <summary>Branche les objets d'élevage sur la carte (appelé avec <see cref="InteractiveMenuProvider.Attach"/>).</summary>
        public static void Attach(MapControl map)
        {
            if (map == null || map.IsDisposed || map.Router == null) return;
            lock (attachments)
            {
                if (attachments.TryGetValue(map, out _)) return;
                attachments.Add(map, new Attachment(map));
            }
        }

        /// <summary>Dernier menu construit pour cette carte (diagnostic et tests), ou <c>null</c>.</summary>
        public static ContextMenuStrip LastMenu(MapControl map)
        {
            if (map == null) return null;
            lock (attachments) return attachments.TryGetValue(map, out Attachment attachment) ? attachment.LastMenu : null;
        }

        private sealed class Attachment
        {
            private readonly MapControl map;
            private readonly InteractionRouter router;
            private bool detached;

            internal Attachment(MapControl map)
            {
                this.map = map; router = map.Router;
                router.MoveRequested += OnMoveRequested;
                map.Disposed += (s, e) => Detach();
            }

            internal ContextMenuStrip LastMenu { get; private set; }

            private Task OnMoveRequested(object sender, MapClickEventArgs e)
            {
                if (detached || e.Handled || e.Button != MouseButtons.Left) return Task.CompletedTask;
                GameClass current = router.Account?.Game;
                if (current == null || current.Fight?.IsInFight == true || map.SelectedSpellId.HasValue || !HasMenu(current, e.CellId)) return Task.CompletedTask;
                e.Handled = true;
                ActorMenuContext context = router.CreateContext(null, null, e.CellId, e.Modifiers);
                Show(BuildMenu(Title(current, e.CellId), Entries(current, e.CellId), router, context));
                return Task.CompletedTask;
            }

            private void Show(ContextMenuStrip strip)
            {
                ContextMenuStrip previous = LastMenu;
                LastMenu = strip;
                if (previous != null && !previous.IsDisposed) { if (previous.Visible) previous.Close(); previous.Dispose(); }
                try { ActorContextMenu.Show(strip, map, map.PointToClient(Cursor.Position)); }
                catch (Exception error) when (error is InvalidOperationException || error is ArgumentException || error is NotImplementedException)
                { router.Account?.Logger?.LogException("CARTE", error); }
            }

            private void Detach()
            {
                if (detached) return;
                detached = true;
                router.MoveRequested -= OnMoveRequested;
                ContextMenuStrip last = LastMenu; LastMenu = null;
                if (last != null && !last.IsDisposed) last.Dispose();
            }
        }
    }
}
