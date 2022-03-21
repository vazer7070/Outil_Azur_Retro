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
        static int MapId { get; set; }
        static int CellId { get; set; }

        public static ConcurrentDictionary<int, int> Z = new ConcurrentDictionary<int, int>();

        static string ZPath = @".\ressources\Bot\BotZaaps";
        public static void LoadZaaps()
        {
            try
            {
                DirectoryInfo MapFolder = new DirectoryInfo($"{ZPath}");
                foreach (FileInfo f in MapFolder.GetFiles())
                {
                    if (f.Exists)
                    {
                        XElement xmlmap = XElement.Load(f.FullName);

                        MapId = int.Parse(xmlmap.Element("MAP").Value);
                        CellId = int.Parse(xmlmap.Element("CELLULE").Value);
                    }
                    Z.TryAdd(MapId, CellId);
                }
                
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return;
            }
        }
       
        
    }
}
