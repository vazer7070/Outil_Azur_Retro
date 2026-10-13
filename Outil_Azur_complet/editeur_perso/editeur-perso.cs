using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tools_protocol.Kryone.Database;
using Tools_protocol.Query;
using Tools_protocol.Emulators;

namespace Outil_Azur_complet.editeur_perso
{
    public partial class editeur_perso : Form
    {
        private readonly HashSet<string> _characterList;
        private readonly Dictionary<string, iTalk.iTalk_TextBox_Small> _editableFields =
            new Dictionary<string, iTalk.iTalk_TextBox_Small>(StringComparer.Ordinal);
        private readonly Dictionary<int, Dictionary<string, string>> _pendingChanges =
            new Dictionary<int, Dictionary<string, string>>();
        private readonly Dictionary<int, Dictionary<string, object>> _originalCharacterValues =
            new Dictionary<int, Dictionary<string, object>>();
        private int _currentCharacterId;
        private bool _loadingFields;
        private bool _disposed;
        private bool _moderationBusy;
        private readonly string _initialCharacterName;
        private static readonly HashSet<string> AllowedCharacterFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "name", "account", "groupe", "level", "xp", "sexe", "class", "kamas", "capital", "energy", "size",
            "map", "cell", "honor", "deshonor", "wife", "alignement", "gfx", "intelligence", "chance", "agilite",
            "sagesse", "force", "vitalite", "color1", "color2", "color3", "savepos"
        };

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

        public editeur_perso() : this(null)
        {
        }

        public editeur_perso(string initialCharacterName)
        {
            InitializeComponent();
            _initialCharacterName = initialCharacterName;
            _characterList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            InitializeControls();
            BuildEditorLayout();
            FormClosing += (s,e) => { if (_moderationBusy) { e.Cancel = true; return; } if (_pendingChanges.Count > 0 && MessageBox.Show(this,"Des modifications ne sont pas enregistrées. Les abandonner et fermer ?","Modifications en attente",MessageBoxButtons.YesNo,MessageBoxIcon.Question) != DialogResult.Yes) e.Cancel = true; };
        }

        private void InitializeControls()
        {
            foreach (Control control in GetDescendants(this))
                if (control is iTalk.iTalk_TextBox_Small field)
                    field.ReadOnly = true;

            AddEditableField("name", iTalk_TextBox_Small5);
            AddEditableField("account", iTalk_TextBox_Small6);
            AddEditableField("groupe", iTalk_TextBox_Small7);
            AddEditableField("level", iTalk_TextBox_Small8);
            AddEditableField("xp", iTalk_TextBox_Small9);
            AddEditableField("sexe", iTalk_TextBox_Small10);
            AddEditableField("class", iTalk_TextBox_Small11);
            AddEditableField("kamas", iTalk_TextBox_Small12);
            AddEditableField("capital", iTalk_TextBox_Small13);
            AddEditableField("energy", iTalk_TextBox_Small14);
            AddEditableField("size", iTalk_TextBox_Small17);
            AddEditableField("map", iTalk_TextBox_Small18);
            AddEditableField("cell", iTalk_TextBox_Small19);
            AddEditableField("honor", iTalk_TextBox_Small21);
            AddEditableField("deshonor", iTalk_TextBox_Small22);
            AddEditableField("wife", iTalk_TextBox_Small23);
            AddEditableField("alignement", iTalk_TextBox_Small24);
            AddEditableField("gfx", iTalk_TextBox_Small25);
            AddEditableField("intelligence", iTalk_TextBox_Small27);
            AddEditableField("chance", iTalk_TextBox_Small28);
            AddEditableField("agilite", iTalk_TextBox_Small29);
            AddEditableField("sagesse", iTalk_TextBox_Small30);
            AddEditableField("force", iTalk_TextBox_Small31);
            AddEditableField("vitalite", iTalk_TextBox_Small32);
            AddEditableField("color1", iTalk_TextBox_Small2);
            AddEditableField("color2", iTalk_TextBox_Small1);
            AddEditableField("color3", iTalk_TextBox_Small3);
            AddEditableField("savepos", iTalk_TextBox_Small20);

            iTalk_Button_31.Enabled = false;
            iTalk_Button_31.Click += (s, e) => SaveChanges();
            iTalk_Button_11.Enabled = false;
            iTalk_Button_11.Click += async (s, e) => await ToggleCharacterBanAsync();
            iTalk_Button_12.Enabled = false;
            iTalk_Button_12.Click += async (s, e) => await DeleteSelectedCharacterAsync();
            iTalk_Button_21.Enabled = false;
            iTalk_Button_21.Click += (s, e) => OpenInventoryEditor();
            iTalk_Button_22.Enabled = false;
            iTalk_Button_23.Enabled = false;
            iTalk_Button_22.Click += (s, e) => OpenSkillsEditor(CharacterSkillKind.Spells);
            iTalk_Button_23.Click += (s, e) => OpenSkillsEditor(CharacterSkillKind.Jobs);

            textBox1.TextChanged += HandleSearchTextChanged;
            listBox1.SelectedIndexChanged += (s, e) => LoadCharacterDetails();
        }

