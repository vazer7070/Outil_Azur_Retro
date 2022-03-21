using Outil_Azur_complet.Bot.Controls.tooltip;
using Syncfusion.Windows.Forms;
using Syncfusion.WinForms.Controls;
using Syncfusion.WinForms.Controls.Events;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Interactives;
using Tool_BotProtocol.Game.Monstres;
using Tool_BotProtocol.Game.NPC;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Utils.Pics;

namespace Outil_Azur_complet.Bot.Controls
{
    [Serializable]
    public partial class UserMapControl : UserControl
    {
        public int H { get; set; }
        public int W { get; set; }
        public int SizeCell = 26;
        private bool MouseIn;
        private UserMapCell CellH;
        private UserMapCell cell2;
        private UserMapCell CellBottom;
        private Accounts Account;
        public MapQuality MQ;
        private ConcurrentDictionary<int, Animations> Anim;
        private System.Timers.Timer AnimTimer;
        private bool ShowAnim;
        private bool ShowCell;
        private Bitmap TriggerPic = Properties.Resources._21000;
        SfToolTip sf = new SfToolTip();
       
        private readonly int[] DoorGFX = { 6750, 6749, 6744, 6745, 6746, 6747, 6748, 6751, 6752, 6753, 6754, 6755, 6756, 6757, 6758, 6759, 6760, 6762, 6763, 6764, 6765, 6766, 6767, 6768, 6772, 6773, 6774, 6775, 6776 };
        private readonly int[] StatueGFX = { 1854 ,708 ,922, 1351, 1470, 1570, 1591, 1592, 1583, 1597, 1598,1845, 1853, 1854, 1855, 1856, 1857, 1858, 1859, 1860, 1861, 1862, 2054  };
        private readonly int[] MiscGFX = { 7352, 260, 261, 262, 263, 264, 265, 266, 267 ,268, 938, 939, 940, 941, 942, 943, 944, 945, 946, 2520, 2521, 2522, 2523, 2524 ,2525, 2526, 2527, 2528, 2529, 2530, 2531, 2532, 2533, 2534, 2535, 2536, 2537, 2538, 2538, 2539, 2540, 2541, 2542, 7519, 7041, 7042, 7043, 7044, 7045, 7046, 7001, 7002, 7003, 7004, 7005, 7006, 7007, 7008, 7009, 7010, 7011, 7012, 7013, 7014, 7015, 7016, 7017,7019, 7020, 7021, 7022, 7023, 7024, 7025, 7027, 7028, 7032, 7033, 7034, 7035, 7036, 7037, 7038, 7039, 7350, 7351, 7353 };
        private readonly int[] TreeGFX = { 7500, 215, 211, 217, 219, 211, 212, 947, 948, 949, 950, 951, 1657, 1658, 1666, 1667, 1668, 1669, 2726, 2727, 2728, 2729, 2932, 2733,7542, 7557, 7541, 7509 };

        private readonly int[] RecolteGFX = {7511, 7512, 7513, 7514, 7515, 7516, 7517, 7518};
       

        [Browsable(false)]
        public int RealCellHeight { get;  private set; }
        [Browsable(false)]
        public int RealcellWidth { get; private set; }
        public Color CellInactive { get; set; }
        public Color CellActive { get; set; }
        public bool TraceOnOver { get; set; }

        [Browsable(false)]
        public UserMapCell CurrentCellHover { get; set; }
        public Color BorderColorOver { get; set; }

        [Browsable(false)]
        public UserMapCell[] Cells { get; set; }
        public void SetAccount(Accounts A) => Account = A;
        public bool ShowAnimations
        {
            get => ShowAnim;
            set
            {
                ShowAnim = value;
                if(ShowAnim)
                    AnimTimer.Start();
            }
        }
        public bool ShowCellId
        {
            get => ShowCell;
            set
            {
                ShowCell = value;
                Invalidate();
            }
        }
        public MapQuality MapQ
        {
            get => MQ;
            set
            {
                MQ = value;
                Invalidate();
            }
        }
        public delegate void CellClickedHandler(UserMapCell cell, MouseButtons Buttons, bool Goodies);
        public event CellClickedHandler CellClicked;
        public event Action<UserMapCell, UserMapCell> HasClickedOnCell;

