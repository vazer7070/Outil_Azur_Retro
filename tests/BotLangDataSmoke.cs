using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Data;

// Textes du client (LangData) : XML synthétiques écrits dans un dossier temporaire (aucun texte du client),
// substitutions #n / %n / ~n / {…} / [n], messages Im, repli « identifiant », XML corrompus, rechargement
// concurrent ; puis, si le dossier ressources/Bot/BotLang a été copié par la compilation, cohérence des XML livrés.
internal static class BotLangDataSmoke
{
    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(e.Name).Name + ".dll");
            if (!File.Exists(path)) path = Path.ChangeExtension(path, "exe");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try
        {
            string delivered = Run();
            Console.WriteLine("OK: LangData (dialogues, PNJ, cartes, monstres, objets, Im, quêtes, titres, interactifs), repli identifiant, XML corrompus, rechargement concurrent ; " + delivered);
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Equal(string expected, string actual, string what) { if (expected != actual) throw new Exception(what + " : attendu « " + expected + " », obtenu « " + actual + " »"); }

    private static void Write(string folder, string file, string family, string body)
    {
        File.WriteAllText(Path.Combine(folder, file), "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"" + family + "\" langue=\"fr\" version=\"1\" source=\"" + family + "_fr_1.swf\">\n" + body + "\n</BotLang>\n", new UTF8Encoding(false));
    }

    private static string Run()
    {
        string folder = Path.Combine(TestPaths.Work, "botlang-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        Write(folder, "dialog.xml", "dialog",
            "<question id=\"10\" texte=\"Bonjour #1, ton coffre coûte #2 kamas.&#10;À bientôt.\" />\n" +
            "<question id=\"11\" texte=\"Le rang #1~2 avec #2\" />\n" +
            "<question id=\"12\" texte=\"Parle à `SRVT:CHEF`.\" />\n" +
            "<reponse id=\"20\" texte=\"Oui\" />");
        Write(folder, "npc.xml", "npc", "<action id=\"1\" nom=\"Acheter\" />\n<action id=\"3\" nom=\"Parler\" />\n<pnj id=\"5\" nom=\"Marchand fictif\" actions=\"1,3\" />\n<pnj id=\"6\" nom=\"Muet fictif\" />");
        Write(folder, "lang.xml", "lang",
            "<texte cle=\"INFOS_168\" valeur=\"%1 est muet pendant %2 minutes.\" />\n" +
            "<texte cle=\"INFOS_54\" valeur=\"Nouvelle quête : &lt;b&gt;%1&lt;/b&gt;\" />\n" +
            "<texte cle=\"INFOS_21\" valeur=\"Tu as obtenu %1 '%2'.\" />\n" +
            "<texte cle=\"ERROR_6\" valeur=\"Impossible d'apprendre le métier %1.\" />\n" +
            "<texte cle=\"PVP_41\" valeur=\"Combat en %1, %2.\" />\n" +
            "<texte cle=\"ACCEPT\" valeur=\"Accepter\" />\n" +
            "<config cle=\"LINK\" valeur=\"http://exemple.invalid\" />");
        Write(folder, "maps.xml", "maps",
            "<carte id=\"100\" x=\"-3\" y=\"4\" sousZone=\"7\" />\n<carte id=\"101\" x=\"0\" y=\"0\" sousZone=\"8\" />\n" +
            "<sousZone id=\"7\" nom=\"Clairière fictive\" zone=\"1\" />\n<sousZone id=\"8\" nom=\"//Caché\" zone=\"1\" />\n<zone id=\"1\" nom=\"Contrée fictive\" superZone=\"0\" />");
        Write(folder, "monsters.xml", "monsters",
            "<monstre id=\"9\" nom=\"Bestiole\" gfx=\"1500\" race=\"1\"><grade n=\"1\" niveau=\"2\" resistances=\"1,-2,3,4,5,6,7\" /><grade n=\"2\" niveau=\"4\" resistances=\"2,3,4,5,6,7,8\" /></monstre>");
        Write(folder, "items.xml", "items", "<objet id=\"9\" nom=\"Épée #1\" type=\"6\" gfx=\"36\" niveau=\"5\" description=\"Lame #1.\" />\n<objet id=\"10\" nom=\"Blé \" type=\"6\" />\n<type id=\"6\" nom=\"Épée\" superType=\"2\" />\n<texteUnique id=\"0\" texte=\"fictive\" />");
        Write(folder, "jobs.xml", "jobs", "<metier id=\"2\" nom=\"Bûcheron fictif\" specialisation=\"0\" icone=\"1\" />");
        Write(folder, "spells.xml", "spells", "<sort id=\"3\" nom=\"Sort fictif\" description=\"Inflige des dégâts.\" />");
        Write(folder, "emotes.xml", "emotes", "<emote id=\"1\" nom=\"S'asseoir\" commande=\"sit\" />\n<emote id=\"3\" nom=\"Applaudir\" commande=\"appl\" />");
        Write(folder, "interactiveobjects.xml", "interactiveobjects", "<interactif id=\"3\" nom=\"Arbre fictif\" type=\"1\" competences=\"6,7\" />\n<gfx id=\"7500\" interactif=\"3\" />");
        Write(folder, "skills.xml", "skills", "<competence id=\"6\" nom=\"Couper\" metier=\"2\" interactif=\"3\" />");
        Write(folder, "titles.xml", "titles", "<titre id=\"1\" texte=\"Fléau de %1\" couleur=\"16777215\" typeParametre=\"1\" />\n<titre id=\"2\" texte=\"Héros de %1\" couleur=\"255\" typeParametre=\"0\" />");
        Write(folder, "quests.xml", "quests",
            "<quete id=\"7\" nom=\"Quête fictive\" />\n<etape id=\"4\" nom=\"Étape fictive\" description=\"Aller voir.\" />\n" +
            "<objectif id=\"1\" type=\"3\" parametres=\"5,9,2\" />\n<objectif id=\"2\" type=\"0\" parametres=\"[&quot;Lieu, virgule&quot;]\" />\n" +
            "<modele id=\"3\" texte=\"Rapporter #2 à #1 (#3)\" />\n<modele id=\"0\" texte=\"#1\" />");
        Write(folder, "servers.xml", "servers", "<entree table=\"SRVT\" id=\"1\" l=\"CHEF\" d=\"Chef commun\" />\n<entree table=\"SRVC\" id=\"1|2\" valeur=\"Chef du serveur 2\" />");
        File.WriteAllText(Path.Combine(folder, "corrompu.xml"), "<BotLang famille=\"casse\"><question id=\"1\" texte=\"non fermé\"", Encoding.UTF8);
        File.WriteAllText(Path.Combine(folder, "racine.xml"), "<Autre famille=\"autre\" />", Encoding.UTF8);

        // Avant tout chargement : repli sur l'identifiant, sans exception.
        LangData.Clear();
        Equal("5", LangData.Npc.Name(5), "PNJ sans npc.xml");
        Equal("10", LangData.Dialog.Question(10, new[] { "x" }), "question sans dialog.xml");
        Equal("100", LangData.Map.Name(100), "carte sans maps.xml");
        Check(LangData.Map.Coords(100) == null && LangData.Monster.Grade(9, 1) == null && LangData.Npc.Actions(5).Length == 0, "Valeurs absentes non nulles");
        Equal("INFOS_168 : Bob, 10", LangData.Text.FormatIm("0168;Bob~10").Text, "Im sans lang.xml");
        Check(LangData.EntryCount == 0 && LangData.Families.Length == 0, "Textes présents avant chargement");

        int families = Task.Run(() => LangData.Load(folder)).Result;
        Check(families == 14, "Familles chargées : " + families + " (" + string.Join(", ", LangData.Families) + ")");
        Check(LangData.LoadWarnings.Any(w => w.StartsWith("corrompu.xml")) && LangData.LoadWarnings.Any(w => w.StartsWith("racine.xml")) && LangData.LoadWarnings.Length == 2,
            "Avertissements : " + string.Join(" | ", LangData.LoadWarnings));
        Check(!LangData.IsLoaded("casse") && LangData.IsLoaded("dialog") && LangData.Version("dialog") == "1", "Famille corrompue chargée ou version perdue");
        Check(LangData.Count("dialog", "question") == 3 && LangData.Ids("npc", "pnj").SequenceEqual(new[] { "5", "6" }), "Comptes des tables");

        // Dialogues : #n, ~n, `SRVT:…`
        Equal("Bonjour Bob, ton coffre coûte 150 kamas.\nÀ bientôt.", LangData.Dialog.Question(10, new[] { "Bob", "150" }), "#1/#2");
        Equal("Bonjour , ton coffre coûte  kamas.\nÀ bientôt.", LangData.Dialog.Question(10), "#n sans paramètre");
        Equal("Le rang A", LangData.Dialog.Question(11, new[] { "A" }), "~2 sans deuxième paramètre");
        Equal("Le rang A avec B", LangData.Dialog.Question(11, new[] { "A", "B" }), "~2 avec deuxième paramètre");
        Equal("Parle à Chef commun.", LangData.Dialog.Question(12), "SRVT commun");
        Equal("Parle à Chef du serveur 2.", LangData.Text.Fetch("Parle à `SRVT:CHEF`.", 2), "SRVT du serveur");
        Equal("Oui", LangData.Dialog.Answer(20), "réponse");
        Equal("999", LangData.Dialog.Answer(999), "réponse absente");
        Check(LangData.Dialog.HasQuestion(10) && !LangData.Dialog.HasQuestion(13), "HasQuestion");
        Equal("xZy", LangData.Describe("x{#1}y", new[] { "Z" }), "bloc {}");
        Equal("P reste", LangData.Describe("[0]reste", new[] { "P" }), "[n]");
        Equal("a#", LangData.Describe("a#", new string[0]), "# final");
        Equal("{ouvert", LangData.Describe("{ouvert", null), "{ sans }");
        Equal("Zone", LangData.Combine("Zone{~ps}", "m", true), "accord singulier");
        Equal("Zones", LangData.Combine("Zone{~ps}", "m", false), "accord pluriel");
        Equal("Possédées", LangData.Combine("Possédé{~fe}{~ps}", "f", false), "accord féminin pluriel");
        Equal("Possédé", LangData.Combine("Possédé{~fe}{~ps}", "m", true), "accord masculin singulier");

        // PNJ, cartes, monstres, objets, interactifs, émotes, titres, quêtes
        Equal("Marchand fictif", LangData.Npc.Name(5), "nom de PNJ");
        Check(LangData.Npc.Actions(5).SequenceEqual(new[] { 1, 3 }) && LangData.Npc.ActionNames(5).SequenceEqual(new[] { "Acheter", "Parler" }) && LangData.Npc.Actions(6).Length == 0, "actions de PNJ");
        Equal("404", LangData.Npc.Name(404), "PNJ absent");
        Equal("Contrée fictive (Clairière fictive)", LangData.Map.Name(100), "nom de carte");
        Equal("Contrée fictive", LangData.Map.Name(101), "sous-zone masquée par //");
        Check(LangData.Map.Coords(100) == new Point(-3, 4) && LangData.Map.SubAreaId(100) == 7 && LangData.Map.AreaId(101) == 1, "coordonnées / zones");
        Equal("//Caché", LangData.Map.SubArea(101), "sous-zone brute");
        Equal("Contrée fictive", LangData.Map.Area(100), "zone");
        LangData.MonsterGrade grade = LangData.Monster.Grade(9, 2);
        Check(grade != null && grade.Level == 4 && grade.Resistances.Length == 7 && grade.Resistances[0] == 2 && LangData.Monster.Grade(9, 6) == null, "grade de monstre");
        Check(LangData.Monster.Grade(9, 1).Resistances[1] == -2 && LangData.Monster.Gfx(9) == 1500, "résistance négative / gfx");
        var fighter = (Tool_BotProtocol.Game.Maps.Entities.FightMonsterActor)Tool_BotProtocol.Game.Maps.GmParser
            .Parse("GM|+6;1;0;-8;9;-2;1500^100;2;-1;-1;-1;0,0,0,0;50;6;3;1", true).Entries.Single().Actor;
        Check(fighter.Grade == 2 && fighter.Level == 4, "niveau d'un monstre en combat (grade 2) : " + fighter.Level);
        Equal("Épée fictive", LangData.Item.Name(9), "nom d'objet (I.us)");
        Equal("Blé", LangData.Item.Name(10), "nom d'objet sans l'espace finale d'items_fr");
        Equal("Lame fictive.", LangData.Item.Description(9), "description d'objet");
        Check(LangData.Item.Gfx(9) == 36 && LangData.Item.Type(9) == 6 && LangData.Item.TypeName(6) == "Épée", "objet : gfx/type");
        Equal("Arbre fictif", LangData.Interactive.Name(7500), "interactif par gfx");
        Check(LangData.Interactive.Skills(7500).SequenceEqual(new[] { 6, 7 }) && LangData.Interactive.Type(7500) == 1 && LangData.Interactive.Skills(1).Length == 0, "compétences de l'interactif");
        Equal("1", LangData.Interactive.Name(1), "interactif absent");
        Check(LangData.Skill.Name(6) == "Couper" && LangData.Skill.Job(6) == 2 && LangData.Job.Name(2) == "Bûcheron fictif", "compétence / métier");
        Check(LangData.Emote.IdFromCommand("sit") == 1 && LangData.Emote.IdFromCommand("dance") == null && LangData.Emote.Ids.SequenceEqual(new[] { 1, 3 }), "émotes");
        Check(LangData.Spell.Name(3) == "Sort fictif" && LangData.Spell.Name(4) == "4", "sorts");
        Check(LangData.Smiley.Count == 15, "smileys du client");
        Equal("« Fléau de Bestiole »", LangData.Title.Name(1, "9"), "titre avec monstre");
        Equal("« Héros de Bob »", LangData.Title.Name(2, "Bob"), "titre avec texte");
        Check(LangData.Title.Color(1) == 16777215, "couleur de titre");
        Equal("Rapporter Épée #1 à Marchand fictif (2)", LangData.Quest.Objective(1), "objectif de quête (paramètre non relu)");
        Equal("Lieu, virgule", LangData.Quest.Objective(2), "objectif JSON");
        Check(LangData.Quest.Name(7) == "Quête fictive" && LangData.Quest.StepName(4) == "Étape fictive", "quête / étape");
        IReadOnlyDictionary<string, string> raw = LangData.Raw("servers", "SRVT", "1");
        Check(raw != null && raw["l"] == "CHEF" && LangData.Raw("servers", "SRVT", "2") == null, "accès brut");

        // Textes d'interface et Im
        Equal("Bob est muet pendant 10 minutes.", LangData.Text.Get("INFOS_168", "Bob", "10"), "getText %1 %2");
        Equal("!ABSENT!", LangData.Text.Get("ABSENT"), "clé absente");
        LangData.ImMessage im = LangData.Text.FormatIm("0168;Bob~10");
        Check(im != null && im.Text == "Bob est muet pendant 10 minutes." && im.Channel == LangData.ImChannel.Info && im.Keys.SequenceEqual(new[] { "INFOS_168" }), "Im INFOS_168");
        im = LangData.Text.FormatIm("1006;2");
        Check(im.Text == "Impossible d'apprendre le métier Bûcheron fictif." && im.Channel == LangData.ImChannel.Error, "Im ERROR_6 (métier) : " + im.Text);
        im = LangData.Text.FormatIm("054;7");
        Check(im.Text == "Nouvelle quête : <b>Quête fictive</b>" && LangData.Text.Plain(im.Text) == "Nouvelle quête : Quête fictive", "Im INFOS_54 (quête) : " + im.Text);
        Equal("Tu as obtenu 3 'Épée fictive'.", LangData.Text.FormatIm("021;3~9").Text, "Im INFOS_21 (objet)");
        Equal("Combat en Clairière fictive, Contrée fictive.", LangData.Text.FormatIm("241;7~1").Text, "Im PVP_41 (zones)");
        Equal("A est muet pendant 1 minutes. B est muet pendant 2 minutes.", LangData.Text.FormatIm("0168;A~1|168;B~2").Text, "Im à deux messages");
        Equal("Bob est muet pendant 10 minutes.", LangData.Text.FormatIm("0168;Bob~10;ignoré").Text, "paramètres après le second ;");
        Equal("Accepter", LangData.Text.FormatIm("0ACCEPT").Text, "Im par clé");
        Equal("Accepter", LangData.Text.Im(0, "ACCEPT", null), "Text.Im par clé");
        Check(LangData.Text.FormatIm(null) == null && LangData.Text.FormatIm("") == null && LangData.Text.FormatIm("9168") == null && LangData.Text.FormatIm("0") == null && LangData.Text.FormatIm("0|") == null, "Im vides");
        foreach (string bad in new[] { "0;", "0;;", "1~~", "2|||", "0021", "0021;", "0150;x", "0151", "0123;a", "1007;abc", "286;", "0" + new string('9', 40) })
            LangData.Text.FormatIm(bad); // ne doit jamais lever

        // Rechargement concurrent : les lectures restent cohérentes, sans exception.
        Exception readerError = null;
        int reads = 0;
        using (var stop = new ManualResetEventSlim())
        {
            var reader = new Thread(() =>
            {
                try
                {
                    while (!stop.IsSet)
                    {
                        string name = LangData.Npc.Name(5);
                        if (name != "Marchand fictif" && name != "5") throw new Exception("Lecture incohérente : " + name);
                        LangData.Map.Name(100); LangData.Text.FormatIm("0168;a~b"); reads++;
                    }
                }
                catch (Exception error) { readerError = error; }
            }) { IsBackground = true };
            reader.Start();
            for (int i = 0; i < 15; i++) { LangData.Load(folder); LangData.Load(Path.Combine(folder, "absent")); }
            LangData.Load(folder);
            stop.Set();
            reader.Join(5000);
        }
        Check(readerError == null, "Lecture pendant le rechargement : " + readerError);
        Check(reads > 0, "Le lecteur concurrent n'a rien lu");
        LangData.Load(Path.Combine(folder, "absent"));
        Check(LangData.LoadWarnings.Length == 1 && LangData.LoadWarnings[0].Contains("introuvable") && LangData.Npc.Name(5) == "5", "Dossier absent");

        return CheckDelivered();
    }

    /// <summary>XML livrés (copiés par la compilation) : se chargent sans avertissement et se référencent entre eux.</summary>
    private static string CheckDelivered()
    {
        string folder = Path.Combine(TestPaths.ApplicationBin, "ressources", "Bot", "BotLang");
        if (!Directory.Exists(folder) || Directory.GetFiles(folder, "*.xml").Length == 0) return "XML livrés absents de " + folder + " (non vérifiés)";
        var watch = Stopwatch.StartNew();
        LangData.Load(folder);
        long loadMs = watch.ElapsedMilliseconds;
        Check(LangData.LoadWarnings.Length == 0, "XML livrés : " + string.Join(" | ", LangData.LoadWarnings));
        foreach (string family in new[] { "dialog", "npc", "maps", "monsters", "items", "spells", "emotes", "lang", "interactiveobjects", "skills", "jobs", "titles", "quests" })
            Check(LangData.IsLoaded(family) && !string.IsNullOrEmpty(LangData.Version(family)), "Famille livrée absente : " + family);
        foreach (string[] table in new[] { new[] { "dialog", "question" }, new[] { "dialog", "reponse" }, new[] { "npc", "pnj" }, new[] { "npc", "action" }, new[] { "maps", "carte" }, new[] { "maps", "sousZone" }, new[] { "maps", "zone" }, new[] { "monsters", "monstre" }, new[] { "items", "objet" }, new[] { "spells", "sort" }, new[] { "emotes", "emote" }, new[] { "lang", "texte" }, new[] { "interactiveobjects", "gfx" }, new[] { "skills", "competence" }, new[] { "jobs", "metier" }, new[] { "titles", "titre" }, new[] { "quests", "objectif" } })
            Check(LangData.Count(table[0], table[1]) > 0, "Table livrée vide : " + table[0] + "/" + table[1]);
        int npcActions = 0, maps = 0, gfx = 0, objectives = 0, imKeys = 0;
        foreach (string id in LangData.Ids("npc", "pnj"))
            foreach (int action in LangData.Npc.Actions(int.Parse(id))) { Check(LangData.Raw("npc", "action", action.ToString()) != null, "Action de PNJ inconnue : " + action); npcActions++; }
        foreach (string id in LangData.Ids("maps", "carte"))
        {
            int map = int.Parse(id);
            int? sub = LangData.Map.SubAreaId(map);
            Check(LangData.Map.Coords(map) != null && sub != null && LangData.Raw("maps", "sousZone", sub.Value.ToString()) != null, "Carte sans sous-zone : " + id);
            Check(!string.IsNullOrEmpty(LangData.Map.Name(map)), "Nom de carte vide : " + id); maps++;
        }
        foreach (string id in LangData.Ids("interactiveobjects", "gfx"))
        {
            int? data = LangData.Interactive.IdFromGfx(int.Parse(id));
            Check(data != null && LangData.Raw("interactiveobjects", "interactif", data.Value.ToString()) != null, "Gfx d'interactif sans entrée : " + id); gfx++;
        }
        foreach (string id in LangData.Ids("dialog", "question")) LangData.Dialog.Question(int.Parse(id), new[] { "a", "b", "c" });
        foreach (string id in LangData.Ids("items", "objet")) LangData.Item.Name(int.Parse(id));
        foreach (string id in LangData.Ids("quests", "objectif")) { LangData.Quest.Objective(int.Parse(id)); objectives++; }
        foreach (string key in LangData.Ids("lang", "texte"))
        {
            int n;
            foreach (string prefix in new[] { "INFOS_", "ERROR_", "PVP_" })
                if (key.StartsWith(prefix) && int.TryParse(key.Substring(prefix.Length), out n))
                {
                    char type = prefix == "INFOS_" ? '0' : prefix == "ERROR_" ? '1' : '2';
                    LangData.ImMessage message = LangData.Text.FormatIm(type + n.ToString() + ";1~2~3~4");
                    // Une valeur vide dans le fichier du client donne « !CLÉ! », comme dans le client.
                    bool empty = string.IsNullOrEmpty(LangData.Raw("lang", "texte", key)["valeur"]);
                    Check(message != null && (empty ? message.Text == "!" + key + "!" : !message.Text.StartsWith("!")), "Clé Im non résolue : " + key);
                    imKeys++;
                }
        }
        int entries = LangData.EntryCount;
        LangData.Clear();
        return "XML livrés : " + entries + " entrées chargées en " + loadMs + " ms, " + maps + " cartes, " + npcActions + " actions de PNJ, " + gfx + " gfx d'interactifs, " + objectives + " objectifs, " + imKeys + " messages Im cohérents";
    }
}
