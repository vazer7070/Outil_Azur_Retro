using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Perso.Inventory;
using Tool_BotProtocol.Game.Perso.Inventory.Enums;
using Tool_BotProtocol.Game.Perso.Spells;

namespace Outil_Azur_complet.Bot.Controls.Banner
{
    /// <summary>Onglet de la barre de raccourcis (<c>BANNER_TAB_SPELLS</c> « Sorts », <c>BANNER_TAB_ITEMS</c> « Obj. »).</summary>
    public enum ShortcutTab { Spells, Items }

    /// <summary>Contenu glissé d'une case à l'autre de la barre.</summary>
    public sealed class ShortcutPayload
    {
        public ShortcutPayload(ShortcutSlotKind kind, long id, int quantity, int fromPosition) { Kind = kind; Id = id; Quantity = quantity; FromPosition = fromPosition; }
        public ShortcutSlotKind Kind { get; }
        public long Id { get; }
        public int Quantity { get; }
        public int FromPosition { get; }
    }

    /// <summary>
    /// Barre de raccourcis du bandeau (<c>MouseShortcuts</c> 246 × 64) : onglets Sorts / Obj., case du corps à corps
    /// (<c>SH0</c>, <c>²</c>) et 14 cases de 25 × 25 sur deux rangées (<c>SH1</c>…<c>SH7</c>, puis <c>SH8</c>…<c>SH14</c> avec Ctrl).
    /// <list type="bullet">
    /// <item>Sorts : la case d'un sort vient de l'ordre local (<see cref="BotOptions.SpellBar"/>), puis de la position de
    /// <c>SL</c> ; les sorts sans case remplissent les cases libres, le reste passe sur les pages suivantes (‹ ›).
    /// Glisser un sort sur une case envoie <c>SM&lt;id&gt;|&lt;case&gt;</c> (<c>Spells.moveToUsed</c>) et garde l'ordre
    /// localement : StarLoco ne répond que <c>BN</c> (matrice §2 n°28).</item>
    /// <item>Objets : objets de l'inventaire aux positions 35 à 48 (case + <c>ITEM_OFFSET</c> 34) ; glisser envoie
    /// <c>OM&lt;id&gt;|&lt;case + 34&gt;|&lt;quantité&gt;</c> (<c>Items.movement</c>, pile entière comme la valeur par défaut
    /// de <c>PopupQuantity</c>), un clic utilise l'objet (<c>OU&lt;id&gt;|</c>).</item>
    /// <item>Corps à corps : le bot n'envoie pas encore l'attaque à l'arme (<c>GA303</c>) ; la case l'explique.</item>
    /// </list>
    /// </summary>
    public sealed class ShortcutBar : Control
    {
        public const int Slots = 14;
        public const int ItemOffset = 34;
        public const string DragFormat = "AzurClientRetro.Shortcut";
        private const int Cell = SpellShortcutButton.CellSize, Gap = 2, TabWidth = 28;
        private readonly Accounts account;
        private readonly BotOptions options;
        private readonly List<Button> slots = new List<Button>();
        private readonly Dictionary<Button, short> spellIds = new Dictionary<Button, short>();
        private readonly Dictionary<Button, uint> itemIds = new Dictionary<Button, uint>();
        private readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>(StringComparer.Ordinal);
        private readonly HashSet<string> requested = new HashSet<string>(StringComparer.Ordinal);
        private readonly SpellShortcutButton closeCombat;
        private readonly Button spellsTab, itemsTab, previous, next;
        private readonly ToolTip tips = new ToolTip();
        private ShortcutTab tab = ShortcutTab.Spells;
        private int page, pageCount = 1;
        private Point dragOrigin;
        private SpellShortcutButton dragSource;
        private ContextMenuStrip lastMenu;
        private readonly int uiThread = System.Threading.Thread.CurrentThread.ManagedThreadId;

