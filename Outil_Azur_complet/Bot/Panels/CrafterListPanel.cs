using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls.Chat;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Game.Jobs;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Livre des artisans (interface <c>CrafterList</c> du client 1.34) : s'ouvre sur <c>ECK14|&lt;métier&gt;;…</c> (livre posé sur la carte),
    /// se ferme sur <c>EV</c>. Choisir un métier envoie <c>EJF&lt;métier&gt;</c> ; la liste suit les <c>EJ+</c> / <c>EJ-</c> du serveur.
    /// Carte de l'artisan choisi (<c>CrafterCard</c>, lot X6) : « Message privé » prépare <c>/w &lt;nom&gt; </c> dans la console
    /// (<c>askPrivateMessage</c>), « Localiser » pointe la boussole du bandeau vers sa carte (<c>updateCompass</c>), actif
    /// seulement quand les textes du client en donnent les coordonnées.
    /// </summary>
    public sealed class CrafterListPanel : GamePanel
    {
        private const string IdleText = "Aucun livre des artisans ouvert. Utilisez un livre des artisans sur la carte (ECK14).";
        private ComboBox jobChoice;
        private ListView crafterList;
        private Label bookStatus, crafterDetail;
        private Control searchButton, leaveButton, messageButton, locateButton;
        private CrafterBook bound;
        private bool wasOpen, filling;

        public override string Title => Text("CRAFTERS_LIST", "Livre des artisans");
        public override bool IsModal => true;
        public override bool IsServerWindowOpen => Game?.Interactions?.Jobs?.Book?.IsOpen == true;
        /// <summary>Artisan sélectionné dans la liste (sa carte), ou <c>null</c>.</summary>
        public Crafter SelectedCrafter => crafterList != null && crafterList.SelectedItems.Count == 1 ? crafterList.SelectedItems[0].Tag as Crafter : null;
        /// <summary>Dernière cible de boussole posée par « Localiser » (tests).</summary>
        public Point? LastCompass { get; private set; }

        /// <summary>Choisit un métier dans la liste du livre et demande ses artisans (<c>EJF</c>) ; faux s'il n'est pas proposé.</summary>
        public bool ChooseJob(int jobId)
        {
            if (jobChoice == null) return false;
            for (int index = 0; index < jobChoice.Items.Count; index++)
                if (jobChoice.Items[index] is JobChoice choice && choice.JobId == jobId) { jobChoice.SelectedIndex = index; return true; }
            return false;
        }

        private static string Text(string key, string fallback) => JobCatalog.Text(key, fallback);

        protected override Control CreateView()
        {
            var page = Page();
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, BackColor = BotUi.Paper, Padding = new Padding(0, 0, 0, 4) };
            var jobLabel = MakeLabel(Text("JOB", "Métier"), 9, true); jobLabel.AutoSize = false; jobLabel.Width = 60; jobLabel.Height = 30;
            jobLabel.TextAlign = ContentAlignment.MiddleLeft;
            jobChoice = new ComboBox { Name = "crafters-job", Width = 190, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                BackColor = BotUi.PaperLight, ForeColor = BotUi.Ink, Font = BotFonts.Get(9), Margin = new Padding(0, 4, 7, 0) };
            jobChoice.SelectedIndexChanged += async (s, e) => { if (!filling) await Search(); };
            searchButton = MakeButton("Actualiser", async (s, e) => await Search(), false, 100); searchButton.Name = "crafters-search";
            top.Controls.Add(jobLabel); top.Controls.Add(jobChoice); top.Controls.Add(searchButton);

            crafterList = MakeList(8.25f, Text("CRAFTER", "Artisan"), Text("LEVEL_SMALL", "Niv."), "Position", "Atelier");
            crafterList.Name = "crafters-list";
            crafterList.Columns[0].Width = 140; crafterList.Columns[1].Width = 46; crafterList.Columns[2].Width = 80; crafterList.Columns[3].Width = 100;
            crafterList.SelectedIndexChanged += (s, e) => ShowDetail();
            crafterDetail = MakeLabel(string.Empty, 8); crafterDetail.Dock = DockStyle.Bottom; crafterDetail.Height = 40; crafterDetail.Name = "crafters-detail";

            bookStatus = MakeStatus(IdleText); bookStatus.Name = "crafters-status";
            leaveButton = MakeButton("Fermer", async (s, e) => await Leave(), false, 100); leaveButton.Name = "crafters-leave";
            messageButton = MakeButton(MapActionTexts.PrivateMessage, (s, e) => PrivateMessage(), false, 180); messageButton.Name = "crafters-whisper";
            locateButton = MakeButton(MapActionTexts.Locate, (s, e) => Locate(), false, 100); locateButton.Name = "crafters-locate";
            messageButton.Enabled = false; locateButton.Enabled = false;
            page.Controls.Add(crafterList); page.Controls.Add(crafterDetail); page.Controls.Add(bookStatus);
            page.Controls.Add(BotUi.Actions(messageButton, locateButton, leaveButton));
            page.Controls.Add(top);
            return page;
        }

        protected override void OnBind(GameClass game)
        {
            bound = game.Interactions?.Jobs?.Book;
            if (bound != null) bound.Changed += OnServerChanged;
        }

        protected override void OnUnbind(GameClass game)
        {
            if (bound != null) bound.Changed -= OnServerChanged;
            bound = null;
            wasOpen = false;
        }

        private void OnServerChanged() => ExchangePanel.PostToForm(Host, () =>
        {
            if (IsDisposed) return;
            bool open = IsServerWindowOpen;
            if (open && !wasOpen) RequestShow();
            else if (!open && wasOpen) RaiseClosed();
            wasOpen = open;
            RefreshView();
        });

        public override void RefreshView()
        {
            CrafterBook book = Game?.Interactions?.Jobs?.Book;
            if (book == null || crafterList == null || IsDisposed) return;
            int[] jobs = book.IsOpen ? book.Jobs.ToArray() : new int[0];
            int[] shown = jobChoice.Items.OfType<JobChoice>().Select(choice => choice.JobId).ToArray();
            if (!shown.SequenceEqual(jobs))
            {
                filling = true;
                try
                {
                    jobChoice.Items.Clear();
                    foreach (int job in jobs) jobChoice.Items.Add(new JobChoice(job));
                    int index = Array.IndexOf(jobs, book.SelectedJob);
                    jobChoice.SelectedIndex = index >= 0 ? index : -1;
                }
                finally { filling = false; }
            }

            string selected = crafterList.SelectedItems.Count == 1 ? (crafterList.SelectedItems[0].Tag as Crafter)?.Id : null;
            crafterList.BeginUpdate(); crafterList.Items.Clear();
            foreach (Crafter crafter in book.Crafters.OrderByDescending(entry => entry.JobLevel).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var row = crafterList.Items.Add(crafter.Name);
                row.SubItems.Add(crafter.JobLevel.ToString(CultureInfo.CurrentCulture));
                row.SubItems.Add(crafter.Location);
                row.SubItems.Add(crafter.InWorkshop ? Text("IN_WORKSHOP", "En atelier") : Text("OUTSIDE_WORKSHOP", "Hors atelier"));
                row.ForeColor = crafter.InWorkshop ? BotUi.Olive : BotUi.Ink;
                row.Tag = crafter;
                if (crafter.Id == selected) row.Selected = true;
            }
            crafterList.EndUpdate();
            ShowDetail();

            string message = book.LastMessage ?? string.Empty;
            if (!book.IsOpen) bookStatus.Text = IdleText + (message.Length > 0 ? " " + message : string.Empty);
            else if (book.SelectedJob == 0) bookStatus.Text = "Choisissez un métier : le serveur envoie les artisans connectés qui l'exercent (EJF).";
            else bookStatus.Text = book.Crafters.Count.ToString(CultureInfo.CurrentCulture) + " artisan(s) « " + JobCatalog.JobName(book.SelectedJob) + " » connecté(s).";
            bool open = Connected && book.IsOpen;
            searchButton.Enabled = open && jobChoice.SelectedItem is JobChoice;
            jobChoice.Enabled = open && jobs.Length > 0;
            leaveButton.Enabled = open;
        }

        private void ShowDetail()
        {
            Crafter crafter = SelectedCrafter;
            messageButton.Enabled = crafter != null;
            locateButton.Enabled = CoordsOf(crafter) != null;
            if (crafter == null) { crafterDetail.Text = string.Empty; return; }
            JobOptions options = crafter.Options;
            string price = options.IsNotFree ? Text("NOT_FREE", "Payant") + (options.IsFreeIfFailed ? ", " + Text("FREE_IF_FAILED", "gratuit en cas d'échec") : string.Empty)
                : "Gratuit";
            crafterDetail.Text = crafter.Name + " : " + price + (options.ResourcesNeeded ? " · " + Text("CRAFT_RESSOURCES_NEEDED", "ressources à fournir") : string.Empty)
                + " · " + Text("MIN_ITEM_IN_RECEIPT", "ingrédients minimum") + " " + options.MinSlots.ToString(CultureInfo.CurrentCulture) + ".";
        }

        /// <summary>Coordonnées de la carte d'un artisan d'après les textes du client (<c>maps.xml</c>), sinon <c>null</c>.</summary>
        private static Point? CoordsOf(Crafter crafter)
        {
            if (crafter == null || crafter.MapId <= 0) return null;
            try { return LangData.Map.Coords(crafter.MapId); }
            catch (Exception error) when (!(error is OutOfMemoryException)) { return null; }
        }

        /// <summary>« Message privé » (<c>CrafterCard._btnPrivateMessage</c> → <c>askPrivateMessage</c>) : « /w &lt;nom&gt; » dans la console, sans paquet.</summary>
        private void PrivateMessage()
        {
            Crafter crafter = SelectedCrafter;
            MapActions actions = Game?.Interactions?.MapActions;
            if (crafter == null) { Feedback("Choisissez un artisan dans la liste."); return; }
            if (actions == null) { Feedback("La session n’est plus disponible."); return; }
            Feedback(actions.RequestPrivateMessage(crafter.Name)?.Message);
        }

        /// <summary>« Localiser » (<c>CrafterCard._btnLocate</c> → <c>updateCompass</c>) : boussole du bandeau vers la carte de l'artisan.</summary>
        private void Locate()
        {
            Crafter crafter = SelectedCrafter;
            if (crafter == null) { Feedback("Choisissez un artisan dans la liste."); return; }
            Point? where = CoordsOf(crafter);
            if (where == null) { Feedback("Position de " + crafter.Name + " inconnue : la carte " + crafter.MapId.ToString(CultureInfo.InvariantCulture) + " n'est pas dans les textes du client."); return; }
            LastCompass = where;
            (Host?.FindForm() as GameClientFullform)?.Banner?.SetCompassTarget(where);
            Feedback(ChatLinks.DescribeCompass(where.Value.X, where.Value.Y, Game?.Map));
        }

        protected internal override bool OnUserClose()
        {
            if (!IsServerWindowOpen) return true;
            _ = Leave();
            return false;
        }

        private Task Search()
        {
            CrafterBook book = Game?.Interactions?.Jobs?.Book;
            if (book == null || !(jobChoice.SelectedItem is JobChoice choice)) return Task.CompletedTask;
            return ReportAsync(() => book.FindCraftersAsync(choice.JobId));
        }

        private Task Leave()
        {
            CrafterBook book = Game?.Interactions?.Jobs?.Book;
            return book == null ? Task.CompletedTask : ReportAsync(book.LeaveAsync);
        }

        private sealed class JobChoice
        {
            internal JobChoice(int jobId) { JobId = jobId; Name = JobCatalog.JobName(jobId); }
            internal int JobId { get; }
            private string Name { get; }
            public override string ToString() => Name;
        }
    }
}
