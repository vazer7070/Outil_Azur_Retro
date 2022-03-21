using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_Editor.maps.data;
using Tool_Editor.maps.managers;
using Tools_protocol.Managers;
using Tools_protocol.Query;

namespace Outil_Azur_complet.maps
{
    public partial class MainEditeur : Form
    {
       

        public MainEditeur()
        {
            InitializeComponent();
        }
        public TilesData SelectedTile = null;
        public Tools T = Tools.Brush;
        public CellMode CellMod = CellMode.Null;

        public int Calque = 1;
        public bool SelectedFlip = false;
        public int SelectedRotate = 0;

        public int CellTrigger = 0;
        public int MapTrigger = 0;
        public int NbTrigger = 0;

        public string FolderSols = @".\ressources\maps\sols\";
        public string FolderObjets = @".\ressources\maps\objets\";
        public List<MapForm> OpenMap = new List<MapForm>();
        public int Mapcount;
        public MapForm MapSelected;

        public bool Show_Grid = false;
        public bool Show_CellID = false;
        public bool Show_Back = true;
        public bool Show_ground = true;
        public bool Show_calque1 = true;
        public bool Show_calque2 = true;
        public Image Bi = null;

        public Map EndFightMap;


        public enum Tools
        {
            Brush,
            Selector,
            CellMode
        }
        public enum CellMode
        {
            Null,
            UnWalkable,
            LoS,
            Path,
            Paddock,
            Fight1,
            Fight2
        }

