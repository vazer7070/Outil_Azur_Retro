using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tools_protocol.Kryone.Database;

namespace Outil_Azur_complet.editeur_perso
{
    public partial class editeur_perso : Form, IDisposable
    {
        private readonly HashSet<string> _characterList;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly SemaphoreSlim _loadSemaphore;
        private bool _disposed;

        private const int NEUTRE = 0;
        private const int ANGE = 1;
        private const int DEMON = 2;
        private const int SERIANNE = 3;

        private static readonly Dictionary<int, string> _classMapping = new Dictionary<int, string>
        {
            {1, "Féca"}, {2, "Osamoda"}, {3, "Énutrof"}, {4, "Sram"},
            {5, "Xélor"}, {6, "Écaflip"}, {7, "Éniripsa"}, {8, "Iop"},
            {9, "Crâ"}, {10, "Sadida"}, {11, "Sacrieur"}, {12, "Pandawa"}
        };

        private static readonly Dictionary<int, string> _alignMapping = new Dictionary<int, string>
        {
            {NEUTRE, "Neutre"}, {ANGE, "Ange"}, {DEMON, "Démon"}, {SERIANNE, "Sérianne"}
        };

        public editeur_perso()
        {
            InitializeComponent();
            _characterList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _cancellationTokenSource = new CancellationTokenSource();
            _loadSemaphore = new SemaphoreSlim(1, 1);
            InitializeControls();
        }

        private void InitializeControls()
        {
            // Désactiver les contrôles en lecture seule
            iTalk_TextBox_Small4.Enabled = false;
            iTalk_TextBox_Small16.Enabled = false;
            iTalk_TextBox_Small20.Enabled = false;
            iTalk_TextBox_Small1.Enabled = false;
            iTalk_TextBox_Small2.Enabled = false;
            iTalk_TextBox_Small3.Enabled = false;

            // Attacher les gestionnaires d'événements
            FormClosing += (s, e) => _cancellationTokenSource.Cancel();
            textBox1.TextChanged += HandleSearchTextChanged;
            listBox1.SelectedIndexChanged += async (s, e) => await LoadCharacterDetailsAsync();
        }

        private async void editeur_perso_Load(object sender, EventArgs e)
        {
            await LoadCharacterListAsync();
        }

        private async Task LoadCharacterListAsync(bool clearExisting = false)
        {
            if (_disposed) return;

            try
            {
                await _loadSemaphore.WaitAsync();
                
                listBox1.BeginUpdate();
                if (clearExisting)
                {
                    listBox1.Items.Clear();
                    _characterList.Clear();
                }

                await Task.Run(() =>
                {
                    foreach (string name in CharacterList.PersoAll.Keys)
                    {
                        var lowerName = name.ToLower();
                        if (!_characterList.Contains(lowerName))
                        {
                            this.InvokeIfRequired(() => listBox1.Items.Add(name));
                            _characterList.Add(lowerName);
                        }
                    }
                });

                UpdateCharacterCount();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du chargement : {ex.Message}", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                listBox1.EndUpdate();
                _loadSemaphore.Release();
            }
        }

        private void HandleSearchTextChanged(object sender, EventArgs e)
        {
            if (_disposed) return;

            listBox1.BeginUpdate();
            try
            {
                listBox1.Items.Clear();
                var searchText = textBox1.Text?.Trim().ToLower() ?? string.Empty;

                var filteredItems = string.IsNullOrEmpty(searchText) 
                    ? _characterList 
                    : _characterList.Where(name => name.StartsWith(searchText, StringComparison.OrdinalIgnoreCase));

                foreach (var item in filteredItems)
                {
                    listBox1.Items.Add(item);
                }

                UpdateCharacterCount();
            }
            finally
            {
                listBox1.EndUpdate();
            }
        }

