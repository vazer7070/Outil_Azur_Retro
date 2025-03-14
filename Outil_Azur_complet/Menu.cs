using Outil_Azur_complet.Bot;
using Outil_Azur_complet.Parser;
using System;
using System.IO;
using System.Windows.Forms;
using Tools_protocol;
using Tools_protocol.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace Outil_Azur_complet
{
    public partial class Menu : Form
    {
        private readonly Dictionary<string, Lazy<Form>> _formCache;

        public Menu()
        {
            InitializeComponent();
            _formCache = new Dictionary<string, Lazy<Form>>();
            InitializeFormCache();
        }

        private void InitializeFormCache()
        {
            _formCache["Éditeur de compte"] = new Lazy<Form>(() => new editeur_compte.editeurcompte());
            _formCache["Éditeur de personnage"] = new Lazy<Form>(() => new editeur_perso.editeur_perso());
            _formCache["Outil de recherche"] = new Lazy<Form>(() => new outil_recherche.Recherche());
            _formCache["Éditeur d'objets"] = new Lazy<Form>(() => new editeur_items.itemeditor());
            _formCache["Éditeur de maps"] = new Lazy<Form>(() => new maps.MainEditeur());
            _formCache["Gestionnaire"] = new Lazy<Form>(() => new RessourceParser());
            _formCache["AzurBot"] = new Lazy<Form>(() => new LoadingBotForm());
        }

        private async void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            if (iTalk_ComboBox1.SelectedItem == null) return;

            string selection = iTalk_ComboBox1.SelectedItem.ToString();
            
            if (!_formCache.ContainsKey(selection)) return;

            if (selection != "Éditeur de maps" && selection != "Gestionnaire" && selection != "AzurBot" && InitializeForm.NoDB)
            {
                MessageBox.Show("Cette fonctionnalité nécessite une connexion à la base de données.", 
                    "Accès impossible", 
                    MessageBoxButtons.OK, 
                    MessageBoxIcon.Warning);
                return;
            }

            await Task.Run(() => {
                try
                {
                    this.Invoke((MethodInvoker)delegate {
                        if (selection == "Éditeur de maps" && !InitializeForm.MapEditorOK)
                        {
                            MessageBox.Show(
                                "Il manque des dossiers pour une utilisation correcte de l'éditeur de cartes, merci de vérifier l'existence/chemins des dossiers pré-requis puis de relancer l'application afin de pouvoir lancer cette partie de l'application.", 
                                "Lancement impossible", 
                                MessageBoxButtons.OK, 
                                MessageBoxIcon.Error);
                            return;
                        }

                        var form = _formCache[selection].Value;
                        if (!form.Visible)
                        {
                            form.Show();
                        }
                        else
                        {
                            form.BringToFront();
                        }
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur lors du lancement du module : {ex.Message}", 
                        "Erreur", 
                        MessageBoxButtons.OK, 
                        MessageBoxIcon.Error);
                }
            });
        }

        private void Menu_Load(object sender, EventArgs e)
        {
            iTalk_ComboBox1.Items.RemoveAll(item => string.IsNullOrEmpty(item?.ToString()));
        }

        private async void iTalk_Button_12_Click(object sender, EventArgs e)
        {
            await Task.Run(() => {
                try
                {
                    this.Invoke((MethodInvoker)delegate {
                        if (Application.OpenForms.Count == 0)
                        {
                            Application.ExitThread();
                        }
                        else
                        {
                            foreach (Form form in Application.OpenForms.Cast<Form>().ToList())
                            {
                                form.Close();
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    // Log l'erreur si nécessaire
                }
            });
        }

        private void iTalk_Button_13_Click(object sender, EventArgs e)
        {
            using (var settingsForm = new SettingsForm())
            {
                settingsForm.ShowDialog();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var form in _formCache.Values.Where(f => f.IsValueCreated))
                {
                    form.Value?.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }

    public static class ComboBoxExtensions
    {
        public static void RemoveAll(this ComboBox.ObjectCollection items, Func<object, bool> predicate)
        {
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (predicate(items[i]))
                {
                    items.RemoveAt(i);
                }
            }
        }
    }
}
