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
        public int Orientation { get; set; } = 2;
        public int GraphicsScaleX { get; set; } = 100;
        public int GraphicsScaleY { get; set; } = 100;
        public Cell Cell { get ; set ; }
        public int Level { get; set; }
        public List<Monstres>MobsInGroupe { get; set; }
        public Monstres GroupeLeader { get; set; }
        bool IDisposed;

        private static string MonstersPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ressources", "Bot", "BotMonsters");
        public static ConcurrentDictionary<int, Monstres> AllMonstersTemplate = new ConcurrentDictionary<int, Monstres>();
        public int GetAllMonster => MobsInGroupe.Count;
        public int MobsGroupelevel => MobsInGroupe.Sum(x => x.Level);
        public int Star { get; set; }
        public string Name { get; set; }

        public Monstres(int ID, int temp, Cell C, int level, int S)
        {
            id = ID;
            TemplateID = temp;
            if (AllMonstersTemplate.ContainsKey(temp))
            {
                Name = AllMonstersTemplate[temp].Name;
                GFX = AllMonstersTemplate[temp].GFX;
            }
            else
                Name = $"Monstre {temp}";
            Cell = C;
            Level = level;
            MobsInGroupe = new List<Monstres>();
            Star = S;
        }
        
        public static Monstres ReturnMonsters(int template) => AllMonstersTemplate.TryGetValue(template, out Monstres value) ? value : null;
        public static Task LoadAllMonstersAsync()
        {
            return Task.Run(() =>
            {
                var loaded = new ConcurrentDictionary<int, Monstres>();
                foreach (string file in Directory.EnumerateFiles(MonstersPath, "*.xml"))
                {
                    XElement xml = XElement.Load(file);
                    var monster = new Monstres { TemplateID = int.Parse(xml.Element("ID").Value),
                        Name = xml.Element("NAME").Value, GFX = int.Parse(xml.Element("GFX").Value) };
                    loaded[monster.TemplateID] = monster;
                }
                AllMonstersTemplate = loaded;
            });
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
            for(int i = 0;i < MobsInGroupe.Count; i++)
            {
                if(MobsInGroupe[i].TemplateID.Equals(id))
                    nombre++;
            }
            return nombre;
        }

        public void Dispose() => Dispose(true);
        Monstres() { MobsInGroupe = new List<Monstres>(); }
        public virtual void Dispose(bool disposed)
        {
            if (!IDisposed)
            {
                MobsInGroupe.Clear();
                MobsInGroupe = null;
                IDisposed = true;
            }
        }
    }
}