        public ShortcutBar(Accounts account, BotOptions options)
        {
            this.account = account; this.options = options;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            // Onglets, corps à corps, 7 colonnes de cases, puis la colonne des pages (sorts au-delà de 14, ajout du bot).
            BackColor = BotUi.FrameLight; Size = new Size(TabWidth + Gap + 9 * (Cell + Gap), 2 * (Cell + Gap) + 2);
            AccessibleName = "Barre de raccourcis";
            spellsTab = TabButton(BannerArt.Text("BANNER_TAB_SPELLS", "Sorts"), ShortcutTab.Spells);
            itemsTab = TabButton(BannerArt.Text("BANNER_TAB_ITEMS", "Obj."), ShortcutTab.Items);
            closeCombat = new SpellShortcutButton { Font = BotFonts.Get(6), Kind = ShortcutSlotKind.CloseCombat, Position = 0, Shortcut = "²",
                Occupied = true, Available = false, Fallback = "CàC", AccessibleName = "Corps à corps" };
            closeCombat.Click += (s, e) => Report(CloseCombatText);
            tips.SetToolTip(closeCombat, CloseCombatText);
            Controls.Add(closeCombat);
            for (int i = 0; i < Slots; i++)
            {
                var slot = new SpellShortcutButton { Font = BotFonts.Get(6), Position = i + 1, Shortcut = KeyLabel(i + 1) };
                slot.Clear("Case de raccourci " + (i + 1).ToString(CultureInfo.InvariantCulture) + " vide");
                slot.Click += (s, e) => Activate(((SpellShortcutButton)s).Position);
                slot.MouseDown += OnSlotMouseDown;
                slot.MouseMove += OnSlotMouseMove;
                slot.MouseUp += OnSlotMouseUp;
                slot.AllowDrop = true;
                slot.DragEnter += OnSlotDragOver; slot.DragOver += OnSlotDragOver; slot.DragDrop += OnSlotDragDrop;
                slots.Add(slot); Controls.Add(slot);
            }
            previous = PageButton("‹", -1, "Page précédente de sorts");
            next = PageButton("›", 1, "Page suivante de sorts");
            Controls.Add(spellsTab); Controls.Add(itemsTab); Controls.Add(previous); Controls.Add(next);
            LayoutCells();
        }

        /// <summary>Les 14 cases (positions 1 à 14), dans l'ordre.</summary>
        public IReadOnlyList<Button> SlotButtons => slots;
        /// <summary>Liste partagée avec la fenêtre de jeu (raccourcis 1 à 14, tests).</summary>
        internal List<Button> SlotList => slots;
        /// <summary>Sort de chaque case de l'onglet Sorts affichée.</summary>
        internal Dictionary<Button, short> SpellIds => spellIds;
        public Button CloseCombatSlot => closeCombat;
        public Button PreviousPage => previous;
        public Button NextPage => next;
        public ShortcutTab Tab { get => tab; set { if (tab == value) return; tab = value; page = 0; RefreshContent(); } }
        public int Page => page;
        public int PageCount => pageCount;
        public ContextMenuStrip LastMenu => lastMenu;
        /// <summary>Sort actuellement choisi pour viser (liseré doré), fourni par la carte.</summary>
        public Func<short?> SelectedSpell { get; set; }

        /// <summary>Clic sur un sort : la fenêtre de jeu passe la carte en mode visée.</summary>
        public event Action<short> SpellClicked;
        /// <summary>« Fiche du sort » du menu d'une case.</summary>
        public event Action<short> SpellDetailsRequested;
        /// <summary>Retour d'action court pour le bandeau.</summary>
        public event Action<string> Feedback;

        public static string CloseCombatText => "Corps à corps (²) : à venir, le bot n'envoie pas encore l'attaque à l'arme (GA303).";

        /// <summary>Clé du personnage pour l'ordre local : identifiant et nom (uniques sur un serveur).</summary>
        public string CharacterKey
        {
            get
            {
                var character = account?.Game?.character;
                return character == null || string.IsNullOrEmpty(character.Name) ? null : character.id.ToString(CultureInfo.InvariantCulture) + "|" + character.Name;
            }
        }

