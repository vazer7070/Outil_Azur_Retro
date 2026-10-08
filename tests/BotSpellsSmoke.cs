using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Perso.Spells;

internal static class BotSpellsSmoke
{
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(args.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        Run().GetAwaiter().GetResult();
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static Accounts Account() { return new Accounts(new AccountConfig("synthetic-spell-test", "synthetic", "loopback")); }
    private static async Task Run()
    {
        Dictionary<short, Spell> originalTemplates = Spell.AllSpells;
        Spell.AllSpells = new Dictionary<short, Spell>();
        try
        {
            var template = new Spell(7, "Flèche de test");
            for (byte level = 1; level <= 6; level++)
            {
                var stats = new SpellStats { PA = 3, Min_portee = 1, Max_portee = 6,
                    AvecLigneDeVue = true, portee_modifiable = true, PerTurn = 2, PerObjective = 1, Interval = 0 };
                stats.NormalEffect.Add(new SpellEffect(100, new Zones(SpellActionZone.CERCLE, 2)));
                stats.CriticalEffect.Add(new SpellEffect(101, new Zones(SpellActionZone.SOLO, 0)));
                template.GetSpellsStats(level, stats);
            }
            MessagesReception.Init();
            Check(MessagesReception.messagesDatas.Count(data => data.MessageName == "SL") == 1, "SL handler missing or duplicated");
            using (var first = Account())
            using (var second = Account())
            {
                int refreshes = 0;
                var logs = new List<string>();
                first.Game.character.Spells_Refresh += () => Interlocked.Increment(ref refreshes);
                first.Logger.log_event += (entry, color) => logs.Add(entry.message);
                await MessagesReception.ReceptionAsync(first.Connexion, "SL7~2~a;999~1~null;");
                await MessagesReception.ReceptionAsync(second.Connexion, "SL7~3~b;");
                Spell firstSpell = first.Game.character.Spells[7], secondSpell = second.Game.character.Spells[7];
                Check(first.Game.character.Spells.Count == 2 && firstSpell.Name == "Flèche de test" && firstSpell.Level == 2 && firstSpell.Position == "a", "SL did not populate the character spell list");
                Check(first.Game.character.Spells[999].Name == "Sort #999" && !first.Game.character.Spells[999].HasMetadata && first.Game.character.Spells[999].GetStats() == null,
                    "Unknown spell metadata was not represented safely");
                Check(!Spell.AllSpells.ContainsKey(999) && template.Level == 0, "Character spell state polluted global resource templates");
                Check(!ReferenceEquals(firstSpell, secondSpell) && !ReferenceEquals(firstSpell.Stats[2], template.Stats[2]), "Mutable spell metadata was shared between accounts");
                firstSpell.Stats[2].PA = 9;
                firstSpell.Stats[2].NormalEffect[0].Id = 123;
                firstSpell.Stats[2].NormalEffect[0].ZoneEffet.taille = 10;
                firstSpell.Stats[2].CriticalEffect[0].ZoneEffet.taille = 4;
                Check(template.Stats[2].PA == 3 && secondSpell.Stats[2].PA == 3 &&
                    template.Stats[2].NormalEffect[0].Id == 100 && template.Stats[2].NormalEffect[0].ZoneEffet.taille == 2 &&
                    secondSpell.Stats[2].NormalEffect[0].ZoneEffet.taille == 2 && template.Stats[2].CriticalEffect[0].ZoneEffet.taille == 0,
                    "Spell effects or zones leaked across accounts or global resources");
                await MessagesReception.ReceptionAsync(first.Connexion, "SUK7~4");
                Check(first.Game.character.Spells[7].Level == 4 && first.Game.character.Spells[7].Position == "a" &&
                    second.Game.character.Spells[7].Level == 3 && template.Level == 0, "SUK failed to apply only the character level while retaining its position");
                await MessagesReception.ReceptionAsync(first.Connexion, "SUK999~2");
                Check(first.Game.character.Spells[999].Level == 2, "SUK rejected a spell without resource metadata");
                await MessagesReception.ReceptionAsync(first.Connexion, "SUE");
                Check(first.Game.character.Spells[7].Level == 4 && logs.Any(log => log.Contains("Amélioration du sort refusée")), "SUE changed spell state or lacked a French diagnostic");
                await MessagesReception.ReceptionAsync(first.Connexion, "SUK7~7");
                await MessagesReception.ReceptionAsync(first.Connexion, "SUKbad");
                Check(first.Game.character.Spells[7].Level == 4, "Malformed upgrade changed a learned spell");
                await MessagesReception.ReceptionAsync(first.Connexion, "SL7~1~a;7~3~c;bad;8~0~d;9~1~too-long;");
                Check(first.Game.character.Spells.Count == 1 && first.Game.character.Spells[7].Level == 3 && first.Game.character.Spells[7].Position == "c",
                    "Repeated SL contained duplicate, stale or malformed spell entries");
                // SLo (option « voir tous les sorts ») a son propre gestionnaire : il ne vide plus la liste.
                await MessagesReception.ReceptionAsync(first.Connexion, "SLo+");
                Check(first.Game.character.Spells.Count == 1 && first.Game.character.SpellBook.CanSeeAllSpells, "SLo+ was routed to SL");
                await MessagesReception.ReceptionAsync(first.Connexion, "SLo-");
                Check(first.Game.character.Spells.Count == 1 && !first.Game.character.SpellBook.CanSeeAllSpells, "SLo- was routed to SL");
                await MessagesReception.ReceptionAsync(first.Connexion, "SL");
                Check(first.Game.character.Spells.IsEmpty && second.Game.character.Spells.Count == 1, "Empty SL retained spells or cleared another account");
                Check(refreshes >= 6, "Spell updates did not notify the UI");
                Task reader = Task.Run(() =>
                {
                    for (int index = 0; index < 5000; index++)
                    {
                        var snapshot = first.Game.character.Spells.ToArray();
                        Check(snapshot.Length <= 2 && snapshot.All(entry => entry.Value.Level >= 1 && entry.Value.Level <= 6), "Concurrent UI snapshot was invalid");
                    }
                });
                for (int index = 0; index < 100; index++)
                    await MessagesReception.ReceptionAsync(first.Connexion, index % 2 == 0 ? "SL7~1~a;999~1~null;" : "SL7~2~b;");
                await reader;
            }
        }
        finally { Spell.AllSpells = originalTemplates; }
        Console.WriteLine("OK: StarLoco SL/SUK/SUE spell state, SLo kept apart from SL, per-character metadata/effect isolation, unknown metadata, repeated/empty lists, malformed packets and concurrent UI snapshots");
    }
}
