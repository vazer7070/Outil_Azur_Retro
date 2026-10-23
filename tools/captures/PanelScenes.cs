using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Outil_Azur_complet.Bot;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Utils.Crypto;

/// <summary>Volets du tiroir ouverts comme le joueur les ouvre (boutons du bandeau, fenêtres du serveur) et combat.</summary>
internal static partial class Scenes
{
    private static void Panels(Accounts account, GameClientFullform form)
    {
        BannerPanel("08-caracteristiques", form, "Stats");
        BannerPanel("09-sorts", form, "Spells", after: f => f.Panels.Get<SpellsPanel>()?.SelectSpell(143));
        BannerPanel("10-inventaire", form, "Inventory", after: f =>
        {
            var grid = (ItemGrid)BotCaptures.Field(f.Panels.Get<InventoryPanel>(), "inventory");
            grid.SelectItem(0x1018u); // Fiole de Soin : fiche de l'objet sous la grille
        });
        BannerPanel("11-quetes", form, "Quests", after: f =>
        {
            Feed(account, "QL+3;0|9;0|10;0|13;1|20;1");
            BotCaptures.Complete(account.Game.Interactions.Quests.SelectAsync(3));
            Feed(account, "QS3|3|2,1;3,0|2|4||");
        });
        BannerPanel("12-guilde", form, "Guild",
            prepare: f => Feed(account, "gSLes Gardiens du Puits|3|" + Base36(0x2E6B3A) + "|5|" + Base36(0xF0D070) + "|" + Base36(766)),
            after: f =>
            {
                Feed(account, "gIG1|12|1000|1500|-1");
                Feed(account, "gIM+" + CharacterId + ";" + CharacterName + ";57;81;2;48250;10;766;1;0;0"
                    + "|4202;Sentinelle-Demo;112;90;1;912000;50;1;2;1;0|4203;Herboriste-Demo;23;101;3;1200;5;0;1;0;0"
                    + "|4204;Forgeron-Demo;76;40;4;35000;20;0;0;-1;30|4205;Eclaireuse-Demo;34;71;5;800;5;0;0;2;72");
            });
        BannerPanel("13-carte-monde", form, "WorldMap");
        if (BotCaptures.Wanted("14-hotel-de-vente")) Auction(account, form);
        if (BotCaptures.Wanted("15-atelier")) Craft(account, form);
        BannerPanel("16-monture", form, "Mount", prepare: f => Feed(account, "Re+" + OwnMount));
    }

    // Re+ (Mount.parse du client) : identifiant, modèle (dragodinde amande), ancêtres, capacités, nom, sexe, xp, niveau…
    private const string OwnMount = "-12:18:?,?,?,?,?,?,?,?,?,?,?,?,?,?:,,9:Bourrasque:1:1500,1000,2000:5:1:250:0:2000,10000:1000,1000:5000,7500:-2500,-10000,10000:3000,10000:-1:0::10,240:2,20:";

