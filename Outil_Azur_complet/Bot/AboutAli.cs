using Syncfusion.Windows.Forms.Tools;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Accounts;

namespace Outil_Azur_complet.Bot
{
    public partial class AboutAli : Form
    {
        Accounts A;
        public AboutAli(Accounts Acc)
        {
            InitializeComponent();
            A = Acc;
        }

        private void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void AboutAli_Load(object sender, EventArgs e)
        {
            pictureBox1.Image = Image.FromFile($@".\ressources\Bot\UI\{A.Game.character.stats.Alignement}.png");
            iTalk_Label2.Text = $"{A.Game.character.stats.AlignLVL}/100";
        }
    }
}
