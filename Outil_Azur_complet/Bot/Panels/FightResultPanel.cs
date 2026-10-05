using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls.Banner;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Combats;
using Tool_BotProtocol.Game.Data;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Résultat de fin de combat (<c>GameResults</c> du client 1.34, paquet <c>GE</c>) : titre « Résultat du combat - Nombre de
    /// tours : n », durée, bonus d'étoiles (combats contre des monstres), tables des gagnants, des perdants et du percepteur
    /// avec les colonnes du client (nom, niveau, kamas, xp gagnée, monture, guilde, objets gagnés ; honneur, grade et
    /// déshonneur pour une agression), crâne (<c>_mcDeadHead</c>) devant les combattants morts, bouton « Fermer ».
    /// Le volet s'ouvre tout seul à la réception de <c>GE</c> (<see cref="Fights.CombatResultReceived"/>, fil réseau) et
    /// garde le dernier résultat tant qu'un nouveau combat ne l'a pas remplacé. Les noms de monstres et d'objets viennent
    /// des textes du client s'ils sont chargés, sinon de leur numéro.
    /// </summary>
    public sealed class FightResultPanel : GamePanel
    {
        private static readonly string[] PveColumns = { "NAME_BIG|Nom", "LEVEL_SMALL|Niv.", "KAMAS|Kamas", "WIN_XP|XP gagnée", "XP_MOUNT|Monture", "XP_GUILD|Guilde", "WIN_ITEMS|Objets gagnés" };
        /// <summary>Colonnes de <c>UI_GameResultTeamPVP</c> (agression, conquête) : l'honneur et le déshonneur remplacent la monture et la guilde.</summary>
        private static readonly string[] PvpColumns = { "NAME_BIG|Nom", "LEVEL_SMALL|Niv.", "KAMAS|Kamas", "HONOUR_POINTS|Points d'honneur", "RANK|Grade", "WIN_ITEMS|Objets gagnés", "DISGRACE_POINTS|Points de déshonneur", "WIN_XP|XP gagnée" };
        private static readonly int[] PveWidths = { 130, 44, 70, 80, 60, 60, 160 }, PvpWidths = { 130, 44, 70, 90, 50, 140, 90, 80 };
        private Label heading, duration, bonus, status;
        private Label winnersTitle, losersTitle, collectorsTitle;
        private ListView winners, losers, collectors;
        private PictureBox star;
        private ImageList icons;
        private Control close;
        private FightResult result;
        private int turns, lastTurns;

        public override string Title => BannerArt.Text("GAME_RESULTS", "Résultat du combat");
        public override Image Icon => ClientAssets.Icon("icone-pvp", 24);

        /// <summary>Dernier résultat affiché, ou <c>null</c>.</summary>
        public FightResult Result => result;
        public ListView Winners => winners;
        public ListView Losers => losers;
        public ListView Collectors => collectors;
        public string HeadingText => heading?.Text ?? string.Empty;
        public string DurationText => duration?.Text ?? string.Empty;
        public string BonusText => bonus != null && bonus.Visible ? bonus.Text : string.Empty;
        public Control CloseButton => close;

        protected override Control CreateView()
        {
            var page = Page();
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, BackColor = Color.Transparent, Margin = new Padding(0) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            heading = MakeLabel(Title, 11, true); heading.Dock = DockStyle.Fill; heading.TextAlign = ContentAlignment.MiddleLeft; heading.AutoEllipsis = true; heading.Name = "result-heading";
            layout.Controls.Add(heading, 0, 0);
            var line = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(0) };
            duration = MakeLabel(string.Empty, 9); duration.Dock = DockStyle.Left; duration.Width = 170; duration.TextAlign = ContentAlignment.MiddleLeft; duration.Name = "result-duration";
            star = new PictureBox { Dock = DockStyle.Left, Width = 22, SizeMode = PictureBoxSizeMode.CenterImage, BackColor = Color.Transparent, Visible = false, Margin = new Padding(0) };
            bonus = MakeLabel(string.Empty, 9); bonus.Dock = DockStyle.Fill; bonus.TextAlign = ContentAlignment.MiddleLeft; bonus.ForeColor = BotUi.Olive; bonus.Visible = false; bonus.Name = "result-bonus";
            line.Controls.Add(bonus); line.Controls.Add(star); line.Controls.Add(duration);
            layout.Controls.Add(line, 0, 1);
            icons = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
            winnersTitle = SectionTitle(BannerArt.Text("WINNERS", "Gagnants")); layout.Controls.Add(winnersTitle, 0, 2);
            winners = ResultList("result-winners"); layout.Controls.Add(winners, 0, 3);
            losersTitle = SectionTitle(BannerArt.Text("LOOSERS", "Perdants")); layout.Controls.Add(losersTitle, 0, 4);
            losers = ResultList("result-losers"); layout.Controls.Add(losers, 0, 5);
            collectorsTitle = SectionTitle(BannerArt.Text("GUILD_TAXCOLLECTORS", "Percepteurs")); collectorsTitle.Visible = false; layout.Controls.Add(collectorsTitle, 0, 6);
            collectors = ResultList("result-collectors"); collectors.Visible = false; layout.Controls.Add(collectors, 0, 7);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(0), Padding = new Padding(0, 6, 0, 0), WrapContents = false };
            close = MakeButton(BannerArt.Text("CLOSE", "Fermer"), (s, e) => RaiseClosed(), true, 110); close.Name = "result-close";
            status = MakeStatus("Le résultat du prochain combat s'affichera ici."); status.Dock = DockStyle.Fill; status.Height = 34; status.Margin = new Padding(8, 0, 0, 0);
            actions.Controls.Add(close); actions.Controls.Add(status);
            layout.Controls.Add(actions, 0, 8);
            page.Controls.Add(layout);
            BannerArt.RequestIcon(page, "Star", 18, image => { if (star != null && !star.IsDisposed) star.Image = image; });
            BannerArt.Request(page, "UI_GameResultPlayer_mort", image => AddSkull(image));
            return page;
        }

        private static Label SectionTitle(string text)
        {
            Label label = MakeLabel(text, 9, true);
            label.Dock = DockStyle.Fill; label.TextAlign = ContentAlignment.BottomLeft; label.Margin = new Padding(0, 4, 0, 0);
            return label;
        }

        private ListView ResultList(string name)
        {
            ListView list = MakeList(8.25f, PveColumns.Select(Column).ToArray());
            list.Name = name; list.Margin = new Padding(0); list.SmallImageList = icons; list.GridLines = false;
            SizeColumns(list, PveWidths);
            return list;
        }

        private static string Column(string entry)
        {
            string[] parts = entry.Split('|');
            return BannerArt.Text(parts[0], parts[1]);
        }

        private static void SizeColumns(ListView list, int[] widths)
        {
            for (int index = 0; index < list.Columns.Count; index++) list.Columns[index].Width = index < widths.Length ? widths[index] : 80;
        }

        /// <summary>Crâne de <c>UI_GameResultPlayer</c> (instance <c>_mcDeadHead</c>, coin haut-gauche de l'image) réduit à 16 pixels.</summary>
        private void AddSkull(Bitmap image)
        {
            if (image == null || icons == null || IsDisposed) return;
            try
            {
                var skull = new Bitmap(16, 16);
                using (var graphics = Graphics.FromImage(skull))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    lock (image) graphics.DrawImage(image, new Rectangle(0, 0, 16, 16), new Rectangle(0, 4, 26, 34), GraphicsUnit.Pixel);
                }
                icons.Images.Clear(); icons.Images.Add("dead", skull);
                winners?.Invalidate(); losers?.Invalidate(); collectors?.Invalidate();
            }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException || error is System.Runtime.InteropServices.ExternalException) { }
        }

        protected override void OnBind(GameClass game)
        {
            if (game?.Fight == null) return;
            game.Fight.CombatResultReceived += OnResult;
            game.Fight.CombatChanged += OnCombatChanged;
        }

        protected override void OnUnbind(GameClass game)
        {
            if (game?.Fight == null) return;
            game.Fight.CombatResultReceived -= OnResult;
            game.Fight.CombatChanged -= OnCombatChanged;
        }

        /// <summary>
        /// Fil réseau : le client affiche <c>currentTableTurn</c> (3ᵉ champ de <c>GTS</c>, jamais envoyé par StarLoco, matrice §2 n° 16) ;
        /// à défaut, le nombre de tours joués par le personnage. <c>GE</c> remet ces compteurs à zéro avant de livrer le résultat :
        /// la dernière valeur connue en combat est gardée ici.
        /// </summary>
        private void OnCombatChanged()
        {
            Fights fight = Game?.Fight;
            if (fight == null || !fight.IsInFight) return;
            if (fight.IsPlacement) { lastTurns = 0; return; }
            int value = fight.Timeline.TableTurn ?? fight.TurnNumber;
            if (value > 0) lastTurns = value;
        }

        /// <summary>Fil réseau : le résultat est copié puis affiché sur le fil de l'interface.</summary>
        private void OnResult(FightResult received)
        {
            if (received == null) return;
            int played = lastTurns; lastTurns = 0;
            OnUi(() => { Show(received, played); RequestShow(); });
        }

        /// <summary>Remplit le volet avec un résultat (thread de l'interface) ; <paramref name="playedTurns"/> est le nombre de tours joués par le personnage.</summary>
        public void Show(FightResult value, int playedTurns)
        {
            if (value == null || IsDisposed) return;
            result = value; turns = playedTurns;
            Control view = View;
            heading.Text = Title + (turns > 0 ? " - " + BannerArt.Text("TURNS_NUMBER", "Nombre de tours") + " : " + turns.ToString(CultureInfo.InvariantCulture) : string.Empty);
            duration.Text = BannerArt.Text("DURATION", "Durée") + " : " + DurationOf(value.DurationMilliseconds);
            bool bonusShown = value.StarBonus.HasValue && value.StarBonus.Value > 0;
            bonus.Visible = star.Visible = bonusShown;
            if (bonusShown) bonus.Text = BannerArt.Text("GAME_RESULTS_BONUS", "Bonus sur ce combat") + " : +" + value.StarBonus.Value.ToString(CultureInfo.InvariantCulture) + " %";
            string[] columns = (value.FightType == 1 ? PvpColumns : PveColumns).Select(Column).ToArray();
            Fill(winners, columns, value.Winners, value.FightType);
            Fill(losers, columns, value.Losers, value.FightType);
            bool withCollector = value.Collectors.Count > 0;
            collectorsTitle.Visible = collectors.Visible = withCollector;
            if (withCollector) Fill(collectors, columns, value.Collectors, value.FightType);
            int self = Game?.character?.id ?? 0;
            FightResultEntry own = self != 0 ? value.Find(self) : null;
            status.Text = own == null ? (value.Rejected.Count > 0 ? value.Rejected.Count + " ligne(s) illisible(s) ignorée(s)." : string.Empty)
                : own.Kind == FightResultKind.Winner ? "Victoire." : own.Kind == FightResultKind.Loser ? "Défaite." : string.Empty;
            view.Invalidate(true);
        }

        private void Fill(ListView list, string[] columns, IReadOnlyList<FightResultEntry> entries, int fightType)
        {
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                if (list.Columns.Count != columns.Length || list.Columns.Cast<ColumnHeader>().Select(column => column.Text).SequenceEqual(columns) == false)
                {
                    list.Columns.Clear();
                    foreach (string column in columns) list.Columns.Add(column, 80);
                    SizeColumns(list, fightType == 1 ? PvpWidths : PveWidths);
                }
                foreach (FightResultEntry entry in entries)
                {
                    var item = new ListViewItem(NameOf(entry)) { Tag = entry, ToolTipText = entry.Raw };
                    if (entry.IsDead && icons.Images.Count > 0) item.ImageKey = "dead";
                    else if (entry.IsDead) item.Text = "† " + item.Text;
                    item.SubItems.Add(entry.Level.ToString(CultureInfo.InvariantCulture));
                    if (fightType == 1)
                    {
                        // GameResultPlayerPVP : kamas, honneur gagné (total entre parenthèses), grade, objets, déshonneur gagné, xp gagnée.
                        item.SubItems.Add(entry.Kamas.HasValue ? BannerArt.Thousands(entry.Kamas.Value) : string.Empty);
                        item.SubItems.Add(Signed(entry.WonHonour) + (entry.Honour.HasValue ? " (" + BannerArt.Thousands(entry.Honour.Value) + ")" : string.Empty));
                        item.SubItems.Add(entry.Rank.HasValue ? entry.Rank.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
                        item.SubItems.Add(ItemsOf(entry));
                        item.SubItems.Add(Signed(entry.WonDisgrace) + (entry.Disgrace.HasValue ? " (" + BannerArt.Thousands(entry.Disgrace.Value) + ")" : string.Empty));
                        item.SubItems.Add(entry.WonExperience.HasValue ? BannerArt.Thousands(entry.WonExperience.Value) : string.Empty);
                    }
                    else
                    {
                        item.SubItems.Add(entry.Kamas.HasValue ? BannerArt.Thousands(entry.Kamas.Value) : string.Empty);
                        item.SubItems.Add(entry.WonExperience.HasValue ? BannerArt.Thousands(entry.WonExperience.Value) : string.Empty);
                        item.SubItems.Add(entry.MountExperience.HasValue && entry.MountExperience.Value != 0 ? BannerArt.Thousands(entry.MountExperience.Value) : string.Empty);
                        item.SubItems.Add(entry.GuildExperience.HasValue && entry.GuildExperience.Value != 0 ? BannerArt.Thousands(entry.GuildExperience.Value) : string.Empty);
                        item.SubItems.Add(ItemsOf(entry));
                    }
                    list.Items.Add(item);
                }
            }
            finally { list.EndUpdate(); }
        }

        private static string Signed(int? value) => !value.HasValue ? string.Empty : (value.Value > 0 ? "+" : string.Empty) + value.Value.ToString(CultureInfo.InvariantCulture);

        /// <summary>Nom affiché : joueur tel quel, monstre par son modèle (<c>monsters_fr</c>), percepteur « prénom nom ».</summary>
        public static string NameOf(FightResultEntry entry)
        {
            if (entry == null) return string.Empty;
            if (entry.MonsterTemplateId.HasValue)
                return LangData.IsLoaded("monsters") ? LangData.Monster.Name(entry.MonsterTemplateId.Value) : "Monstre #" + entry.MonsterTemplateId.Value.ToString(CultureInfo.InvariantCulture);
            if (entry.Kind == FightResultKind.Collector) return entry.NameData.Replace(',', ' ').Trim();
            return entry.NameData;
        }

        /// <summary>Objets gagnés « Nom ×n, … » (<c>items_fr</c>), ou « objet #id » sans les textes.</summary>
        public static string ItemsOf(FightResultEntry entry)
        {
            if (entry == null || entry.Items.Count == 0) return string.Empty;
            bool loaded = LangData.IsLoaded("items");
            return string.Join(", ", entry.Items.Select(item =>
                (loaded && LangData.Item.Has(item.TemplateId) ? LangData.Item.Name(item.TemplateId) : "objet #" + item.TemplateId.ToString(CultureInfo.InvariantCulture))
                + (item.Quantity > 1 ? " ×" + item.Quantity.ToString(CultureInfo.InvariantCulture) : string.Empty)));
        }

        /// <summary>Durée « m min ss s » (ou « ss s ») d'après la durée en millisecondes de l'en-tête de <c>GE</c>.</summary>
        public static string DurationOf(long milliseconds)
        {
            long seconds = Math.Max(0, milliseconds) / 1000;
            return seconds >= 60
                ? (seconds / 60).ToString(CultureInfo.InvariantCulture) + " min " + (seconds % 60).ToString("00", CultureInfo.InvariantCulture) + " s"
                : seconds.ToString(CultureInfo.InvariantCulture) + " s";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (icons != null) { foreach (Image image in icons.Images) image.Dispose(); icons.Dispose(); icons = null; }
            }
        }
    }
}
