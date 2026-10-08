using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot;
using Outil_Azur_complet.Bot.Interfaces;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.NPC;
using Tool_BotProtocol.Game.Perso.Spells;

/// <summary>Vues photographiées, dans l'ordre du parcours d'un joueur.</summary>
internal static partial class Scenes
{
    internal const string AccountName = "compte-demo";
    internal const string CharacterName = "Aventuriere-Demo";
    internal const int CharacterId = 4201;
    internal static int MapId;

    internal static void Run()
    {
        MessagesReception.Init();
        string lang = Path.Combine(BotCaptures.Bin, "ressources", "Bot", "BotLang");
        if (Directory.Exists(lang)) LangData.Load(lang); else BotCaptures.Note("textes du client absents (" + lang + ") : libellés de repli");
        foreach (string warning in LangData.LoadWarnings.Take(5)) BotCaptures.Note("textes du client : " + warning);
        string spells = Path.Combine(BotCaptures.Bin, "ressources", "Bot", "BotSorts");
        if (Directory.Exists(spells)) LoadSpells(spells);
        Tool_BotProtocol.Game.Monstres.Monstres.ClientNameResolver =id => LangData.Monster.Has(id) ? LangData.Monster.Name(id) : null;
        // PNJ : noms inventés (le menu reprend les actions des textes du client pour ces modèles).
        PNJ.AllPNJ[546] = new NamedNpc(546, "Marchande des ressources");
        PNJ.AllPNJ[23] = new NamedNpc(23, "Gardien du puits");
        MapId = CaptureMap.Prepare();
        Console.WriteLine("carte : " + CaptureMap.Source);

        if (BotCaptures.Wanted("01-connexion")) Login();
        if (BotCaptures.Wanted("02-serveurs")) Servers();
        if (BotCaptures.Wanted("03-personnages")) Characters();
        GameViews();
    }

    private static void LoadSpells(string folder)
    {
        // Sorts du bot (BotSorts/<id>.xml), comme l'étape « Sorts » du chargement, avec un chemin explicite : l'exécutable
        // des captures ne vit pas à côté de l'application.
        try { Spell.LoadAllSpells(folder); }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is System.Xml.XmlException)
        { BotCaptures.Note("sorts du bot illisibles : " + error.Message); }
    }

    private sealed class NamedNpc : PNJ
    {
        public NamedNpc(int template, string name) { NPc_ID = template; Name = name; GFX = 9048; }
    }

    // ------------------------------------------------------------------------------------------------ connexion et serveurs

    private static void Login()
    {
        string folder = Path.Combine("ressources", "Bot", "AccountSingle"); Directory.CreateDirectory(folder);
        foreach (string name in new[] { AccountName, "compte-secondaire" })
            File.WriteAllText(Path.Combine(folder, name + ".json"),
                "{ \"Comptes\": { \"Compte\": \"" + name + "\", \"MDP\": \"mot-de-passe-fictif\", \"Lieu\": \"Privé\" } }");
        using (var form = new LoginForm())
        {
            BotCaptures.Place(form, form.Size); form.Show(); BotCaptures.Pump(300);
            var list = (ListView)BotCaptures.Field(form, "accounts");
            if (list.Items.Count > 0) { list.Items[0].Selected = true; list.Focus(); }
            BotCaptures.Shot("01-connexion", form);
            form.Close();
        }
        AccountConfig.AccountsDico.Clear();
    }

    private static void Servers()
    {
        var config = new AccountConfig(AccountName, "mot-de-passe-fictif", "Privé");
        using (var server = new FakeServer())
        using (var form = new PersoSelection(config, false))
        {
            BotCaptures.Place(form, form.Size); form.Show(); BotCaptures.Pump(300);
            var account = (Accounts)BotCaptures.Field(form, "A");
            server.Attach(account);
            Feed(account, "AxK8640000000|601,2|602,1");
            Feed(account, "AH601;1;110;1|602;1;110;1|603;0;110;1|604;2;110;0");
            var list = (ListView)BotCaptures.Field(form, "servers");
            BotCaptures.PumpUntil(() => list.Items.Count > 0, 3);
            if (list.Items.Count > 0) { list.Items[0].Selected = true; list.Focus(); }
            BotCaptures.Shot("02-serveurs", form);
            form.Close();
        }
    }

    private static void Characters()
    {
        var config = new AccountConfig(AccountName, "mot-de-passe-fictif", "Privé");
        using (var server = new FakeServer())
        {
            var account = new Accounts(config);
            server.Attach(account);
            account.Game.Server.ServerID = 601;
            Feed(account, "ALK8640000000|3|" + CharacterId + ";" + CharacterName + ";57;81;e4a54b;6c3b1f;f2e6c4;;0;601;0|4202;Sentinelle-Demo;112;90;2b5c8a;f0d070;3a2a1a;;0;601;0|4203;Herboriste-Demo;23;101;6a8f3c;d8c8a0;402818;;0;601;0");
            using (var form = new SelectPlayerPerso(account, true))
            {
                BotCaptures.Place(form, BotCaptures.WindowSize); form.Show(); BotCaptures.Pump(1500);
                BotCaptures.Shot("03-personnages", form);
                form.Close();
            }
            account.Dispose();
        }
    }

    internal static void Feed(Accounts account, string packet) => BotCaptures.Complete(MessagesReception.ReceptionAsync(account.Connexion, packet));
}
