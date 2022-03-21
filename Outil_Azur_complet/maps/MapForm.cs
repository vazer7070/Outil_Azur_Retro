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
        MainEditeur E = new MainEditeur();
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

        public bool IsCellTool = false;
        public bool IsBrushTool = false;
        public bool ModeTrigger = false;
        public bool EndFight = false;

        public MainEditeur ME = new MainEditeur();


        public void New(Map map = null)
        {

            if (map != null)
            {
                MyMap = map;
                MyMap.Load();
            }
        }
        public MapForm()
        {
            InitializeComponent();
        }

        private void MapForm_Load(object sender, EventArgs e)
        {
            KeyPreview = true;
            if (MyMap == null)
            {
                MyMap = new Map();
                MyMap.Cells = new CellsData[(H * (W * 2 - 1) - W + 1)];
                MyMap.Width = W;
                MyMap.Height = H;
            }
            New(MyMap);
            MyMap.IsEditing = true;
            PicSize = new Size(W * SizeCell * 2, H * SizeCell);
            Size = new Size(PicSize.Width + 16, PicSize.Height + 38);
            MyPic = new Bitmap(PicSize.Width, PicSize.Height);
            Grid = new Bitmap(PicSize.Width, PicSize.Height);
            G = Graphics.FromImage(MyPic);
            pictureBox1.Image = MyPic;
            CellsData.PourceTile = SizeCell / SizeBaseCell;

            Map.AddMap(MyMap);

            GenerateGrid();
            UnWalkBorder();
            DrawAll();
            Loaded = true;
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
                for (int i = 0; i <= W; i++)
                {
                    int E_H = n * SizeCell;
                    int E_W = i * SizeCell * 2;
                    Point A = new Point(SizeCell + E_W, E_H);
                    Point B = new Point(SizeCell * 2 + E_W, SizeCell / 2 + E_H);
                    Point C = new Point(SizeCell + E_W, SizeCell + E_H);
                    Point D = new Point(E_W, SizeCell / 2 + E_H);

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
                    int E_H = (u * SizeCell) + (SizeCell / 2);
                    int E_W = (o * SizeCell * 2) + SizeCell;
                    Point A = new Point(SizeCell + E_W, E_H);
                    Point B = new Point(SizeCell * 2 + E_W, SizeCell / 2 + E_H);
                    Point C = new Point(SizeCell + E_W, SizeCell + E_H);
                    Point D = new Point(E_W, SizeCell / 2 + E_H);

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
           
            G.Clear(Color.Black);

            if (ME.Show_Back)
            {
                if (MyMap.Background != null)
                {
                    int backPosX = (int)(TilesData.Get_Grounds(MyMap.Background.ID).X * CellsData.PourceTile);
                    int backPosY = (int)(TilesData.Get_Grounds(MyMap.Background.ID).Y * CellsData.PourceTile);
                    Rectangle R = new Rectangle(new Point(CellsData.SizeCell - backPosX, Convert.ToInt32(CellsData.SizeCell / 2) - backPosY), PicSize);
                    G.DrawImage(MyMap.Background.Image(), R);

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
                    G.DrawRectangle(Pens.Brown, SizeCell, Convert.ToInt32(SizeCell / 2), PicSize.Width - SizeCell * 2, PicSize.Height - SizeCell);

                    
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.Message);
                }
            }
            Draw_Mode();
            Grid = (Bitmap)MyPic.Clone();
            pictureBox1.Image = MyPic;
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
                G.DrawLine(Pens.Brown, MyMap.Cells[i].Location[3], MyMap.Cells[i].Location[0]);
                G.DrawLine(Pens.Brown, MyMap.Cells[i].Location[0], MyMap.Cells[i].Location[1]);
            }
            for (int i = H * ((W * 2) - 1) - (W * 2 - 1); i <= MyMap.Cells.Length - 1; i++)
            {
                G.DrawLine(Pens.Brown, MyMap.Cells[i].Location[3], MyMap.Cells[i].Location[2]);
                G.DrawLine(Pens.Brown, MyMap.Cells[i].Location[2], MyMap.Cells[i].Location[1]);
            }
            for (int i = W - 1; i <= MyMap.Cells.Length - 1; i += (W * 2 - 1))
            {
                G.DrawLine(Pens.Brown, MyMap.Cells[i].Location[0], MyMap.Cells[i].Location[1]);
                G.DrawLine(Pens.Brown, MyMap.Cells[i].Location[1], MyMap.Cells[i].Location[2]);
            }
            for (int i = 0; i <= MyMap.Cells.Length - 1; i += (W * 2 - 1))
            {
                G.DrawLine(Pens.Brown, MyMap.Cells[i].Location[0], MyMap.Cells[i].Location[3]);
                G.DrawLine(Pens.Brown, MyMap.Cells[i].Location[3], MyMap.Cells[i].Location[2]);
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
                pictureBox1.BackgroundImage = MyMap.Background.Image();

            }
            else
            {
                pictureBox1.BackgroundImage = null;
            }
        }

        public void DrawBackground(TilesData image)
        {
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
            
           
        }

        private void pictureBox1_MouseMove(object sender, MouseEventArgs e)
        {
            int id = Get_CellID(pictureBox1.PointToClient(MousePosition));

            if (id != HoverCell && id != -1)
            {
                G.Clear(Color.Black);
                G.DrawImage(Grid, new Point(0, 0));
                HoverCell = id;
                MyMap.Cells[HoverCell].Border(G, Brushes.BlueViolet);
                pictureBox1.Image = MyPic;
            }


        }

        private void pictureBox1_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.None)
            {
                SelectedCell = HoverCell;
            }
            if ((TilesData.SelectedTiles != null) || (E.T != MainEditeur.Tools.Selector))
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
                        E.AddTrigger(MyMap.ID, SelectedCell, this);
                        return;
                    }
                    if (EndFight)
                    {
                        E.AddEndFightAction(MyMap, SelectedCell);
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
                        bool add = true;
                        if(e.Button == MouseButtons.Right)
                        {
                            add = false;
                            AddCellType(add, SelectedCell);
                        }
                    }
                }
            }
        }
        public void AddCellType(bool add, int cellid)
        {
            
            switch (E.CellMod)
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
                Grid = (Bitmap)MyPic.Clone();
                pictureBox1.Image = MyPic;
            }
        }
        public void DeleteTile(int calque = 0)
        {
            if(calque == 0)
            {
                if(MyMap.Cells[SelectedCell].GFX2 != null && MyMap.Cells[SelectedCell].GFX3 != null)
                {
                    if (E.Calque == 1)
                        MyMap.Cells[SelectedCell].GFX2 = null;
                    if (E.Calque == 2)
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
            if (calque.Equals(0))
            {
                if (MyMap.Cells[SelectedCell].GFX2 != null && MyMap.Cells[SelectedCell].GFX3 != null)
                {
                    if (E.Calque == 1)
                    {
                        TilesData.SelectedTiles = MyMap.Cells[SelectedCell].GFX2;
                        MyMap.Cells[SelectedCell].GFX2 = null;
                    }
                    if (E.Calque == 2)
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
            if (TilesData.SelectedTiles != null)
            {

                switch (TilesData.SelectedTiles.type)
                {
                    case TilesData.TileType.ground:
                        MyMap.Cells[SelectedCell].GFX1 = TilesData.SelectedTiles;
                        MyMap.Cells[SelectedCell].FlipGFX1 = E.SelectedFlip;
                        MyMap.Cells[SelectedCell].RotaGFX1 = E.SelectedRotate;
                        break;
                    case TilesData.TileType.objet:
                        switch (E.Calque)
                        {
                            case 1:
                                MyMap.Cells[SelectedCell].GFX2 = TilesData.SelectedTiles;
                                MyMap.Cells[SelectedCell].FlipGFX2 = E.SelectedFlip;
                                MyMap.Cells[SelectedCell].RotaGFX2 = E.SelectedRotate;
                                break;
                            case 2:
                                MyMap.Cells[SelectedCell].GFX3 = TilesData.SelectedTiles;
                                MyMap.Cells[SelectedCell].FlipGFX3 = E.SelectedFlip;
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

        private void MapForm_ResizeEnd(object sender, EventArgs e)
        {
           if (SettingsManager.LockSize)
            {
                PicSize = new Size(W * SizeCell * 2, H * SizeCell);
                Size = new Size(PicSize.Width + 16, PicSize.Height + 38);
                MyPic = new Bitmap(PicSize.Width, PicSize.Height);
                Grid = new Bitmap(PicSize.Width, PicSize.Height);
                G = Graphics.FromImage(MyPic);
                pictureBox1.Image = MyPic;
                CellsData.PourceTile = SizeCell / SizeBaseCell;
                GenerateGrid();
                DrawAll();
            }
            else
            {
                int x = Convert.ToInt32(pictureBox1.Width / (W * 2 + 1));
                int y = Convert.ToInt32(pictureBox1.Height / (H + 2));
                if (x < y)
                {
                    CellsData.SizeCell = x;
                }
                else
                {
                    CellsData.SizeCell = y;
                }
                PicSize = new Size(W * CellsData.SizeCell * 2, H * CellsData.SizeCell);
                MyPic = new Bitmap(PicSize.Width, PicSize.Height);
                G = Graphics.FromImage(MyPic);
                CellsData.PourceTile = CellsData.SizeCell / SizeBaseCell;
                pictureBox1.Image = MyPic;
                Size = new Size(PicSize.Width + 16, PicSize.Height + 38);
                GenerateGrid();
                DrawAll();
            }

        }
    }
}

