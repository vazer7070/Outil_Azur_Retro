using Microsoft.Web.Services3.Referral;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Tool_Editor.maps.data;

using Tool_Editor.maps.managers;

namespace Outil_Azur_complet.maps
{
    public partial class MapForm : Form
    {
        private MainEditeur Editor => MdiParent as MainEditeur;
        public int ID = 0;
        const int Sleep = 800;
        public static int SizeBaseCell = 26;
        public int W;
        public int H;

        public  Map MyMap;

        public Size PicSize = new Size();
        public Bitmap MyPic;
        public Bitmap Grid;
        public Graphics G;

        public int HoverCell = 1;
        public int SelectedCell = 1;
        public bool Edited = false;
        public bool Loaded = false;
        public bool ChangeSize = false;

        public int SizeCell = CellsData.SizeCell;
        public double PourceOfTile = CellsData.PourceTile;
        public bool Show_Grid = false;
        public bool Show_CellID = false;
        public bool Show_Back = true;
        public bool Show_ground = true;
        public bool Show_calque1 = true;
        public bool Show_calque2 = true;
        public bool ShowServerPlacements { get; private set; }
        public MapServerPlacementLayer ServerPlacements { get; private set; }
        private readonly ToolTip placementTip = new ToolTip();
        private string currentPlacementTip = "";

        public void SetServerPlacements(MapServerPlacementLayer layer)
        {
            if (layer == null || layer.MapId != ID) throw new ArgumentException("Les placements ne correspondent pas à cette carte.");
            ServerPlacements = layer;
            ShowServerPlacements = true;
            DrawAll();
        }

        public void HideServerPlacements()
        {
            ShowServerPlacements = false;
            currentPlacementTip = "";
            placementTip.SetToolTip(pictureBox1, "");
            DrawAll();
        }

        public void ClearServerPlacements()
        {
            ServerPlacements = null;
            HideServerPlacements();
        }

        public bool IsCellTool = false;
        public bool IsBrushTool = false;
        public bool ModeTrigger = false;
        public bool EndFight = false;
        private Map loadedMap;

        public void New(Map map = null)
        {

            if (map != null && !ReferenceEquals(map, loadedMap))
            {
                map.Load();
                MyMap = map;
                loadedMap = map;
            }
        }
        public MapForm()
        {
            InitializeComponent();
            AutoScaleMode=AutoScaleMode.None;
            BackColor=Editors.EditorUi.Background;
            pictureBox1.Dock=DockStyle.None;
            pictureBox1.BackColor=Color.FromArgb(247,249,252);
            AutoScroll=true;
        }

        private void MapForm_Load(object sender, EventArgs e)
        {
            if(Loaded){FitCanvas();return;}
            KeyPreview = true;
            if (MyMap == null)
            {
                MyMap = new Map();
                MyMap.Cells = new CellsData[Map.CellCount(W, H)];
                MyMap.Width = W;
                MyMap.Height = H;
            }
            New(MyMap);
            MyMap.IsEditing = true;
            if (Editor != null)
            {
                Show_Grid = Editor.Show_Grid;
                Show_CellID = Editor.Show_CellID;
                Show_Back = Editor.Show_Back;
                Show_ground = Editor.Show_ground;
                Show_calque1 = Editor.Show_calque1;
                Show_calque2 = Editor.Show_calque2;
                IsBrushTool = Editor.T == MainEditeur.Tools.Brush;
                IsCellTool = Editor.T == MainEditeur.Tools.CellMode;
            }
            PicSize = new Size(W * SizeCell * 2, H * SizeCell);
            Size = new Size(PicSize.Width + 16, PicSize.Height + 38);
            MyPic = new Bitmap(PicSize.Width, PicSize.Height);
            Grid = new Bitmap(PicSize.Width, PicSize.Height);
            G = Graphics.FromImage(MyPic);
            pictureBox1.Image = MyPic;
            CellsData.PourceTile = (double)SizeCell / SizeBaseCell;

            Map.AddMap(MyMap);

            bool initializeBorders = MyMap.Cells.All(cell => cell == null);
            GenerateGrid();
            if (initializeBorders) UnWalkBorder();
            DrawAll();
            Loaded = true;
            FitCanvas();
        }