        public UserMapControl()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            MQ = MapQuality.HAUT;
            H = 17;
            W = 15;
            TraceOnOver = false;
            CellInactive = Color.DarkGray;
            CellActive = Color.Azure;
            ShowAnim = true;
            Anim = new ConcurrentDictionary<int, Animations>();
            AnimTimer = new System.Timers.Timer(80);
            AnimTimer.Elapsed += FinalizeAnimations;

           
            SetCellNum();
            DrawGrille();
            InitializeComponent();
        }
        private void FinalizeAnimations(object sender, ElapsedEventArgs e)
        {
            if(Anim.Count > 0)
            {
                Invalidate();
            }else if (!ShowAnim)
            {
                AnimTimer.Stop();
            }
        }
        protected void OnCellclicked(UserMapCell cell, MouseButtons buttons, bool G) => CellClicked?.Invoke(cell, buttons, G);
        protected void OnCellOver(UserMapCell cell, UserMapCell last) => HasClickedOnCell?.Invoke(cell, last);

        private void ApplyQuality(Graphics g)
        {
            switch (MQ)
            {
                case MapQuality.BAS:
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.CompositingQuality = CompositingQuality.HighSpeed;
                    g.InterpolationMode = InterpolationMode.Low;
                    g.SmoothingMode = SmoothingMode.HighSpeed;
                    break;

                case MapQuality.MOYEN:
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.CompositingQuality = CompositingQuality.GammaCorrected;
                    g.InterpolationMode = InterpolationMode.High;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    break;


                case MapQuality.HAUT:
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    break;
            }
        }

        public void SetCellNum()
        {
            Cells = new UserMapCell[2 * H * W];
            short cellid = 0;
            UserMapCell cell;
            for (int i = 0; i < H; i++)
            {
                for(int j = 0; j < W *2; j++)
                {
                    cell = new UserMapCell(cellid++);
                    Cells[cell.id] = cell;
                }
            }
            
        }
        private double GetMaxHight()
        {
            double CellLarge = Width / (double)(W + 1);
            double CellLong = Height / (double)(H + 1);
            CellLarge = Math.Min(CellLong * 2, CellLarge);

            return CellLarge;
        }
        
