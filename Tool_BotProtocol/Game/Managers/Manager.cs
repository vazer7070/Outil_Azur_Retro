using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Managers.Mouvements;
using Tool_BotProtocol.Game.Managers.recoltes;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Utils.Interfaces;

namespace Tool_BotProtocol.Game.Managers
{
    public  class Manager : IEliminable, IDisposable
    {
        public Mouvement Mouvements { get; private set; }
        public RecoltesClass Recolte { get; private set; }
        public Teleport.Teleport Teleport { get; private set; }
        private bool disposed;

        public Manager(Accounts.Accounts A, Map map, CharacterClass perso)
        {
            Mouvements = new Mouvement(A, map, perso);
            Recolte = new RecoltesClass(A, Mouvements, map);
            Teleport = new Teleport.Teleport(A, Mouvements, map);
        }

        public void Clear()
        {
            Mouvements.Clear();
            Recolte.Clear();
            Teleport.Dispose();
        }

        public void Dispose() => Dispose(true);
        ~Manager() => Dispose(true);
        protected virtual void Dispose(bool d)
        {
            if(disposed) return;
            if(d)
                Mouvements.Dispose();
            Mouvements = null;
            disposed = true;
        }
    }
}