        #region Grille
        public int Get_CellID(Point P)
        {
            try
            {
                int x = P.X;
                int y = P.Y;

                foreach (CellsData C in MyMap.Cells)
                {
                    if (C != null)
                    {
                        int num = (((y - C.Location[0].Y) * (C.Location[1].X - C.Location[0].X)) - ((x - C.Location[0].X) * (C.Location[1].Y - C.Location[0].Y)));
                        int num2 = (((y - C.Location[1].Y) * (C.Location[2].X - C.Location[1].X)) - ((x - C.Location[1].X) * (C.Location[2].Y - C.Location[1].Y)));
                        int num3 = (((y - C.Location[2].Y) * (C.Location[3].X - C.Location[2].X)) - ((x - C.Location[2].X) * (C.Location[3].Y - C.Location[2].Y)));
                        int num4 = (((y - C.Location[3].Y) * (C.Location[0].X - C.Location[3].X)) - ((x - C.Location[3].X) * (C.Location[0].Y - C.Location[3].Y)));
                        if (num >= 0 && num2 >= 0 && num3 >= 0 && num4 >= 0)
                            return C.ID;
                    }
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
            }
            return -1;
        }
        public void GenerateGrid()
        {
            for (int n = 0; n <= H - 1; n++)
            {
                for (int i = 0; i < W; i++)
                {
                    int E_H = n * SizeCell;
                    int E_W = i * SizeCell * 2;
                    Point A = new Point(SizeCell + E_W, E_H);
                    Point B = new Point(SizeCell * 2 + E_W, SizeCell/2 + E_H);
                    Point C = new Point(SizeCell + E_W, SizeCell + E_H);
                    Point D = new Point(E_W, SizeCell/2 + E_H);

                    int ID = i + (n * W * 2) - n;

                    if (ID <= MyMap.Cells.Length - 1)
                    {
                        if (MyMap.Cells[ID] == null)
                        {
                            CellsData NewCell = new CellsData();
                            NewCell.New(this);
                            NewCell.ID = ID;
                            NewCell.Location = new Point[] { A, B, C, D };
                            MyMap.Cells[NewCell.ID] = NewCell;
                        }
                        else
                        {
                            MyMap.Cells[ID].JoinMap(this);
                            MyMap.Cells[ID].Location = new Point[] { A, B, C, D };
                        }
                    }
                }
            }

            for (int u = 0; u <= H - 2; u++)
            {
                for (int o = 0; o <= W - 2; o++)
                {
                    int E_H = (u * SizeCell) + (SizeCell/2);
                    int E_W = (o * SizeCell * 2) + SizeCell;
                    Point A = new Point(SizeCell + E_W, E_H);
                    Point B = new Point(SizeCell * 2 + E_W, SizeCell/2 + E_H);
                    Point C = new Point(SizeCell + E_W, SizeCell + E_H);
                    Point D = new Point(E_W, SizeCell/2 + E_H);

                    int ID = o + (u * (W * 2) + W) - u;

                    if (ID <= MyMap.Cells.Length - 1)
                    {
                        if (MyMap.Cells[ID] == null)
                        {
                            CellsData CD = new CellsData();
                            CD.New(this);
                            CD.ID = ID;
                            CD.Location = new Point[] { A, B, C, D };
                            MyMap.Cells[CD.ID] = CD;
                        }
                        else
                        {
                            MyMap.Cells[ID].JoinMap(this);
                            MyMap.Cells[ID].Location = new Point[] { A, B, C, D };
                        }
                    }
                }
            }
        }

        public void DrawAll(bool showlimit = true)
        {
            if (G == null || MyMap?.Cells == null) return;
            CellsData.SizeCell = SizeCell;
            CellsData.PourceTile = (double)SizeCell / SizeBaseCell;
            G.Clear(Color.FromArgb(247,249,252));

            if (Show_Back)
            {
                if (MyMap.Background != null)
                {
                    int backPosX = (int)(TilesData.Get_Grounds(MyMap.Background.ID).X * CellsData.PourceTile);
                    int backPosY = (int)(TilesData.Get_Grounds(MyMap.Background.ID).Y * CellsData.PourceTile);
                    Rectangle R = new Rectangle(new Point(CellsData.SizeCell - backPosX, Convert.ToInt32(CellsData.SizeCell / 2) - backPosY), PicSize);
                    G.DrawImage(MyMap.Background.Image(true), R);

                }
            }
            foreach(CellsData C in MyMap.Cells)
            {
                if (C == null)
                    continue;
                if (Show_ground)
                {
                    if (C.GFX1 != null)
                        C.Draw_GFX1(G);
                }
                if (Show_calque1)
                {
                    if (C.GFX2 != null)
                        C.Draw_GFX2(G);
                }
                if (Show_calque2)
                {
                    if(C.GFX3 != null)
                    {
                        C.Draw_GFX3(G);
                        if (showlimit && C.IO)
                            C.DrawIO(G);
                    }
                }

            }
            
            if (showlimit)
            {
                try
                {
                    DrawGrid();
                    G.DrawRectangle(Pens.LightSlateGray, SizeCell, Convert.ToInt32(SizeCell / 2), PicSize.Width - SizeCell * 2, PicSize.Height - SizeCell);

                    
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.Message);
                }
            }
            Draw_Mode();
            if (showlimit && ShowServerPlacements && ServerPlacements?.MapId == ID)
                ServerPlacements.Draw(G, MyMap.Cells, SizeCell);
            if(Editor?.T==MainEditeur.Tools.Selector && SelectedCell>=0 && SelectedCell<MyMap.Cells.Length)
                MyMap.Cells[SelectedCell]?.Border(G,Brushes.DodgerBlue);
            Grid?.Dispose();
            Grid = (Bitmap)MyPic.Clone();
            pictureBox1.Image = MyPic;
            pictureBox1.Invalidate();
        }
        public void Draw_Mode()
        {
            //MessageBox.Show("drawmode");
            foreach (CellsData C in MyMap.Cells)
            {
                
                if (C == null)
                    continue;
                if (Show_Grid)
                    C.Border(G, Brushes.Gray);
                if (Show_CellID)
                    C.Draw_ID(G);
                if (IsCellTool)
                    C.DrawMode(G);
                
            }
        }
        public void DrawGrid()
        {
            for (int i = 0; i <= W - 1; i++)
            {
                G.DrawLine(Pens.LightSlateGray, MyMap.Cells[i].Location[3], MyMap.Cells[i].Location[0]);
                G.DrawLine(Pens.LightSlateGray, MyMap.Cells[i].Location[0], MyMap.Cells[i].Location[1]);
            }
            for (int i = H * ((W * 2) - 1) - (W * 2 - 1); i <= MyMap.Cells.Length - 1; i++)
            {
                G.DrawLine(Pens.LightSlateGray, MyMap.Cells[i].Location[3], MyMap.Cells[i].Location[2]);
                G.DrawLine(Pens.LightSlateGray, MyMap.Cells[i].Location[2], MyMap.Cells[i].Location[1]);
            }
            for (int i = W - 1; i <= MyMap.Cells.Length - 1; i += (W * 2 - 1))
            {
                G.DrawLine(Pens.LightSlateGray, MyMap.Cells[i].Location[0], MyMap.Cells[i].Location[1]);
                G.DrawLine(Pens.LightSlateGray, MyMap.Cells[i].Location[1], MyMap.Cells[i].Location[2]);
            }
            for (int i = 0; i <= MyMap.Cells.Length - 1; i += (W * 2 - 1))
            {
                G.DrawLine(Pens.LightSlateGray, MyMap.Cells[i].Location[0], MyMap.Cells[i].Location[3]);
                G.DrawLine(Pens.LightSlateGray, MyMap.Cells[i].Location[3], MyMap.Cells[i].Location[2]);
            }
        }