        private void AddEditableField(string column, iTalk.iTalk_TextBox_Small control)
        {
            _editableFields.Add(column, control);
            control.TextChanged += (s, e) => TrackChange(column, control.Text);
        }

        private static PropertyInfo GetCharacterProperty(string column)
        {
            string propertyName = char.ToUpperInvariant(column[0]) + column.Substring(1);
            return typeof(CharacterList).GetProperty(propertyName);
        }

        private static string DisplayValue(CharacterList character, string column)
        { return DisplayFieldValue(column, GetCharacterProperty(column).GetValue(character)); }

        private static string DisplayFieldValue(string column, object value)
        {
            switch (column)
            {
                case "class": return _classMapping.TryGetValue(Convert.ToInt32(value), out string className)
                    ? className : Convert.ToString(value);
                case "sexe": return Convert.ToInt32(value) == 0 ? "Mâle" : "Femelle";
                case "alignement": return _alignMapping.TryGetValue(Convert.ToInt32(value), out string alignment)
                    ? alignment : Convert.ToString(value);
                case "groupe": return GetGradeName(Convert.ToInt32(value));
                case "wife": return Convert.ToInt32(value) > 0 ? Convert.ToString(value) : "Non marié(e)";
                default: return Convert.ToString(value);
            }
        }

        private void TrackChange(string column, string value)
        {
            if (_loadingFields || _currentCharacterId == 0) return;
            var character = CharacterList.PersoAll.Values.FirstOrDefault(p => p.Id == _currentCharacterId);
            if (character == null || character.Logged != 0 ||
                !_originalCharacterValues.TryGetValue(_currentCharacterId, out var original)) return;
            if (!_pendingChanges.TryGetValue(_currentCharacterId, out var fields))
            {
                fields = new Dictionary<string, string>(StringComparer.Ordinal);
                _pendingChanges.Add(_currentCharacterId, fields);
            }
            if (string.Equals(value, DisplayFieldValue(column, original[column]), StringComparison.Ordinal))
                fields.Remove(column);
            else
                fields[column] = value;
            if (fields.Count == 0) _pendingChanges.Remove(_currentCharacterId);
            UpdatePendingCount();
        }

        private void UpdatePendingCount()
        {
            int count = _pendingChanges.Sum(p => p.Value.Count);
            iTalk_NotificationNumber1.Value = count;
            iTalk_Button_31.Enabled = count > 0;
            if (editorLayout != null) editorLayout.Status.Text = count + " modification(s) en attente · " + iTalk_Label35.Text + ".";
        }

