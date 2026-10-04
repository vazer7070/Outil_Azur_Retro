using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Inventaire textuel (liste « Objet / Qté / Position ») : Équiper/Déséquiper <c>OM&lt;objet&gt;|&lt;position&gt;|1</c>,
    /// Utiliser <c>OU&lt;objet&gt;|</c>, Jeter <c>OD&lt;objet&gt;|&lt;quantité&gt;</c> après confirmation. L'état local attend le serveur.
    /// Volet provisoire repris tel quel de l'ancien tiroir ; la grille du client appartient au lot de l'inventaire.
    /// </summary>
    public sealed class InventoryPanel : GamePanel
    {
        private ListView inventory;
        private Label inventoryHelp;
        private NumericUpDown inventoryQuantity;
        private Control equipItem, unequipItem, useItem, dropItem;
        private bool dropArmed, refreshingList;
        private InventoryClass bag;

        public override string Title => "Inventaire";
        public override Image Icon => ClientAssets.Icon("icone-inventaire", 24);

        protected override Control CreateView()
        {
            var page = Page();
            inventory = MakeList(9, "Objet", "Qté", "Position");
            inventory.Columns[0].Width = 165; inventory.Columns[1].Width = 48; inventory.Columns[2].Width = 120;
            page.Controls.Add(inventory);
            inventoryHelp = MakeStatus("Sélectionnez un objet. Le serveur confirme chaque action."); inventoryHelp.Height = 46;
            inventoryQuantity = Quantity();
            equipItem = MakeButton("Équiper", async (s, e) => await EquipSelected(true), true, 95);
            unequipItem = MakeButton("Déséquiper", async (s, e) => await EquipSelected(false), false, 112);
            useItem = MakeButton("Utiliser", async (s, e) => await UseSelected(), false, 92);
            dropItem = MakeButton("Jeter", async (s, e) => await DropSelected(), false, 100);
            page.Controls.Add(inventoryHelp);
            page.Controls.Add(BotUi.Actions(equipItem, unequipItem, useItem, dropItem, QuantityLabel(), inventoryQuantity));
            inventory.SelectedIndexChanged += (s, e) => { if (!refreshingList) DisarmDrop(); UpdateActions(); };
            return page;
        }

        protected override void OnBind(GameClass game)
        {
            bag = game.character?.Inventory;
            if (bag != null) bag.RefreshInventory += OnInventoryChanged;
        }
        protected override void OnUnbind(GameClass game)
        {
            if (bag != null) bag.RefreshInventory -= OnInventoryChanged;
            bag = null;
        }

        private void OnInventoryChanged(bool changed) => OnUi(() =>
        {
            if (!changed) Feedback(Game?.character?.Inventory?.LastServerMessage);
            else RefreshView();
        });

        public override void RefreshView()
        {
            if (Game == null || inventory == null) return;
            uint selected = inventory.SelectedItems.Count == 0 ? 0u : (uint)inventory.SelectedItems[0].Tag;
            refreshingList = true;
            try
            {
                inventory.BeginUpdate(); inventory.Items.Clear();
                foreach (var item in Game.character.Inventory.Objets.OrderBy(x => x.IsEquipped() ? 0 : 1).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    var row = inventory.Items.Add(string.IsNullOrEmpty(item.Name) ? "Objet n° " + item.ID : item.Name);
                    row.SubItems.Add(item.Qua.ToString()); row.SubItems.Add(Position(item.position.ToString()));
                    row.Tag = item.Inventory_ID; if (item.Inventory_ID == selected) row.Selected = true;
                }
                inventory.EndUpdate();
            }
            finally { refreshingList = false; }
            UpdateActions();
        }

        private void UpdateActions()
        {
            if (equipItem == null || Game == null) return;
            InventoryObjects item = SelectedItem(Game, inventory);
            bool ready = Connected && item != null && !Game.Fight.IsInFight && Account?.IsMoving() != true;
            equipItem.Enabled = ready && !item.IsEquipped() && InventoryUtilities.GetPosition(item.Type) != null;
            unequipItem.Enabled = ready && item.IsEquipped();
            useItem.Enabled = ready && !item.IsEquipped();
            dropItem.Enabled = ready && !item.IsEquipped();
            if (item != null) inventoryQuantity.Maximum = Math.Max(1, item.Qua);
            useItem.Text = SpecialItems.IsDocument(item) ? "Lire" : "Utiliser";
            if (dropArmed) return;
            dropItem.Text = "Jeter";
            // Pierres d'âme et documents (lot F14) : règle du serveur rappelée sous l'objet.
            string note = SpecialItems.Note(item, Game.Map?.MapID ?? 0);
            inventoryHelp.Text = item == null ? "Sélectionnez un objet. Le serveur confirme chaque action."
                : item.Name + " · " + item.Qua + " · " + (item.IsEquipped() ? "équipé" : "dans le sac")
                    + (item.HasMetadata ? string.Empty : " · fiche absente de BotObjets : type et niveau inconnus")
                    + (note == null ? string.Empty : Environment.NewLine + note);
            inventoryHelp.Height = note == null ? 46 : 80;
        }

        private void DisarmDrop() { dropArmed = false; if (dropItem != null) dropItem.Text = "Jeter"; }

        private async Task EquipSelected(bool equip)
        {
            InventoryObjects item = SelectedItem(Game, inventory);
            if (item == null) return;
            DisarmDrop();
            try
            {
                bool sent = equip ? await Game.character.Inventory.Equip_item(item) : await Game.character.Inventory.Desequip_Item(item);
                Feedback(sent ? (equip ? "Équipement demandé ; le serveur confirme par OM." : "Retrait demandé ; le serveur confirme par OM.") : "Demande refusée : voir le journal.");
            }
            catch (Exception error) { Feedback(error.Message); }
        }

        private async Task UseSelected()
        {
            InventoryObjects item = SelectedItem(Game, inventory);
            if (item == null) return;
            DisarmDrop();
            try
            {
                ItemUseResult result = await SpecialItems.UseAsync(Game, item);
                Feedback(result.Message);
            }
            catch (Exception error) { Feedback(error.Message); }
        }

        private async Task DropSelected()
        {
            InventoryObjects item = SelectedItem(Game, inventory);
            if (item == null) return;
            int quantity = (int)inventoryQuantity.Value;
            if (!dropArmed)
            {
                dropArmed = true; dropItem.Text = "Confirmer";
                inventoryHelp.Text = "Jeter " + quantity + " × " + item.Name + " au sol ? Cliquez de nouveau sur Confirmer ; changer de sélection annule.";
                return;
            }
            DisarmDrop();
            try
            {
                bool sent = await Game.character.Inventory.Drop_Item(item, quantity);
                Feedback(sent ? "Objet jeté ; le serveur confirme par OR ou OQ." : "Demande refusée : voir le journal.");
            }
            catch (Exception error) { Feedback(error.Message); }
        }

        /// <summary>Objet de l'inventaire correspondant à la ligne sélectionnée (étiquette = identifiant d'inventaire).</summary>
        internal static InventoryObjects SelectedItem(GameClass game, ListView list)
        {
            if (list == null || list.SelectedItems.Count != 1 || !(list.SelectedItems[0].Tag is uint id) || game == null) return null;
            return game.character.Inventory.GetByInventoryId(id);
        }

        internal static NumericUpDown Quantity() => new NumericUpDown { Minimum = 1, Maximum = 1, Value = 1, Width = 80, Height = 34,
            Font = BotFonts.Get(9), BackColor = BotUi.PaperLight, ForeColor = BotUi.Ink, Margin = new Padding(0, 4, 7, 0) };

        internal static Label QuantityLabel()
        {
            var label = MakeLabel("Quantité", 8); label.Width = 58; label.Height = 34; label.TextAlign = ContentAlignment.MiddleLeft;
            label.Margin = new Padding(4, 0, 0, 0); return label;
        }

        private static string Position(string value)
        {
            switch (value)
            {
                case "NOT_EQUIPPED": return "Sac";
                case "NECKLACE": return "Amulette";
                case "WEAPON": return "Arme";
                case "LEFT_RING": return "Anneau gauche";
                case "RIGHT_RING": return "Anneau droit";
                case "BELT": return "Ceinture";
                case "BOOTS": return "Bottes";
                case "HAT": return "Coiffe";
                case "CAPE": return "Cape";
                case "PET": return "Familier";
                case "SHIELD": return "Bouclier";
                default: return value.Replace('_', ' ');
            }
        }
    }
}
