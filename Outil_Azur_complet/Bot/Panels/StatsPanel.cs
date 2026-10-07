using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Game.Perso.Stats;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Fiche des caractéristiques (13 lignes) et répartition des points : Augmenter envoie <c>AB&lt;caractéristique&gt;</c>
    /// (vitalité 11, sagesse 12, force 10, intelligence 15, chance 13, agilité 14). Volet provisoire repris de l'ancien tiroir ;
    /// la fiche complète du client appartient au lot des fiches.
    /// </summary>
    public sealed class StatsPanel : GamePanel
    {
        private static readonly int[] BoostPackets = { 11, 12, 10, 15, 13, 14 };
        private static readonly StatsEnum[] BoostStats = { StatsEnum.VITALITE, StatsEnum.SAGESSE, StatsEnum.FORCE, StatsEnum.INTELLIGENCE, StatsEnum.CHANCE, StatsEnum.AGILITE };
        private ListView stats;
        private ComboBox boostStat;
        private Label boostHelp;
        private Control boost;
        private bool boosting;
        private CharacterClass character;
        private Controls.TitleBadge titleBadge; // titre porté (lot F10)

        public override string Title => "Caractéristiques";
        public override Image Icon => ClientAssets.Icon("icone-caracteristiques", 24);

        protected override Control CreateView()
        {
            var page = Page();
            stats = MakeList(9, "Caractéristique", "Valeur");
            stats.Columns[0].Width = 236; stats.Columns[1].Width = 94;
            page.Controls.Add(stats);
            boostStat = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList,
                Font = BotFonts.Get(10), BackColor = BotUi.PaperLight, ForeColor = BotUi.Ink };
            boostStat.Items.AddRange(new object[] { "Vitalité", "Sagesse", "Force", "Intelligence", "Chance", "Agilité" });
            boostStat.SelectedIndex = 0; boostStat.SelectedIndexChanged += (s, e) => RefreshView();
            var allocation = new Panel { Dock = DockStyle.Bottom, Height = 142, Padding = new Padding(0, 5, 0, 0), BackColor = BotUi.Paper };
            var selectLabel = MakeLabel("Répartir les points de caractéristiques", 9, true); selectLabel.Dock = DockStyle.Top; selectLabel.Height = 23;
            boostHelp = MakeStatus(""); boostHelp.Height = 40;
            boost = MakeButton("Augmenter", async (s, e) => await Boost(), true, 125);
            allocation.Controls.Add(boostStat); allocation.Controls.Add(selectLabel);
            allocation.Controls.Add(boostHelp); allocation.Controls.Add(BotUi.Actions(boost));
            page.Controls.Add(allocation);
            page.Controls.Add(titleBadge = new Controls.TitleBadge { Dock = DockStyle.Top });
            return page;
        }

        protected override void OnBind(GameClass game)
        {
            character = game.character;
            titleBadge?.Bind(game);
            if (character != null) { character.RefreshCaracteristiques += OnCharacterChanged; character.SeeLifeRegen += OnCharacterChanged; }
        }
        protected override void OnUnbind(GameClass game)
        {
            if (character != null) { character.RefreshCaracteristiques -= OnCharacterChanged; character.SeeLifeRegen -= OnCharacterChanged; }
            character = null;
            titleBadge?.Bind(null);
        }
        private void OnCharacterChanged() => OnUi(RefreshView);

        public override void RefreshView()
        {
            if (Game == null || stats == null) return;
            var c = Game.character; var s = c.stats;
            stats.BeginUpdate(); stats.Items.Clear();
            Row("Vie", s.VitalityActual + " / " + s.MaxVitality); Row("Énergie", s.ActualEnergy + " / " + s.EnergyMax);
            Row("Expérience", s.ActualEXP + " / " + s.ExpNivNext); Row("Points de caractéristiques", c.Carac_Points);
            Row("Points d’action", s.PA.StatsTotal); Row("Points de mouvement", s.PM.StatsTotal); Row("Vitalité", s.Vita.StatsTotal);
            Row("Sagesse", s.Sagesse.StatsTotal); Row("Force", s.Force.StatsTotal); Row("Intelligence", s.Intell.StatsTotal);
            Row("Chance", s.Chance.StatsTotal); Row("Agilité", s.Agility.StatsTotal); Row("Pods", c.Inventory.Actual_pods + " / " + c.Inventory.Pods_Max);
            stats.EndUpdate();
            int cost = BoostCost();
            boostHelp.Text = c.Carac_Points + " point(s) disponible(s). Coût estimé : " + cost + " point(s).";
            boost.Enabled = !boosting && !Game.Fight.IsInFight && Connected && c.id > 0 && cost > 0 && c.Carac_Points >= cost;
        }

        private void Row(string title, object value) { var row = stats.Items.Add(title); row.SubItems.Add(Convert.ToString(value)); }

        private int BoostCost() => Game.character.stats.GetCapitalStatsBoost(Game.character.Race_ID, BoostStats[Math.Max(0, boostStat.SelectedIndex)]);

        private async Task Boost()
        {
            if (!boost.Enabled || Account?.Connexion == null) return;
            boosting = true; boost.Enabled = false;
            try { await Account.Connexion.SendPacket("AB" + BoostPackets[Math.Max(0, boostStat.SelectedIndex)], true); await Task.Delay(500); }
            catch (Exception error) { Account?.Logger?.LogError("INTERFACE", error.Message); }
            finally { boosting = false; if (!IsDisposed) RefreshView(); }
        }
    }
}
