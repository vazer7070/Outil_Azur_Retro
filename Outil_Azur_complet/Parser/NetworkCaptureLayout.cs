using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Editors;
using Tools_protocol.Network;

namespace Outil_Azur_complet.Parser
{
    public partial class RessourceParser
    {
        private readonly PacketCaptureProxy captureProxy = new PacketCaptureProxy();
        private readonly object captureGate = new object();
        private readonly Queue<CapturedPacket> pendingPackets = new Queue<CapturedPacket>();
        private readonly List<CapturedPacket> capturedPackets = new List<CapturedPacket>();
        private DataGridView captureGrid;
        private TextBox captureDetails;
        private TextBox captureFilter;
        private ComboBox captureDirection;
        private CheckBox captureFollow;
        private NumericUpDown captureListenPort, captureServerPort;
        private TextBox captureServerHost;
        private Control captureStart, captureStop;
        private Label captureState;
        private Timer captureTimer;
        private string pendingCaptureStatus;
        private int captureBytes, droppedPackets;
        private bool stoppingCapture, closingAfterCapture;

        private void BuildNetworkCapture()
        {
            var sheet = layout.Sheet("Capture réseau", 1);
            var page = (TabPage)sheet.Parent;
            var intro = EditorUi.Label("1. Indiquez le serveur de test.  2. Démarrez le relais.  3. Configurez le client vers 127.0.0.1 et le port local.", 10);
            intro.Height = 48; EditorUi.AddField(sheet, intro);
            var settings = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, Height = 110 };
            for (int i = 0; i < 3; i++) settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            captureListenPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 5555, Name = "capture_port_local" };
            captureServerHost = new TextBox { Text = "127.0.0.1", Name = "capture_serveur" };
            captureServerPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 450, Name = "capture_port_serveur" };
            settings.Controls.Add(BoundEditorLayout.FieldPanel(captureListenPort, "Port local", "Le client se connecte à ce port."), 0, 0);
            settings.Controls.Add(BoundEditorLayout.FieldPanel(captureServerHost, "Adresse du serveur", "Serveur de test à joindre."), 1, 0);
            settings.Controls.Add(BoundEditorLayout.FieldPanel(captureServerPort, "Port du serveur", "Port TCP indiqué par votre serveur."), 2, 0);
            foreach (Control control in settings.Controls) control.Dock = DockStyle.Fill;
            EditorUi.AddField(sheet, settings);

            var actions = new FlowLayoutPanel { Height = 42, WrapContents = false, AutoScroll = true };
            captureStart = EditorUi.Button("Démarrer le relais", true, 170);
            captureStop = EditorUi.Button("Arrêter", false, 105); captureStop.Enabled = false;
            var clear = EditorUi.Button("Vider la liste", false, 125);
            var export = EditorUi.Button("Exporter le journal", false, 170);
            actions.Controls.AddRange(new[] { captureStart, captureStop, clear, export }); EditorUi.AddField(sheet, actions);
            captureState = EditorUi.Label("Arrêté · aucun port ouvert", 10, true); captureState.Height = 26; captureState.ForeColor = EditorUi.Blue;
            EditorUi.AddField(sheet, captureState);

            var privacy = EditorUi.Label("Les messages d'authentification sont masqués. Le masquage reste partiel : le jeu et les discussions peuvent contenir des données personnelles. Relais TCP en clair ; aucune redirection automatique du client.", 9);
            privacy.Height = 52; EditorUi.AddField(sheet, privacy);
            var filters = new TableLayoutPanel { ColumnCount = 3, Height = 42 };
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185)); filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            captureFilter = new TextBox { Dock = DockStyle.Top, AccessibleName = "Filtrer les paquets", Name = "capture_filtre" };
            captureDirection = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, AccessibleName = "Sens des paquets" };
            captureDirection.Items.AddRange(new object[] { "Tous les messages", "Client → serveur", "Serveur → client" }); captureDirection.SelectedIndex = 0;
            captureFollow = new CheckBox { Text = "Suivre le flux", Checked = true, Dock = DockStyle.Top, AutoSize = true };
            filters.Controls.Add(captureFilter, 0, 0); filters.Controls.Add(captureDirection, 1, 0); filters.Controls.Add(captureFollow, 2, 0); EditorUi.AddField(sheet, filters);
            captureGrid = new DataGridView {
                Height = 270, Name = "capture_paquets", AccessibleName = "Paquets capturés",
                ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, RowHeadersVisible = false, MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoGenerateColumns = false,
                BackgroundColor = EditorUi.Surface, BorderStyle = BorderStyle.FixedSingle,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowTemplate = { Height = 28 }
            };
            captureGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "heure", HeaderText = "Heure", AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 105 });
            captureGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "connexion", HeaderText = "Connexion", AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 80 });
            captureGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "sens", HeaderText = "Sens", AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 140 });
            captureGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "message", HeaderText = "Message", FillWeight = 75 });
            captureGrid.DefaultCellStyle.SelectionBackColor = EditorUi.Selection; captureGrid.DefaultCellStyle.SelectionForeColor = EditorUi.Ink;
            captureGrid.ColumnHeadersDefaultCellStyle.BackColor = EditorUi.Paper; captureGrid.ColumnHeadersDefaultCellStyle.ForeColor = EditorUi.Ink; captureGrid.DefaultCellStyle.BackColor = EditorUi.Surface; captureGrid.DefaultCellStyle.ForeColor = EditorUi.Ink; captureGrid.GridColor = EditorUi.Border; captureGrid.EnableHeadersVisualStyles = false;
            captureDetails = new TextBox { Multiline = true, ReadOnly = true, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, ForeColor = EditorUi.Ink, ScrollBars = ScrollBars.Vertical, WordWrap = true, AccessibleName = "Message sélectionné", Name = "capture_detail", Font = new Font("Consolas", 9) };
            // Keep the controls and both packet views visible, including in the
            // minimum window size. The packet grid scrolls independently.
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(16, 8, 16, 10), BackColor = EditorUi.Surface };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int rowHeight in new[] { 32, 94, 42, 28, 44, 38 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var views = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, Margin = new Padding(0) };
            views.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            views.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 67)); views.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            captureGrid.Dock = DockStyle.Fill; captureGrid.Margin = new Padding(0, 0, 12, 0); views.Controls.Add(captureGrid, 0, 0);
            var detail = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            var detailTitle = EditorUi.Label("Message sélectionné", 10, true); detailTitle.Dock = DockStyle.Top; detailTitle.Height = 26;
            captureDetails.Dock = DockStyle.Fill; detail.Controls.Add(captureDetails); detail.Controls.Add(detailTitle); views.Controls.Add(detail, 1, 0);
            intro.Text = "Client de test : 127.0.0.1 + port local. Démarrez le relais après avoir indiqué le serveur.";
            var searchLabel = EditorUi.Label("Filtrer", 9); searchLabel.Dock = DockStyle.Left; searchLabel.Width = 45;
            var search = new Panel { Dock = DockStyle.Fill }; filters.Controls.Remove(captureFilter); captureFilter.Dock = DockStyle.Fill; search.Controls.Add(captureFilter); search.Controls.Add(searchLabel); filters.Controls.Add(search, 0, 0);
            Control[] rows = { intro, settings, actions, captureState, privacy, filters, views };
            for (int i = 0; i < rows.Length; i++) { rows[i].Dock = DockStyle.Fill; rows[i].Margin = new Padding(0); root.Controls.Add(rows[i], 0, i); }
            page.Controls.Remove(sheet); sheet.Dispose(); page.AutoScroll = false; page.Controls.Add(root);

            captureStart.Click += (s, e) => StartNetworkCapture();
            captureStop.Click += async (s, e) => await StopNetworkCapture();
            clear.Click += (s, e) => { lock (captureGate) { pendingPackets.Clear(); droppedPackets = 0; } capturedPackets.Clear(); captureBytes = 0; RefreshCaptureRows(); };
            export.Click += (s, e) => ExportNetworkCapture();
            captureFilter.TextChanged += (s, e) => RefreshCaptureRows();
            captureDirection.SelectedIndexChanged += (s, e) => RefreshCaptureRows();
            captureGrid.CurrentCellChanged += (s, e) => { var packet = captureGrid.CurrentRow == null ? null : captureGrid.CurrentRow.Tag as CapturedPacket; captureDetails.Text = packet == null ? "" : packet.Text; };
            captureProxy.PacketCaptured += packet => { lock (captureGate) { if (pendingPackets.Count >= 500) { pendingPackets.Dequeue(); droppedPackets++; } pendingPackets.Enqueue(packet); } };
            captureProxy.StatusChanged += message => { lock (captureGate) pendingCaptureStatus = message; };
            captureTimer = new Timer { Interval = 350 }; captureTimer.Tick += (s, e) => DrainCapture(); captureTimer.Start();
            FormClosing += async (s, e) => {
                if (closingAfterCapture || (!captureProxy.IsRunning && !stoppingCapture)) return;
                e.Cancel = true;
                if (working || stoppingCapture) return;
                await StopNetworkCapture(); closingAfterCapture = true; Close();
            };
            Disposed += (s, e) => { captureTimer.Stop(); captureTimer.Dispose(); captureProxy.Dispose(); };
        }

        private void StartNetworkCapture()
        {
            try {
                captureProxy.Start((int)captureListenPort.Value, captureServerHost.Text.Trim(), (int)captureServerPort.Value);
                SetCaptureButtons(true);
                captureState.Text = "En écoute sur 127.0.0.1:" + captureProxy.ListeningPort + " · en attente du client";
                layout.Status.Text = "Configurez l'adresse et le port du client vers le relais local.";
            } catch (Exception error) { layout.Status.Text = error.Message; MessageBox.Show(this, error.Message, "Relais impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private async Task StopNetworkCapture()
        {
            if (stoppingCapture) return;
            stoppingCapture = true; captureStart.Enabled = false; captureStop.Enabled = false; captureState.Text = "Arrêt du relais…";
            try { await captureProxy.StopAsync(); DrainCapture(); captureState.Text = "Arrêté · journal conservé"; }
            catch (Exception error) { layout.Status.Text = error.Message; captureState.Text = "Erreur d'arrêt : " + error.Message; }
            finally { stoppingCapture = false; SetCaptureButtons(captureProxy.IsRunning); }
        }

        private void SetCaptureButtons(bool running)
        {
            captureStart.Enabled = !running; captureStop.Enabled = running;
            captureListenPort.Enabled = captureServerHost.Enabled = captureServerPort.Enabled = !running;
        }

        private void DrainCapture()
        {
            CapturedPacket[] incoming; string state; int lost;
            lock (captureGate) { incoming = pendingPackets.ToArray(); pendingPackets.Clear(); state = pendingCaptureStatus; pendingCaptureStatus = null; lost = droppedPackets; }
            foreach (var packet in incoming) {
                capturedPackets.Add(packet); captureBytes += packet.Text.Length * 2;
                while (capturedPackets.Count > 1000 || captureBytes > 2 * 1024 * 1024) { captureBytes -= capturedPackets[0].Text.Length * 2; capturedPackets.RemoveAt(0); }
            }
            if (incoming.Length != 0) RefreshCaptureRows();
            if (state != null) captureState.Text = state;
            if (incoming.Length != 0 || state != null) layout.Status.Text = capturedPackets.Count + " message(s) en mémoire · 1 000 maximum / 2 Mo" + (lost == 0 ? "" : " · " + lost + " ignoré(s) pendant une surcharge");
            if (!stoppingCapture) SetCaptureButtons(captureProxy.IsRunning);
        }

        private void RefreshCaptureRows()
        {
            if (captureGrid == null) return;
            var selected = captureGrid.CurrentRow == null ? null : captureGrid.CurrentRow.Tag as CapturedPacket;
            captureGrid.SuspendLayout();
            try {
                captureGrid.Rows.Clear();
                foreach (var packet in FilteredCapture()) {
                    string display = packet.Text.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
                    if (display.Length > 180) display = display.Substring(0, 180) + "…";
                    int row = captureGrid.Rows.Add(packet.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff"), packet.ConnectionId,
                        packet.FromClient ? "Client → serveur" : "Serveur → client", display);
                    captureGrid.Rows[row].Tag = packet;
                    if (packet.Redacted) captureGrid.Rows[row].DefaultCellStyle.ForeColor = EditorUi.Error;
                }
                if (captureGrid.Rows.Count > 0) {
                    var row = !captureFollow.Checked && selected != null ? captureGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => ReferenceEquals(r.Tag, selected)) : null;
                    row = row ?? captureGrid.Rows[captureGrid.Rows.Count - 1]; captureGrid.CurrentCell = row.Cells[0];
                    if (captureFollow.Checked) captureGrid.FirstDisplayedScrollingRowIndex = row.Index;
                    captureDetails.Text = ((CapturedPacket)row.Tag).Text;
                }
                else captureDetails.Text = "";
            } finally { captureGrid.ResumeLayout(); }
        }

        private IEnumerable<CapturedPacket> FilteredCapture()
        {
            string filter = captureFilter.Text.Trim(); int direction = captureDirection.SelectedIndex;
            return capturedPackets.Where(p => (direction != 1 || p.FromClient) && (direction != 2 || !p.FromClient) &&
                (filter.Length == 0 || p.Text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private void ExportNetworkCapture()
        {
            try {
                DrainCapture();
                using (var dialog = new SaveFileDialog { Filter = "Journal texte (*.log)|*.log", DefaultExt = "log", FileName = "capture-azur-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log" })
                if (dialog.ShowDialog(this) == DialogResult.OK) {
                    string target = Path.GetFullPath(dialog.FileName), temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
                    var lines = FilteredCapture().Select(p => p.Timestamp.ToString("O") + "\t" + p.ConnectionId + "\t" + (p.FromClient ? "client→serveur" : "serveur→client") + "\t" +
                        p.Text.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t"));
                    try { File.WriteAllLines(temporary, lines, new UTF8Encoding(false)); if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target); }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    layout.Status.Text = "Journal filtré exporté avec le masquage des messages sensibles.";
                }
            } catch (Exception error) { layout.Status.Text = error.Message; MessageBox.Show(this, error.Message, "Export impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
}
