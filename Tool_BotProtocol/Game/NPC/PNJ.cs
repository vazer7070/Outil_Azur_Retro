using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;

namespace Tool_BotProtocol.Game.NPC
{
    /// <summary>
    /// PNJ : modèle chargé de <c>BotNPCs</c> (<see cref="AllPNJ"/>, indexé par identifiant de modèle) ou PNJ d'une carte.
    /// Les PNJ lus dans <c>GM</c> sont des <see cref="NpcActor"/>, qui dérivent de cette classe.
    /// </summary>
    public class PNJ : MapActor
    {
        public override ActorKind Kind => ActorKind.Npc;
        public int MapId { get; set; }
        public int GFX { get => Gfx; set => Gfx = value; }
        public int GraphicsScaleX { get => ScaleX; set => ScaleX = value; }
        public int GraphicsScaleY { get => ScaleY; set => ScaleY = value; }
        public int Sexe { get; set; }
        /// <summary>Identifiant du modèle (<c>npc_template</c>) ; différent de <see cref="MapActor.Id"/> chez StarLoco.</summary>
        public int NPc_ID { get; set; }
        public short Question_ID { get; set; }
        public List<short> Réponses { get; set; }

        private static string pnjpath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ressources", "Bot", "BotNPCs");

        public static ConcurrentDictionary<int, PNJ> AllPNJ = new ConcurrentDictionary<int, PNJ>();

        /// <summary>
        /// Nom des PNJ fourni par les textes du client (<c>npc_fr</c>, <c>N.d[id].n</c>) lorsqu'ils sont chargés ;
        /// null ou une chaîne vide fait retomber sur <c>BotNPCs</c> puis sur « PNJ #modèle ».
        /// </summary>
        public static Func<int, string> ClientNameResolver { get; set; }

        public PNJ(int Id, int Self_ID, Cell C)
        {
            id = Id;
            if (AllPNJ.TryGetValue(Self_ID, out PNJ template) && template != null)
            {
                MapId = template.MapId;
                Orientation = template.Orientation;
                GFX = template.GFX;
                Sexe = template.Sexe;
                CellId = template.CellId;
            }
            Name = ResolveName(Self_ID);
            NPc_ID = Self_ID;
            if (C != null) Cell = C;
        }

        /// <summary>Nom affiché d'un modèle de PNJ : textes du client, puis <c>BotNPCs</c>, puis « PNJ #modèle ».</summary>
        public static string ResolveName(int templateId)
        {
            string name = null;
            try { name = ClientNameResolver?.Invoke(templateId); }
            catch (Exception error) when (!(error is OutOfMemoryException)) { name = null; }
            if (string.IsNullOrEmpty(name) && AllPNJ.TryGetValue(templateId, out PNJ template)) name = template?.Name;
            return string.IsNullOrEmpty(name) ? "PNJ #" + templateId : name;
        }

        public static PNJ ReturnNpc(int id, bool notSelfid)
        {
            if (notSelfid) return AllPNJ.FirstOrDefault(x => x.Value.id == id).Value;
            return AllPNJ.TryGetValue(id, out PNJ value) ? value : null;
        }
        public static Task LoadAllNPCAsync()
        {
            return Task.Run(() =>
            {
                var loaded = new ConcurrentDictionary<int, PNJ>();
                foreach (string file in Directory.EnumerateFiles(pnjpath, "*.xml"))
                {
                    XElement xml = XElement.Load(file);
                    var npc = new PNJ { id = int.Parse(xml.Element("ID").Value), Name = xml.Element("NOM").Value,
                        MapId = int.Parse(xml.Element("MAP").Value), CellId = int.Parse(xml.Element("CELLULE").Value),
                        Orientation = int.Parse(xml.Element("ORIENTATION").Value), GFX = int.Parse(xml.Element("GFX").Value),
                        Sexe = int.Parse(xml.Element("SEXE").Value) };
                    loaded[npc.id] = npc;
                }
                AllPNJ = loaded;
            });
        }

        /// <summary>Construction d'un modèle ou d'un acteur dont les champs sont remplis ensuite.</summary>
        protected PNJ() { }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Réponses?.Clear();
                Réponses = null;
            }
            base.Dispose(disposing);
        }
    }
}
