using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        static string ZPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ressources", "Bot", "BotZaaps");
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
    }
}
