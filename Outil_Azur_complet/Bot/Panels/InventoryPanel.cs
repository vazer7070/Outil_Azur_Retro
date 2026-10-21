using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Controls.Banner;
using Outil_Azur_complet.Bot.Controls.Chat;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Montures;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Game.Perso.Inventory;
using Tool_BotProtocol.Game.Session;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Inventaire en grille comme la fenêtre <c>Inventory</c> du client (742 × 449) : kamas et pods (<c>PLAYER_WEIGHT</c>),
    /// plateau d'équipement à dix-sept emplacements (<c>_ctr0.._ctr16</c>), filtres Équipement / Divers / Ressources / Quête,
    /// grille du sac avec les icônes du client, fiche d'objet (<c>ItemViewer</c>) avec effets, conditions et panoplie portée.
    /// Envois du client 1.34 : glisser ou Équiper/Déséquiper <c>OM&lt;objet&gt;|&lt;position&gt;</c> (sans quantité, comme
    /// <c>Items.movement</c> à deux paramètres), Utiliser <c>OU&lt;objet&gt;|</c>, Jeter <c>OD&lt;objet&gt;|&lt;quantité&gt;</c> et Détruire
    /// <c>Od&lt;objet&gt;|&lt;quantité&gt;</c> par la boîte de quantité et les questions du client (lot X12 : <see cref="DropSelected"/>,
    /// <see cref="DestroySelected"/>, option <c>ConfirmDropItem</c>). Bouton « plus de choix » : les six tris d'<c>InventoryViewer</c>
    /// (<see cref="InventorySort"/>), gardés tant que le volet existe.
    /// L'état local attend toujours la réponse du serveur (<c>OM</c>, <c>OR</c>, <c>OQ</c>, <c>OAE</c>, <c>OdE</c>, <c>ODE</c>, <c>Im</c>).
    /// Nourrir (lot X4) : une ressource glissée sur le familier porté envoie <c>OM&lt;objet&gt;|8</c> ; sur la monture annoncée
    /// (<c>Re+</c>), une pile passe d'abord par la boîte de quantité du client (<see cref="QuantityPopup"/>) puis
    /// <c>OM&lt;objet&gt;|16|&lt;quantité&gt;</c>. Les réponses <c>Im</c> du repas (<c>INFOS_25</c> à <c>INFOS_32</c>, <c>INFOS_105</c>,
    /// <c>ERROR_53</c>, <c>ERROR_54</c>, <c>ERROR_90</c>) s'affichent sous l'objet.
    /// </summary>
    public sealed class InventoryPanel : GamePanel
    {
        private const string SelectText = "Sélectionnez un objet. Le serveur confirme chaque action.";
        private EquipmentPlateau plateau;
        private ItemGrid inventory;
        private ItemTooltip sheet;
        private Panel sheetArea, podsGauge;
        private Label kamasLabel, podsLabel, inventoryHelp;
        private FlowLayoutPanel filters;
        // Un bouton par filtre avec sa catégorie (null = tout le sac : un dictionnaire refuserait cette clé ; Tag reste libre
        // pour les étiquettes texte que les tests de l'habillage lisent sur tous les boutons de la fenêtre).
        private readonly List<KeyValuePair<ItemCategory?, ClientButton>> filterButtons = new List<KeyValuePair<ItemCategory?, ClientButton>>();
        private ItemCategory? filter;
        private Control equipItem, unequipItem, useItem, dropItem, destroyItem;
        private ContextMenuStrip itemMenu, sortMenu;
        private ClientButton sortButton;
        private ToolTip sortTips;
        private InventorySortKey sort;
        private bool refreshing, dropPending, destroyPending;
        private uint? selectedId;
        // Dernier refus du serveur (OAE, ODE, OdE, Im1…) : gardé sous l'objet jusqu'au prochain changement de sélection ou
        // d'inventaire, car la fenêtre de jeu rafraîchit les volets visibles à chaque mise à jour d'état.
        private string serverNotice;
        // Message Im du repas du familier ou de la monture seulement (INFOS_25 à 32, INFOS_105, ERROR_53, ERROR_54, ERROR_90) :
        // StarLoco envoie OQ, OR, OM ou OCO autour de lui, il survit donc aux mises à jour d'inventaire et ne s'efface qu'au
        // changement de sélection ou au dépôt suivant (lot X4). Les autres refus Im (Im119|44…) s'effacent au prochain changement
        // d'inventaire, comme avant X4 (lot X4-CORR).
        private bool noticeFromIm;
        private InventoryClass bag;
        private CharacterClass character;
        private GameSession session;
        private MountActions mount;
        /// <summary>Messages d'information du repas d'un familier (<c>PetEntry.eat</c> de StarLoco) et de la monture (<c>Im0105</c>).</summary>
        private static readonly int[] FeedingInfos = { 25, 26, 27, 28, 29, 30, 31, 32, 105 };
        /// <summary>Refus ou fin du repas : ERROR_53 (le familier ne mange pas cela), ERROR_54 (familier mort, suivi d'un OM et d'un OCO), ERROR_90 (la monture ne mange pas cela).</summary>
        private static readonly int[] FeedingErrors = { 53, 54, 90 };

        public override string Title => "Inventaire";
        public override Image Icon => ClientAssets.Icon("icone-inventaire", 24);

        /// <summary>Catégorie affichée dans la grille du sac ; null = tout le sac.</summary>
        public ItemCategory? Filter
        {
            get { return filter; }
            set { if (filter == value) return; filter = value; UpdateFilterButtons(); RefreshView(); }
        }

        /// <summary>Tri du menu « plus de choix » (<see cref="InventorySortKey.None"/> : catégorie puis nom), gardé tant que le volet existe.</summary>
        public InventorySortKey Sort
        {
            get { return sort; }
            set { if (sort == value) return; sort = value; RefreshView(); }
        }

        /// <summary>Objet sélectionné (sac ou plateau), ou null.</summary>
        public InventoryObjects CurrentItem => selectedId.HasValue ? Game?.character?.Inventory?.GetByInventoryId(selectedId.Value) : null;

        protected override Control CreateView()
        {
            var page = Page();

            // En-tête : kamas à gauche, « %1 pods sur %2 » et sa jauge à droite.
            var header = new Panel { Dock = DockStyle.Top, Height = 32, Margin = Padding.Empty, BackColor = BotUi.Paper };
            kamasLabel = MakeLabel("0 kamas", 9, true); kamasLabel.Dock = DockStyle.Left; kamasLabel.Width = 150;
            kamasLabel.TextAlign = ContentAlignment.MiddleLeft;
            // Icône à part : le Padding d'un Label décale aussi son image, qui recouvrait alors les premiers chiffres.
            var kamasIcon = new PictureBox { Dock = DockStyle.Left, Width = 22, Margin = Padding.Empty, SizeMode = PictureBoxSizeMode.CenterImage, BackColor = BotUi.Paper, Name = "inventory-kamas-icon" };
            BannerArt.RequestIcon(kamasIcon, "kamas", 18, image => { if (!kamasIcon.IsDisposed) kamasIcon.Image = image; });
            podsLabel = MakeLabel(string.Empty, 8); podsLabel.Dock = DockStyle.Fill; podsLabel.TextAlign = ContentAlignment.MiddleRight; podsLabel.ForeColor = BotUi.Muted;
            podsGauge = new Panel { Dock = DockStyle.Bottom, Height = 5, Margin = Padding.Empty, BackColor = BotUi.Paper };
            podsGauge.Paint += PaintPodsGauge;
            header.Controls.Add(podsLabel); header.Controls.Add(kamasLabel); header.Controls.Add(kamasIcon); header.Controls.Add(podsGauge);

            plateau = new EquipmentPlateau { Dock = DockStyle.Top, Margin = Padding.Empty };
            plateau.Resolve = id => Game?.character?.Inventory?.GetByInventoryId((uint)id);
            plateau.SlotActivated += slot => Select(slot.Item?.Inventory_ID, true);
            plateau.SlotDoubleActivated += slot => { Select(slot.Item?.Inventory_ID, true); _ = EquipSelected(false); };
            plateau.Dropped += (payload, slot) => _ = MoveAsync(payload, slot.Position);
            plateau.MenuRequested += (slot, location) => ShowMenu(slot.Item, slot, location);

            filters = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30, Margin = Padding.Empty, Padding = new Padding(0, 3, 0, 0), WrapContents = false, BackColor = BotUi.Paper };
            AddFilter(null, Lang("ALL", "Tout"), 52);
            AddFilter(ItemCategory.Equipment, Lang("EQUIPEMENT", "Équipement"), 84);
            AddFilter(ItemCategory.Miscellaneous, Lang("NONEQUIPEMENT", "Divers"), 60);
            AddFilter(ItemCategory.Resources, Lang("RESSOURECES", "Ressources"), 84);
            AddFilter(ItemCategory.Quest, "Quête", 56);
            UpdateFilterButtons();
            // « Plus de choix » d'InventoryViewer (_btnMoreChoice, infobulle SEARCH_AND_SORT) : menu des six tris sous le bouton.
            sortButton = (ClientButton)MakeButton("…", (s, e) => ShowSortMenu(), false, 34);
            sortButton.Height = 24; sortButton.Margin = new Padding(12, 0, 0, 0); sortButton.Font = BotFonts.Get(8, FontStyle.Bold);
            sortButton.Name = "inventory-more-choice"; sortButton.AccessibleName = InventorySort.ButtonTip;
            filters.Controls.Add(sortButton);
            sortTips = new ToolTip(); sortTips.SetToolTip(sortButton, InventorySort.ButtonTip);
            sortMenu = new ContextMenuStrip { Renderer = new RetroMenuRenderer(), BackColor = BotUi.Paper, Font = BotFonts.Get(9), ShowImageMargin = false, Name = "inventory-sort-menu" };

            inventory = new ItemGrid { Dock = DockStyle.Fill, Margin = Padding.Empty, EmptyText = "Votre sac est vide." };
            inventory.SelectionChanged += (s, e) => { if (!refreshing) Select(inventory.SelectedItem?.Inventory_ID, false); };
            inventory.ItemActivated += item => { Select(item.Inventory_ID, false); _ = ActivateSelected(); };
            inventory.MenuRequested += (item, location) => ShowMenu(item, inventory, location);
            inventory.Dropped += payload => _ = MoveAsync(payload, -1);

            sheetArea = new Panel { Dock = DockStyle.Bottom, Height = 150, Margin = Padding.Empty, AutoScroll = true, BackColor = BotUi.Paper, Padding = new Padding(0, 4, 0, 0) };
            sheet = new ItemTooltip { Dock = DockStyle.Top };
            sheetArea.Controls.Add(sheet);

            inventoryHelp = MakeStatus(SelectText); inventoryHelp.Height = 46;
            equipItem = MakeButton("Équiper", async (s, e) => await EquipSelected(true), true, 88);
            unequipItem = MakeButton("Déséquiper", async (s, e) => await EquipSelected(false), false, 100);
            useItem = MakeButton("Utiliser", async (s, e) => await UseSelected(), false, 84);
            dropItem = MakeButton("Jeter", async (s, e) => await DropSelected(), false, 84);
            destroyItem = MakeButton("Détruire", async (s, e) => await DestroySelected(), false, 84);
            itemMenu = new ContextMenuStrip { Renderer = new RetroMenuRenderer(), BackColor = BotUi.Paper, Font = BotFonts.Get(9), ShowImageMargin = false };

            // Ordre d'ancrage : le dernier ajouté prend le bord en premier.
            page.Controls.Add(inventory); page.Controls.Add(filters); page.Controls.Add(plateau); page.Controls.Add(header);
            page.Controls.Add(sheetArea); page.Controls.Add(inventoryHelp);
            page.Controls.Add(BotUi.Actions(equipItem, unequipItem, useItem, dropItem, destroyItem));
            return page;
        }

        private void AddFilter(ItemCategory? category, string title, int width)
        {
            var button = (ClientButton)MakeButton(title, (s, e) => Filter = category, false, width);
            button.Height = 24; button.Margin = new Padding(0, 0, 3, 0); button.Font = BotFonts.Get(8);
            filterButtons.Add(new KeyValuePair<ItemCategory?, ClientButton>(category, button));
            filters.Controls.Add(button);
        }

        /// <summary>Menu « plus de choix » sous le bouton : les six tris du client, le tri courant coché.</summary>
        private void ShowSortMenu()
        {
            if (sortMenu == null || sortButton == null) return;
            InventorySort.Fill(sortMenu, sort, key => Sort = key);
            try { sortMenu.Show(sortButton, new Point(0, sortButton.Height)); }
            catch (Exception error) when (error is InvalidOperationException || error is ArgumentException) { /* Contrôle en cours de fermeture. */ }
        }

        private void UpdateFilterButtons()
        {
            foreach (KeyValuePair<ItemCategory?, ClientButton> entry in filterButtons)
            {
                bool active = entry.Key == filter;
                entry.Value.Primary = active; entry.Value.Font = BotFonts.Get(8, active ? FontStyle.Bold : FontStyle.Regular); entry.Value.Invalidate();
            }
        }

        protected override void OnBind(GameClass game)
        {
            bag = game.character?.Inventory;
            character = game.character;
            session = game.Session;
            if (bag != null) bag.RefreshInventory += OnInventoryChanged;
            if (character != null) { character.RefreshCaracteristiques += OnStatsChanged; character.PodsRefresh += OnStatsChanged; }
            if (session != null) session.ServerMessageReceived += OnServerMessage;
            mount = game.Interactions?.Mount;
            if (mount != null) mount.Changed += OnMountChanged;
        }

        protected override void OnUnbind(GameClass game)
        {
            if (bag != null) bag.RefreshInventory -= OnInventoryChanged;
            if (character != null) { character.RefreshCaracteristiques -= OnStatsChanged; character.PodsRefresh -= OnStatsChanged; }
            if (session != null) session.ServerMessageReceived -= OnServerMessage;
            if (mount != null) mount.Changed -= OnMountChanged;
            bag = null; character = null; session = null; mount = null;
        }

        private void OnInventoryChanged(bool changed) => OnUi(() =>
        {
            if (!changed) ShowHelp(Game?.character?.Inventory?.LastServerMessage);
            else { if (!noticeFromIm) serverNotice = null; RefreshView(); }
        });

        private void OnStatsChanged() => OnUi(UpdateHeader);

        /// <summary><c>Re+</c> / <c>Re-</c> : la case de la monture montre son icône et accepte une ressource seulement avec une monture.</summary>
        private void OnMountChanged() => OnUi(() => { if (plateau != null && Game != null) plateau.HasMount = Game.character?.Inventory?.HasMount == true; });

        /// <summary>
        /// Refus du serveur annoncés par <c>Im</c> (<c>Im119|44</c> : conditions non remplies) et réponses au repas du familier
        /// ou de la monture (<c>Im032</c>, <c>Im0105</c>, <c>Im153</c>, <c>Im190</c>…) affichés sous l'objet quand le volet est ouvert.
        /// </summary>
        private void OnServerMessage(ServerMessage message)
        {
            if (message == null) return;
            bool feeding = message.NumericId.HasValue
                && ((message.Kind == ServerMessageKind.Info && Array.IndexOf(FeedingInfos, message.NumericId.Value) >= 0)
                    || (message.Kind == ServerMessageKind.Error && Array.IndexOf(FeedingErrors, message.NumericId.Value) >= 0));
            if (message.Kind != ServerMessageKind.Error && !feeding) return;
            // Seuls les messages du repas survivent aux OQ, OR, OM et OCO qui les entourent ; tout autre refus (Im119|44…) s'efface,
            // comme avant X4, au prochain changement d'inventaire (lot X4-CORR).
            OnUi(() => { if (Host != null && Host.IsOpen(this)) ShowHelp(message.Text, feeding); });
        }

        private void ShowHelp(string message, bool fromIm = false)
        {
            if (string.IsNullOrEmpty(message) || inventoryHelp == null) return;
            serverNotice = message;
            noticeFromIm = fromIm;
            Feedback(message);
            UpdateActions();
        }

        public override void RefreshView()
        {
            if (Game == null || inventory == null) return;
            InventoryClass items = Game.character?.Inventory;
            if (items == null) return;
            InventoryObjects[] all = items.Objets.Where(item => item != null).ToArray();
            if (selectedId.HasValue && all.All(item => item.Inventory_ID != selectedId.Value)) selectedId = null;
            refreshing = true;
            try
            {
                plateau.SetItems(all.Where(IsWorn));
                plateau.HasMount = items.HasMount;
                plateau.Selected = selectedId;
                inventory.SetItems(InventorySort.Apply(all.Where(item => !IsWorn(item) && (filter == null || ItemSlots.CategoryOf(item.Type) == filter.Value)), sort));
                inventory.EmptyText = filter == null ? "Votre sac est vide." : "Aucun objet de cette catégorie dans le sac.";
                if (selectedId.HasValue) { if (!inventory.SelectItem(selectedId.Value)) inventory.ClearSelection(); }
                else inventory.ClearSelection();
            }
            finally { refreshing = false; }
            UpdateHeader();
            UpdateSheet();
            UpdateActions();
        }

        /// <summary>Objet porté : position 0 à 16 ; la barre de raccourcis (34 et plus) laisse l'objet dans le sac.</summary>
        private static bool IsWorn(InventoryObjects item) => (int)item.position >= 0 && (int)item.position < ItemSlots.ShortcutOffset;

        private void UpdateHeader()
        {
            if (kamasLabel == null || Game == null) return;
            long kamas = Game.character?.Kamas ?? 0;
            kamasLabel.Text = BannerArt.Thousands(kamas) + " " + Lang("KAMAS", "Kamas").ToLowerInvariant();
            InventoryClass items = Game.character?.Inventory;
            int current = items?.Actual_pods ?? 0, max = items?.Pods_Max ?? 0;
            podsLabel.Text = max > 0 || current > 0
                ? Lang("PLAYER_WEIGHT", "%1 pods sur %2", BannerArt.Thousands(current), BannerArt.Thousands(max))
                : "Pods inconnus (paquet Ow attendu)";
            podsGauge.Invalidate();
        }

        private void PaintPodsGauge(object sender, PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            Rectangle area = new Rectangle(0, 0, podsGauge.Width - 1, podsGauge.Height - 1);
            using (var pen = new Pen(BotUi.Gold)) graphics.DrawRectangle(pen, area);
            InventoryClass items = Game?.character?.Inventory;
            if (items == null || items.Pods_Max <= 0) return;
            double ratio = Math.Max(0, Math.Min(1.0, (double)items.Actual_pods / items.Pods_Max));
            int width = (int)Math.Round((area.Width - 1) * ratio);
            if (width <= 0) return;
            using (var fill = new SolidBrush(ratio >= 1.0 ? Color.FromArgb(160, 60, 40) : BotUi.Olive)) graphics.FillRectangle(fill, 1, 1, width, area.Height - 1);
        }

        private void Select(uint? id, bool fromPlateau)
        {
            if (selectedId != id) { serverNotice = null; noticeFromIm = false; }
            selectedId = id;
            refreshing = true;
            try
            {
                if (fromPlateau) inventory.ClearSelection();
                plateau.Selected = fromPlateau ? id : null;
            }
            finally { refreshing = false; }
            UpdateSheet();
            UpdateActions();
        }

        private void UpdateSheet()
        {
            if (sheet == null) return;
            InventoryObjects item = CurrentItem;
            if (item == null) { sheet.Clear(); return; }
            ItemSheet card = ItemSheet.From(item);
            ItemSetSheet worn = null;
            if (card.SetId.HasValue && Game?.character?.Inventory != null && Game.character.Inventory.ItemSets.TryGetValue(card.SetId.Value, out ItemSetState state))
                worn = ItemSetSheet.From(state);
            sheet.Show(card, worn);
        }

        private void UpdateActions()
        {
            if (equipItem == null || Game == null) return;
            InventoryObjects item = CurrentItem;
            bool worn = item != null && IsWorn(item);
            bool ready = Connected && item != null && !Game.Fight.IsInFight && Account?.IsMoving() != true;
            ItemSheet card = item == null ? null : sheet.Sheet ?? ItemSheet.From(item);
            equipItem.Enabled = ready && !worn && ItemSlots.PositionsFor(item.Type).Count > 0;
            unequipItem.Enabled = ready && worn;
            useItem.Enabled = ready && !worn;
            dropItem.Enabled = ready && !worn && (card == null || card.CanDrop);
            destroyItem.Enabled = ready && !worn && (card == null || card.CanDestroy);
            useItem.Text = SpecialItems.IsDocument(item) ? "Lire" : "Utiliser";
            // Pierres d'âme et documents (lot F14) : règle du serveur rappelée sous l'objet ; dernier refus du serveur en dessous.
            string note = item == null ? null : SpecialItems.Note(item, Game.Map?.MapID ?? 0);
            string text = item == null ? SelectText
                : item.Name + " · " + item.Qua + " · " + (worn ? ItemSlots.Name((int)item.position) : "dans le sac")
                    + (item.HasMetadata ? string.Empty : " · fiche absente de BotObjets et des textes du client")
                    + (note == null ? string.Empty : Environment.NewLine + note);
            if (!string.IsNullOrEmpty(serverNotice)) text = (item == null ? string.Empty : text + Environment.NewLine) + serverNotice;
            inventoryHelp.Text = text;
            inventoryHelp.Height = note == null && (item == null || string.IsNullOrEmpty(serverNotice)) ? 46 : 80;
        }

        /// <summary>Double-clic ou Entrée sur un objet du sac : utiliser s'il est utilisable, sinon équiper (<c>DOUBLE_CLICK_TO_EQUIP</c>).</summary>
        private Task ActivateSelected()
        {
            InventoryObjects item = CurrentItem;
            if (item == null) return Task.CompletedTask;
            if (ItemSlots.PositionsFor(item.Type).Count > 0 && !ShortcutBar.IsShortcutItem(item)) return EquipSelected(true);
            return UseSelected();
        }

        private async Task EquipSelected(bool equip)
        {
            InventoryObjects item = CurrentItem;
            if (item == null) return;
            try
            {
                bool sent = equip ? await Game.character.Inventory.Equip_item(item) : await Game.character.Inventory.Desequip_Item(item);
                Feedback(sent ? (equip ? "Équipement demandé ; le serveur confirme par OM." : "Retrait demandé ; le serveur confirme par OM.") : "Demande refusée : voir le journal.");
            }
            catch (Exception error) { Feedback(error.Message); }
        }

        private async Task UseSelected()
        {
            InventoryObjects item = CurrentItem;
            if (item == null) return;
            try
            {
                ItemUseResult result = await SpecialItems.UseAsync(Game, item);
                Feedback(result.Message);
            }
            catch (Exception error) { Feedback(error.Message); }
        }

        /// <summary>
        /// Jeter au sol comme <c>Inventory.dropDownItem</c> du client : pendant un échange, <c>CANT_MOVE_ITEM</c> (<c>canMoveItem</c>)
        /// et rien ne part ; une pile ouvre la boîte de quantité (valeur 1, maximum la pile) ; avec l'option <c>ConfirmDropItem</c>
        /// (vraie par défaut), la question <c>CONFIRM_DROP_ITEM</c> (<c>CAUTION_YESNO</c>, titre <c>CAUTION</c>) précède
        /// <c>OD&lt;objet&gt;|&lt;quantité&gt;</c>, la quantité étant bornée à la pile relue (<c>Math.min</c>). Le serveur confirme par OR/OQ.
        /// </summary>
        private async Task DropSelected()
        {
            InventoryObjects item = CurrentItem;
            if (item == null || dropPending) return;
            dropPending = true;
            try
            {
                InventoryClass items = Game?.character?.Inventory;
                if (items == null) return;
                if (items.MoveForbiddenByExchange(out string exchange))
                {
                    await BotDialogs.InfoAsync(View, Lang("INFORMATIONS", "Informations"), Lang("CANT_MOVE_ITEM", exchange));
                    return;
                }
                int quantity = 1;
                if (item.Qua > 1)
                {
                    int? chosen = await BotDialogs.AskQuantityAsync(View, 1, item.Qua);
                    if (chosen == null) { Feedback("Rien n'a été jeté."); return; }
                    quantity = chosen.Value;
                }
                if (Options.ConfirmDropItem)
                {
                    BotDialogResult answer = await BotDialogs.AskYesNoAsync(View, Lang("CAUTION", "Attention"),
                        Lang("CONFIRM_DROP_ITEM", "Êtes-vous certain de vouloir poser à terre cet objet ?"));
                    if (answer != BotDialogResult.Yes) { Feedback("Rien n'a été jeté."); return; }
                }
                InventoryObjects current = Game?.character?.Inventory?.GetByInventoryId(item.Inventory_ID);
                if (current == null) { Feedback("L'objet n'est plus dans l'inventaire."); return; }
                bool sent = await Game.character.Inventory.Drop_Item(current, Math.Min(quantity, current.Qua));
                Feedback(sent ? "Objet jeté ; le serveur confirme par OR ou OQ." : "Demande refusée : voir le journal.");
            }
            catch (Exception error) { Feedback(error.Message); }
            finally { dropPending = false; }
        }

        /// <summary>
        /// Détruire comme <c>Inventory.destroyItem</c> et <c>askDestroy</c> du client : une pile ouvre la boîte de quantité (valeur 1,
        /// maximum la pile) puis part sans autre question ; un exemplaire seul passe par <c>DO_U_DESTROY</c> (« Voulez-vous vraiment
        /// détruire %1 %2 ? », titre <c>QUESTION</c>). Envoi <c>Od&lt;objet&gt;|&lt;quantité&gt;</c> borné à la pile relue.
        /// </summary>
        private async Task DestroySelected()
        {
            InventoryObjects item = CurrentItem;
            if (item == null || destroyPending) return;
            destroyPending = true;
            try
            {
                int quantity = 1;
                if (item.Qua > 1)
                {
                    int? chosen = await BotDialogs.AskQuantityAsync(View, 1, item.Qua);
                    if (chosen == null) { Feedback("Destruction annulée."); return; }
                    quantity = chosen.Value;
                }
                else
                {
                    string question = Lang("DO_U_DESTROY", "Voulez-vous vraiment détruire %1 %2 ?", "1", item.Name);
                    BotDialogResult answer = await BotDialogs.AskYesNoAsync(View, Lang("QUESTION", "Question"), question);
                    if (answer != BotDialogResult.Yes) { Feedback("Destruction annulée."); return; }
                }
                InventoryObjects current = Game?.character?.Inventory?.GetByInventoryId(item.Inventory_ID);
                if (current == null) { Feedback("L'objet n'est plus dans l'inventaire."); return; }
                bool sent = await Game.character.Inventory.Destroy(current, Math.Min(quantity, current.Qua));
                Feedback(sent ? "Destruction demandée ; le serveur confirme par OR ou OQ." : "Demande refusée : voir le journal.");
            }
            catch (Exception error) { Feedback(error.Message); }
            finally { destroyPending = false; }
        }

        /// <summary>Options de la fenêtre de jeu (<c>ConfirmDropItem</c>), sinon celles du dossier de configuration.</summary>
        private BotOptions Options => (View?.FindForm() as GameClientFullform)?.Options ?? BotOptions.Current;

        /// <summary>
        /// Dépôt d'un objet glissé : vers un emplacement (<c>OM&lt;objet&gt;|&lt;position&gt;</c>) ou vers le sac (<c>-1</c>). Comme
        /// <c>dropItem</c> du client, une pile de plus d'un exemplaire posée sur la monture (16) ou rendue au sac (-1) ouvre
        /// d'abord la boîte de quantité (valeur et maximum : la pile) ; seule une valeur positive part, bornée à la pile
        /// (<c>OM&lt;objet&gt;|16|&lt;quantité&gt;</c>). Ailleurs, ou pour un seul exemplaire, aucune quantité n'est envoyée.
        /// </summary>
        private async Task MoveAsync(ShortcutPayload payload, int position)
        {
            if (payload == null || Game == null) return;
            InventoryObjects item = Game.character?.Inventory?.GetByInventoryId((uint)payload.Id);
            if (item == null) { Feedback("Cet objet n'est plus dans l'inventaire."); return; }
            Select(item.Inventory_ID, position >= 0);
            serverNotice = null; noticeFromIm = false;
            try
            {
                int? quantity = null;
                // Case refusée (le client ne l'allume pas) : pas de boîte de quantité, le modèle refuse et journalise.
                ItemSlot target = position >= 0 ? plateau?[position] : null;
                bool accepted = target == null || target.Accepts(item);
                if (accepted && item.Qua > 1 && (position == -1 || position == ItemSlots.MountPosition) && (int)item.position != position)
                {
                    int? chosen = await QuantityPopup.AskAsync(View, item.Qua, item.Qua);
                    InventoryObjects current = Game?.character?.Inventory?.GetByInventoryId(item.Inventory_ID);
                    if (chosen == null || chosen.Value <= 0) { Feedback("Déplacement annulé."); return; }
                    if (current == null) { Feedback("Cet objet n'est plus dans l'inventaire."); return; }
                    item = current;
                    quantity = Math.Min(chosen.Value, current.Qua);
                }
                bool feeding = (position == ItemSlots.PetPosition || position == ItemSlots.MountPosition) && !ItemSlots.Accepts(position, item.Type);
                bool sent = await Game.character.Inventory.MoveToSlot(item, position, quantity);
                Feedback(!sent ? "Déplacement refusé : voir le journal."
                    : feeding ? (position == ItemSlots.PetPosition ? "Repas du familier demandé" : "Repas de la monture demandé") + " ; le serveur répond par OQ ou OR et un message."
                    : position < 0 ? "Rangement demandé ; le serveur confirme par OM." : "Équipement demandé ; le serveur confirme par OM.");
            }
            catch (Exception error) { Feedback(error.Message); }
        }

        /// <summary>Menu contextuel d'une case (sac ou plateau) : les mêmes actions que les boutons, comme le menu des objets du client.</summary>
        private void ShowMenu(InventoryObjects item, Control owner, Point location)
        {
            if (item == null || owner == null || itemMenu == null) return;
            Select(item.Inventory_ID, owner is ItemSlot);
            itemMenu.Items.Clear();
            bool worn = IsWorn(item);
            var title = new ToolStripMenuItem(item.Name + (item.Qua > 1 ? " × " + item.Qua : string.Empty)) { Enabled = false };
            itemMenu.Items.Add(title);
            itemMenu.Items.Add(new ToolStripSeparator());
            if (worn) itemMenu.Items.Add(new ToolStripMenuItem("Déséquiper", null, async (s, e) => await EquipSelected(false)) { Enabled = unequipItem.Enabled });
            else
            {
                itemMenu.Items.Add(new ToolStripMenuItem("Équiper", null, async (s, e) => await EquipSelected(true)) { Enabled = equipItem.Enabled });
                itemMenu.Items.Add(new ToolStripMenuItem(useItem.Text, null, async (s, e) => await UseSelected()) { Enabled = useItem.Enabled });
                itemMenu.Items.Add(new ToolStripMenuItem("Jeter…", null, async (s, e) => await DropSelected()) { Enabled = dropItem.Enabled });
                itemMenu.Items.Add(new ToolStripMenuItem("Détruire…", null, async (s, e) => await DestroySelected()) { Enabled = destroyItem.Enabled });
            }
            try { itemMenu.Show(owner, location); }
            catch (Exception error) when (error is InvalidOperationException || error is ArgumentException) { /* Contrôle en cours de fermeture. */ }
        }

        private static string Lang(string key, string fallback, params string[] args) => ChatUiText.Get(key, fallback, args);

        protected override void Dispose(bool disposing)
        {
            if (disposing) { itemMenu?.Dispose(); sortMenu?.Dispose(); sortTips?.Dispose(); }
            base.Dispose(disposing);
        }

        internal static NumericUpDown Quantity() => new NumericUpDown { Minimum = 1, Maximum = 1, Value = 1, Width = 80, Height = 34,
            Font = BotFonts.Get(9), BackColor = BotUi.PaperLight, ForeColor = BotUi.Ink, Margin = new Padding(0, 4, 7, 0) };

        internal static Label QuantityLabel()
        {
            var label = MakeLabel("Quantité", 8); label.Width = 58; label.Height = 34; label.TextAlign = ContentAlignment.MiddleLeft;
            label.Margin = new Padding(4, 0, 0, 0); return label;
        }

        /// <summary>Objet de l'inventaire correspondant à la ligne sélectionnée d'une liste (étiquette = identifiant d'inventaire) ; utilisé par la boutique et les échanges.</summary>
        internal static InventoryObjects SelectedItem(GameClass game, ListView list)
        {
            if (list == null || list.SelectedItems.Count != 1 || !(list.SelectedItems[0].Tag is uint id) || game == null) return null;
            return game.character.Inventory.GetByInventoryId(id);
        }
    }
}