        public void DrawGrille()
        {
            try
            {
                int cellid = 0;
                double cellwidth = GetMaxHight();
                double cellheight = Math.Ceiling(cellwidth / 2);

                int offsetX = Convert.ToInt32((Width - ((W + 0.5) * cellwidth)) / 2);
                var offsetY = Convert.ToInt32((Height - ((H + 0.5) * cellheight)) / 2);

                double midCellHeight = cellheight / 2;
                double midCellWidth = cellwidth / 2;

                for (int y = 0; y <= (2 * H) -1; ++y)
                {
                    if ((y % 2) == 0)
                    {
                        for (int x = 0; x <= W - 1; x++)
                        {
                            Point left = new Point(Convert.ToInt32(offsetX + (x * cellwidth)), Convert.ToInt32(offsetY + (y * midCellHeight) + midCellHeight));
                            Point top = new Point(Convert.ToInt32(offsetX + (x * cellwidth) + midCellWidth), Convert.ToInt32(offsetY + (y * midCellHeight)));
                            Point right = new Point(Convert.ToInt32(offsetX + (x * cellwidth) + cellwidth), Convert.ToInt32(offsetY + (y * midCellHeight) + midCellHeight));
                            Point down = new Point(Convert.ToInt32(offsetX + (x * cellwidth) + midCellWidth), Convert.ToInt32(offsetY + (y * midCellHeight) + cellheight));
                            //MessageBox.Show($"cellid(188): {cellid++}, {left}; {top}; {right}; {down};");
                            Cells[cellid++].Points = new Point[] { left, top, right, down };
                        }
                    }
                    else
                    {
                        for (int x = 0; x <= W - 2; x++)
                        {
                            Point left = new Point(Convert.ToInt32(offsetX + (x * cellwidth) + midCellWidth), Convert.ToInt32(offsetY + (y * midCellHeight) + midCellHeight));
                            Point top = new Point(Convert.ToInt32(offsetX + (x * cellwidth) + cellwidth), Convert.ToInt32(offsetY + (y * midCellHeight)));
                            Point right = new Point(Convert.ToInt32(offsetX + (x * cellwidth) + cellwidth + midCellWidth), Convert.ToInt32(offsetY + (y * midCellHeight) + midCellHeight));
                            Point down = new Point(Convert.ToInt32(offsetX + (x * cellwidth) + cellwidth), Convert.ToInt32(offsetY + (y* midCellHeight) + cellheight));
                            //MessageBox.Show($"cellid: {cellid++}, {left}; {top}; {right}; {down};");
                            Cells[cellid++].Points = new Point[] { left, top, right, down };
                        }
                    }
                }
                RealCellHeight = (int)cellheight;
                RealcellWidth = (int)cellwidth;
                Invalidate();
            }
            catch { }

        }
        public virtual void DrawUniqueCell(Graphics G, UserMapCell cell)
        {
            if (cell.IsRectangle(G.ClipBounds))
            {
                switch (cell.State)
                {
                    case CellState.WALKABLE:
                        cell.DrawColor(G, Color.Gray, Color.White);
                        if (ShowCellId)
                            cell.DrawCell_ID(this, G);
                        break;
                    case CellState.OBSTACLE:
                        if (ShowCellId)
                            cell.DrawCell_ID(this, G);
                        else
                            cell.DrawObstacle(G, Color.Gray, Color.FromArgb(60, 60, 60));
                        break;
                    case CellState.TRIGGER:

                        if (ShowCellId)
                        {
                            cell.DrawColor(G, Color.Gray, Color.Yellow);
                            cell.DrawCell_ID(this, G);
                        }
                        else
                        {
                            cell.DrawTrigger(TriggerPic, G);
                        }
                        break;
                    case CellState.INTERACTIVE:
                        if (!ShowCellId)
                        {
                            int map = Account.Game.Map.MapID;
                            int GFX_id = Account.Game.Map.GetCellFromId(cell.id).Interactives.gfx;
                            if (PicturesManager.InteractivePicGfx(GFX_id, false) != null)
                            {
                                if (Zaaps.Z.ContainsKey(map) && cell.id == Zaaps.Z[map])
                                {

                                    if (map == 7411 || map == 8785 || map == 5295 || map == 11210)
                                        cell.DrawZaap(PicturesManager.InteractivePicGfx(GFX_id, true), G, true);
                                    else
                                        cell.DrawZaap(PicturesManager.InteractivePicGfx(GFX_id, false), G);

                                }
                                else if (DoorGFX.Contains(GFX_id))
                                {
                                    cell.DrawDoor(PicturesManager.InteractivePicGfx(GFX_id, false), G);

                                }
                                else if (StatueGFX.Contains(GFX_id))
                                {
                                    cell.DrawStatue(PicturesManager.InteractivePicGfx(GFX_id, false), G);

                                }
                                else if (MiscGFX.Contains(GFX_id))
                                {
                                    cell.DrawMisc(PicturesManager.InteractivePicGfx(GFX_id, false), G);
                                }
                                else if (TreeGFX.Contains(GFX_id))
                                {
                                    cell.DrawTree(PicturesManager.InteractivePicGfx(GFX_id, false), G);
                                }
                                else
                                {
                                    cell.DrawZaapi(PicturesManager.InteractivePicGfx(GFX_id, false), G);
                                }
                            }
                            else
                            {
                                cell.DrawColor(G, Color.LightGoldenrodYellow, Color.LightGoldenrodYellow);
                                cell.DrawCell_ID(this, G);
                            }

                        }
                        else
                        {
                            cell.DrawColor(G, Color.LightGoldenrodYellow, Color.LightGoldenrodYellow);
                            cell.DrawCell_ID(this, G);
                        }
                        break;
                    default:
                        cell.DrawColor(G, Color.Gray, Color.DarkGray);
                        break;
                }
                if (Account != null)
                {
                    if (Account.Game.character.Cell != null && cell.id == Account.Game.character.Cell.CellID && !Anim.ContainsKey(Account.Game.character.id))
                        cell.Draw_FillingPie(G, Color.Blue, RealCellHeight / 2);
                    else if (Account.Game.Map.Entites.Values.Where(x => x is Monstres).FirstOrDefault(x => x.Cell.CellID == cell.id && !Anim.ContainsKey(x.id)) != null)
                        cell.Draw_FillingPie(G, Color.DarkRed, RealCellHeight / 2);
                    else if (Account.Game.Map.Entites.Values.Where(x => x is PNJ).FirstOrDefault(x => x.Cell.CellID == cell.id && !Anim.ContainsKey(x.id)) != null)
                    {
                        
                        PNJ P = PNJ.ReturnNpc(Account.Game.Map.NPC_List().FirstOrDefault(x => x.Cell.CellID == cell.id).NPc_ID, true);
                        if (P != null)
                        {
                            Bitmap bmp = PicturesManager.InteractivePicSprite(P.GFX, P.Orientation);
                            if (bmp != null)
                                cell.DrawNPC(bmp, G);
                            else
                                cell.Draw_FillingPie(G, Color.FromArgb(179, 120, 211), RealCellHeight / 2);
                        }
                        else
                            cell.Draw_FillingPie(G, Color.FromArgb(78, 119, 185), RealCellHeight / 2);


                    }
                    else if (Account.Game.Map.Entites.Values.Where(x => x is Personnages).FirstOrDefault(x => x.Cell.CellID == cell.id && !Anim.ContainsKey(x.id)) != null)
                        cell.Draw_FillingPie(G, Color.FromArgb(81, 113, 202), RealCellHeight / 2);

                }
            }
        }
        public static Bitmap ReturnMonsterStar(int star) 
        {
            
            switch (star)
            {
                case 0:
                    return Properties.Resources.re11_1;
                    case 15:
                    return Properties.Resources.re1_1;
                case 30:
                    return Properties.Resources.re2_1;
                case 45:
                    return Properties.Resources.re3_1;
                case 60:
                    return Properties.Resources.re4_1;
                case 75:
                    return Properties.Resources.re5_1;
                case 90:
                    return Properties.Resources.re6_1;
                case 105:
                    return Properties.Resources.re7_1;
                case 120:
                    return Properties.Resources.re8_1;
                case 135:
                    return Properties.Resources.re9_1;
                case 150:
                    return Properties.Resources.re10_1;
                default:
                    return Properties.Resources.re11_1;
            };
        }
        public void DrawCells(Graphics G)
        {
            ApplyQuality(G);
            G.Clear(BackColor);
            foreach(UserMapCell cell in Cells)
            {
                DrawUniqueCell(G, cell);
                DrawAnim(G);
            }
        }
        public void AddAnimations(int id, List<Cell> path, int d, AnimationType T)
        {
            if (path.Count < 2 || !ShowAnimations)
                return;
            if (Anim.ContainsKey(id))
                FinalizeAnimations(Anim[id]);
            Animations NewAnim = new Animations(id, path.Select(x => Cells[x.CellID]), d, T);
            NewAnim.Finalize += FinalizeAnimations;
            Anim.TryAdd(id, NewAnim);
            NewAnim.Init();
        }
        private void FinalizeAnimations(Animations A)
        {
            A.Finalize -= FinalizeAnimations;
            Anim.TryRemove(A.Entites_Id, out Animations B);
            A.Dispose();

            Invalidate();
        }
        private void DrawAnim(Graphics G)
        {
            foreach(Animations A in Anim.Values)
            {
                if(A.Path == null)
                    continue;
                using(SolidBrush S = new SolidBrush(AnimColor(A)))
                    G.FillPie(S, A.Actual_point.X - (RealCellHeight /2 /2), A.Actual_point.Y - RealCellHeight /2 /2, RealCellHeight /2, RealCellHeight /2, 0, 360);
            }
        }
        private Color AnimColor(Animations A)
        {
            switch (A.AnimationType)
            {
                case AnimationType.PERSONNAGE:
                    return Color.Blue;
                case AnimationType.GROUPE_MONSTRES:
                    return Color.DarkRed;
                default:
                    return Color.FromArgb(81, 113, 202);
            }
        }
        public void RefreshMap()
        {
            
            if (Account.Game.Map == null)
                return;
            Anim.Clear();
            AnimTimer.Stop();


            Cell[] MapCells = Account.Game.Map.MapCells;
            if (MapCells == null)
                return;
            
            foreach(Cell cell in MapCells)
            {
                Cells[cell.CellID].State = CellState.NO_WALKABLE;

                if (cell.IsWalkable())
                    Cells[cell.CellID].State = CellState.WALKABLE;
                if(cell.LineofSight)
                    Cells[cell.CellID].State = CellState.OBSTACLE;
                if(cell.IsTrigger())
                    Cells[cell.CellID].State = CellState.TRIGGER;
                if (cell.IsInteractiveCell())
                {
                    Cells[cell.CellID].State = CellState.INTERACTIVE;
                }
                    
                    
            }
            AnimTimer.Start();
            Invalidate();
        }
        public UserMapCell GetCell(Point P)
        {
            UserMapCell C = new UserMapCell();
            C = Cells.FirstOrDefault(x => x.IsRectangle(new Rectangle(P.X - RealcellWidth, P.Y - RealCellHeight, RealcellWidth, RealCellHeight)) && pointPoly(x.Points, P));
            
            return C;
        }