        public ShortcutSlotKind KindAt(int position) => (Slot(position) as SpellShortcutButton)?.Kind ?? ShortcutSlotKind.Empty;
        public short? SpellAt(int position) { Button slot = Slot(position); return slot != null && spellIds.TryGetValue(slot, out short id) ? id : (short?)null; }
        public uint? ItemAt(int position) { Button slot = Slot(position); return slot != null && itemIds.TryGetValue(slot, out uint id) ? id : (uint?)null; }

        /// <summary>
        /// Disposition des sorts : page 0 = 14 cases (ordre local, puis position <c>SL</c>, puis sorts sans case dans les cases
        /// libres), pages suivantes = sorts restants. Calcul pur, sans interface.
        /// </summary>
        public static List<short?[]> LayoutSpells(IEnumerable<Spell> learned, IReadOnlyDictionary<int, short> local)
        {
            Spell[] spells = (learned ?? Enumerable.Empty<Spell>()).Where(s => s != null).OrderBy(SpellPosition).ThenBy(s => s.ID).ToArray();
            var known = new HashSet<short>(spells.Select(s => s.ID));
            var first = new short?[Slots];
            var placed = new HashSet<short>();
            if (local != null)
                foreach (var entry in local.OrderBy(e => e.Key))
                    if (entry.Key >= 1 && entry.Key <= Slots && known.Contains(entry.Value) && first[entry.Key - 1] == null && placed.Add(entry.Value))
                        first[entry.Key - 1] = entry.Value;
            foreach (Spell spell in spells)
            {
                if (placed.Contains(spell.ID) || (local != null && local.Values.Contains(spell.ID))) continue;
                int position = SpellPosition(spell);
                if (position >= 1 && position <= Slots && first[position - 1] == null) { first[position - 1] = spell.ID; placed.Add(spell.ID); }
            }
            var rest = new Queue<short>(spells.Where(s => !placed.Contains(s.ID)).Select(s => s.ID));
            for (int i = 0; i < Slots && rest.Count > 0; i++) if (first[i] == null) first[i] = rest.Dequeue();
            var pages = new List<short?[]> { first };
            while (rest.Count > 0)
            {
                var more = new short?[Slots];
                for (int i = 0; i < Slots && rest.Count > 0; i++) more[i] = rest.Dequeue();
                pages.Add(more);
            }
            return pages;
        }

        /// <summary>Position de <c>SL</c> (caractère du hachage du client, « _ » = aucune).</summary>
        public static int SpellPosition(Spell spell)
        {
            if (spell == null || string.IsNullOrEmpty(spell.Position) || spell.Position == "_") return int.MaxValue;
            int index = Array.IndexOf(Tool_BotProtocol.Utils.Crypto.Hash.caracteres_array, spell.Position[0]);
            return index < 0 ? int.MaxValue : index;
        }

        /// <summary>Recopie sorts, objets, disponibilités et infobulles dans les cases (thread de l'interface).</summary>
        public void RefreshContent()
        {
            if (IsDisposed) return;
            spellsTab.Invalidate(); itemsTab.Invalidate();
            spellIds.Clear(); itemIds.Clear();
            if (tab == ShortcutTab.Spells) RefreshSpells(); else RefreshItems();
        }

