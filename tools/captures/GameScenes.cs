using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Outil_Azur_complet.Bot;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Interfaces;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Utils.Crypto;

/// <summary>Fenêtre de jeu : carte, chat, bandeau, menu d'un PNJ, volets du bandeau, hôtel de vente, atelier, combat.</summary>
internal static partial class Scenes
{
    // Cellules de la carte 7411 (et de la prairie synthétique, même taille 15 × 17).
    private const int SelfCell = 324, OtherCell = 345, ThirdCell = 266, MerchantNpcCell = 211, KeeperNpcCell = 223, GroupCell = 302;

    // ASK : panoplie de l'Aventurier portée, épée, ressources et potions dans le sac (identifiants hexadécimaux).
    private static readonly string Items = string.Concat(
        Item(0x1001, 2478, 1, "0", "7d#a#0#0#0d0+10"), Item(0x1002, 40, 1, "1", "64#2#4#0#1d3+1"), Item(0x1003, 2475, 1, "2", "76#5#0#0#0d0+5"),
        Item(0x1004, 2477, 1, "3", "7d#5#0#0#0d0+5"), Item(0x1005, 2476, 1, "5", "77#3#0#0#0d0+3"), Item(0x1006, 2474, 1, "6", "7c#4#0#0#0d0+4"),
        Item(0x1007, 2473, 1, "7", "7e#4#0#0#0d0+4"),
        Item(0x1010, 285, 8), Item(0x1011, 311, 6), Item(0x1012, 286, 3), Item(0x1013, 287, 12), Item(0x1014, 289, 25), Item(0x1015, 384, 17),
        Item(0x1016, 304, 4), Item(0x1017, 303, 9), Item(0x1018, 283, 5, "", "6e#1e#32#0#1d21+29"), Item(0x1019, 548, 2), Item(0x101a, 421, 7), Item(0x101b, 1734, 3));

    private static string Item(int guid, int template, int quantity, string position = "", string effects = "") =>
        guid.ToString("x", CultureInfo.InvariantCulture) + "~" + template.ToString("x", CultureInfo.InvariantCulture) + "~" + quantity.ToString("x", CultureInfo.InvariantCulture)
        + "~" + position + "~" + effects + ";";

    /// <summary>As de StarLoco (Player.getAsPacket) : en-tête puis les champs 9 à 50 (base, équipement, dons, boost[, total]).</summary>
    private static string Stats()
    {
        var packet = new StringBuilder("As1162000,1115000,1255000|48250|5|2|0~0,0,0,0,0,0|612,740|9310,10000|412|112|");
        var values = new Dictionary<int, string>
        {
            { 9, "6,1,0,0,7" }, { 10, "3,0,0,0,3" }, { 11, "180,35,0,0" }, { 12, "100,50,0,0" }, { 13, "20,10,0,0" }, { 14, "0,5,0,0" },
            { 15, "15,0,0,0" }, { 16, "0,10,0,0" }, { 17, "0,0,0,0" }, { 18, "1,0,0,0" }, { 19, "0,4,0,0" }, { 27, "0,2,0,0" },
        };
        for (int i = 9; i <= 50; i++)
            packet.Append(values.TryGetValue(i, out string value) ? value : (i <= 10 || i >= 29 ? "0,0,0,0,0" : "0,0,0,0")).Append('|');
        return packet.ToString();
    }

    private const string Spells = "SL141~5~b;142~4~c;143~3~d;144~2~e;145~3~f;146~1~g;147~2~h;148~1~i;154~1~j;150~1~_;";

    private static string Player(int cell, int direction, int id, string name, int breed, int sex, string colors, string stuff, string guild = "")
        => "+" + cell + ";" + direction + ";0;" + id + ";" + name + ";" + breed + ";" + (breed * 10 + sex) + "^100;" + sex + ";0,0,0," + (id + 61) + ";"
           + colors + ";" + stuff + ";0;;;" + guild + ";0;";

    private static string Actors() => "GM|"
        + Player(SelfCell, 3, CharacterId, CharacterName, 8, 1, "e4a54b;6c3b1f;f2e6c4", "28,9aa,9a9,,") + "|"
        + Player(OtherCell, 5, 4202, "Sentinelle-Demo", 9, 0, "2b5c8a;f0d070;3a2a1a", ",,,,", "Les Gardiens du Puits;1a,2b,3c,4d") + "|"
        + Player(ThirdCell, 1, 4203, "Herboriste-Demo", 10, 1, "6a8f3c;d8c8a0;402818", ",,,,") + "|"
        + "+" + MerchantNpcCell + ";4;0;-1;546;-4;9048^100;0;-1;-1;-1;,,,,;-1;0|"
        + "+" + KeeperNpcCell + ";2;0;-2;23;-4;9001^100;1;-1;-1;-1;,,,,;-1;0|"
        + "+" + GroupCell + ";2;20;-3;101,101,104;-3;1566^100,1566^100,1562^100;4,6,8;-1,-1,-1;0,0,0,0;-1,-1,-1;0,0,0,0;-1,-1,-1;0,0,0,0";

