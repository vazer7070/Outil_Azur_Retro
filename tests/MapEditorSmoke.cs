using System;
using System.Drawing;
using System.Data;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Outil_Azur_complet.maps;
using Outil_Azur_complet;
using Tool_Editor.maps.data;
using AssetSearch = Tool_Editor.maps.managers.SearchManager;

internal static class MapEditorSmoke
{
    [STAThread]
    private static void Main()
    {
        string app = TestPaths.ApplicationBin;
        AppDomain.CurrentDomain.AssemblyResolve += (sender,args) =>
        {
            string name=new AssemblyName(args.Name).Name;
            string path=Path.Combine(app,name+".dll");
            if(!File.Exists(path)) path=Path.Combine(app,name+".exe");
            return File.Exists(path)?Assembly.LoadFrom(path):null;
        };
        Run();
    }
    private static object Field(object value,string name)
    { return value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value); }
    private static object Call(object value,string name,params object[] args)
    {
        try { return value.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(value,args); }
        catch(TargetInvocationException error) { throw error.InnerException; }
    }
    private static void Check(bool ok,string message) { if(!ok)throw new Exception(message); }
    private static void Number(object editor,string name,long value)
    { object control=Field(editor,name); control.GetType().GetProperty("Value").SetValue(control,value,null); }
    private static void Run()
    {
        string scratch=Path.Combine(TestPaths.Work, "map-assets-test");
        string nested=Path.Combine(scratch,"nested");
        string valid=Path.Combine(nested,"42.PNG"),invalid=Path.Combine(scratch,"keep-this.png");
        Directory.CreateDirectory(nested);
        try
        {
            using(var bitmap=new Bitmap(52,26)) bitmap.Save(valid);
            using(var ground=new Bitmap(52,26))using(var graphics=Graphics.FromImage(ground)){graphics.Clear(Color.Lime);ground.Save(Path.Combine(nested,"43.png"));}
            using(var objectImage=new Bitmap(20,40))using(var graphics=Graphics.FromImage(objectImage)){graphics.Clear(Color.Blue);objectImage.Save(Path.Combine(nested,"44.png"));}
            using(var padded=new Bitmap(80,60))using(var graphics=Graphics.FromImage(padded)){graphics.Clear(Color.Transparent);using(var brush=new SolidBrush(Color.Red))graphics.FillRectangle(brush,45,20,20,20);padded.Save(Path.Combine(nested,"45.png"));}
            File.WriteAllText(invalid,"not a numeric background name");
            var node=new TreeNode("Grounds");
            AssetSearch.SearchGrounds(scratch,node);
            Check(TilesData.ListGrounds[42]!=null,"nested tile not indexed");
            AssetSearch.SearchObject(scratch,new TreeNode("Objects"));
            Check(TilesData.ListObject[44]!=null,"object tile not indexed");
            Check(TilesData.Anchor(TilesData.ListGrounds[43],new Size(52,26))==new Point(26,13),"Ground fallback anchor is off-center");
            Check(TilesData.Anchor(TilesData.ListObject[44],new Size(20,40))==new Point(10,40),"Object fallback anchor does not sit on the cell");
            var paddedTile=TilesData.ListGrounds[45];Check(TilesData.Anchor(paddedTile,paddedTile.Image(true))==new Point(55,30),"Transparent PNG margins still offset the visible tile");
            string supplied=Path.Combine(TestPaths.ApplicationBin,"ressources","maps","sols","Herbe","4.png");
            if(File.Exists(supplied))
            {var real=new TilesData(4,supplied,"Herbe",TilesData.TileType.ground);var anchor=TilesData.Anchor(real,real.Image(true));Check(anchor.X>real.Image(true).Width/2,"The supplied padded ground image still uses the canvas center");}
            Check(node.Nodes.Count==1 && node.Nodes[0].Tag!=null,"directory path tags missing");
            AssetSearch.SearchBackground(scratch);
            Check(File.Exists(invalid),"asset scanner deleted a file");

            using(var editor=new MainEditeur()) using(var map=new MapForm())
            {
                Check(((ToolStrip)Field(editor,"toolStrip1")).Items.ContainsKey("exportSwfButton"),"SWF export is absent from toolbar");
                Check(((ToolStrip)Field(editor,"toolStrip1")).Items.ContainsKey("serverPlacementsButton"),"Server placement layer is absent from toolbar");
                map.MdiParent=editor; map.W=3; map.H=4; map.ID=12345;
                map.New(new Map { ID=12345,Width=3,Height=4,Cells=new CellsData[18],HasProjectCells=true });
                editor.OpenMap.Add(map);
                Call(map,"MapForm_Load",map,EventArgs.Empty);
                Check(map.Loaded && map.MyMap.Cells[0].Location!=null,"map initialization failed");
                var npcs=new DataTable();npcs.Columns.Add("mapid",typeof(int));npcs.Columns.Add("npcid",typeof(int));npcs.Columns.Add("cellid",typeof(int));
                npcs.Rows.Add(12345,10,0);npcs.Rows.Add(12345,11,18);npcs.Rows.Add(456,12,1);
                var groups=new DataTable();groups.Columns.Add("mapid",typeof(int));groups.Columns.Add("cellid",typeof(int));groups.Columns.Add("groupData",typeof(string));groups.Rows.Add(12345,0,"101,1,2;");
                var zaaps=new DataTable();zaaps.Columns.Add("mapID",typeof(int));zaaps.Columns.Add("cellID",typeof(int));zaaps.Rows.Add(12345,5);
                var paddocks=new DataTable();paddocks.Columns.Add("mapid",typeof(int));paddocks.Rows.Add(12345);
                var placements=MapServerPlacementLayer.FromTables(12345,18,new Dictionary<ServerResourceKind,DataTable>{
                    {ServerResourceKind.Npcs,npcs},{ServerResourceKind.MonsterGroups,groups},{ServerResourceKind.Zaaps,zaaps},{ServerResourceKind.Paddocks,paddocks}});
                Check(placements.Count(ServerResourceKind.Npcs)==1 && placements.At(0).Count==2 && placements.At(5).Count==1 && placements.PaddockCount==1,"Placements are not scoped to valid cells and the map");
                Check(placements.DescribeCell(0,false).Contains("PNJ #10") && placements.DescribeCell(5,false).Contains("Zaap"),"Placement details are missing");
                map.MyMap.Cells[1].Paddock=true;map.SetServerPlacements(placements);
                Check(map.ShowServerPlacements && map.MyPic.GetPixel(16,13).ToArgb()!=Color.FromArgb(247,249,252).ToArgb(),"Server markers are not rendered on the map");
                map.HideServerPlacements();Check(!map.ShowServerPlacements,"Server markers cannot be hidden");
                using(var placement=new Bitmap(110,100))using(var graphics=Graphics.FromImage(placement))
                {
                    graphics.Clear(Color.White);map.MyMap.Cells[0].Draw_Tiles(graphics,TilesData.ListGrounds[43],false,0);
                    Check(placement.GetPixel(0,0).ToArgb()==Color.Lime.ToArgb() && placement.GetPixel(51,25).ToArgb()==Color.Lime.ToArgb() && placement.GetPixel(52,26).ToArgb()==Color.White.ToArgb(),"Ground image is offset from the cursor cell");
                    graphics.Clear(Color.White);map.MyMap.Cells[0].Draw_Tiles(graphics,TilesData.ListObject[44],false,0);
                    Check(placement.GetPixel(16,0).ToArgb()==Color.Blue.ToArgb() && placement.GetPixel(35,12).ToArgb()==Color.Blue.ToArgb() && placement.GetPixel(36,13).ToArgb()==Color.White.ToArgb(),"Object foot is offset from the cursor cell");
                    graphics.Clear(Color.White);map.MyMap.Cells[0].Draw_Tiles(graphics,paddedTile,false,0);
                    Check(placement.GetPixel(26,13).ToArgb()==Color.Red.ToArgb() && placement.GetPixel(15,13).ToArgb()==Color.White.ToArgb(),"Visible pixels of a padded tile miss the cursor cell");
                }
                foreach(int cellId in new[]{0,1,3,8,17})
                {var corners=map.MyMap.Cells[cellId].Location;var center=new Point((corners[0].X+corners[2].X)/2,(corners[0].Y+corners[2].Y)/2);Check(map.Get_CellID(center)==cellId,"Mouse hit-test misses cell "+cellId);}
                // This fixture loads without showing a document. Enable the tools
                // that the real workspace enables when its document is activated.
                ((ToolStrip)Field(editor,"toolStrip2")).Enabled=true;
                Call(editor,"SelectCell",map,0);
                ((CheckBox)Field(editor,"activeCellCheckBox")).Checked=false;
                Check(!map.MyMap.Cells[0].Active,"active cell property is not editable");
                ((ComboBox)Field(editor,"movementComboBox")).SelectedIndex=6;
                Check(map.MyMap.Cells[0].Type()==6,"uncommon movement property is not editable");
                ((ComboBox)Field(editor,"movementComboBox")).SelectedIndex=4;
                ((ToolStripButton)Field(editor,"toolStripButton7")).PerformClick();
                Check(editor.T==MainEditeur.Tools.CellMode && editor.CellMod==MainEditeur.CellMode.UnWalkable && map.IsCellTool,"cell toolbar not connected");
                map.MyMap.Cells[0].UnWalk=false;
                Call(map,"pictureBox1_MouseDown",map,new MouseEventArgs(MouseButtons.Left,1,26,13,0));
                Check(map.MyMap.Cells[0].UnWalk,"left click did not add cell mode");
                Call(map,"pictureBox1_MouseDown",map,new MouseEventArgs(MouseButtons.Right,1,26,13,0));
                Check(!map.MyMap.Cells[0].UnWalk,"right click did not remove cell mode");
                ((ToolStripButton)Field(editor,"toolStripButton5")).PerformClick();
                ((ToolStripButton)Field(editor,"toolStripButton15")).PerformClick();
                ((ToolStripButton)Field(editor,"toolStripButton16")).PerformClick();
                Check(editor.SelectedFlip && editor.SelectedRotate==1,"flip/rotation toolbar not connected");
                TilesData.SelectedTiles=TilesData.ListGrounds[42];
                map.SelectedCell=0;
                map.AddTile();
                Check(map.MyMap.Cells[0].GFX1!=null && map.MyMap.Cells[0].FlipGFX1,"tile was placed on the wrong cell");
                TilesData.SelectedTiles=null;
                map.DrawAll();

                Call(editor,"PopulateMapProperties",map);
                Number(editor,"iTalk_NumericUpDown1",777);
                Number(editor,"iTalk_NumericUpDown9",-12);
                Number(editor,"iTalk_NumericUpDown10",34);
                ((Control)Field(editor,"sfComboBox1")).Text="2";
                ((Control)Field(editor,"sfComboBox2")).Text="3";
                Call(editor,"ApplyMapProperties",map);
                Check(map.ID==777 && map.MyMap.ID==777 && map.MyMap.X==-12 && map.MyMap.Y==34,"map properties not applied");
                Check(Map.GetByID(12345)==null && Map.GetByID(777)==map.MyMap,"map ID registry was not updated");
                Map.MapList.Remove(map.MyMap.ID);
                map.G.Dispose(); map.Grid.Dispose(); map.MyPic.Dispose();
            }
            Console.WriteLine("OK: asset safety, nested paths, map tools, exact cell placement, renderer and metadata");
        }
        finally
        {
            if(File.Exists(valid))File.Delete(valid);
            foreach(string name in new[]{"43.png","44.png","45.png"}){string extra=Path.Combine(nested,name);if(File.Exists(extra))File.Delete(extra);}
            if(File.Exists(invalid))File.Delete(invalid);
            Directory.Delete(nested); Directory.Delete(scratch);
        }
    }
}