        private static IEnumerable<Control> GetDescendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (Control descendant in GetDescendants(child))
                    yield return descendant;
            }
        }

        private void editeur_perso_Load(object sender, EventArgs e)
        {
            LoadCharacterList();
        }

        private void LoadCharacterList()
        {
            if (_disposed) return;

            try
            {
                _characterList.Clear();
                foreach (string name in CharacterList.PersoAll.Keys)
                    _characterList.Add(name);
                RefreshCharacterList();
                if (!string.IsNullOrWhiteSpace(_initialCharacterName) &&
                    listBox1.Items.Contains(_initialCharacterName))
                    listBox1.SelectedItem = _initialCharacterName;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du chargement : {ex.Message}", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void HandleSearchTextChanged(object sender, EventArgs e)
        {
            if (_disposed) return;

            RefreshCharacterList();
        }

        private void RefreshCharacterList()
        {
            listBox1.BeginUpdate();
            try
            {
                listBox1.Items.Clear();
                var searchText = textBox1.Text?.Trim() ?? string.Empty;

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

        private void LoadCharacterDetails()
        {
            if (_disposed) return;
            if (listBox1.SelectedItem == null)
            {
                _currentCharacterId = 0;
                _loadingFields = true;
                try
                {
                    foreach (var control in GetDescendants(this).OfType<iTalk.iTalk_TextBox_Small>())
                        control.Text = string.Empty;
                    iTalk_ComboBox1.Items.Clear();
                    iTalk_ComboBox2.Items.Clear();
                    iTalk_ComboBox3.Items.Clear();
                    iTalk_Label35.Text = "-";
                    iTalk_Button_11.Enabled = false;
                    iTalk_Button_12.Enabled = false;
                    iTalk_Button_21.Enabled = false;
                    iTalk_Button_22.Enabled = false;
                    iTalk_Button_23.Enabled = false;
                }
                finally { _loadingFields = false; }
                return;
            }

            try
            {
                var characterName = listBox1.SelectedItem.ToString();
                LoadCharacterData(characterName);
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
            _currentCharacterId = character.Id;
            if (!_pendingChanges.ContainsKey(character.Id) || !_originalCharacterValues.ContainsKey(character.Id))
                _originalCharacterValues[character.Id] = AllowedCharacterFields.ToDictionary(column => column,
                    column => GetCharacterProperty(column).GetValue(character), StringComparer.Ordinal);
            var original = _originalCharacterValues[character.Id];
            _loadingFields = true;
            try
            {
                foreach (var field in _editableFields)
                {
                    field.Value.Text = _pendingChanges.TryGetValue(character.Id, out var changes) &&
                        changes.TryGetValue(field.Key, out string pending)
                        ? pending : DisplayFieldValue(field.Key, original[field.Key]);
                    field.Value.ReadOnly = character.Logged != 0;
                }

                // Champs volontairement non modifiables.
                iTalk_TextBox_Small4.Text = character.Id.ToString();
                iTalk_TextBox_Small16.Text = character.Prison == 1 ? "Oui" : "Non";
                iTalk_Label35.Text = character.Logged < 0 ? "État inconnu" : character.Logged == 0 ? "Déconnecté" : "Connecté";
            }
            finally { _loadingFields = false; }
            UpdateModerationControls(character);
            UpdatePendingCount();
            LoadInventoryAndSkills(name);
        }

        private void UpdateModerationControls(CharacterList character)
        {
            bool canAct = !_moderationBusy && character != null && character.Logged == 0 &&
                EmulatorRegistry.Current.Supports(EmulatorFeature.Characters);
            iTalk_Button_11.Enabled = canAct;
            iTalk_Button_12.Enabled = canAct;
            iTalk_Button_21.Enabled = character != null;
            iTalk_Button_22.Enabled = canAct;
            iTalk_Button_23.Enabled = canAct;
            var account = character == null ? null : AccountList.AllAccount.Values.FirstOrDefault(
                value => value.Guid == character.Account);
            iTalk_Button_11.Text = account != null && account.Banned != 0
                ? "Débannir le compte du personnage"
                : "Bannir le compte du personnage";
        }

        private void OpenInventoryEditor()
        {
            var character = CharacterList.PersoAll.Values.FirstOrDefault(value => value.Id == _currentCharacterId);
            if (character == null) return;
            var editor = new editeur_items.itemeditor(character.Name);
            editor.Show(this);
        }

        private void OpenSkillsEditor(CharacterSkillKind kind)
        {
            var character = CharacterList.PersoAll.Values.FirstOrDefault(value => value.Id == _currentCharacterId);
            if (character == null) return;
            using (var editor = new CharacterSkillsForm(character.Id, kind))
                if (editor.ShowDialog(this) == DialogResult.OK)
                {
                    string data = editor.SavedData;
                    if (kind == CharacterSkillKind.Spells) character.Spells = data; else character.Jobs = data;
                    LoadInventoryAndSkills(character.Name);
                }
        }

        private async Task ToggleCharacterBanAsync()
        {
            if (_moderationBusy) return;
            var character = CharacterList.PersoAll.Values.FirstOrDefault(value => value.Id == _currentCharacterId);
            if (character == null) return;
            var account = AccountList.AllAccount.Values.FirstOrDefault(value => value.Guid == character.Account);
            bool banned = account == null || account.Banned == 0;
            _moderationBusy = true;
            UpdateModerationControls(character);
            try
            {
                await Task.Run(() => KryoneModerationService.SetAccountBannedForCharacter(
                    character.Id, character.Name, banned));
                if (account != null) account.Banned = (sbyte)(banned ? 1 : 0);
                MessageBox.Show(banned
                        ? $"Le compte de {character.Name} est maintenant banni."
                        : $"Le compte de {character.Name} n'est plus banni.",
                    "Modération", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Le bannissement n'a pas été modifié : {ex.Message}",
                    "Modération impossible", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _moderationBusy = false;
                if (!_disposed) UpdateModerationControls(character);
            }
        }

        private async Task DeleteSelectedCharacterAsync()
        {
            if (_moderationBusy) return;
            var character = CharacterList.PersoAll.Values.FirstOrDefault(value => value.Id == _currentCharacterId);
            if (character == null) return;
            if (MessageBox.Show(
                $"Supprimer définitivement {character.Name} et tous les objets de ses inventaires ? " +
                "Cette action est irréversible.", "Confirmation de suppression",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
                != DialogResult.Yes) return;

            _moderationBusy = true;
            UpdateModerationControls(character);
            try
            {
                var result = await Task.Run(() => KryoneModerationService.DeleteCharacter(
                    character.Id, character.Name));
                _pendingChanges.Remove(result.CharacterId);
                CharacterList.PersoAll.Remove(result.CharacterName);
                CharacterList.IdAccount.Remove(result.CharacterId);
                foreach (int id in result.ItemIds)
                {
                    ItemList.ItemsList.Remove(id);
                    ItemList.ItemsId.Remove(id);
                }
                CharacterList.PersoCount = CharacterList.PersoAll.Count;
                _currentCharacterId = 0;
                LoadCharacterList();
                LoadCharacterDetails();
                UpdatePendingCount();
                MessageBox.Show($"Le personnage {result.CharacterName} a été supprimé.", "Personnage supprimé",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Aucune donnée n'a été supprimée : {ex.Message}",
                    "Suppression impossible", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _moderationBusy = false;
                if (!_disposed)
                {
                    var current = CharacterList.PersoAll.Values.FirstOrDefault(
                        value => value.Id == _currentCharacterId);
                    UpdateModerationControls(current);
                }
            }
        }

        private static object ParseFieldValue(string column, string text)
        {
            string value = (text ?? string.Empty).Trim();
            if (column == "savepos")
            {
                string[] parts = value.Split(',');
                if (parts.Length != 2 || !int.TryParse(parts[0], out int savedMap) || savedMap <= 0 || !int.TryParse(parts[1], out int savedCell) || savedCell < 0 || savedCell >= 19801)
                    throw new FormatException("Le point de sauvegarde doit être au format carte,cellule, avec une carte positive et une cellule entre 0 et 19 800.");
                return value;
            }
            if (column == "color1" || column == "color2" || column == "color3")
            {
                if (!int.TryParse(value, out int color) || color < -1 || color > 0xFFFFFF) throw new FormatException("Une couleur doit être comprise entre -1 et 16 777 215.");
                return color;
            }
            if (column == "name")
            {
                if (value.Length == 0 || value.Length > 50 || value.Any(char.IsControl))
                    throw new FormatException("Le nom doit contenir entre 1 et 50 caractères imprimables.");
                return value;
            }

            if (column == "wife" && string.Equals(value, "Non marié(e)", StringComparison.OrdinalIgnoreCase))
                value = "0";
            if (column == "sexe")
            {
                if (string.Equals(value, "Mâle", StringComparison.OrdinalIgnoreCase)) value = "0";
                if (string.Equals(value, "Femelle", StringComparison.OrdinalIgnoreCase)) value = "1";
            }
            if (column == "class")
                foreach (var entry in _classMapping)
                    if (string.Equals(value, entry.Value, StringComparison.OrdinalIgnoreCase))
                        value = entry.Key.ToString(CultureInfo.InvariantCulture);
            if (column == "alignement")
                foreach (var entry in _alignMapping)
                    if (string.Equals(value, entry.Value, StringComparison.OrdinalIgnoreCase))
                        value = entry.Key.ToString(CultureInfo.InvariantCulture);
            if (column == "groupe")
                for (int group = 0; group <= 3; group++)
                    if (string.Equals(value, GetGradeName(group), StringComparison.OrdinalIgnoreCase))
                        value = group.ToString(CultureInfo.InvariantCulture);

            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number) || number < 0)
                throw new FormatException($"{column} doit être un entier positif ou nul.");
            if ((column == "level" || column == "account" || column == "size" || column == "gfx") && number == 0)
                throw new FormatException($"{column} doit être supérieur à zéro.");
            if (column == "class" && (number < 1 || number > 12) ||
                column == "sexe" && number > 1 ||
                column == "alignement" && number > 3 ||
                column == "groupe" && number > 3)
                throw new FormatException($"La valeur de {column} n'est pas reconnue.");

            Type type = GetCharacterProperty(column).PropertyType;
            try { return Convert.ChangeType(number, type, CultureInfo.InvariantCulture); }
            catch (OverflowException) { throw new FormatException($"{column} dépasse la capacité du champ."); }
        }

        private static MySqlCommand CreateCharacterUpdateCommand(string table, CharacterList character,
            Dictionary<string, object> values, MySqlConnection connection, MySqlTransaction transaction)
        {
            if (!Tools_protocol.Query.QueryBuilder.IsIdentifier(table) || values.Count == 0 ||
                values.Keys.Any(column => !AllowedCharacterFields.Contains(column)))
                throw new InvalidOperationException("Table ou champ de personnage non autorisé.");
            string assignments = string.Join(", ", values.Keys.Select(
                (column, index) => $"`{column}`=@value{index}"));
            string query = $"UPDATE `{table}` SET {assignments} " +
                "WHERE `id`=@id AND `name`=@originalName AND `logged`=0";
            var command = new MySqlCommand(query, connection, transaction);
            int parameterIndex = 0;
            foreach (var field in values)
                command.Parameters.AddWithValue("@value" + parameterIndex++, field.Value);
            command.Parameters.AddWithValue("@id", character.Id);
            command.Parameters.AddWithValue("@originalName", character.Name);
            return command;
        }

        private static void ApplyCharacterChanges(string table, Dictionary<int, Dictionary<string, object>> prepared,
            Dictionary<int, Dictionary<string, object>> originals)
        {
            ServerSql.Require(EmulatorFeature.Characters);
            ServerSql.Identifier(table);
            string accountTable = AccountList.TableCompte;
            string accounts = ServerSql.Identifier(accountTable);
            if (prepared == null || prepared.Count == 0 || prepared.Any(entry => entry.Value == null ||
                entry.Value.Count == 0 || entry.Value.Keys.Any(column => !AllowedCharacterFields.Contains(column))))
                throw new InvalidOperationException("Les modifications du personnage sont invalides.");
            if (originals == null || prepared.Any(entry => !originals.ContainsKey(entry.Key) || originals[entry.Key] == null ||
                !originals[entry.Key].ContainsKey("name") || !originals[entry.Key].ContainsKey("account") ||
                entry.Value.Keys.Any(column => !originals[entry.Key].ContainsKey(column))))
                throw new InvalidOperationException("Les valeurs d'origine du personnage sont inconnues. Rechargez les données.");
            var renamed = prepared.Where(entry => entry.Value.ContainsKey("name")).Select(entry => Convert.ToString(entry.Value["name"])).ToArray();
            if (renamed.Distinct(StringComparer.OrdinalIgnoreCase).Count() != renamed.Length)
                throw new InvalidOperationException("Deux personnages ne peuvent pas recevoir le même nom.");
            using (var connection = new MySqlConnection(ServerSql.ConnectionFor("perso", EmulatorFeature.Characters)))
            {
                connection.Open();
                ServerSql.RequireInnoDb(connection, table, accountTable);
                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    foreach (var entry in prepared.OrderBy(pair => pair.Key))
                    {
                        var character = CharacterList.PersoAll.Values.FirstOrDefault(p => p.Id == entry.Key);
                        if (character == null) throw new InvalidOperationException("Un personnage modifié n'est plus chargé.");
                        var original = originals[entry.Key];
                        string originalName = Convert.ToString(original["name"]);
                        int originalAccount = Convert.ToInt32(original["account"]);
                        string columns = string.Join(",", new[] { "name", "account", "logged" }.Concat(entry.Value.Keys)
                            .Distinct().Select(ServerSql.Identifier));
                        using (var read = new MySqlCommand("SELECT " + columns + " FROM " + ServerSql.Identifier(table) +
                            " WHERE `id`=@id FOR UPDATE", connection, transaction))
                        {
                            read.Parameters.AddWithValue("@id", character.Id);
                            using (var reader = read.ExecuteReader())
                            {
                                if (!reader.Read() || Convert.ToString(reader["name"]) != originalName ||
                                    Convert.ToInt32(reader["account"]) != originalAccount)
                                    throw new InvalidOperationException("Le personnage ou son compte a changé. Rechargez les données.");
                                ServerSql.RequireOffline(reader["logged"], originalName);
                                foreach (string column in entry.Value.Keys)
                                    if (Convert.ToString(reader[column], CultureInfo.InvariantCulture) !=
                                        Convert.ToString(original[column], CultureInfo.InvariantCulture))
                                        throw new InvalidOperationException("Le champ " + column + " de " + character.Name + " a changé. Rechargez les données.");
                            }
                        }
                        var accountIds = new HashSet<int> { originalAccount };
                        if (entry.Value.TryGetValue("account", out object destination)) accountIds.Add(Convert.ToInt32(destination));
                        foreach (int accountId in accountIds.OrderBy(id => id))
                            using (var read = new MySqlCommand("SELECT `logged` FROM " + accounts + " WHERE `guid`=@id FOR UPDATE", connection, transaction))
                            {
                                read.Parameters.AddWithValue("@id", accountId);
                                object logged = read.ExecuteScalar();
                                if (logged == null) throw new InvalidOperationException("Le compte " + accountId + " n'existe plus.");
                                ServerSql.RequireOffline(logged, "ce compte");
                            }
                        foreach (var field in entry.Value)
                            if (field.Value is string text) ServerSql.RequireTextLength(connection, transaction, table, field.Key, text);
                        if (entry.Value.TryGetValue("name", out object newName))
                            using (var read = new MySqlCommand("SELECT `id` FROM " + ServerSql.Identifier(table) +
                                " WHERE LOWER(CONVERT(`name` USING utf8mb4)) COLLATE utf8mb4_bin=LOWER(CONVERT(@name USING utf8mb4)) COLLATE utf8mb4_bin AND `id`<>@id FOR UPDATE", connection, transaction))
                            {
                                read.Parameters.AddWithValue("@name", newName);
                                read.Parameters.AddWithValue("@id", character.Id);
                                if (read.ExecuteScalar() != null)
                                    throw new InvalidOperationException("Ce nom de personnage est déjà utilisé. Rechargez les données.");
                            }
                        using (var command = CreateCharacterUpdateCommand(table, character, entry.Value, connection, transaction))
                        {
                            command.Parameters["@originalName"].Value = originalName;
                            if (command.ExecuteNonQuery() != 1)
                                throw new InvalidOperationException(character.Name + " a changé. Rechargez les données.");
                        }
                    }
                    transaction.Commit();
                }
            }
        }

        private void SaveChanges()
        {
            if (_pendingChanges.Count == 0) return;
            try
            {
                string table = CharacterList.TablePerso;
                if (!Tools_protocol.Query.QueryBuilder.IsIdentifier(table))
                    throw new InvalidOperationException("Le nom de la table des personnages est invalide.");

                var prepared = new Dictionary<int, Dictionary<string, object>>();
                foreach (var entry in _pendingChanges)
                {
                    var character = CharacterList.PersoAll.Values.FirstOrDefault(p => p.Id == entry.Key);
                    if (character == null) throw new InvalidOperationException("Un personnage modifié n'est plus chargé.");
                    if (character.Logged != 0)
                        throw new InvalidOperationException($"Déconnectez {character.Name} avant de le modifier.");
                    var values = new Dictionary<string, object>();
                    foreach (var field in entry.Value)
                    {
                        if (!_editableFields.ContainsKey(field.Key))
                            throw new InvalidOperationException("Champ de personnage non autorisé.");
                        values.Add(field.Key, ParseFieldValue(field.Key, field.Value));
                    }
                    if (values.TryGetValue("name", out object newName) &&
                        CharacterList.PersoAll.TryGetValue((string)newName, out var existing) &&
                        existing.Id != entry.Key)
                        throw new InvalidOperationException($"Le nom {newName} est déjà utilisé.");
                    prepared.Add(entry.Key, values);
                }

                iTalk_Button_31.Enabled = false;
                ApplyCharacterChanges(table, prepared, _originalCharacterValues);

                int selectedId = _currentCharacterId;
                foreach (var entry in prepared)
                {
                    var character = CharacterList.PersoAll.Values.First(p => p.Id == entry.Key);
                    if (entry.Value.ContainsKey("name")) CharacterList.PersoAll.Remove(character.Name);
                    foreach (var field in entry.Value)
                        GetCharacterProperty(field.Key).SetValue(character, field.Value);
                    if (entry.Value.ContainsKey("name"))
                    {
                        CharacterList.PersoAll.Add(character.Name, character);
                        CharacterList.IdAccount[character.Id] = character.Name;
                    }
                }
                _pendingChanges.Clear();
                _originalCharacterValues.Clear();
                UpdatePendingCount();
                LoadCharacterList();
                var selected = CharacterList.PersoAll.Values.FirstOrDefault(p => p.Id == selectedId);
                if (selected != null)
                {
                    if (!listBox1.Items.Contains(selected.Name)) textBox1.Text = string.Empty;
                    listBox1.SelectedItem = selected.Name;
                }
                MessageBox.Show("Modifications enregistrées.", "Personnage",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Aucune modification enregistrée : {ex.Message}", "Erreur",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { UpdatePendingCount(); }
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
            var parts = jobData.Split(',');
            if (parts.Length >= 2 && int.TryParse(parts[1], out int jobXp))
            {
                var jobName = JobsList.Name_Jobs(parts[0]);
                iTalk_ComboBox3.Items.Add($"{jobName} (XP: {jobXp})");
            }
            else if (!string.IsNullOrWhiteSpace(jobData))
                iTalk_ComboBox3.Items.Add($"Métier non reconnu ({jobData})");
        }

        private string GetClassName(int classId)
        {
            return _classMapping.TryGetValue(classId, out var className) ? className : "Inconnu";
        }

        private string GetAlignmentName(int alignId)
        {
            return _alignMapping.TryGetValue(alignId, out var alignName) ? alignName : "Inconnu";
        }

        private static string GetGradeName(int grade)
        {
            switch (grade)
            {
                case 0: return "Joueur";
                case 1: return "Animateur";
                case 2: return "Maître du jeu";
                case 3: return "Administrateur";
                default: return grade.ToString();
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
                    _characterList.Clear();
                }
                _disposed = true;
            }
            base.Dispose(disposing);
        }
    }

}