        private async Task LoadCharacterDetailsAsync()
        {
            if (_disposed || listBox1.SelectedItem == null) return;

            try
            {
                var characterName = listBox1.SelectedItem.ToString();
                await Task.Run(() => LoadCharacterData(characterName));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du chargement des détails : {ex.Message}", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadCharacterData(string name)
        {
            var character = CharacterList.Listing(name);
            if (character == null) return;

            this.InvokeIfRequired(() =>
            {
                // Informations de base
                iTalk_TextBox_Small4.Text = character.Id.ToString();
                iTalk_TextBox_Small5.Text = character.Name;
                iTalk_TextBox_Small8.Text = character.Level.ToString();
                iTalk_TextBox_Small9.Text = character.Xp.ToString();
                iTalk_TextBox_Small12.Text = character.Kamas.ToString();

                // Statistiques
                iTalk_TextBox_Small13.Text = character.Capital.ToString();
                iTalk_TextBox_Small14.Text = character.Energy.ToString();
                iTalk_TextBox_Small17.Text = character.Size.ToString();
                iTalk_TextBox_Small18.Text = character.Map.ToString();
                iTalk_TextBox_Small19.Text = character.Cell.ToString();
                iTalk_TextBox_Small20.Text = character.Savepos;

                // Caractéristiques
                iTalk_TextBox_Small27.Text = character.Intelligence.ToString();
                iTalk_TextBox_Small28.Text = character.Chance.ToString();
                iTalk_TextBox_Small29.Text = character.Agilite.ToString();
                iTalk_TextBox_Small30.Text = character.Sagesse.ToString();
                iTalk_TextBox_Small31.Text = character.Force.ToString();
                iTalk_TextBox_Small32.Text = character.Vitalite.ToString();

                // Apparence
                iTalk_TextBox_Small25.Text = character.Gfx.ToString();
                iTalk_TextBox_Small2.Text = character.Color1.ToString();
                iTalk_TextBox_Small1.Text = character.Color2.ToString();
                iTalk_TextBox_Small3.Text = character.Color3.ToString();

                // Informations supplémentaires
                iTalk_TextBox_Small11.Text = GetClassName(character.Class);
                iTalk_TextBox_Small10.Text = character.Sexe == 0 ? "Mâle" : "Femelle";
                iTalk_TextBox_Small24.Text = GetAlignmentName(character.Alignement);
                iTalk_TextBox_Small16.Text = character.Prison == 1 ? "Oui" : "Non";
                iTalk_Label35.Text = character.Logged == 1 ? "Connecté" : "Déconnecté";
                iTalk_TextBox_Small23.Text = character.Wife > 0 ? character.Wife.ToString() : "Non marié(e)";
                iTalk_TextBox_Small6.Text = character.Account.ToString();
                iTalk_TextBox_Small7.Text = GetGradeName(character.Groupe);

                LoadInventoryAndSkills(name);
            });
        }

        private void LoadInventoryAndSkills(string name)
        {
            iTalk_ComboBox1.BeginUpdate();
            iTalk_ComboBox2.BeginUpdate();
            iTalk_ComboBox3.BeginUpdate();

            try
            {
                // Inventaire
                iTalk_ComboBox1.Items.Clear();
                CharacterList.GetInventory(name);
                iTalk_ComboBox1.Items.AddRange(CharacterList.ItemsPerso.ToArray());

                // Sorts
                iTalk_ComboBox2.Items.Clear();
                CharacterList.GetSpells(name);
                iTalk_ComboBox2.Items.AddRange(SpellsList.SpellsShow.ToArray());

                // Métiers
                iTalk_ComboBox3.Items.Clear();
                var jobs = JobsList.LookJobs(name);
                if (!string.IsNullOrEmpty(jobs))
                {
                    LoadJobsList(jobs);
                }
            }
            finally
            {
                iTalk_ComboBox1.EndUpdate();
                iTalk_ComboBox2.EndUpdate();
                iTalk_ComboBox3.EndUpdate();
            }
        }

        private void LoadJobsList(string jobs)
        {
            if (jobs.Contains(";"))
            {
                foreach (var job in jobs.Split(';'))
                {
                    AddJobToList(job);
                }
            }
            else
            {
                AddJobToList(jobs);
            }
        }

        private void AddJobToList(string jobData)
        {
            try
            {
                var parts = jobData.Split(',');
                var jobName = JobsList.Name_Jobs(parts[0]);
                var jobXp = int.Parse(parts[1]);
                iTalk_ComboBox3.Items.Add($"{jobName} (XP: {jobXp})");
            }
            catch
            {
                iTalk_ComboBox3.Items.Add("Aucun métier");
            }
        }

        private string GetClassName(int classId)
        {
            return _classMapping.TryGetValue(classId, out var className) ? className : "Inconnu";
        }

        private string GetAlignmentName(int alignId)
        {
            return _alignMapping.TryGetValue(alignId, out var alignName) ? alignName : "Inconnu";
        }

        private string GetGradeName(int grade)
        {
            switch (grade)
            {
                case 0: return "Joueur";
                case 1: return "Animateur";
                case 2: return "Maître du jeu";
                case 3: return "Administrateur";
                default: return "Inconnu";
            }
           
        }

        private void UpdateCharacterCount()
        {
            iTalk_Label1.Text = $"Nombre de personnages: {listBox1.Items.Count}";
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource.Dispose();
                    _loadSemaphore.Dispose();
                }
                _disposed = true;
            }
            base.Dispose(disposing);
        }
    }

    public static class ControlExtensions
    {
        public static void InvokeIfRequired(this Control control, Action action)
        {
            if (control.InvokeRequired)
                control.Invoke(action);
            else
                action();
        }
    }
}
