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
        public OtherSizeForm()
        {
            InitializeComponent();
        }
        public int value1;
        public int value2;
        public bool IsOk = false;

        private void iTalk_Button_21_Click(object sender, EventArgs e)
        {
            string x = iTalk_TextBox_Small1.Text;
            string y = iTalk_TextBox_Small2.Text;
            if(!string.IsNullOrWhiteSpace(x) || !string.IsNullOrWhiteSpace(y))
            {
                bool B_x = int.TryParse(x, out int I_x);
                bool B_y = int.TryParse(y, out int I_y);
                if(B_x && B_y)
                {
                    value1 = I_x;
                    value2 = I_y;
                    IsOk = true;
                    Close();
                }
                else
                {
                    MessageBox.Show("Les valeurs renseignées ne sont pas valides, merci de vérifier.", "Valeurs incorrectes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            else
            {
                MessageBox.Show("Merci de bien remplir les informations.!", "Valeurs incorrectes", MessageBoxButtons.OK,MessageBoxIcon.Exclamation);
                return;
            }
        }

        private void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            Hide();
        }
    }
}