    private static void GameViews()
    {
        using (var server = new FakeServer())
        {
            var account = new Accounts(new AccountConfig(AccountName, "mot-de-passe-fictif", "Privé"));
            server.Attach(account);
            account.Game.Server.ServerID = 601;
            Feed(account, "ASK|" + CharacterId + "|" + CharacterName + "|57|8|1|81|e4a54b|6c3b1f|f2e6c4|" + Items);
            var form = new GameClientFullform(account);
            try
            {
                BotCaptures.Place(form, BotCaptures.WindowSize); form.Show(); BotCaptures.Pump(300);
                Feed(account, Stats()); Feed(account, Spells); Feed(account, "Ow642|1450");
                Feed(account, "GDM|" + MapId + "|0711291819|cle");
                Feed(account, Actors());
                Feed(account, "fC1");
                BotCaptures.PumpUntil(() => form.Map != null && !form.Map.ArtworkStatus.StartsWith("Chargement", StringComparison.Ordinal), 30);
                BotCaptures.Pump(2500); // sprites des acteurs lus sur le pool de fils
                if (form.Map == null) { BotCaptures.Skip("04-jeu", "la carte n'a pas été créée"); return; }
                Console.WriteLine("décor : " + form.Map.ArtworkStatus);
                if (Environment.GetEnvironmentVariable("AZUR_CAPTURE_DEBUG") == "1") { form.Map.ShowCellIds = true; BotCaptures.Pump(500); }

                if (BotCaptures.Wanted("04-jeu")) BotCaptures.Shot("04-jeu", form);
                if (BotCaptures.Wanted("05-chat")) Chat(account, form);
                if (BotCaptures.Wanted("06-bandeau")) Banner(form);
                if (BotCaptures.Wanted("07-menu-pnj")) NpcMenu(form);
                Panels(account, form);
                if (BotCaptures.Wanted("17-combat-placement") || BotCaptures.Wanted("18-combat")) Fight(account, form);
            }
            finally
            {
                form.Close(); BotCaptures.Pump(200); form.Dispose();
            }
        }
    }

    private static void Chat(Accounts account, GameClientFullform form)
    {
        Feed(account, "cC+*#$pi%:?!^");
        foreach (string packet in new[]
        {
            "cMK|4202|Sentinelle-Demo|Bonjour à tous ! Quelqu'un pour le donjon des Bouftous ?",
            "cMK%|4203|Herboriste-Demo|La réunion de guilde commence à 21 h.",
            "cMK$|4202|Sentinelle-Demo|J'arrive, attendez-moi en [4,-18].",
            "cMK:|5001|Marchand-Demo|Vends Laine de Bouftou x100, 15 kamas l'unité.",
            "cMK?|5002|Recruteur-Demo|La guilde des Gardiens du Puits recrute, niveau 30 minimum.",
            "cMKF|4203|Herboriste-Demo|Tu as encore des Graines de Sésame ?",
            "cMKT|4203|Herboriste-Demo|Oui, une douzaine.",
            "cMK!|5003|Milicien-Demo|Prisme attaqué en [5,-17] !",
            "cMK|4202|Sentinelle-Demo|*salue*",
            "Im0153;Herboriste-Demo",
        }) Feed(account, packet);
        form.Chat.Expanded = true; BotCaptures.Pump(600);
        BotCaptures.Shot("05-chat", form);
        form.Chat.Expanded = false; BotCaptures.Pump(400);
    }

    private static void Banner(GameClientFullform form)
    {
        Rectangle hud = form.Hud.RectangleToScreen(form.Hud.ClientRectangle);
        BotCaptures.Shot("06-bandeau", Rectangle.Intersect(hud, new Rectangle(form.Location, form.Size)));
    }

    private static void NpcMenu(GameClientFullform form)
    {
        var map = (UserMapControl)BotCaptures.Field(form.Map, "UserMap");
        Point centre = map.Cells[MerchantNpcCell].Centre;
        Cursor.Position = map.PointToScreen(new Point(centre.X + 10, centre.Y - 10));
        BotCaptures.Complete(form.Map.Router.RouteAsync(MerchantNpcCell, MouseButtons.Right));
        ContextMenuStrip menu = form.Map.Router.LastMenu;
        if (menu == null || !menu.Visible) { BotCaptures.Skip("07-menu-pnj", "le clic droit sur le PNJ n'a pas ouvert de menu"); return; }
        BotCaptures.Pump(300);
        BotCaptures.Shot("07-menu-pnj", form);
        menu.Close(); BotCaptures.Pump(200);
    }
}
