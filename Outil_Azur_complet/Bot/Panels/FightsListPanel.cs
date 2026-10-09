using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Interactions;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Combats de la carte, comme l'interface <c>FightsInfos</c> du client 1.34 (322 × 345) : l'ouverture envoie <c>fL</c>,
    /// la sélection d'un combat envoie <c>fD&lt;id&gt;</c> et affiche les deux équipes (carré rouge / bleu, « Niveau » = somme
    /// des niveaux), le bouton spectateur envoie <c>GA903&lt;id&gt;</c> puis referme le volet. Le volet, toujours enregistré
    /// dans le tiroir, porte aussi les boîtes de duel de la session (<see cref="MapActionPrompts"/>).
    /// </summary>
    public sealed class FightsListPanel : GamePanel
    {
        /// <summary><c>TEAMS_COLOR</c> du client : rouge pour l'équipe 1, bleu pour l'équipe 2.</summary>
        private static readonly Color[] TeamColors = { Color.FromArgb(255, 0, 0), Color.FromArgb(0, 0, 255) };
        private ListView fightList;
        private readonly ListView[] teamLists = new ListView[2];
        private readonly Label[] teamTitles = new Label[2];
        private Label status;
        private Control refresh, spectate, close;
        private MapActions actions;
        private MapActionPrompts prompts;
        private PanelHost subscribedHost;
        private long? selectedFight;
        private bool filling;
        /// <summary>Avance la colonne de durée chaque seconde sans reconstruire la liste (sélection et défilement gardés).</summary>
        private Timer clock;

        public override string Title => MapActionTexts.CurrentFights;
        public override Image Icon => ClientAssets.Icon("icone-pvp", 24);

        protected override Control CreateView()
        {
            var page = Page();
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent, Margin = new Padding(0) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            fightList = MakeList(9, "Combat", MapActionTexts.FightersCount, MapActionTexts.Duration);
            fightList.Columns[0].Width = 96; fightList.Columns[1].Width = 130; fightList.Columns[2].Width = 84;
            fightList.Margin = new Padding(0);
            fightList.SelectedIndexChanged += (s, e) => { if (!filling) SelectionChanged(); };
            layout.Controls.Add(fightList, 0, 0);
            var teams = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = new Padding(0, 6, 0, 0) };
            teams.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            teams.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            teams.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int index = 0; index < 2; index++) teams.Controls.Add(TeamBox(index), index, 0);
            layout.Controls.Add(teams, 0, 1);
            status = MakeStatus(MapActionTexts.SelectFight);
            status.Dock = DockStyle.Fill;
            layout.Controls.Add(status, 0, 2);
            refresh = MakeButton("Actualiser", async (s, e) => await Run(() => actions?.RequestFightListAsync()), false, 96);
            spectate = MakeButton(MapActionTexts.Spectator, async (s, e) => await SpectateSelected(), true, 120);
            close = MakeButton(MapActionTexts.Close, (s, e) => Host?.RequestClose(this), false, 84);
            page.Controls.Add(layout);
            page.Controls.Add(BotUi.Actions(refresh, spectate, close));
            clock = new Timer { Interval = 1000 };
            clock.Tick += (s, e) => UpdateDurations();
            clock.Start();
            return page;
        }

        /// <summary>Colonne d'une équipe : carré de couleur, « Équipe n · Niveau N », combattants et niveaux.</summary>
        private Control TeamBox(int index)
        {
            var box = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(index == 0 ? 0 : 4, 0, index == 0 ? 4 : 0, 0) };
            var header = new Panel { Dock = DockStyle.Top, Height = 22, BackColor = Color.Transparent };
            var square = new Panel { Width = 10, Height = 10, Location = new Point(1, 6), BackColor = TeamColors[index] };
            square.Paint += (s, e) => { using (var pen = new Pen(BotUi.Frame)) e.Graphics.DrawRectangle(pen, 0, 0, square.Width - 1, square.Height - 1); };
            teamTitles[index] = MakeLabel(MapActionTexts.Team + " " + (index + 1), 9, true);
            teamTitles[index].AutoSize = false; teamTitles[index].Location = new Point(16, 2); teamTitles[index].Height = 18;
            teamTitles[index].Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top; teamTitles[index].AutoEllipsis = true;
            header.Controls.Add(square); header.Controls.Add(teamTitles[index]);
            header.Resize += (s, e) => teamTitles[index].Width = Math.Max(10, header.Width - 18);
            var rule = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = BotUi.Gold };
            teamLists[index] = MakeList(8.5f, "Combattant", MapActionTexts.Level);
            teamLists[index].Columns[0].Width = 108; teamLists[index].Columns[1].Width = 46;
            box.Controls.Add(teamLists[index]); box.Controls.Add(rule); box.Controls.Add(header);
            return box;
        }

        protected override void OnBind(GameClass game)
        {
            actions = game.Interactions?.MapActions;
            if (actions != null)
            {
                actions.Changed += OnFightsChanged;
                if (Host != null) prompts = new MapActionPrompts(actions, Host);
            }
            subscribedHost = Host;
            if (subscribedHost != null) subscribedHost.PanelShown += OnPanelShown;
        }

        protected override void OnUnbind(GameClass game)
        {
            if (actions != null) actions.Changed -= OnFightsChanged;
            prompts?.Dispose(); prompts = null;
            if (subscribedHost != null) subscribedHost.PanelShown -= OnPanelShown;
            subscribedHost = null; actions = null; selectedFight = null;
        }

        /// <summary>Comme l'ouverture de <c>FightsInfos</c> : demande la liste <c>fL</c> à chaque affichage du volet.</summary>
        private void OnPanelShown(object sender, PanelEventArgs e)
        {
            if (!ReferenceEquals(e.Panel, this) || actions == null) return;
            selectedFight = null;
            _ = Run(() => actions.RequestFightListAsync(), quiet: true);
        }

        private void OnFightsChanged() => OnUi(RefreshView);

        public override void RefreshView()
        {
            if (fightList == null) return;
            MapActions current = actions;
            var fights = current?.Fights ?? new MapFightInfo[0];
            DateTime? now = Game?.Session?.EstimatedServerTime;
            filling = true;
            try
            {
                fightList.BeginUpdate(); fightList.Items.Clear();
                foreach (MapFightInfo fight in fights)
                {
                    var row = fightList.Items.Add("#" + fight.FightId.ToString(CultureInfo.InvariantCulture));
                    row.SubItems.Add(fight.Team1.Count + " contre " + fight.Team2.Count);
                    row.SubItems.Add(FormatDuration(fight, now));
                    row.Tag = fight.FightId;
                    if (fight.FightId == selectedFight) row.Selected = true;
                }
                fightList.EndUpdate();
            }
            finally { filling = false; }
            RefreshDetails();
        }

        /// <summary>Colonne « Durée » des lignes affichées, recalculée en place.</summary>
        private void UpdateDurations()
        {
            MapActions current = actions;
            if (current == null || fightList == null || fightList.IsDisposed || !fightList.Visible) return;
            DateTime? now = Game?.Session?.EstimatedServerTime;
            foreach (ListViewItem row in fightList.Items)
            {
                MapFightInfo fight = row.Tag is long id ? current.GetFight(id) : null;
                if (fight == null || row.SubItems.Count < 3) continue;
                string text = FormatDuration(fight, now);
                if (row.SubItems[2].Text != text) row.SubItems[2].Text = text;
            }
        }

        /// <summary>Équipes du combat sélectionné, texte d'état et boutons (sans reconstruire la liste).</summary>
        private void RefreshDetails()
        {
            if (fightList == null) return;
            MapActions current = actions;
            var fights = current?.Fights ?? new MapFightInfo[0];
            MapFightInfo selected = selectedFight.HasValue ? current?.GetFight(selectedFight.Value) : null;
            if (selected == null) selectedFight = null;
            for (int index = 0; index < 2; index++)
            {
                MapFightTeam team = selected == null ? null : index == 0 ? selected.Team1 : selected.Team2;
                teamTitles[index].Text = MapActionTexts.Team + " " + (index + 1)
                    + (team?.Members != null ? " · " + MapActionTexts.Level + " " + team.TotalLevel : string.Empty);
                ListView list = teamLists[index];
                list.BeginUpdate(); list.Items.Clear();
                if (team?.Members != null)
                    foreach (MapFightMember member in team.Members)
                    {
                        var row = list.Items.Add(member.Name);
                        row.SubItems.Add(member.Level.ToString(CultureInfo.InvariantCulture));
                        if (member.Kind != MapFightMemberKind.Player) row.ForeColor = BotUi.Muted;
                    }
                list.EndUpdate();
            }
            int count = current?.FightCount ?? 0;
            status.Text = fights.Count == 0
                ? (count > 0 ? count + " combat(s) annoncé(s) sur la carte ; actualisez la liste." : "Aucun combat en cours sur cette carte.")
                : selected == null ? MapActionTexts.SelectFight
                : selected.HasDetails ? "Combat #" + selected.FightId + " : " + selected.Team1.Count + " contre " + selected.Team2.Count + "."
                : "Détail du combat #" + selected.FightId + " demandé au serveur.";
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            if (spectate == null) return;
            bool inFight = Account?.IsFighting() == true;
            refresh.Enabled = Connected && actions != null;
            spectate.Enabled = Connected && actions != null && selectedFight.HasValue && !inFight;
        }

        /// <summary>Comme <c>FightsInfos</c> : la sélection envoie <c>fD&lt;id&gt;</c> et affiche les équipes à la réponse.</summary>
        private void SelectionChanged()
        {
            selectedFight = fightList.SelectedItems.Count == 1 ? (long?)(long)fightList.SelectedItems[0].Tag : null;
            RefreshDetails();
            if (selectedFight.HasValue && actions != null)
            {
                long id = selectedFight.Value;
                _ = Run(() => actions.RequestFightDetailsAsync(id), quiet: true);
            }
        }

        private async Task SpectateSelected()
        {
            if (!selectedFight.HasValue || actions == null) return;
            long id = selectedFight.Value;
            InteractionResult result = await actions.SpectateAsync(id);
            Feedback(result?.Message);
            if (result != null && result.Sent && Host != null && !Host.IsDisposed) Host.RequestClose(this);
        }

        private async Task Run(Func<Task<InteractionResult>> action, bool quiet = false)
        {
            try
            {
                Task<InteractionResult> pending = action();
                if (pending == null) return;
                InteractionResult result = await pending;
                if (result == null) return;
                if (!result.Sent) { Feedback(result.Message); Account?.Logger?.LogError("COMBATS", result.Message); }
                else if (!quiet) Feedback(result.Message);
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { Feedback(error.Message); }
        }

        /// <summary>Durée du combat (<c>getDiffDate</c>) ; « placement » avant le début, tiret sans heure du serveur.</summary>
        internal static string FormatDuration(MapFightInfo fight, DateTime? serverNow)
        {
            if (fight == null) return string.Empty;
            if (!fight.StartTime.HasValue) return "placement";
            TimeSpan? elapsed = fight.Elapsed(serverNow);
            if (!elapsed.HasValue) return "—";
            TimeSpan value = elapsed.Value;
            return value.TotalHours >= 1
                ? ((int)value.TotalHours).ToString(CultureInfo.InvariantCulture) + ":" + value.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" + value.Seconds.ToString("00", CultureInfo.InvariantCulture)
                : value.Minutes.ToString(CultureInfo.InvariantCulture) + ":" + value.Seconds.ToString("00", CultureInfo.InvariantCulture);
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing) return;
            clock?.Stop(); clock?.Dispose(); clock = null;
            prompts?.Dispose(); prompts = null;
            if (subscribedHost != null) subscribedHost.PanelShown -= OnPanelShown;
            subscribedHost = null;
        }
    }
}
