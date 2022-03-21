using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tool_BotProtocol.Game.Groupes
{
    public class Regroupement : IDisposable
    {
        private Groupe groupe;
        List<Accounts.Accounts> Missing_player;
        private bool disposed;

        public Regroupement(Groupe G) => groupe = G;
        public void Dispose() => Dispose(true);
        ~Regroupement() => Dispose(false);
        protected virtual void Dispose(bool di)
        {
            if (!disposed)
            {
                groupe = null;
                Missing_player?.Clear();
                Missing_player = null;
                disposed = true;
            }
        }
    }
}
