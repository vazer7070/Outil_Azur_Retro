using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tools_protocol.Codebreak.Database;
using Tools_protocol.Json;
using Tools_protocol.Kryone.Database;
using Tools_protocol.Managers;
using Tools_protocol.Query;
using System.Collections.Concurrent;
using System.Linq;
using System.Collections;
using System.Text.RegularExpressions;
using Tools_protocol.Emulators;

namespace Outil_Azur_complet.editeur_compte
{
    public partial class editeurcompte : Form, IDisposable
    {
        private readonly ConcurrentDictionary<string, PendingAccountChange> _pendingChanges;
        private readonly HashSet<string> _accountList;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly object _lockObject = new object();
        private bool _disposed;

        List<Dictionary<string, object>> allAccounts = new List<Dictionary<string, object>>();
        private readonly SemaphoreSlim _loadSemaphore = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _updateSemaphore = new SemaphoreSlim(1, 1);
        private bool _loadingFields;
        private bool _saving;

        private sealed class PendingAccountChange
        {
            public uint AccountId { get; set; }
            public string Field { get; set; }
            public string Value { get; set; }
            public string OriginalValue { get; set; }
        }

        public string TableCompte => EmulatorRegistry.Current.Table("comptes");
        private static bool CanEditAccounts => EmulatorRegistry.Current.Supports(EmulatorFeature.AccountEditing);
        public string TablePerso => EmuManager.ReturnTable("perso", InitializeForm.EMUSELECT);

        public editeurcompte()
        {
            InitializeComponent();
            
            _pendingChanges = new ConcurrentDictionary<string, PendingAccountChange>();
            _accountList = new HashSet<string>();
            _cancellationTokenSource = new CancellationTokenSource();

            InitializeEventHandlers();
            ConfigureControls();
            BuildEditorLayout();
        }

