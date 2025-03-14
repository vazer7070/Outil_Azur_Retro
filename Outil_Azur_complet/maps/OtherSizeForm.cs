using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Outil_Azur_complet.maps
{
    public partial class OtherSizeForm : Form
    {
        private const int MIN_SIZE = 1;
        private const int MAX_SIZE = 100;
        private readonly ErrorProvider _errorProvider;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public bool IsValid { get; private set; }

        public OtherSizeForm()
        {
            InitializeComponent();
            
            _errorProvider = new ErrorProvider
            {
                BlinkStyle = ErrorBlinkStyle.AlwaysBlink
            };

            InitializeEventHandlers();
            ConfigureInputValidation();
        }

        private void InitializeEventHandlers()
        {
            iTalk_TextBox_Small1.TextChanged += ValidateInput;
            iTalk_TextBox_Small2.TextChanged += ValidateInput;
            
            iTalk_TextBox_Small1.KeyPress += NumberOnlyKeyPress;
            iTalk_TextBox_Small2.KeyPress += NumberOnlyKeyPress;
        }

        private void ConfigureInputValidation()
        {
            iTalk_TextBox_Small1.MaxLength = 3;
            iTalk_TextBox_Small2.MaxLength = 3;
        }

        private void ValidateInput(object sender, EventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            _errorProvider.SetError(textBox, string.Empty);

            if (string.IsNullOrWhiteSpace(textBox.Text))
            {
                _errorProvider.SetError(textBox, "Ce champ est requis");
                return;
            }

            if (!int.TryParse(textBox.Text, out int value))
            {
                _errorProvider.SetError(textBox, "Veuillez entrer un nombre valide");
                return;
            }

            if (value < MIN_SIZE || value > MAX_SIZE)
            {
                _errorProvider.SetError(textBox, $"La valeur doit être entre {MIN_SIZE} et {MAX_SIZE}");
            }
        }

        private void NumberOnlyKeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
            {
                e.Handled = true;
            }
        }

        private void ApplySize(object sender, EventArgs e)
        {
            if (ValidateForm())
            {
                Width = int.Parse(iTalk_TextBox_Small1.Text);
                Height = int.Parse(iTalk_TextBox_Small2.Text);
                IsValid = true;
                Close();
            }
        }

        private bool ValidateForm()
        {
            if (string.IsNullOrWhiteSpace(iTalk_TextBox_Small1.Text) || 
                string.IsNullOrWhiteSpace(iTalk_TextBox_Small2.Text))
            {
                ShowError("Merci de remplir tous les champs", "Champs manquants");
                return false;
            }

            if (!int.TryParse(iTalk_TextBox_Small1.Text, out int width) || 
                !int.TryParse(iTalk_TextBox_Small2.Text, out int height))
            {
                ShowError("Les valeurs doivent être des nombres entiers", "Format invalide");
                return false;
            }

            if (width < MIN_SIZE || width > MAX_SIZE || 
                height < MIN_SIZE || height > MAX_SIZE)
            {
                ShowError(
                    $"Les dimensions doivent être comprises entre {MIN_SIZE} et {MAX_SIZE}",
                    "Dimensions invalides"
                );
                return false;
            }

            return true;
        }

        private void ShowError(string message, string title)
        {
            MessageBox.Show(
                message,
                title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
        }

        private void CancelSize(object sender, EventArgs e)
        {
            IsValid = false;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _errorProvider?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