        private void RefreshSpells()
        {
            var character = account?.Game?.character;
            Spell[] learned = character?.Spells?.Values.ToArray() ?? new Spell[0];
            string key = CharacterKey;
            List<short?[]> pages = LayoutSpells(learned, key == null || options == null ? null : options.SpellBar(key));
            pageCount = pages.Count; page = Math.Max(0, Math.Min(pageCount - 1, page));
            previous.Visible = next.Visible = pageCount > 1;
            previous.Enabled = page > 0; next.Enabled = page < pageCount - 1;
            tips.SetToolTip(previous, "Page " + (page + 1) + " / " + pageCount + " · page précédente");
            tips.SetToolTip(next, "Page " + (page + 1) + " / " + pageCount + " · page suivante");
            short? selected = SelectedSpell?.Invoke();
            var fight = account?.Game?.Fight;
            for (int i = 0; i < Slots; i++)
            {
                var slot = (SpellShortcutButton)slots[i];
                short? id = pages[page][i];
                if (id == null || character == null || !character.Spells.TryGetValue(id.Value, out Spell spell))
                {
                    slot.Clear("Case de sort " + (i + 1) + " vide");
                    tips.SetToolTip(slot, "Case de sort " + (i + 1) + " vide · glisser un sort ici, ou clic droit pour en placer un");
                    continue;
                }
                spellIds[slot] = spell.ID;
                slot.Kind = ShortcutSlotKind.Spell; slot.Occupied = true; slot.Quantity = 0;
                slot.Icon = Icon("Spells", spell.ID.ToString(CultureInfo.InvariantCulture)); slot.Fallback = spell.ID.ToString(CultureInfo.InvariantCulture);
                slot.SelectedSpell = selected == spell.ID;
                string reason = fight?.GetSpellUnavailableReason(spell.ID);
                slot.Available = reason == null;
                slot.AccessibleName = (spell.Name ?? "Sort #" + spell.ID) + " · niveau " + spell.Level;
                SpellStats data = spell.GetStats();
                string cost = data == null ? "Données du sort absentes" : data.PA + " PA · portée " + data.Min_portee + "–" + data.Max_portee;
                tips.SetToolTip(slot, slot.AccessibleName + "\n" + cost + "\n" + (reason ?? "Cliquer puis choisir une cellule sur la carte")
                    + "\nGlisser vers une autre case : SM · clic droit : fiche du sort · Échap : annuler");
                slot.Invalidate();
            }
        }

        private void RefreshItems()
        {
            pageCount = 1; page = 0; previous.Visible = next.Visible = false;
            InventoryObjects[] items = Items();
            for (int i = 0; i < Slots; i++)
            {
                var slot = (SpellShortcutButton)slots[i];
                int position = i + 1 + ItemOffset;
                InventoryObjects item = items.FirstOrDefault(o => (int)o.position == position);
                if (item == null)
                {
                    slot.Clear("Case d'objet " + (i + 1) + " vide");
                    tips.SetToolTip(slot, "Case d'objet " + (i + 1) + " vide · clic droit pour y placer un objet utilisable");
                    continue;
                }
                itemIds[slot] = item.Inventory_ID;
                slot.Kind = ShortcutSlotKind.Item; slot.Occupied = true; slot.Quantity = item.Qua; slot.SelectedSpell = false;
                slot.Icon = ItemIcon(item); slot.Fallback = (item.Name ?? "?").Substring(0, Math.Min(3, (item.Name ?? "?").Length));
                slot.Available = item.Qua > 0;
                slot.AccessibleName = item.Name + " × " + item.Qua;
                tips.SetToolTip(slot, slot.AccessibleName + "\nClic : utiliser (OU) · glisser vers une autre case : OM · clic droit : menu");
                slot.Invalidate();
            }
        }

        /// <summary>
        /// Raccourci <c>SH&lt;n&gt;</c> ou clic sur une case : sort → mode visée ; objet → <c>OU&lt;id&gt;|</c> ;
        /// <c>SH0</c> → corps à corps (à venir). Renvoie vrai si la case avait un contenu.
        /// </summary>
        public bool Activate(int position)
        {
            if (position == 0) { Report(CloseCombatText); return true; }
            Button slot = Slot(position);
            if (slot == null) return false;
            if (spellIds.TryGetValue(slot, out short spell)) { SpellClicked?.Invoke(spell); return true; }
            if (itemIds.TryGetValue(slot, out uint item)) { _ = UseItemAsync(item); return true; }
            return false;
        }

