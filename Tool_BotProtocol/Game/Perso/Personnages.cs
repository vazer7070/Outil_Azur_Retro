using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Interfaces;

namespace Tool_BotProtocol.Game.Perso
{
    public class Personnages : Entites
    {
        public int id { get; set; }
        public Cell Cell { get; set; }
        public string name { get; set; }
        public byte Sexe { get; set; } = 0;
        public string Name { get; set; }

        private bool disposed;
        public Personnages(int Id, string N, byte S, Cell C)
        {
            id = Id;
            Name = N;
            name = N;
            Sexe = S;
            Cell = C;
        }
        Personnages() => Dispose(true);

        public virtual void Dispose(bool disposed)
        {
            if (!disposed)
            {
                Cell = null;
                disposed = true;
            }
        }
        public void Dispose() => Dispose(true);
    }
}
