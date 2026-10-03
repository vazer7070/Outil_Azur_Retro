using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using MySql.Data.MySqlClient;
using Outil_Azur_complet;
using Outil_Azur_complet.maps;
using Tools_protocol.Json;
using Tools_protocol.Query;

internal static class AllEditorsIntegrationSmoke
{
    private static string Connection(string database) {return new MySqlConnectionStringBuilder{Server="127.0.0.1",Port=43306,UserID="root",Database=database,ConnectionTimeout=3}.ConnectionString;}
    private static void Sql(string connection,string sql){using(var c=new MySqlConnection(connection))using(var command=new MySqlCommand(sql,c)){c.Open();command.ExecuteNonQuery();}}
    private static object Scalar(string connection,string sql){using(var c=new MySqlConnection(connection))using(var command=new MySqlCommand(sql,c)){c.Open();return command.ExecuteScalar();}}
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static bool Rejected(Action run){try{run();return false;}catch(FormatException){return true;}catch(InvalidOperationException){return true;}catch(MySqlException){return true;}}
    private static void Set(object target,string name,object value){var property=target.GetType().GetProperty(name);if(property!=null)property.SetValue(target,value,null);else target.GetType().GetField(name).SetValue(target,value);}
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{string name=new AssemblyName(e.Name).Name,path=Path.Combine(TestPaths.ApplicationBin,name+".dll");if(!File.Exists(path))path=Path.Combine(TestPaths.ApplicationBin,name+".exe");return File.Exists(path)?Assembly.LoadFrom(path):null;};Run();
    }
    private static void Run()
    {
        string database="azur_all_edit_"+Guid.NewGuid().ToString("N").Substring(0,8),server=Connection(""),connection=Connection(database);
        try
        {
            Sql(server,"CREATE DATABASE `"+database+"` CHARACTER SET utf8mb4");
            Sql(connection,"CREATE TABLE templates(id INT PRIMARY KEY,type INT NOT NULL DEFAULT 1,name VARCHAR(50) NOT NULL DEFAULT '',level INT NOT NULL DEFAULT 1,statsTemplate VARCHAR(300) NOT NULL DEFAULT '',pod INT DEFAULT 0,panoplie INT DEFAULT -1,prix INT DEFAULT 0,conditions VARCHAR(100) DEFAULT '',armesInfos VARCHAR(100) DEFAULT '',sold INT DEFAULT 0,avgPrice INT DEFAULT 0,points INT DEFAULT 0,exchangesObject INT DEFAULT 0,boutique INT DEFAULT 0) ENGINE=InnoDB;"+
                "INSERT INTO templates(id,name,statsTemplate) VALUES(1,'Amulette Azur','7d#01#0a#0#1d10+0'),(2,'Ressource',''),(3,'Modele libre','');"+
                "CREATE TABLE sets(ID INT PRIMARY KEY,name VARCHAR(150),items TEXT,bonus TEXT) ENGINE=InnoDB; INSERT INTO sets VALUES(10,'Azur','','125:5;125:10,118:2');"+
                "CREATE TABLE crafts(id INT PRIMARY KEY,craft TEXT) ENGINE=InnoDB; CREATE TABLE monsters(id INT PRIMARY KEY,name VARCHAR(100)) ENGINE=InnoDB;INSERT INTO monsters VALUES(1,'Bouftou');"+
                "CREATE TABLE drops(id INT UNSIGNED AUTO_INCREMENT,monsterName VARCHAR(255) DEFAULT '',monsterId INT UNSIGNED NOT NULL,objectName VARCHAR(255) DEFAULT '',objectId INT UNSIGNED NOT NULL,percentGrade1 DECIMAL(6,3) DEFAULT 0,percentGrade2 DECIMAL(6,3) DEFAULT 0,percentGrade3 DECIMAL(6,3) DEFAULT 0,percentGrade4 DECIMAL(6,3) DEFAULT 0,percentGrade5 DECIMAL(6,3) DEFAULT 0,ceil INT DEFAULT 0,action VARCHAR(255) DEFAULT '1',level INT DEFAULT -1,PRIMARY KEY(id,monsterId,objectId)) ENGINE=InnoDB;"+
                "CREATE TABLE items(guid INT PRIMARY KEY,template INT,qua INT,pos INT,stats TEXT,puit INT) ENGINE=InnoDB;INSERT INTO items VALUES(100,1,1,-1,'7d#01#0a#0#1d10+0',0);"+
                "CREATE TABLE players(id INT PRIMARY KEY,name VARCHAR(50),objets TEXT,logged INT) ENGINE=InnoDB;INSERT INTO players VALUES(1,'Alice','100|',0);"+
                "CREATE TABLE cells(MapID INT,CellID INT,ActionID INT,EventID INT,ActionsArgs TEXT,Conditions TEXT) ENGINE=InnoDB; INSERT INTO cells VALUES(100,0,0,1,'200,1','-1');"+
                "CREATE TABLE doors(maps VARCHAR(255),doorsEnable VARCHAR(255),doorsDisable VARCHAR(255),cellsEnable VARCHAR(255),cellsDisable VARCHAR(255),requiredCells VARCHAR(255),button VARCHAR(11),time INT) ENGINE=InnoDB;INSERT INTO doors VALUES('100','1','2','3','4',NULL,'5',30);");
            DatabaseManager.ConnectionString=connection;DatabaseManager2.ConnectionString=connection;InitializeForm.EMUSELECT="Kryone";
            JsonManager.Auth_dico.Clear();JsonManager.World_dico.Clear();
            foreach(var pair in new[]{new[]{"Template","templates"},new[]{"panoplies","sets"},new[]{"crafts","crafts"},new[]{"drops","drops"},new[]{"monstres","monsters"},new[]{"perso","players"},new[]{"cellule","cells"},new[]{"Iporte","doors"}})JsonManager.Auth_dico[pair[0]]=pair[1];JsonManager.World_dico["items"]="items";
            Sql(connection,"CREATE TABLE npcs(mapid INT,npcid INT,cellid INT,orientation INT,isMovable TINYINT,PRIMARY KEY(mapid,npcid,cellid)) ENGINE=InnoDB;"+
                "INSERT INTO npcs VALUES(100,10,0,1,0),(200,11,1,1,0);"+
                "CREATE TABLE mobgroups_fix(mapid INT,cellid INT,groupData VARCHAR(200),Donjon VARCHAR(100),Salle VARCHAR(50),Timer INT,message VARCHAR(100),PRIMARY KEY(mapid,cellid)) ENGINE=InnoDB;"+
                "INSERT INTO mobgroups_fix VALUES(100,5,'1,2,3;','','',30000,''),(200,6,'1,2,3;','','',30000,'');"+
                "CREATE TABLE zaaps(mapID INT PRIMARY KEY,cellID INT) ENGINE=InnoDB;INSERT INTO zaaps VALUES(100,17),(200,2);"+
                "CREATE TABLE mountpark_data(mapid INT PRIMARY KEY,cellid INT,owner INT,guild INT,price INT,data TEXT,enclos TEXT,ObjetPlacer TEXT,durabilite TEXT) ENGINE=InnoDB;"+
                "INSERT INTO mountpark_data VALUES(100,7,0,-1,100,'','','','');");
            JsonManager.Auth_dico["npcs"]="npcs";JsonManager.Auth_dico["groupe_monstre"]="mobgroups_fix";
            JsonManager.Auth_dico["zaaps"]="zaaps";JsonManager.Auth_dico["enclos"]="mountpark_data";
            var placements=MapServerPlacementLayer.Load(100,18);
            Check(placements.Count(ServerResourceKind.Npcs)==1 && placements.At(0).Count==1 &&
                placements.Count(ServerResourceKind.MonsterGroups)==1 && placements.At(5).Count==1 &&
                placements.Count(ServerResourceKind.Zaaps)==1 && placements.At(17).Count==1 &&
                placements.PaddockCount==1 && placements.At(7).Count==1 &&
                placements.At(7)[0].Kind==ServerResourceKind.Paddocks && placements.At(2).Count==0 && placements.Notice=="",
                "Live server placements are not scoped to the active map");
            Sql(connection,"DROP TABLE zaaps");
            var partialPlacements=MapServerPlacementLayer.Load(100,18);
            Check(partialPlacements.Count(ServerResourceKind.Npcs)==1 && partialPlacements.Count(ServerResourceKind.Zaaps)==0 &&
                partialPlacements.Notice.Contains("zaaps"),"A missing placement table hides valid placements");
            var set=ServerDataService.Load(ServerResourceKind.ItemSets);set.Data.Rows[0]["items"]="1,2";ServerDataService.Save(set);
            Check(Convert.ToInt32(Scalar(connection,"SELECT panoplie FROM templates WHERE id=1"))==10,"Set membership did not update templates");
            set.Data.Rows[0]["items"]="2";ServerDataService.Save(set);Check(Convert.ToInt32(Scalar(connection,"SELECT panoplie FROM templates WHERE id=1"))==-1,"Removed set item kept membership");
            var templates=ServerDataService.Load(ServerResourceKind.ItemTemplates);templates.Data.Rows.Find(1)["panoplie"]=10;ServerDataService.Save(templates);
            Check(Convert.ToString(Scalar(connection,"SELECT items FROM sets WHERE ID=10"))=="2,1","Template membership did not update set composition");
            templates.Data.Rows.Find(1)["name"]="L'Azur\\nouveau";ServerDataService.Save(templates);Check(Convert.ToString(Scalar(connection,"SELECT name FROM templates WHERE id=1"))=="L'Azur\\nouveau","Template editing escaped text incorrectly");
            set=ServerDataService.Load(ServerResourceKind.ItemSets);set.Data.Rows[0]["items"]="2,999";Check(Rejected(()=>ServerDataService.Save(set)),"Missing set item accepted");Check(Convert.ToString(Scalar(connection,"SELECT items FROM sets"))=="2,1","Failed set edit persisted partially");
            templates=ServerDataService.Load(ServerResourceKind.ItemTemplates);templates.Data.Rows.Find(1).Delete();Check(Rejected(()=>ServerDataService.Save(templates)),"Live item template deleted");
            templates=ServerDataService.Load(ServerResourceKind.ItemTemplates);templates.Data.Rows.Find(3).Delete();Sql(connection,"ALTER TABLE items ENGINE=MyISAM");Check(Rejected(()=>ServerDataService.Save(templates)),"Template deletion accepted unprotected world exemplars");Check(Convert.ToInt32(Scalar(connection,"SELECT COUNT(*) FROM templates WHERE id=3"))==1,"Refused template deletion persisted partially");Sql(connection,"ALTER TABLE items ENGINE=InnoDB");ServerDataService.Save(templates);Check(Convert.ToInt32(Scalar(connection,"SELECT COUNT(*) FROM templates WHERE id=3"))==0,"Unused template cannot be removed");
            var craft=ServerDataService.Load(ServerResourceKind.Crafts);var recipe=ServerDataService.Add(craft);recipe["id"]=1;recipe["craft"]="2*5";ServerDataService.Save(craft);recipe["craft"]="999*5";Check(Rejected(()=>ServerDataService.Save(craft)),"Missing ingredient accepted");recipe["craft"]="2*0";Check(Rejected(()=>ServerDataService.Save(craft)),"Zero recipe quantity accepted");
            var drops=ServerDataService.Load(ServerResourceKind.Drops);var drop=ServerDataService.Add(drops);drop["monsterId"]=1;drop["objectId"]=2;drop["percentGrade1"]=12.5m;ServerDataService.Save(drops);Check(Convert.ToDecimal(Scalar(connection,"SELECT percentGrade1 FROM drops"))==12.5m,"AUTO_INCREMENT drop CRUD failed");drop["percentGrade1"]=101;Check(Rejected(()=>ServerDataService.Save(drops)),"Invalid drop probability accepted");
            var cells=ServerDataService.Load(ServerResourceKind.MapTriggers,100,17);cells.Data.Rows[0]["ActionsArgs"]="300,0";ServerDataService.Save(cells);Check(Convert.ToString(Scalar(connection,"SELECT ActionsArgs FROM cells"))=="300,0","Table without unique index cannot be edited");
            Sql(connection,"INSERT INTO cells SELECT * FROM cells");cells=ServerDataService.Load(ServerResourceKind.MapTriggers,100,17);cells.Data.Rows[0]["ActionsArgs"]="400,0";Check(Rejected(()=>ServerDataService.Save(cells)),"Ambiguous original rows changed");
            var doors=ServerDataService.Load(ServerResourceKind.InteractiveDoors);doors.Data.Rows[0]["time"]=60;ServerDataService.Save(doors);Check(Convert.ToInt32(Scalar(connection,"SELECT time FROM doors"))==60,"Nullable keyless mechanism editing failed");
            string path=Path.Combine(TestPaths.Work,"all-data.sql");Check(ServerDataService.ExportAllSql("Template",path)==2,"Full SQL export count");Check(File.ReadAllText(path).Contains("CONVERT(X'"),"Full SQL export lost typed string escaping");File.Delete(path);
            TestInventoryUpdate(connection);
            TestSuppliedSchemas(connection);
            Console.WriteLine("OK: templates, sets and reciprocal membership, recipes, drops with AUTO_INCREMENT, keyless/ambiguous rows, full SQL and inventory editing/conflict guards");
        }
        finally{Sql(server,"DROP DATABASE IF EXISTS `"+database+"`");}
    }
    private static void TestSuppliedSchemas(string connection)
    {
        var kinds=new[]{ServerResourceKind.NpcQuestions,ServerResourceKind.NpcResponses,ServerResourceKind.Quests,ServerResourceKind.QuestSteps,ServerResourceKind.QuestObjectives,ServerResourceKind.EndFightActions,ServerResourceKind.ObjectActions,ServerResourceKind.Dungeons,ServerResourceKind.Maps};
        foreach(var resource in EditorFixtures.Resources.Where(info=>kinds.Contains(info.Kind)))
        {
            string table=resource.Kind==ServerResourceKind.Maps?"maps":"fixture_"+resource.Table;Sql(connection,"CREATE TABLE `"+table+"` ("+EditorFixtures.Schema(resource)+") ENGINE=InnoDB");JsonManager.Auth_dico[resource.Key]=table;
            var snapshot=ServerDataService.Load(resource.Kind);var row=ServerDataService.Add(snapshot);EditorFixtures.Populate(resource,row);Check(ServerDataService.Save(snapshot)==1,"Insert failed for "+resource.Table);
            string column=snapshot.Data.Columns.Cast<DataColumn>().Where(c=>c.DataType==typeof(string)&&c.ColumnName!="id").Select(c=>c.ColumnName).FirstOrDefault();
            if(column!=null){row[column]="Azur";Check(ServerDataService.Save(snapshot)==1,"Edit failed for "+resource.Table);}
            Check(ServerDataService.Load(resource.Kind).Data.Rows.Count==1,"Reload failed for "+resource.Table);
            if(resource.Kind==ServerResourceKind.Maps)
            {
                var map=new Tool_Editor.maps.data.Map{ID=100,DateMap="0612041200",Width=3,Height=4,Capabilities=98,X=-62,Y=-103,SubArea=320,Cells=Enumerable.Range(0,18).Select(index=>new Tool_Editor.maps.data.CellsData{ID=index}).ToArray(),HasProjectCells=true};
                string path=Path.Combine(TestPaths.Work,"map-schema-export.sql");
                typeof(ServerDataForm).Assembly.GetType("Outil_Azur_complet.maps.MapSqlExport").GetMethod("Export",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{map,path});
                string sql=File.ReadAllText(path);Check(sql.Contains("`maps`")&&sql.Contains("`heigth`"),"Map SQL ignores the configured Kryone schema");
                Sql(connection,sql);
                Check(Convert.ToInt32(Scalar(connection,"SELECT width FROM `"+table+"`"))==3 &&
                    Convert.ToInt32(Scalar(connection,"SELECT heigth FROM `"+table+"`"))==4 &&
                    Convert.ToInt32(Scalar(connection,"SELECT capabilities FROM `"+table+"`"))==98 &&
                    Convert.ToString(Scalar(connection,"SELECT date FROM `"+table+"`"))=="0612041200" &&
                    Convert.ToString(Scalar(connection,"SELECT mappos FROM `"+table+"`"))=="-62,-103,320" &&
                    Convert.ToInt32(Scalar(connection,"SELECT LENGTH(mapData) FROM `"+table+"`"))==180,
                    "Generated map SQL changes the client data or server metadata");
                string assets=Path.Combine(TestPaths.ApplicationBin,"ressources","maps");
                Tool_Editor.maps.managers.SearchManager.SearchGrounds(Path.Combine(assets,"sols"),new System.Windows.Forms.TreeNode("Sols"));
                Tool_Editor.maps.managers.SearchManager.SearchObject(Path.Combine(assets,"objets"),new System.Windows.Forms.TreeNode("Objets"));
                string fixture=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"..","Fixtures","10000_0612041200.swf");
                var nowel=Tool_Editor.maps.managers.MapSwfSerializer.Load(fixture);nowel.Load();
                nowel.X=-66;nowel.Y=-102;nowel.SubArea=320;
                string nowelPath=Path.Combine(TestPaths.Work,"nowel-10000-kryone.sql");
                typeof(ServerDataForm).Assembly.GetType("Outil_Azur_complet.maps.MapSqlExport").GetMethod("Export",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{nowel,nowelPath});
                string nowelSql=File.ReadAllText(nowelPath);
                Check(nowelSql.Contains("INSERT INTO `maps`") && nowelSql.Contains("`mapData`"),"The supplied Nowel map did not generate schema-aware SQL");
                Sql(connection,nowelSql);
                Check(Convert.ToString(Scalar(connection,"SELECT mapData FROM `maps` WHERE id=10000"))==nowel.MapData &&
                    Convert.ToString(Scalar(connection,"SELECT date FROM `maps` WHERE id=10000"))=="0612041200" &&
                    Convert.ToInt32(Scalar(connection,"SELECT capabilities FROM `maps` WHERE id=10000"))==98 &&
                    Convert.ToString(Scalar(connection,"SELECT mappos FROM `maps` WHERE id=10000"))=="-66,-102,320" &&
                    Convert.ToInt32(Scalar(connection,"SELECT LENGTH(mapData) FROM `maps` WHERE id=10000"))==4790,
                    "The generated Nowel SQL cannot recreate the client data and server coordinates");
                Sql(connection,"DELETE FROM `maps` WHERE id=10000");
                snapshot=ServerDataService.Load(resource.Kind);row=snapshot.Data.Rows[0];
            }
            row.Delete();Check(ServerDataService.Save(snapshot)==1 && Convert.ToInt32(Scalar(connection,"SELECT COUNT(*) FROM `"+table+"`"))==0,"Delete failed for "+resource.Table);
        }
        Console.WriteLine("OK: CRUD of dialogues, quests, objectives, end-fight actions, object actions, dungeons and maps using the supplied schemas; generated map SQL applied");
    }
    private static void TestInventoryUpdate(string connection)
    {
        var assembly=typeof(ServerDataForm).Assembly;Type changeType=assembly.GetType("Outil_Azur_complet.editeur_items.InventoryChange"),kindType=assembly.GetType("Outil_Azur_complet.editeur_items.InventoryChangeKind"),service=assembly.GetType("Outil_Azur_complet.editeur_items.InventoryUpdateService");
        object change=Activator.CreateInstance(changeType,true);Set(change,"CharacterId",1);Set(change,"CharacterName","Alice");Set(change,"Kind",Enum.Parse(kindType,"Update"));Set(change,"ItemGuid",100);Set(change,"Quantity",2);Set(change,"Position",-1);Set(change,"Puit",5);Set(change,"Stats","7d#2#0a#0#1d9+1");Set(change,"OriginalTemplate",1);Set(change,"OriginalQuantity",1);Set(change,"OriginalPosition",-1);Set(change,"OriginalPuit",0);Set(change,"OriginalStats","7d#01#0a#0#1d10+0");
        var changes=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(changeType));changes.Add(change);
        Action apply=()=>{try{service.GetMethod("Apply",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{changes});}catch(TargetInvocationException error){throw error.InnerException;}};
        Sql(connection,"UPDATE players SET logged=1");Check(Rejected(apply),"Online inventory edit accepted");Sql(connection,"UPDATE players SET logged=0");apply();Check(Convert.ToInt32(Scalar(connection,"SELECT qua FROM items WHERE guid=100"))==2,"Inventory quantity not edited");Check(Convert.ToInt32(Scalar(connection,"SELECT puit FROM items WHERE guid=100"))==5,"Inventory forge well not edited");Check(Rejected(apply),"Stale inventory item overwrote changes");
        Set(change,"OriginalQuantity",2);Set(change,"OriginalPuit",5);Set(change,"OriginalStats","7d#2#0a#0#1d9+1");Set(change,"Position",1);Set(change,"Quantity",1);Check(Rejected(apply),"Amulet equipped into weapon position");Set(change,"Position",0);apply();Check(Convert.ToInt32(Scalar(connection,"SELECT pos FROM items WHERE guid=100"))==0,"Valid inventory equipment not edited");
    }
}