        /// <summary>Utilise un objet de la barre (<c>Items.use</c> : <c>OU&lt;id&gt;|</c>), avec les vérifications de l'inventaire.</summary>
        public async Task<bool> UseItemAsync(uint inventoryId)
        {
            var inventory = account?.Game?.character?.Inventory;
            InventoryObjects item = inventory?.GetByInventoryId(inventoryId);
            if (item == null) { Report("Cet objet n'est plus dans l'inventaire."); return false; }
            try
            {
                bool sent = await inventory.Use_Item(item);
                Report(sent ? "Utilisation de " + item.Name + " demandée." : "Objet non utilisé : voir le journal de l'inventaire.");
                return sent;
            }
            catch (Exception error) when (error is InvalidOperationException || error is ObjectDisposedException || error is System.IO.IOException || error is System.Net.Sockets.SocketException)
            {
                Report("Envoi impossible : " + error.Message); return false;
            }
        }

        /// <summary>
        /// Sort déposé sur la case <paramref name="position"/> (1 à 14) : <c>SM&lt;id&gt;|&lt;position&gt;</c> puis ordre local
        /// enregistré dans les options. Rien n'est envoyé si le sort y est déjà ou si la connexion manque.
        /// </summary>
        public async Task<bool> DropSpellAsync(short spellId, int position)
        {
            if (position < 1 || position > Slots) return false;
            var character = account?.Game?.character;
            if (character?.Spells == null || !character.Spells.TryGetValue(spellId, out Spell learned)) { Report("Ce sort n'est pas appris."); return false; }
            string key = CharacterKey;
            if (key == null || options == null) { Report("Personnage pas encore chargé."); return false; }
            // Comme MouseShortcuts : rien n'est envoyé si le sort est déjà à cette position (ordre local ou position SL).
            if (tab == ShortcutTab.Spells && page == 0 && SpellAt(position) == spellId
                && ((options.SpellBar(key).TryGetValue(position, out short known) && known == spellId) || SpellPosition(learned) == position)) return false;
            if (!Connected) { Report("Connectez le personnage avant de ranger la barre de sorts."); return false; }
            try { await account.Connexion.SendPacket("SM" + spellId.ToString(CultureInfo.InvariantCulture) + "|" + position.ToString(CultureInfo.InvariantCulture)); }
            catch (Exception error) when (error is InvalidOperationException || error is ObjectDisposedException || error is System.IO.IOException || error is System.Net.Sockets.SocketException)
            {
                Report("Envoi impossible : " + error.Message); return false;
            }
            options.SetSpellSlot(key, position, spellId);
            OnUi(RefreshContent);
            return true;
        }

        /// <summary>
        /// Objet déposé sur la case <paramref name="position"/> (1 à 14) : <c>OM&lt;id&gt;|&lt;position + 34&gt;|&lt;quantité&gt;</c>.
        /// La case change à la réponse <c>OM</c> du serveur (positions 35 à 48 acceptées pour les objets utilisables).
        /// </summary>
        public async Task<bool> DropItemAsync(uint inventoryId, int position, int quantity)
        {
            if (position < 1 || position > Slots) return false;
            InventoryObjects item = account?.Game?.character?.Inventory?.GetByInventoryId(inventoryId);
            if (item == null) { Report("Cet objet n'est plus dans l'inventaire."); return false; }
            int target = position + ItemOffset;
            if ((int)item.position == target) return false;
            if (!IsShortcutItem(item)) { Report(BannerArt.Text("CANT_MOVE_ITEM_HERE", "Impossible de déplacer cet objet ici.")); return false; }
            if (!Connected) { Report("Connectez le personnage avant de ranger la barre d'objets."); return false; }
            quantity = Math.Max(1, Math.Min(item.Qua, quantity));
            return await Send("OM" + inventoryId.ToString(CultureInfo.InvariantCulture) + "|" + target.ToString(CultureInfo.InvariantCulture) + "|" + quantity.ToString(CultureInfo.InvariantCulture),
                item.Name + " placé dans la case " + position + " (le serveur confirme par OM).");
        }