        private void InitializeEventHandlers()
        {
            FormClosing += (s, e) =>
            {
                if (_saving)
                {
                    e.Cancel = true;
                    MessageBox.Show("Veuillez attendre la fin de l'enregistrement.", "Compte",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else if (_pendingChanges.Count > 0 && MessageBox.Show(this,"Des modifications ne sont pas enregistrées. Les abandonner et fermer ?","Modifications en attente",MessageBoxButtons.YesNo,MessageBoxIcon.Question) != DialogResult.Yes)
                    e.Cancel = true;
                else
                    _cancellationTokenSource.Cancel();
            };
            listBox1.SelectedIndexChanged += async (s, e) => await LoadAccountDetailsAsync();
            textBox1.TextChanged += HandleSearchTextChanged;
            iTalk_Button_22.Click += async (s, e) => await SaveChangesAsync();
            iTalk_Button_14.Click += async (s, e) =>
            {
                using (var form = new CreateForm())
                {
                    if (form.ShowDialog(this) == DialogResult.OK)
                        await LoadEditorAsync(InitializeForm.EMUSELECT);
                }
            };
            iTalk_Button_15.Click += (s, e) => Close();
            iTalk_Button_13.Click += (s, e) => ToggleAccountFlag("vip", iTalk_TextBox_Small11);
            iTalk_Button_12.Click += (s, e) => ToggleAccountFlag("banned", iTalk_TextBox_Small5);
            iTalk_Button_11.Click += async (s, e) => await DeleteSelectedAccountAsync();
            iTalk_Button_21.Click += (s, e) => OpenSelectedCharacter();

            iTalk_TextBox_Small3.TextChanged += (s, e) => QueueAccountChange("pseudo", iTalk_TextBox_Small3.Text);
            iTalk_TextBox_Small4.TextChanged += (s, e) => QueueAccountChange("pass", iTalk_TextBox_Small4.Text);
            iTalk_TextBox_Small6.TextChanged += (s, e) => QueueAccountChange("question", iTalk_TextBox_Small6.Text);
            iTalk_TextBox_Small7.TextChanged += (s, e) => QueueAccountChange("reponse", iTalk_TextBox_Small7.Text);
            iTalk_TextBox_Small9.TextChanged += (s, e) => QueueAccountChange("points", iTalk_TextBox_Small9.Text);
            iTalk_TextBox_Small10.TextChanged += (s, e) => QueueAccountChange("lastIp", iTalk_TextBox_Small10.Text);
        }

        private void ConfigureControls()
        {
            iTalk_TextBox_Small1.Enabled = false;
            iTalk_TextBox_Small2.ReadOnly = true;
            iTalk_TextBox_Small5.Enabled = false;
            iTalk_TextBox_Small11.Enabled = false;
            iTalk_Button_11.Enabled = false;
            iTalk_Button_21.Enabled = false;
        }

        private async void editeurcompte_Load(object sender, EventArgs e)
        {
            await LoadEditorAsync(InitializeForm.EMUSELECT);
        }

        private async Task LoadEditorAsync(string emu)
        {
            if (_disposed) return;

            await _loadSemaphore.WaitAsync();
            bool beganUpdate = false;
            try
            {
                allAccounts = await Task.Run(() => EmuManager.GetAllAccountPropertiesForEmulator(emu));
                if (_disposed) return;
                listBox1.BeginUpdate();
                beganUpdate = true;
                listBox1.Items.Clear();
                _accountList.Clear();
                foreach (var account in allAccounts)
                {
                    string accountName = GetAccountValue(account, "Username", "Account", "Name");
                    if (!string.IsNullOrEmpty(accountName))
                    {
                        listBox1.Items.Add(accountName);
                        _accountList.Add(accountName);
                    }
                }
                UpdateAccountCount();
                bool canEditKryone = CanEditAccounts;
                iTalk_Button_22.Enabled = canEditKryone;
                iTalk_Button_14.Enabled = canEditKryone;
                iTalk_Button_13.Enabled = canEditKryone;
                iTalk_Button_12.Enabled = canEditKryone;
                iTalk_Button_11.Enabled = false;
                iTalk_Button_21.Enabled = false;
                foreach (var control in new[] { iTalk_TextBox_Small3, iTalk_TextBox_Small4,
                    iTalk_TextBox_Small6, iTalk_TextBox_Small7, iTalk_TextBox_Small9, iTalk_TextBox_Small10 })
                    control.ReadOnly = !canEditKryone;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du chargement : {ex.Message}", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (beganUpdate && !_disposed) listBox1.EndUpdate();
                _loadSemaphore.Release();
            }
        }

        private async Task LoadAccountDetailsAsync()
        {
            if (_disposed) return;
            if (listBox1.SelectedItem == null)
            {
                _loadingFields = true;
                try
                {
                    foreach (var field in new[] { iTalk_TextBox_Small1, iTalk_TextBox_Small2,
                        iTalk_TextBox_Small3, iTalk_TextBox_Small4, iTalk_TextBox_Small5,
                        iTalk_TextBox_Small6, iTalk_TextBox_Small7, iTalk_TextBox_Small9,
                        iTalk_TextBox_Small10, iTalk_TextBox_Small11 })
                        field.Text = string.Empty;
                    iTalk_ComboBox1.Items.Clear();
                    iTalk_Label9.Text = "-";
                    iTalk_Button_11.Enabled = false;
                    iTalk_Button_21.Enabled = false;
                }
                finally { _loadingFields = false; }
                return;
            }

            try
            {
                string selectedAccount = listBox1.SelectedItem.ToString();
                var account = allAccounts.FirstOrDefault(item =>
                    string.Equals(GetAccountValue(item, "Username", "Account", "Name"),
                        selectedAccount, StringComparison.OrdinalIgnoreCase));
                if (account == null) return;

                UpdateAccountFields(account);
                if (EmulatorRegistry.Current.Supports(EmulatorFeature.Characters))
                {
                    UpdateConnectionStatus();
                    await LoadCharactersAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du chargement des détails : {ex.Message}", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string GetAccountValue(Dictionary<string, object> account, params string[] propertyNames)
        {
            foreach (string name in propertyNames)
            {
                if (account.TryGetValue(name, out object value) && value != null)
                    return value.ToString();
            }
            return string.Empty;
        }

        private void UpdateAccountFields(Dictionary<string, object> info)
        {
            _loadingFields = true;
            try
            {
                iTalk_TextBox_Small1.Text = GetAccountValue(info, "Guid", "Id");
                iTalk_TextBox_Small2.Text = GetAccountValue(info, "Account", "Username", "Name");
                iTalk_TextBox_Small3.Text = GetAccountValue(info, "Pseudo", "Nickname");
                iTalk_TextBox_Small4.Text = GetAccountValue(info, "Pass", "Password");
                iTalk_TextBox_Small5.Text = GetAccountValue(info, "Banned", "IsBanned");
                iTalk_TextBox_Small6.Text = GetAccountValue(info, "Question", "SecretQuestion");
                iTalk_TextBox_Small7.Text = GetAccountValue(info, "Reponse", "SecretAnswer");
                iTalk_TextBox_Small9.Text = GetAccountValue(info, "Points");
                iTalk_TextBox_Small10.Text = GetAccountValue(info, "lastIp", "LastConnectionIp");
                iTalk_TextBox_Small11.Text = GetAccountValue(info, "Vip");

                if (uint.TryParse(GetAccountValue(info, "Guid", "Id"), out uint accountId))
                {
                    foreach (var field in new Dictionary<string, Control>
                    {
                        ["pseudo"] = iTalk_TextBox_Small3, ["pass"] = iTalk_TextBox_Small4,
                        ["question"] = iTalk_TextBox_Small6, ["reponse"] = iTalk_TextBox_Small7,
                        ["points"] = iTalk_TextBox_Small9, ["lastIp"] = iTalk_TextBox_Small10,
                        ["banned"] = iTalk_TextBox_Small5, ["vip"] = iTalk_TextBox_Small11
                    })
                        if (_pendingChanges.TryGetValue($"{accountId}|{field.Key}", out var pending))
                            field.Value.Text = pending.Value;
                }
            }
            finally
            {
                _loadingFields = false;
            }
        }

        private async Task LoadCharactersAsync()
        {
            if (!int.TryParse(iTalk_TextBox_Small1.Text, out int accountId)) return;

            iTalk_ComboBox1.BeginUpdate();
            iTalk_ComboBox1.Items.Clear();

            try
            {
                var characters = await Task.Run(() => CharacterList.Informations(accountId));
                if (_disposed || !int.TryParse(iTalk_TextBox_Small1.Text, out int currentId) ||
                    currentId != accountId) return;
                foreach (var character in characters)
                {
                    iTalk_ComboBox1.Items.Add(character);
                }
                iTalk_Button_21.Enabled = iTalk_ComboBox1.Items.Count > 0;
            }
            finally
            {
                iTalk_ComboBox1.EndUpdate();
            }
        }

        private void UpdateConnectionStatus()
        {
            if (listBox1.SelectedItem == null ||
                !int.TryParse(iTalk_TextBox_Small1.Text, out int accountId)) return;

            var account = AccountList.Informations(listBox1.SelectedItem.ToString());
            bool isOffline = account != null && account.Guid == accountId && account.Logged == 0;
            iTalk_Label9.Text = account == null || account.Guid != accountId ? "Statut inconnu" :
                account.Logged == 0 ? "Non connecté" : account.Logged == 1 ? "Connecté" :
                "Statut inconnu (" + account.Logged + ")";
            iTalk_Label9.ForeColor = isOffline ? System.Drawing.Color.Red :
                account != null && account.Guid == accountId && account.Logged == 1 ? System.Drawing.Color.Green :
                System.Drawing.Color.DarkOrange;
            bool canModerate = CanEditAccounts && !_saving;
            iTalk_Button_11.Enabled = canModerate && isOffline;
            if (account == null) return;
            iTalk_Button_12.Text = account.Banned == 0 ? "Bannir compte" : "Débannir compte";
            iTalk_Button_13.Text = account.Vip == 0 ? "Rendre VIP" : "Retirer VIP";
        }

        private void QueueAccountChange(string field, string value)
        {
            if (_disposed || _loadingFields || _saving || !CanEditAccounts ||
                listBox1.SelectedItem == null ||
                !uint.TryParse(iTalk_TextBox_Small1.Text, out uint accountId)) return;

            string key = $"{accountId}|{field}";
            var cachedAccount = allAccounts.FirstOrDefault(item =>
                GetAccountValue(item, "Guid", "Id") == accountId.ToString(CultureInfo.InvariantCulture));
            if (cachedAccount == null) return;
            string original = _pendingChanges.TryGetValue(key, out var existing) ?
                existing.OriginalValue : GetAccountValue(cachedAccount, field == "lastIp" ? "lastIp" :
                    char.ToUpperInvariant(field[0]) + field.Substring(1));
            if (string.Equals(value, original, StringComparison.Ordinal))
                _pendingChanges.TryRemove(key, out _);
            else
            {
                var change = new PendingAccountChange
                { AccountId = accountId, Field = field, Value = value, OriginalValue = original };
                _pendingChanges.AddOrUpdate(key, change, (changeKey, oldValue) =>
                    new PendingAccountChange { AccountId = accountId, Field = field,
                        Value = value, OriginalValue = oldValue.OriginalValue });
            }
            UpdateNotificationCount();
        }

        private void ToggleAccountFlag(string field, Control control)
        {
            if (_saving || listBox1.SelectedItem == null) return;
            if (!int.TryParse(control.Text, out int currentValue)) return;
            control.Text = currentValue == 0 ? "1" : "0";
            QueueAccountChange(field, control.Text);
            if (field == "banned")
                iTalk_Button_12.Text = control.Text == "0" ? "Bannir compte" : "Débannir compte";
            if (field == "vip")
                iTalk_Button_13.Text = control.Text == "0" ? "Rendre VIP" : "Retirer VIP";
        }

        private void OpenSelectedCharacter()
        {
            if (iTalk_ComboBox1.SelectedItem == null) return;
            var editor = new editeur_perso.editeur_perso(iTalk_ComboBox1.SelectedItem.ToString());
            editor.Show(this);
        }

        private async Task DeleteSelectedAccountAsync()
        {
            if (_disposed || _saving || listBox1.SelectedItem == null ||
                !uint.TryParse(iTalk_TextBox_Small1.Text, out uint accountId)) return;
            string accountName = listBox1.SelectedItem.ToString();
            var account = AccountList.AllAccount.Values.FirstOrDefault(value => value.Guid == accountId);
            if (account != null && account.Logged != 0)
            {
                MessageBox.Show("Déconnectez le compte avant de le supprimer.", "Compte",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            int characterCount = CharacterList.PersoAll.Values.Count(value => value.Account == accountId);
            var confirmation = MessageBox.Show(
                $"Supprimer définitivement le compte {accountName}, ses {characterCount} personnage(s) " +
                "et leurs objets ? Cette action est irréversible.",
                "Confirmation de suppression", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.Yes) return;

            _saving = true;
            await _updateSemaphore.WaitAsync();
            try
            {
                iTalk_Button_11.Enabled = false;
                iTalk_Button_22.Enabled = false;
                var result = await Task.Run(() => KryoneModerationService.DeleteAccount(accountId, accountName));
                RemoveDeletedAccountFromCache(result);
                await LoadEditorAsync(InitializeForm.EMUSELECT);
                MessageBox.Show($"Le compte {accountName} et ses données de personnage ont été supprimés.",
                    "Compte supprimé", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Aucune donnée n'a été supprimée : {ex.Message}", "Suppression impossible",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _saving = false;
                _updateSemaphore.Release();
                if (!_disposed)
                    iTalk_Button_22.Enabled = CanEditAccounts;
            }
        }

        private void RemoveDeletedAccountFromCache(AccountDeletionResult result)
        {
            foreach (string key in _pendingChanges.Keys.Where(key =>
                key.StartsWith(result.AccountId + "|", StringComparison.Ordinal)).ToArray())
                _pendingChanges.TryRemove(key, out _);
            AccountList.AllAccount.Remove(result.AccountName);
            if (result.AccountId <= int.MaxValue)
                CharacterList.IdCompte.Remove(Convert.ToInt32(result.AccountId));
            foreach (int id in result.CharacterIds) CharacterList.IdAccount.Remove(id);
            foreach (string name in result.CharacterNames) CharacterList.PersoAll.Remove(name);
            foreach (int id in result.ItemIds)
            {
                ItemList.ItemsList.Remove(id);
                ItemList.ItemsId.Remove(id);
            }
            AccountList.AccountListCount = AccountList.AllAccount.Count;
            CharacterList.PersoCount = CharacterList.PersoAll.Count;
            UpdateNotificationCount();
        }

        private void HandleSearchTextChanged(object sender, EventArgs e)
        {
            if (_disposed) return;

            listBox1.BeginUpdate();
            try
            {
                listBox1.Items.Clear();
                string searchText = textBox1.Text.Trim();

                if (string.IsNullOrEmpty(searchText))
                {
                    foreach (string account in _accountList)
                    {
                        listBox1.Items.Add(account);
                    }
                }
                else
                {
                    IEnumerable<string> filteredAccounts = _accountList.Where(a => 
                        a.StartsWith(searchText, StringComparison.OrdinalIgnoreCase));
                    foreach (string account in filteredAccounts)
                    {
                        listBox1.Items.Add(account);
                    }
                }

                UpdateAccountCount();
            }
            finally
            {
                listBox1.EndUpdate();
            }
        }

        private void UpdateAccountCount()
        {
            iTalk_Label14.Text = string.Format("{0} comptes chargés.", listBox1.Items.Count);
        }

        private void UpdateNotificationCount()
        {
            iTalk_NotificationNumber1.Value = _pendingChanges.Count;
            if (editorLayout != null) editorLayout.Status.Text = _pendingChanges.Count + " modification(s) en attente · " + listBox1.Items.Count + " compte(s) affiché(s).";
        }

        private async Task SaveChangesAsync()
        {
            if (_disposed || _pendingChanges.IsEmpty || _saving) return;
            _saving = true;
            await _updateSemaphore.WaitAsync();
            var editableControls = new[] { iTalk_TextBox_Small3, iTalk_TextBox_Small4,
                iTalk_TextBox_Small6, iTalk_TextBox_Small7, iTalk_TextBox_Small9, iTalk_TextBox_Small10 };
            try
            {
                var changes = _pendingChanges.ToArray();
                string table = TableCompte;
                if (!Tools_protocol.Query.QueryBuilder.IsIdentifier(table))
                    throw new InvalidOperationException("Le nom de la table des comptes est invalide.");

                foreach (var pair in changes)
                {
                    if (pair.Value.Field == "points" &&
                        (!int.TryParse(pair.Value.Value, out int points) || points < 0))
                        throw new FormatException("Les points doivent être un entier positif ou nul.");
                }

                iTalk_Button_22.Enabled = false;
                foreach (var control in editableControls) control.Enabled = false;
                await Task.Run(() => ApplyAccountChanges(table, changes));

                foreach (var pair in changes)
                {
                    ((ICollection<KeyValuePair<string, PendingAccountChange>>)_pendingChanges).Remove(pair);
                    UpdateCachedAccount(pair.Value);
                }
                UpdateNotificationCount();
                MessageBox.Show("Modifications enregistrées.", "Compte", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Aucune modification n'a été enregistrée : {ex.Message}",
                    "Erreur SQL", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _saving = false;
                if (!_disposed)
                {
                    iTalk_Button_22.Enabled = CanEditAccounts;
                    foreach (var control in editableControls) control.Enabled = true;
                    UpdateConnectionStatus();
                }
                _updateSemaphore.Release();
            }
        }

        private static void ApplyAccountChanges(string table, KeyValuePair<string, PendingAccountChange>[] changes)
        {
            ServerSql.Require(EmulatorFeature.AccountEditing);
            // La base des comptes est celle que le profil leur attribue (auth pour Kryone et StarLoco).
            string connectionString = EmulatorRegistry.ConnectionFor("comptes");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("La connexion auth doit être active.");
            if (!Tools_protocol.Query.QueryBuilder.IsIdentifier(table))
                throw new InvalidOperationException("Le nom de la table des comptes est invalide.");
            var settings = new MySqlConnectionStringBuilder(connectionString);
            if (!Tools_protocol.Query.QueryBuilder.IsIdentifier(settings.Database))
                throw new InvalidOperationException("Le nom de la base auth est invalide.");
            if (changes == null) throw new ArgumentNullException(nameof(changes));
            var allowedFields = new HashSet<string>(StringComparer.Ordinal)
            { "pseudo", "pass", "question", "reponse", "points", "lastIp", "banned", "vip" };
            foreach (var pair in changes)
            {
                var change = pair.Value;
                if (change == null || change.AccountId == 0 || change.Field == null ||
                    !allowedFields.Contains(change.Field))
                    throw new InvalidOperationException("Champ ou identifiant de compte non autorisé.");
                if (change.OriginalValue == null)
                    throw new InvalidOperationException("La valeur d'origine du compte est inconnue. Rechargez les données.");
                if (change.Value == null)
                    throw new InvalidOperationException("Une valeur de compte ne peut pas être nulle.");
                AccountChangeValue(change.Field, change.Value);
                AccountChangeValue(change.Field, change.OriginalValue);
            }
            var accountGroups = changes.Select(pair => pair.Value).GroupBy(change => change.AccountId)
                .OrderBy(group => group.Key).ToArray();
            if (accountGroups.Any(group => group.Select(change => change.Field).Distinct().Count() != group.Count()))
                throw new InvalidOperationException("Un champ de compte est présent plusieurs fois dans les modifications.");
            if (changes.Length == 0) return;
            string accounts = $"`{settings.Database}`.`{table}`";
            using (var connection = new MySqlConnection(settings.ConnectionString))
            {
                connection.Open();
                ServerSql.RequireStrictWrites(connection);
                using (var engineCommand = new MySqlCommand(
                    "SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=@schema AND TABLE_NAME=@table",
                    connection))
                {
                    engineCommand.Parameters.AddWithValue("@schema", settings.Database);
                    engineCommand.Parameters.AddWithValue("@table", table);
                    string engine = Convert.ToString(engineCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
                    if (!string.Equals(engine, "InnoDB", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("L'enregistrement des comptes exige une table InnoDB " +
                            "(actuel : " + (string.IsNullOrEmpty(engine) ? "absente" : engine) + ").");
                }
                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    foreach (var change in changes.Select(pair => pair.Value))
                        if (change.Field != "points" && change.Field != "banned" && change.Field != "vip")
                            ServerSql.RequireTextLength(connection, transaction, table, change.Field, change.Value);
                    foreach (var group in accountGroups)
                    {
                        string fields = string.Join(",", group.Select(change => "`" + change.Field + "`"));
                        using (var command = new MySqlCommand(
                            $"SELECT `logged`,{fields} FROM {accounts} WHERE `guid`=@accountId FOR UPDATE",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue("@accountId", group.Key);
                            using (var reader = command.ExecuteReader())
                            {
                                if (!reader.Read())
                                    throw new InvalidOperationException("Le compte n'existe plus dans la base.");
                                ServerSql.RequireOffline(reader["logged"], "le compte");
                                foreach (var change in group)
                                {
                                    if (reader[change.Field] == DBNull.Value ||
                                        !Equals(AccountChangeValue(change.Field, Convert.ToString(reader[change.Field],
                                            CultureInfo.InvariantCulture)), AccountChangeValue(change.Field, change.OriginalValue)))
                                        throw new InvalidOperationException("Un champ du compte a changé depuis son chargement. " +
                                            "Rechargez les données avant de réessayer ; vos saisies restent en attente.");
                                }
                            }
                        }
                    }
                    foreach (var group in accountGroups)
                    {
                        var modified = group.Where(change => !Equals(AccountChangeValue(change.Field, change.Value),
                            AccountChangeValue(change.Field, change.OriginalValue))).ToArray();
                        if (modified.Length == 0) continue;
                        string assignments = string.Join(",", modified.Select((change, index) =>
                            "`" + change.Field + "`=@value" + index));
                        using (var command = new MySqlCommand(
                            $"UPDATE {accounts} SET {assignments} WHERE `guid`=@accountId AND `logged`=0",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue("@accountId", group.Key);
                            for (int index = 0; index < modified.Length; index++)
                                command.Parameters.AddWithValue("@value" + index,
                                    AccountChangeValue(modified[index].Field, modified[index].Value));
                            if (command.ExecuteNonQuery() != 1)
                                throw new InvalidOperationException("L'état du compte a changé. Rechargez les données.");
                        }
                    }
                    transaction.Commit();
                }
            }
        }

        private static object AccountChangeValue(string field, string value)
        {
            if (field != "points" && field != "banned" && field != "vip") return value;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ||
                (field == "points" ? number < 0 : number != 0 && number != 1))
                throw new FormatException(field == "points" ?
                    "Les points doivent être un entier positif ou nul." :
                    "Les indicateurs VIP et bannissement doivent valoir 0 ou 1.");
            return number;
        }

        private void UpdateCachedAccount(PendingAccountChange change)
        {
            var account = AccountList.AllAccount.Values.FirstOrDefault(item => item.Guid == change.AccountId);
            if (account == null) return;
            switch (change.Field)
            {
                case "pseudo": account.Pseudo = change.Value; break;
                case "pass": account.Pass = change.Value; break;
                case "question": account.Question = change.Value; break;
                case "reponse": account.Reponse = change.Value; break;
                case "points": account.Points = int.Parse(change.Value); break;
                case "lastIp": account.lastIp = change.Value; break;
                case "banned": account.Banned = sbyte.Parse(change.Value); break;
                case "vip": account.Vip = int.Parse(change.Value); break;
            }
            var displayed = allAccounts.FirstOrDefault(item =>
                GetAccountValue(item, "Guid") == change.AccountId.ToString());
            if (displayed != null)
            {
                string property = change.Field == "lastIp" ? "lastIp" :
                    char.ToUpperInvariant(change.Field[0]) + change.Field.Substring(1);
                if (change.Field == "pass") property = "Pass";
                if (change.Field == "reponse") property = "Reponse";
                displayed[property] = change.Value;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource.Dispose();
                }
                _disposed = true;
            }
            base.Dispose(disposing);
        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

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