        public void OpenNewMap(int x, int y)
        {

            MapForm NewMap = new MapForm();
            NewMap.MdiParent = this;
            MapSelected = NewMap;
            NewMap.W = x;
            NewMap.H = y;
            NewMap.Text = $"MapID: {Mapcount + 1}";
            Mapcount += 1;
            NewMap.Show();
            OpenMap.Add(NewMap);
            iTalk_NotificationNumber3.Value = MapSelected.W;
            iTalk_NotificationNumber2.Value = MapSelected.H;

        }
        private void petiteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenNewMap(15, 17);
        }
        public void AddTrigger(int mapid, int cellid, MapForm M)
        {
            if(CellTrigger == 0)
            {
                NbTrigger += 1;
                CellTrigger = cellid;
                MapTrigger = mapid;
                M.MyMap.Cells[cellid].Trigger = true;
                M.MyMap.Cells[cellid].TriggerName = $"{NbTrigger}D";
                M.DrawAll();
            }
            else
            {
                string table_cells = EmuManager.ReturnTable("cellule", EmuManager.EMUSELECTED);
                string sqlinsert = $"{QueryBuilder.InsertIntoQuery(table_cells, new string[] { "*" }, new string[] { MapTrigger.ToString(), CellTrigger.ToString(), "0", "1", $"{mapid},{cellid}", "-1" }, "")}\n" +
                    $"{QueryBuilder.MultiDeleteQuery(table_cells, new string[] { "MapID", "CellID", "ActionsArgs" }, new string[] { MapTrigger.ToString(), CellTrigger.ToString(), $"{mapid},{cellid}" })}";
                File.WriteAllText($@".\creations\{table_cells}.sql", $"{sqlinsert}");

                CellTrigger = 0;
                MapTrigger = 0;
                M.MyMap.Cells[cellid].Trigger = true;
                M.MyMap.Cells[cellid].TriggerName = $"{NbTrigger}A";
                M.DrawAll();

                DialogResult DR = MessageBox.Show("Les triggers ont été enregistrés, continuer.?", "Ajout des triggers", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if(DR == DialogResult.Yes)
                {
                    MessageBox.Show("Merci de cliquer sur la cellule initiale", "Ajout de triggers", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else if(DR == DialogResult.No)
                {
                    M.ModeTrigger = false;
                    T = Tools.Brush;
                    RefreshAllMap();
                }
                else
                {
                    M.ModeTrigger = false;
                    T = Tools.Brush;
                    RefreshAllMap();
                }
            }
        }
        public void AddEndFightAction(Map M, int cellid)
        {
            if(EndFightMap == null)
            {
                EndFightMap = M;
                MessageBox.Show("Merci de selectionner la cellule initiale", "Selection de la cellule de départ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                if(M.ID != EndFightMap.ID)
                {
                    MessageBox.Show($"La carte {EndFightMap.ID} téléportera le joueur vers la carte {M.ID} cellule: {cellid} \n" +
                        $"Vous pouvez gérer cette action dans le gestionnaire de carte.");
                    EndFightMap.NextRoom = M.ID;
                    EndFightMap.NextCell = cellid;
                    string endfight_table = EmuManager.ReturnTable("endfight", EmuManager.EMUSELECTED);
                    string groupmonster = EmuManager.ReturnTable("groupe_monstre", EmuManager.EMUSELECTED);
                    string query = $"{QueryBuilder.InsertIntoQuery(groupmonster, new string[] { "*" }, new string[] { $"{EndFightMap.ID}", "4", "0", $"{M.ID},{cellid}" }, "")} \n" +
                        $"{QueryBuilder.DeleteFromQuery(endfight_table, "map", $"={EndFightMap.ID}")}";
                    File.WriteAllText($@".\creations\donjons.sql", query);
                }
                else
                {
                    MessageBox.Show("La carte de départ et d'arrivée doivent être différentes", "Création impossible", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    return;
                }
                EndFightMap = M;
                MessageBox.Show("Veuillez selectionner la cellule suivante", "Placement de cellule", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        private void toolStripButton2_Click(object sender, EventArgs e)
        {
            SaveForm saveform = new SaveForm();
            saveform.ShowDialog();
        }

        private void toolStripButton4_Click(object sender, EventArgs e)
        {
            AboutForm about = new AboutForm();
            about.ShowDialog();
        }

        private void sWFToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog OFD = new OpenFileDialog();
            OFD.InitialDirectory = @".\swf\";
            OFD.Filter = "Fichiers maps (*.swf)|*.swf*;";
            OFD.Multiselect = true;
            if (OFD.ShowDialog() == DialogResult.OK)
            {

            }
        }
        public void GlobalLoading()
        {

            treeView1.Nodes.Add("Grounds");
            treeView1.Nodes[0].Text = "Exterieur";
            treeView1.Nodes.Add("Objects");
            treeView1.Nodes[1].Text = "Props";
            SearchManager.SearchBackground(@".\ressources\maps\backgrounds");
            SearchManager.SearchGrounds(FolderSols, treeView1.Nodes[0]);
            SearchManager.SearchObject(FolderObjets, treeView1.Nodes[1]);
            SearchManager.SearchZik(@".\ressources\maps\musics");
           
            foreach (string h in SearchManager.Song)
            {
                iTalk_ComboBox2.Items.Add(h);
            }
            afficherCalque1ToolStripMenuItem.Checked = true;
            afficherCalque2ToolStripMenuItem.Checked = true;
            afficherFondToolStripMenuItem.Checked = true;
            afficherSolToolStripMenuItem.Checked = true;

        }
        public Bitmap Image(string path)
        {
            return (Bitmap)System.Drawing.Image.FromFile(path);
               
        }

       

      
      

        private void MainEditeur_Load(object sender, EventArgs e)
        {
            GlobalLoading();
            GC.Collect();
        }

        private void treeView1_AfterSelect_1(object sender, TreeViewEventArgs e)
        {
            backgroundWorker1.RunWorkerAsync();
        }
        public void DrawMiniBack(bool D = false)
        {
            if (D)
            {
                pictureBox1.BackgroundImage = null;
            }
            else
            {
                pictureBox1.BackgroundImage = Bi;
            }
            
        }

        private void iTalk_Button_21_Click(object sender, EventArgs e)
        {
            if (MapSelected == null)
            {
                MessageBox.Show("Merci de créer ou d'ouvrir une map avant de selectionner un fond", "Application d'un fond impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            else
            {
                BG_Select B = new BG_Select();
                B.New(MapSelected);
                B.ShowDialog();
                if(B.I != null)
                    Bi = B.I;
                DrawMiniBack();

            }
        }
        private void iTalk_Button_22_Click(object sender, EventArgs e)
        {
            if (MapSelected == null)
            {
                return;
            }
            else
            {
                MapSelected.DrawBackground(null);
                DrawMiniBack(true);

            }
        }

        private void afficherGrilleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Show_Grid == false)
            {
                Show_Grid = true;
                afficherGrilleToolStripMenuItem.Checked = true;
            }
            else
            {
                Show_Grid = false;
                afficherGrilleToolStripMenuItem.Checked = false;
            }
            UpdateGrid(Show_Grid);
            RefreshAllMap();
        }
        private void UpdateGrid(bool grid)
        {
            foreach (MapForm m in OpenMap)
            {
                m.Show_Grid = grid;
            }
        }
    
        private void RefreshAllMap()
        {
            foreach (MapForm m in OpenMap)
            {
                m.DrawAll();
            }
        }
        private void UpdateCellIDShowed(bool showCell)
        {
            foreach (MapForm m in OpenMap)
            {
                m.Show_CellID = showCell;
            }
        }
    

        private void afficherCellIDToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Show_CellID == false)
            {
                Show_CellID = true;
                afficherCellIDToolStripMenuItem.Checked = true;
            }
            else
            {
                Show_CellID = false;
                afficherCellIDToolStripMenuItem.Checked = false;
            }
            UpdateCellIDShowed(Show_CellID);
            RefreshAllMap();
        }

        private void personnaliséeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenNewMap(19, 22);
        }

        private void taillePersonnaliséeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OtherSizeForm O = new OtherSizeForm();
            O.ShowDialog();
            if(O.IsOk)
                OpenNewMap(O.value1, O.value2 );
        }

        private void toolStripButton20_Click(object sender, EventArgs e)
        {

        }

        private void triggersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if(donjonsToolStripMenuItem.Checked == true)
            {
                donjonsToolStripMenuItem.Checked = false;
                MapSelected.EndFight = false;
            }
            if (triggersToolStripMenuItem.Checked == true)
            {
                triggersToolStripMenuItem.Checked = false;
                T = Tools.Brush;
                MapSelected.IsCellTool = false;
                MapSelected.ModeTrigger = false;
            }
            else
            {
                triggersToolStripMenuItem.Checked = true;
                MessageBox.Show("Selectionnez les cellules qui deviendront les triggers", "Ajout de triggers", MessageBoxButtons.OK, MessageBoxIcon.Information);
                T = Tools.CellMode;
                MapSelected.IsCellTool = true;
                MapSelected.ModeTrigger = true;
            }
            RefreshAllMap();
        }

        private void donjonsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if(Mapcount >= 2)
            {
                if (triggersToolStripMenuItem.Checked == true)
                {
                    triggersToolStripMenuItem.Checked = false;
                    MapSelected.ModeTrigger = false;
                }
                if (donjonsToolStripMenuItem.Checked == true)
                {
                    donjonsToolStripMenuItem.Checked = false;
                    T = Tools.Brush;
                    Donjonmode(false, false);
                }
                else
                {
                    donjonsToolStripMenuItem.Checked = true;
                    MessageBox.Show("Merci de cliquer sur la carte initiale", "Selection de la carte initale EndfightAction", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    T = Tools.CellMode;
                    Donjonmode(true, true);
                }
               
                RefreshAllMap();
            }
            else
            {
                MessageBox.Show("Il vous faut avoir plus d'une carte ouverte pour créer un donjon.", "Impossible de créer un donjon", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }
        private void Donjonmode(bool iscelltool, bool endfight)
        {

            foreach (MapForm m in OpenMap)
            {
                m.IsCellTool = iscelltool;
                m.EndFight = endfight;
            }

        }  
            private void afficherFondToolStripMenuItem_Click(object sender, EventArgs e)
        {
            
            if(afficherFondToolStripMenuItem.Checked == true)
            {
                afficherFondToolStripMenuItem.Checked = false;
                Show_Back = false;
            }
            else
            {
                afficherFondToolStripMenuItem.Checked = true;
                Show_Back = true;
            }
            UpdateShowBack(Show_Back);
            RefreshAllMap();

        }
        private void UpdateShowBack(bool showback)
        {
            foreach (MapForm m in OpenMap)
            {
                m.Show_Back = showback;
            }
        
        }
        private void afficherSolToolStripMenuItem_Click(object sender, EventArgs e)
        { 
            if(afficherSolToolStripMenuItem.Checked == true)
            {
                afficherSolToolStripMenuItem.Checked = false;
                Show_ground = false;
            }
            else
            {
                afficherSolToolStripMenuItem.Checked = true;
                Show_ground = true;
            }
            ShowGround(Show_ground);
            RefreshAllMap();

        }
        private void ShowGround(bool showground)
        {
            foreach (MapForm m in OpenMap)
            {
                m.Show_ground = showground;
            }
        
        }
        private void afficherCalque1ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if(afficherCalque1ToolStripMenuItem.Checked == true)
            {
                afficherCalque1ToolStripMenuItem.Checked = false;
                Show_calque1 = false;
            }
            else
            {
                afficherCalque1ToolStripMenuItem.Checked = true;
                Show_calque1 = true;
            }
            UpdateCalque1(Show_calque1);
            RefreshAllMap();
        }
        private void UpdateCalque1(bool calque1)
        {
            foreach (MapForm m in OpenMap)
            {
                m.Show_calque1 = calque1;
            }
        }
        private void UpdateCalque2(bool calque2)
        {
            foreach (MapForm m in OpenMap)
            {
                m.Show_calque2 = calque2;
            }
        }
            private void afficherCalque2ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (afficherCalque2ToolStripMenuItem.Checked == true)
            {
                afficherCalque2ToolStripMenuItem.Checked = false;
                Show_calque2 = false;
            }
            else
            {
                afficherCalque2ToolStripMenuItem.Checked = true;
                Show_calque2 = true;
            }
            UpdateCalque2(Show_calque2);
            RefreshAllMap();
        }

        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {
           
            if (listView1.SelectedItems.Count == 0 || listView1.SelectedItems == null)
                return;
            if (treeView1.SelectedNode.FullPath.Contains("sols"))
            {
                int id = Convert.ToInt32(listView1.SelectedItems[0].Text);
                SelectedTile = TilesData.ListGrounds[id];
                TilesData.SelectedTiles = SelectedTile;
                //MessageBox.Show(SelectedTile.ID.ToString());

            }
            else if (treeView1.SelectedNode.FullPath.Contains("objets"))
            {
                int id = Convert.ToInt32(listView1.SelectedItems[0].Text);
                SelectedTile = TilesData.ListObject[id];
            }
            
            if(MapSelected != null)
            {
                T = Tools.Brush;
                MapSelected.IsCellTool = false;
                MapSelected.IsBrushTool = true;
                CellMod = CellMode.Null;
                RefreshAllMap();
            }
        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {

        }

        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            ImageList IL = new ImageList();
            IL.ImageSize = new Size(30, 30);
            IL.ColorDepth = ColorDepth.Depth32Bit;
            string TVT = treeView1.SelectedNode.FullPath;
            int count = TVT.Split('\\').Count();
            listView1.Items.Clear();

            if (TVT.Contains("sols") && count == 3)
            {
                DirectoryInfo dir = new DirectoryInfo(FolderSols + TVT.Split('\\')[2]);
                foreach (FileInfo file in dir.GetFiles())
                {
                    int id = Convert.ToInt32(file.Name.Split('.')[0]);
                    try
                    {
                        IL.Images.Add(Image(file.FullName));
                        TilesData.ListGrounds[id] = new TilesData(id, file.FullName, file.DirectoryName.Split('\\')[9], TilesData.TileType.ground);
                        
                    }
                    catch
                    {
                        continue;
                    }
                }
                listView1.View = View.LargeIcon;
                IL.ImageSize = new Size(32, 32);
                listView1.LargeImageList = IL;
                int i = 0;
                foreach(TilesData T in TilesData.ListGrounds)
                {
                    if(T != null)
                    {
                        if(T.Folder == treeView1.SelectedNode.Text)
                        {
                           
                            ListViewItem item = new ListViewItem();
                            listView1.Items.Add(T.ID.ToString(), i);
                            i += 1;
                        }
                    }
                }


            }
            else if (TVT.Contains("objets") && count == 3)
            {
                DirectoryInfo dir = new DirectoryInfo(FolderObjets + TVT.Split('\\')[2]);
                foreach (FileInfo file in dir.GetFiles())
                {
                    try
                    {
                        int id = Convert.ToInt32(file.Name.Split('.')[0]);
                        IL.Images.Add(Image(file.FullName));
                        TilesData.ListObject[id] = new TilesData(id, file.FullName, file.DirectoryName.Split('\\')[9], TilesData.TileType.objet);
                    }
                    catch
                    {
                        continue;
                    }
                }
                listView1.View = View.LargeIcon;
                IL.ImageSize = new Size(32, 32);
                listView1.LargeImageList = IL;
                int i = 0;
                foreach (TilesData T in TilesData.ListObject)
                {
                    if (T != null)
                    {
                        if (T.Folder == treeView1.SelectedNode.Text)
                        {
                            //MessageBox.Show(T.ID.ToString());
                            ListViewItem item = new ListViewItem();
                            item.ImageIndex = i;
                            item.Text = T.ID.ToString();
                            listView1.Items.Add(item);
                            i += 1;
                        }
                    }
                }

            }
        }

        private void afficherFightCellsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if(afficherFightCellsToolStripMenuItem.Checked == true)
            {
                afficherFightCellsToolStripMenuItem.Checked = false;
            }
            else
            {
                afficherFightCellsToolStripMenuItem.Checked = true;
            }
        }

        private void toolStripButton5_Click(object sender, EventArgs e)
        {
           if(toolStripButton5.Checked == true)
            {
                toolStripButton5.Checked = false;

            }else if(toolStripButton5.Checked == false)
            {
                toolStripButton5.Checked = true;
                CellMod = CellMode.Null;
                T = Tools.Brush;
                RefreshAllMap();
            }
        }

        private void iTalk_Label8_Click(object sender, EventArgs e)
        {

        }

        private void toolStripDropDownButton1_Click(object sender, EventArgs e)
        {

        }

        private void xpTaskBarBox1_ItemClick(object sender, Syncfusion.Windows.Forms.Tools.XPTaskBarItemClickArgs e)
        {

        }

        private void iTalk_Label7_Click(object sender, EventArgs e)
        {

        }

        private void tabPageAdv2_Click(object sender, EventArgs e)
        {

        }

        
    }
}
