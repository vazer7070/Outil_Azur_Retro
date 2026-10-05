using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Habitat;
using Tool_BotProtocol.Game.Interactions;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Maison du personnage : menu intérieur (<c>HouseIndoor</c> du client 1.34, visible à l'intérieur d'une maison que le
    /// compte possède : une entrée par compétence de <c>H.ids</c> dont l'état vient de son critère, clic → <c>GA507&lt;compétence&gt;</c>),
    /// paramètres de la maison de guilde (<c>GUILD_HOUSE_CONFIGURATION</c> : <c>hG</c>, <c>hG+</c>, <c>hG-</c>, <c>hG&lt;droits&gt;</c>) et fenêtre
    /// de vente (<c>HouseSale</c>, ouverte sur <c>hCK&lt;id&gt;|&lt;prix&gt;</c>) : le propriétaire fixe le prix (<c>hS&lt;prix&gt;</c>) ou annule la vente
    /// (<c>hS0</c>, <c>CANCEL_THE_SALE</c>), un visiteur achète (<c>DO_U_BUY_HOUSE</c> puis <c>hB&lt;prix&gt;</c>), Fermer (ou ×/Échap) envoie <c>hV</c>.
    /// </summary>
    public sealed class HouseIndoorPanel : GamePanel
    {
        private const string Reference = "MAISON";
        private const string NoHouseText = "Entrez dans une maison (porte : « Entrer ») pour son menu intérieur. La fenêtre de vente s'ouvre quand le serveur l'annonce (hCK) après « Acheter », « Vendre » ou « Modifier le prix de vente » sur la porte ou ici.";
        private readonly Dictionary<short, Control> indoorButtons = new Dictionary<short, Control>();
        private Label houseTitle, houseOwner, houseDescription, houseStatus, saleTitle, saleInfo, guildTitle, guildInfo;
        private FlowLayoutPanel indoorActions;
        private Panel saleArea, guildArea;
        private NumericUpDown salePrice;
        private Control saleBuy, saleSell, saleCancel, saleLeave, guildState, guildShare, guildUnshare, guildApply;
        private CheckedListBox guildRights;
        private HouseActions bound;
        private bool wasOpen;
        private Form buyDialog;

        public override string Title => HouseTexts.Text("HOUSE_WORD", "Maison");
        public override Image Icon => ClientAssets.Icon("UI_HouseIndoor", 24) ?? ClientAssets.Icon("icone-carte", 24);
        public override bool IsModal => true;
        public override bool IsServerWindowOpen => Game?.Interactions?.House?.IsOpen == true;
        /// <summary>Boîte <c>DO_U_BUY_HOUSE</c> ouverte, ou <c>null</c> (diagnostic et tests).</summary>
        public Form BuyDialog => buyDialog;

        protected override Control CreateView()
        {
            var page = Page();
            var sheet = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true, BackColor = BotUi.Paper, Margin = new Padding(0) };
            sheet.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            houseTitle = Row(sheet, MakeLabel(HouseTexts.Text("HOUSE_WORD", "Maison"), 11, true), 26);
            houseOwner = Row(sheet, MakeLabel(string.Empty, 9), 20);
            houseDescription = Row(sheet, MakeLabel(string.Empty, 9), 58); houseDescription.ForeColor = BotUi.Muted;
            indoorActions = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 76, WrapContents = true, BackColor = BotUi.Paper, Margin = new Padding(0, 4, 0, 4), AccessibleName = "Menu intérieur" };
            foreach (short skill in HouseTexts.IndoorSkills())
            {
                short chosen = skill;
                var button = MakeButton(SkillName(skill), async (s, e) => await ReportAsync(() => Game.Interactions.House.UseIndoorSkillAsync(chosen)), false, 150);
                button.Name = "house-skill-" + skill; button.Margin = new Padding(0, 0, 6, 6); button.Visible = false;
                indoorButtons[skill] = button;
                indoorActions.Controls.Add(button);
            }
            AddRow(sheet, indoorActions, 76);

            saleArea = new Panel { Dock = DockStyle.Fill, Height = 96, BackColor = BotUi.Paper, Margin = new Padding(0), Visible = false, Name = "house-sale" };
            saleTitle = MakeLabel(HouseTexts.Text("HOUSE_SALE", "Mise en vente de la maison"), 9, true); saleTitle.Dock = DockStyle.Top; saleTitle.Height = 22;
            saleInfo = MakeLabel(string.Empty, 9); saleInfo.Dock = DockStyle.Top; saleInfo.Height = 20;
            salePrice = new NumericUpDown { Minimum = 0, Maximum = int.MaxValue, Value = 0, Width = 120, Height = 34, ThousandsSeparator = true, Font = BotFonts.Get(9),
                BackColor = BotUi.PaperLight, ForeColor = BotUi.Ink, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(2, 7, 6, 0), AccessibleName = "Prix de vente" };
            Label priceLabel = BotUi.Label(HouseTexts.Text("PRICE", "Prix").Trim(), 9); priceLabel.AutoSize = true; priceLabel.Margin = new Padding(0, 12, 0, 0); priceLabel.ForeColor = BotUi.Muted;
            saleSell = MakeButton(HouseTexts.Text("SELL", "Vendre"), async (s, e) => await Sell(), true, 90);
            saleCancel = MakeButton(HouseTexts.Text("CANCEL_THE_SALE", "Annuler la vente"), async (s, e) => await ReportAsync(() => Game.Interactions.House.CancelSaleAsync()), false, 130);
            saleBuy = MakeButton(HouseTexts.Text("BUY", "Acheter"), async (s, e) => await AskBuy(), true, 100);
            saleLeave = MakeButton(HouseTexts.Text("CLOSE", "Fermer"), async (s, e) => await Leave(), false, 90);
            saleArea.Controls.Add(BotUi.Actions(priceLabel, salePrice, saleSell, saleCancel, saleBuy, saleLeave)); saleArea.Controls.Add(saleInfo); saleArea.Controls.Add(saleTitle);
            AddRow(sheet, saleArea, 96);

            guildArea = new Panel { Dock = DockStyle.Fill, Height = 236, BackColor = BotUi.Paper, Margin = new Padding(0), Visible = false, Name = "house-guild" };
            guildTitle = MakeLabel(HouseTexts.Text("GUILD_HOUSE_CONFIGURATION", "Paramètres de cette maison de guilde"), 9, true); guildTitle.Dock = DockStyle.Top; guildTitle.Height = 22;
            guildInfo = MakeLabel(string.Empty, 9); guildInfo.Dock = DockStyle.Top; guildInfo.Height = 20; guildInfo.ForeColor = BotUi.Muted;
            guildRights = new CheckedListBox { Dock = DockStyle.Fill, BackColor = BotUi.PaperLight, ForeColor = BotUi.Ink, BorderStyle = BorderStyle.FixedSingle, Font = BotFonts.Get(9),
                CheckOnClick = true, IntegralHeight = false, AccessibleName = "Droits de la maison de guilde" };
            foreach (HouseGuildRights right in Rights()) guildRights.Items.Add(new RightItem(right), false);
            guildState = MakeButton("État", async (s, e) => await ReportAsync(() => Game.Interactions.House.RequestGuildStateAsync()), false, 70);
            guildShare = MakeButton("Partager", async (s, e) => await ReportAsync(() => Game.Interactions.House.ShareWithGuildAsync()), false, 90);
            guildUnshare = MakeButton("Retirer", async (s, e) => await ReportAsync(() => Game.Interactions.House.UnshareAsync()), false, 80);
            guildApply = MakeButton("Appliquer", async (s, e) => await ApplyRights(), true, 100);
            guildArea.Controls.Add(guildRights); guildArea.Controls.Add(guildInfo); guildArea.Controls.Add(guildTitle);
            guildArea.Controls.Add(BotUi.Actions(guildState, guildShare, guildUnshare, guildApply));
            AddRow(sheet, guildArea, 236);

            houseStatus = MakeStatus(NoHouseText);
            page.Controls.Add(sheet); page.Controls.Add(houseStatus);
            return page;
        }

        private static Label Row(TableLayoutPanel sheet, Label label, int height) { label.Dock = DockStyle.Fill; AddRow(sheet, label, height); return label; }
        private static void AddRow(TableLayoutPanel sheet, Control control, int height)
        {
            sheet.RowCount = sheet.RowStyles.Count + 1;
            sheet.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            control.Height = height;
            sheet.Controls.Add(control, 0, sheet.RowStyles.Count - 1);
        }

        private static IEnumerable<HouseGuildRights> Rights() =>
            Enum.GetValues(typeof(HouseGuildRights)).Cast<HouseGuildRights>().Where(right => right != HouseGuildRights.None).OrderBy(right => (int)right);

        private static string SkillName(short skill)
        {
            try { return LangData.Skill.Name(skill); }
            catch (Exception) { return "Compétence " + skill.ToString(CultureInfo.InvariantCulture); }
        }

        protected override void OnBind(GameClass game)
        {
            bound = game.Interactions?.House;
            if (bound != null) bound.Changed += OnServerChanged;
            wasOpen = false;
        }

        protected override void OnUnbind(GameClass game)
        {
            if (bound != null) bound.Changed -= OnServerChanged;
            bound = null;
            wasOpen = false;
            CloseBuyDialog();
        }

        private void OnServerChanged()
        {
            if (!ExchangePanel.PostToForm(Host, () => { if (!IsDisposed) ApplyServerChange(); })) OnUi(ApplyServerChange);
        }

        private void ApplyServerChange()
        {
            bool open = IsServerWindowOpen;
            if (open && !wasOpen) { if (salePrice != null) salePrice.Value = Math.Max(0, Math.Min(int.MaxValue, bound?.SalePrice ?? 0)); RequestShow(); }
            else if (!open && wasOpen) { CloseBuyDialog(); RaiseClosed(); }
            wasOpen = open;
            RefreshView();
        }

        public override void RefreshView()
        {
            if (Game == null || houseTitle == null) return;
            HouseActions houses = Game.Interactions.House;
            HouseInfo current = houses.CurrentHouse;
            HouseInfo sale = houses.SaleHouse;
            HouseInfo shown = sale ?? current;
            bool connected = Connected;
            houseTitle.Text = shown == null ? HouseTexts.Text("HOUSE_WORD", "Maison") : shown.Name;
            houseOwner.Text = shown == null ? "Aucune maison sur cette carte." : shown.OwnerLine() + (shown.IsGuildHouse ? " · " + shown.GuildName : string.Empty)
                + (shown.IsForSale && shown.Price > 0 ? " · en vente à " + shown.Price.ToString("N0", CultureInfo.CurrentCulture) + " kamas" : string.Empty);
            houseDescription.Text = shown?.Description ?? string.Empty;
            // Menu intérieur : seulement chez soi (isAtHome), états des compétences comme HouseIndoor.
            bool atHome = current != null && current.LocalOwner;
            foreach (var pair in indoorButtons)
            {
                string state = current == null ? SkillState.Hidden : SkillState.Evaluate(SkillState.Criterion(pair.Key), true, current.LocalOwner, current.IsForSale, current.IsLocked);
                pair.Value.Visible = atHome && state != SkillState.Hidden;
                pair.Value.Enabled = connected && state == SkillState.Enabled && (!houses.IsOpen || pair.Key == 81);
            }
            indoorActions.Visible = atHome;
            // Fenêtre de vente.
            saleArea.Visible = houses.IsOpen;
            if (houses.IsOpen && sale != null)
            {
                bool owner = sale.LocalOwner;
                saleInfo.Text = owner ? (sale.IsForSale ? "En vente à " + sale.Price.ToString("N0", CultureInfo.CurrentCulture) + " kamas." : "Pas encore en vente.")
                    : HouseTexts.Text("PRICE", "Prix").Trim() + " : " + houses.SalePrice.ToString("N0", CultureInfo.CurrentCulture) + " kamas.";
                salePrice.Visible = saleSell.Visible = owner; saleCancel.Visible = owner && sale.IsForSale; saleBuy.Visible = !owner;
                saleSell.Enabled = owner && connected && !houses.IsPending;
                saleCancel.Enabled = owner && connected && !houses.IsPending;
                saleBuy.Enabled = !owner && connected && !houses.IsPending && houses.SalePrice > 0;
                saleLeave.Enabled = connected;
            }
            // Maison de guilde : chez soi, avec une guilde.
            string guild = (Game.Map?.Self as Tool_BotProtocol.Game.Maps.Entities.PlayerActor)?.GuildName;
            guildArea.Visible = atHome && !string.IsNullOrEmpty(guild);
            if (guildArea.Visible)
            {
                guildInfo.Text = !current.GuildRightsKnown ? "Droits inconnus : demandez l'état (hG)." : current.IsGuildHouse ? "Partagée avec " + current.GuildName + "." : "Maison privée (non partagée).";
                for (int index = 0; index < guildRights.Items.Count; index++)
                    guildRights.SetItemChecked(index, current.GuildRightsKnown && (current.GuildRights & (int)((RightItem)guildRights.Items[index]).Right) != 0);
                guildState.Enabled = connected;
                guildShare.Enabled = connected && current.GuildRightsKnown && !current.IsGuildHouse;
                guildUnshare.Enabled = guildApply.Enabled = connected && current.GuildRightsKnown && current.IsGuildHouse;
            }
            houseStatus.Text = (shown == null ? NoHouseText : string.Empty) + (houses.LastMessage.Length > 0 ? (shown == null ? "\n" : string.Empty) + houses.LastMessage : string.Empty);
            if (houseStatus.Text.Length == 0) houseStatus.Text = atHome ? "Chez vous : le menu intérieur envoie GA507<compétence>." : "Maison d'un autre joueur.";
        }

        protected internal override bool OnUserClose()
        {
            if (!IsServerWindowOpen) return true;
            _ = Leave();
            return false;
        }

        private Task Sell() => ReportAsync(() => Game.Interactions.House.SellAsync((int)Math.Min(int.MaxValue, salePrice.Value)));
        private Task Leave() => ReportAsync(() => Game.Interactions.House.LeaveAsync());

        private Task ApplyRights()
        {
            HouseGuildRights rights = HouseGuildRights.None;
            foreach (RightItem item in guildRights.CheckedItems) rights |= item.Right;
            return ReportAsync(() => Game.Interactions.House.SetGuildRightsAsync(rights));
        }

        /// <summary><c>NOT_ENOUGH_RICH</c> sans boîte, sinon <c>DO_U_BUY_HOUSE</c> (Oui / Non) avant <c>hB&lt;prix&gt;</c>.</summary>
        private async Task AskBuy()
        {
            try
            {
                HouseActions houses = Game?.Interactions?.House;
                HouseInfo house = houses?.SaleHouse;
                if (houses == null || house == null || !houses.IsOpen) return;
                int kamas = Game.character?.Kamas ?? 0;
                if (kamas < houses.SalePrice) { Feedback(HouseTexts.Text("NOT_ENOUGH_RICH", "Tu n'as pas assez de kamas pour réaliser cette action.")); return; }
                CloseBuyDialog();
                Form[] before = BotDialogs.OpenDialogs.ToArray();
                string price = houses.SalePrice.ToString(CultureInfo.InvariantCulture);
                Task<BotDialogResult> question = BotDialogs.AskYesNoAsync(Host, HouseTexts.Text("BUY", "Acheter"),
                    HouseTexts.Text("DO_U_BUY_HOUSE", "Confirmez-vous l'achat de '" + house.Name + "' au prix de " + price + " kamas ?", house.Name, price));
                buyDialog = BotDialogs.OpenDialogs.Except(before).LastOrDefault();
                BotDialogResult answer = await question;
                buyDialog = null;
                if (answer != BotDialogResult.Yes || !houses.IsOpen) return;
                if (IsDisposed) await houses.BuyAsync(); else await ReportAsync(houses.BuyAsync);
            }
            catch (Exception error) { Account?.Logger?.LogException(Reference, error); }
        }

        private void CloseBuyDialog()
        {
            Form dialog = buyDialog;
            buyDialog = null;
            if (dialog != null && !dialog.IsDisposed) dialog.Close();
        }

        private sealed class RightItem
        {
            internal RightItem(HouseGuildRights right) { Right = right; }
            internal HouseGuildRights Right { get; }
            public override string ToString() => HouseTexts.RightName(Right);
        }
    }
}