        /// <summary>Objet utilisable selon les textes du client (<c>u</c> de <c>items_fr</c>) : seuls ceux-là vont dans la barre.</summary>
        public static bool IsShortcutItem(InventoryObjects item)
        {
            if (item == null) return false;
            IReadOnlyDictionary<string, string> data = LangData.Raw("items", "objet", item.ID.ToString(CultureInfo.InvariantCulture));
            return data != null && data.TryGetValue("utilisable", out string usable) && usable == "true";
        }

        private InventoryObjects[] Items()
        {
            try { return account?.Game?.character?.Inventory?.Objets.ToArray() ?? new InventoryObjects[0]; }
            catch (InvalidOperationException) { return new InventoryObjects[0]; }
        }

        private bool Connected => account?.Connexion != null && account.Connexion.IsConnected();

        private async Task<bool> Send(string packet, string done)
        {
            try { await account.Connexion.SendPacket(packet); Report(done); return true; }
            catch (Exception error) when (error is InvalidOperationException || error is ObjectDisposedException || error is System.IO.IOException || error is System.Net.Sockets.SocketException)
            {
                Report("Envoi impossible : " + error.Message); return false;
            }
        }

        private void Report(string message) { if (!string.IsNullOrEmpty(message)) Feedback?.Invoke(message); }

        private void OnUi(Action action)
        {
            if (IsDisposed) return;
            Control target = IsHandleCreated ? (Control)this : FindForm();
            // Sans poignée de fenêtre, seul le fil qui a créé la barre touche aux cases (le prochain rafraîchissement suit sinon).
            if (target == null || !target.IsHandleCreated) { if (System.Threading.Thread.CurrentThread.ManagedThreadId == uiThread) action(); return; }
            BotUi.OnUi(target, () => { if (!IsDisposed) action(); });
        }

        private Bitmap Icon(string family, string name)
        {
            string key = family + "/" + name;
            lock (icons)
            {
                if (icons.TryGetValue(key, out Bitmap image)) return image;
                if (!requested.Add(key)) return null;
            }
            // Image déjà en cache : rendue tout de suite, sans relancer RefreshContent depuis RefreshContent.
            bool inline = true;
            BannerArt.Request(this, family, name, loaded =>
            {
                lock (icons) icons[key] = loaded;
                // Sans fenêtre, l'image attend le prochain rafraîchissement ; sinon on est sur le thread de l'interface.
                if (!inline && loaded != null && IsHandleCreated && !InvokeRequired) RefreshContent();
            });
            inline = false;
            lock (icons) return icons.TryGetValue(key, out Bitmap cached) ? cached : null;
        }

        private Bitmap ItemIcon(InventoryObjects item)
        {
            int? gfx = LangData.Item.Gfx(item.ID);
            int type = LangData.Item.Type(item.ID) ?? item.Type;
            return gfx == null ? null : Icon("Items", type.ToString(CultureInfo.InvariantCulture) + "/" + gfx.Value.ToString(CultureInfo.InvariantCulture));
        }

        private Button Slot(int position) => position >= 1 && position <= slots.Count ? slots[position - 1] : null;

        // AZERTY : & é " ' ( - è, puis les mêmes avec Ctrl (libellés du client « Ctrl+& »… abrégés).
        private static string KeyLabel(int position) => position <= 7 ? "&é\"'(-è"[position - 1].ToString() : "^" + "&é\"'(-è"[position - 8];

        private Button TabButton(string text, ShortcutTab value)
        {
            var button = new TabLabelButton(text, () => tab == value) { Size = new Size(TabWidth, Cell + 1), Tag = "client-icon", Font = BotFonts.Get(7),
                AccessibleName = "Onglet " + text };
            button.Click += (s, e) => Tab = value;
            tips.SetToolTip(button, text + " (touche < pour basculer)");
            return button;
        }

