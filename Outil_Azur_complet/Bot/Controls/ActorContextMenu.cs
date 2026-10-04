using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Interfaces;
using Tool_BotProtocol.Game.Maps.Interfaces;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Menu contextuel d'un acteur de la carte, construit depuis <see cref="ActorMenuRegistry"/> comme le
    /// <c>PopupMenu</c> du client : nom de l'acteur en tête (entrée statique), puis les entrées des fournisseurs.
    /// Avec plusieurs acteurs (Ctrl + clic droit), un sous-menu par acteur de la cellule.
    /// </summary>
    public static class ActorContextMenu
    {
        /// <summary>Étiquette des entrées statiques (nom de l'acteur), pour le rendu et les tests.</summary>
        public const string HeaderTag = "actor-menu-header";
        private static readonly RetroMenuRenderer renderer = new RetroMenuRenderer();

        /// <summary>Construit le menu sans l'afficher ; l'appelant le libère (ou l'affiche avec <see cref="Show"/>).</summary>
        public static ContextMenuStrip Build(InteractionRouter router, IReadOnlyList<Entites> actors, short cellId, Keys modifiers, bool perActor)
        {
            if (router == null) throw new ArgumentNullException(nameof(router));
            var strip = new ContextMenuStrip { Renderer = renderer, Font = BotFonts.Get(8.25f), BackColor = BotUi.Paper, ForeColor = BotUi.Ink,
                ShowImageMargin = true, AccessibleName = "Menu de l'acteur", Name = "actor-context-menu" };
            if (actors == null || actors.Count == 0) return strip;
            if (perActor && actors.Count > 1)
            {
                foreach (Entites actor in actors)
                {
                    var sub = new ToolStripMenuItem(MenuText(ActorClassifier.DisplayName(actor))) { Tag = actor, AccessibleName = "Actions sur " + ActorClassifier.DisplayName(actor) };
                    sub.DropDown.Renderer = renderer; sub.DropDown.Font = strip.Font; sub.DropDown.BackColor = BotUi.Paper;
                    Fill(sub.DropDownItems, router, router.CreateContext(actor, actors, cellId, modifiers));
                    strip.Items.Add(sub);
                }
            }
            else Fill(strip.Items, router, router.CreateContext(actors[0], actors, cellId, modifiers));
            return strip;
        }

        /// <summary>Affiche le menu sous la souris ; il se libère de lui-même une fois refermé.</summary>
        public static void Show(ContextMenuStrip strip, Control owner, Point location)
        {
            if (strip == null || owner == null || owner.IsDisposed) return;
            strip.Closed += (s, e) => DisposeLater(strip, owner);
            strip.Show(owner, location);
        }

        private static void Fill(ToolStripItemCollection items, InteractionRouter router, ActorMenuContext context)
        {
            items.Add(new ToolStripLabel(MenuText(ActorClassifier.DisplayName(context.Actor))) { Tag = HeaderTag, Font = BotFonts.Get(8.25f, FontStyle.Bold),
                ForeColor = BotUi.Ink, AccessibleName = "Acteur : " + ActorClassifier.DisplayName(context.Actor) });
            IReadOnlyList<MenuEntry> entries = router.Registry.EntriesFor(context.Actor, context.Game, router.ReportProviderError);
            if (entries.Count == 0) { items.Add(new ToolStripMenuItem("Aucune action disponible") { Enabled = false }); return; }
            foreach (MenuEntry entry in entries) items.Add(CreateItem(entry, router, context));
        }

        private static ToolStripItem CreateItem(MenuEntry entry, InteractionRouter router, ActorMenuContext context)
        {
            if (entry.EstSéparateur) return new ToolStripSeparator();
            var item = new ToolStripMenuItem(entry.Texte ?? string.Empty, entry.Icône) { Tag = entry, ToolTipText = entry.Infobulle,
                AccessibleName = entry.Texte };
            if (!string.IsNullOrEmpty(entry.Raccourci)) { item.ShortcutKeyDisplayString = entry.Raccourci; item.ShowShortcutKeys = true; }
            if (entry.ParDéfaut) item.Font = BotFonts.Get(8.25f, FontStyle.Bold);
            if (entry.SousMenu != null && entry.SousMenu.Count > 0)
            {
                item.DropDown.Renderer = renderer; item.DropDown.BackColor = BotUi.Paper;
                foreach (MenuEntry child in entry.SousMenu) if (child != null) item.DropDownItems.Add(CreateItem(child, router, context));
                item.Enabled = entry.Activé;
            }
            else item.Enabled = entry.Activé && entry.Action != null;
            if (entry.Action != null) item.Click += async (s, e) => await router.ExecuteAsync(entry, context);
            return item;
        }

        /// <summary>Nom reçu du serveur : « &amp; » ne doit pas devenir un raccourci clavier souligné.</summary>
        private static string MenuText(string name) => (name ?? string.Empty).Replace("&", "&&");

        private static void DisposeLater(ContextMenuStrip strip, Control owner)
        {
            // L'entrée cliquée reçoit son Click après la fermeture : la libération attend la fin du message en cours.
            if (owner != null && !owner.IsDisposed && owner.IsHandleCreated)
                try { owner.BeginInvoke((Action)strip.Dispose); return; }
                catch (InvalidOperationException) { }
            strip.Dispose();
        }
    }

    /// <summary>Rendu Retro des menus : parchemin, filets dorés, cadre brun, surbrillance olive (aucun thème système).</summary>
    internal sealed class RetroMenuRenderer : ToolStripRenderer
    {
        private static readonly Color Highlight = Color.FromArgb(214, 205, 170);

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var fill = new SolidBrush(BotUi.Paper)) e.Graphics.FillRectangle(fill, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using (var dark = new Pen(BotUi.Frame)) e.Graphics.DrawRectangle(dark, bounds);
            using (var gold = new Pen(BotUi.Gold)) e.Graphics.DrawRectangle(gold, Rectangle.Inflate(bounds, -1, -1));
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using (var fill = new SolidBrush(BotUi.PaperLight)) e.Graphics.FillRectangle(fill, e.AffectedBounds);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var bounds = new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height - 1);
            using (var fill = new SolidBrush(Highlight)) e.Graphics.FillRectangle(fill, bounds);
            using (var border = new Pen(BotUi.Olive)) e.Graphics.DrawRectangle(border, bounds);
        }

        protected override void OnRenderLabelBackground(ToolStripItemRenderEventArgs e)
        {
            if (!(e.Item.Tag is string tag) || tag != ActorContextMenu.HeaderTag) return;
            using (var fill = new SolidBrush(BotUi.Gold)) e.Graphics.FillRectangle(fill, new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height));
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled || (e.Item.Tag as string) == ActorContextMenu.HeaderTag ? BotUi.Ink : BotUi.Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var line = new Pen(BotUi.Gold)) e.Graphics.DrawLine(line, 4, y, e.Item.Width - 4, y);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item != null && e.Item.Enabled ? BotUi.Ink : BotUi.Muted;
            base.OnRenderArrow(e);
        }
    }
}
