using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;

namespace Tool_BotProtocol.Game.Monstres
{
    /// <summary>
    /// Monstre (modèle de <c>BotMonsters</c>, membre d'un groupe) ou groupe de monstres d'une carte (vue historique).
    /// Les groupes lus dans <c>GM</c> sont des <see cref="MonsterGroupActor"/> et les monstres de combat des
    /// <see cref="FightMonsterActor"/> ; tous deux dérivent de cette classe.
    /// </summary>
    public class Monstres : MapActor
    {
        public override ActorKind Kind => ActorKind.MonsterGroup;
        public int TemplateID { get; set; } = 0;
        public int GFX { get => Gfx; set => Gfx = value; }
        public int GraphicsScaleX { get => ScaleX; set => ScaleX = value; }
        public int GraphicsScaleY { get => ScaleY; set => ScaleY = value; }
        public int Level { get; set; }
        public List<Monstres> MobsInGroupe { get; set; }
        public Monstres GroupeLeader { get; set; }

        private static string MonstersPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ressources", "Bot", "BotMonsters");
        public static ConcurrentDictionary<int, Monstres> AllMonstersTemplate = new ConcurrentDictionary<int, Monstres>();
        public int GetAllMonster => MobsInGroupe.Count;
        public int MobsGroupelevel => MobsInGroupe.Sum(x => x.Level);
        public int Star { get; set; }

        /// <summary>
        /// Nom des monstres fourni par les textes du client (<c>monsters_fr</c>) lorsqu'ils sont chargés ;
        /// null ou une chaîne vide fait retomber sur <c>BotMonsters</c> puis sur « Monstre #modèle ».
        /// </summary>
        public static Func<int, string> ClientNameResolver { get; set; }

        public Monstres(int ID, int temp, Cell C, int level, int S) : this()
        {
            id = ID;
            TemplateID = temp;
            if (AllMonstersTemplate.TryGetValue(temp, out Monstres template) && template != null) GFX = template.GFX;
            Name = ResolveName(temp);
            Cell = C;
            Level = level;
            Star = S;
        }

        /// <summary>Membre d'un groupe lu dans <c>GM</c> : modèle, niveau et sprite transmis par le serveur.</summary>
        public Monstres(MonsterGroupMember member, MonsterGroupActor group) : this(group?.id ?? 0, member?.TemplateId ?? 0, group?.Cell, member?.Level ?? 0, group?.Stars ?? 0)
        {
            if (member == null || group == null) return;
            if (member.Gfx > 0) GFX = member.Gfx;
            ScaleX = member.ScaleX; ScaleY = member.ScaleY;
            Orientation = group.Orientation;
            if (group.Cell == null) CellId = group.CellId;
            GroupeLeader = group;
        }

        /// <summary>Nom affiché d'un modèle de monstre : textes du client, puis <c>BotMonsters</c>, puis « Monstre #modèle ».</summary>
        public static string ResolveName(int templateId)
        {
            string name = null;
            try { name = ClientNameResolver?.Invoke(templateId); }
            catch (Exception error) when (!(error is OutOfMemoryException)) { name = null; }
            if (string.IsNullOrEmpty(name) && AllMonstersTemplate.TryGetValue(templateId, out Monstres template)) name = template?.Name;
            return string.IsNullOrEmpty(name) ? "Monstre #" + templateId : name;
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
            if (GroupeLeader != null && GroupeLeader.TemplateID == id)
                return true;
            for (int i = 0; i < MobsInGroupe.Count; i++)
            {
                if (MobsInGroupe[i].TemplateID == id)
                    return true;
            }
            return false;
        }
        public int GroupSize(int id)
        {
            int nombre = 0;
            for (int i = 0; i < MobsInGroupe.Count; i++)
            {
                if (MobsInGroupe[i].TemplateID.Equals(id))
                    nombre++;
            }
            return nombre;
        }

        /// <summary>Modèle, membre ou acteur dont les champs sont remplis ensuite.</summary>
        protected Monstres() { MobsInGroupe = new List<Monstres>(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing && MobsInGroupe != null)
            {
                MobsInGroupe.Clear();
                MobsInGroupe = null;
            }
            base.Dispose(disposing);
        }
    }
}
