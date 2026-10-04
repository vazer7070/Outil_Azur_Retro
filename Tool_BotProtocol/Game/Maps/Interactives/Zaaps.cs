using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace Tool_BotProtocol.Game.Maps.Interactives
{
    public class Zaaps
    {
        public static ConcurrentDictionary<int, int> Z = new ConcurrentDictionary<int, int>();
        /// <summary>
        /// Zaapis connus du serveur (<c>zaapi</c>) : carte → alignement (1 Bonta, 2 Brâkmar ; StarLoco range toute autre
        /// valeur dans la liste neutre). La liste et le prix réels arrivent par <c>Wc</c> quand le zaapi est utilisé.
        /// </summary>
        public static ConcurrentDictionary<int, int> Zaapis = new ConcurrentDictionary<int, int>();
        public static string[] LoadWarnings { get; private set; } = new string[0];
        static string ZPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ressources", "Bot", "BotZaaps");
        public static string ZaapisPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ressources", "Bot", "BotZaapis");

        public static Task LoadZaapsAsync()
        {
            return Task.Run(() =>
            {
                var loaded = new ConcurrentDictionary<int, int>();
                foreach (string file in Directory.EnumerateFiles(ZPath, "*.xml"))
                {
                    XElement xml = XElement.Load(file);
                    loaded[int.Parse(xml.Element("MAP").Value)] = int.Parse(xml.Element("CELLULE").Value);
                }
                Z = loaded;
            });
        }

        public static Task LoadZaapisAsync() => LoadZaapisAsync(ZaapisPath);

        /// <summary>
        /// Charge <c>BotZaapis</c> hors du thread appelant ; les fichiers illisibles sont listés dans <see cref="LoadWarnings"/>
        /// et un dossier absent donne une liste vide.
        /// </summary>
        public static Task LoadZaapisAsync(string directory)
        {
            return Task.Run(() =>
            {
                var loaded = new ConcurrentDictionary<int, int>();
                var warnings = new List<string>();
                if (Directory.Exists(directory))
                {
                    foreach (string file in Directory.EnumerateFiles(directory, "*.xml"))
                    {
                        try
                        {
                            XElement xml = XElement.Load(file);
                            int map = Number(xml, "MAP"), alignment = Number(xml, "ALIGNEMENT");
                            if (map <= 0) throw new FormatException("carte invalide.");
                            loaded[map] = alignment;
                        }
                        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                            || error is System.Xml.XmlException || error is FormatException)
                        { warnings.Add(Path.GetFileName(file) + " : " + error.Message); }
                    }
                }
                else warnings.Add("Dossier absent : " + directory);
                Zaapis = loaded;
                LoadWarnings = warnings.ToArray();
            });
        }

        private static int Number(XElement xml, string name)
        {
            string text = ((string)xml.Element(name) ?? "").Trim();
            int value;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                throw new FormatException("Élément " + name + " absent ou invalide : « " + text + " ».");
            return value;
        }
    }
}
