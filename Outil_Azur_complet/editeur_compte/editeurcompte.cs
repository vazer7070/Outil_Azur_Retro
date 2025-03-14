using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
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

namespace Outil_Azur_complet.editeur_compte
{
    public partial class editeurcompte : Form, IDisposable
    {
        private readonly ConcurrentDictionary<string, string> _modifiedQueries;
        private readonly HashSet<string> _accountList;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly object _lockObject = new object();
        private bool _disposed;

        private static readonly SemaphoreSlim _loadSemaphore = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim _updateSemaphore = new SemaphoreSlim(1, 1);

        public string TableCompte => EmuManager.ReturnTable("comptes", InitializeForm.EMUSELECT);
        public string TablePerso => EmuManager.ReturnTable("perso", InitializeForm.EMUSELECT);

        public editeurcompte()
        {
            InitializeComponent();
            
            _modifiedQueries = new ConcurrentDictionary<string, string>();
            _accountList = new HashSet<string>();
            _cancellationTokenSource = new CancellationTokenSource();

            InitializeEventHandlers();
            ConfigureControls();
        }

        private void InitializeEventHandlers()
        {
            FormClosing += (s, e) => _cancellationTokenSource.Cancel();
            listBox1.SelectedIndexChanged += async (s, e) => await LoadAccountDetailsAsync();
            textBox1.TextChanged += HandleSearchTextChanged;
        }

        private void ConfigureControls()
        {
            iTalk_TextBox_Small1.Enabled = false;
            iTalk_TextBox_Small5.Enabled = false;
            iTalk_TextBox_Small11.Enabled = false;
        }

        private async void editeurcompte_Load(object sender, EventArgs e)
        {
            await LoadEditorAsync(InitializeForm.EMUSELECT);
        }

        private async Task LoadEditorAsync(string emu)
        {
            if (_disposed) return;

            try
            {
                await _loadSemaphore.WaitAsync();
                
                listBox1.BeginUpdate();
                listBox1.Items.Clear();
                _accountList.Clear();

                await Task.Run(() =>
                {
                    if (emu.Equals("Kryone"))
                    {
                        foreach (string account in AccountList.AllAccount.Keys)
                        {
                            this.InvokeIfRequired(() => listBox1.Items.Add(account));
                            _accountList.Add(account);
                        }
                    }
                    else if (emu.Equals("Codebreak"))
                    {
                        AccountListi.AccountsName.Clear();
                        AccountListi.LoadAccounts();
                        foreach (string account in AccountListi.AccountsName)
                        {
                            this.InvokeIfRequired(() => listBox1.Items.Add(account));
                            _accountList.Add(account);
                        }
                    }
                });

                UpdateAccountCount();
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

        private async Task LoadAccountDetailsAsync()
        {
            if (_disposed || listBox1.SelectedItem == null) return;

            try
            {
                var selectedAccount = listBox1.SelectedItem.ToString();
                await Task.Run(() =>
                {
                    var accountInfo = new string[10];
                    for (int i = 1; i <= 10; i++)
                    {
                        accountInfo[i-1] = EmuManager.ReturnAccountsInfo(InitializeForm.EMUSELECT, selectedAccount, i);
                    }

                    this.InvokeIfRequired(() => UpdateAccountFields(accountInfo));
                });

                await LoadCharactersAsync();
                UpdateConnectionStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du chargement des détails : {ex.Message}", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateAccountFields(string[] info)
        {
            if (info == null || info.Length != 10) return;

            iTalk_TextBox_Small1.Text = info[0];
            iTalk_TextBox_Small2.Text = info[1];
            iTalk_TextBox_Small3.Text = info[2];
            iTalk_TextBox_Small4.Text = info[3];
            iTalk_TextBox_Small5.Text = info[4];
            iTalk_TextBox_Small6.Text = info[5];
            iTalk_TextBox_Small7.Text = info[6];
            iTalk_TextBox_Small9.Text = info[7];
            iTalk_TextBox_Small10.Text = info[8];
            iTalk_TextBox_Small11.Text = info[9];
        }

        private async Task LoadCharactersAsync()
        {
            if (!int.TryParse(iTalk_TextBox_Small1.Text, out int accountId)) return;

            iTalk_ComboBox1.BeginUpdate();
            iTalk_ComboBox1.Items.Clear();

            try
            {
                var characters = await Task.Run(() => CharacterList.Informations(accountId));
                foreach (var character in characters)
                {
                    iTalk_ComboBox1.Items.Add(character);
                }
            }
            finally
            {
                iTalk_ComboBox1.EndUpdate();
            }
        }

        private void UpdateConnectionStatus()
        {
            if (!int.TryParse(iTalk_TextBox_Small1.Text, out int accountId)) return;

            var isConnected = AccountList.Informations(listBox1.SelectedItem.ToString()).Logged == 1;
            iTalk_Label9.Text = isConnected ? "Connecté" : "Non connecté";
            iTalk_Label9.ForeColor = isConnected ? System.Drawing.Color.Green : System.Drawing.Color.Red;
        }

        private async Task UpdateAccountAsync(string field, string value, string controlKey)
        {
            if (_disposed || string.IsNullOrEmpty(iTalk_TextBox_Small1.Text)) return;

            try
            {
                await _updateSemaphore.WaitAsync();

                var accountId = iTalk_TextBox_Small1.Text;
                var query = QueryBuilder.UpdateFromQuery(TableCompte, field, 1, value, "guid", accountId);

                _modifiedQueries.AddOrUpdate(
                    $"{controlKey}{accountId}",
                    query,
                    (key, oldValue) => query
                );

                UpdateNotificationCount();
            }
            finally
            {
                _updateSemaphore.Release();
            }
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

        private void UpdateNotificationCount(bool reset = false)
        {
            if (reset)
            {
                iTalk_NotificationNumber1.Value = 0;
            }
            else
            {
                iTalk_NotificationNumber1.Value++;
            }
        }

        private async Task SaveChangesAsync()
        {
            if (_disposed) return;

            try
            {
                await _updateSemaphore.WaitAsync();

                foreach (string query in _modifiedQueries.Values)
                {
                    await Task.Run(() => DatabaseManager.UpdateQuery(query));
                }

                _modifiedQueries.Clear();
                UpdateNotificationCount(true);
            }
            finally
            {
                _updateSemaphore.Release();
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
                    _loadSemaphore.Dispose();
                    _updateSemaphore.Dispose();
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