        private void UnWalkBorder()
        {


            for (int i = 0; i <= W - 1; i++)
            {
                MyMap.Cells[i].UnWalk = true;
            }

            for (int i = H * ((W * 2) - 1) - (W * 2 - 1); i <= MyMap.Cells.Length - 1; i++)
            {
                MyMap.Cells[i].UnWalk = true;
            }

            for (int i = W - 1; i <= MyMap.Cells.Length - 1; i += (W * 2 - 1))
            {
                MyMap.Cells[i].UnWalk = true;
            }

            for (int i = 0; i <= MyMap.Cells.Length - 1; i += (W * 2 - 1))
            {
                MyMap.Cells[i].UnWalk = true;
            }
            
        }
        #endregion
        public void RefreshMap()
        {
            if (MyMap.Background != null)
            {
                pictureBox1.BackgroundImage = MyMap.Background.ImageLoaded;

            }
            else
            {
                pictureBox1.BackgroundImage = null;
            }
        }

        public void DrawBackground(TilesData image)
        {
            MyMap.BackGroundID = image?.ID ?? 0;
            Edited = true;
            if (image != null)
            {
                MyMap.Background = image;

                    }
                    else
                    {
                MyMap.Background = null;
            }

            RefreshMap();
            DrawAll();
        }




        private void pictureBox1_MouseEnter(object sender, EventArgs e)
        {
            pictureBox1.Focus();

        }

