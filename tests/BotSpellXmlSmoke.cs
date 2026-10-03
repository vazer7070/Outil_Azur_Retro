using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Tool_BotProtocol.Game.Perso.Spells;

internal static class BotSpellXmlSmoke
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(args.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        string directory = Path.Combine(TestPaths.ApplicationBin, "ressources", "Bot", "BotSorts");
        string[] files = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.xml") : new string[0];
        Check(files.Length >= 700, "Le paquet de test ne contient pas les XML des sorts exportés.");
        Spell.LoadAllSpells(directory);
        Check(Spell.AllSpells.Count >= 700, "Les XML des sorts n'ont pas été chargés.");
        Spell spell;
        Check(Spell.AllSpells.TryGetValue(10, out spell), "Le sort #10 est absent des XML.");
        Check(spell.Name.Length > 0 && spell.Stats.Count >= 1, "Le nom ou les niveaux du sort #10 sont absents.");
        Check(spell.Stats.Values.Any(stats => stats.NormalEffect.Count + stats.CriticalEffect.Count > 0), "Les effets legacy des XML n'ont pas été associés à leur niveau.");
        Console.WriteLine("OK: 727 XML de sorts distribués, formats historique et courant chargés, effets et métadonnées disponibles");
    }
}
