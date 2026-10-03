using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Outil_Azur_complet;

// Read only CREATE TABLE definitions from the supplied dump. No user records or
// connection settings are imported into the interface/integration fixtures.
internal static class EditorFixtures
{
    internal sealed class Resource
    {
        internal ServerResourceKind Kind;internal string Key,Table,Title;
        internal Resource(ServerResourceKind kind,string key,string table,string title){Kind=kind;Key=key;Table=table;Title=title;}
    }
    internal static readonly Resource[] Resources={
        new Resource(ServerResourceKind.ItemTemplates,"Template","item_template","Modèles d'objets"),
        new Resource(ServerResourceKind.ItemSets,"panoplies","itemsets","Panoplies"),
        new Resource(ServerResourceKind.Crafts,"crafts","crafts","Recettes de fabrication"),
        new Resource(ServerResourceKind.Drops,"drops","drops","Butins des monstres"),
        new Resource(ServerResourceKind.NpcQuestions,"npc_questions","npc_questions","Questions des PNJ"),
        new Resource(ServerResourceKind.NpcResponses,"npc_reponse","npc_reponses_actions","Réponses et actions des PNJ"),
        new Resource(ServerResourceKind.Quests,"quete","quest_data","Quêtes"),
        new Resource(ServerResourceKind.QuestSteps,"quete_etape","quest_etapes","Étapes des quêtes"),
        new Resource(ServerResourceKind.QuestObjectives,"quete_objectif","quest_objectifs","Objectifs et récompenses"),
        new Resource(ServerResourceKind.MapTriggers,"cellule","scripted_cells","Téléportations des cartes"),
        new Resource(ServerResourceKind.EndFightActions,"endfight","endfight_action","Actions de fin de combat"),
        new Resource(ServerResourceKind.InteractiveDoors,"Iporte","interactive_doors","Portes et mécanismes"),
        new Resource(ServerResourceKind.ObjectActions,"objets_actions","objectsactions","Actions des objets"),
        new Resource(ServerResourceKind.Dungeons,"donjons","donjons","Entrées de donjons"),
        new Resource(ServerResourceKind.NpcTemplates,"npc_template","npc_template","Définitions des PNJ"),
        new Resource(ServerResourceKind.Npcs,"npcs","npcs","Placements des PNJ"),
        new Resource(ServerResourceKind.Monsters,"monstres","monsters","Monstres"),
        new Resource(ServerResourceKind.MonsterGroups,"groupe_monstre","mobgroups_fix","Groupes de monstres"),
        new Resource(ServerResourceKind.Paddocks,"enclos","mountpark_data","Enclos"),
        new Resource(ServerResourceKind.Interactives,"interactions","interactive_objects_data","Objets interactifs"),
        new Resource(ServerResourceKind.Jobs,"metiers","jobs_data","Métiers"),
        new Resource(ServerResourceKind.Spells,"sort","sorts","Sorts"),
        new Resource(ServerResourceKind.Zaaps,"zaaps","zaaps","Zaaps"),
        new Resource(ServerResourceKind.Maps,"cartes","maps","Données des cartes")
    };
    private static string dump;
    internal static string Schema(Resource resource)
    {
        if(dump==null)dump=File.ReadAllText(Path.Combine(new DirectoryInfo(TestPaths.ApplicationBin).Parent.Parent.Parent.FullName,"kworldsave.sql"));
        Match match=Regex.Match(dump,@"CREATE TABLE `"+Regex.Escape(resource.Table)+@"` \((.*?)\)\s*ENGINE=",RegexOptions.Singleline);
        if(!match.Success)throw new Exception("Supplied schema missing: "+resource.Table);
        return match.Groups[1].Value;
    }
    internal static DataTable Table(Resource resource)
    {
        var table=new DataTable(resource.Table);
        foreach(Match match in Regex.Matches(Schema(resource),@"^`([^`]+)`\s+([^\r\n]+)",RegexOptions.Multiline))
        {
            string definition=match.Groups[2].Value;string type=Regex.Match(definition,@"^\w+").Value.ToLowerInvariant();
            Type data=type=="decimal"?typeof(decimal):type=="bigint"?typeof(long):type.Contains("int")?typeof(int):typeof(string);
            var column=table.Columns.Add(match.Groups[1].Value,data);column.AllowDBNull=!definition.Contains("NOT NULL");
            Match fallback=Regex.Match(definition,@"DEFAULT\s+(?:'([^']*)'|(-?\d+(?:\.\d+)?)|(NULL))");
            if(fallback.Success && fallback.Groups[3].Value!="NULL")column.DefaultValue=Convert.ChangeType(fallback.Groups[1].Success?fallback.Groups[1].Value:fallback.Groups[2].Value,data,CultureInfo.InvariantCulture);
            else if(!column.AllowDBNull)column.DefaultValue=data==typeof(string)?(object)"":Activator.CreateInstance(data);
        }
        return table;
    }
    internal static string[] Keys(Resource resource)
    {return Regex.Matches(Regex.Match(Schema(resource),@"PRIMARY KEY \(([^\r\n]*)\)").Groups[1].Value,@"`([^`]+)`").Cast<Match>().Select(m=>m.Groups[1].Value).ToArray();}
    internal static void Set(DataRow row,string column,object value){if(row.Table.Columns.Contains(column))row[column]=value;}
    internal static void Populate(Resource resource,DataRow row)
    {
        foreach(string id in new[]{"id","mapid","map"})Set(row,id,100);
        foreach(string name in new[]{"name","nom","Name IO"})Set(row,name,resource.Title+" Azur");
        Set(row,"cellid",42);Set(row,"npcid",100);Set(row,"npc",100);Set(row,"orientation",2);Set(row,"isMovable",0);
        if(resource.Kind==ServerResourceKind.ItemTemplates){Set(row,"level",20);Set(row,"type",1);Set(row,"panoplie",-1);Set(row,"statsTemplate","7d#1#0a#0#1d10+0");}
        if(resource.Kind==ServerResourceKind.ItemSets){Set(row,"items","100,101");Set(row,"bonus","125:5;125:10,118:2");}
        if(resource.Kind==ServerResourceKind.Crafts)Set(row,"craft","101*5;102*2");
        if(resource.Kind==ServerResourceKind.Drops){Set(row,"monsterId",100);Set(row,"objectId",100);Set(row,"monsterName","Bouftou");Set(row,"objectName","Amulette Azur");for(int grade=1;grade<=5;grade++)Set(row,"percentGrade"+grade,12.5m);}
        if(resource.Kind==ServerResourceKind.Spells)for(int level=1;level<=6;level++)Set(row,"lvl"+level,"-1");
        if(resource.Kind==ServerResourceKind.NpcQuestions){Set(row,"responses","101;102");Set(row,"description","Bienvenue ! Voulez-vous découvrir la région ?");}
        if(resource.Kind==ServerResourceKind.Quests){Set(row,"etapes","101;102");Set(row,"npc","100");}
        if(resource.Kind==ServerResourceKind.MapTriggers){Set(row,"ActionsArgs","101,42");Set(row,"Conditions","-1");}
        if(resource.Kind==ServerResourceKind.MonsterGroups)Set(row,"groupData","100,1,5");
        if(resource.Kind==ServerResourceKind.Jobs){Set(row,"tools","100,101");Set(row,"crafts","101;100,101");Set(row,"skills","7003;101");Set(row,"AP","5,10,20,30");}
        if(resource.Kind==ServerResourceKind.InteractiveDoors){Set(row,"maps","100");Set(row,"button","42");Set(row,"time",30);}
        if(resource.Kind==ServerResourceKind.ObjectActions){Set(row,"template",100);Set(row,"type",1);Set(row,"args","100,42");}
        if(resource.Kind==ServerResourceKind.Maps){Set(row,"width",15);Set(row,"heigth",17);Set(row,"height",17);Set(row,"date","20260917");Set(row,"mappos","0,0,0");}
    }
}