    private static string Base36(int value)
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        string text = string.Empty;
        do { text = digits[value % 36] + text; value /= 36; } while (value > 0);
        return text;
    }

    /// <summary>Ouvre un volet par son bouton du bandeau, le photographie avec la fenêtre, puis le referme.</summary>
    private static void BannerPanel(string name, GameClientFullform form, string panel, Action<GameClientFullform> prepare = null, Action<GameClientFullform> after = null)
    {
        if (!BotCaptures.Wanted(name)) return;
        prepare?.Invoke(form);
        if (!form.Banner.OpenPanel(panel)) { BotCaptures.Skip(name, "le bouton « " + panel + " » du bandeau n'a pas ouvert de volet"); return; }
        BotCaptures.Pump(800);
        after?.Invoke(form);
        BotCaptures.Pump(800);
        BotCaptures.Shot(name, form);
        form.Panels.CloseAll(); BotCaptures.Pump(300);
    }

    /// <summary>Hôtel de vente des ressources (ECK11) : catégorie « Céréale », modèle « Blé », prix moyen et lots en vente.</summary>
    private static void Auction(Accounts account, GameClientFullform form)
    {
        Feed(account, "ECK11|1,10,100;34,38,12;2;100;20;-1;350");
        var panel = form.Panels.Get<AuctionBuyPanel>();
        if (!BotCaptures.PumpUntil(() => form.Panels.Current == panel, 5)) { BotCaptures.Skip("14-hotel-de-vente", "ECK11 n'a pas ouvert le volet d'achat"); return; }
        var categories = (ComboBox)BotCaptures.Field(panel, "categoryBox");
        var templates = (ListView)BotCaptures.Field(panel, "templateList");
        var lines = (ListView)BotCaptures.Field(panel, "lineList");
        int cereal = categories.Items.Cast<object>().ToList().FindIndex(item => item.ToString().StartsWith("C", StringComparison.Ordinal));
        categories.SelectedIndex = cereal >= 0 ? cereal : 0; BotCaptures.Pump(300);
        Feed(account, "EHL34|289;400;533;423;532;401");
        BotCaptures.PumpUntil(() => templates.Items.Count > 0, 3);
        ListViewItem wheat = templates.Items.Cast<ListViewItem>().FirstOrDefault(row => Equals(row.Tag, 289)) ?? (templates.Items.Count > 0 ? templates.Items[0] : null);
        if (wheat != null) { wheat.Selected = true; BotCaptures.Pump(300); }
        Feed(account, "EHP289|14");
        Feed(account, "EHl289|9001;;15;138;1290|9002;;16;;|9003;;;140;|9004;;;;1350");
        BotCaptures.PumpUntil(() => lines.Items.Count > 0, 3);
        if (lines.Items.Count > 0) { lines.Items[0].Selected = true; BotCaptures.Pump(300); }
        BotCaptures.Shot("14-hotel-de-vente", form);
        Feed(account, "EV"); BotCaptures.PumpUntil(() => form.Panels.Current == null, 3);
        form.Panels.CloseAll(); BotCaptures.Pump(300);
    }

    /// <summary>Atelier du boulanger (ECK3, compétence 27 « Cuire du pain ») avec la farine et l'eau du sac posées.</summary>
    private static void Craft(Accounts account, GameClientFullform form)
    {
        Feed(account, "JS|25;27~8~0~0~90");
        Feed(account, "JX|25;23;20000;25000;30000;");
        Feed(account, "ECK3|8;27");
        var panel = form.Panels.Get<CraftPanel>();
        if (!BotCaptures.PumpUntil(() => form.Panels.Current == panel, 5)) { BotCaptures.Skip("15-atelier", "ECK3 n'a pas ouvert le volet de l'atelier"); return; }
        Feed(account, "EMKO+" + 0x1010 + "|1"); Feed(account, "EMKO+" + 0x1011 + "|1");
        BotCaptures.Pump(800);
        BotCaptures.Shot("15-atelier", form);
        Feed(account, "EV"); BotCaptures.PumpUntil(() => form.Panels.Current == null, 3);
        form.Panels.CloseAll(); BotCaptures.Pump(300);
    }

    // ------------------------------------------------------------------------------------------------ combat

    /// <summary>
    /// Combat contre le groupe de Bouftous : placement (GJK, GP, GM des combattants, GIC) puis tour du personnage (GS, GTM,
    /// GTL, GTS). Le serveur fictif ne répond pas : seules les fenêtres et la frise reçues du « serveur » sont montrées.
    /// </summary>
    private static void Fight(Accounts account, GameClientFullform form)
    {
        int[] ours = { SelfCell, OtherCell, 309 }, theirs = { ThirdCell, 252, 280 };
        Feed(account, "GJK2|1|1|0|45000|4");
        Feed(account, "GP" + string.Concat(ours.Select(c => Hash.Get_Cell_Char((short)c))) + "|" + string.Concat(theirs.Select(c => Hash.Get_Cell_Char((short)c))) + "|0");
        Feed(account, "GM|" + Fighter(SelfCell, CharacterId, CharacterName, 8, 81, 1, 57, "e4a54b;6c3b1f;f2e6c4", "28,9aa,9a9,,", 612, 7, 3, 0)
            + "|" + Fighter(OtherCell, 4202, "Sentinelle-Demo", 9, 90, 0, 112, "2b5c8a;f0d070;3a2a1a", ",,,,", 980, 8, 4, 0)
            + "|" + Monster(ThirdCell, -1, 101, 1566, 2, 52, 4, 3) + "|" + Monster(252, -2, 101, 1566, 1, 44, 4, 3) + "|" + Monster(280, -3, 104, 1562, 3, 30, 3, 4));
        Feed(account, "GIC|" + CharacterId + ";" + SelfCell + ";1|4202;" + OtherCell + ";1");
        Feed(account, "GR14202");
        BotCaptures.Pump(1500);
        if (BotCaptures.Wanted("17-combat-placement")) BotCaptures.Shot("17-combat-placement", form);

        Feed(account, "GS");
        Feed(account, "GTM|" + CharacterId + ";0;612;7;3;" + SelfCell + ";;740|4202;0;980;8;4;" + OtherCell + ";;980|-1;0;52;4;3;" + ThirdCell + ";;52|-2;0;44;4;3;252;;44|-3;0;30;3;4;280;;30");
        Feed(account, "GTL|" + CharacterId + "|-1|4202|-2|-3");
        Feed(account, "GTS" + CharacterId + "|30000");
        BotCaptures.Pump(1500);
        if (BotCaptures.Wanted("18-combat")) BotCaptures.Shot("18-combat", form);
    }

    // Fighter.getGmPacket de StarLoco (personnage) : cellule, orientation, id, nom, classe, gfx^taille, sexe, niveau,
    // alignement, couleurs, équipement, vie, PA, PM, résistances (5), esquives PA/PM, équipe, monture.
    private static string Fighter(int cell, int id, string name, int breed, int gfx, int sex, int level, string colors, string stuff, int life, int ap, int mp, int team)
        => "+" + cell + ";1;0;" + id + ";" + name + ";" + breed + ";" + gfx + "^100;" + sex + ";" + level + ";0,0,0," + (level + id) + ";"
           + colors + ";" + stuff + ";" + life + ";" + ap + ";" + mp + ";0;0;0;0;0;0;0;" + team + ";;";

    // Fighter.getGmPacket (monstre) : cellule, orientation, id, modèle, -2, gfx^taille, grade, couleurs, accessoires, vie, PA, PM, équipe.
    private static string Monster(int cell, int id, int template, int gfx, int grade, int life, int ap, int mp)
        => "+" + cell + ";1;0;" + id + ";" + template + ";-2;" + gfx + "^100;" + grade + ";-1;-1;-1;0,0,0,0;" + life + ";" + ap + ";" + mp + ";1";
}
