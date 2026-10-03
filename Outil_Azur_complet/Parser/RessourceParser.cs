using System;
using System.IO;
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
                iTalk_Button_21.Enabled = false;
                working=true;
                int count = await XmlParser.ParseSQLToXML(path, type, forBot);
                MessageBox.Show($"{count} ressource(s) exportée(s) dans {Path.GetFullPath(path)}.", "Export terminé", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception error) { MessageBox.Show(error.Message, "Export impossible", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { working=false;if (!IsDisposed) iTalk_Button_21.Enabled = true; }
        }
        private static string BotDirectory(string type)
        {
            switch (type)
            {
                case "Maps": return @".\ressources\Bot\BotMaps";
                case "Objets": return @".\ressources\Bot\BotObjets";
                case "Métiers": return @".\ressources\Bot\BotJobs";
                case "PNJs": return @".\ressources\Bot\BotNPCs";
                case "Zaaps": return @".\ressources\Bot\BotZaaps";
                case "Monstres": return @".\ressources\Bot\BotMonsters";
                case "Sorts": return @".\ressources\Bot\BotSorts";
                default: throw new NotSupportedException("Le bot n'utilise pas ce type de ressource XML.");
            }
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
