using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Tools_protocol.Parser.XML;

namespace Outil_Azur_complet.Parser
{
    public partial class RessourceParser : Form
    {
        public RessourceParser()
        {
            InitializeComponent();
            iTalk_RadioButton1.Checked = true;
            iTalk_ComboBox1.SelectedIndex = 0;
            BuildEditorLayout();
        }
        private void iTalk_Button_11_Click(object sender, EventArgs e) => Close();
        private void iTalk_RadioButton2_CheckedChanged(object sender) => iTalk_TextBox_Small1.Enabled = !iTalk_RadioButton2.Checked;
        private async void iTalk_Button_21_Click(object sender, EventArgs e)
        {
            try
            {
                if (InitializeForm.NoDB) throw new InvalidOperationException("Connectez les bases de données avant d'exporter les ressources.");
                string type = iTalk_ComboBox1.SelectedItem as string;
                if (string.IsNullOrEmpty(type)) throw new InvalidOperationException("Sélectionnez les données à exporter.");
                bool forBot = iTalk_RadioButton2.Checked;
                string path = forBot ? BotDirectory(type) : iTalk_TextBox_Small1.Text;
                BotExportContext context = forBot ? BotExportContext.ForCurrentEmulator() : null;
                if (forBot && type == "Maps") context.MapBackground = ClientBackgroundReader(clientFolder.Text);
                iTalk_Button_21.Enabled = false;
                working=true;
                int count = await XmlParser.ParseSQLToXML(path, type, forBot, context);
                string message = $"{count} ressource(s) exportée(s) dans {Path.GetFullPath(path)}.";
                bool warned = context != null && context.WarningCount > 0;
                if (warned)
                    message += Environment.NewLine + Environment.NewLine + context.WarningCount + " avertissement(s) :" + Environment.NewLine
                        + string.Join(Environment.NewLine, context.Warnings.Take(12)) + (context.WarningCount > 12 ? Environment.NewLine + "…" : "");
                MessageBox.Show(message, "Export terminé", MessageBoxButtons.OK, warned ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
            catch (Exception error) { MessageBox.Show(error.Message, "Export impossible", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { working=false;if (!IsDisposed) iTalk_Button_21.Enabled = true; }
        }
        /// <summary>Dossier lu par le bot pour un type de ressource (à côté de l'exécutable, comme les chargeurs du bot).</summary>
        private static string BotDirectory(string type)
        {
            string folder;
            switch (type)
            {
                case "Maps": folder = "BotMaps"; break;
                case "Objets": folder = "BotObjets"; break;
                case "Métiers": folder = "BotJobs"; break;
                case "PNJs": folder = "BotNPCs"; break;
                case "Zaaps": folder = "BotZaaps"; break;
                case "Monstres": folder = "BotMonsters"; break;
                case "Sorts": folder = "BotSorts"; break;
                case "Interactifs": folder = "BotInteractives"; break;
                case "Déclencheurs": folder = "BotTriggers"; break;
                case "Zaapis": folder = "BotZaapis"; break;
                case "Panoplies": folder = "BotItemSets"; break;
                default: throw new NotSupportedException("Le bot n'utilise pas ce type de ressource XML.");
            }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ressources", "Bot", folder);
        }

        /// <summary>
        /// Lecteur du fond des cartes dans le client (StarLoco n'a pas de colonne background) ; null si aucun dossier
        /// n'est indiqué : les cartes sont alors exportées avec BACK = 0 et un avertissement.
        /// </summary>
        private static Func<MapBackgroundRequest, int> ClientBackgroundReader(string clientDirectory)
        {
            if (string.IsNullOrWhiteSpace(clientDirectory)) return null;
            string maps = ResourceMapConversion.ClientMapsDirectory(clientDirectory);
            if (maps == null) throw new DirectoryNotFoundException("Le dossier du client est introuvable : " + clientDirectory);
            return request => ResourceMapConversion.ReadClientBackground(maps, request);
        }
        private void iTalk_LinkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
                if (dialog.ShowDialog(this) == DialogResult.OK) iTalk_TextBox_Small1.Text = dialog.SelectedPath;
        }
        private void iTalk_TextBox_Small1_TextChanged(object sender, EventArgs e) { }
        private void iTalk_TextBox_Small1_Click(object sender, EventArgs e) { }
        private void iTalk_ComboBox1_SelectedIndexChanged(object sender, EventArgs e) { }
    }
}
