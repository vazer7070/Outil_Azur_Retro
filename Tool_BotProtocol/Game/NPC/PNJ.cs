using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Interfaces;

namespace Tool_BotProtocol.Game.NPC
{
    public class PNJ : Entites
    {
        public int id { get; set; }
        
        public int MapId { get; set; }
        public int CellId { get; set; }
        public int Orientation { get; set; }
        public int GFX { get; set; }
        public int Sexe { get; set; }
        public Cell Cell { get; set; }
        public int NPc_ID { get; set; }
        public short Question_ID { get; set; }
        public List<short> Réponses { get; set; }
        public string Name { get; set; }

        private static string pnjpath = @".\ressources\Bot\BotNPCs";

        private bool dispose;
        public static ConcurrentDictionary<int, PNJ> AllPNJ = new ConcurrentDictionary<int, PNJ>();
        public PNJ(int Id, int Self_ID, Cell C)
        {
            id = Id;
            if (AllPNJ.ContainsKey(Self_ID))
            {
                Name = AllPNJ[Self_ID].Name;
                MapId = AllPNJ[Self_ID].MapId;
                CellId = AllPNJ[Self_ID].CellId;
                Orientation = AllPNJ[Self_ID].Orientation;
                GFX = AllPNJ[Self_ID].GFX;
                Sexe = AllPNJ[Self_ID].Sexe;
            }
            else
                Name = $"Undefined({Self_ID})";
            Cell = C;
            NPc_ID = Self_ID;
        }
        public static PNJ ReturnNpc(int id, bool notSelfid)
        {
            try
            {
                if (notSelfid)
                    return AllPNJ.FirstOrDefault(x => x.Value.id == id).Value;
                else
                    return AllPNJ[id];
            }
            catch
            {
                return null;
            }
        }
        public static void LoadAllNPC()
        {
            try
            {
                DirectoryInfo MapFolder = new DirectoryInfo($"{pnjpath}");
                foreach (FileInfo f in MapFolder.GetFiles())
                {
                    if (f.Exists && f.Extension.Equals(".xml"))
                    {
                        XElement xmlnpc = XElement.Load(f.FullName);
                        PNJ P = new PNJ
                        {
                            id = int.Parse(xmlnpc.Element("ID").Value),
                            Name = xmlnpc.Element("NOM").Value,
                            MapId = int.Parse(xmlnpc.Element("MAP").Value),
                            CellId = int.Parse(xmlnpc.Element("CELLULE").Value),
                            Orientation = int.Parse(xmlnpc.Element("ORIENTATION").Value),
                            GFX = int.Parse(xmlnpc.Element("GFX").Value),
                            Sexe = int.Parse(xmlnpc.Element("SEXE").Value)
                        };
                        AllPNJ.TryAdd(P.id, P);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return;
            }

        }
        PNJ() => Dispose(true);

        public void Dispose() => Dispose(true);
        public virtual void Dispose( bool disposed)
        {
            if (!dispose)
            {
                Réponses?.Clear();
                Réponses = null;
                Cell = null;
                dispose = true;
            }
        }
    }
}
