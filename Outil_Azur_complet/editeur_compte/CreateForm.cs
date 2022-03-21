using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tools_protocol.Kryone.Database;

namespace Outil_Azur_complet.editeur_compte
{
    public partial class CreateForm : Form
    {
        int select;
        public CreateForm()
        {
            InitializeComponent();
        }

        private void iTalk_TextBox_Small1_TextChanged(object sender, EventArgs e)
        {

        }

        private void iTalk_Button_21_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(iTalk_TextBox_Small1.Text) || !String.IsNullOrWhiteSpace(iTalk_TextBox_Small2.Text) || !string.IsNullOrWhiteSpace(iTalk_TextBox_Small3.Text) || !string.IsNullOrWhiteSpace(iTalk_TextBox_Small4.Text))
            {
                if(iTalk_TextBox_Small1.Text.Length >= 5 || iTalk_TextBox_Small2.Text.Length >= 8 || iTalk_TextBox_Small3.Text.Length >= 6 || iTalk_TextBox_Small4.Text.Length >= 6)
                {
                    if(radioButton1.Checked.Equals(true) || radioButton2.Checked.Equals(true) || radioButton3.Checked.Equals(true))
                    {
                        if (!AccountList.Accounts.Contains(iTalk_TextBox_Small1.Text))
                        {
                            if (radioButton1.Checked.Equals(true))
                            {
                                select = 0;
                            }
                            else if (radioButton2.Checked.Equals(true))
                            {
                                select = 1;
                            }
                            else if (radioButton3.Checked.Equals(true))
                            {
                                select = 2;
                            }
                            try
                            {
                                AccountList.CreateAccount(iTalk_TextBox_Small4.Text, select, iTalk_TextBox_Small3.Text, iTalk_TextBox_Small2.Text, iTalk_TextBox_Small1.Text);
                                AccountList.Accounts.Add(iTalk_TextBox_Small1.Text);
                                MessageBox.Show($"Le compte {iTalk_TextBox_Small4.Text} a été crée avec succès.!", "Création du compte réussie", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                iTalk_TextBox_Small4.Text = "";
                                iTalk_TextBox_Small3.Text = "";
                                iTalk_TextBox_Small2.Text = "";
                                iTalk_TextBox_Small1.Text = "";
                                radioButton1.Checked = false;
                                radioButton2.Checked = false;
                                radioButton3.Checked = false;
                            }
                            catch(Exception u)
                            {
                                MessageBox.Show(u.Message, u.Source, MessageBoxButtons.OK, MessageBoxIcon.Error);
                                return;
                            }
                        }
                        else
                        {
                            MessageBox.Show("Le nom de compte voulu existe déjà, merci de le changer", "requête impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                    }
                    else
                    {
                        MessageBox.Show("Vous êtes obligé de choisir une méthode de hash.!", "requête impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                else
                {
                    MessageBox.Show("La longueur des paramètres n'est pas correcte, il faut au minimum:" + "\n" + "5 caractères pour le nom, 8 pour le mot de passe, 6 pour la question et 6 pour la réponse." + "\n" + "Veuillez vérifier vos informations.", "requête impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }


            }
            else
            {
                MessageBox.Show("Veuillez remplir correctement les informations demandées.", "requête impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
        }

        private void radioButton1_CheckedChanged(object sender, EventArgs e)
        {

        }
    }
}
