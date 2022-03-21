using AzurToolRetro.Forms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AzurToolRetro
{
    internal static class Program
    {
        /// <summary>
        /// Point d'entrée principal de l'application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense("NTU3Njg4QDMxMzkyZTM0MmUzMFlBY2pPdFBwVmUvdGtkRG81ZFJwUlFsVlA3aXV4djZIbk9BaUUyQzVmbFE9");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
