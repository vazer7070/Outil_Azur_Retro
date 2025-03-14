using System;
using System.Windows.Forms;
using Tools_protocol.Kryone.Database;

namespace Outil_Azur_complet.editeur_compte
{
    public partial class CreateForm : Form
    {
        private const int MIN_USERNAME_LENGTH = 5;
        private const int MIN_PASSWORD_LENGTH = 8;
        private const int MIN_QUESTION_LENGTH = 6;
        private const int MIN_ANSWER_LENGTH = 6;

        private readonly ErrorProvider _errorProvider;
        private int _selectedHashMethod = -1;

        public CreateForm()
        {
            InitializeComponent();
            _errorProvider = new ErrorProvider();
            InitializeControls();
        }

        private void InitializeControls()
        {
            radioButton1.CheckedChanged += HandleHashMethodChanged;
            radioButton2.CheckedChanged += HandleHashMethodChanged;
            radioButton3.CheckedChanged += HandleHashMethodChanged;

            iTalk_TextBox_Small1.TextChanged += ValidateInput;
            iTalk_TextBox_Small2.TextChanged += ValidateInput;
            iTalk_TextBox_Small3.TextChanged += ValidateInput;
            iTalk_TextBox_Small4.TextChanged += ValidateInput;
        }

        private void HandleHashMethodChanged(object sender, EventArgs e)
        {
            var radioButton = sender as RadioButton;
            if (radioButton?.Checked == true)
            {
                switch (radioButton.Name)
                {
                    case "radioButton1":
                        _selectedHashMethod = 0;
                        break;
                    case "radioButton2":
                        _selectedHashMethod = 1;
                        break;
                    case "radioButton3":
                        _selectedHashMethod = 2;
                        break;
                }
                ValidateInput(sender, e);
            }
        }

        private void ValidateInput(object sender, EventArgs e)
        {
            var control = sender as Control;
            if (control == null) return;

            string error = null;
            switch (control.Name)
            {
                case "iTalk_TextBox_Small1":
                    if (string.IsNullOrWhiteSpace(iTalk_TextBox_Small1.Text))
                        error = "Le nom d'utilisateur est requis";
                    else if (iTalk_TextBox_Small1.Text.Length < MIN_USERNAME_LENGTH)
                        error = $"Le nom d'utilisateur doit contenir au moins {MIN_USERNAME_LENGTH} caractères";
                    break;

                case "iTalk_TextBox_Small2":
                    if (string.IsNullOrWhiteSpace(iTalk_TextBox_Small2.Text))
                        error = "Le mot de passe est requis";
                    else if (iTalk_TextBox_Small2.Text.Length < MIN_PASSWORD_LENGTH)
                        error = $"Le mot de passe doit contenir au moins {MIN_PASSWORD_LENGTH} caractères";
                    break;

                case "iTalk_TextBox_Small3":
                    if (string.IsNullOrWhiteSpace(iTalk_TextBox_Small3.Text))
                        error = "La question secrète est requise";
                    else if (iTalk_TextBox_Small3.Text.Length < MIN_QUESTION_LENGTH)
                        error = $"La question secrète doit contenir au moins {MIN_QUESTION_LENGTH} caractères";
                    break;

                case "iTalk_TextBox_Small4":
                    if (string.IsNullOrWhiteSpace(iTalk_TextBox_Small4.Text))
                        error = "La réponse secrète est requise";
                    else if (iTalk_TextBox_Small4.Text.Length < MIN_ANSWER_LENGTH)
                        error = $"La réponse secrète doit contenir au moins {MIN_ANSWER_LENGTH} caractères";
                    break;
            }

            _errorProvider.SetError(control, error);
        }

        private bool ValidateForm()
        {
            if (_selectedHashMethod == -1)
            {
                ShowError("Vous devez sélectionner une méthode de hachage");
                return false;
            }

            if (string.IsNullOrWhiteSpace(iTalk_TextBox_Small1.Text) ||
                string.IsNullOrWhiteSpace(iTalk_TextBox_Small2.Text) ||
                string.IsNullOrWhiteSpace(iTalk_TextBox_Small3.Text) ||
                string.IsNullOrWhiteSpace(iTalk_TextBox_Small4.Text))
            {
                ShowError("Veuillez remplir tous les champs requis");
                return false;
            }

            if (iTalk_TextBox_Small1.Text.Length < MIN_USERNAME_LENGTH ||
                iTalk_TextBox_Small2.Text.Length < MIN_PASSWORD_LENGTH ||
                iTalk_TextBox_Small3.Text.Length < MIN_QUESTION_LENGTH ||
                iTalk_TextBox_Small4.Text.Length < MIN_ANSWER_LENGTH)
            {
                ShowError($"Longueurs minimales requises :\n" +
                         $"- Nom d'utilisateur : {MIN_USERNAME_LENGTH} caractères\n" +
                         $"- Mot de passe : {MIN_PASSWORD_LENGTH} caractères\n" +
                         $"- Question : {MIN_QUESTION_LENGTH} caractères\n" +
                         $"- Réponse : {MIN_ANSWER_LENGTH} caractères");
                return false;
            }

            if (AccountList.AllAccount.ContainsKey(iTalk_TextBox_Small1.Text))
            {
                ShowError("Ce nom d'utilisateur existe déjà");
                return false;
            }

            return true;
        }

        private void ShowError(string message)
        {
            MessageBox.Show(message, "Erreur de validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void ResetForm()
        {
            iTalk_TextBox_Small1.Text = string.Empty;
            iTalk_TextBox_Small2.Text = string.Empty;
            iTalk_TextBox_Small3.Text = string.Empty;
            iTalk_TextBox_Small4.Text = string.Empty;
            radioButton1.Checked = false;
            radioButton2.Checked = false;
            radioButton3.Checked = false;
            _selectedHashMethod = -1;
            _errorProvider.Clear();
        }

        private void iTalk_Button_21_Click(object sender, EventArgs e)
        {
            if (!ValidateForm()) return;

            try
            {
                AccountList.CreateAccount(
                    iTalk_TextBox_Small4.Text,
                    _selectedHashMethod,
                    iTalk_TextBox_Small3.Text,
                    iTalk_TextBox_Small2.Text,
                    iTalk_TextBox_Small1.Text
                );

                MessageBox.Show(
                    $"Le compte {iTalk_TextBox_Small1.Text} a été créé avec succès !",
                    "Création réussie",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                ResetForm();
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Erreur lors de la création du compte : {ex.Message}",
                    "Erreur",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _errorProvider?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
