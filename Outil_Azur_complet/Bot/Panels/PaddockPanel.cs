using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Montures;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Enclos de la carte : état annoncé par <c>Rp</c> (public, privé, à vendre, guilde, places et objets), objets d'élevage
    /// posés (<c>GDO</c>, retrait <c>Ro&lt;cellule&gt;</c> dans un enclos de sa guilde), étable et enclos de l'interface
    /// <c>MountStorage</c> (ouverts par « Accéder », <c>ECK16|&lt;étable&gt;~&lt;enclos&gt;</c>, tenus à jour par <c>Ee</c> / <c>Ef</c> ;
    /// déplacements <c>Erp</c>, <c>Erg</c>, <c>Erc</c>, <c>ErC</c>, <c>Efp</c>, <c>Efg</c> ; fermeture <c>EV</c>) et fenêtre
    /// <c>MountParkSale</c> (<c>RD</c> : mise en vente <c>Rs&lt;prix&gt;</c>, annulation <c>Rs0</c>, achat <c>Rb&lt;prix&gt;</c> après
    /// <c>DO_U_BUY_MOUNTPARK</c>, fermeture <c>Rv</c>). Le volet s'ouvre de lui-même sur <c>ECK16</c> et <c>RD</c>.
    /// </summary>
    public sealed class PaddockPanel : GamePanel
    {
        private const string Reference = "ENCLOS";
        private const string NoParkText = "Aucun enclos annoncé sur cette carte (Rp). Utilisez « Accéder », « Acheter » ou « Vendre » sur l'enclos : le volet s'ouvre à la réponse du serveur (ECK16 ou RD).";
        public const int ShedTab = 0, ParkTab = 1, CertificatesTab = 2, EquippedTab = 3;
        private static readonly MountLocation[] TabLocations = { MountLocation.Shed, MountLocation.Park, MountLocation.Certificate, MountLocation.Equipped };
        private Label parkTitle, parkDetails, status, saleTitle, saleInfo;
        private ListView objects, mounts;
        private Control removeObject, saleValidate, saleCancel, saleBuy, saleClose, toShed, toPark, toEquipped, toCertificate, viewCertificate, shedClose;
        private NumericUpDown salePrice;
        private Panel objectsArea, saleArea, shedArea;
        private MountTabStrip shedTabs;
        private MountSheet selectedSheet;
        private MountActions bound;
        private ShedExchange boundShed;
        private InventoryClass inventory;
        private Tool_BotProtocol.Game.Maps.Map boundMap;
        private bool wasOpen, saleWasOpen;
        private Form buyDialog;

        public override string Title => MountTexts.Get("MOUNT_PARK", "Enclos");
        public override Image Icon => ClientAssets.Icon("icone-monture", 24);
        public override bool IsModal => IsServerWindowOpen;
        /// <summary>Étable ouverte (<c>ECK16</c>) ou fenêtre de vente (<c>RD</c>).</summary>
        public override bool IsServerWindowOpen => ShedOpen || SaleOpen;
        public bool ShedOpen => Game?.Interactions?.Mount?.Shed?.IsOpen == true;
        public bool SaleOpen => Game?.Interactions?.Mount?.IsOpen == true;
        /// <summary>Onglet de l'étable affiché.</summary>
        public int CurrentShedTab => shedTabs?.Selected ?? ShedTab;
        public ListView MountList => mounts;
        public ListView ObjectList => objects;
        public MountSheet SelectedSheet => selectedSheet;
        public Form BuyDialog => buyDialog;

        protected override Control CreateView()
        {
            var page = Page();
            var sheet = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true, BackColor = BotUi.Paper, Margin = new Padding(0), Name = "paddock-sheet" };
            sheet.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            parkTitle = MakeLabel(MountTexts.Get("MOUNT_PARK", "Enclos"), 11, true); parkTitle.Name = "paddock-title";
            AddRow(sheet, parkTitle, 26);
            parkDetails = MakeLabel(string.Empty, 9); parkDetails.ForeColor = BotUi.Muted; parkDetails.Name = "paddock-details";
            AddRow(sheet, parkDetails, 38);

            saleArea = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Visible = false, Name = "paddock-sale", Margin = new Padding(0) };
            saleTitle = MakeLabel(MountTexts.Get("MOUNTPARK_PURCHASE", "Achat d'un enclos"), 9, true); saleTitle.Dock = DockStyle.Top; saleTitle.Height = 22;
            saleInfo = MakeLabel(string.Empty, 9); saleInfo.Dock = DockStyle.Top; saleInfo.Height = 40;
            salePrice = new NumericUpDown { Minimum = 0, Maximum = int.MaxValue, Width = 120, Height = 34, ThousandsSeparator = true, Font = BotFonts.Get(9), BackColor = BotUi.PaperLight,
                ForeColor = BotUi.Ink, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(2, 7, 6, 0), AccessibleName = "Prix de vente de l'enclos", Name = "paddock-price" };
            saleValidate = MakeButton(MountTexts.Get("VALIDATE", "Valider"), async (s, e) => await ReportAsync(() => Game.Interactions.Mount.SellParkAsync((int)Math.Min(int.MaxValue, salePrice.Value))), true, 84);
            saleCancel = MakeButton(MountTexts.Get("CANCEL_THE_SALE", "Annuler la vente"), async (s, e) => await ReportAsync(() => Game.Interactions.Mount.SellParkAsync(0)), false, 120);
            saleBuy = MakeButton(MountTexts.Get("BUY", "Acheter"), async (s, e) => await AskBuy(), true, 84);
            saleClose = MakeButton(MountTexts.Get("CLOSE", "Fermer"), async (s, e) => await ReportAsync(() => Game.Interactions.Mount.CloseSaleAsync()), false, 80);
            saleArea.Controls.Add(BotUi.Actions(salePrice, saleValidate, saleCancel, saleBuy, saleClose)); saleArea.Controls.Add(saleInfo); saleArea.Controls.Add(saleTitle);
            AddRow(sheet, saleArea, 110);

            shedArea = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Visible = false, Name = "paddock-shed", Margin = new Padding(0) };
            shedTabs = new MountTabStrip(MountTexts.Get("MOUNT_SHED", "Etable"), MountTexts.Get("MOUNT_PARK", "Enclos"), MountTexts.Get("MOUNT_CERTIFICATES", "Certificats"), "Équipée")
                { Dock = DockStyle.Top, Name = "paddock-tabs" };
            shedTabs.TabClicked += (s, index) => { shedTabs.Selected = index; RefreshView(); };
            mounts = MakeList(9, MountTexts.Get("NAME_BIG", "Nom"), "Modèle", "Niv.", "Sexe");
            mounts.Dock = DockStyle.Top; mounts.Height = 120; mounts.Name = "paddock-mounts";
            mounts.Columns[0].Width = 110; mounts.Columns[1].Width = 150; mounts.Columns[2].Width = 40; mounts.Columns[3].Width = 60;
            mounts.SelectedIndexChanged += (s, e) => RefreshSelection();
            selectedSheet = new MountSheet { Dock = DockStyle.Top, Name = "paddock-selected", EmptyText = "Choisissez une monture de la liste." };
            toEquipped = MakeButton(MountTexts.Get("MOUNT_INVENTORY_ACTION", "Equiper"), async (s, e) => await Move(MountLocation.Equipped), false, 80);
            toShed = MakeButton(MountTexts.Get("MOUNT_SHED_ACTION", "Stocker"), async (s, e) => await Move(MountLocation.Shed), false, 80);
            toPark = MakeButton(MountTexts.Get("MOUNT_PARK_ACTION", "Elever"), async (s, e) => await Move(MountLocation.Park), false, 80);
            toCertificate = MakeButton(MountTexts.Get("MOUNT_CERTIFICATE_ACTION", "Echanger"), async (s, e) => await Move(MountLocation.Certificate), false, 84);
            viewCertificate = MakeButton("Voir la fiche", async (s, e) => await ViewCertificate(), false, 96);
            shedClose = MakeButton(MountTexts.Get("CLOSE", "Fermer"), async (s, e) => await ReportAsync(() => Game.Interactions.Mount.Shed.LeaveAsync()), false, 80);
            var shedActions = BotUi.Actions(toEquipped, toShed, toPark, toCertificate, viewCertificate, shedClose);
            shedActions.Dock = DockStyle.Top;
            shedArea.Controls.Add(selectedSheet); shedArea.Controls.Add(shedActions); shedArea.Controls.Add(mounts); shedArea.Controls.Add(shedTabs);
            AddRow(sheet, shedArea, 520);

            objectsArea = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Name = "paddock-objects", Margin = new Padding(0) };
            var objectsTitle = MakeLabel("Objets d'élevage", 9, true); objectsTitle.Dock = DockStyle.Top; objectsTitle.Height = 22;
            objects = MakeList(9, "Objet", "Cellule", "Durabilité");
            objects.Columns[0].Width = 170; objects.Columns[1].Width = 60; objects.Columns[2].Width = 90;
            objects.SelectedIndexChanged += (s, e) => UpdateButtons();
            removeObject = MakeButton(MountTexts.Get("REMOVE", "Retirer"), async (s, e) => await RemoveSelected(), false, 90);
            objectsArea.Controls.Add(objects); objectsArea.Controls.Add(objectsTitle); objectsArea.Controls.Add(BotUi.Actions(removeObject));
            AddRow(sheet, objectsArea, 170);

            status = MakeStatus(NoParkText);
            page.Controls.Add(sheet); page.Controls.Add(status);
            return page;
        }

        private static void AddRow(TableLayoutPanel sheet, Control control, int height)
        {
            sheet.RowCount = sheet.RowStyles.Count + 1;
            sheet.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            control.Dock = DockStyle.Fill; control.Height = height;
            sheet.Controls.Add(control, 0, sheet.RowStyles.Count - 1);
        }

        protected override void OnBind(GameClass game)
        {
            bound = game.Interactions?.Mount;
            if (bound != null) bound.Changed += OnServerChanged;
            boundShed = bound?.Shed;
            if (boundShed != null) boundShed.Changed += OnServerChanged;
            if (game.Interactions?.Interactive != null) game.Interactions.Interactive.Changed += OnServerChanged;
            inventory = game.character?.Inventory;
            if (inventory != null) inventory.RefreshInventory += OnInventoryChanged;
            boundMap = game.Map;
            if (boundMap != null) { boundMap.RefreshMap += OnServerChanged; boundMap.GroundObjectChanged += OnGroundObject; }
            wasOpen = saleWasOpen = false;
        }

        protected override void OnUnbind(GameClass game)
        {
            if (bound != null) bound.Changed -= OnServerChanged;
            if (boundShed != null) boundShed.Changed -= OnServerChanged;
            if (game?.Interactions?.Interactive != null) game.Interactions.Interactive.Changed -= OnServerChanged;
            if (inventory != null) inventory.RefreshInventory -= OnInventoryChanged;
            if (boundMap != null) { boundMap.RefreshMap -= OnServerChanged; boundMap.GroundObjectChanged -= OnGroundObject; }
            bound = null; boundShed = null; inventory = null; boundMap = null;
            wasOpen = saleWasOpen = false;
            CloseBuyDialog();
        }

        private void OnServerChanged()
        {
            if (!ExchangePanel.PostToForm(Host, () => { if (!IsDisposed) ApplyServerChange(); })) OnUi(ApplyServerChange);
        }

        private void OnGroundObject(int cell) => OnServerChanged();

        private void OnInventoryChanged(bool changed)
        {
            if (!changed) return;
            if (!ExchangePanel.PostToForm(Host, () => { if (!IsDisposed) RefreshView(); })) OnUi(RefreshView);
        }

        private void ApplyServerChange()
        {
            if (Game == null || parkTitle == null) return;
            bool open = IsServerWindowOpen, sale = SaleOpen;
            if (sale && !saleWasOpen)
            {
                MountActions mount = Game.Interactions.Mount;
                salePrice.Value = Math.Max(0, Math.Min(int.MaxValue, mount.SaleDefaultPrice));
            }
            if (!sale && saleWasOpen) CloseBuyDialog();
            if (open && !wasOpen) RequestShow();
            else if (!open && wasOpen) RaiseClosed();
            wasOpen = open; saleWasOpen = sale;
            RefreshView();
        }

        public override void RefreshView()
        {
            if (Game == null || parkTitle == null) return;
            MountActions mount = Game.Interactions?.Mount;
            if (mount == null) return;
            MountParkInfo park = mount.Park;
            bool mine = mount.IsParkMine;
            parkTitle.Text = park == null ? MountTexts.Get("MOUNT_PARK", "Enclos") : park.Describe().Replace("\n", " ").Replace("\r", string.Empty);
            parkDetails.Text = park == null ? "Aucun enclos sur cette carte." :
                park.Size.ToString(CultureInfo.InvariantCulture) + " montures · " + park.Items.ToString(CultureInfo.InvariantCulture) + " objets d'élevage"
                + (park.GuildName.Length > 0 ? " · " + park.GuildName + (mine ? " (votre guilde)" : string.Empty) : string.Empty);
            RefreshSale(mount, park, mine);
            RefreshShed(mount);
            RefreshObjects(mount, mine);
            string message = ShedOpen ? mount.Shed.LastMessage : mount.LastMessage;
            status.Text = message.Length > 0 ? message : park == null ? NoParkText : mine ? "Enclos de votre guilde." : "Enclos d'une autre guilde ou public.";
        }

        private void RefreshSale(MountActions mount, MountParkInfo park, bool mine)
        {
            bool open = mount.IsOpen;
            saleArea.Visible = open;
            if (!open) return;
            bool connected = Connected;
            saleTitle.Text = mine ? MountTexts.Get("MOUNTPARK_SALE", "Mise en vente de l'enclos") : MountTexts.Get("MOUNTPARK_PURCHASE", "Achat d'un enclos");
            string description = park == null ? string.Empty : MountTexts.Get("MOUNTPARK_DESCRIPTION", "Cet enclos peut contenir " + park.Size + " montures et " + park.Items + " objets d'élevage.",
                park.Size.ToString(CultureInfo.InvariantCulture), park.Items.ToString(CultureInfo.InvariantCulture)).Replace("\n", " ").Replace("  ", " ");
            saleInfo.Text = description + (mine ? string.Empty : "\n" + MountTexts.Get("PRICE", "Prix") + " : " + (park?.Price ?? 0).ToString("N0", CultureInfo.CurrentCulture) + " kamas");
            salePrice.Visible = saleValidate.Visible = mine;
            saleCancel.Visible = mine && park != null && park.Price != 0;
            saleBuy.Visible = !mine;
            saleValidate.Enabled = saleCancel.Enabled = connected;
            saleBuy.Enabled = connected && park != null && park.Price > 0 && buyDialog == null;
            saleClose.Enabled = connected;
        }

        private void RefreshShed(MountActions mount)
        {
            ShedExchange shed = mount.Shed;
            bool open = shed != null && shed.IsOpen;
            shedArea.Visible = open;
            if (!open) return;
            shedTabs.SetBadge(ShedTab, shed.ShedMounts.Count > 0);
            shedTabs.SetBadge(ParkTab, shed.ParkMounts.Count > 0);
            shedTabs.SetBadge(CertificatesTab, shed.Certificates.Count > 0);
            shedTabs.SetBadge(EquippedTab, mount.HasMount);
            long selected = SelectedId();
            mounts.BeginUpdate(); mounts.Items.Clear();
            MountLocation location = TabLocations[CurrentShedTab];
            if (location == MountLocation.Certificate)
            {
                foreach (InventoryObjects item in shed.Certificates.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    var row = mounts.Items.Add(item.Name); row.SubItems.Add(MountTexts.Get("MOUNT_CERTIFICATES", "Certificat")); row.SubItems.Add(string.Empty); row.SubItems.Add(string.Empty);
                    row.Tag = (long)item.Inventory_ID;
                    if (item.Inventory_ID == selected) row.Selected = true;
                }
            }
            else
            {
                IEnumerable<Mount> list = location == MountLocation.Shed ? shed.ShedMounts : location == MountLocation.Park ? shed.ParkMounts
                    : mount.Current != null ? new[] { mount.Current } : new Mount[0];
                foreach (Mount entry in list.OrderBy(item => item.ModelId).ThenBy(item => item.Id))
                {
                    var row = mounts.Items.Add(entry.DisplayName + (entry.NewBorn ? " (nouveau-né)" : string.Empty));
                    row.SubItems.Add(entry.ModelName); row.SubItems.Add(entry.Level.ToString(CultureInfo.InvariantCulture)); row.SubItems.Add(entry.SexText);
                    row.Tag = (long)entry.Id;
                    if (entry.Id == selected) row.Selected = true;
                }
            }
            mounts.EndUpdate();
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            if (selectedSheet == null || Game == null) return;
            MountActions mount = Game.Interactions?.Mount;
            ShedExchange shed = mount?.Shed;
            long id = SelectedId();
            MountLocation location = TabLocations[CurrentShedTab];
            Mount shown = null;
            if (shed != null && id != 0)
            {
                if (location == MountLocation.Shed) shown = shed.ShedMounts.FirstOrDefault(entry => entry.Id == id);
                else if (location == MountLocation.Park) shown = shed.ParkMounts.FirstOrDefault(entry => entry.Id == id);
                else if (location == MountLocation.Equipped) shown = mount.Current;
            }
            selectedSheet.SetMount(shown, null, false, location == MountLocation.Certificate && id != 0 ? "Certificat : « Voir la fiche » demande sa monture au serveur (Rd)." : null);
            selectedSheet.FitWidth(shedArea.ClientSize.Width);
            bool open = Connected && shed != null && shed.IsOpen;
            bool chosen = id != 0;
            toEquipped.Enabled = open && chosen && ShedExchange.MovePackets(location, MountLocation.Equipped, id).Count > 0 && !mount.HasMount;
            toShed.Enabled = open && chosen && ShedExchange.MovePackets(location, MountLocation.Shed, id).Count > 0;
            toPark.Enabled = open && chosen && ShedExchange.MovePackets(location, MountLocation.Park, id).Count > 0;
            toCertificate.Enabled = open && chosen && ShedExchange.MovePackets(location, MountLocation.Certificate, id).Count > 0;
            viewCertificate.Enabled = open && chosen && location == MountLocation.Certificate;
            shedClose.Enabled = open;
        }

        private void RefreshObjects(MountActions mount, bool mine)
        {
            objectsArea.Visible = mount.Park != null;
            if (!objectsArea.Visible) return;
            int selected = objects.SelectedItems.Count == 0 ? -1 : (int)objects.SelectedItems[0].Tag;
            objects.BeginUpdate(); objects.Items.Clear();
            Tool_BotProtocol.Game.Maps.Map map = Game.Map;
            GroundObject[] placed = map == null ? new GroundObject[0] : map.GroundObjects.Values.Where(entry => entry != null && entry.Durability.HasValue).OrderBy(entry => entry.CellId).ToArray();
            foreach (GroundObject entry in placed)
            {
                var row = objects.Items.Add(InventoryObjects.DisplayName(entry.ItemTemplateId));
                row.SubItems.Add(entry.CellId.ToString(CultureInfo.InvariantCulture));
                row.SubItems.Add(entry.Durability.Value.ToString(CultureInfo.InvariantCulture) + " / " + (entry.DurabilityMax ?? 0).ToString(CultureInfo.InvariantCulture));
                row.Tag = entry.CellId;
                if (entry.CellId == selected) row.Selected = true;
            }
            objects.EndUpdate();
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            if (removeObject == null || Game == null) return;
            MountActions mount = Game.Interactions?.Mount;
            removeObject.Enabled = Connected && mount != null && mount.IsParkMine && objects.SelectedItems.Count > 0;
        }

        private long SelectedId() => mounts != null && mounts.SelectedItems.Count > 0 && mounts.SelectedItems[0].Tag is long id ? id : 0;

        protected internal override bool OnUserClose()
        {
            if (ShedOpen) { _ = ReportAsync(() => Game.Interactions.Mount.Shed.LeaveAsync()); return false; }
            if (SaleOpen) { _ = ReportAsync(() => Game.Interactions.Mount.CloseSaleAsync()); return false; }
            return true;
        }

        private Task Move(MountLocation to)
        {
            long id = SelectedId();
            MountLocation from = TabLocations[CurrentShedTab];
            if (id == 0) return Task.CompletedTask;
            return ReportAsync(() => Game.Interactions.Mount.Shed.MoveAsync(from, to, id));
        }

        private Task ViewCertificate()
        {
            long id = SelectedId();
            InventoryObjects item = id <= 0 || id > uint.MaxValue ? null : Game.character?.Inventory?.GetByInventoryId((uint)id);
            return ReportAsync(() => Game.Interactions.Mount.ViewCertificateAsync(item));
        }

        private Task RemoveSelected()
        {
            if (objects.SelectedItems.Count == 0) return Task.CompletedTask;
            int cell = (int)objects.SelectedItems[0].Tag;
            return ReportAsync(() => Game.Interactions.Mount.RemoveParkObjectAsync(cell));
        }

        /// <summary><c>NOT_ENOUGH_RICH</c> sans boîte, sinon <c>DO_U_BUY_MOUNTPARK</c> (Oui / Non) avant <c>Rb&lt;prix&gt;</c>, comme <c>MountParkSale</c>.</summary>
        private async Task AskBuy()
        {
            try
            {
                MountActions mount = Game?.Interactions?.Mount;
                MountParkInfo park = mount?.Park;
                if (mount == null || park == null || !mount.IsOpen || park.Price <= 0) return;
                if ((Game.character?.Kamas ?? 0) < park.Price) { Feedback(MountTexts.Get("NOT_ENOUGH_RICH", "Tu n'as pas assez de kamas pour réaliser cette action.")); return; }
                CloseBuyDialog();
                Form[] before = BotDialogs.OpenDialogs.ToArray();
                string price = park.Price.ToString("N0", CultureInfo.CurrentCulture);
                Task<BotDialogResult> question = BotDialogs.AskYesNoAsync(Host, MountTexts.Get("MOUNTPARK_PURCHASE", "Achat d'un enclos"),
                    MountTexts.Get("DO_U_BUY_MOUNTPARK", "Confirmez-vous l'achat de cet enclos au prix de " + price + " kamas ?", price));
                buyDialog = BotDialogs.OpenDialogs.Except(before).LastOrDefault();
                RefreshView();
                BotDialogResult answer = await question;
                buyDialog = null;
                if (!IsDisposed) RefreshView();
                if (answer != BotDialogResult.Yes || !mount.IsOpen) return;
                if (IsDisposed) await mount.BuyParkAsync(); else await ReportAsync(mount.BuyParkAsync);
            }
            catch (Exception error) { Account?.Logger?.LogException(Reference, error); }
        }

        private void CloseBuyDialog()
        {
            Form dialog = buyDialog;
            buyDialog = null;
            if (dialog != null && !dialog.IsDisposed) dialog.Close();
        }
    }
}
