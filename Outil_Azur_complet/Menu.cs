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
        private readonly Dictionary<string, Func<Form>> _formFactories;
        private readonly Dictionary<string, Form> _openForms;

        public Menu()
        {
            InitializeComponent();
            _formFactories = new Dictionary<string, Func<Form>>();
            _openForms = new Dictionary<string, Form>();
            InitializeFormCache();
            BuildEditorMenuLayout();
        }

        private void InitializeFormCache()
        {
            _formFactories["Éditeur de compte"] = () => new editeur_compte.editeurcompte();
            _formFactories["Éditeur de personnage"] = () => new editeur_perso.editeur_perso();
            _formFactories["Outil de recherche"] = () => new outil_recherche.Recherche();
            _formFactories["Éditeur d'objets"] = () => new editeur_items.itemeditor();
            _formFactories["Objets du client"] = () => new editeur_items.ItemClientEditorForm();
            iTalk_ComboBox1.Items.Add("Objets du client");
            _formFactories["Éditeur de maps"] = () => new maps.MainEditeur();
            _formFactories["Gestionnaire"] = () => new RessourceParser();
            _formFactories["AzurBot"] = () => new LoadingBotForm();
            _formFactories["Éditeur de sorts"] = () => new ServerDataForm(ServerResourceKind.Spells, "Éditeur de sorts");
            _formFactories["Éditeur de métiers"] = () => new ServerDataForm(ServerResourceKind.Jobs, "Éditeur de métiers");
            _formFactories["Éditeur de ressources"] = () => new ServerDataForm(ServerResourceKind.Interactives, "Ressources interactives");
            _formFactories["Éditeur de PNJ"] = () => new ServerDataForm(ServerResourceKind.NpcTemplates, "Définitions des PNJ");
            _formFactories["Éditeur de monstres"] = () => new ServerDataForm(ServerResourceKind.Monsters, "Définitions des monstres");
            iTalk_ComboBox1.Items.AddRange(new object[] { "Éditeur de sorts", "Éditeur de métiers", "Éditeur de ressources", "Éditeur de PNJ", "Éditeur de monstres" });
            AddEditor("Éditeur de modèles d'objets",ServerResourceKind.ItemTemplates);
            AddEditor("Éditeur de panoplies",ServerResourceKind.ItemSets);
            AddEditor("Éditeur de recettes",ServerResourceKind.Crafts);
            AddEditor("Éditeur de butins",ServerResourceKind.Drops);
            AddEditor("Questions des PNJ",ServerResourceKind.NpcQuestions);
            AddEditor("Réponses et actions des PNJ",ServerResourceKind.NpcResponses);
            AddEditor("Éditeur de quêtes",ServerResourceKind.Quests);
            AddEditor("Étapes des quêtes",ServerResourceKind.QuestSteps);
            AddEditor("Objectifs et récompenses des quêtes",ServerResourceKind.QuestObjectives);
            AddEditor("Téléportations des cartes",ServerResourceKind.MapTriggers);
            AddEditor("Actions de fin de combat",ServerResourceKind.EndFightActions);
            AddEditor("Entrées de donjons",ServerResourceKind.Dungeons);
            AddEditor("Portes et mécanismes",ServerResourceKind.InteractiveDoors);
            AddEditor("Actions des objets",ServerResourceKind.ObjectActions);
            AddEditor("Placements des PNJ",ServerResourceKind.Npcs);
            AddEditor("Groupes de monstres fixes",ServerResourceKind.MonsterGroups);
            AddEditor("Éditeur d'enclos",ServerResourceKind.Paddocks);
            AddEditor("Éditeur de zaaps",ServerResourceKind.Zaaps);
            AddEditor("Données serveur des cartes",ServerResourceKind.Maps);
        }
        private void AddEditor(string title,ServerResourceKind kind)
        { _formFactories[title]=()=>new ServerDataForm(kind,title);iTalk_ComboBox1.Items.Add(title); }

        private void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            if (iTalk_ComboBox1.SelectedItem == null) return;

            string selection = iTalk_ComboBox1.SelectedItem.ToString();
            
            if (!_formFactories.ContainsKey(selection)) return;

            if (selection != "Éditeur de maps" && selection != "Gestionnaire" && selection != "AzurBot" && selection != "Objets du client" && InitializeForm.NoDB)
            {
                MessageBox.Show("Cette fonctionnalité nécessite une connexion à la base de données.", 
                    "Accès impossible", 
                    MessageBoxButtons.OK, 
                    MessageBoxIcon.Warning);
                return;
            }

            try
            {
                if (selection == "Éditeur de maps" && !InitializeForm.MapEditorOK)
                {
                    MessageBox.Show(
                        "Il manque des dossiers pour une utilisation correcte de l'éditeur de cartes, merci de vérifier l'existence/chemins des dossiers pré-requis puis de relancer l'application afin de pouvoir lancer cette partie de l'application.",
                        "Lancement impossible", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (!_openForms.TryGetValue(selection, out Form form) || form.IsDisposed)
                {
                    form = _formFactories[selection]();
                    _openForms[selection] = form;
                    form.FormClosed += (s, args) => _openForms.Remove(selection);
                }

                if (!form.Visible) form.Show();
                else form.BringToFront();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du lancement du module : {ex.Message}",
                    "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Menu_Load(object sender, EventArgs e)
        {
            iTalk_ComboBox1.Items.RemoveAll(item => string.IsNullOrEmpty(item?.ToString()));
        }

        private void iTalk_Button_12_Click(object sender, EventArgs e)
        {
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            foreach (Form form in _openForms.Values.ToList())
            {
                if (form.IsDisposed) continue;
                form.Close();
                if (!form.IsDisposed)
                {
                    e.Cancel = true;
                    break;
                }
            }
            base.OnFormClosing(e);
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
                foreach (var form in _openForms.Values.ToList())
                {
                    form.Dispose();
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
