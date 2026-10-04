using System;
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
        /// <summary>Récolte et état des objets interactifs de la carte (<c>GDF</c>).</summary>
        public Harvest Harvest { get; private set; }
        private bool disposed;

        public Manager(Accounts.Accounts A, Map map, CharacterClass perso)
        {
            Mouvements = new Mouvement(A, map, perso);
            Harvest = new Harvest(A, Mouvements, map);
        }

        public void Clear()
        {
            Harvest.Clear();
            Mouvements.Clear();
        }

        public void Dispose() => Dispose(true);
        protected virtual void Dispose(bool d)
        {
            if(disposed) return;
            if(d)
            {
                Harvest.Dispose();
                Mouvements.Dispose();
            }
            Mouvements = null;
            disposed = true;
        }
    }
}
