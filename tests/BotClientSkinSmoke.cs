using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Outil_Azur_complet.Bot;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Game.Accounts;

// Vérifie que les éléments graphiques exportés du client fourni (Resources/Bot/Client) sont livrés
// à côté de l'exécutable et réellement utilisés par les fenêtres du bot : pilules des boutons,
// bandeau de connexion, socle de l'aperçu de création, icônes du bandeau de jeu.
internal static class BotClientSkinSmoke
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
            string file = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(e.Name).Name + ".dll");
            if (!File.Exists(file)) file = Path.ChangeExtension(file, "exe"); return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        };
        try { Application.EnableVisualStyles(); Run(); Console.WriteLine("OK: client assets are shipped and drive the bot's buttons, banner, pedestal and icons"); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static IEnumerable<Control> All(Control root) { foreach (Control child in root.Controls) { yield return child; foreach (var inner in All(child)) yield return inner; } }
    private static int Count(Bitmap image, Func<Color, bool> predicate)
    {
        int total = 0; for (int y = 0; y < image.Height; y += 2) for (int x = 0; x < image.Width; x += 2) if (predicate(image.GetPixel(x, y))) total++; return total;
    }

    private static void Run()
    {
        string previous = Environment.CurrentDirectory; string work = Path.Combine(TestPaths.Work, "bot-client-skin"); Directory.CreateDirectory(work); Environment.CurrentDirectory = work;
        try
        {
            // 1. Livraison : les PNG du dossier source sont copiés par la compilation et chacun est documenté.
            string shipped = Path.Combine(TestPaths.ApplicationBin, "ressources", "Bot", "UI", "Client");
            string source = Path.GetFullPath(Path.Combine(TestPaths.ApplicationBin, "..", "..", "Resources", "Bot", "Client"));
            Check(Directory.Exists(shipped), "Client assets were not copied next to the executable: " + shipped);
            var files = Directory.GetFiles(shipped, "*.png").Select(Path.GetFileName).ToArray();
            Check(files.Length >= 40, "Only " + files.Length + " client assets shipped");
            foreach (string required in new[] { "logo.png", "oeufs.png", "socle.png", "bouton-principal-haut.png", "bouton-haut.png", "bouton-bas.png", "icone-inventaire.png", "stat-vitalite.png" })
                Check(files.Contains(required), "Missing client asset: " + required);
            if (Directory.Exists(source)) {
                string provenance = File.ReadAllText(Path.Combine(source, "PROVENANCE.md"));
                foreach (string file in Directory.GetFiles(source, "*.png").Select(Path.GetFileName)) {
                    string stem = Path.GetFileNameWithoutExtension(file);
                    Check(provenance.Contains(file) || (stem.Contains("-") && provenance.Contains(stem.Substring(0, stem.IndexOf('-')) + "-*")), "Asset without provenance entry: " + file);
                }
            }

            // 2. Chargeur : cache, échelle des icônes, absence tolérée.
            var assets = typeof(LoginForm).Assembly.GetType("Outil_Azur_complet.Bot.ClientAssets");
            Check(assets != null, "ClientAssets type is missing");
            var get = assets.GetMethod("Get", Any); var icon = assets.GetMethod("Icon", Any);
            var logo = (Bitmap)get.Invoke(null, new object[] { "logo" });
            Check(logo != null && logo.Width > 300 && logo.Height > 300, "Logo not loaded from the shipped folder");
            Check(ReferenceEquals(logo, get.Invoke(null, new object[] { "logo" })), "Assets are reloaded instead of cached");
            Check(get.Invoke(null, new object[] { "inexistant" }) == null, "Unknown asset should be null");
            var small = (Bitmap)icon.Invoke(null, new object[] { "icone-inventaire", 24 });
            Check(small != null && Math.Max(small.Width, small.Height) == 24 && Math.Min(small.Width, small.Height) >= 12, "Icon is not scaled into 24 px");
            Check(ReferenceEquals(small, icon.Invoke(null, new object[] { "icone-inventaire", 24 })), "Scaled icons are not cached");

            AccountConfig.AccountsDico.Clear(); GlobalConfig.InitializeConfig();
            var config = new AccountConfig("compte-fictif", "secret de test", "Privé");

            // 3. Connexion : pilules du client sur les boutons, bandeau logo + œufs.
            using (var login = new LoginForm())
            {
                login.Show(); Application.DoEvents();
                var buttons = All(login).OfType<Button>().Where(x => x.GetType().Name == "ClientButton").ToArray();
                Check(buttons.Length >= 4, "Login buttons are not client buttons: " + buttons.Length);
                var primary = buttons.First(x => x.Text == "Connexion"); var secondary = buttons.First(x => x.Text == "Fermer");
                Check((bool)primary.GetType().GetProperty("Primary", Any).GetValue(primary, null), "« Connexion » is not the primary button");
                using (var image = new Bitmap(primary.Width, primary.Height)) {
                    primary.DrawToBitmap(image, new Rectangle(Point.Empty, primary.Size));
                    Check(Count(image, c => c.R > 200 && c.G > 90 && c.G < 180 && c.B < 70) > 60, "Primary button does not show the client's orange pill");
                }
                using (var image = new Bitmap(secondary.Width, secondary.Height)) {
                    secondary.DrawToBitmap(image, new Rectangle(Point.Empty, secondary.Size));
                    Check(Count(image, c => c.R > 200 && c.G > 180 && c.B > 140 && c.B < 215) > 60, "Secondary button does not show the client's parchment pill");
                }
                var banner = All(login).FirstOrDefault(x => x.Name == "client-oeufs");
                Check(banner != null && banner.Height >= 80, "Login screen lacks the client's class-egg banner");
                Check(All(banner).OfType<PictureBox>().Count(x => x.Image != null) == 1, "Egg banner lacks its picture");
                Check(banner.BackColor.A == 255 && banner.BackColor.R < 120, "Egg banner background was not taken from the client image");
                var header = All(login).FirstOrDefault(x => x.Name == "client-bandeau-connexion");
                Check(header != null && header.Height >= 60 && header.Dock == DockStyle.Top, "Login screen lacks the client's illustrated header");
                var pictures = All(header).OfType<PictureBox>().Where(x => x.Image != null).ToArray();
                Check(pictures.Length == 2 && pictures.Any(x => x.Name == "client-logo" && x.Dock == DockStyle.Left), "Header lacks the illustration or the logo");
                Check(header.BackColor.R > 180 && header.BackColor.B < 170, "Header background was not taken from the client's beige band");
                login.Close();
            }

            // 3 bis. Choix du serveur : bannière de UI_ChooseServer au-dessus de la liste.
            using (var servers = new PersoSelection(config, false))
            {
                servers.Show(); Application.DoEvents();
                var banner = All(servers).FirstOrDefault(x => x.Name == "client-bandeau-serveurs");
                Check(banner != null && banner.Dock == DockStyle.Top && banner.Height >= 50, "Server screen lacks the client's banner");
                Check(All(banner).OfType<PictureBox>().Single(x => x.Image != null).Dock == DockStyle.Left && banner.BackColor.R < 140 && banner.BackColor.R > banner.BackColor.G + 20, "Server banner is not anchored left on its dark red edge");
                var list = All(servers).OfType<ListView>().First();
                Check(list.Height > 100 && list.Top >= banner.Bottom, "Server list is hidden by the banner");
                servers.Close();
            }

            // 4. Création : le portrait se tient sur le socle du client, sur le parchemin de son écran.
            using (var account = new Accounts(config))
            using (var create = new CreateCharacter(account))
            {
                create.Show(); Application.DoEvents();
                var portrait = All(create).First(x => x.GetType().Name == "CharacterPortrait");
                var socle = (Bitmap)get.Invoke(null, new object[] { "socle" });
                Check(socle != null, "Pedestal asset missing");
                Check(portrait.BackColor.ToArgb() != BotUiPaperLight().ToArgb() && portrait.BackColor.R > 200, "Creation preview does not use the client's parchment");
                using (var image = new Bitmap(portrait.Width, portrait.Height)) {
                    portrait.DrawToBitmap(image, new Rectangle(Point.Empty, portrait.Size));
                    Check(Count(image, c => c.R < 120 && c.G < 110 && c.B < 100) > 30, "Pedestal is not drawn under the portrait");
                }
                var choose = All(create).OfType<Button>().First(x => x.Text == "Choisir…");
                Check(choose.GetType().GetProperty("Glyph", Any).GetValue(choose, null) != null, "Colour buttons lack the client's dice");
                create.Close();
            }

            // 5. Jeu : les neuf icônes du bandeau viennent du client, le bouton d'envoi garde son image.
            using (var account = new Accounts(config))
            using (var game = new GameClientFullform(account))
            {
                game.Show(); Application.DoEvents();
                var icons = All(game).OfType<Button>().Where(x => (string)x.Tag == "client-icon" && x.Image != null && x.Width == 33).ToArray();
                Check(icons.Length == 9, "Expected nine HUD icon buttons, found " + icons.Length);
                foreach (string name in new[] { "icone-caracteristiques", "icone-sorts", "icone-inventaire", "icone-quetes", "icone-carte", "icone-amis", "icone-guilde", "icone-monture", "icone-pvp" })
                    Check(icons.Any(x => ReferenceEquals(x.Image, icon.Invoke(null, new object[] { name, 24 }))), "HUD does not use the client icon " + name);
                var send = All(game).OfType<Button>().First(x => x.Width == 32 && x.Image != null && x.GetType().Name == "ClientButton");
                using (var image = new Bitmap(send.Width, send.Height)) {
                    send.DrawToBitmap(image, new Rectangle(Point.Empty, send.Size));
                    Check(Count(image, c => c.R < 110 && c.G < 110 && c.B < 110) > 4, "Send button image is not painted");
                }
                game.Close();
            }
        }
        finally { Environment.CurrentDirectory = previous; }
    }
    private static Color BotUiPaperLight()
    {
        var field = typeof(LoginForm).Assembly.GetType("Outil_Azur_complet.Bot.BotUi").GetField("PaperLight", Any);
        return (Color)field.GetValue(null);
    }
}
