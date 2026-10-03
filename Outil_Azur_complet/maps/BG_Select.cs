using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_Editor.maps.data;
using Tool_Editor.maps.managers;
using Tools_protocol.Kryone.Database;
using SearchManager = Tool_Editor.maps.managers.SearchManager;

namespace Outil_Azur_complet.maps
{
    public partial class BG_Select : Form
    {
        MapForm MF;
        public Image I;
        public BG_Select()
        {
            InitializeComponent();
            BuildEditorLayout();
            Disposed+=(s,e)=>IM.Dispose();
        }
        public ImageList IM = new ImageList();


        private void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            Dispose();
        }
        public void New(MapForm Formparent)
        {
            MF = Formparent;
        }
        private void BG_Select_Load(object sender, EventArgs e)
        {
            TopMost = true;

            IM.ImageSize = new Size(166, 100);
            IM.ColorDepth = ColorDepth.Depth32Bit;
            IntPtr imageHandle=IM.Handle;

            int i = 0;
            foreach (TilesData T in TilesData.Backgrounds_Tiles)
            {
                if (T != null)
                {
                    iTalk_Listview1.Items.Add(T.ID.ToString(), i);
                    using(var image=T.Image())IM.Images.Add(image);
                    i += 1;
                }
            }
            iTalk_Listview1.LargeImageList = IM;
            iTalk_Listview1.Refresh();
        }

        private void iTalk_Button_21_Click(object sender, EventArgs e)
        {
            if (MF == null || iTalk_Listview1.SelectedItems.Count == 0)
                return;
            MF.DrawBackground(TilesData.Backgrounds_Tiles[Convert.ToInt32(iTalk_Listview1.SelectedItems[0].Text)]);
            I = TilesData.Backgrounds_Tiles[Convert.ToInt32(iTalk_Listview1.SelectedItems[0].Text)].Image();
            iTalk_Listview1.LargeImageList.Dispose();
            iTalk_Listview1.Dispose();
            Dispose();
        }
    }
}
