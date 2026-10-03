using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using MySql.Data.MySqlClient;
using Tools_protocol.Json;
using Tools_protocol.Query;
using Tools_protocol.Parser.XML;
using Tools_protocol.Managers;
using Tool_BotProtocol.Game.Perso.Spells;

internal static class ResourceExportIntegrationSmoke
{
    private static void Main()
    {
        string bin = TestPaths.ApplicationBin;
        AppDomain.CurrentDomain.AssemblyResolve += (sender,args) => {
            string candidate=Path.Combine(bin,new AssemblyName(args.Name).Name+".dll");
            return File.Exists(candidate)?Assembly.LoadFrom(candidate):null;
        };
        Run();
    }
    private static string Connection(string db) { return new MySqlConnectionStringBuilder { Server="127.0.0.1",Port=43306,UserID="root",Database=db,ConnectionTimeout=3 }.ConnectionString; }
    private static void Sql(string connection,string sql)
    {
        using(var db=new MySqlConnection(connection)) using(var command=new MySqlCommand(sql,db)) { db.Open(); command.ExecuteNonQuery(); }
    }
    private static void Check(bool ok,string message) { if(!ok)throw new Exception(message); }
    private static void Run()
    {
        string db="azur_xml_test_"+Guid.NewGuid().ToString("N").Substring(0,8);
        string connection=Connection(db),server=Connection("");
        string scratch=Path.Combine(TestPaths.Work,"xml-test-"+Guid.NewGuid().ToString("N"));
        string initial=Directory.GetCurrentDirectory();
        Directory.CreateDirectory(scratch);
        try
        {
            Sql(server,"CREATE DATABASE `"+db+"` CHARACTER SET utf8mb4");
            DatabaseManager.ConnectionString=connection;
            EmuManager.EMUSELECTED="Kryone";
            JsonManager.Auth_dico["Template"]="templates";
            JsonManager.Auth_dico["cartes"]="maps";
            JsonManager.Auth_dico["sort"]="sorts";
            JsonManager.Auth_dico["npcs"]="npcs";
            JsonManager.Auth_dico["npc_template"]="npc_template";
            Sql(connection,"CREATE TABLE templates (id INT,name TEXT,type INT,level INT,pod INT,conditions TEXT,statsTemplate TEXT,nullable TEXT); INSERT INTO templates VALUES (42,'Sword & <test>',1,20,3,'PL>5','76#1#2',NULL)");
            Check(XmlParser.ParseSQLToXML(scratch,"Objets").GetAwaiter().GetResult()==1,"full export count");
            XElement full=XElement.Load(Path.Combine(scratch,"Objets.xml"));
            Check(full.Element("RECORD").Element("name").Value=="Sword & <test>","XML escaping");
            Check(full.Element("RECORD").Element("nullable").Attribute("NULL").Value=="true","NULL lost");
            string items=Path.Combine(scratch,"items");
            Check(XmlParser.ParseSQLToXML(items,"Objets",true).GetAwaiter().GetResult()==1,"bot item count");
            Check(XElement.Load(Path.Combine(items,"42.xml")).Element("NOM").Value=="Sword & <test>","bot record shape");
            Sql(connection,"CREATE TABLE maps (id INT,width INT,heigth INT,mappos TEXT,mapData TEXT,background INT); INSERT INTO maps VALUES(1,15,17,'-12,34','abc',0)");
            string maps=Path.Combine(scratch,"maps");
            XmlParser.ParseSQLToXML(maps,"Maps",true).GetAwaiter().GetResult();
            string original=File.ReadAllText(Path.Combine(maps,"1.xml"));
            Sql(connection,"UPDATE maps SET mappos='invalid'");
            try { XmlParser.ParseSQLToXML(maps,"Maps",true).GetAwaiter().GetResult(); throw new Exception("bad map accepted"); } catch(FormatException) { }
            Check(File.ReadAllText(Path.Combine(maps,"1.xml"))==original,"failed conversion damaged existing file");
            Sql(connection,"CREATE TABLE sorts (id INT,nom TEXT,lvl1 TEXT,lvl2 TEXT,lvl3 TEXT,lvl4 TEXT,lvl5 TEXT,lvl6 TEXT)");
            using(var sqlDb=new MySqlConnection(connection)) using(var command=new MySqlCommand("INSERT INTO sorts VALUES(25,'Test spell',@a,@b,@c,@d,@e,@f)",sqlDb))
            {
                sqlDb.Open();
                for(int i=0;i<6;i++) command.Parameters.AddWithValue("@"+(char)('a'+i),"108;11;16;-1;0;0;1d6+10,108;21;32;-1;0;0;1d12+20,"+(i+1)+",1,4,40,100,false,true,false,true,0,0,0,0,PaCb,-1,18;19,42,false");
                command.ExecuteNonQuery();
            }
            string spells=Path.Combine(scratch,"ressources","Bot","BotSorts");
            Check(XmlParser.ParseSQLToXML(spells,"Sorts",true).GetAwaiter().GetResult()==1,"spell count");
            XElement spell=XElement.Load(Path.Combine(spells,"25.xml")).Element("SORT");
            Check(spell.Element("NOM").Value=="Test spell" && spell.Elements("NIVEAU").Count()==6,"spell nesting/six levels");
            Check(spell.Elements("NIVEAU").First().Attribute("PA").Value=="1" && spell.Elements("NIVEAU").Last().Attribute("PA").Value=="6","levels copied same values");
            Check(spell.Elements("NIVEAU").First().Elements("EFFETS").Last().Attribute("CRITIQUE").Value=="true","critical effect missing");
            Directory.SetCurrentDirectory(scratch);
            Spell.LoadAllSpells(); Spell.LoadAllSpells();
            Check(Spell.AllSpells.Count==1 && Spell.AllSpells[25].Stats.Count==6 && Spell.AllSpells[25].Stats[6].PA==6,"bot read/reload");
            Sql(connection,"CREATE TABLE npcs (mapid INT,npcid INT,cellid INT,orientation INT); CREATE TABLE npc_template(id INT,gfxID INT,sex INT); INSERT INTO npc_template VALUES(9,100,1); INSERT INTO npcs VALUES(1,9,10,2),(2,9,20,3)");
            string npcs=Path.Combine(scratch,"npcs");
            Check(XmlParser.ParseSQLToXML(npcs,"PNJs",true).GetAwaiter().GetResult()==2 && Directory.GetFiles(npcs,"*.xml").Length==2,"NPC placements overwrote each other");
            Check(Directory.GetDirectories(scratch,".azur-export-*",SearchOption.AllDirectories).Length==0,"staging not cleaned");
            Console.WriteLine("OK: full XML, NULL/escaping, bot resources, six spell levels, critical effects, reload and failed-export preservation");
        }
        finally
        {
            Directory.SetCurrentDirectory(initial);
            Sql(server,"DROP DATABASE IF EXISTS `"+db+"`");
            Directory.Delete(scratch,true);
        }
    }
}