        private Button PageButton(string text, int direction, string name)
        {
            var button = new TabLabelButton(text, () => false) { Size = new Size(Cell, Cell), Font = BotFonts.Get(9, FontStyle.Bold), AccessibleName = name, Visible = false,
                Tag = "client-icon" };
            button.Click += (s, e) => { page = Math.Max(0, Math.Min(pageCount - 1, page + direction)); RefreshContent(); };
            return button;
        }

        private void LayoutCells()
        {
            spellsTab.Location = new Point(0, 1); itemsTab.Location = new Point(0, Cell + Gap + 1);
            int left = TabWidth + Gap;
            closeCombat.Location = new Point(left, 1);
            int pages = left + 8 * (Cell + Gap);
            previous.Location = new Point(pages, 1); next.Location = new Point(pages, Cell + Gap + 1);
            for (int i = 0; i < Slots; i++)
                slots[i].Location = new Point(left + (i % 7 + 1) * (Cell + Gap), 1 + (i / 7) * (Cell + Gap));
        }

        private void OnSlotMouseDown(object sender, MouseEventArgs e)
        {
            var slot = (SpellShortcutButton)sender;
            if (e.Button == MouseButtons.Left && slot.Occupied) { dragSource = slot; dragOrigin = e.Location; }
        }

        private void OnSlotMouseMove(object sender, MouseEventArgs e)
        {
            var slot = (SpellShortcutButton)sender;
            if (dragSource != slot || e.Button != MouseButtons.Left) return;
            Size threshold = SystemInformation.DragSize;
            if (Math.Abs(e.X - dragOrigin.X) < threshold.Width && Math.Abs(e.Y - dragOrigin.Y) < threshold.Height) return;
            dragSource = null;
            ShortcutPayload payload = PayloadOf(slot);
            if (payload == null) return;
            var data = new DataObject(); data.SetData(DragFormat, payload);
            slot.DoDragDrop(data, DragDropEffects.Move);
        }

        private void OnSlotMouseUp(object sender, MouseEventArgs e)
        {
            dragSource = null;
            if (e.Button == MouseButtons.Right) ShowMenu((SpellShortcutButton)sender, e.Location);
        }

        private ShortcutPayload PayloadOf(SpellShortcutButton slot)
        {
            if (spellIds.TryGetValue(slot, out short spell)) return new ShortcutPayload(ShortcutSlotKind.Spell, spell, 1, slot.Position);
            if (itemIds.TryGetValue(slot, out uint item))
                return new ShortcutPayload(ShortcutSlotKind.Item, item, Math.Max(1, slot.Quantity), slot.Position);
            return null;
        }

        private void OnSlotDragOver(object sender, DragEventArgs e)
        {
            var payload = e.Data?.GetData(DragFormat) as ShortcutPayload;
            bool fits = payload != null && (payload.Kind == ShortcutSlotKind.Spell ? tab == ShortcutTab.Spells : tab == ShortcutTab.Items);
            e.Effect = fits ? DragDropEffects.Move : DragDropEffects.None;
        }

        private void OnSlotDragDrop(object sender, DragEventArgs e)
        {
            var payload = e.Data?.GetData(DragFormat) as ShortcutPayload;
            _ = Drop(payload, ((SpellShortcutButton)sender).Position);
        }

        /// <summary>Dépôt d'un contenu glissé sur une case (glisser-déposer de la barre).</summary>
        public Task<bool> Drop(ShortcutPayload payload, int position)
        {
            if (payload == null || payload.FromPosition == position && page == 0) return Task.FromResult(false);
            if (payload.Kind == ShortcutSlotKind.Spell) return DropSpellAsync((short)payload.Id, position);
            if (payload.Kind == ShortcutSlotKind.Item) return DropItemAsync((uint)payload.Id, position, payload.Quantity);
            return Task.FromResult(false);
        }

