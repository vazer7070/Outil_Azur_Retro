using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using MySql.Data.MySqlClient;
using Outil_Azur_complet;
using Outil_Azur_complet.editeur_perso;
using Outil_Azur_complet.maps;
using Outil_Azur_complet.outil_recherche;
using Tools_protocol.Emulators;
using Tools_protocol.Json;
using Tools_protocol.Kryone.Database;
using Tools_protocol.Managers;
using Tools_protocol.Parser.XML;
using Tools_protocol.Query;

// StarLoco keeps players and item instances in its login database and the static
// resources in its game database. Every walkthrough below runs against the two
// supplied schemas, recreated empty, with synthetic rows only.
internal static class StarLocoSchemaIntegrationSmoke
{
    private static string Connection(string database)
    { return new MySqlConnectionStringBuilder { Server="127.0.0.1",Port=43306,UserID="root",Database=database,ConnectionTimeout=3 }.ConnectionString; }
    private static void Sql(string connection,string sql)
    { using(var c=new MySqlConnection(connection))using(var command=new MySqlCommand(sql,c)){c.Open();command.ExecuteNonQuery();} }
    private static object Scalar(string connection,string sql)
    { using(var c=new MySqlConnection(connection))using(var command=new MySqlCommand(sql,c)){c.Open();return command.ExecuteScalar();} }
    private static int Count(string connection,string table)
    { return Convert.ToInt32(Scalar(connection,"SELECT COUNT(*) FROM `"+table+"`")); }
    private static string Text(string connection,string sql)
    { return Convert.ToString(Scalar(connection,sql)); }
    private static int Number(string connection,string sql)
    { return Convert.ToInt32(Scalar(connection,sql)); }
    private static void Check(bool condition,string message)
    { if(!condition)throw new Exception(message); }
    private static object Call(Type type,string name,object instance,params object[] arguments)
    {
        try{return type.GetMethod(name,BindingFlags.Static|BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Invoke(instance,arguments);}
        catch(TargetInvocationException error){throw error.InnerException;}
    }
    private static void Refused(Action action,string message)
    {
        try{action();}
        catch(InvalidOperationException){return;}
        throw new Exception(message);
    }
    private static void RefusedFormat(Action action,string message)
    {
        try{action();}
        catch(InvalidOperationException){return;}
        catch(FormatException){return;}
        throw new Exception(message);
    }
    private static HashSet<string> Tables(string connection)
    {
        var tables=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using(var c=new MySqlConnection(connection))using(var command=new MySqlCommand("SHOW TABLES",c))
        {c.Open();using(var reader=command.ExecuteReader())while(reader.Read())tables.Add(reader.GetString(0));}
        return tables;
    }
    private static int LoadFixture(string connection,string name)
    {
        string fixture=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"..","Fixtures",name);
        string sql=File.ReadAllText(fixture);
        Check(!sql.Contains("INSERT INTO") && !sql.Contains("REPLACE INTO"),"The StarLoco schema fixture "+name+" contains data rows");
        Check(!sql.Contains("AUTO_INCREMENT="),"The StarLoco schema fixture "+name+" keeps AUTO_INCREMENT counters");
        int created=0;
        foreach(string block in sql.Split(new[]{"-- END TABLE"},StringSplitOptions.RemoveEmptyEntries))
        {
            int start=block.IndexOf("CREATE TABLE `",StringComparison.OrdinalIgnoreCase);
            if(start<0)continue;
            Sql(connection,block.Substring(start).Trim());created++;
        }
        return created;
    }
    // Populate only synthetic values. Required columns come from the fixture's metadata,
    // rather than relying on the server's permissive implicit defaults.
    private static void SeedRow(string connection,string table,Dictionary<string,object> overrides)
    {
        var values=new Dictionary<string,object>(overrides,StringComparer.OrdinalIgnoreCase);
        using(var c=new MySqlConnection(connection))
        {
            c.Open();
            using(var command=new MySqlCommand("SELECT COLUMN_NAME,DATA_TYPE,IS_NULLABLE,COLUMN_DEFAULT,EXTRA FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table ORDER BY ORDINAL_POSITION",c))
            {
                command.Parameters.AddWithValue("@table",table);
                using(var reader=command.ExecuteReader())while(reader.Read())
                {
                    string name=reader.GetString(0),type=reader.GetString(1),extra=reader.GetString(4);
                    if(values.ContainsKey(name)||extra.Contains("auto_increment")||reader.GetString(2)!="NO"||reader[3]!=DBNull.Value)continue;
                    bool numeric=new[]{"tinyint","smallint","mediumint","int","bigint","decimal","float","double","bit"}.Contains(type);
                    values.Add(name,numeric?(object)0:string.Empty);
                }
            }
            using(var command=new MySqlCommand("INSERT INTO `"+table+"` ("+string.Join(",",values.Keys.Select(k=>"`"+k+"`"))+") VALUES ("+string.Join(",",values.Select((v,i)=>"@p"+i))+")",c))
            {
                int i=0;foreach(var pair in values)command.Parameters.AddWithValue("@p"+i++,pair.Value??DBNull.Value);
                Check(command.ExecuteNonQuery()==1,"Synthetic row was not inserted into "+table);
            }
        }
    }
    private static void ApplyCharacter(int id,string field,object value)
    {
        ApplyCharacters(new Dictionary<int,Dictionary<string,object>>{{id,new Dictionary<string,object>{{field,value}}}});
    }
    private static void ApplyCharacters(Dictionary<int,Dictionary<string,object>> changes)
    {Call(typeof(editeur_perso),"ApplyCharacterChanges",null,"players",changes,CaptureOriginals(changes));}
    private static Dictionary<int,Dictionary<string,object>> CaptureOriginals(Dictionary<int,Dictionary<string,object>> changes)
    {
        var originals=new Dictionary<int,Dictionary<string,object>>();
        foreach(var entry in changes)
        {
            CharacterList character=CharacterList.PersoAll.Values.Single(p=>p.Id==entry.Key);
            var fields=new Dictionary<string,object>();
            foreach(string column in new[]{"name","account"}.Concat(entry.Value.Keys).Distinct())
                fields.Add(column,typeof(CharacterList).GetProperty(char.ToUpperInvariant(column[0])+column.Substring(1)).GetValue(character,null));
            originals.Add(entry.Key,fields);
        }
        return originals;
    }
    private static void ApplyAccounts(params object[][] changes)
    {
        Type editor=typeof(InitializeForm).Assembly.GetType("Outil_Azur_complet.editeur_compte.editeurcompte");
        Type change=editor.GetNestedType("PendingAccountChange",BindingFlags.NonPublic);
        Type pair=typeof(KeyValuePair<,>).MakeGenericType(typeof(string),change);
        Array prepared=Array.CreateInstance(pair,changes.Length);
        for(int i=0;i<changes.Length;i++)
        {
            object value=Activator.CreateInstance(change,true);
            change.GetProperty("AccountId").SetValue(value,Convert.ToUInt32(changes[i][0]),null);
            change.GetProperty("Field").SetValue(value,(string)changes[i][1],null);
            change.GetProperty("Value").SetValue(value,(string)changes[i][2],null);
            change.GetProperty("OriginalValue").SetValue(value,(string)changes[i][3],null);
            prepared.SetValue(Activator.CreateInstance(pair,new[]{(object)i.ToString(),value}),i);
        }
        Call(editor,"ApplyAccountChanges",null,"accounts",prepared);
    }
    private static void ApplyInventory(int id,string name,string kind,int guid,int template,int quantity)
    {
        ApplyInventoryChange(new Dictionary<string,object>{{"CharacterId",id},{"CharacterName",name},{"Kind",kind},{"ItemGuid",guid},{"TemplateId",template},{"Quantity",quantity}});
    }
    private static void ApplyInventoryChange(Dictionary<string,object> members)
    {
        Assembly app=typeof(InitializeForm).Assembly;
        Type service=app.GetType("Outil_Azur_complet.editeur_items.InventoryUpdateService");
        Type change=app.GetType("Outil_Azur_complet.editeur_items.InventoryChange");
        object value=Activator.CreateInstance(change,true);
        foreach(var member in members)
        {
            object data=member.Key=="Kind"?Enum.Parse(app.GetType("Outil_Azur_complet.editeur_items.InventoryChangeKind"),(string)member.Value):member.Value;
            PropertyInfo property=change.GetProperty(member.Key);
            if(property!=null)property.SetValue(value,data,null);
            else change.GetField(member.Key).SetValue(value,data);
        }
        Type listType=typeof(List<>).MakeGenericType(change);
        object list=Activator.CreateInstance(listType);
        listType.GetMethod("Add").Invoke(list,new[]{value});
        Call(service,"Apply",null,list);
    }
    private static Dictionary<string,string> ReferenceNames(ServerDataSnapshot snapshot)
    { return (Dictionary<string,string>)typeof(ServerDataSnapshot).GetField("ReferenceNames",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(snapshot); }
    private static Dictionary<string,Dictionary<string,string>> ReferenceLists(ServerDataSnapshot snapshot)
    { return (Dictionary<string,Dictionary<string,string>>)typeof(ServerDataSnapshot).GetField("ReferenceLists",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(snapshot); }
    private static Type ItemCreation
    { get { return typeof(InitializeForm).Assembly.GetType("Outil_Azur_complet.editeur_items.ItemCreationService"); } }
    private static Type Moderation
    { get { return typeof(InitializeForm).Assembly.GetType("Outil_Azur_complet.KryoneModerationService"); } }
    private static Type Inventory
    { get { return typeof(InitializeForm).Assembly.GetType("Outil_Azur_complet.editeur_items.InventoryUpdateService"); } }

    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve+=(sender,args)=>{
            string name=new AssemblyName(args.Name).Name,path=Path.Combine(TestPaths.ApplicationBin,name+".dll");
            if(!File.Exists(path))path=Path.Combine(TestPaths.ApplicationBin,name+".exe");
            return File.Exists(path)?Assembly.LoadFrom(path):null;
        };
        Run();
    }

    private static void Run()
    {
        string suffix=Guid.NewGuid().ToString("N").Substring(0,8);
        string login="azur_starloco_login_"+suffix,game="azur_starloco_game_"+suffix;
        string server=Connection(""),loginConnection=Connection(login),gameConnection=Connection(game);
        string configPath=Path.GetFullPath(@".\config.json");
        string work=Path.Combine(TestPaths.Work,"starloco-"+suffix);
        Directory.CreateDirectory(work);
        try
        {
            Check(Convert.ToString(Scalar(server,"SELECT @@GLOBAL.sql_mode")).Split(',').Contains("STRICT_TRANS_TABLES"),"The isolated MySQL server must use STRICT_TRANS_TABLES for the supplied-schema write checks");
            Sql(server,"CREATE DATABASE `"+login+"` CHARACTER SET utf8mb4");
            Sql(server,"CREATE DATABASE `"+game+"` CHARACTER SET utf8mb4");
            Check(LoadFixture(loginConnection,"starloco-login-schema.sql")==27,"Not all supplied StarLoco login tables were recreated");
            Check(LoadFixture(gameConnection,"starloco-game-schema.sql")==47,"Not all supplied StarLoco game tables were recreated");
            Check(Number(loginConnection,"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND ENGINE<>'InnoDB'")==0 && Number(gameConnection,"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND ENGINE<>'InnoDB'")==0,"The StarLoco fixtures must only use InnoDB");

            // Configuration as Azur writes it: Aname is the login schema, Wname the game schema, Emu StarLoco.
            JsonManager.RewriteConfig("127.0.0.1","root","",login,game,"StarLoco");
            Check(JsonManager.LectureConfig(configPath) && JsonManager.Aauth==login && JsonManager.Aworld==game && JsonManager.SearchConfig("emu")=="StarLoco","The StarLoco configuration could not be written and reread: "+JsonManager.LastError);
            string authPath=Path.Combine(work,"auth_tables.json"),worldPath=Path.Combine(work,"world_tables.json");
            JsonManager.WriteConfig("auth",authPath);JsonManager.WriteConfig("world",worldPath);
            Check(JsonManager.LectureAuth(authPath) && JsonManager.LectureWorld(worldPath),"Default table mappings cannot be read: "+JsonManager.LastError);
            DatabaseManager.ConnectionString=loginConnection;DatabaseManager2.ConnectionString=gameConnection;
            InitializeForm.EMUSELECT=JsonManager.SearchConfig("emu");
            EmulatorProfile profile=EmulatorRegistry.Current;
            Check(profile.Id=="StarLoco" && EmuManager.EMUSELECTED=="StarLoco" && InitializeForm.EMUSELECT=="StarLoco","The StarLoco profile was not selected from the configuration");
            // The profile carries the real names: the JSON mapping files are not consulted for StarLoco.
            JsonManager.Auth_dico["comptes"]="comptes_json";JsonManager.World_dico["items"]="items_json";
            Check(profile.Table("comptes")=="accounts" && profile.Table("items")=="world.entity.objects" && EmuManager.ReturnTable("perso","StarLoco")=="players","StarLoco table names came from the JSON mapping instead of the profile");
            JsonManager.Auth_dico["comptes"]="accounts";JsonManager.World_dico["items"]="items";
            Check(EmulatorRegistry.ConnectionFor("perso")==loginConnection && EmulatorRegistry.ConnectionFor("items")==loginConnection && EmulatorRegistry.ConnectionFor("sort")==gameConnection && EmulatorRegistry.ConnectionFor("enclos")==gameConnection,"StarLoco tables are not routed to the login/game connections");

            // Every table the profile declares exists in the database the profile assigns to it.
            var loginTables=Tables(loginConnection);var gameTables=Tables(gameConnection);
            int authCount=0,worldCount=0;
            foreach(string logical in profile.Tables)
            {
                string physical=profile.Table(logical);
                Check(QueryBuilder.IsIdentifier(physical),"StarLoco table name is not a safe identifier: "+physical);
                if(profile.Locate(logical)==TableLocation.Auth){Check(loginTables.Contains(physical),"StarLoco login lacks "+logical+" → "+physical);authCount++;}
                else{Check(gameTables.Contains(physical),"StarLoco game lacks "+logical+" → "+physical);worldCount++;}
            }
            Check(authCount==16 && worldCount==42,"Unexpected StarLoco profile coverage: "+authCount+" login + "+worldCount+" game tables");
            Check(!profile.KnowsTable("titres") && !profile.KnowsTable("paroli") && profile.Table("titres")=="","Kryone-only tables must stay unknown for StarLoco");

            // All 24 resource editors read their table from the game schema.
            foreach(ServerResourceKind kind in Enum.GetValues(typeof(ServerResourceKind)))
            {
                var snapshot=ServerDataService.Load(kind);
                Check(snapshot.Data!=null && snapshot.Data.Columns.Count>0 && snapshot.Data.Rows.Count==0,"Editor cannot read the supplied StarLoco game schema for "+kind);
            }
            Console.WriteLine("OK: 27 login + 47 game StarLoco tables recreated, 58 profile tables located, 24 resource editors read the game schema");

            TestAccounts(loginConnection);
            uint account=AccountList.AllAccount["azur-starloco"].Guid,second=AccountList.AllAccount["azur-starloco-md5"].Guid;
            SeedRow(gameConnection,"item_template",new Dictionary<string,object>{{"id",7},{"name","Azur StarLoco potion"},{"statsTemplate","76#1#0#0#0d0+1"},{"type",12}});
            foreach(var row in new[]{new{Id=1,Name="AzurStar",Account=(int)account,Items=""},new{Id=2,Name="AzurStarOther",Account=(int)account,Items="100|"},new{Id=3,Name="AzurStarSecond",Account=(int)second,Items="101|"}})
                SeedRow(loginConnection,"players",new Dictionary<string,object>{{"id",row.Id},{"name",row.Name},{"account",row.Account},{"logged",0},{"class",1},{"level",1},{"size",100},{"gfx",1},{"color1",-1},{"color2",-1},{"color3",-1},{"objets",row.Items},{"server",1}});
            Sql(loginConnection,"INSERT INTO `world.entity.objects` (`id`,`template`,`quantity`,`position`,`stats`,`puit`) VALUES (100,7,2,-1,'',0),(101,7,1,-1,'',0)");
            CharacterList.AllPerso();
            Check(CharacterList.PersoCount==3 && CharacterList.Listing("AzurStar").Account==(int)account && CharacterList.IdAccount[2]=="AzurStarOther","StarLoco players were not loaded from login");
            TestCharacterWrites(loginConnection,account);
            TestSkills(loginConnection,gameConnection);
            TestInventory(loginConnection,gameConnection);
            TestItemCreation(loginConnection,gameConnection);
            TestResourceEditors(loginConnection,gameConnection,work);
            TestSearch(loginConnection,gameConnection);
            TestPlacements(gameConnection);
            TestBotExport(loginConnection,gameConnection,work);
            TestAccountEditing(loginConnection,account,second);
            Console.WriteLine("OK: StarLoco accounts, characters, skills across login/game, inventory in login.world.entity.objects, item creation, resource editors, search caches, placements, XML export and moderation");
        }
        finally
        {
            Sql(server,"DROP DATABASE IF EXISTS `"+login+"`");
            Sql(server,"DROP DATABASE IF EXISTS `"+game+"`");
            if(File.Exists(configPath))File.Delete(configPath);
        }
    }

    private static void TestAccounts(string login)
    {
        AccountList.AllAccounts();
        Check(AccountList.AllAccount.Count==0,"A fresh StarLoco login schema should have no account");
        AccountList.CreateAccount("azur-starloco",2,"synthetic-password","Synthetic question","Synthetic answer");
        AccountList.CreateAccount("azur-starloco-md5",1,"synthetic-password","Synthetic question","Synthetic answer");
        Check(AccountList.AllAccount.Count==2 && AccountList.AccountListCount==2 && CharacterList.IdCompte.Count==2,"New StarLoco account caches were not updated");
        Check(AccountList.AllAccount["azur-starloco"].Pass==AccountList.SHA51("synthetic-password") && AccountList.AllAccount["azur-starloco"].Guid>0,"The SHA512 password was not stored in StarLoco accounts");
        AccountList.AllAccounts();
        Check(AccountList.AllAccount.Count==2 && AccountList.AllAccount["azur-starloco"].Question=="Synthetic question" && AccountList.AllAccount["azur-starloco"].Pseudo=="azur-starloco" && AccountList.AllAccount["azur-starloco"].Banned==0,"Accounts could not be reloaded from the StarLoco login schema");
        // StarLoco has neither pass_no_crypt nor banRaison: only its own mandatory fields are filled, in strict SQL mode.
        Check(Number(login,"SELECT COUNT(*) FROM accounts WHERE lastConnectionDate='' AND lastIP='' AND friends='' AND enemy='' AND lastConnectDay='' AND heurevote=0 AND bannedTime=0 AND rules=1 AND admin=0")==2,"Mandatory StarLoco account fields were not initialized safely");
        bool duplicate=false;
        try{AccountList.CreateAccount("azur-starloco",2,"other-password","q","r");}
        catch(MySqlException){duplicate=true;}
        catch(InvalidOperationException){duplicate=true;}
        Check(duplicate && Count(login,"accounts")==2 && AccountList.AllAccount.Count==2,"Duplicate StarLoco account creation changed rows or cache");
        Sql(login,"ALTER TABLE accounts ENGINE=MyISAM");
        try{Refused(()=>AccountList.CreateAccount("azur-myisam",2,"synthetic-password","q","r"),"StarLoco account creation accepted MyISAM");}
        finally{Sql(login,"ALTER TABLE accounts ENGINE=InnoDB");}
        // The account editor reads StarLoco through the Kryone data model.
        var rows=EmuManager.GetAllAccountPropertiesForEmulator("StarLoco");
        Check(rows.Count==2 && rows.Any(entry=>Convert.ToString(entry["Account"])=="azur-starloco-md5"),"The account editor could not list StarLoco accounts");
    }

    private static void TestCharacterWrites(string login,uint account)
    {
        ApplyCharacters(new Dictionary<int,Dictionary<string,object>>{{1,new Dictionary<string,object>{{"color1",0x123456},{"color3",0xABCDEF},{"savepos","100,260"}}}});
        Check(Number(login,"SELECT color1 FROM players WHERE id=1")==0x123456 && Number(login,"SELECT color3 FROM players WHERE id=1")==0xABCDEF && Text(login,"SELECT savepos FROM players WHERE id=1")=="100,260","StarLoco character colors and save position were not saved");
        CharacterList.AllPerso();
        ApplyCharacter(1,"name","AzurStarRenamed");
        Check(Text(login,"SELECT name FROM players WHERE id=1")=="AzurStarRenamed","StarLoco character rename was not saved");
        CharacterList.AllPerso();
        ApplyCharacter(1,"name","AzurStar");
        CharacterList.AllPerso();
        Sql(login,"UPDATE accounts SET logged=1 WHERE guid="+account);
        Refused(()=>ApplyCharacter(1,"color1",1),"A StarLoco character on an online account was modified");
        Sql(login,"UPDATE accounts SET logged=0 WHERE guid="+account);
        Sql(login,"UPDATE players SET logged=1 WHERE id=1");
        Refused(()=>ApplyCharacter(1,"color1",1),"An online StarLoco character was modified");
        // players.logged accepts NULL in StarLoco: an unknown state is reported and writes are refused.
        Sql(login,"UPDATE players SET logged=NULL WHERE id=1");
        CharacterList.AllPerso();
        Check(CharacterList.Listing("AzurStar").Logged==-1,"Nullable StarLoco logged state was not loaded as unknown");
        Refused(()=>ApplyCharacter(1,"color1",1),"A StarLoco character with unknown logged state was modified");
        Sql(login,"UPDATE players SET logged=0 WHERE id=1");
        CharacterList.AllPerso();
        Refused(()=>ApplyCharacter(1,"account",999999),"A StarLoco character could be moved to a nonexistent account");
        Refused(()=>ApplyCharacter(1,"name","AzurStarOther"),"A StarLoco character could take an existing name");
        Check(Number(login,"SELECT color1 FROM players WHERE id=1")==0x123456 && Text(login,"SELECT name FROM players WHERE id=1")=="AzurStar","Refused StarLoco character writes mutated the row");
        Sql(login,"ALTER TABLE players ENGINE=MyISAM");
        try{Refused(()=>ApplyCharacter(1,"color1",1),"StarLoco character writes accepted MyISAM");}
        finally{Sql(login,"ALTER TABLE players ENGINE=InnoDB");}
        RefusedFormat(()=>ApplyCharacter(1,"name",new string('n',31)),"A StarLoco character name exceeding varchar(30) was accepted");
    }

    private static void TestSkills(string login,string game)
    {
        // Jobs and spells live in game while the character lives in login: the catalogue is read across databases.
        SeedRow(game,"jobs_data",new Dictionary<string,object>{{"id",1},{"name","Azur StarLoco job"}});
        SeedRow(game,"sorts",new Dictionary<string,object>{{"id",1},{"nom","Azur StarLoco spell"},{"lvl1","-1"},{"lvl2","-1"},{"lvl3","-1"},{"lvl4","-1"},{"lvl5","-1"},{"lvl6","-1"}});
        var jobs=CharacterSkillsService.Load(1,CharacterSkillKind.Jobs);
        Check(jobs.CharacterName=="AzurStar" && jobs.Choices.Count==1 && jobs.Choices[0].Id==1 && jobs.Choices[0].Name=="Azur StarLoco job","The StarLoco jobs catalogue in game could not be read from the login character");
        Check(CharacterSkillsService.Save(jobs,new[]{new CharacterSkillEntry{Id=1,Value=1200}})=="1,1200" && Text(login,"SELECT jobs FROM players WHERE id=1")=="1,1200","StarLoco character jobs were not saved");
        RefusedFormat(()=>CharacterSkillsService.Save(jobs,new[]{new CharacterSkillEntry{Id=99,Value=1}}),"A job missing from game.jobs_data was accepted");
        Check(Text(login,"SELECT jobs FROM players WHERE id=1")=="1,1200","A refused job list changed the row");
        var spells=CharacterSkillsService.Load(1,CharacterSkillKind.Spells);
        Check(spells.Choices.Count==1 && spells.Choices[0].Name=="Azur StarLoco spell","The StarLoco spells catalogue in game could not be read from the login character");
        Check(CharacterSkillsService.Save(spells,new[]{new CharacterSkillEntry{Id=1,Value=3}})=="1;3" && Text(login,"SELECT spells FROM players WHERE id=1")=="1;3","StarLoco character spells were not saved");
        CharacterList.AllPerso();
        CharacterList.GetSpells("AzurStar");
        Check(SpellsList.SpellsShow.Single()=="Azur StarLoco spell - niveau: 3","The character spell list did not resolve names from game.sorts");
    }

    private static void TestInventory(string login,string game)
    {
        CharacterList.GetInventory("AzurStarOther");
        Check(CharacterList.InventoryLoadError==null && CharacterList.ItemsPerso.Single()=="Azur StarLoco potion (100) x2","login.world.entity.objects was not read with the StarLoco column names");
        Check(Call(Inventory,"UnavailableReason",null)==null,"Inventory editing reported unavailable for StarLoco");
        Check(((ICollection)Call(Inventory,"LoadTemplateChoices",null)).Count==1,"Template choices were not read from game.item_template");
        ApplyInventory(1,"AzurStar","Add",0,7,3);
        string inventory=Text(login,"SELECT objets FROM players WHERE id=1");
        int guid=int.Parse(inventory.TrimEnd('|'));
        Check(guid==102 && Number(login,"SELECT quantity FROM `world.entity.objects` WHERE id="+guid)==3 && Number(login,"SELECT position FROM `world.entity.objects` WHERE id="+guid)==-1 && Text(login,"SELECT stats FROM `world.entity.objects` WHERE id="+guid)=="76#1#0#0#0d0+1","Inventory insertion did not create the StarLoco item instance");
        ApplyInventory(1,"AzurStar","Remove",guid,0,1);
        Check(Number(login,"SELECT quantity FROM `world.entity.objects` WHERE id="+guid)==2,"Partial removal did not update the StarLoco quantity column");
        ApplyInventoryChange(new Dictionary<string,object>{{"CharacterId",1},{"CharacterName","AzurStar"},{"Kind","Update"},{"ItemGuid",guid},{"TemplateId",7},{"Quantity",5},{"Position",-1},{"Puit",0},{"Stats","76#1#0#0#0d0+1"},{"OriginalTemplate",7},{"OriginalQuantity",2},{"OriginalPosition",-1},{"OriginalPuit",0},{"OriginalStats","76#1#0#0#0d0+1"}});
        Check(Number(login,"SELECT quantity FROM `world.entity.objects` WHERE id="+guid)==5,"Item update did not write the StarLoco quantity column");
        Refused(()=>ApplyInventoryChange(new Dictionary<string,object>{{"CharacterId",1},{"CharacterName","AzurStar"},{"Kind","Update"},{"ItemGuid",guid},{"TemplateId",7},{"Quantity",6},{"Position",-1},{"Puit",0},{"Stats","76#1#0#0#0d0+1"},{"OriginalTemplate",7},{"OriginalQuantity",2},{"OriginalPosition",-1},{"OriginalPuit",0},{"OriginalStats","76#1#0#0#0d0+1"}}),"A stale StarLoco item was overwritten");
        Refused(()=>ApplyInventory(1,"AzurStar","Remove",guid,0,9),"More items than the StarLoco quantity were removed");
        Check(Number(login,"SELECT quantity FROM `world.entity.objects` WHERE id="+guid)==5 && Text(login,"SELECT objets FROM players WHERE id=1")==inventory,"Refused inventory writes mutated StarLoco rows");
        CharacterList.AllPerso();CharacterList.GetInventory("AzurStar");
        Check(CharacterList.InventoryLoadError==null && CharacterList.ItemsPerso.Single()=="Azur StarLoco potion ("+guid+") x5","The StarLoco inventory could not be reloaded");
        Sql(login,"ALTER TABLE `world.entity.objects` ENGINE=MyISAM");
        try{Refused(()=>ApplyInventory(1,"AzurStar","Remove",guid,0,1),"StarLoco inventory writes accepted MyISAM");}
        finally{Sql(login,"ALTER TABLE `world.entity.objects` ENGINE=InnoDB");}
    }

    private static void TestItemCreation(string login,string game)
    {
        string[] values={"900","12","Azur StarLoco item","1","","0","-1","0","","","0","0","0"};
        string template=(string)Call(ItemCreation,"BuildTemplateQuery",null,"item_template",values,false,false);
        Check(template.StartsWith("INSERT INTO `item_template`(") && template.Contains("`exchangesObject`") && !template.Contains("`exchangeable`") && !template.Contains("`doplons`"),"The template query does not match the StarLoco item_template columns: "+template);
        // StarLoco has no exchangeable column: the option cannot be stored and is refused rather than dropped.
        Refused(()=>Call(ItemCreation,"BuildTemplateQuery",null,"item_template",values,true,false),"The exchangeable option was silently ignored for StarLoco");
        string item=(string)Call(ItemCreation,"BuildItemQuery",null,"world.entity.objects",900,500);
        Check(item.StartsWith("INSERT INTO `world.entity.objects`(`id`,`template`,`quantity`,`position`,`stats`,`puit`) VALUES ('500','900','0','-1','','0')"),"The item instance query does not use the StarLoco column names: "+item);
        SeedRow(game,"itemsets",new Dictionary<string,object>{{"ID",3},{"name","Azur StarLoco set"}});
        var queries=new Dictionary<string,string>
        {
            {"template",template},{"item",item},
            {"craft",QueryBuilder.InsertIntoQuery("crafts",new[]{"id","craft"},new[]{"900","7*2"},"")},
            {"pano",(string)Call(ItemCreation,"BuildPanoplyQuery",null,"itemsets","items","name","Azur StarLoco set",900)}
        };
        Call(ItemCreation,"Inject",null,queries,900,500);
        Check(Text(game,"SELECT name FROM item_template WHERE id=900")=="Azur StarLoco item" && Number(login,"SELECT COUNT(*) FROM `world.entity.objects` WHERE id=500 AND template=900 AND quantity=0 AND position=-1")==1 && Text(game,"SELECT craft FROM crafts WHERE id=900")=="7*2" && Text(game,"SELECT items FROM itemsets WHERE ID=3")=="900","Item creation did not write across the StarLoco login and game databases");
        Refused(()=>Call(ItemCreation,"Inject",null,queries,900,500),"A duplicate StarLoco template or item identifier was accepted");
        Check(Count(game,"item_template")==2 && Count(login,"world.entity.objects")==4,"A refused creation changed StarLoco rows");
    }

    private static void TestResourceEditors(string login,string game,string work)
    {
        // Spells: add, reread, modify and export SQL against game.sorts (duration instead of durer).
        var spells=ServerDataService.Load(ServerResourceKind.Spells);
        Check(spells.Data.Rows.Count==1 && spells.Data.Columns.Contains("duration") && !spells.Data.Columns.Contains("durer"),"The StarLoco spells table was not read");
        DataRow spell=ServerDataService.Add(spells);
        Check(Convert.ToInt32(spell["id"])==2 && Convert.ToString(spell["lvl1"])=="-1","The new StarLoco spell was not prepared");
        spell["nom"]="Azur StarLoco spell 2";
        Check(ServerDataService.Save(spells)==1 && Text(game,"SELECT nom FROM sorts WHERE id=2")=="Azur StarLoco spell 2" && Number(game,"SELECT duration FROM sorts WHERE id=2")==800,"The StarLoco spell was not inserted with the schema defaults");
        spells=ServerDataService.Load(ServerResourceKind.Spells);
        DataRow second=spells.Data.Rows.Cast<DataRow>().Single(row=>Convert.ToInt32(row["id"])==2);
        second["duration"]=1200;
        Check(ServerDataService.Save(spells)==1 && Number(game,"SELECT duration FROM sorts WHERE id=2")==1200,"The StarLoco spell duration was not updated");
        second["duration"]=1500;
        string exportPath=Path.Combine(work,"sorts-changes.sql");
        ServerDataService.ExportSql(spells,exportPath);
        string export=File.ReadAllText(exportPath);
        Check(export.Contains("UPDATE `sorts` SET") && export.Contains("1500") && Number(game,"SELECT duration FROM sorts WHERE id=2")==1200,"The SQL export of StarLoco spell changes is wrong");
        spells.Data.RejectChanges();
        string fullExport=Path.Combine(work,"sorts-all.sql");
        Check(ServerDataService.ExportAllSql("sort",fullExport)==2 && File.ReadAllText(fullExport).Contains("INSERT INTO `sorts`"),"The full export of game.sorts failed");
        string itemsExport=Path.Combine(work,"objects-all.sql");
        Check(ServerDataService.ExportAllSql("items",itemsExport)==4 && File.ReadAllText(itemsExport).Contains("INSERT INTO `world.entity.objects`"),"The full export of login.world.entity.objects failed");

        // Maps: the StarLoco maps table has no cells, cases nor background column.
        var maps=ServerDataService.Load(ServerResourceKind.Maps);
        Check(maps.Data.Rows.Count==0 && !maps.Data.Columns.Contains("background") && maps.Data.Columns.Contains("sniffed"),"The StarLoco maps table was not read");
        DataRow map=ServerDataService.Add(maps);
        map["id"]=10001;map["mappos"]="5,-3,0";map["date"]="0";
        Check(ServerDataService.Save(maps)==1 && Text(game,"SELECT mappos FROM maps WHERE id=10001")=="5,-3,0" && Number(game,"SELECT width FROM maps WHERE id=10001")==-1 && Number(game,"SELECT sniffed FROM maps WHERE id=10001")==0,"The StarLoco map row was not inserted");

        // Item templates and sets keep their reciprocal links inside game.
        var templates=ServerDataService.Load(ServerResourceKind.ItemTemplates);
        Check(templates.Data.Rows.Count==2 && ReferenceNames(templates).ContainsKey("3"),"StarLoco item templates or their set names were not read");
        DataRow template=ServerDataService.Add(templates);
        Check(Convert.ToInt32(template["id"])==901,"The new StarLoco template identifier is wrong");
        template["name"]="Azur StarLoco template 3";
        Check(ServerDataService.Save(templates)==1 && Text(game,"SELECT name FROM item_template WHERE id=901")=="Azur StarLoco template 3","The StarLoco template was not inserted");
        var sets=ServerDataService.Load(ServerResourceKind.ItemSets);
        Check(sets.Data.Rows.Count==1,"The StarLoco itemsets table was not read");
        sets.Data.Rows[0]["items"]="900,901";
        Check(ServerDataService.Save(sets)==1 && Number(game,"SELECT panoplie FROM item_template WHERE id=901")==3 && Number(game,"SELECT panoplie FROM item_template WHERE id=900")==3 && Text(game,"SELECT items FROM itemsets WHERE ID=3")=="900,901","Set membership was not propagated to the StarLoco templates");
        // Deleting a template checks its instances in login.world.entity.objects from the game connection.
        templates=ServerDataService.Load(ServerResourceKind.ItemTemplates);
        templates.Data.Rows.Cast<DataRow>().Single(row=>Convert.ToInt32(row["id"])==900).Delete();
        Refused(()=>ServerDataService.Save(templates),"A StarLoco template with instances in login was deleted");
        Check(Number(game,"SELECT COUNT(*) FROM item_template WHERE id=900")==1,"The refused deletion removed the StarLoco template");
        templates.Data.RejectChanges();
        templates.Data.Rows.Cast<DataRow>().Single(row=>Convert.ToInt32(row["id"])==901).Delete();
        Check(ServerDataService.Save(templates)==1 && Number(game,"SELECT COUNT(*) FROM item_template WHERE id=901")==0 && Text(game,"SELECT items FROM itemsets WHERE ID=3")=="900","Deleting an unused StarLoco template did not update its set");

        // Drops reference monsters and templates of the same game database.
        SeedRow(game,"monsters",new Dictionary<string,object>{{"id",31},{"name","Azur StarLoco monster"},{"gfxID",1}});
        var drops=ServerDataService.Load(ServerResourceKind.Drops);
        Check(ReferenceLists(drops).ContainsKey("monsterId") && ReferenceLists(drops)["monsterId"]["31"]=="Azur StarLoco monster","StarLoco monster names were not offered to the drops editor");
        DataRow drop=ServerDataService.Add(drops);
        drop["monsterId"]=31;drop["objectId"]=7;drop["percentGrade1"]=1.5m;
        Check(ServerDataService.Save(drops)==1 && Count(game,"drops")==1 && Convert.ToDecimal(Scalar(game,"SELECT percentGrade1 FROM drops WHERE monsterId=31 AND objectId=7"))==1.5m,"The StarLoco drop was not inserted");
        drop=ServerDataService.Add(drops);
        drop["monsterId"]=32;drop["objectId"]=7;
        RefusedFormat(()=>ServerDataService.Save(drops),"A drop for a monster missing from game.monsters was accepted");
        drops.Data.RejectChanges();

        // Jobs: a third editor with the same load, add and save cycle.
        var jobs=ServerDataService.Load(ServerResourceKind.Jobs);
        Check(jobs.Data.Rows.Count==1,"The StarLoco jobs_data table was not read");
        DataRow job=ServerDataService.Add(jobs);
        job["name"]="Azur StarLoco job 2";
        Check(ServerDataService.Save(jobs)==1 && Text(game,"SELECT name FROM jobs_data WHERE id=2")=="Azur StarLoco job 2","The StarLoco job was not inserted");
    }

    private static void TestSearch(string login,string game)
    {
        SeedRow(login,"administration.groups",new Dictionary<string,object>{{"id",1},{"name","Azur StarLoco group"},{"isPlayer",1},{"inLadder",1}});
        EmulatorRegistry.Current.LoadCaches();
        Check(AccountList.AllAccount.Count==2 && CharacterList.PersoCount==3 && GroupesList.Grades[1]=="Azur StarLoco group","The StarLoco startup caches were not loaded");
        ItemTemplateList.Load_Item();ItemSetList.LoadPano();SpellsList.Load_Spells();SpellsList.Load_Spells();
        DropsList.Load_Drops();DropsList.Load_Drops();MonsterList.Load_Monster();JobsList.ANPE();
        Check(ItemTemplateList.CountItems==2 && ItemTemplateList.GetItem(900,1)=="Azur StarLoco item" && ItemTemplateList.ReturnItemId("Azur StarLoco potion")==7,"StarLoco item templates were not cached from game");
        Check(ItemSetList.SetName.Single()=="Azur StarLoco set" && ItemSetList.ReturnItems(3,1)=="900","StarLoco item sets were not cached from game");
        Check(SpellsList.CountSpells==2 && SpellsList.SpellsName.Count==2 && SpellsList.AllSpells[2].Durée==1200 && SpellsList.ReturnSpellsIDByName("Azur StarLoco spell")==1,"StarLoco spells were not cached with their duration column");
        Check(DropsList.Drops_Count==1 && DropsList.DropsName.Single()=="" && DropsList.DropInfo(1,2)=="31" && DropsList.DropInfo(1,3)=="7","StarLoco drops without an id column were not cached");
        Check(MonsterList.Monsters_count==1 && MonsterList.ReturnMonstersInfos(31,1)=="Azur StarLoco monster" && MonsterList.CapturableOrNot(31)=="Oui","StarLoco monsters without iaModels/size were not cached");
        Check(JobsList.JobsCount==2 && JobsList.Name_Jobs("1")=="Azur StarLoco job","StarLoco jobs were not cached from game");
        using(var search=new Recherche())
        {
            Call(typeof(Recherche),"Recherche_Load",search,null,EventArgs.Empty);
            Check(search.list.Count==1 && search.list2.Count==1 && search.list3.Count==2,"The search tool did not list the StarLoco sets, drops and spells");
        }
    }

    private static void TestPlacements(string game)
    {
        SeedRow(game,"npc_template",new Dictionary<string,object>{{"id",1},{"gfxID",9001},{"scaleX",100},{"scaleY",100}});
        SeedRow(game,"npcs",new Dictionary<string,object>{{"mapid",100},{"npcid",1},{"cellid",250},{"orientation",1}});
        SeedRow(game,"npcs",new Dictionary<string,object>{{"mapid",101},{"npcid",1},{"cellid",250},{"orientation",1}});
        SeedRow(game,"mobgroups_fix",new Dictionary<string,object>{{"mapid",100},{"cellid",251},{"groupData","31,1,1"}});
        SeedRow(game,"zaaps",new Dictionary<string,object>{{"mapID",100},{"cellID",252}});
        SeedRow(game,"mountpark_data",new Dictionary<string,object>{{"mapid",100},{"owner",-1},{"price",0}});
        var layer=MapServerPlacementLayer.Load(100,479);
        Check(layer.Notice=="" && layer.Count(ServerResourceKind.Npcs)==1 && layer.Count(ServerResourceKind.MonsterGroups)==1 && layer.Count(ServerResourceKind.Zaaps)==1 && layer.PaddockCount==1,"StarLoco placements were not loaded from game: "+layer.Summary+" "+layer.Notice);
        Check(layer.At(250).Count==1 && layer.At(251).Count==1 && layer.At(252).Count==1 && layer.At(253).Count==0,"StarLoco placement cells are wrong");
        var npcs=ServerDataService.Load(ServerResourceKind.Npcs,100,479);
        Check(npcs.Data.Rows.Count==1 && ReferenceNames(npcs).ContainsKey("1"),"The StarLoco NPC placements of one map were not isolated");
        var paddocks=ServerDataService.Load(ServerResourceKind.Paddocks,100,479);
        Check(paddocks.Data.Rows.Count==1 && !paddocks.Data.Columns.Contains("cellid") && paddocks.Data.Columns.Contains("price"),"The StarLoco paddock ownership table was not read for the map");
    }

    private static void TestBotExport(string login,string game,string work)
    {
        string folder=Path.Combine(work,"xml");
        Func<string,bool,int> export=(type,forBot)=>XmlParser.ParseSQLToXML(folder,type,forBot).GetAwaiter().GetResult();
        Check(export("Sorts",true)==2 && File.Exists(Path.Combine(folder,"1.xml")) && File.Exists(Path.Combine(folder,"2.xml")),"StarLoco spells were not exported for the bot");
        Check(export("Maps",true)==1 && File.ReadAllText(Path.Combine(folder,"10001.xml")).Contains("<BACK>0</BACK>"),"StarLoco maps without background were not exported for the bot");
        Check(export("PNJs",true)==2 && File.Exists(Path.Combine(folder,"1_100_250.xml")) && File.ReadAllText(Path.Combine(folder,"1_101_250.xml")).Contains("<GFX>9001</GFX>"),"StarLoco NPCs were not exported with their game templates");
        Check(export("Objets",true)==2 && export("Monstres",true)==1 && export("Métiers",true)==2 && export("Zaaps",true)==1,"StarLoco bot resources were not exported from game");
        Check(export("Joueurs",false)==3 && File.ReadAllText(Path.Combine(folder,"Joueurs.xml")).Contains("NAME=\"players\""),"StarLoco players were not exported from login");
        Check(export("Maisons",false)==0 && File.Exists(Path.Combine(folder,"Maisons.xml")) && export("Panoplies",false)==1,"StarLoco houses and sets were not exported");
    }

    private static void TestAccountEditing(string login,uint account,uint second)
    {
        ApplyAccounts(new object[]{account,"pseudo","Azur edited pseudo","azur-starloco"},new object[]{account,"points","15","0"});
        Check(Text(login,"SELECT pseudo FROM accounts WHERE guid="+account)=="Azur edited pseudo" && Number(login,"SELECT points FROM accounts WHERE guid="+account)==15,"StarLoco account edits were not saved");
        Sql(login,"UPDATE accounts SET logged=1 WHERE guid="+account);
        Refused(()=>ApplyAccounts(new object[]{account,"points","16","15"}),"An online StarLoco account was modified");
        Sql(login,"UPDATE accounts SET logged=0 WHERE guid="+account);
        Sql(login,"UPDATE accounts SET points=17 WHERE guid="+account);
        Refused(()=>ApplyAccounts(new object[]{account,"points","16","15"}),"A stale StarLoco account value was overwritten");
        Check(Number(login,"SELECT points FROM accounts WHERE guid="+account)==17,"A refused account edit mutated the StarLoco row");
        Check((bool)Call(Moderation,"SetAccountBannedForCharacter",null,1,"AzurStar",true) && Number(login,"SELECT banned FROM accounts WHERE guid="+account)==1,"StarLoco account ban was not written");
        Check((bool)Call(Moderation,"SetAccountBannedForCharacter",null,1,"AzurStar",false) && Number(login,"SELECT banned FROM accounts WHERE guid="+account)==0,"StarLoco account unban was not written");
        Call(Moderation,"DeleteCharacter",null,2,"AzurStarOther");
        Check(Count(login,"players")==2 && Number(login,"SELECT COUNT(*) FROM `world.entity.objects` WHERE id=100")==0 && Number(login,"SELECT COUNT(*) FROM `world.entity.objects` WHERE id=101")==1,"Deleting a StarLoco character did not remove its items from login.world.entity.objects");
        Call(Moderation,"DeleteAccount",null,second,"azur-starloco-md5");
        Check(Count(login,"accounts")==1 && Count(login,"players")==1 && Number(login,"SELECT COUNT(*) FROM `world.entity.objects` WHERE id=101")==0,"Deleting a StarLoco account did not remove its characters and items");
        AccountList.AllAccounts();CharacterList.AllPerso();
        Check(AccountList.AllAccount.Count==1 && CharacterList.PersoCount==1,"StarLoco caches were not reloaded after deletions");
    }
}
