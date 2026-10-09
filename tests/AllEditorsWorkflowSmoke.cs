using System;
using System.Collections;
using System.ComponentModel;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;
using Outil_Azur_complet;
using Outil_Azur_complet.editeur_compte;
using Outil_Azur_complet.editeur_perso;
using Outil_Azur_complet.editeur_items;
using Outil_Azur_complet.maps;
using Outil_Azur_complet.Parser;
using Tools_protocol.Kryone.Database;

internal static class AllEditorsWorkflowSmoke
{
    private static object Get(object target,string name){return target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(target);}
    private static object Call(object target,string name,params object[] args){try{return target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Invoke(target,args);}catch(TargetInvocationException error){throw error.InnerException;}}
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static System.Collections.Generic.IEnumerable<Control> All(Control parent){yield return parent;foreach(Control child in parent.Controls)foreach(var item in All(child))yield return item;}
    private static void Layout(Control parent){parent.PerformLayout();foreach(Control child in parent.Controls)Layout(child);}
    // Clé privée d'un événement de Form : EVENT_SHOWN / EVENT_LOAD sous .NET Framework, ShownEvent / LoadEvent sous Mono.
    private static object FormEventKey(string framework,string mono)
    {
        foreach(string field in new[]{framework,mono}){object key=typeof(Form).GetField(field,BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null);if(key!=null)return key;}
        throw new MissingFieldException("Form has neither "+framework+" (.NET Framework) nor "+mono+" (Mono) as private event key");
    }
    private static void Render(Form form,string name)
    {
        var events=(EventHandlerList)typeof(Component).GetProperty("Events",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form,null);
        foreach(object key in new[]{FormEventKey("EVENT_SHOWN","ShownEvent"),FormEventKey("EVENT_LOAD","LoadEvent")}){if(events[key]!=null)events.RemoveHandler(key,events[key]);}
        form.ShowInTaskbar=false;form.Opacity=0;form.Show();foreach(Form child in form.MdiChildren)child.Show();Layout(form);
        using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(TestPaths.Work,name+".png"));}
        form.Hide();
    }
    [STAThread]private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{string name=new AssemblyName(e.Name).Name,path=Path.Combine(TestPaths.ApplicationBin,name+".dll");if(!File.Exists(path))path=Path.Combine(TestPaths.ApplicationBin,name+".exe");return File.Exists(path)?Assembly.LoadFrom(path):null;};Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);Application.EnableVisualStyles();Run();
    }
    private static void Run()
    {
        InitializeForm.NoDB=true;InitializeForm.EMUSELECT="Kryone";Tools_protocol.Query.DatabaseManager.ConnectionString=null;Tools_protocol.Query.DatabaseManager2.ConnectionString=null;
        using(var menu=new Outil_Azur_complet.Menu())
        {
            Check(((IDictionary)Get(menu,"_formFactories")).Count==32,"Some editors are inaccessible from the workshop");Render(menu,"atelier-azur");
            var navigation=All(menu).OfType<TabControl>().Last();navigation.SelectedTab=navigation.TabPages.Cast<TabPage>().First(page=>page.Text=="Objets");Render(menu,"atelier-objets");
        }
        using(var account=new editeurcompte())
        {
            ((ListBox)Get(account,"listBox1")).Items.AddRange(new object[]{"Compte-Azur","Compte-Demo"});
            foreach(var pair in new[]{new[]{"1","1"},new[]{"2","Compte-Azur"},new[]{"3","Azur"},new[]{"9","100"}})((Control)Get(account,"iTalk_TextBox_Small"+pair[0])).Text=pair[1];
            ((Control)Get(account,"iTalk_Label14")).Text="2 comptes · données de démonstration";
            Check(All(account).Any(c=>c.Text=="Identifiant du compte"),"Account translated fields are absent");Render(account,"editeur-comptes");
        }
        using(var create=new CreateForm())
        {
            var md5=(RadioButton)Get(create,"radioButton2");var sha=(RadioButton)Get(create,"radioButton3");md5.Checked=true;sha.Checked=true;Check(sha.Checked&&!md5.Checked,"Moving the hash choices broke their exclusivity");Render(create,"creation-compte");
        }
        CharacterList.PersoAll.Clear();var player=(CharacterList)FormatterServices.GetUninitializedObject(typeof(CharacterList));player.Id=1;player.Name="Alice";player.Level=20;player.Account=1;player.Class=1;player.Color1=-1;player.Color2=-1;player.Color3=-1;player.Savepos="100,42";player.Objets="";player.Spells="";player.Jobs="";CharacterList.PersoAll[player.Name]=player;
        using(var character=new editeur_perso())
        {
            Call(character,"editeur_perso_Load",character,EventArgs.Empty);((ListBox)Get(character,"listBox1")).SelectedItem=player.Name;
            var editable=(IDictionary)Get(character,"_editableFields");Check(editable.Contains("color1")&&editable.Contains("savepos"),"Appearance/save position cannot be edited");
            var color=(Control)Get(character,"iTalk_TextBox_Small2");color.Text="123";((TextBox)Get(character,"textBox1")).Text="no-result";((TextBox)Get(character,"textBox1")).Text="";((ListBox)Get(character,"listBox1")).SelectedItem=player.Name;Check(color.Text=="123","Pending color was overwritten on navigation");color.Text="-1";
            var classChoice=All(character).OfType<ComboBox>().First(c=>c.Name=="iTalk_TextBox_Small11_choix");Check(classChoice.Enabled,"Class selector stayed disabled after loading an offline character");Render(character,"editeur-personnages");
            var pages=(TabControl)Get(Get(character,"editorLayout"),"Tabs");pages.SelectedTab=pages.TabPages.Cast<TabPage>().First(page=>page.Text=="Apparence");Render(character,"apparence-personnages");
        }
        var parse=typeof(editeur_perso).GetMethod("ParseFieldValue",BindingFlags.Static|BindingFlags.NonPublic);Check((int)parse.Invoke(null,new object[]{"color1","-1"})==-1,"Default color cannot be saved");Check((string)parse.Invoke(null,new object[]{"savepos","100,42"})=="100,42","Save position validation");
        using(var items=new itemeditor())
        {
            Call(items,"LoadStatic");Check(((ListBox)Get(items,"listBox2")).Items.Count>20,"French item effects require unrelated startup loading");
            var radio1=(Control)Get(items,"iTalk_RadioButton1");var radio2=(Control)Get(items,"iTalk_RadioButton2");radio1.GetType().GetProperty("Checked").SetValue(radio1,true,null);radio2.GetType().GetProperty("Checked").SetValue(radio2,true,null);Check(!(bool)radio1.GetType().GetProperty("Checked").GetValue(radio1,null),"Moving export choices broke their exclusivity");
            ((Control)Get(items,"iTalk_TextBox_Small1")).Text="Amulette Azur";((Control)Get(items,"iTalk_TextBox_Small2")).Text="15000";Render(items,"creation-objet");
            var exportTabs=(TabControl)Get(Get(items,"editorLayout"),"Tabs");exportTabs.SelectedTab=exportTabs.TabPages.Cast<TabPage>().First(page=>page.Text=="Fichier client");Render(items,"export-objet-client");
            Call(items,"LoadStaticInventory");Call(items,"BuildInventoryLayout");var tabs=(TabControl)Get(Get(items,"editorLayout"),"Tabs");tabs.SelectedTab=(TabPage)Get(items,"tabPage5");Render(items,"editeur-inventaire");
            items.Size=items.MinimumSize;Render(items,"editeur-inventaire-compact");Check(Enumerable.Range(0,tabs.TabCount).All(index=>tabs.GetTabRect(index).X==tabs.GetTabRect(0).X),"Compact inventory navigation overlaps in a second column");Check(tabs.GetTabRect(tabs.TabCount-1).Bottom<=tabs.ClientSize.Height,"Last inventory section is inaccessible at the minimum window size");
        }
        using(var maps=new MainEditeur())using(var map=new MapForm())
        {
            map.MdiParent=maps;map.W=15;map.H=17;map.ID=100;map.New(new Tool_Editor.maps.data.Map{ID=100,Width=15,Height=17,Cells=new Tool_Editor.maps.data.CellsData[479],HasProjectCells=true});maps.OpenMap.Add(map);Call(map,"MapForm_Load",map,EventArgs.Empty);Call(maps,"SelectCell",map,0);
            ((CheckBox)Get(maps,"flipGround")).Checked=true;Check(map.MyMap.Cells[0].FlipGFX1,"Cell orientation editor is disconnected");((ComboBox)Get(maps,"fightTeam")).SelectedIndex=2;Check(map.MyMap.Cells[0].FightCell==0,"Fight placement is accepted on a blocked border cell");
            map.MyMap.Cells[0].Type(4);Call(maps,"SelectCell",map,0);((ComboBox)Get(maps,"fightTeam")).SelectedIndex=2;Check(map.MyMap.Cells[0].FightCell==2,"Fight placement editor is disconnected");map.Show_Grid=true;map.DrawAll();
            PopulateMapPreview(maps,map);
            Call(maps,"PopulateMapProperties",map);
            Check(All(maps).OfType<NumericUpDown>().First(control=>control.Name=="iTalk_NumericUpDown1_nombre").Value==map.MyMap.ID,"Displayed map identifier does not follow the loaded map");
            map.MyMap.Cells[0].NivSol=7;Call(maps,"SelectCell",map,0);var groundHeight=All(maps).OfType<NumericUpDown>().First(control=>control.Name=="iTalk_NumericUpDown3_nombre");Check(groundHeight.Value==7,"Displayed cell number does not follow the selected cell");groundHeight.Value=9;Check(map.MyMap.Cells[0].NivSol==9,"Editing a native cell number does not update the map");
            Render(maps,"editeur-cartes");
            foreach(int cellId in new[]{0,14,100,478})
            {var corners=map.MyMap.Cells[cellId].Location;var center=new Point((corners[0].X+corners[2].X)/2,(corners[0].Y+corners[2].Y)/2);Check(map.Get_CellID(center)==cellId,"Resized map cursor misses cell "+cellId);}
            var targetCell=map.MyMap.Cells[100];var targetCorners=targetCell.Location;var targetPoint=new Point((targetCorners[0].X+targetCorners[2].X)/2,(targetCorners[0].Y+targetCorners[2].Y)/2);
            var selectedTile=Tool_Editor.maps.data.TilesData.ListGrounds.FirstOrDefault(tile=>tile!=null && tile.Path.Contains("Herbe"));
            Check(selectedTile!=null,"The supplied grass tile was not indexed");
            targetCell.GFX1=null;Tool_Editor.maps.data.TilesData.SelectedTiles=selectedTile;maps.T=MainEditeur.Tools.Brush;map.IsBrushTool=true;map.IsCellTool=false;
            Call(map,"pictureBox1_MouseDown",map,new MouseEventArgs(MouseButtons.Left,1,targetPoint.X,targetPoint.Y,0));
            Check(ReferenceEquals(targetCell.GFX1,selectedTile),"Clicking a resized map placed the tile on another cell");
            foreach(var cell in map.MyMap.Cells)if(cell!=targetCell)cell.GFX1=null;
            maps.T=MainEditeur.Tools.Selector;map.DrawAll();Render(maps,"placement-tuile-cible");
            Tool_Editor.maps.data.TilesData.SelectedTiles=null;
            Check(map.FormBorderStyle==FormBorderStyle.None && map.ClientSize.Width>600,"Map canvas remains in a small internal window");
            ((ComboBox)Get(maps,"propertySection")).SelectedIndex=5;Render(maps,"editeur-cellules");
        }
        using(var maps=new MainEditeur())using(var map=new MapForm())
        {
            string assets=Path.Combine(TestPaths.ApplicationBin,"ressources","maps");
            var tree=(TreeView)Get(maps,"treeView1");
            Tool_Editor.maps.managers.SearchManager.SearchGrounds(Path.Combine(assets,"sols"),tree.Nodes.Add("Sols"));
            Tool_Editor.maps.managers.SearchManager.SearchObject(Path.Combine(assets,"objets"),tree.Nodes.Add("Objets"));
            Tool_Editor.maps.managers.SearchManager.SearchBackground(Path.Combine(assets,"backgrounds"));
            string fixture=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"..","Fixtures","10000_0612041200.swf");
            var official=Tool_Editor.maps.managers.MapSwfSerializer.Load(fixture);
            map.MdiParent=maps;map.W=official.Width;map.H=official.Height;map.ID=official.ID;map.New(official);
            maps.OpenMap.Add(map);Call(map,"MapForm_Load",map,EventArgs.Empty);
            Check(map.MyMap.Cells.Length==479 && map.MyMap.Cells[478].Location!=null && map.MyMap.Background!=null,
                "The supplied Nowel SWF cannot be opened with Azur's own image library");
            Call(maps,"PopulateMapProperties",map);
            var fullCapabilities=(NumericUpDown)Get(maps,"capabilitiesMask");
            Check(fullCapabilities.Value==98,"The official map's complete authorization value is hidden");
            fullCapabilities.Value=99;Call(maps,"ApplyMapProperties",map);
            Check(map.MyMap.Capabilities==99,"Upper authorization bits cannot be edited");
            fullCapabilities.Value=98;Call(maps,"ApplyMapProperties",map);
            Check(map.MyMap.Capabilities==98,"Restoring the official authorization value failed");
            map.Show_Grid=false;map.DrawAll();Render(maps,"carte-nowel-importee");
            var npcs=new DataTable();npcs.Columns.Add("mapid",typeof(int));npcs.Columns.Add("npcid",typeof(int));npcs.Columns.Add("cellid",typeof(int));npcs.Rows.Add(10000,10,100);
            var groups=new DataTable();groups.Columns.Add("mapid",typeof(int));groups.Columns.Add("cellid",typeof(int));groups.Columns.Add("groupData",typeof(string));groups.Rows.Add(10000,222,"100,1,5;");
            var zaaps=new DataTable();zaaps.Columns.Add("mapid",typeof(int));zaaps.Columns.Add("cellid",typeof(int));zaaps.Rows.Add(10000,320);
            var paddocks=new DataTable();paddocks.Columns.Add("mapid",typeof(int));paddocks.Columns.Add("cellid",typeof(int));paddocks.Rows.Add(10000,260);
            map.MyMap.Cells[260].Paddock=true;
            map.SetServerPlacements(MapServerPlacementLayer.FromTables(10000,479,new Dictionary<ServerResourceKind,DataTable>{
                {ServerResourceKind.Npcs,npcs},{ServerResourceKind.MonsterGroups,groups},{ServerResourceKind.Zaaps,zaaps},{ServerResourceKind.Paddocks,paddocks}}));
            Render(maps,"carte-nowel-placements-demo");
        }
        using(var manager=new RessourceParser()){Render(manager,"gestionnaire-ressources");}
        using(var settings=new SettingsForm()){Render(settings,"configuration");}
        using(var json=new Outil_Azur_complet.Annexes.JsonModifier()){Render(json,"configuration-tables");}
        using(var size=new OtherSizeForm()){Render(size,"creation-carte");}
        using(var save=new SaveForm()){Render(save,"export-carte");}
        TestClientItemEditor();
        TestHexRoundTrip();
        TestStructuredEditors();
        TestEveryResourceSchema();
        Console.WriteLine("OK: all legacy editor layouts, hash/export exclusivity, character appearance drafts, item effects, map orientation/fight controls and UI renders");
    }
    private static void TestClientItemEditor()
    {
        string path=Path.Combine(TestPaths.Work,"client-ui.swf"),copy=Path.Combine(TestPaths.Work,"client-ui-copy.swf");
        Tool_Editor.items.ItemClientSwf.Save(path,new Tool_Editor.items.ItemClientDefinition{Id=100,Name="Amulette Azur",Description="Un objet de démonstration",Type=1,Level=10,Price=120,Weight=5,Graphic=200,ForgeMagic=true});
        var source=Tool_Editor.items.ItemClientSwf.Load(path);source.SaveCopy(copy,source.WithDefinition(new Tool_Editor.items.ItemClientDefinition{Id=200,Name="Épée Azur",Type=6,Level=20,ActionPoints=4,MinimumRange=1,MaximumRange=1}));
        using(var client=new ItemClientEditorForm())
        {
            Call(client,"LoadFile",copy);Call(client,"Commit");Check(((IDictionary)Get(client,"drafts")).Count==0,"Opening a client item silently stages changes");
            Check(All(client).Any(control=>control.Text=="Nom de l'objet") && All(client).Any(control=>control.Text=="Forge-magie autorisée"),"Client fields are untranslated");Render(client,"editeur-objets-client");
            var input=All(client).First(control=>control.Name=="n_valeur");input.Text="Amulette corrigée";Call(client,"Commit");
            var names=(ListBox)Get(client,"list");names.SelectedIndex=1;names.SelectedIndex=0;Check(All(client).First(control=>control.Name=="n_valeur").Text=="Amulette corrigée","Switching client items lost the draft");
            var pages=(TabControl)Get(client,"tabs");pages.SelectedTab=pages.TabPages.Cast<TabPage>().First(page=>page.Text=="Arme");Render(client,"options-arme-client");
            var quantity=All(client).First(control=>control.Name=="e1_valeur");quantity.Text="3";Call(client,"Commit");
            var drafts=(System.Collections.Generic.Dictionary<int,System.Collections.Generic.IDictionary<string,object>>)Get(client,"drafts");source=Tool_Editor.items.ItemClientSwf.Load(copy);string exported=Path.Combine(TestPaths.Work,"client-ui-edited.swf");source.SaveCopy(exported,source.WithChanges(drafts));
            var reread=Tool_Editor.items.ItemClientSwf.Load(exported);Check(reread.ItemNames[100]=="Amulette corrigée" && (int)((object[])reread.GetFields(100)["e"])[1]==3,"Client editor fields are not saved into the actual SWF");drafts.Clear();
        }
    }
    private static void TestStructuredEditors()
    {
        var assembly=typeof(ServerDataForm).Assembly;Type specType=assembly.GetType("Outil_Azur_complet.Editors.FieldSpec"),listType=assembly.GetType("Outil_Azur_complet.Editors.DelimitedListForm"),itemCatalog=assembly.GetType("Outil_Azur_complet.Editors.ItemFieldCatalog");
        var hex=itemCatalog.GetMethod("Hex",BindingFlags.Static|BindingFlags.NonPublic);var number=specType.GetMethod("Number",BindingFlags.Static|BindingFlags.NonPublic);
        string[][] cases={new[]{"Effets de l'objet","7d#01#0a#0#1d10+0","Type d'effet","Valeur minimale","Valeur maximale","Paramètre complémentaire","Jet de dés"},new[]{"Ingrédients de la recette","101*5;102*2","Ingrédient","Quantité"},new[]{"Bonus du palier","125:5,118:2","Type d'effet","Valeur du bonus"}};
        foreach(var info in cases)
        {
            bool effects=info[0]=="Effets de l'objet",recipe=info[0]=="Ingrédients de la recette";int count=info.Length-2;Array specs=Array.CreateInstance(specType,count);
            for(int field=0;field<count;field++)
            {
                object spec=effects&&field<4?hex.Invoke(null,new object[]{info[field+2],"Valeur présentée en décimal."}):field==4?Activator.CreateInstance(specType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{info[field+2],"Par exemple 1d10+0.","Général",false},null):number.Invoke(null,new object[]{info[field+2],"",false});
                if(field==0)specType.GetField("Suggestions",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(spec,effects||!recipe?Tools_protocol.Data.EffectsListing.SpellsEffectList:new System.Collections.Generic.Dictionary<string,string>{{"101","Ressource Azur"},{"102","Bois de frêne"}});
                specs.SetValue(spec,field);
            }
            using(var form=(Form)Activator.CreateInstance(listType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{info[0],"Modifiez chaque entrée séparément. Appliquer reporte les valeurs dans la fiche.",info[1],recipe?';':',',effects?'#':recipe?'*':':',specs,new Func<string,string>(record=>recipe?"Ingrédient #"+record.Replace("*"," · quantité "):record),count},null))
            {
                Check((bool)Call(form,"Commit"),"Invalid original structured options: "+info[0]);Check(((System.Collections.Generic.List<string>)Get(form,"records"))[0]==info[1].Split(recipe?';':',')[0],"Structured editor rewrites untouched original data");Render(form,effects?"options-effets-objet":recipe?"options-recette":"options-bonus-panoplie");
            }
        }
        Type colorType=assembly.GetType("Outil_Azur_complet.Editors.ColorValueForm");var parse=colorType.GetMethod("Parse",BindingFlags.Static|BindingFlags.NonPublic);
        Check((int)parse.Invoke(null,new object[]{"#007ACC"})==0x007ACC && (int)parse.Invoke(null,new object[]{"-1"})==-1,"Color defaults or RGB encoding changed");
        using(var color=(Form)Activator.CreateInstance(colorType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{"31487"},null)){Render(color,"choisir-couleur");}
        Type conditionsType=assembly.GetType("Outil_Azur_complet.Editors.ConditionExpressionForm");
        using(var conditions=(Form)Activator.CreateInstance(conditionsType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{"PL>19&CS>10"},null))
        {
            ((Control)Get(conditions,"value")).Text="20";Call(conditions,"AddCondition");Check(((Control)Get(conditions,"expression")).Text.StartsWith("PL>19&CS>10&"),"Guided condition builder discarded the existing expression");Render(conditions,"options-conditions");
        }
    }
    private static void PopulateMapPreview(MainEditeur editor,MapForm map)
    {
        string directory=Path.Combine(TestPaths.ApplicationBin,"ressources","maps","sols","Herbe");
        if(!Directory.Exists(directory))return;
        var tree=(TreeView)Get(editor,"treeView1");var root=tree.Nodes.Add("Sols");tree.Nodes.Add("Objets");
        Tool_Editor.maps.managers.SearchManager.SearchGrounds(directory,root);
        var node=root.Nodes.Cast<TreeNode>().FirstOrDefault()??root;node.Tag=directory;tree.ExpandAll();tree.SelectedNode=node;Call(editor,"PopulateTileList",node);
        var tile=Tool_Editor.maps.data.TilesData.ListGrounds.FirstOrDefault(item=>item!=null && item.Path.StartsWith(directory,StringComparison.OrdinalIgnoreCase));
        if(tile!=null)
        {
            foreach(var cell in map.MyMap.Cells)if(!cell.UnWalk)cell.GFX1=tile;
            map.DrawAll();
        }
    }
    private static void TestEveryResourceSchema()
    {
        foreach(var resource in EditorFixtures.Resources)
        {
            var table=EditorFixtures.Table(resource);var row=table.NewRow();EditorFixtures.Populate(resource,row);table.Rows.Add(row);table.AcceptChanges();
            var snapshot=new ServerDataSnapshot();typeof(ServerDataSnapshot).GetProperty("Data").SetValue(snapshot,table,null);typeof(ServerDataSnapshot).GetProperty("Kind").SetValue(snapshot,resource.Kind,null);
            string[] keys=EditorFixtures.Keys(resource);
            typeof(ServerDataSnapshot).GetField("Keys",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(snapshot,keys.Length>0?keys:table.Columns.Cast<DataColumn>().Select(column=>column.ColumnName).ToArray());
            typeof(ServerDataSnapshot).GetField("MapColumn",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(snapshot,table.Columns.Cast<DataColumn>().FirstOrDefault(column=>column.ColumnName.Equals("map",StringComparison.OrdinalIgnoreCase)||column.ColumnName.Equals("mapid",StringComparison.OrdinalIgnoreCase))==null?null:table.Columns.Cast<DataColumn>().First(column=>column.ColumnName.Equals("map",StringComparison.OrdinalIgnoreCase)||column.ColumnName.Equals("mapid",StringComparison.OrdinalIgnoreCase)).ColumnName);
            typeof(ServerDataSnapshot).GetField("ReferenceNames",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(snapshot,new System.Collections.Generic.Dictionary<string,string>{{"100","Amulette Azur"},{"101","Ressource Azur"},{"102","Bois de frêne"}});
            using(var form=new ServerDataForm(resource.Kind,resource.Title))
            {
                form.GetType().GetField("snapshot",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,snapshot);Call(form,"RefreshList",new object[]{null});Call(form,"SetBusy",false);
                typeof(ServerDataForm).BaseType.GetMethod("SetStatus",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{"Données de démonstration · aucune connexion au serveur utilisée."});
                Check(((IList)Get(form,"fields")).Count==table.Columns.Count,"Missing options for "+resource.Table);
                var tabs=(TabControl)Get(form,"tabs");foreach(TabPage page in tabs.TabPages){tabs.SelectedTab=page;Check((bool)Call(form,"CommitCurrent"),"Invalid untouched fixture: "+resource.Table+" / "+page.Text);}
                Check(table.GetChanges()==null,"Viewing options changed "+resource.Table);
                tabs.SelectedIndex=0;Render(form,"ressource-"+resource.Kind);
                if(resource.Kind==ServerResourceKind.ItemTemplates||resource.Kind==ServerResourceKind.ItemSets||resource.Kind==ServerResourceKind.Crafts||resource.Kind==ServerResourceKind.Drops)
                foreach(TabPage page in tabs.TabPages)if(page.Text=="Effets"||page.Text=="Composition"||page.Text=="Bonus"||page.Text=="Recette"||page.Text=="Probabilités"){tabs.SelectedTab=page;Render(form,"ressource-"+resource.Kind+"-options");}
            }
        }
        Console.WriteLine("OK: all 24 resource editors expose the supplied Kryone schemas without changing untouched values");
    }
    private static void TestHexRoundTrip()
    {
        var assembly=typeof(ServerDataForm).Assembly;var catalog=assembly.GetType("Outil_Azur_complet.Editors.ItemFieldCatalog");object spec=catalog.GetMethod("Hex",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{"Valeur",""});Type fieldType=assembly.GetType("Outil_Azur_complet.Editors.EditorField");
        using(var field=(Control)Activator.CreateInstance(fieldType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{new DataColumn("hex",typeof(string)){AllowDBNull=false},"01A",spec,false},null))
        {
            var input=All(field).First(c=>c.Name=="hex_valeur");Check(input.Text=="26","Hex effect value not presented as decimal");Check((string)Call(field,"Read")=="01A","Viewing an effect changed its original hex representation");input.Text="27";Check((string)Call(field,"Read")=="1b","Editing decimal effect value did not encode hexadecimal");
        }
    }
}