        private void MapForm_SizeChanged(object sender, EventArgs e)
        {
            if (Loaded)
            {
                MapForm_ResizeEnd(sender, e);


            }
        }

        private void pictureBox1_MouseMove(object sender, MouseEventArgs e)
        {
            if (!Loaded) return;
            int id = Get_CellID(e.Location);
            string placement = "";
            if (ShowServerPlacements && ServerPlacements?.MapId == ID && id >= 0 && id < MyMap.Cells.Length)
                placement = ServerPlacements.DescribeCell(id, MyMap.Cells[id]?.Paddock == true);
            if (placement != currentPlacementTip)
            {
                currentPlacementTip = placement;
                placementTip.SetToolTip(pictureBox1, placement);
            }
            if (id != HoverCell)
            {
                if (Grid == null) return;
                G.DrawImageUnscaled(Grid, 0, 0);
                HoverCell = id;
                if (id >= 0 && id < MyMap.Cells.Length)
                    MyMap.Cells[id]?.Border(G, Brushes.BlueViolet);
                pictureBox1.Invalidate();
            }
        }

        private void pictureBox1_MouseDown(object sender, MouseEventArgs e)
        {
            MainEditeur editor = Editor;
            if (!Loaded || editor == null) return;
            HoverCell = Get_CellID(e.Location);
            if (HoverCell < 0 || HoverCell >= MyMap.Cells.Length) return;
            if (e.Button != MouseButtons.None)
            {
                SelectedCell = HoverCell;
            }
            editor.SelectCell(this, SelectedCell);
            if (editor.T != MainEditeur.Tools.Selector)
            {
                Edited = true;
                if (e.Button == MouseButtons.Middle)
                {
                    MoveTile();
                }
                else
                {
                    if (ModeTrigger)
                    {
                        if (e.Button == MouseButtons.Left) editor.AddTrigger(MyMap.ID, SelectedCell, this);
                        return;
                    }
                    if (EndFight)
                    {
                        if (e.Button == MouseButtons.Left) editor.AddEndFightAction(MyMap, SelectedCell);
                        return;
                    }
                    if (!IsCellTool)
                    {
                        
                        if (IsBrushTool)
                        {
                            
                            if (e.Button == MouseButtons.Right)
                            {
                                DeleteTile();
                            }
                            else if(e.Button == MouseButtons.Left)
                            {
                               
                                if (IsBrushTool)
                                    AddTile();

                            }
                        }
                    }
                    else
                    {
                        if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
                            AddCellType(e.Button == MouseButtons.Left, SelectedCell);
                    }
                }
            }
            editor.SelectCell(this, SelectedCell);
        }
        public void AddCellType(bool add, int cellid)
        {
            MainEditeur editor = Editor;
            if (editor == null || cellid < 0 || cellid >= MyMap.Cells.Length || MyMap.Cells[cellid] == null) return;
            switch (editor.CellMod)
            {
                case MainEditeur.CellMode.UnWalkable:
                    if(MyMap.Cells[cellid].UnWalk != add)
                    {
                        MyMap.Cells[cellid].UnWalk = add;
                        MyMap.Cells[cellid].Path = false;
                        MyMap.Cells[cellid].Paddock = false;
                        MyMap.Cells[cellid].FightCell = 0;
                        RefreshCellType(add = false, cellid);
                    }
                    break;
                case MainEditeur.CellMode.LoS:
                    if(MyMap.Cells[cellid].Los != !add)
                    {
                        MyMap.Cells[cellid].Los = !add;
                        RefreshCellType(!add, cellid);
                    }
                    break;
                case MainEditeur.CellMode.Path:
                    if(!MyMap.Cells[cellid].UnWalk && MyMap.Cells[cellid].Path != add)
                    {
                        MyMap.Cells[cellid].Path = add;
                        RefreshCellType(!add, cellid);
                    }
                    break;
                case MainEditeur.CellMode.Paddock:
                    if (!MyMap.Cells[cellid].UnWalk && MyMap.Cells[cellid].Paddock != add)
                    {
                        MyMap.Cells[cellid].Paddock = add;
                        RefreshCellType(!add, cellid);
                    }
                    break;
                case MainEditeur.CellMode.Fight1:
                    if (!MyMap.Cells[cellid].UnWalk)
                    {
                        if (add)
                        {
                            if(MyMap.Cells[cellid].FightCell != 1)
                            {
                                MyMap.Cells[cellid].FightCell = 1;
                                RefreshCellType(false, cellid);
                            }
                        }
                        else
                        {
                            MyMap.Cells[cellid].FightCell = 0;
                            DrawAll();
                        }
                    }
                    break;
                case MainEditeur.CellMode.Fight2:
                    if (!MyMap.Cells[cellid].UnWalk)
                    {
                        if (add)
                        {
                            if (MyMap.Cells[cellid].FightCell != 2)
                            {
                                MyMap.Cells[cellid].FightCell = 2;
                                RefreshCellType(false, cellid);
                            }
                        }
                        else
                        {
                            MyMap.Cells[cellid].FightCell = 0;
                            DrawAll();
                        }
                    }
                    break;
            }
        }
        private void RefreshCellType(bool draw, int cellid)
        {
            if (draw)
            {
                DrawAll();
            }
            else
            {
                G.Clear(Color.Black);
                G.DrawImage(Grid, new Point(0, 0));
                MyMap.Cells[cellid].DrawMode(G);
                Grid?.Dispose();
                Grid = (Bitmap)MyPic.Clone();
                pictureBox1.Image = MyPic;
            }
        }
        public void DeleteTile(int calque = 0)
        {
            MainEditeur editor = Editor;
            if (SelectedCell < 0 || SelectedCell >= MyMap.Cells.Length || MyMap.Cells[SelectedCell] == null) return;
            if(calque == 0)
            {
                if(MyMap.Cells[SelectedCell].GFX2 != null && MyMap.Cells[SelectedCell].GFX3 != null)
                {
                    if (editor?.Calque == 1)
                        MyMap.Cells[SelectedCell].GFX2 = null;
                    if (editor?.Calque == 2)
                        MyMap.Cells[SelectedCell].GFX3 = null;
                    DrawAll();
                }else if(MyMap.Cells[SelectedCell].GFX3 != null)
                {
                    MyMap.Cells[SelectedCell].GFX3 = null;
                    DrawAll();

                }else if(MyMap.Cells[SelectedCell].GFX2 != null)
                {
                    MyMap.Cells[SelectedCell].GFX2 = null;
                    DrawAll();

                }else if(MyMap.Cells[SelectedCell].GFX1 != null)
                {
                    MyMap.Cells[SelectedCell].GFX1 = null;
                    DrawAll();
                }
            }
            else
            {
                if(calque == 3)
                {
                    MyMap.Cells[SelectedCell].GFX3 = null;
                }else if(calque == 2)
                {
                    MyMap.Cells[SelectedCell].GFX2 = null;
                }else if(calque == 1)
                {
                    MyMap.Cells[SelectedCell].GFX1 = null;
                }
                DrawAll();
            }
        }
        public void MoveTile(int calque = 0)
        {
            MainEditeur editor = Editor;
            if (SelectedCell < 0 || SelectedCell >= MyMap.Cells.Length || MyMap.Cells[SelectedCell] == null) return;
            if (calque.Equals(0))
            {
                if (MyMap.Cells[SelectedCell].GFX2 != null && MyMap.Cells[SelectedCell].GFX3 != null)
                {
                    if (editor?.Calque == 1)
                    {
                        TilesData.SelectedTiles = MyMap.Cells[SelectedCell].GFX2;
                        MyMap.Cells[SelectedCell].GFX2 = null;
                    }
                    if (editor?.Calque == 2)
                    {
                        TilesData.SelectedTiles = MyMap.Cells[SelectedCell].GFX3;
                        MyMap.Cells[SelectedCell].GFX3 = null;
                    }
                    DrawAll();
                }
                else if (MyMap.Cells[SelectedCell].GFX3 != null)
                {
                    TilesData.SelectedTiles = MyMap.Cells[SelectedCell].GFX3;
                    MyMap.Cells[SelectedCell].GFX3 = null;
                    DrawAll();
                }
                else if (MyMap.Cells[SelectedCell].GFX2 != null)
                {
                    TilesData.SelectedTiles = MyMap.Cells[SelectedCell].GFX2;
                    MyMap.Cells[SelectedCell].GFX2 = null;
                    DrawAll();
                }
                else if (MyMap.Cells[SelectedCell].GFX1 != null)
                {
                    TilesData.SelectedTiles = MyMap.Cells[SelectedCell].GFX1;
                    MyMap.Cells[SelectedCell].GFX1 = null;
                    DrawAll();
                }
            }
            else
            {
                if (calque == 3)
                {
                    TilesData.SelectedTiles = MyMap.Cells[SelectedCell].GFX3;
                    MyMap.Cells[SelectedCell].GFX3 = null;
                    DrawAll();

                }
                else if (calque == 2)
                {
                    TilesData.SelectedTiles = MyMap.Cells[SelectedCell].GFX2;
                    MyMap.Cells[SelectedCell].GFX2 = null;
                    DrawAll();

                }
                else if (calque == 1)
                {
                    TilesData.SelectedTiles = MyMap.Cells[SelectedCell].GFX1;
                    MyMap.Cells[SelectedCell].GFX1 = null;
                    DrawAll();
                }
            }
        }
        public void AddTile()
        {
            MainEditeur editor = Editor;
            if (TilesData.SelectedTiles != null && editor != null)
            {
                int correctedCell = SelectedCell;
                if (correctedCell < 0 || correctedCell >= MyMap.Cells.Length || MyMap.Cells[correctedCell] == null) return;
                
                switch (TilesData.SelectedTiles.type)
                {
                    case TilesData.TileType.ground:
                        MyMap.Cells[correctedCell].GFX1 = TilesData.SelectedTiles;
                        MyMap.Cells[correctedCell].FlipGFX1 = editor.SelectedFlip;
                        MyMap.Cells[correctedCell].RotaGFX1 = editor.SelectedRotate;
                        break;
                    case TilesData.TileType.objet:
                        switch (editor.Calque)
                        {
                            case 1:
                                MyMap.Cells[correctedCell].GFX2 = TilesData.SelectedTiles;
                                MyMap.Cells[correctedCell].FlipGFX2 = editor.SelectedFlip;
                                MyMap.Cells[correctedCell].RotaGFX2 = editor.SelectedRotate;
                                break;
                            case 2:
                                MyMap.Cells[correctedCell].GFX3 = TilesData.SelectedTiles;
                                MyMap.Cells[correctedCell].FlipGFX3 = editor.SelectedFlip;
                                break;
                        }
                        break;
                }
                DrawAll();
            }
            else
            {
                MessageBox.Show("tile null");
            }
        }