        public bool pointPoly(Point[] poly, Point P)
        {
           if(poly != null)
            {
                int XA, YA, XN, YN, x1, y1, x2, y2;
                bool inside = false;

                if (poly.Length < 3)
                    return false;
                XA = poly[poly.Length - 1].X;
                YA = poly[poly.Length - 1].Y;

                foreach (Point point in poly)
                {
                    XN = point.X;
                    YN = point.Y;
                    if (XN > XA)
                    {
                        x1 = XA;
                        x2 = XN;
                        y1 = YA;
                        y2 = YN;
                    }
                    else
                    {
                        x1 = XN;
                        x2 = XA;
                        y1 = YN;
                        y2 = YA;
                    }
                    if ((XN < P.X) == (P.X <= XA) && (P.Y - (long)y1) * (x2 - x1) < (y2 - (long)y1) * (P.X - x1))
                    {
                        inside = !inside;
                    }
                    XA = XN;
                    YA = YN;
                }
                return inside;
            }
            return false;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            DrawCells(e.Graphics);
            base.OnPaint(e);
        }
        protected override void OnResize(EventArgs e)
        {
            DrawGrille();
            base.OnResize(e);
        }
        private void Sf_ToolTipShowing(object sender, ToolTipShowingEventArgs e)
        {
            e.Location = CellH.Centre;
            
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            try
            {
                cell2 = GetCell(e.Location);
                Graphics g = this.CreateGraphics();
                Cell C = null;
                ToolTip T = new ToolTip();
                ToolTipItem toolTipItem2 = new ToolTipItem();
                ToolTipInfo Tinfo = new ToolTipInfo();
                T.UseFading = false;

                if (cell2 != null)
                {
                    if (CellH == null)
                        CellH = cell2;
                    C = Account.Game.Map.GetCellFromId(cell2.id);
                    if (CellH != null && cell2 != CellH)
                    {

                        if (CellH.State != CellState.NO_WALKABLE || CellH.State != CellState.OBSTACLE || CellH.State != CellState.WALKABLE)
                        {
                            sf.Hide();
                            Invalidate(CellH.Rectangle);
                        }
                        CellH = cell2;

                    }
                    if (CellH.State == CellState.INTERACTIVE)
                    {
                        if (RecolteGFX.Contains(C.Interactives.gfx))
                            CellH.Border(g, Brushes.DarkGreen);
                        else
                            CellH.Border(g, Brushes.Red);

                    }
                    else if (C != null && CellH.State == CellState.TRIGGER)
                    {
                        CellH.Border(g, Brushes.Purple);

                    }
                    else if (Account.Game.Map.Entites.Values.Where(x => x is Personnages).FirstOrDefault(x => x.Cell.CellID == CellH.id && !Anim.ContainsKey(x.id)) != null)
                    {
                        CellH.Border(g, Brushes.Blue);
                        T.ToolTipTitle = "Joueur";
                        T.Show(Account.Game.Map.PersoList().FirstOrDefault(x => x.Cell.CellID == CellH.id).name, this, CellH.Centre, 60);

                    }
                    else if (Account.Game.Map.Entites.Values.Where(x => x is Monstres).FirstOrDefault(x => x.Cell.CellID == CellH.id && !Anim.ContainsKey(x.id)) != null)
                    {
                        List<string> groups = new List<string>();
                        List<string> names = new List<string>();
                        string S = "";
                        foreach( var n in Account.Game.Map.MonsterList().FirstOrDefault(x => x.Cell.CellID == CellH.id).MobsInGroupe)
                            groups.Add($"{n.TemplateID}|{n.Level}|{n.Star}");

                        foreach(string s in groups)
                        {
                            names.Add($"{Monstres.ReturnMonsters(int.Parse(s.Split('|')[0])).Name}({s.Split('|')[1]})");
                            S = s.Split('|')[2];
                        }
                        string f = string.Join("\n", names.ToArray());
                        CellH.Border(g, Brushes.DarkRed);

                        string level = $"NIVEAU: {Account.Game.Map.MonsterList()?.FirstOrDefault(x => x.Cell.CellID == CellH.id).MobsGroupelevel}";
                        toolTipItem2.Text = $"{level}\n{f}";
                        toolTipItem2.Image = ReturnMonsterStar(int.Parse(S));
                        toolTipItem2.Style.ImageSize = new Size(80, 15);
                        Tinfo.Items.Add(toolTipItem2);
                        sf.Show(Tinfo);


                    }
                    else if (Account.Game.character.Cell != null && CellH.id == Account.Game.character.Cell.CellID && !Anim.ContainsKey(Account.Game.character.id))
                    {

                        CellH.Border(g, Brushes.Blue);
                        T.ToolTipTitle = "Joueur (soi-même)";
                        T.Show(Account.Game.character.Name, this, CellH.Centre, 60);

                    }
                    else if (Account.Game.Map.Entites.Values.Where(x => x is PNJ).FirstOrDefault(x => x.Cell.CellID == CellH.id && !Anim.ContainsKey(x.id)) != null)
                    {

                        CellH.Border(g, Brushes.BlueViolet);
                        T.ToolTipTitle = $"PNJ({Account.Game.Map.NPC_List().FirstOrDefault(x => x.Cell.CellID == CellH.id).GFX}";
                        T.Show(Account.Game.Map.Entites.Values.Where(x => x is PNJ).FirstOrDefault(x => x.Cell.CellID == CellH.id && !Anim.ContainsKey(x.id)).Name, this, CellH.Centre, 60);
                    }
                }
            }
            catch { }

            if (MouseIn)
            {
                UserMapCell cell = GetCell(e.Location);
                if(CellH != null && CellH != cell)
                {
                    OnCellclicked(CellH, e.Button, true);
                    CellH = cell;
                }
                if(cell != null)
                {
                    
                    OnCellclicked(cell, e.Button, true);
                }
                    
            }
           
            base.OnMouseMove(e);
        }
        
        protected override void OnMouseDown(MouseEventArgs e)
        {
            UserMapCell cell = GetCell(e.Location);
            if(cell != null)
            {
                CellH = CellBottom = cell;
                
            }
            MouseIn = true;
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            MouseIn = false;
            UserMapCell cell = GetCell(e.Location);
            if(CellH != null)
            {
                
                OnCellclicked(CellH, e.Button, cell != CellBottom);
                CellH = null;
            }
            base.OnMouseUp(e);
        }

        private void UserMapControl_Paint(object sender, PaintEventArgs e)
        {
            
        }
    }
}
