using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Tools_protocol.Kryone.Database;

namespace Outil_Azur_complet.editeur_items
{
    public partial class itemeditor
    {
        private void StageAddItem(object sender, EventArgs args)
        {
            if (!_inventoryAvailable || listBox4.SelectedItem == null || listBox5.SelectedItem == null)
            {
                MessageBox.Show("Sélectionnez un personnage et un modèle d'objet.", "Inventaire",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!_inventoryTemplates.TryGetValue(listBox5.SelectedItem.ToString(), out InventoryTemplateChoice template))
                return;
            var character = CharacterList.Listing(listBox4.SelectedItem.ToString());
            if (character == null || character.Logged != 0)
            {
                MessageBox.Show("Le personnage doit être chargé et déconnecté.", "Inventaire",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (iTalk_NumericUpDown6.Value <= 0 || iTalk_NumericUpDown6.Value > int.MaxValue)
            {
                MessageBox.Show("Indiquez une quantité supérieure à zéro.", "Inventaire",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            int quantity = (int)iTalk_NumericUpDown6.Value;
            string key = $"{character.Id}:add:{template.Id}";
            if (_inventoryChanges.TryGetValue(key, out InventoryChange existing))
                existing.Quantity = checked(existing.Quantity + quantity);
            else
                _inventoryChanges.Add(key, new InventoryChange
                {
                    CharacterId = character.Id,
                    CharacterName = character.Name,
                    Kind = InventoryChangeKind.Add,
                    TemplateId = template.Id,
                    Quantity = quantity
                });
            RefreshPendingInventory();
        }

        private void StageRemoveItems(object sender, EventArgs args)
        {
            if (!_inventoryAvailable || listBox4.SelectedItem == null || sfListView1.CheckedItems.Count == 0)
            {
                MessageBox.Show("Cochez au moins un objet du personnage.", "Inventaire",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var character = CharacterList.Listing(listBox4.SelectedItem.ToString());
            if (character == null || character.Logged != 0)
            {
                    MessageBox.Show("Le personnage doit être chargé et déconnecté.", "Inventaire",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            bool multiple = sfListView1.CheckedItems.Count > 1;
            var additions = new List<InventoryChange>();
            foreach (var selected in sfListView1.CheckedItems)
            {
                Match match = Regex.Match(selected.ToString(), @"\((\d+)\)");
                if (!match.Success || !int.TryParse(match.Groups[1].Value, out int guid) ||
                    !ItemList.ItemsList.TryGetValue(guid, out ItemList item))
                {
                    MessageBox.Show("Les détails de l'objet sélectionné ne sont pas chargés.", "Inventaire",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                int quantity = multiple ? item.Qua : (int)iTalk_NumericUpDown6.Value;
                if (_inventoryChanges.ContainsKey($"{character.Id}:update:{guid}"))
                {
                    MessageBox.Show("Cet objet a déjà une modification en attente. Annulez-la dans Changements d'inventaire avant de le retirer.", "Inventaire",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (quantity <= 0 || quantity > item.Qua)
                {
                    MessageBox.Show($"La quantité doit être comprise entre 1 et {item.Qua}.", "Inventaire",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                string key = $"{character.Id}:remove:{guid}";
                if (_inventoryChanges.TryGetValue(key, out InventoryChange existing))
                {
                    if ((long)existing.Quantity + quantity > item.Qua)
                    {
                        MessageBox.Show("La quantité déjà en attente dépasse le stock.", "Inventaire",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                additions.Add(new InventoryChange
                {
                    CharacterId = character.Id,
                    CharacterName = character.Name,
                    Kind = InventoryChangeKind.Remove,
                    ItemGuid = guid,
                    Quantity = quantity
                });
            }
            foreach (var change in additions)
            {
                string key = $"{change.CharacterId}:remove:{change.ItemGuid}";
                if (_inventoryChanges.TryGetValue(key, out InventoryChange existing))
                    existing.Quantity += change.Quantity;
                else _inventoryChanges.Add(key, change);
            }
            RefreshPendingInventory();
        }

        private void RefreshPendingInventory()
        {
            listView1.BeginUpdate();
            try
            {
                listView1.Items.Clear();
                foreach (var entry in _inventoryChanges)
                {
                    InventoryChange change = entry.Value;
                    string name = change.Kind == InventoryChangeKind.Add
                        ? list2.FirstOrDefault(value => value.EndsWith($"(#{change.TemplateId})", StringComparison.Ordinal))
                          ?? $"Modèle #{change.TemplateId}"
                        : ItemList.ItemsList.TryGetValue(change.ItemGuid, out ItemList item)
                          ? $"{ItemTemplateList.GetItem(item.Template, 1)} ({change.ItemGuid})"
                          : $"Objet ({change.ItemGuid})";
                    var row = new ListViewItem(name) { Tag = entry.Key };
                    row.SubItems.Add(change.Quantity.ToString());
                    row.SubItems.Add(change.Kind == InventoryChangeKind.Add ? "Ajouter" : change.Kind==InventoryChangeKind.Update ? "Modifier" : "Retirer");
                    row.SubItems.Add(change.CharacterName);
                    listView1.Items.Add(row);
                }
            }
            finally { listView1.EndUpdate(); }
            iTalk_Button_23.Enabled = _inventoryAvailable && _inventoryChanges.Count > 0;
        }

        private void RemoveSelectedPendingChange()
        {
            if (listView1.SelectedItems.Count == 0) return;
            string key = listView1.SelectedItems[0].Tag as string;
            if (key == null) return;
            _inventoryChanges.Remove(key);
            RefreshPendingInventory();
        }

        private void ApplyPendingInventory(object sender, EventArgs args)
        {
            if (!_inventoryAvailable || _inventoryChanges.Count == 0) return;
            iTalk_Button_23.Enabled = false;
            try
            {
                var updated = InventoryUpdateService.Apply(_inventoryChanges.Values.ToArray());
                foreach (var entry in updated)
                {
                    var character = CharacterList.PersoAll.Values.FirstOrDefault(player => player.Id == entry.Key);
                    if (character != null) character.Objets = entry.Value;
                }
                _inventoryChanges.Clear();
                Persoinventory.Clear();
                if (listBox4.SelectedItem != null)
                {
                    string name = listBox4.SelectedItem.ToString();
                    CharacterList.GetInventory(name);
                    var items = CharacterList.ItemsPerso.ToList();
                    Persoinventory[name] = items;
                    sfListView1.DataSource = null;
                    sfListView1.DataSource = items;
                    sfListView1.Refresh();
                    iTalk_Label29.Text = sfListView1.RowCount.ToString();
                }
                RefreshPendingInventory();
                _inventoryStatus.ForeColor = Color.DarkGreen;
                _inventoryStatus.Text = "Inventaire enregistré. Reconnectez le personnage pour voir les changements en jeu.";
                MessageBox.Show("Modifications enregistrées.", "Inventaire", MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception error)
            {
                _inventoryStatus.ForeColor = Color.DarkRed;
                _inventoryStatus.Text = error.Message;
                MessageBox.Show($"Aucune modification enregistrée : {error.Message}", "Inventaire",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { iTalk_Button_23.Enabled = _inventoryAvailable && _inventoryChanges.Count > 0; }
        }
    }
}
