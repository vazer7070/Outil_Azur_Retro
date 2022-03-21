using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Interfaces;

namespace Tool_BotProtocol.Game.Monstres
{
    public class Monstres : Entites
    {
        public int id { get; set; } = 0;
        public int TemplateID { get; set; } = 0;
        public int GFX { get; set; } = 0;
        public Cell Cell { get ; set ; }
        public int Level { get; set; }
        public List<Monstres>MobsInGroupe { get; set; }
        public Monstres GroupeLeader { get; set; }
        bool IDisposed;

        private static string MonstersPath = @".\ressources\Bot\BotMonsters\";
        public static ConcurrentDictionary<int, Monstres> AllMonstersTemplate = new ConcurrentDictionary<int, Monstres>();
        public int GetAllMonster => MobsInGroupe.Count + 1;
        public int MobsGroupelevel => MobsInGroupe.Sum(x => x.Level);
        public int Star { get; set; }
        public string Name { get; set; }

        public Monstres(int ID, int temp, Cell C, int level, int S)
        {
            id = ID;
            TemplateID = temp;
            if (AllMonstersTemplate.ContainsKey(temp))
                Name = AllMonstersTemplate[temp].Name;
            else
                Name = $"Undefined ({temp})";
            Cell = C;
            Level = level;
            MobsInGroupe = new List<Monstres>();
            Star = S;
        }
        
        public static Monstres ReturnMonsters(int template) => AllMonstersTemplate[template];
        public static void LoadAllMonstrers()
        {
            try
            {
                DirectoryInfo MapFolder = new DirectoryInfo($"{MonstersPath}");
                foreach (FileInfo f in MapFolder.GetFiles())
                {
                    if (f.Exists)
                    {
                        XElement xmlmap = XElement.Load(f.FullName);
                        Monstres M = new Monstres
                        {
                            TemplateID = int.Parse(xmlmap.Element("ID").Value),
                            Name = xmlmap.Element("NAME").Value,
                            GFX = int.Parse(xmlmap.Element("GFX").Value)
                        };
                        AllMonstersTemplate.TryAdd(M.TemplateID, M);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return;
            }
        }
        public bool GroupHasThisMob(int id)
        {
            if(GroupeLeader.TemplateID == id)
                return true;
            for(int i = 0; i < MobsInGroupe.Count; i++)
            {
                if (MobsInGroupe[i].TemplateID == id)
                    return true;
            }
            return false;
        }
        public int GroupSize(int id)
        {
            int nombre = 0;
            if(GroupeLeader.TemplateID == id)
                nombre++;
            for(int i = 0;i < MobsInGroupe.Count; i++)
            {
                if(MobsInGroupe[i].TemplateID.Equals(id))
                    nombre++;
            }
            return nombre;
        }

        public void Dispose() => Dispose(true);
        Monstres() => Dispose(false);
        public virtual void Dispose(bool disposed)
        {
            if (IDisposed)
            {
                MobsInGroupe.Clear();
                MobsInGroupe = null;
                disposed = true;
            }
        }
    }
}
