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
        public static async Task LoadZaapsAsync()
        {
            try
            {
                DirectoryInfo zaapsFolder = new DirectoryInfo(ZPath);
                FileInfo[] files = zaapsFolder.GetFiles();

                List<Task> tasks = new List<Task>();

                foreach (FileInfo file in files)
                {
                    if (file.Exists)
                    {
                        tasks.Add(Task.Run(async () =>
                        {
                            XElement xmlmap = await Task.Run(() => XElement.Load(file.FullName));

                            int MapId = int.Parse(xmlmap.Element("MAP").Value);
                            int CellId = int.Parse(xmlmap.Element("CELLULE").Value);

                            Z.TryAdd(MapId, CellId);
                        }));
                    }
                }

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return;
            }
        }



    }
}
