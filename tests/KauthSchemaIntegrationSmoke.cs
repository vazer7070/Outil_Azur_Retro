using System;
using System.Collections.Generic;
using System.Collections;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using MySql.Data.MySqlClient;
using Outil_Azur_complet;
using Outil_Azur_complet.editeur_perso;
using Outil_Azur_complet.maps;
using Tools_protocol.Json;
using Tools_protocol.Kryone.Database;
using Tools_protocol.Query;

internal static class KauthSchemaIntegrationSmoke
{
    private static string Connection(string database)
    { return new MySqlConnectionStringBuilder { Server="127.0.0.1",Port=43306,UserID="root",Database=database,ConnectionTimeout=3 }.ConnectionString; }
    private static void Sql(string connection,string sql)
    { using(var c=new MySqlConnection(connection))using(var command=new MySqlCommand(sql,c)){c.Open();command.ExecuteNonQuery();} }
    private static void Check(bool condition,string message)
    { if(!condition)throw new Exception(message); }
    private static object Scalar(string connection,string sql)
    { using(var c=new MySqlConnection(connection))using(var command=new MySqlCommand(sql,c)){c.Open();return command.ExecuteScalar();} }
    private static object Call(Type type,string name,object instance,params object[] arguments)
    {
        try{return type.GetMethod(name,BindingFlags.Static|BindingFlags.Instance|BindingFlags.NonPublic).Invoke(instance,arguments);}
        catch(TargetInvocationException error){throw error.InnerException;}
    }
    private static void Refused(Action action,string message)
    {
        try{action();}
        catch(InvalidOperationException){return;}
        throw new Exception(message);
    }
    private static void RefusedLength(Action action,string message)
    {
        try{action();}
        catch(InvalidOperationException){return;}
        catch(FormatException){return;}
        throw new Exception(message);
    }
    private static void RefusedEncoding(Action action,string message)
    {
        try{action();}
        catch(MySqlException error)
        {
            if(error.Number==1366)return;
            throw;
        }
        throw new Exception(message);
    }
    // Populate only synthetic values. Required columns come from this fixture's metadata,
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
        Assembly app=typeof(InitializeForm).Assembly;
        Type service=app.GetType("Outil_Azur_complet.editeur_items.InventoryUpdateService");
        Type change=app.GetType("Outil_Azur_complet.editeur_items.InventoryChange");
        object value=Activator.CreateInstance(change,true);
        change.GetProperty("CharacterId").SetValue(value,id,null);
        change.GetProperty("CharacterName").SetValue(value,name,null);
        change.GetProperty("Kind").SetValue(value,Enum.Parse(app.GetType("Outil_Azur_complet.editeur_items.InventoryChangeKind"),kind),null);
        change.GetProperty("ItemGuid").SetValue(value,guid,null);
        change.GetProperty("TemplateId").SetValue(value,template,null);
        change.GetProperty("Quantity").SetValue(value,quantity,null);
        Type listType=typeof(List<>).MakeGenericType(change);
        object list=Activator.CreateInstance(listType);
        listType.GetMethod("Add").Invoke(list,new[]{value});
        Call(service,"Apply",null,list);
    }
    private static void SetGlobalMode(string server,string mode)
    {
        MySqlConnection.ClearAllPools();
        using(var c=new MySqlConnection(server))using(var command=new MySqlCommand("SET GLOBAL sql_mode=@mode",c))
        {c.Open();command.Parameters.AddWithValue("@mode",mode);command.ExecuteNonQuery();}
        MySqlConnection.ClearAllPools();
    }
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
        string database="azur_kauth_"+suffix,world="azur_kauth_world_"+suffix;
        string server=Connection(""),connection=Connection(database),worldConnection=Connection(world);
        string originalMode=Convert.ToString(Scalar(server,"SELECT @@GLOBAL.sql_mode"));
        try
        {
            Check(originalMode.Split(',').Contains("STRICT_TRANS_TABLES"),"The isolated MySQL server must use STRICT_TRANS_TABLES for the supplied-schema write checks");
            Sql(server,"CREATE DATABASE `"+database+"` CHARACTER SET utf8mb4");
            string fixture=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"..","Fixtures","kauth-schema.sql");
            string sql=File.ReadAllText(fixture);
            Check(!sql.Contains("INSERT INTO") && !sql.Contains("REPLACE INTO"),"The supplied schema fixture contains data rows");
            int created=0;
            foreach(string block in sql.Split(new[]{"-- END TABLE"},StringSplitOptions.RemoveEmptyEntries))
            {
                int start=block.IndexOf("CREATE TABLE",StringComparison.OrdinalIgnoreCase);
                if(start<0)continue;
                Sql(connection,block.Substring(start).Trim());created++;
            }
            Check(created==58,"Not all supplied kauth tables were recreated");
            DatabaseManager.ConnectionString=connection;DatabaseManager2.ConnectionString=connection;
            InitializeForm.EMUSELECT="Kryone";
            string authPath=Path.Combine(TestPaths.Work,"kauth-auth-tables.json");
            JsonManager.WriteConfig("auth",authPath);
            Check(JsonManager.LectureAuth(authPath),"Default auth table mapping cannot be read");
            var tables=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using(var c=new MySqlConnection(connection))using(var cmd=new MySqlCommand("SHOW TABLES",c))
            {c.Open();using(var reader=cmd.ExecuteReader())while(reader.Read())tables.Add(reader.GetString(0));}
            foreach(var pair in JsonManager.Auth_dico)
                Check(tables.Contains(pair.Value),"Default mapping misses supplied kauth table "+pair.Key+" → "+pair.Value);
            foreach(ServerResourceKind kind in Enum.GetValues(typeof(ServerResourceKind)))
            {
                var snapshot=ServerDataService.Load(kind);
                Check(snapshot.Data!=null && snapshot.Data.Columns.Count>0,"Editor cannot read supplied kauth schema for "+kind);
            }
            var paddocks=ServerDataService.Load(ServerResourceKind.Paddocks,100,479);
            Check(paddocks.Data.Columns.Contains("cellid"),"Supplied kauth enclosure cell is not available to the editor");
            var layer=MapServerPlacementLayer.Load(100,479);
            Check(layer.Notice=="" && layer.PaddockCount==0,"Empty supplied schema cannot load server placement layer");
            Console.WriteLine("OK: 58 supplied kauth schemas, 50 default mappings, 24 resource editors and scoped placements match without importing private records");

            Sql(server,"CREATE DATABASE `"+world+"` CHARACTER SET utf8mb4");
            Sql(worldConnection,"CREATE TABLE items (guid INT PRIMARY KEY,template INT NOT NULL,qua INT NOT NULL,pos INT NOT NULL,stats TEXT NOT NULL,puit INT NOT NULL) ENGINE=InnoDB");
            DatabaseManager2.ConnectionString=worldConnection;JsonManager.World_dico["items"]="items";
            Check(Convert.ToString(Scalar(connection,"SELECT @@SESSION.sql_mode")).Split(',').Contains("STRICT_TRANS_TABLES"),"The account creation connection does not use strict SQL mode");
            TestAccounts(connection);
            uint account=AccountList.AllAccount["azur-sha512"].Guid,second=AccountList.AllAccount["azur-md5"].Guid;
            foreach(var row in new[]{new{Id=1,Name="AzurSynthetic",Logged=(object)0},new{Id=2,Name="AzurUnknown",Logged=(object)DBNull.Value},new{Id=3,Name="AzurOther",Logged=(object)0}})
                SeedRow(connection,"players",new Dictionary<string,object>{{"id",row.Id},{"name",row.Name},{"account",(int)account},{"logged",row.Logged},{"class",1},{"level",1},{"size",100},{"gfx",1},{"color1",-1},{"color2",-1},{"color3",-1},{"objets",row.Id==2?"100|":""}});
            SeedRow(connection,"item_template",new Dictionary<string,object>{{"id",7},{"name","Azur synthetic potion"},{"statsTemplate","76#1#0#0#0d0+1"}});
            SeedRow(connection,"jobs_data",new Dictionary<string,object>{{"id",1},{"name","Azur synthetic job"}});
            Sql(worldConnection,"INSERT INTO items VALUES (100,7,2,-1,'synthetic',0)");
            CharacterList.AllPerso();
            Check(CharacterList.PersoCount==3 && CharacterList.Listing("AzurUnknown").Logged==-1,"Nullable kauth logged state was not loaded as unknown");
            TestUnknownState(connection,worldConnection);
            TestCharacterWrites(connection,account);
            TestSkillsAndInventory(connection,worldConnection);
            TestAccountWrites(connection,account,second);
            AccountList.AllAccounts();

            // A permissive server must never silently truncate values that the real schema caps.
            SetGlobalMode(server,"");
            Check(Convert.ToString(Scalar(connection,"SELECT @@SESSION.sql_mode"))=="","The length guards were not tested under permissive SQL mode");
            int count=Convert.ToInt32(Scalar(connection,"SELECT COUNT(*) FROM accounts"));
            RefusedLength(()=>AccountList.CreateAccount(new string('a',31),2,"synthetic-password","q","r"),"An account name exceeding kauth varchar(30) was silently truncated");
            RefusedLength(()=>AccountList.CreateAccount("azur-long-question",2,"synthetic-password",new string('q',101),"r"),"A question exceeding kauth varchar(100) was silently truncated");
            Check(Convert.ToInt32(Scalar(connection,"SELECT COUNT(*) FROM accounts"))==count && AccountList.AllAccount.Count==count,"Rejected account creation changed rows or cache");
            CharacterList.AllPerso();
            RefusedLength(()=>ApplyCharacter(1,"name",new string('n',31)),"A character name exceeding kauth varchar(30) was silently truncated");
            Check(Convert.ToString(Scalar(connection,"SELECT name FROM players WHERE id=1"))=="AzurSynthetic","Rejected character rename changed the row");
            string pseudo=Convert.ToString(Scalar(connection,"SELECT pseudo FROM accounts WHERE guid="+account));
            RefusedLength(()=>ApplyAccounts(new object[]{account,"pseudo",new string('p',31),pseudo}),"A pseudo exceeding kauth varchar(30) was silently truncated");
            Check(Convert.ToString(Scalar(connection,"SELECT pseudo FROM accounts WHERE guid="+account))==pseudo,"Rejected pseudo change mutated the row");
            var jobs=CharacterSkillsService.Load(1,CharacterSkillKind.Jobs);
            string original=Convert.ToString(Scalar(connection,"SELECT jobs FROM players WHERE id=1"));
            RefusedLength(()=>CharacterSkillsService.Save(jobs,new[]{new CharacterSkillEntry{Id=1,Value=10,Tail=new string('x',300)}}),"A jobs list exceeding kauth varchar(300) was silently truncated");
            Check(Convert.ToString(Scalar(connection,"SELECT jobs FROM players WHERE id=1"))==original,"Rejected long jobs list changed the row");
            RefusedEncoding(()=>AccountList.CreateAccount("azur-\u0416",2,"synthetic-password","q","r"),"A Cyrillic account name was silently replaced in kauth latin1");
            RefusedEncoding(()=>AccountList.CreateAccount("azur-\uD83D\uDE00",2,"synthetic-password","q","r"),"An emoji account name was silently replaced in kauth latin1");
            Check(Convert.ToInt32(Scalar(connection,"SELECT COUNT(*) FROM accounts"))==count && AccountList.AllAccount.Count==count && !AccountList.AllAccount.ContainsKey("azur-\u0416") && !AccountList.AllAccount.ContainsKey("azur-\uD83D\uDE00"),"Rejected unrepresentable account creation changed rows or cache");
            RefusedEncoding(()=>ApplyAccounts(new object[]{account,"pseudo","Azur \u0416",pseudo}),"An unrepresentable pseudo was silently replaced in kauth latin1");
            RefusedEncoding(()=>ApplyCharacter(1,"name","Azur\uD83D\uDE00"),"An unrepresentable character name was silently replaced in kauth latin1");
            Check(Convert.ToString(Scalar(connection,"SELECT pseudo FROM accounts WHERE guid="+account))==pseudo && AccountList.AllAccount["azur-sha512"].Pseudo==pseudo && Convert.ToString(Scalar(connection,"SELECT name FROM players WHERE id=1"))=="AzurSynthetic" && CharacterList.Listing("AzurSynthetic").Name=="AzurSynthetic","Rejected unrepresentable edits changed rows or cache");
            Console.WriteLine("OK: synthetic kauth account creation SHA512/MD5, required fields/cache, character UI unknown state, guarded account/character/skills/inventory writes, atomic rollback and strict/permissive schema lengths");
        }
        finally
        {
            SetGlobalMode(server,originalMode);
            Sql(server,"DROP DATABASE IF EXISTS `"+database+"`");
            Sql(server,"DROP DATABASE IF EXISTS `"+world+"`");
        }
    }

    private static void TestAccounts(string connection)
    {
        AccountList.AllAccounts();
        AccountList.CreateAccount("azur-sha512",2,"synthetic-password","Synthetic question","Synthetic answer");
        AccountList.CreateAccount("azur-md5",1,"synthetic-password","Synthetic question","Synthetic answer");
        Check(AccountList.AllAccount.Count==2 && AccountList.AccountListCount==2 && CharacterList.IdCompte.Count==2,"New account caches were not updated");
        string expectedMd5;
        using(var md5=MD5.Create())expectedMd5=string.Concat(md5.ComputeHash(Encoding.UTF8.GetBytes("synthetic-password")).Select(b=>b.ToString("x2")));
        Check(AccountList.AllAccount["azur-md5"].Pass==expectedMd5 && AccountList.AllAccount["azur-sha512"].Pass==AccountList.SHA51("synthetic-password"),"The requested password algorithms were not stored");
        AccountList.AllAccounts();
        Check(AccountList.AllAccount.Count==2 && AccountList.AllAccount["azur-sha512"].Question=="Synthetic question","Accounts could not be reloaded from the actual kauth schema");
        Check(Convert.ToInt32(Scalar(connection,"SELECT COUNT(*) FROM accounts WHERE pass_no_crypt='' AND lastConnectionDate='' AND lastIP='' AND friends='' AND enemy='' AND lastConnectDay='' AND banRaison='' AND heurevote=0"))==2,"Mandatory account fields were not initialized safely");
        bool duplicate=false;
        try{AccountList.CreateAccount("azur-sha512",2,"other-password","q","r");}
        catch(MySqlException){duplicate=true;}
        catch(InvalidOperationException){duplicate=true;}
        Check(duplicate && Convert.ToInt32(Scalar(connection,"SELECT COUNT(*) FROM accounts"))==2 && AccountList.AllAccount.Count==2,"Duplicate creation changed rows or cache");
        Sql(connection,"ALTER TABLE accounts ADD azur_unknown_required INT NOT NULL");
        try{Refused(()=>AccountList.CreateAccount("azur-required",2,"synthetic-password","q","r"),"An unknown mandatory account field was accepted");}
        finally{Sql(connection,"ALTER TABLE accounts DROP COLUMN azur_unknown_required");}
        Sql(connection,"ALTER TABLE accounts ENGINE=MyISAM");
        try{Refused(()=>AccountList.CreateAccount("azur-myisam",2,"synthetic-password","q","r"),"Account creation accepted MyISAM");}
        finally{Sql(connection,"ALTER TABLE accounts ENGINE=InnoDB");}
        Check(Convert.ToInt32(Scalar(connection,"SELECT COUNT(*) FROM accounts"))==2 && AccountList.AllAccount.Count==2,"Failed account creations changed rows or cache");
        TestAccountsSmall(connection);
    }

    private static void TestAccountsSmall(string connection)
    {
        string originalTable=JsonManager.Auth_dico["comptes"];
        Sql(connection,"CREATE TABLE accounts_small (guid INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,account VARCHAR(30) NOT NULL UNIQUE,pass TEXT NOT NULL,banned TINYINT NOT NULL DEFAULT 0,pseudo VARCHAR(30) NOT NULL,question VARCHAR(100) NOT NULL,reponse VARCHAR(100) NOT NULL,lastIP VARCHAR(30) NOT NULL DEFAULT '',vip INT NOT NULL DEFAULT 0,points INT NOT NULL DEFAULT 0,logged INT NOT NULL DEFAULT 0) ENGINE=InnoDB");
        try
        {
            JsonManager.Auth_dico["comptes"]="accounts_small";
            AccountList.CreateAccount("azur-small",2,"synthetic-password","Small schema question","Small schema answer");
            Check(Convert.ToInt32(Scalar(connection,"SELECT COUNT(*) FROM accounts_small"))==1,"A smaller account schema could not create a synthetic account");
            AccountList.AllAccounts();
            AccountList small=AccountList.AllAccount["azur-small"];
            Check(AccountList.AllAccount.Count==1 && AccountList.AccountListCount==1 && small.Guid>0 && small.Pass==AccountList.SHA51("synthetic-password") && small.Pseudo=="azur-small" && small.Question=="Small schema question" && small.Reponse=="Small schema answer" && small.lastIp=="" && small.Banned==0 && small.Vip==0 && small.Points==0 && small.Logged==0 && CharacterList.IdCompte[(int)small.Guid]=="azur-small","The smaller account schema could not be reread with the actual constructor and caches");
        }
        finally
        {
            JsonManager.Auth_dico["comptes"]=originalTable;
            try{AccountList.AllAccounts();}
            finally{Sql(connection,"DROP TABLE IF EXISTS accounts_small");}
        }
        Check(AccountList.AllAccount.Count==2 && AccountList.AccountListCount==2 && CharacterList.IdCompte.Count==2,"Restoring the full kauth account mapping did not restore its caches");
    }

    private static void TestUnknownState(string connection,string worldConnection)
    {
        using(var form=new editeur_perso())
        {
            Call(typeof(editeur_perso),"LoadCharacterData",form,"AzurUnknown");
            var label=(Control)typeof(editeur_perso).GetField("iTalk_Label35",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
            Check(label.Text=="État inconnu","Nullable logged was displayed as offline");
            var fields=(IDictionary)typeof(editeur_perso).GetField("_editableFields",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
            foreach(DictionaryEntry field in fields)
                Check((bool)field.Value.GetType().GetProperty("ReadOnly").GetValue(field.Value,null),"Unknown connection state left a character field editable");
        }
        Type moderation=typeof(InitializeForm).Assembly.GetType("Outil_Azur_complet.KryoneModerationService");
        Refused(()=>Call(moderation,"DeleteCharacter",null,2,"AzurUnknown"),"Deleting a NULL-logged character was accepted or raised a raw cast exception");
        var jobs=CharacterSkillsService.Load(2,CharacterSkillKind.Jobs);
        Refused(()=>CharacterSkillsService.Save(jobs,new[]{new CharacterSkillEntry{Id=1,Value=0}}),"Saving skills for a NULL-logged character was accepted or raised a raw cast exception");
        Refused(()=>ApplyInventory(2,"AzurUnknown","Remove",100,0,1),"Saving inventory for a NULL-logged character was accepted or raised a raw cast exception");
        Check(Convert.ToInt32(Scalar(connection,"SELECT COUNT(*) FROM players WHERE id=2 AND logged IS NULL AND objets='100|' AND jobs=''"))==1 && Convert.ToInt32(Scalar(worldConnection,"SELECT qua FROM items WHERE guid=100"))==2,"Unknown-state refusal mutated character or item data");
    }

    private static void TestCharacterWrites(string connection,uint account)
    {
        ApplyCharacters(new Dictionary<int,Dictionary<string,object>>{{1,new Dictionary<string,object>{{"color1",0x123456},{"color2",-1},{"color3",0xABCDEF},{"savepos","100,260"}}}});
        Check(Convert.ToInt32(Scalar(connection,"SELECT color1 FROM players WHERE id=1"))==0x123456 && Convert.ToInt32(Scalar(connection,"SELECT color3 FROM players WHERE id=1"))==0xABCDEF && Convert.ToString(Scalar(connection,"SELECT savepos FROM players WHERE id=1"))=="100,260","Character colors and save position were not saved");
        CharacterList.AllPerso();
        Sql(connection,"UPDATE accounts SET logged=1 WHERE guid="+account);
        Refused(()=>ApplyCharacter(1,"color1",1),"A character on an online account was modified");
        Sql(connection,"UPDATE accounts SET logged=0 WHERE guid="+account);
        Sql(connection,"UPDATE players SET logged=1 WHERE id=1");
        Refused(()=>ApplyCharacter(1,"color1",1),"An online character was modified");
        Sql(connection,"UPDATE players SET logged=NULL WHERE id=1");
        Refused(()=>ApplyCharacter(1,"color1",1),"A character with unknown logged state was modified");
        Sql(connection,"UPDATE players SET logged=0 WHERE id=1");
        Refused(()=>ApplyCharacter(1,"account",999999),"A character could be moved to a nonexistent account");
        Sql(connection,"UPDATE players SET color1=99 WHERE id=1");
        Refused(()=>ApplyCharacter(1,"color1",1),"A stale character value was overwritten");
        Check(Convert.ToInt32(Scalar(connection,"SELECT color1 FROM players WHERE id=1"))==99,"A stale write mutated the row");
        CharacterList.AllPerso();
        var firstWindowChanges=new Dictionary<int,Dictionary<string,object>>{{1,new Dictionary<string,object>{{"color1",80}}}};
        var firstWindowOriginals=CaptureOriginals(firstWindowChanges);
        using(var form=new editeur_perso())
        {
            Call(typeof(editeur_perso),"LoadCharacterData",form,"AzurSynthetic");
            // The second window saves and refreshes the shared cache after the first captured its values.
            Sql(connection,"UPDATE players SET color1=77 WHERE id=1");
            CharacterList.Listing("AzurSynthetic").Color1=77;
            Call(typeof(editeur_perso),"TrackChange",form,"color1","77");
            var pending=(Dictionary<int,Dictionary<string,string>>)typeof(editeur_perso).GetField("_pendingChanges",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
            Check(pending.ContainsKey(1) && pending[1].ContainsKey("color1"),"The first window compared a new input with the refreshed shared cache instead of its loaded snapshot");
            Refused(()=>Call(typeof(editeur_perso),"ApplyCharacterChanges",null,"players",firstWindowChanges,firstWindowOriginals),"A first window overwrote a second window's saved change after the shared cache refreshed");
            Check(Convert.ToInt32(Scalar(connection,"SELECT color1 FROM players WHERE id=1"))==77 && CharacterList.Listing("AzurSynthetic").Color1==77,"The stale window changed the database or shared cache");
        }
        Sql(connection,"UPDATE players SET color1=99 WHERE id=1");
        CharacterList.AllPerso();
        Sql(connection,"UPDATE players SET logged=1 WHERE id=3");
        Refused(()=>ApplyCharacters(new Dictionary<int,Dictionary<string,object>>{{1,new Dictionary<string,object>{{"color1",42}}},{3,new Dictionary<string,object>{{"color1",43}}}}),"Grouped character writes accepted an online character");
        Check(Convert.ToInt32(Scalar(connection,"SELECT color1 FROM players WHERE id=1"))==99 && Convert.ToInt32(Scalar(connection,"SELECT color1 FROM players WHERE id=3"))==-1,"Grouped character changes did not roll back");
        Sql(connection,"UPDATE players SET logged=0 WHERE id=3");
        Refused(()=>ApplyCharacter(1,"name","AzurOther"),"A character could be renamed to an existing character name");
        Refused(()=>ApplyCharacters(new Dictionary<int,Dictionary<string,object>>{{1,new Dictionary<string,object>{{"name","AzurShared"}}},{3,new Dictionary<string,object>{{"name","azurshared"}}}}),"A batch could give two characters the same name");
        Check(Convert.ToString(Scalar(connection,"SELECT name FROM players WHERE id=1"))=="AzurSynthetic" && Convert.ToString(Scalar(connection,"SELECT name FROM players WHERE id=3"))=="AzurOther","Rejected duplicate names mutated character rows");
        var previous=CharacterList.PersoAll;
        Sql(connection,"UPDATE players SET name='AZURSYNTHETIC' WHERE id=3");
        try
        {
            Refused(()=>CharacterList.AllPerso(),"Colliding database character names did not produce an explicit diagnostic");
            Check(ReferenceEquals(previous,CharacterList.PersoAll) && CharacterList.PersoCount==3,"A rejected duplicate-name reload replaced the previous character cache");
        }
        finally{Sql(connection,"UPDATE players SET name='AzurOther' WHERE id=3");}
        CharacterList.AllPerso();
        Sql(connection,"ALTER TABLE players ENGINE=MyISAM");
        try{Refused(()=>ApplyCharacter(1,"color1",1),"Character writes accepted MyISAM");}
        finally{Sql(connection,"ALTER TABLE players ENGINE=InnoDB");}
    }

    private static void TestSkillsAndInventory(string connection,string worldConnection)
    {
        var jobs=CharacterSkillsService.Load(1,CharacterSkillKind.Jobs);
        Check(jobs.Choices.Count==1 && jobs.Choices[0].Id==1,"The kauth jobs catalogue could not be loaded");
        Check(CharacterSkillsService.Save(jobs,new[]{new CharacterSkillEntry{Id=1,Value=1200}})=="1,1200" && Convert.ToString(Scalar(connection,"SELECT jobs FROM players WHERE id=1"))=="1,1200","The kauth character jobs were not saved");
        ApplyInventory(1,"AzurSynthetic","Add",0,7,3);
        string inventory=Convert.ToString(Scalar(connection,"SELECT objets FROM players WHERE id=1"));
        int guid=int.Parse(inventory.TrimEnd('|'));
        Check(guid!=100 && Convert.ToInt32(Scalar(worldConnection,"SELECT qua FROM items WHERE guid="+guid))==3,"Inventory insertion did not link the kauth player to the separate world item");
        ApplyInventory(1,"AzurSynthetic","Remove",guid,0,1);
        Check(Convert.ToInt32(Scalar(worldConnection,"SELECT qua FROM items WHERE guid="+guid))==2,"Partial inventory removal did not update the world item");
        CharacterList.AllPerso();CharacterList.GetInventory("AzurSynthetic");
        Check(CharacterList.InventoryLoadError==null && CharacterList.ItemsPerso.Single()=="Azur synthetic potion ("+guid+") x2","The separate kauth/world inventory could not be reloaded");
    }

    private static void TestAccountWrites(string connection,uint account,uint second)
    {
        ApplyAccounts(new object[]{account,"pseudo","Azur edited pseudo","azur-sha512"},new object[]{account,"points","15","0"});
        Check(Convert.ToString(Scalar(connection,"SELECT pseudo FROM accounts WHERE guid="+account))=="Azur edited pseudo" && Convert.ToInt32(Scalar(connection,"SELECT points FROM accounts WHERE guid="+account))==15,"Account edits were not saved against kauth");
        Sql(connection,"UPDATE accounts SET logged=1 WHERE guid="+account);
        Refused(()=>ApplyAccounts(new object[]{account,"points","16","15"}),"An online account was modified");
        Sql(connection,"UPDATE accounts SET logged=0 WHERE guid="+account);
        Sql(connection,"UPDATE accounts SET points=17 WHERE guid="+account);
        Refused(()=>ApplyAccounts(new object[]{account,"points","16","15"}),"A stale account value was overwritten");
        Check(Convert.ToInt32(Scalar(connection,"SELECT points FROM accounts WHERE guid="+account))==17,"A stale account refusal mutated the row");
        Sql(connection,"UPDATE accounts SET logged=1 WHERE guid="+second);
        Refused(()=>ApplyAccounts(new object[]{account,"points","18","17"},new object[]{second,"points","1","0"}),"Grouped account edits accepted an online account");
        Check(Convert.ToInt32(Scalar(connection,"SELECT points FROM accounts WHERE guid="+account))==17 && Convert.ToInt32(Scalar(connection,"SELECT points FROM accounts WHERE guid="+second))==0,"Grouped account changes did not roll back");
        Sql(connection,"UPDATE accounts SET logged=0 WHERE guid="+second);
    }
}