        private void ShowMenu(SpellShortcutButton slot, Point location)
        {
            var menu = new ContextMenuStrip { Renderer = new RetroMenuRenderer(), BackColor = BotUi.Paper, Font = BotFonts.Get(9), ShowImageMargin = false };
            menu.Closed += (s, e) => BeginDispose(menu);
            if (tab == ShortcutTab.Spells)
            {
                if (spellIds.TryGetValue(slot, out short spell))
                    menu.Items.Add(new ToolStripMenuItem("Fiche du sort", null, (s, e) => SpellDetailsRequested?.Invoke(spell)));
                var place = new ToolStripMenuItem("Placer un sort ici >>");
                foreach (Spell learned in (account?.Game?.character?.Spells?.Values.ToArray() ?? new Spell[0]).OrderBy(x => x.Name, StringComparer.CurrentCulture))
                {
                    short id = learned.ID; int position = slot.Position;
                    place.DropDownItems.Add(new ToolStripMenuItem(learned.Name ?? "Sort #" + id, null, (s, e) => _ = DropSpellAsync(id, position)));
                }
                place.Enabled = place.DropDownItems.Count > 0;
                menu.Items.Add(place);
            }
            else
            {
                if (itemIds.TryGetValue(slot, out uint item))
                {
                    menu.Items.Add(new ToolStripMenuItem("Utiliser", null, (s, e) => _ = UseItemAsync(item)));
                    InventoryObjects held = account?.Game?.character?.Inventory?.GetByInventoryId(item);
                    if (held != null)
                        menu.Items.Add(new ToolStripMenuItem("Retirer de la barre", null, (s, e) => _ = Send("OM" + item.ToString(CultureInfo.InvariantCulture) + "|" + ((int)InventorySlots.NOT_EQUIPPED).ToString(CultureInfo.InvariantCulture) + "|" + Math.Max(1, held.Qua).ToString(CultureInfo.InvariantCulture),
                            held.Name + " rangé dans l'inventaire (le serveur confirme par OM).")));
                }
                var place = new ToolStripMenuItem("Placer un objet ici >>");
                foreach (InventoryObjects candidate in Items().Where(o => o.position == InventorySlots.NOT_EQUIPPED && IsShortcutItem(o)).OrderBy(o => o.Name, StringComparer.CurrentCulture).Take(40))
                {
                    uint id = candidate.Inventory_ID; int position = slot.Position, quantity = candidate.Qua;
                    place.DropDownItems.Add(new ToolStripMenuItem(candidate.Name + " × " + candidate.Qua, null, (s, e) => _ = DropItemAsync(id, position, quantity)));
                }
                place.Enabled = place.DropDownItems.Count > 0;
                if (!place.Enabled) place.ToolTipText = "Aucun objet utilisable dans l'inventaire.";
                menu.Items.Add(place);
            }
            lastMenu = menu;
            menu.Show(slot, location);
        }

        private static void BeginDispose(ContextMenuStrip menu)
        {
            try { if (menu.IsHandleCreated) menu.BeginInvoke((Action)menu.Dispose); else menu.Dispose(); }
            catch (InvalidOperationException) { menu.Dispose(); }
        }

        // Les cases ont leur propre menu (MouseUp droit) : le clic droit ne remonte pas jusqu'au menu global du bandeau.
        protected override void WndProc(ref Message m)
        {
            const int WM_CONTEXTMENU = 0x7B;
            if (m.Msg == WM_CONTEXTMENU) { m.Result = IntPtr.Zero; return; }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { tips.Dispose(); icons.Clear(); }
            base.Dispose(disposing);
        }

        /// <summary>Bouton plat du bandeau (onglets, pages) dessiné dans la palette Retro.</summary>
        private sealed class TabLabelButton : Button
        {
            private readonly Func<bool> selected;
            internal TabLabelButton(string text, Func<bool> selected)
            {
                this.selected = selected; Text = text;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; TabStop = false; Margin = new Padding(0); Cursor = Cursors.Hand;
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                bool on = selected();
                e.Graphics.Clear(on ? BotUi.Paper : Color.FromArgb(67, 60, 43));
                using (var border = new Pen(BotUi.Gold)) e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? (on ? BotUi.Ink : BotUi.PaperLight) : BotUi.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);
            }
        }
    }
}
