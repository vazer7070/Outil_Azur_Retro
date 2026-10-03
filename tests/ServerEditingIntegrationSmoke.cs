using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using MySql.Data.MySqlClient;
using Outil_Azur_complet;
using Outil_Azur_complet.editeur_perso;
using Tools_protocol.Json;
using Tools_protocol.Query;

internal static class ServerEditingIntegrationSmoke
{
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender,args) => {
            string name=new AssemblyName(args.Name).Name, path=Path.Combine(TestPaths.ApplicationBin,name+".dll");
            if(!File.Exists(path))path=Path.Combine(TestPaths.ApplicationBin,name+".exe");
            return File.Exists(path)?Assembly.LoadFrom(path):null;
        };
        Run();
    }
    private static string Connection(string database)
    { return new MySqlConnectionStringBuilder { Server="127.0.0.1",Port=43306,UserID="root",Database=database,ConnectionTimeout=3 }.ConnectionString; }
    private static void Sql(string connection,string sql)
    { using(var c=new MySqlConnection(connection))using(var cmd=new MySqlCommand(sql,c)){ c.Open();cmd.ExecuteNonQuery(); } }
    private static object Scalar(string connection,string sql)
    { using(var c=new MySqlConnection(connection))using(var cmd=new MySqlCommand(sql,c)){ c.Open();return cmd.ExecuteScalar(); } }
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static bool Rejected(Action action)
    {try {action();return false;}catch(FormatException){return true;}catch(InvalidOperationException){return true;}catch(MySqlException){return true;} }
    private static void Run()
    {
        string database="azur_edit_"+Guid.NewGuid().ToString("N").Substring(0,8), server=Connection(""), connection=Connection(database);
        try
        {
            Sql(server,"CREATE DATABASE `"+database+"` CHARACTER SET utf8mb4");
            Sql(connection,"CREATE TABLE accounts(guid INT PRIMARY KEY,logged INT NOT NULL) ENGINE=InnoDB;"+
                "CREATE TABLE players(id INT PRIMARY KEY,name VARCHAR(40),account INT,logged INT,spells TEXT,jobs TEXT) ENGINE=InnoDB;"+
                "INSERT INTO accounts VALUES(1,0); INSERT INTO players VALUES(1,'Alice',1,0,'10;2;8,11;3;-1','1,100,keep;2,200');"+
                "CREATE TABLE sorts(id INT PRIMARY KEY,nom VARCHAR(100),lvl1 TEXT NOT NULL,lvl2 TEXT NOT NULL,lvl3 TEXT NOT NULL,lvl4 TEXT NOT NULL,lvl5 TEXT NOT NULL,lvl6 TEXT NOT NULL) ENGINE=InnoDB;"+
                "INSERT INTO sorts VALUES(10,'Sort A','-1','-1','-1','-1','-1','-1'),(11,'Sort B','-1','-1','-1','-1','-1','-1');"+
                "CREATE TABLE jobs_data(id INT PRIMARY KEY,name VARCHAR(100) NOT NULL,tools VARCHAR(300) NOT NULL,crafts TEXT NOT NULL,skills VARCHAR(255),AP TEXT NOT NULL) ENGINE=InnoDB;"+
                "INSERT INTO jobs_data VALUES(1,'Job A','1','2',NULL,'ap'),(2,'Job B','3','4','skills','ap');"+
                "CREATE TABLE npc_template(id INT PRIMARY KEY) ENGINE=InnoDB; INSERT INTO npc_template VALUES(10);"+
                "CREATE TABLE npcs(mapid INT,npcid INT,cellid INT,orientation INT,isMovable TINYINT NOT NULL DEFAULT 0,PRIMARY KEY(mapid,npcid,cellid)) ENGINE=InnoDB;"+
                "INSERT INTO npcs VALUES(100,10,0,1,0),(200,10,2,1,0);");
            DatabaseManager.ConnectionString=connection; InitializeForm.EMUSELECT="Kryone";
            JsonManager.Auth_dico.Clear();
            foreach(var pair in new[]{new[]{"comptes","accounts"},new[]{"perso","players"},new[]{"sort","sorts"},new[]{"metiers","jobs_data"},new[]{"npcs","npcs"},new[]{"npc_template","npc_template"}})JsonManager.Auth_dico[pair[0]]=pair[1];
            var spells=CharacterSkillsService.Load(1,CharacterSkillKind.Spells);
            Check(spells.Entries.Count==2 && spells.Entries[0].Tail=="8","Spell positions lost");
            var entries=spells.Entries.Select(e=>new CharacterSkillEntry { Id=e.Id,Value=e.Value,Tail=e.Tail }).ToList(); entries[0].Value=6;
            CharacterSkillsService.Save(spells,entries);
            Check(Convert.ToString(Scalar(connection,"SELECT spells FROM players WHERE id=1"))=="10;6;8,11;3;-1","Spell update");
            Sql(connection,"UPDATE players SET spells='10;4;8' WHERE id=1");
            Check(Rejected(()=>CharacterSkillsService.Save(spells,entries)),"Stale spells overwrote data");
            var jobs=CharacterSkillsService.Load(1,CharacterSkillKind.Jobs);
            Check(jobs.Entries[0].Tail=="keep","Job parameters lost");
            entries=jobs.Entries.Select(e=>new CharacterSkillEntry { Id=e.Id,Value=e.Value,Tail=e.Tail }).ToList();entries[0].Value=500;
            Sql(connection,"UPDATE accounts SET logged=1");
            Check(Rejected(()=>CharacterSkillsService.Save(jobs,entries)),"Online account edited");
            Sql(connection,"UPDATE accounts SET logged=0; UPDATE players SET logged=1");
            Check(Rejected(()=>CharacterSkillsService.Save(jobs,entries)),"Online character edited");
            Sql(connection,"UPDATE players SET logged=0");
            CharacterSkillsService.Save(jobs,entries);
            Check(Convert.ToString(Scalar(connection,"SELECT jobs FROM players WHERE id=1"))=="1,500,keep;2,200","Job update");
            entries.Add(new CharacterSkillEntry { Id=999,Value=1 });
            Check(Rejected(()=>CharacterSkillsService.Save(jobs,entries)),"Unknown job allowed");
            Check(Convert.ToString(Scalar(connection,"SELECT jobs FROM players WHERE id=1"))=="1,500,keep;2,200","Failed skills update was not atomic");
            entries.RemoveAt(entries.Count-1); entries[1].Id=entries[0].Id;
            Check(Rejected(()=>CharacterSkillsService.Save(jobs,entries)),"Duplicate skill accepted");
            Sql(connection,"ALTER TABLE sorts ADD sprite INT NOT NULL DEFAULT 1, ADD spriteInfos VARCHAR(20) NOT NULL DEFAULT '0,0,0', ADD effectTarget VARCHAR(30) NOT NULL DEFAULT '', ADD type INT NOT NULL DEFAULT 0;");
            Tools_protocol.Kryone.Database.SpellsList.Load_Spells(); Tools_protocol.Kryone.Database.SpellsList.Load_Spells();
            Check(Tools_protocol.Kryone.Database.SpellsList.AllSpells.Count==2 && Tools_protocol.Kryone.Database.SpellsList.SpellsName.Count==2,"Spell schema without durer or repeated loading failed");
            Tools_protocol.Kryone.Database.JobsList.ANPE(); Tools_protocol.Kryone.Database.JobsList.ANPE();
            Check(Tools_protocol.Kryone.Database.JobsList.AllJobs.Count==2,"Job reloading duplicates resources");

            var resource=ServerDataService.Load(ServerResourceKind.Jobs);
            resource.Data.Rows[0]["name"]="O'Brien\\name\n"; resource.Data.Rows[1]["name"]="Updated B";
            Sql(connection,"UPDATE jobs_data SET AP='external' WHERE id=2");
            Check(Rejected(()=>ServerDataService.Save(resource)),"Stale resource allowed");
            Check(Convert.ToString(Scalar(connection,"SELECT name FROM jobs_data WHERE id=1"))=="Job A","Partial resource transaction persisted");
            resource=ServerDataService.Load(ServerResourceKind.Jobs);resource.Data.Rows[0]["name"]="O'Brien\\name\n";
            string path=Path.Combine(TestPaths.Work,"resource-changes.sql");ServerDataService.ExportSql(resource,path);
            Sql(connection,File.ReadAllText(path));
            Check(Convert.ToString(Scalar(connection,"SELECT name FROM jobs_data WHERE id=1"))=="O'Brien\\name\n","SQL export escaping");
            resource=ServerDataService.Load(ServerResourceKind.Jobs);var added=ServerDataService.Add(resource);added["id"]=3;added["name"]="New Job";
            Check(added["skills"]==DBNull.Value,"NULL default discarded");
            Check(ServerDataService.Save(resource)==1,"Resource insertion");
            resource.Data.Rows.Find(3).Delete();ServerDataService.Save(resource);
            Check(Convert.ToInt32(Scalar(connection,"SELECT COUNT(*) FROM jobs_data WHERE id=3"))==0,"Resource deletion");
            var npc=ServerDataService.Load(ServerResourceKind.Npcs,100,17);
            Check(((System.Collections.Generic.Dictionary<string,string>)typeof(ServerDataSnapshot).GetField("ReferenceNames",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(npc)).ContainsKey("10"),"NPC choice list is absent");
            Check(npc.Data.Rows.Count==1,"Map scope leaked another map");
            npc.Data.Rows[0]["cellid"]=16;ServerDataService.Save(npc);
            Check(Convert.ToInt32(Scalar(connection,"SELECT cellid FROM npcs WHERE mapid=200"))==2,"Other map changed");
            npc.Data.Rows[0]["cellid"]=17;Check(Rejected(()=>ServerDataService.Save(npc)),"Out-of-map placement allowed");
            npc.Data.RejectChanges();npc.Data.Rows[0]["npcid"]=999;Check(Rejected(()=>ServerDataService.Save(npc)),"Missing NPC template allowed");
            Sql(connection,"ALTER TABLE jobs_data ENGINE=MyISAM");resource=ServerDataService.Load(ServerResourceKind.Jobs);resource.Data.Rows[0]["name"]="unsafe";
            Check(Rejected(()=>ServerDataService.Save(resource)),"MyISAM resources edited");
            Sql(connection,"ALTER TABLE players ENGINE=MyISAM");entries=new System.Collections.Generic.List<CharacterSkillEntry>{new CharacterSkillEntry{Id=1,Value=100}};
            Check(Rejected(()=>CharacterSkillsService.Save(jobs,entries)),"MyISAM skills edited");
            File.Delete(path);
            Console.WriteLine("OK: character spells/jobs, tail preservation, offline/conflict/transaction guards, resource CRUD/SQL, scoped NPC placement and MyISAM refusals");
        }
        finally {Sql(server,"DROP DATABASE IF EXISTS `"+database+"`");}
    }
}
