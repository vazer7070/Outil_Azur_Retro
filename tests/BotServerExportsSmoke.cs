using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml.Linq;
using Outil_Azur_complet.Parser;
using Tools_protocol.Emulators;
using Tools_protocol.Parser.XML;
using Tool_BotProtocol.Game.Maps.Interactives;
using Tool_BotProtocol.Game.Perso.Inventory;
using BotMap = Tool_BotProtocol.Game.Maps.Map;
using BotTriggers = Tool_BotProtocol.Game.Maps.Triggers;

// Exports du serveur pour le bot (lot D5) : objets interactifs et compétences, cellules déclencheurs, zaapis,
// panoplies et fond des cartes lu dans un SWF de carte. Toutes les lignes, cartes, clés et SWF sont inventés ici ;
// aucune base MySQL ni fichier du client n'est utilisé.
internal static class BotServerExportsSmoke
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";
    private const string PlainCell = "HhGaeaaaaa";

    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(args.Name).Name + ".dll");
            if (!File.Exists(path)) path = Path.ChangeExtension(path, ".exe");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try { Run(); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.Exit(1); }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static string Value(string file, string element) => (string)XElement.Load(file).Element(element);

    private static void Run()
    {
        string work = Path.Combine(TestPaths.Work, "server-exports-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            CheckProfiles();
            CheckInteractives(work);
            CheckTriggersAndZaapis(work);
            CheckItemSets(work);
            CheckMapWithServerData(work);
            CheckMapBackgrounds(work);
            CheckParserWindow();
            Console.WriteLine("OK: interactifs + compétences, déclencheurs, zaapis, panoplies, BACK depuis un SWF de carte chiffré, fenêtre du parseur");
        }
        finally { Directory.Delete(work, true); }
    }

    private static void CheckProfiles()
    {
        EmulatorProfile starloco = EmulatorRegistry.Find("StarLoco");
        Check(starloco.InteractiveSkillRules.Any(rule => rule.Skill == 114 && rule.Matches(7000) && rule.Kind == InteractiveKind.Zaap),
            "StarLoco zaap rule (GameCase.canDoAction, skill 114 on gfx 7000) is missing");
        Check(starloco.InteractiveSkillRules.Any(rule => rule.Skill == 157 && rule.Matches(7031)), "StarLoco zaapi rule is missing");
        Check(starloco.InteractiveSkillRules.Any(rule => rule.Skill == 6 && rule.Matches(7500) && rule.Kind == InteractiveKind.Harvest), "StarLoco harvest rule is missing");
        Check(starloco.InteractiveSkillRules.Any(rule => rule.Skill == 84 && rule.Matches(6700) && rule.Matches(6776) && !rule.Matches(6777)), "StarLoco house range is wrong");
        Check(EmulatorRegistry.Find("Sunshine").InteractiveSkillRules.Count == 0, "Profiles without rules must stay empty");
        Check(starloco.KnowsTable("interactions") && starloco.Table("interactions") == "interactive_objects_data" && starloco.Table("cellule") == "scripted_cells"
            && starloco.Table("zaapi") == "zaapi" && starloco.Table("panoplies") == "itemsets", "StarLoco tables used by the bot exports changed");
    }

    private static void CheckInteractives(string work)
    {
        var table = new DataTable("interactive_objects_data");
        foreach (string column in new[] { "id", "respawn", "duration", "unknow", "walkable" }) table.Columns.Add(column, typeof(int));
        table.Columns.Add("Name IO", typeof(string));
        table.Rows.Add(7500, 12000, 2000, 4, 0, "Arbre d'essai");
        table.Rows.Add(7003, 10000, 1500, 4, 1, "Établi d'essai");
        table.Rows.Add(9001, 10000, 1500, 4, 1, "Décor d'essai");
        var jobs = new DataTable("jobs_data");
        jobs.Columns.Add("id", typeof(int));
        jobs.Columns.Add("skills", typeof(string));
        jobs.Rows.Add(2, "7003;101");
        jobs.Rows.Add(99, "9001;500,501|illisible");
        jobs.Rows.Add(3, DBNull.Value);

        var context = new BotExportContext(EmulatorRegistry.Find("StarLoco").InteractiveSkillRules);
        Check(context.AddJobSkills(jobs.CreateDataReader()) == 2 && context.WarningCount == 1, "Job skills (gfx;skills|…) were not read like Job.java");
        string folder = Path.Combine(work, "BotInteractives");
        int count = XmlParser.ExportBotRecords(folder, "Interactifs", table.CreateDataReader(), context);
        Check(count == Directory.GetFiles(folder, "*.xml").Length && count > 3, "Interactive export count differs from the written files");
        Check(Directory.GetDirectories(folder).Length == 0, "Export staging folder was left behind");

        string tree = Path.Combine(folder, "7500.xml"), bench = Path.Combine(folder, "7003.xml"), custom = Path.Combine(folder, "9001.xml");
        Check(Value(tree, "COMPETENCES") == "6" && Value(tree, "TYPE") == "1" && Value(tree, "NOM") == "Arbre d'essai"
            && Value(tree, "MARCHABLE") == "0" && Value(tree, "REAPPARITION") == "12000" && Value(tree, "DUREE") == "2000" && Value(tree, "SOURCE") == "table",
            "Harvest interactive record is wrong: " + File.ReadAllText(tree));
        Check(Value(bench, "COMPETENCES") == "101" && Value(bench, "TYPE") == "2" && Value(bench, "METIERS") == "2", "Workshop record is wrong");
        Check(Value(custom, "COMPETENCES") == "500,501" && Value(custom, "TYPE") == "2" && Value(custom, "METIERS") == "99", "Workshop known only by jobs_data is wrong");
        string zaap = Path.Combine(folder, "7000.xml"), park = Path.Combine(folder, "6763.xml"), zaapi = Path.Combine(folder, "7030.xml");
        Check(File.Exists(zaap) && Value(zaap, "COMPETENCES") == "44,114" && Value(zaap, "TYPE") == "3" && Value(zaap, "SOURCE") == "serveur"
            && Value(zaap, "MARCHABLE") == "0" && Value(zaap, "DUREE") == "1500", "Zaap known only by the server rules was not exported");
        Check(Value(zaapi, "COMPETENCES") == "157" && Value(zaapi, "TYPE") == "10", "Zaapi rule was not exported");
        Check(Value(park, "TYPE") == "13" && Value(park, "COMPETENCES").Split(',').Contains("175") && Value(park, "COMPETENCES").Split(',').Contains("84"),
            "Paddock gfx must keep the server's house range and the paddock type");

        File.WriteAllText(Path.Combine(folder, "illisible.xml"), "<RECORD>");
        File.WriteAllText(Path.Combine(folder, "gfx-invalide.xml"), "<RECORD><ID>1</ID><GFX>abc</GFX></RECORD>");
        InteractivesParent.LoadAllInteractivesAsync(folder).GetAwaiter().GetResult();
        Check(InteractivesParent.Count == count && InteractivesParent.LoadWarnings.Length == 2, "Bad interactive files must be reported, not fatal");
        InteractivesParent harvest = InteractivesParent.ReturnByGFX(7500);
        Check(harvest != null && harvest.Type == InteractiveType.Harvest && harvest.Recoltable && harvest.Skills.SequenceEqual(new short[] { 6 })
            && harvest.Capacities.Contains((short)6) && harvest.RespawnMs == 12000 && harvest.DurationMs == 2000 && harvest.FromTable && harvest.Name == "Arbre d'essai",
            "Interactive definition was not loaded");
        Check(InteractivesParent.ReturnByGFX(7000).Type == InteractiveType.Zaap && !InteractivesParent.ReturnByGFX(7000).FromTable, "Server-only zaap not loaded");
        Check(InteractivesParent.GetInteractiveBySkill(101).Gfx == 7003 && InteractivesParent.ReturnByGFX(1234) == null, "Lookup by skill or unknown gfx is wrong");
        InteractivesParent.LoadAllInteractivesAsync(Path.Combine(work, "absent")).GetAwaiter().GetResult();
        Check(InteractivesParent.Count == 0 && InteractivesParent.LoadWarnings.Length == 1, "Missing interactive folder must give an empty registry");
        InteractivesParent.LoadAllInteractivesAsync(folder).GetAwaiter().GetResult();
    }

    private static void CheckTriggersAndZaapis(string work)
    {
        var cells = new DataTable("scripted_cells");
        foreach (string column in new[] { "MapID", "CellID", "ActionID", "EventID" }) cells.Columns.Add(column, typeof(int));
        cells.Columns.Add("ActionsArgs", typeof(string));
        cells.Columns.Add("Conditions", typeof(string));
        cells.Rows.Add(32001, 5, 0, 1, "32002,120", "");
        cells.Rows.Add(32001, 7, 0, 1, "32003, 4", "PL>5");
        cells.Rows.Add(32001, 9, 0, 2, "32002,1", "");
        cells.Rows.Add(32002, 3, 1, 1, "DV", "");
        cells.Rows.Add(32003, 2, 0, 1, "40000,2", "");
        string folder = Path.Combine(work, "BotTriggers");
        Check(XmlParser.ExportBotRecords(folder, "Déclencheurs", cells.CreateDataReader(), new BotExportContext()) == 5, "Trigger export count");
        Check(Value(Path.Combine(folder, "32001_5.xml"), "ARGUMENTS") == "32002,120" && Value(Path.Combine(folder, "32001_7.xml"), "CONDITIONS") == "PL>5",
            "Trigger record is wrong");
        BotTriggers.LoadAllTriggersAsync(folder).GetAwaiter().GetResult();
        Check(BotTriggers.Count == 4 && BotTriggers.LoadWarnings.Length == 1, "Only the stop-on-cell event (1) is applied by the server");
        var first = BotTriggers.ForMap(32001);
        Check(first[5].IsTeleport && first[5].TargetMapId == 32002 && first[5].TargetCellId == 120, "Teleport target was not decoded");
        Check(!first[7].IsTeleport && first[7].TargetMapId == -1, "A target with a space must be refused like Integer.parseInt does");
        Check(!BotTriggers.ForMap(32003)[2].IsTeleport, "A target map beyond a short must be refused like Short.parseShort does");
        Check(!first.ContainsKey(9) && BotTriggers.ForMap(32002)[3].ActionId == 1 && BotTriggers.ForMap(123).Count == 0, "Trigger lookup is wrong");

        var zaapi = new DataTable("zaapi");
        zaapi.Columns.Add("mapid", typeof(int));
        zaapi.Columns.Add("align", typeof(int));
        zaapi.Rows.Add(32010, 1);
        zaapi.Rows.Add(32011, 0);
        string zaapis = Path.Combine(work, "BotZaapis");
        Check(XmlParser.ExportBotRecords(zaapis, "Zaapis", zaapi.CreateDataReader(), new BotExportContext()) == 2, "Zaapi export count");
        File.WriteAllText(Path.Combine(zaapis, "illisible.xml"), "<RECORD><MAP>x</MAP></RECORD>");
        Zaaps.LoadZaapisAsync(zaapis).GetAwaiter().GetResult();
        Check(Zaaps.Zaapis.Count == 2 && Zaaps.Zaapis[32010] == 1 && Zaaps.Zaapis[32011] == 0 && Zaaps.LoadWarnings.Length == 1, "Zaapis were not loaded");
    }

    private static void CheckItemSets(string work)
    {
        var sets = new DataTable("itemsets");
        sets.Columns.Add("ID", typeof(int));
        foreach (string column in new[] { "name", "items", "bonus" }) sets.Columns.Add(column, typeof(string));
        sets.Rows.Add(77, "Panoplie d'essai", "8001,8002, 8003,x", "118:10,125:20;118:15,illisible;");
        var context = new BotExportContext();
        string folder = Path.Combine(work, "BotItemSets");
        Check(XmlParser.ExportBotRecords(folder, "Panoplies", sets.CreateDataReader(), context) == 1 && context.WarningCount == 2,
            "Unreadable set entries must be skipped with a warning, like ObjectSet");
        string file = Path.Combine(folder, "77.xml");
        Check(Value(file, "OBJETS") == "8001,8002,8003" && XElement.Load(file).Elements("BONUS").Count() == 2, "Item set record is wrong: " + File.ReadAllText(file));
        ItemSets.LoadAllItemSetsAsync(folder).GetAwaiter().GetResult();
        ItemSet set = ItemSets.Get(77);
        Check(set != null && set.Name == "Panoplie d'essai" && set.Items.SequenceEqual(new[] { 8001, 8002, 8003 }), "Item set was not loaded");
        Check(set.BonusFor(2)[118] == 10 && set.BonusFor(2)[125] == 20 && set.BonusFor(3)[118] == 15 && set.BonusFor(3).Count == 1
            && set.BonusFor(4).Count == 0 && set.BonusFor(1).Count == 0, "The first bonus group must apply to two worn items");
        Check(ItemSets.ForItem(8002) == set && ItemSets.ForItem(1) == null, "Item set lookup by template is wrong");
    }

    private static string InteractiveCell(int gfx)
    {
        // Couche objet 2 = ((v0&2)<<12)+((v7&1)<<12)+(v8<<6)+v9 ; bit 2 de v7 = objet interactif (décodage du client et du serveur).
        var cell = PlainCell.ToCharArray();
        cell[7] = Alphabet[2 | ((gfx >> 12) & 1)];
        cell[8] = Alphabet[(gfx >> 6) & 63];
        cell[9] = Alphabet[gfx & 63];
        return new string(cell);
    }

    private static string MapData(int interactiveCell, int gfx)
    {
        var data = new StringBuilder();
        for (int cell = 0; cell < 18; cell++) data.Append(cell == interactiveCell ? InteractiveCell(gfx) : PlainCell);
        return data.ToString();
    }

    private static void CheckMapWithServerData(string work)
    {
        string maps = Path.Combine(work, "BotMaps");
        Directory.CreateDirectory(maps);
        File.WriteAllText(Path.Combine(maps, "32001.xml"), "<RECORD><ID>32001</ID><LARGEUR>3</LARGEUR><LONGUEUR>4</LONGUEUR><X>1</X><Y>2</Y><MAP_DATA>"
            + MapData(5, 7500) + "</MAP_DATA><BACK>0</BACK></RECORD>");
        BotMap.LoadAllMapsAsync(maps).GetAwaiter().GetResult();
        using (var map = new BotMap())
        {
            map.SetRefreshMap("32001|0706131721|");
            Check(map.HasMapData && map.Interactives.ContainsKey(5), "Synthetic interactive cell was not decoded");
            var interactive = map.Interactives[5];
            Check(interactive.gfx == 7500 && interactive.Interactive != null && interactive.IsUsable && interactive.Interactive.Skills.Contains((short)6),
                "Map.Interactives[cell].Interactive is not filled from BotInteractives");
            Check(map.Triggers.Count == 2 && map.Triggers[5].TargetMapId == 32002 && map.Triggers[5].TargetCellId == 120, "Map.Triggers is not filled for the map");
            map.SetRefreshMap("32002|0706131721|");
            Check(!map.HasMapData && map.Triggers.Count == 1 && map.Triggers.ContainsKey(3), "Triggers must follow the current map even without its geometry");
            map.Clear();
            Check(map.Triggers.Count == 0, "Clear kept the previous map triggers");
        }
    }

    private static string Encrypt(string plain, string key)
    {
        // Inverse de DecryptClass.DecypherData : XOR avec la clé décalée de la somme de contrôle, deux chiffres hexadécimaux par caractère.
        int sum = 0;
        foreach (char value in key) sum = (sum + value % 16) % 16;
        int offset = sum * 2 % key.Length;
        var cipher = new StringBuilder();
        for (int i = 0; i < plain.Length; i++) cipher.Append(((int)(plain[i] ^ key[(i + offset) % key.Length])).ToString("X2"));
        return cipher.ToString();
    }

    private static void Push(BinaryWriter actions, object value)
    {
        byte[] payload;
        if (value is string text) payload = new byte[] { 0 }.Concat(Encoding.UTF8.GetBytes(text)).Concat(new byte[] { 0 }).ToArray();
        else if (value is bool flag) payload = new byte[] { 5, (byte)(flag ? 1 : 0) };
        else payload = new byte[] { 7 }.Concat(BitConverter.GetBytes((int)value)).ToArray();
        actions.Write((byte)0x96); actions.Write((ushort)payload.Length); actions.Write(payload);
    }

    private static void Tag(BinaryWriter writer, int code, byte[] payload)
    {
        writer.Write((ushort)((code << 6) | Math.Min(payload.Length, 63)));
        if (payload.Length >= 63) writer.Write((uint)payload.Length);
        writer.Write(payload);
    }

    /// <summary>SWF de carte assemblé à la main (FWS 6, un DoAction d'affectations) comme ceux de data/maps.</summary>
    private static void WriteMapSwf(string path, int id, int background, string mapData)
    {
        using (var actions = new MemoryStream())
        using (var script = new BinaryWriter(actions))
        using (var movie = new MemoryStream())
        using (var writer = new BinaryWriter(movie))
        {
            var values = new object[] { "id", id, "width", 3, "height", 4, "backgroundNum", background, "ambianceId", 0, "musicId", 0,
                "bOutdoor", true, "capabilities", 0, "mapData", mapData };
            for (int i = 0; i < values.Length; i += 2) { Push(script, values[i]); Push(script, values[i + 1]); script.Write((byte)0x1d); }
            script.Write((byte)0);
            writer.Write(Encoding.ASCII.GetBytes("FWS")); writer.Write((byte)6); writer.Write((uint)0);
            writer.Write(new byte[] { 8, 0 }); writer.Write((ushort)0x0c00); writer.Write((ushort)1);
            Tag(writer, 12, actions.ToArray()); Tag(writer, 1, new byte[0]); Tag(writer, 0, new byte[0]);
            movie.Position = 4; writer.Write((uint)movie.Length);
            File.WriteAllBytes(path, movie.ToArray());
        }
    }

    private static DataTable MapsTable(bool withBackground)
    {
        var table = new DataTable("maps");
        table.Columns.Add("id", typeof(int));
        table.Columns.Add("date", typeof(string));
        table.Columns.Add("width", typeof(int));
        table.Columns.Add("heigth", typeof(int));
        foreach (string column in new[] { "key", "mapData", "mappos" }) table.Columns.Add(column, typeof(string));
        if (withBackground) table.Columns.Add("background", typeof(int));
        return table;
    }

    private static void CheckMapBackgrounds(string work)
    {
        const string date = "0706131721", key = "azur", hexKey = "617A7572";
        string plain = MapData(5, 7500), other = MapData(6, 7003);
        string client = Path.Combine(work, "client"), clientMaps = Path.Combine(client, "data", "maps");
        Directory.CreateDirectory(clientMaps);
        WriteMapSwf(Path.Combine(clientMaps, "32001_" + date + "X.swf"), 32001, 342, Encrypt(plain, key));
        WriteMapSwf(Path.Combine(clientMaps, "32002_" + date + "X.swf"), 32002, 17, Encrypt(other, key));
        Check(ResourceMapConversion.ClientMapsDirectory(client) == clientMaps && ResourceMapConversion.ClientMapsDirectory(Path.Combine(work, "absent")) == null,
            "Client maps folder resolution is wrong");

        var starloco = MapsTable(false);
        starloco.Rows.Add(32001, date, 3, 4, hexKey, plain, "1,2,0");
        starloco.Rows.Add(32002, date, 3, 4, hexKey, plain, "3,4,0");
        starloco.Rows.Add(32003, date, 3, 4, "", plain, "5,6,0");
        string withoutClient = Path.Combine(work, "maps-without-client");
        var bare = new BotExportContext();
        Check(XmlParser.ExportBotRecords(withoutClient, "Maps", starloco.CreateDataReader(), bare) == 3, "Map export count");
        Check(Value(Path.Combine(withoutClient, "32001.xml"), "BACK") == "0" && bare.WarningCount == 1 && bare.Warnings[0].Contains("background"),
            "Without a client folder BACK must be 0 with one warning");

        string withClient = Path.Combine(work, "maps-with-client");
        var context = new BotExportContext { MapBackground = request => ResourceMapConversion.ReadClientBackground(clientMaps, request) };
        XmlParser.ExportBotRecords(withClient, "Maps", starloco.CreateDataReader(), context);
        Check(Value(Path.Combine(withClient, "32001.xml"), "BACK") == "342", "backgroundNum was not read from the encrypted client map");
        Check(Value(Path.Combine(withClient, "32002.xml"), "BACK") == "17", "A client map whose cells differ keeps the background the client shows");
        Check(Value(Path.Combine(withClient, "32003.xml"), "BACK") == "0", "A missing client map must give BACK = 0");
        Check(context.WarningCount == 2 && context.Warnings.Any(w => w.StartsWith("Carte 32002") && w.Contains("cellules"))
            && context.Warnings.Any(w => w.StartsWith("Carte 32003") && w.Contains("32003_" + date + ".swf")),
            "Mismatching and missing client maps must be reported: " + string.Join(" / ", context.Warnings));

        var request = new MapBackgroundRequest(32001, date, "6B6579", plain, 3, 4);
        Check(ResourceMapConversion.ReadClientBackground(clientMaps, request) == 342 && request.Warning.Contains("cellules"), "A wrong key must be reported");
        request = new MapBackgroundRequest(32001, "../" + date, hexKey, plain, 3, 4);
        Check(ResourceMapConversion.ReadClientBackground(clientMaps, request) == 0 && request.Warning != null, "A date with a path must be refused");
        File.WriteAllText(Path.Combine(clientMaps, "32004_" + date + "X.swf"), "pas un SWF");
        request = new MapBackgroundRequest(32004, date, hexKey, plain, 3, 4);
        Check(ResourceMapConversion.ReadClientBackground(clientMaps, request) == 0 && request.Warning.Contains("illisible"), "A broken SWF must be reported");

        var kryone = MapsTable(true);
        kryone.Rows.Add(32001, date, 3, 4, hexKey, plain, "1,2,0", 71);
        string stored = Path.Combine(work, "maps-with-column");
        var columnContext = new BotExportContext { MapBackground = r => ResourceMapConversion.ReadClientBackground(clientMaps, r) };
        XmlParser.ExportBotRecords(stored, "Maps", kryone.CreateDataReader(), columnContext);
        Check(Value(Path.Combine(stored, "32001.xml"), "BACK") == "71" && columnContext.WarningCount == 0, "The background column of the table must win");
    }

    private static void CheckParserWindow()
    {
        Application.EnableVisualStyles();
        using (var form = new RessourceParser())
        {
            var combo = (ComboBox)typeof(RessourceParser).GetField("iTalk_ComboBox1", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
            foreach (string type in new[] { "Interactifs", "Déclencheurs", "Zaapis", "Panoplies" })
                Check(combo.Items.Contains(type), "The parser does not offer " + type);
            Check(typeof(RessourceParser).GetField("clientFolder", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form) != null, "Client folder field is missing");
            MethodInfo directory = typeof(RessourceParser).GetMethod("BotDirectory", BindingFlags.Static | BindingFlags.NonPublic);
            var expected = new Dictionary<string, string> { { "Interactifs", "BotInteractives" }, { "Déclencheurs", "BotTriggers" }, { "Zaapis", "BotZaapis" },
                { "Panoplies", "BotItemSets" }, { "Maps", "BotMaps" } };
            foreach (var pair in expected)
            {
                string path = (string)directory.Invoke(null, new object[] { pair.Key });
                Check(path == Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ressources", "Bot", pair.Value), pair.Key + " is exported to " + path);
            }
        }
    }
}
