using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Tools_protocol.Json;

namespace Outil_Azur_complet.Annexes
{
    public partial class JsonModifier : Form
    {
        private readonly Dictionary<string, string> _authChanges = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _worldChanges = new Dictionary<string, string>();
        private string _currentSection;
        private string _currentKey;
        private bool _loadingSelection;

        public JsonModifier()
        {
            InitializeComponent();
            BuildEditorLayout();
        }

        private void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void JsonModifier_Load(object sender, EventArgs e)
        {
            if (iTalk_ComboBox1.SelectedItem == null && iTalk_ComboBox1.Items.Count > 0)
                iTalk_ComboBox1.SelectedIndex = 0;
            else
                LoadName();
        }

        private void SaveCurrentField()
        {
            if (_loadingSelection || _currentKey == null)
                return;

            bool isAuth = _currentSection == "auth";
            Dictionary<string, string> changes = isAuth ? _authChanges : _worldChanges;
            string original = isAuth ? JsonManager.SearchAuth(_currentKey) : JsonManager.SearchWorld(_currentKey);
            string value = iTalk_TextBox_Small1.Text?.Trim();
            if (string.Equals(value, original, StringComparison.Ordinal))
                changes.Remove(_currentKey);
            else
                changes[_currentKey] = value;
        }

        private void ShowSelectedField()
        {
            _currentSection = iTalk_ComboBox1.SelectedItem?.ToString();
            _currentKey = iTalk_ComboBox2.SelectedItem?.ToString();
            _loadingSelection = true;
            try
            {
                if (_currentKey == null)
                {
                    iTalk_TextBox_Small1.Text = string.Empty;
                    return;
                }

                bool isAuth = _currentSection == "auth";
                Dictionary<string, string> changes = isAuth ? _authChanges : _worldChanges;
                string original = isAuth ? JsonManager.SearchAuth(_currentKey) : JsonManager.SearchWorld(_currentKey);
                iTalk_TextBox_Small1.Text = changes.TryGetValue(_currentKey, out string pending)
                    ? pending : original;
            }
            finally
            {
                _loadingSelection = false;
            }
        }

        private void LoadName()
        {
            SaveCurrentField();
            _loadingSelection = true;
            try
            {
                iTalk_ComboBox2.Items.Clear();
                string section = iTalk_ComboBox1.SelectedItem?.ToString();
                IEnumerable<string> keys = section == "auth" ? JsonManager.Auth_dico.Keys
                    : section == "world" ? JsonManager.World_dico.Keys
                    : Enumerable.Empty<string>();
                foreach (string key in keys)
                    iTalk_ComboBox2.Items.Add(key);
                iTalk_ComboBox2.SelectedIndex = iTalk_ComboBox2.Items.Count > 0 ? 0 : -1;
            }
            finally
            {
                _loadingSelection = false;
            }
            ShowSelectedField();
        }

        private void iTalk_ComboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!_loadingSelection)
                LoadName();
        }

        private void iTalk_ComboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_loadingSelection)
                return;
            SaveCurrentField();
            ShowSelectedField();
        }

        private void iTalk_TextBox_Small1_TextChanged(object sender, EventArgs e)
        {
        }

        private void iTalk_Button_21_Click(object sender, EventArgs e)
        {
            SaveCurrentField();
            if (_authChanges.Count == 0 && _worldChanges.Count == 0)
                return;

            try
            {
                if (_authChanges.Count > 0)
                    JsonManager.RewriteTableJson(JsonConvert.SerializeObject(_authChanges), true);
                if (_worldChanges.Count > 0)
                    JsonManager.RewriteTableJson(JsonConvert.SerializeObject(_worldChanges), false);
            }
            catch (Exception exception)
            {
                MessageBox.Show($"Enregistrement impossible : {exception.Message}", "Configuration", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            MessageBox.Show("Les noms de tables ont été enregistrés. L'application va redémarrer pour les appliquer.",
                "Configuration", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _authChanges.Clear();
            _worldChanges.Clear();
            // Do not stage the just-saved field again while Restart closes the form.
            _currentKey = null;
            Application.Restart();
        }
    }
}
