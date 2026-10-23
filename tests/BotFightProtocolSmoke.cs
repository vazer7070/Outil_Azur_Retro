using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Combats;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Perso.Spells;
using Tool_BotProtocol.Utils.Crypto;
using Tool_BotProtocol.Utils.Logger;

/// <summary>
/// Protocole de combat F12a sur paquets synthétiques (format StarLoco / client 1.34) : ordre des tours (GTL, GTR→GT, GTS à deux
/// ou trois champs), effets (GIE, GIe), zones (GDZ), options (Go, fN/fS/fP/fH), drapeau (Gf), abandon et exclusion (GQ),
/// table GA de combat (vie, PA/PM, états, invisibilité, porter/lancer, invocations, GA;999) et résultat GE (PvM, PvP, mal formé).
/// Lot X1 : corps à corps <c>GA303&lt;cellule&gt;</c> à mains nues, à l'épée puis à l'arc (items.xml et classes.xml synthétiques),
/// <c>GAF</c> → <c>GKK</c>, refus locaux (PA, adjacence, portée, état interdit) et réponses StarLoco (Im1170, GA;305).
/// Lot X8 : zone du type d'arme posée sous le pointeur (« Pa » de l'épée, « Xb » du marteau, « P » à défaut).
/// Lot X9 : défis <c>Gd</c> (textes d'un fightChallenge.xml synthétique, cible « nom (Niv. n) »), <c>GdOK</c> / <c>GdKO</c>,
/// paquets illisibles journalisés, <c>Gdi&lt;type&gt;</c> et ses refus, copie dans le résultat <c>GE</c>, vidage par <c>GV</c>.
/// </summary>
internal static class BotFightProtocolSmoke
{
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(args.Name).Name + ".dll");
            if (!File.Exists(path)) path = Path.ChangeExtension(path, "exe");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try { Run().GetAwaiter().GetResult(); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    private static async Task<T> Within<T>(Task<T> task)
    { if (await Task.WhenAny(task, Task.Delay(6000)) != task) throw new TimeoutException("Fight protocol loopback timed out"); return await task; }

    private static Task<string> Read(Socket peer)
    {
        return Task.Run(() =>
        {
            using (var stream = new MemoryStream())
            {
                byte[] one = new byte[1];
                while (true)
                {
                    if (peer.Receive(one) != 1) throw new IOException("Loopback disconnected");
                    if (one[0] == 0) return Encoding.UTF8.GetString(stream.ToArray()).TrimEnd('\r', '\n');
                    stream.WriteByte(one[0]);
                }
            }
        });
    }

    private static Task Feed(Accounts account, string packet) { return MessagesReception.ReceptionAsync(account.Connexion, packet); }

    private static string LastJournal(Fights fight) { FightLogEntry entry = fight.Journal.LastOrDefault(); return entry == null ? string.Empty : entry.Text; }

    private static void WriteLang(string folder, string family, string body) =>
        File.WriteAllText(Path.Combine(folder, family + ".xml"), "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"" + family
            + "\" langue=\"fr\" version=\"1\" source=\"" + family + "_fr_1.swf\">\n" + body + "\n</BotLang>\n", new UTF8Encoding(false));

    /// <summary>
    /// Corps à corps (lot X1), au tour du personnage 42 sur la cellule 3 : poings par défaut (4 PA, portée 1, sort 0 de StarLoco),
    /// puis épée (5 PA, 1-1) et arc (4 PA, 2-6) lus dans un items.xml synthétique au format de lang2xml.py, poings de la classe 9
    /// dans un classes.xml synthétique (états interdits 1, 3, 18, 42 comme le client).
    /// </summary>
    private static async Task CloseCombatAsync(Accounts account, Socket peer, Fights fight)
    {
        LangData.Clear();
        account.Game.character.Race_ID = 9;
        Map map = account.Game.Map;
        Cell origin = account.Game.character.Cell;
        Check(origin != null && origin.CellID == 3 && fight.IsMyTurn && !fight.IsActionPending, "Close combat section must start on the own turn at cell 3");
        Cell near = map.MapCells.FirstOrDefault(c => c != null && c.IsActive && c.CellID != 6 && origin.GetDistanceBetweenCells(c) == 1);
        Cell far = map.MapCells.FirstOrDefault(c => c != null && c.IsActive && c.CellID != 6 && origin.GetDistanceBetweenCells(c) == 2);
        Check(near != null && far != null, "The synthetic map has no free cell at distance 1 and 2 of cell 3");
        await Feed(account, "GA;4;42;-7," + near.CellID);
        Check(fight.Fighters[-7].CellId == near.CellID, "Monster not moved next to the character");
        string reset = "GTM|42;0;100;8;3;3;;100";

        // Mains nues, sans textes du client : 4 PA, portée 1 (sort 0 « Coup de poing » de StarLoco au niveau 1).
        CloseCombatStats bare = fight.CloseCombat;
        Check(!bare.IsArmed && bare.ActionPoints == 4 && bare.MinRange == 1 && bare.MaxRange == 1 && bare.Source == "défaut", "Default bare-handed close combat");
        Check(fight.GetSpellUnavailableReason(Fights.CloseCombatSpellId, near.CellID) == null
            && fight.GetSpellUnavailableReason(Fights.CloseCombatSpellId, far.CellID) == fight.GetCloseCombatUnavailableReason(far.CellID),
            "Spell 0 of the bar is not the close combat");
        Check(fight.GetCloseCombatUnavailableReason(far.CellID).Contains("adjacente") && !(await fight.AttackAsync(far.CellID)).Sent
            && !(await fight.AttackAsync(origin.CellID)).Sent && peer.Available == 0, "Non-adjacent fists attack sent");
        await Feed(account, "GA;102;42;42,-5");
        Check(fight.ActionPoints == 3 && fight.GetCloseCombatUnavailableReason(near.CellID).Contains("PA insuffisants (4")
            && !(await fight.AttackAsync(near.CellID)).Sent && peer.Available == 0, "Fists attack sent without enough AP");
        await Feed(account, reset);
        int life = fight.Fighters[-7].Life;
        Check((await fight.CastSpellAsync(Fights.CloseCombatSpellId, near.CellID)).Sent && await Within(Read(peer)) == "GA303" + near.CellID,
            "Spell 0 must send GA303<cell> like the client");
        Check(fight.IsActionPending && !(await fight.AttackAsync(near.CellID)).Sent && peer.Available == 0, "Duplicate attack while pending");
        // StarLoco tryCaC sans arme : tryCastSpell(sort 0) dans Fight.cast, verrou « casting » levé une seconde après par GA;102 -0.
        await Feed(account, "GAS42"); await Feed(account, "GA;300;42;0," + near.CellID + ",0,1"); await Feed(account, "GA;100;42;-7,-3");
        await Feed(account, "GA;102;42;42,-4"); await Feed(account, "GAF0|42");
        Check(await Within(Read(peer)) == "GKK0" && fight.IsActionPending && fight.LastActionMessage.Contains("Coup de poing"),
            "GAF of a fists attack must be acknowledged while StarLoco keeps its casting lock");
        await Feed(account, "GA;102;42;42,-0");
        Check(!fight.IsActionPending && fight.ActionPoints == 4 && fight.Fighters[-7].Life == life - 3, "Fists casting unlock, AP or damage");

        // Textes du client : épée et arc (attribut arme = I.u[id].e), poings de la classe 9 (attribut cc = G[9].cc).
        string lang = Path.Combine(TestPaths.Work, "x1-lang");
        Directory.CreateDirectory(lang);
        foreach (string old in Directory.GetFiles(lang, "*.xml")) File.Delete(old);
        WriteLang(lang, "items", "<type id=\"6\" nom=\"Epée\" superType=\"2\" zone=\"Pa\" />\n<type id=\"2\" nom=\"Arc\" superType=\"2\" zone=\"Pa\" />\n"
            + "<type id=\"7\" nom=\"Marteau\" superType=\"2\" zone=\"Xb\" />\n<type id=\"4\" nom=\"Bâton\" superType=\"2\" zone=\"?\" />\n"
            + "<objet id=\"7001\" nom=\"Marteau synthétique\" type=\"7\" gfx=\"1\" niveau=\"1\" arme=\"30,5,1,1,25,50,false,false\" />\n"
            + "<objet id=\"7002\" nom=\"Bâton synthétique\" type=\"4\" gfx=\"1\" niveau=\"1\" arme=\"30,5,1,1,25,50,false,false\" />\n"
            + "<objet id=\"2170\" nom=\"Épée synthétique\" type=\"6\" gfx=\"25\" niveau=\"1\" arme=\"30,5,1,1,25,50,false,false\" />\n"
            + "<objet id=\"6783\" nom=\"Arc synthétique\" type=\"2\" gfx=\"1\" niveau=\"1\" arme=\"5,4,2,6,50,50,false,true\" />\n"
            + "<objet id=\"6987\" nom=\"Pierre synthétique\" type=\"83\" gfx=\"1\" niveau=\"1\" />");
        WriteLang(lang, "classes", "<entree table=\"G\" id=\"9\" cc=\"[[[100,1,5,1]],3,4,1,1,20,50,false,false,[],[1,3,18,42]]\" />");
        Check(LangData.Load(lang) == 2, "Synthetic items.xml/classes.xml not loaded: " + string.Join(" / ", LangData.LoadWarnings));
        try
        {
            CloseCombatStats fists = fight.CloseCombat;
            Check(!fists.IsArmed && fists.Source == "classe" && fists.ActionPoints == 4 && fists.CriticalHit == 20 && fists.CriticalFailure == 50
                && fists.ForbiddenStates.SequenceEqual(new[] { 1, 3, 18, 42 }) && fists.RequiredStates.Count == 0, "Class cc not read");

            // Épée en position 1 (OAK hexadécimal : objet 10, modèle 0x87a = 2170, position 1).
            await Feed(account, "OAKOa~87a~1~1~");
            CloseCombatStats sword = fight.CloseCombat;
            Check(sword.IsArmed && sword.WeaponTemplateId == 2170 && sword.Name == "Épée synthétique" && sword.ActionPoints == 5 && sword.MinRange == 1
                && sword.MaxRange == 1 && sword.CriticalHit == 25 && !sword.LineOnly && sword.ForbiddenStates.Contains(3) && sword.Summary.Contains("5 PA"),
                "Weapon in position 1 not read from items.xml");
            // Lot X8 : zone de l'arme sous le pointeur (I.t[type].z) : « Pa » de l'épée, croix « Xb » du marteau ; zone illisible ou poings : « P ».
            Check(sword.EffectZones.Count == 1 && sword.EffectZones[0].ToString() == "P0" && fists.EffectZones.Count == 1 && fists.EffectZones[0].ToString() == "P0"
                && string.Join(",", CloseCombatStats.For(9, 7001).EffectZones) == "X1" && string.Join(",", CloseCombatStats.For(9, 7002).EffectZones) == "P0"
                && string.Join(",", SpellAim.ForCloseCombat(CloseCombatStats.For(9, 7001)).EffectZones) == "X1",
                "Weapon type zone not used for the pointer: " + string.Join(",", CloseCombatStats.For(9, 7001).EffectZones));
            await Feed(account, "GA;950;42;42,3,1");
            Check(fight.GetCloseCombatUnavailableReason(near.CellID).Contains("état") && !(await fight.AttackAsync(near.CellID)).Sent && peer.Available == 0,
                "Close combat allowed in a forbidden state of the class");
            await Feed(account, "GA;950;42;42,3,0");
            await Feed(account, "GA;102;42;42,-4");
            Check(fight.GetCloseCombatUnavailableReason(near.CellID).Contains("PA insuffisants (5") && !(await fight.AttackAsync(near.CellID)).Sent
                && peer.Available == 0, "Sword attack sent without its 5 AP");
            await Feed(account, reset);
            life = fight.Fighters[-7].Life;
            int journal = fight.Journal.Count;
            Check((await fight.AttackAsync(near.CellID)).Sent && await Within(Read(peer)) == "GA303" + near.CellID && fight.IsActionPending, "Sword GA303 wire");
            await Feed(account, "GAS42"); await Feed(account, "GA;303;42;" + near.CellID); await Feed(account, "GA;100;42;-7,-10");
            await Feed(account, "GA;102;42;42,-5"); await Feed(account, "GAF0|42");
            Check(await Within(Read(peer)) == "GKK0" && !fight.IsActionPending && fight.LastActionMessage.Contains("corps à corps confirmée")
                && fight.ActionPoints == 3 && fight.Fighters[-7].Life == life - 10, "Armed attack: GAF must release the action and be acknowledged");
            Check(fight.Journal.Skip(journal).Any(entry => entry.ActionId == 303 && entry.Text.Contains("attaque au corps à corps")), "GA;303 not in the journal");

            // Refus de StarLoco (Im1170 dans Fight.cast) puis échec critique (GA;305, fin du tour) : GAF acquitté, attente levée.
            await Feed(account, reset);
            Check((await fight.AttackAsync(near.CellID)).Sent && await Within(Read(peer)) == "GA303" + near.CellID, "Second sword GA303");
            await Feed(account, "GAS42"); await Feed(account, "Im1170;3~5"); await Feed(account, "GAF0|42");
            Check(await Within(Read(peer)) == "GKK0" && !fight.IsActionPending && fight.LastActionMessage.Contains("PA insuffisants"), "Im1170 refusal of GA303");
            Check((await fight.AttackAsync(near.CellID)).Sent && await Within(Read(peer)) == "GA303" + near.CellID, "Third sword GA303");
            await Feed(account, "GAS42"); await Feed(account, "GA;305;42;");
            Check(fight.LastActionMessage.Contains("Échec critique au corps à corps") && LastJournal(fight).Contains("échec critique au corps"), "GA;305 not confirmed");
            await Feed(account, "GAF0|42");
            Check(await Within(Read(peer)) == "GKK0" && !fight.IsActionPending, "GAF after GA;305");

            // Arc (OM : épée rangée, puis arc 0x1a7f = 6783 en position 1) : portée 2 à 6, la cible adjacente est refusée.
            await Feed(account, "OM10|"); await Feed(account, "OAKOb~1a7f~1~1~"); await Feed(account, reset);
            CloseCombatStats bow = fight.CloseCombat;
            Check(bow.WeaponTemplateId == 6783 && bow.ActionPoints == 4 && bow.MinRange == 2 && bow.MaxRange == 6 && bow.LineOfSight, "Bow stats");
            Check(fight.GetCloseCombatUnavailableReason(near.CellID).Contains("hors portée (2–6)") && !(await fight.AttackAsync(near.CellID)).Sent
                && peer.Available == 0, "Bow accepted an adjacent target");
            Check(fight.GetCloseCombatUnavailableReason(far.CellID) == null && (await fight.AttackAsync(far.CellID)).Sent
                && await Within(Read(peer)) == "GA303" + far.CellID, "Bow GA303 at distance 2");
            await Feed(account, "GAS42"); await Feed(account, "GA;303;42;" + far.CellID); await Feed(account, "GA;102;42;42,-4"); await Feed(account, "GAF0|42");
            Check(await Within(Read(peer)) == "GKK0" && !fight.IsActionPending && fight.ActionPoints == 4, "Bow attack acknowledgement");

            // Pierre d'âme (type 83, sans attribut arme) : pas de données, le serveur décide (StarLoco répond GA;305).
            await Feed(account, "OM11|"); await Feed(account, "OAKOc~1b4b~1~1~");
            CloseCombatStats stone = fight.CloseCombat;
            Check(stone.IsArmed && !stone.HasData && stone.ItemType == 83 && fight.GetCloseCombatUnavailableReason(far.CellID) == null, "Weapon without data");
            await Feed(account, "OM12|");
            Check(!fight.CloseCombat.IsArmed && fight.CloseCombat.Source == "classe", "Unequipped weapon still used");
        }
        finally { LangData.Clear(); }
        await Feed(account, reset);
    }

    /// <summary>
    /// Défis du combat (lot X9), au tour du personnage : <c>Gd</c> lu avec les textes d'un fightChallenge.xml et d'un lang.xml
    /// synthétiques (« %1 » remplacé par « nom (Niv. n) » de la cible), huitième champ ignoré, doublon remplacé, type sans texte,
    /// paquets illisibles journalisés et ignorés, <c>Gdi&lt;type&gt;</c> et ses refus, <c>GdOK</c> / <c>GdKO</c> (quatrième caractère
    /// du paquet) et type inconnu. Laisse quatre défis (3 réussi, 17 raté, 9 et 40 en cours) pour le résultat <c>GE</c>.
    /// </summary>
    private static async Task ChallengesAsync(Accounts account, Socket peer, Fights fight)
    {
        var events = new List<FightChallenge>();
        var dangers = new List<string>();
        Action<FightChallenge> onChallenge = challenge => { lock (events) events.Add(challenge); };
        Action<LogsMessages, string> onLog = (entry, color) =>
        { if (entry.reference == "COMBAT" && entry.message.StartsWith("Paquet Gd ", StringComparison.Ordinal)) lock (dangers) dangers.Add(entry.message); };
        string lang = Path.Combine(TestPaths.Work, "x9-lang");
        Directory.CreateDirectory(lang);
        foreach (string old in Directory.GetFiles(lang, "*.xml")) File.Delete(old);
        WriteLang(lang, "fightChallenge", "<entree table=\"FC\" id=\"3\" d=\"Abattre %1 avant les autres (texte de test).\" g=\"4\" n=\"Volontaire de test\" />\n"
            + "<entree table=\"FC\" id=\"17\" d=\"Garder tous ses points de vie (texte de test).\" g=\"17\" n=\"Intact de test\" />\n"
            + "<entree table=\"FC\" id=\"9\" d=\"Défi sans cible (texte de test).\" g=\"9\" n=\"Neuf de test\" />");
        WriteLang(lang, "lang", "<texte cle=\"LEVEL_SMALL\" valeur=\"Nv\" />\n<texte cle=\"CURRENT_FIGHT_CHALLENGE\" valeur=\"Défi actif\" />\n"
            + "<texte cle=\"FIGHT_CHALLENGE_DONE\" valeur=\"Défi gagné\" />\n<texte cle=\"FIGHT_CHALLENGE_FAILED\" valeur=\"Défi perdu\" />");
        Check(LangData.Load(lang) == 2, "Synthetic fightChallenge.xml/lang.xml not loaded: " + string.Join(" / ", LangData.LoadWarnings));
        fight.ChallengeChanged += onChallenge; account.Logger.log_event += onLog;
        try
        {
            Check(fight.IsMyTurn && fight.Challenges.Count == 0, "Challenges section must start on the own turn without challenge");
            // Le GM de combat ne porte que le grade : niveau posé ici comme s'il venait de monsters.xml.
            ((Tool_BotProtocol.Game.Monstres.Monstres)account.Game.Map.GetActor(-7)).Level = 4;

            // Gd<type>;<1|0>;<cible>;<xp>;0;<butin>;0; : Challenge.parseToPacket de StarLoco finit par « ; » (huitième champ vide).
            await Feed(account, "Gd3;1;-7;20;0;15;0;");
            FightChallenge target = fight.Challenges.Single();
            Check(target.Id == 3 && target.ShowTarget && target.TargetId == -7 && target.BasicXpBonus == 20 && target.TeamXpBonus == 0 && target.XpBonus == 20
                && target.BasicDropBonus == 15 && target.DropBonus == 15 && target.State == FightChallengeState.Running && target.Gfx == 4,
                "Gd3 fields: " + target.Raw);
            Check(target.Name == "Volontaire de test" && target.Description == "Abattre Bouftou synthétique (Nv 4) avant les autres (texte de test)."
                && target.StateText == "Défi actif" && target.ResultMessage == "", "Gd3 texts: " + target.Name + " / " + target.Description + " / " + target.StateText);
            Check(events.Count == 1 && events[0].Id == 3 && events[0].State == FightChallengeState.Running, "Gd3 did not raise ChallengeChanged");
            // Cible vide, puis cible 0 et huitième champ (StarLoco répète le type d'un défi fini) : pas de cible, champ ignoré.
            await Feed(account, "Gd17;0;;10;0;5;0");
            await Feed(account, "Gd9;0;0;5;0;5;0;9");
            IReadOnlyList<FightChallenge> list = fight.Challenges;
            Check(list.Count == 3 && list[1].Id == 17 && !list[1].ShowTarget && list[1].TargetId == null && list[1].Description == "Garder tous ses points de vie (texte de test)."
                && list[2].Id == 9 && list[2].TargetId == null && list[2].XpBonus == 5 && list[2].Raw == "9;0;0;5;0;5;0;9", "Gd without target or with an eighth field");
            // Un second Gd du même type remplace le premier à sa place (choix du bot) ; un type absent des textes garde un nom par défaut.
            await Feed(account, "Gd9;0;0;7;0;6;0");
            await Feed(account, "Gd40;0;;1;2;3;4");
            list = fight.Challenges;
            Check(list.Count == 4 && list[2].Id == 9 && list[2].XpBonus == 7 && list[2].DropBonus == 6, "Duplicate Gd not replaced in place");
            Check(list[3].Name == "Challenge 40" && list[3].Description == "" && list[3].Gfx == null && list[3].XpBonus == 3 && list[3].DropBonus == 7
                && list[3].TeamXpBonus == 2 && list[3].TeamDropBonus == 4, "Gd of a type without client texts");
            Check(events.Count == 5, "Gd events: " + events.Count);

            // Paquets illisibles : journalisés (COMBAT) et ignorés, sans exception ni changement.
            string[] malformed = { "Gd", "Gd3;1;-7", "Gdx;0;;10;0;5;0", "Gd3;0;;abc;0;5;0", "GdOK", "GdKOx", "GdOK-" };
            foreach (string packet in malformed) await Feed(account, packet);
            Check(fight.Challenges.Count == 4 && fight.Challenges[0].XpBonus == 20 && fight.Challenges.All(c => c.State == FightChallengeState.Running)
                && events.Count == 5 && dangers.Count == malformed.Length, "Malformed Gd applied or not logged: " + string.Join(" / ", dangers));

            // Gdi<type> : bouton « voir la cible » (défi qui désigne une cible) ; refus local sinon, rien n'est envoyé.
            Check(fight.GetChallengeTargetUnavailableReason(3) == null, "Target of challenge 3 refused");
            CombatActionResult ask = await fight.ShowChallengeTargetAsync(3);
            Check(ask.Sent && await Within(Read(peer)) == "Gdi3" && ask.Message == "Cible du challenge demandée.", "Gdi3 wire");
            CombatActionResult noTarget = await fight.ShowChallengeTargetAsync(17), unknown = await fight.ShowChallengeTargetAsync(5);
            Check(!noTarget.Sent && noTarget.Message.Contains("ne désigne pas de cible") && !unknown.Sent && unknown.Message.Contains("n’est pas en cours")
                && peer.Available == 0, "Gdi sent for a challenge without target or unknown");

            // GdOK<type> / GdKO<type> : aiguillage sur le quatrième caractère (K réussi, O raté), ligne du chat dans ResultMessage.
            await Feed(account, "GdOK3"); await Feed(account, "GdKO17");
            list = fight.Challenges;
            Check(list[0].State == FightChallengeState.Succeeded && list[1].State == FightChallengeState.Failed && list[2].State == FightChallengeState.Running
                && list[0].Description == target.Description && list[0].XpBonus == 20, "GdOK/GdKO states");
            Check(events.Count == 7 && events[5].ResultMessage == "Défi gagné : Volontaire de test" && events[6].ResultMessage == "Défi perdu : Intact de test"
                && events[6].StateText == "Défi perdu", "GdOK/GdKO events: " + string.Join(" / ", events.Skip(5).Select(c => c.ResultMessage)));
            // Type absent de la liste : la liste ne change pas, mais la ligne du chat est levée comme dans le client.
            await Feed(account, "GdOK99");
            Check(fight.Challenges.Count == 4 && events.Count == 8 && events[7].Id == 99 && events[7].State == FightChallengeState.Succeeded
                && events[7].ResultMessage == "Défi gagné : Challenge 99", "GdOK of an unknown type");
        }
        finally { fight.ChallengeChanged -= onChallenge; account.Logger.log_event -= onLog; LangData.Clear(); }
        // Les textes sont lus à la réception : LangData.Clear ne change pas les défis reçus.
        Check(fight.Challenges[0].Name == "Volontaire de test", "Challenge names depend on the loaded client texts");
    }

    private static void CheckParsers()
    {
        List<int> order; int actor, duration; int? table;
        Check(FightTurnPackets.TryParseTurnList("|1|2|3", out order) && order.SequenceEqual(new[] { 1, 2, 3 }), "GTL with leading separator");
        Check(FightTurnPackets.TryParseTurnList("1|2|3", out order) && order.SequenceEqual(new[] { 1, 2, 3 }), "GTL1|2|3 form");
        Check(!FightTurnPackets.TryParseTurnList("|1|x", out order) && order.Count == 0 && !FightTurnPackets.TryParseTurnList(null, out order), "Malformed GTL accepted");
        Check(FightTurnPackets.TryParseTurnStart("1|30000", out actor, out duration, out table) && actor == 1 && duration == 30000 && table == null, "Two-field GTS");
        Check(FightTurnPackets.TryParseTurnStart("1|30000|4", out actor, out duration, out table) && table == 4, "Three-field GTS");
        Check(!FightTurnPackets.TryParseTurnStart("1", out actor, out duration, out table) && !FightTurnPackets.TryParseTurnStart("1|-5", out actor, out duration, out table),
            "Malformed GTS accepted");
        Check(FightEffectPackets.ParseEffect("111;1,2;3;;;;2;10").Count == 2 && FightEffectPackets.ParseEffect("111;1;3").Count == 0
            && FightEffectPackets.ParseEffect("x;1;3;;;;2;10").Count == 0, "GIE parser");
        Check(FightEffectPackets.ParseZones("+1;0;3|-2;1;4|+bad|?").Count == 2, "GDZ parser");
        bool enabled; FightOptions option; long team;
        Check(FightEffectPackets.TryParseOption("+H42", out enabled, out option, out team) && enabled && option == FightOptions.NeedHelp && team == 42, "Go+H parser");
        Check(!FightEffectPackets.TryParseOption("+h42", out enabled, out option, out team) && !FightEffectPackets.TryParseOption("*A42", out enabled, out option, out team),
            "Go accepts an option letter the client ignores");
        Check(FightEffectPackets.RequestFor(FightOptions.BlockJoiner) == "fN" && FightEffectPackets.RequestFor(FightOptions.BlockSpectators) == "fS"
            && FightEffectPackets.RequestFor(FightOptions.PartyOnly) == "fP" && FightEffectPackets.RequestFor(FightOptions.NeedHelp) == "fH"
            && FightEffectPackets.RequestFor(FightOptions.BlockJoiner | FightOptions.NeedHelp) == null, "Option request packets");
        Check(FightResult.Parse("") == null && FightResult.Parse("abc|1|0") == null, "Unreadable GE duration accepted");
        FightResult partial = FightResult.Parse("100|7|0|zz;1|2;7;Name;1;0");
        Check(partial != null && partial.Rejected.Count == 1 && partial.Entries.Count == 1 && partial.StarBonus == null, "Malformed GE line stops parsing");
        FightResult pool = FightResult.Parse("100;15|7|0|6;311~2,0~4,abc~1,312~1;40");
        Check(pool.StarBonus == 15 && pool.Pools.Count == 1 && pool.Pools[0].Items.Count == 1 && pool.Pools[0].Kamas == 40, "GE pool line or item stop rule");
    }

    private static async Task Run()
    {
        CheckParsers();
        MessagesReception.Init(); Spell.AllSpells.Clear();
        new Spell(10, "Sort test 10");
        int[] required = { 4, 5, 11, 50, 51, 52, 100, 101, 102, 103, 104, 105, 106, 107, 108, 110, 150, 151, 152, 180, 181, 200, 300, 302, 303, 305, 309, 501, 950, 999 };
        Check(required.All(FightActionTable.IsRegistered), "Combat GA table misses: " + string.Join(",", required.Where(id => !FightActionTable.IsRegistered(id))));
        Check(!FightActionTable.IsRegistered(0) && !FightActionTable.IsRegistered(1), "Refusal/movement must stay in Fights");
        Map.AllBotMaps[900091] = new Map { MapID = 900091, MapWidth = 3, MapHeight = 4, MapData = string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)) };
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            using (var account = new Accounts(new AccountConfig("synthetic-fight", "synthetic", "loopback")))
            {
                Task<Socket> accept = listener.AcceptSocketAsync();
                await account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
                using (Socket peer = await Within(accept))
                {
                    peer.ReceiveTimeout = 6000; account.Game.character.id = 42;
                    await Feed(account, "GDM|900091|date|"); Check(await Within(Read(peer)) == "GI", "GDM did not request GI");
                    await Feed(account, "GM|+0;1;0;42;Synthetic;1;10^100;0");
                    Fights fight = account.Game.Fight;
                    int journalEvents = 0, flags = 0, optionEvents = 0, results = 0;
                    fight.JournalEntryAdded += entry => journalEvents++;
                    fight.FlagReceived += flag => flags++;
                    fight.FightOptionChanged += (teamId, value) => optionEvents++;
                    fight.CombatResultReceived += result => results++;

                    // Go is broadcast to the whole map: it is kept outside a fight too (swords of other fights).
                    await Feed(account, "Go+A42"); await Feed(account, "Go+S42");
                    Check(fight.GetTeamOptions(42) == (FightOptions.BlockJoiner | FightOptions.BlockSpectators), "Go+ not merged");
                    await Feed(account, "Go-A42"); await Feed(account, "Go+Z42"); await Feed(account, "Go+A"); await Feed(account, "Go");
                    Check(fight.GetTeamOptions(42) == FightOptions.BlockSpectators && optionEvents == 3, "Go- ignored or malformed Go applied");
                    Check(!(await fight.ToggleOptionAsync(FightOptions.BlockJoiner)).Sent && !(await fight.GiveUpAsync()).Sent
                        && !(await fight.SetFlagAsync(5)).Sent && !(await fight.KickAsync(43)).Sent, "Fight requests accepted outside a fight");
                    await Feed(account, "GTL|1|2"); await Feed(account, "GTR42");
                    Check(fight.TurnOrder.Count == 0 && peer.Available == 0, "Turn packets applied or answered outside a fight");
                    // Lot X9 : défis ignorés hors combat ; « voir la cible » refusé sans combat.
                    int challengeEvents = 0; fight.ChallengeChanged += challenge => challengeEvents++;
                    await Feed(account, "Gd3;1;-7;20;0;15;0"); await Feed(account, "GdOK3");
                    Check(fight.Challenges.Count == 0 && challengeEvents == 0 && !(await fight.ShowChallengeTargetAsync(3)).Sent && peer.Available == 0,
                        "Challenges applied or Gdi sent outside a fight");

                    // Placement: options, kick, flag. GJK empties the map like Game.onJoin (cleanMap(1)): only the fighters' GM come back.
                    await Feed(account, "GM|+2;4;0;-1;555;-4;9001^80x120;1;ff;-1;-1;2a,,,;2;3|+6;1;0;77;Passerby;1;10^100;0");
                    await Feed(account, "Gc+88;0|43;6;0;-1|-1;7;1;-1"); await Feed(account, "GDO+5;7655;0");
                    Check(account.Game.Map.GetActor(-1) != null && account.Game.Map.GetActor(77) != null && account.Game.Map.FightSwords.Count == 1
                        && account.Game.Map.GroundObjects.Count == 1, "Map actors before GJK not known");
                    // States of the map: a GDF frame, a permanent GDC (level 1 in the client) and a non-permanent one (;0).
                    await Feed(account, "GDF|5;3;0"); await Feed(account, "GDC4;aaaaaaaaaa1000;1"); await Feed(account, "GDC6;aaaaaaaaaa1000;0");
                    Check(account.Game.Map.ObjectStates[5].State == 3 && !account.Game.Map.GetCellFromId(4).LineofSight
                        && !account.Game.Map.GetCellFromId(6).LineofSight, "GDF/GDC before GJK not applied");
                    await Feed(account, "GJK2|1|1|0|30000|0");
                    Check(!account.Game.Map.AllActors.Any() && account.Game.Map.Self == null && account.Game.Map.FightSwords.Count == 0
                        && account.Game.Map.GroundObjects.Count == 0, "GJK kept the actors of the map (NPC, players, swords, ground objects)");
                    // cleanMap(1) only changes the image of GDF objects and restores the cells of level 1 (initializeMap(1)).
                    Check(account.Game.Map.ObjectStates.TryGetValue(5, out InteractiveObjectState kept) && kept.State == 3,
                        "GJK dropped the GDF states of the map");
                    Check(account.Game.Map.GetCellFromId(4).LineofSight, "GJK did not restore the cell of a permanent GDC like cleanMap(1)");
                    Check(!account.Game.Map.GetCellFromId(6).LineofSight, "GJK restored the cell of a non-permanent GDC");
                    await Feed(account, "GDC6");
                    Check(account.Game.Map.GetCellFromId(6).LineofSight, "GJK dropped the snapshot used by a later GDC restore");
                    await Feed(account, "GP" + Hash.Get_Cell_Char(0) + Hash.Get_Cell_Char(3) + "|" + Hash.Get_Cell_Char(9) + "|0");
                    Check(fight.IsPlacement && fight.TeamOptions.Count == 0, "Join keeps options of another fight");
                    await Feed(account, "GM|+0;1;0;42;Synthetic;1;10^100;0|+3;1;0;43;Ally;1;10^100;0"); await Feed(account, "GIC|42;3;1");
                    Check(account.Game.Map.AllActors.Select(actor => actor.Id).OrderBy(id => id).SequenceEqual(new long[] { 43 })
                        && account.Game.Map.Self != null && account.Game.Map.Self.Id == 42, "Fighters' GM after GJK did not rebuild the map actors");
                    Check(fight.Fighters.ContainsKey(43) && account.Game.character.Cell.CellID == 3, "Placement fighters not known");
                    Check((await fight.ToggleOptionAsync(FightOptions.BlockJoiner)).Sent && await Within(Read(peer)) == "fN", "fN wire");
                    Check((await fight.ToggleOptionAsync(FightOptions.BlockSpectators)).Sent && await Within(Read(peer)) == "fS", "fS wire");
                    Check((await fight.ToggleOptionAsync(FightOptions.PartyOnly)).Sent && await Within(Read(peer)) == "fP", "fP wire");
                    Check((await fight.ToggleOptionAsync(FightOptions.NeedHelp)).Sent && await Within(Read(peer)) == "fH", "fH wire");
                    Check(!(await fight.ToggleOptionAsync(FightOptions.BlockJoiner | FightOptions.NeedHelp)).Sent && peer.Available == 0, "Combined option sent");
                    await Feed(account, "Go+A42"); await Feed(account, "Go+H42");
                    Check(fight.OwnTeamOptions == (FightOptions.BlockJoiner | FightOptions.NeedHelp), "Own team options not read from Go");
                    Check(!(await fight.KickAsync(42)).Sent && !(await fight.KickAsync(77)).Sent && !(await fight.KickAsync(0)).Sent && peer.Available == 0,
                        "Kick of self/unknown/non-player accepted");
                    Check((await fight.KickAsync(43)).Sent && await Within(Read(peer)) == "GQ43", "Kick wire");
                    Check(!(await fight.SetFlagAsync(500)).Sent && peer.Available == 0, "Flag on a missing cell accepted");
                    Check((await fight.SetFlagAsync(5)).Sent && await Within(Read(peer)) == "Gf5", "Flag wire");
                    await Feed(account, "Gf43|7");
                    Check(fight.LastFlag != null && fight.LastFlag.ActorId == 43 && fight.LastFlag.CellId == 7 && flags == 1 && LastJournal(fight).Contains("7"),
                        "Gf not recorded");
                    await Feed(account, "Gf43"); await Feed(account, "Gfx|y"); await Feed(account, "Gf43|-1");
                    Check(fight.LastFlag.CellId == 7 && flags == 1, "Malformed Gf applied");

                    // Turn order and timeline.
                    await Feed(account, "GS");
                    // A monster fighter (type -2) is named from monsters.xml like the client, not by its template id.
                    Tool_BotProtocol.Game.Monstres.Monstres.ClientNameResolver = template => template == 101 ? "Bouftou synthétique" : null;
                    await Feed(account, "GM|+9;1;0;-7;101;-2;1100^100;1;-1;-1;-1;0,0,0,0;60;4;2;1");
                    Tool_BotProtocol.Game.Monstres.Monstres.ClientNameResolver = null;
                    Check(fight.Fighters[-7].Name == "Bouftou synthétique", "Monster fighter not named from the client texts: " + fight.Fighters[-7].Name);
                    await Feed(account, "GTL|1|2|3"); Check(fight.TurnOrder.SequenceEqual(new[] { 1, 2, 3 }), "GTL|1|2|3 order");
                    await Feed(account, "GTL1|2|3"); Check(fight.TurnOrder.SequenceEqual(new[] { 1, 2, 3 }), "GTL1|2|3 order");
                    await Feed(account, "GTL|42|-7|43"); await Feed(account, "GTL|42|x");
                    Check(fight.TurnOrder.SequenceEqual(new[] { 42, -7, 43 }) && fight.Timeline.IndexOf(-7) == 1, "GTL order or malformed GTL");
                    await Feed(account, "GTR42"); Check(await Within(Read(peer)) == "GT" && fight.Timeline.ReadyActorId == 42, "GTR not answered with GT");
                    await Feed(account, "GTR"); await Feed(account, "GTRabc"); Check(peer.Available == 0, "Malformed GTR answered");
                    await Feed(account, "GTS1|30000");
                    FightTimeline timeline = fight.Timeline;
                    Check(fight.CurrentActorId == 1 && fight.TurnDurationMilliseconds == 30000 && timeline.TableTurn == null
                        && timeline.RemainingMilliseconds(DateTime.UtcNow) > 0 && timeline.RemainingMilliseconds(DateTime.UtcNow) <= 30000, "Two-field GTS");
                    await Feed(account, "GTS"); await Feed(account, "GTSabc|1"); await Feed(account, "GTS5|-1");
                    Check(fight.CurrentActorId == 1, "Malformed GTS applied");
                    await Feed(account, "GTF1"); Check(fight.Timeline.LastActorId == 1 && fight.CurrentActorId == 0, "GTF timeline");
                    await Feed(account, "GTM|42;0;100;8;3;3;;100|-7;0;60;4;2;9;;60|43;0;50;6;3;6;;50");
                    await Feed(account, "GTS42|30000|3"); Check(fight.IsMyTurn && fight.Timeline.TableTurn == 3, "Three-field GTS");

                    // GIE effects and their expiry.
                    await Feed(account, "GIE111;-7;2;;;;3;10");
                    FightEffect enemyEffect = fight.GetEffects(-7).Single();
                    Check(enemyEffect.EffectId == 111 && enemyEffect.Param1 == 2 && enemyEffect.RemainingTurns == 3 && enemyEffect.SpellId == 10
                        && enemyEffect.Source == FightEffectSource.EffectPacket, "GIE effect state");
                    await Feed(account, "GIE112;42;5;;;;2;10"); await Feed(account, "GIE112;42;5;;;;2;10");
                    FightEffect ownEffect = fight.GetEffects(42).Single();
                    Check(ownEffect.Param1 == 10 && ownEffect.RemainingTurns == 3, "GIE on the current actor: merge or extra turn");
                    await Feed(account, "GIE150;43;1;;;;-1;0"); await Feed(account, "GIEabc"); await Feed(account, "GIE111;x;2;;;;3;0"); await Feed(account, "GIE111");
                    Check(fight.Effects.Count == 3, "Malformed GIE applied");
                    await Feed(account, "GTF42"); await Feed(account, "GTS-7|30000");
                    Check(fight.GetEffects(42).Single().RemainingTurns == 2 && fight.GetEffects(-7).Single().RemainingTurns == 3, "Effects of the previous actor not decremented");
                    await Feed(account, "GTF-7"); await Feed(account, "GTS43|30000"); await Feed(account, "GTF43"); await Feed(account, "GTS42|30000");
                    Check(fight.GetEffects(-7).Single().RemainingTurns == 2 && fight.GetEffects(43).Single().RemainingTurns == -1, "Infinite effect expired");
                    await Feed(account, "GIe"); Check(fight.Effects.Count == 0, "GIe ignored");

                    // States, invisibility, life.
                    await Feed(account, "GA;950;42;-7,8,1");
                    Check(fight.HasState(-7, 8) && fight.GetStates(-7).SequenceEqual(new[] { 8 }) && LastJournal(fight).Contains("état 8"), "GA;950 enter state");
                    await Feed(account, "GA;950;42;-7,3,1"); await Feed(account, "GA;950;42;-7,8,0"); await Feed(account, "GA;950;42;-7");
                    Check(fight.GetStates(-7).SequenceEqual(new[] { 3 }), "GA;950 exit state or malformed state");
                    await Feed(account, "GA;150;-7;-7,2"); Check(fight.Fighters[-7].IsInvisible && LastJournal(fight).Contains("invisible"), "GA;150 invisibility");
                    await Feed(account, "GA;150;-7;-7,0"); Check(!fight.Fighters[-7].IsInvisible, "GA;150 visibility");
                    await Feed(account, "GA;100;42;-7,-15"); Check(fight.Fighters[-7].Life == 45 && LastJournal(fight).Contains("perd 15 PV"), "GA;100 damage");
                    await Feed(account, "GA;108;42;-7,10"); Check(fight.Fighters[-7].Life == 55, "GA;108 heal");
                    await Feed(account, "GA;100;42;-7,0"); Check(fight.Fighters[-7].Life == 55 && LastJournal(fight).Contains("aucun PV"), "GA;100 without change");

                    // Characteristic buffs, AP/MP.
                    await Feed(account, "GA;117;42;42,2,3"); await Feed(account, "GA;117;42;42,2");
                    FightEffect buff = fight.GetEffects(42).Single();
                    Check(buff.EffectId == 117 && buff.Source == FightEffectSource.GameAction && buff.Param1 == 2 && buff.RemainingTurns == 3 && buff.CasterId == "42",
                        "Characteristic GA buff");
                    await Feed(account, "GA;116;42;-7,-1,-1"); Check(fight.GetEffects(-7).Single().RemainingTurns == -1, "Infinite GA buff");
                    await Feed(account, "GA;101;42;-7,-2,1"); Check(fight.Fighters[-7].ActionPoints == 2 && LastJournal(fight).Contains("perd 2 PA"), "GA;101 AP loss");
                    int movement = fight.MovementPoints;
                    await Feed(account, "GA;128;42;42,1,1"); Check(fight.MovementPoints == movement + 1 && fight.Fighters[42].MovementPoints == movement + 1, "GA;128 MP gain");

                    // Zones, including the GA;999 envelope.
                    await Feed(account, "GDZ+5;0;3|+6;1;4"); Check(fight.Zones.Count == 2, "GDZ+");
                    await Feed(account, "GDZ-5;0;3"); await Feed(account, "GDZ"); await Feed(account, "GDZ+x;1;2");
                    Check(fight.Zones.Count == 1 && fight.Zones[0].CellId == 6 && fight.Zones[0].Size == 1 && fight.Zones[0].Color == 4, "GDZ- or malformed GDZ");
                    await Feed(account, "GA;999;42;GDZ+7;0;3"); Check(fight.Zones.Any(zone => zone.CellId == 7), "GDZ inside GA;999");
                    await Feed(account, "GA;999;42;GTL|43|42|-7"); Check(fight.TurnOrder.SequenceEqual(new[] { 43, 42, -7 }), "GTL inside GA;999");
                    await Feed(account, "GA;999;42;GA;999;42;GTL|1"); await Feed(account, "GA;999;42;");
                    Check(fight.TurnOrder.SequenceEqual(new[] { 43, 42, -7 }), "Nested GA;999 executed");

                    // Summon (GM entry re-dispatched) then carry/throw/drop.
                    await Feed(account, "GA;181;42;+11;1;0;-8;102;-2;1200^100;1;-1;-1;-1;0,0,0,0;30;3;3;0");
                    Check(fight.Fighters.ContainsKey(-8) && fight.Fighters[-8].Life == 30 && fight.Fighters[-8].CellId == 11 && account.Game.Map.Entites.ContainsKey(-8)
                        && LastJournal(fight).Contains("invoque"), "GA;181 summon");
                    await Feed(account, "GA;181;42;bad"); await Feed(account, "GA;181;42;+");
                    await Feed(account, "GA;50;42;-8");
                    Check(fight.Fighters[42].CarryingId == -8 && fight.Fighters[-8].CarriedById == 42 && fight.Fighters[-8].CellId == 3, "GA;50 carry");
                    await Feed(account, "GA;51;42;8");
                    Check(fight.Fighters[-8].CellId == 8 && fight.Fighters[42].CellId == 3 && account.Game.character.Cell.CellID == 3
                        && fight.Fighters[42].CarryingId == 0 && fight.Fighters[-8].CarriedById == 0 && account.Game.Map.Entites[-8].Cell.CellID == 8,
                        "GA;51 must move the carried fighter, not the carrier");
                    await Feed(account, "GA;50;42;-8"); await Feed(account, "GA;52;42;-8,9");
                    Check(fight.Fighters[-8].CellId == 9 && fight.Fighters[42].CarryingId == 0, "GA;52 drop");

                    // Relocation, direction, interactive frame.
                    await Feed(account, "GA;4;42;-7,10"); Check(fight.Fighters[-7].CellId == 10 && account.Game.Map.Entites[-7].Cell.CellID == 10, "GA;4");
                    await Feed(account, "GA;5;42;-7,13"); Check(fight.Fighters[-7].CellId == 13, "GA;5");
                    await Feed(account, "GA;11;42;-7,3"); Check(fight.Fighters[-7].Orientation == 3, "GA;11");
                    await Feed(account, "GA;200;42;5,3"); Check(account.Game.Map.ObjectStates[5].State == 3, "GA;200");

                    // Dispel and death.
                    await Feed(account, "GA;117;-8;-7,1,2"); Check(fight.GetEffects(-7).Count == 2, "Buff cast by the summon");
                    await Feed(account, "GA;132;42;42"); Check(fight.GetEffects(42).Count == 0 && LastJournal(fight).Contains("retire"), "GA;132");
                    await Feed(account, "GA;103;42;-8");
                    Check(fight.Fighters[-8].IsDead && fight.Fighters[-8].CellId == -1 && fight.GetEffects(-7).All(effect => effect.CasterId != "-8")
                        && fight.GetEffects(-7).Count == 1, "GA;103 death or caster effects kept");

                    // Spells and journal-only actions.
                    await Feed(account, "GA;300;-7;10,3,0,1"); Check(LastJournal(fight).Contains("Sort test 10") && fight.GetSpellCastsThisTurn(10) == 0, "GA;300 of another actor");
                    await Feed(account, "GA;302;-7;10"); Check(LastJournal(fight).Contains("échec critique"), "GA;302");
                    await Feed(account, "GA;303;-7;3"); Check(LastJournal(fight).Contains("corps à corps"), "GA;303");
                    await Feed(account, "GA;305;-7;"); Check(LastJournal(fight).Contains("échec critique au corps"), "GA;305");
                    await Feed(account, "GA;309;42;-7,1"); Check(LastJournal(fight).Contains("esquive la perte de 1 PM"), "GA;309");
                    int lines = fight.Journal.Count;
                    await Feed(account, "GA;501;42;5,1000"); await Feed(account, "GA;309;42;-7"); Check(fight.Journal.Count == lines, "Animation or malformed GA logged");

                    // Router fallback, external registration, unknown and malformed actions.
                    int routed = 0, tabled = 0;
                    GameActionRouter.Register(4242, context => { routed++; return Task.CompletedTask; });
                    FightActionTable.Register(4243, context => { tabled++; return Task.CompletedTask; });
                    try
                    {
                        await Feed(account, "GA;4242;42;x"); await Feed(account, "GA;4243;42;y");
                        Check(routed == 1 && tabled == 1, "In-fight GA not routed to the combat table then GameActionRouter");
                    }
                    finally { GameActionRouter.Unregister(4242); FightActionTable.Unregister(4243); }
                    FightActionTable.Register(4244, context => { throw new InvalidOperationException("synthetic handler failure"); });
                    try { await Feed(account, "GA;4244;42;z"); }
                    finally { FightActionTable.Unregister(4244); }
                    foreach (string malformed in new[] { "GA;1039;42;foo", "GA;100;42;abc", "GA;100;abc;-7,5", "GA;4;42;-7", "GA;4;42;-7,99999", "GA;50;42;",
                        "GA;51;42;x", "GA;52;42;-8", "GA;11;42;-7,9", "GA;101;42;", "GA;149;42;-7,1", "GA;200;42;-1,3", "GA", "GA;", "GA;x;42;1" })
                        await Feed(account, malformed);
                    Check(fight.IsInFight && fight.Fighters[-7].Life == 55 && fight.Fighters[-7].CellId == 13 && fight.Fighters[-7].Orientation == 3,
                        "Malformed GA changed the state or stopped the fight");

                    await ChallengesAsync(account, peer, fight);
                    await CloseCombatAsync(account, peer, fight);

                    // Abandon (GQ) then PvM result.
                    CombatActionResult giveUp = await fight.GiveUpAsync();
                    Check(giveUp.Sent && await Within(Read(peer)) == "GQ" && giveUp.Message.Contains("mort"), "GQ in an active fight");
                    int journalBeforeEnd = fight.Journal.Count;
                    await Feed(account, "GE12000;1|42|0|2;42;Synthetic;10;0;1000;1500;2000;500;0;0;311~2,312~1;150|0;-7;101;5;1;;;;;;;;");
                    // Comme GameManager.terminateFight du client : GE → Game.onLeave → create() ; StarLoco renvoie alors la carte.
                    Check(await Within(Read(peer)) == "GC1", "GE is not followed by GC1");
                    FightResult pvm = fight.LastResult;
                    Check(pvm != null && pvm.DurationMilliseconds == 12000 && pvm.StarBonus == 1 && pvm.InitiatorId == 42 && pvm.FightType == 0
                        && pvm.Winners.Count == 1 && pvm.Losers.Count == 1 && pvm.IsWinner(42) && !pvm.IsWinner(-7), "GE header/categories");
                    FightResultEntry own = pvm.Find(42), monster = pvm.Find(-7);
                    Check(own.WonExperience == 500 && own.Experience == 1500 && own.Items.Count == 2 && own.Items[0].TemplateId == 311 && own.Items[0].Quantity == 2
                        && own.Kamas == 150 && monster.MonsterTemplateId == 101 && monster.IsDead && monster.WonExperience == null, "GE PvM line");
                    Check(fight.Phase == CombatPhase.Finished && !fight.IsInFight && fight.LastActionMessage.Contains("victoire") && results == 1
                        && fight.Journal.Count == journalBeforeEnd && journalBeforeEnd > 20, "GE end state, result event or kept journal");
                    // Lot X9 : les défis passent dans le résultat (results.challenges du client) puis la liste du combat est vidée.
                    Check(pvm.Challenges.Select(c => c.Id).SequenceEqual(new[] { 3, 17, 9, 40 }) && pvm.Challenges[0].State == FightChallengeState.Succeeded
                        && pvm.Challenges[1].State == FightChallengeState.Failed && pvm.Challenges[0].XpBonus == 20 && fight.Challenges.Count == 0,
                        "GE did not copy the challenges into the result or kept them in the fight");

                    // PvP result (type 1: honour then experience), no star bonus.
                    await Feed(account, "GJK2|1|1|0|30000|1"); Check(fight.LastResult == null && fight.Journal.Count == 0, "New fight keeps the previous result");
                    await Feed(account, "GE5000|42|1|2;42;Synthetic;10;0;0;100;200;10;1;0;0;;0;1000;1500;2000;50");
                    Check(await Within(Read(peer)) == "GC1", "PvP GE is not followed by GC1");
                    FightResult pvp = fight.LastResult; FightResultEntry pvpOwn = pvp?.Find(42);
                    Check(pvp != null && pvp.FightType == 1 && pvp.StarBonus == null && pvpOwn.WonHonour == 10 && pvpOwn.Rank == 1 && pvpOwn.Honour == 100
                        && pvpOwn.WonExperience == 50 && pvpOwn.Items.Count == 0 && pvpOwn.Kamas == 0, "GE PvP line");
                    Check(pvp.Challenges.Count == 0, "PvP result carries challenges");

                    // A malformed GE still ends the fight (the client always leaves the fight screen).
                    await Feed(account, "GJK2|1|1|0|30000|0"); await Feed(account, "GEabc");
                    Check(!fight.IsInFight && fight.Phase == CombatPhase.Finished && fight.LastResult == null && results == 2, "Malformed GE");
                    Check(await Within(Read(peer)) == "GC1", "Malformed GE is not followed by GC1");
                    await Feed(account, "GE5000|42|1|"); Check(peer.Available == 0, "GE outside a fight sent GC1");

                    // GQ during placement, then GV.
                    await Feed(account, "GJK2|1|1|0|30000|0");
                    await Feed(account, "Gd9;0;;5;0;5;0"); Check(fight.Challenges.Count == 1, "Gd ignored during the placement");
                    CombatActionResult leave = await fight.GiveUpAsync();
                    Check(leave.Sent && await Within(Read(peer)) == "GQ" && leave.Message.Contains("sortie"), "GQ in placement");
                    await Feed(account, "GV"); Check(await Within(Read(peer)) == "GC1" && !fight.IsInFight, "GV after GQ");
                    Check(fight.Challenges.Count == 0 && challengeEvents == 9, "GV kept the challenges or challenge events missing: " + challengeEvents);
                    Check(peer.Available == 0 && journalEvents > 20 && flags == 1, "Stray packet or missing notifications");
                }
            }
        }
        finally { listener.Stop(); }
        Console.WriteLine("OK: GTL/GTR/GTS(2-3)/GTF timeline, GIE/GIe effects and expiry, GDZ, Go/fN/fS/fP/fH, Gf, GQ (give up, kick), " +
            "combat GA table (life, AP/MP, states, invisibility, buffs, carry/throw, summon, GA;999, router fallback), GE PvM/PvP/malformed, " +
            "close combat GA303 (fists, sword, bow, soul stone; GAF->GKK, AP/range/state refusals, Im1170, GA;305), weapon type zone under the pointer (lot X8), " +
            "challenges Gd/GdOK/GdKO, Gdi and GE copy (lot X9)");
    }
}