        private void pictureBox1_MouseClick(object sender, MouseEventArgs e)
        {
           
        }

        private void pictureBox1_Click(object sender, EventArgs e)
        {
            
        }

        internal void FitCanvas() { if(Loaded && Visible)MapForm_ResizeEnd(this,EventArgs.Empty); }
        private void MapForm_ResizeEnd(object sender, EventArgs e)
        {
            if (!Loaded || !Visible || W <= 0 || H <= 0) return;
            if (SettingsManager.LockSize)
            {
                // Taille fixe
                PicSize = new Size(W * SizeCell * 2, H * SizeCell);
            }
            else
            {
                // Ajuster la taille du formulaire pour maintenir les proportions
                int availableWidth = Math.Max(1,ClientSize.Width - 48);
                int availableHeight = Math.Max(1,ClientSize.Height - 48);
                
                // Calculer la taille des cellules en préservant le ratio 2:1 (W:H)
                int maxCellWidth = availableWidth / (W * 2);
                int maxCellHeight = availableHeight / H;
                
                // Prendre la plus petite valeur pour maintenir les proportions
                int newSize = Math.Min(maxCellWidth, maxCellHeight);
                newSize = Math.Max(1,Math.Min(newSize,100));
                
                // Mettre à jour les tailles
                SizeCell = newSize;
                CellsData.SizeCell = newSize;
                
                // Calculer les nouvelles dimensions du PictureBox
                PicSize = new Size(W * SizeCell * 2, H * SizeCell);
            }

            // Recréer les bitmaps avec les nouvelles dimensions
            if (G != null) G.Dispose();
            if (MyPic != null) MyPic.Dispose();
            if (Grid != null) Grid.Dispose();
            
            MyPic = new Bitmap(PicSize.Width, PicSize.Height);
            Grid = new Bitmap(PicSize.Width, PicSize.Height);
            G = Graphics.FromImage(MyPic);

            // Mettre à jour les ratios
            PourceOfTile = (float)SizeCell / SizeBaseCell;
            CellsData.PourceTile = PourceOfTile;

            // Configurer le PictureBox
            pictureBox1.Size = PicSize;
            pictureBox1.Location=new Point(Math.Max(24,(ClientSize.Width-PicSize.Width)/2),Math.Max(24,(ClientSize.Height-PicSize.Height)/2));
            pictureBox1.SizeMode = PictureBoxSizeMode.Normal;
            pictureBox1.Image = MyPic;

            // Régénérer la grille
            GenerateGrid();
            
            // Redessiner tout
            DrawAll();
            
            // Forcer le rafraîchissement
            pictureBox1.Invalidate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing || e.CloseReason == CloseReason.MdiFormClosing)
            {
                MainEditeur editor = Editor;
                if (editor != null && !editor.CommitPropertiesBeforeClose(this))
                    e.Cancel = true;
                else if (Edited)
                {
                    DialogResult result = MessageBox.Show(this,
                        "Enregistrer les modifications de cette carte avant de la fermer ?",
                        "Carte modifiée", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    e.Cancel = result == DialogResult.Cancel ||
                        result == DialogResult.Yes && (editor == null || !editor.SaveMap(this));
                }
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            MainEditeur editor = Editor;
            if (MyMap != null)
            {
                MyMap.IsEditing = false;
                if (Map.MapList.TryGetValue(MyMap.ID, out Map cached) && ReferenceEquals(cached, MyMap))
                    Map.MapList.Remove(MyMap.ID);
            }
            editor?.NotifyMapClosed(this);
            pictureBox1.Image = null;
            G?.Dispose();
            Grid?.Dispose();
            MyPic?.Dispose();
            placementTip.Dispose();
            base.OnFormClosed(e);
        }

       

       
    }
}
