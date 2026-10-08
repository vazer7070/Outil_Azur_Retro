using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Outil_Azur_complet.Bot;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Utils.Crypto;

/// <summary>Volets du tiroir ouverts comme le joueur les ouvre (boutons du bandeau, fenêtres du serveur) et combat.</summary>
internal static partial class Scenes
{
    private static void Panels(Accounts account, GameClientFullform form)
    {
        BannerPanel("08-caracteristiques", form, "Stats");
        BannerPanel("09-sorts", form, "Spells");
        BannerPanel("10-inventaire", form, "Inventory");
    }

    /// <summary>Ouvre un volet par son bouton du bandeau, le photographie avec la fenêtre, puis le referme.</summary>
    private static void BannerPanel(string name, GameClientFullform form, string panel, Action<GameClientFullform> prepare = null, Action<GameClientFullform> after = null)
    {
        if (!BotCaptures.Wanted(name)) return;
        prepare?.Invoke(form);
        if (!form.Banner.OpenPanel(panel)) { BotCaptures.Skip(name, "le bouton « " + panel + " » du bandeau n'a pas ouvert de volet"); return; }
        BotCaptures.Pump(800);
        after?.Invoke(form);
        BotCaptures.Shot(name, form);
        form.Panels.CloseAll(); BotCaptures.Pump(300);
    }

    private static void Fight(Accounts account, GameClientFullform form) { }
}
