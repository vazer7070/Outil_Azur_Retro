using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Managers.Mouvements;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Interactives;
using Tool_BotProtocol.Game.Maps.Mouvements;
using Tool_BotProtocol.Game.Perso;

namespace Tool_BotProtocol.Game.Managers.Teleport
{
    public class Teleport : IDisposable
    {
        private Accounts.Accounts account;
        private Map Map;
        private Pathfinder pathfinder;

        private int CellID;
        private string MapID;
        private bool disposed;

        public Teleport(Accounts.Accounts A, Mouvement M, Map map)
        {
            account = A;
            Map = map;
            pathfinder = new Pathfinder();
            map.RefreshMap += SetMapEvent;


        }
        public bool CheckIfTeleportExist(string mapid)
        {
            if (account.Isbusy())
                return false;
            MapID = mapid;
            foreach(KeyValuePair<short, Interactives> zaapi in GetUsablesInteractives())
            {
                if(GetMoveInteractive(zaapi))
                    return true;
            }
            account.Logger.LogError("ZAAPI", $"Aucun zaapi trouvé sur la carte {mapid}");
            return false;
        }
        private Dictionary<short, Interactives> GetUsablesInteractives()
        {
            Dictionary<short, Interactives> UsableElements = new Dictionary<short, Interactives>();
            CharacterClass perso = account.Game.character;

            foreach(Interactives I in Map.Interactives.Values)
            {
                if (!I.IsUsable)
                    continue;
                List<Cell> Path = pathfinder.GetPath(perso.Cell, I.Cell, Map.CellsOccuped(), true, 1);

                if (Path == null || Path.Count == 0)
                    continue;

                foreach(short H in I.Interactive.Capacities)
                {
                    if(H == 157)
                    {
                        UsableElements.Add(I.Cell.CellID, I);
                        CellID = I.Cell.CellID;
                        return UsableElements;
                    }
                }
            }
            return UsableElements;
        }
        private bool GetMoveInteractive(KeyValuePair<short, Interactives> interactives)
        {
            switch(account.Game.Manager.Mouvements.GetCellsMove(interactives.Value.Cell, Map.CellsOccuped(), true, 1))
            {
                case MoveResults.EXIT:
                case MoveResults.SAMECELL:
                    TeleportTentative();
                    return true;
                    default:
                    return false;
            }
        }
        private void TeleportTentative()
        {
            account.Connexion.SendPacket($"GA500{CellID};157");
        }
        public async void InitTeleport()
        {
            await account.Connexion.SendPacketAsync($"WU{MapID}", false);
        }
        private void SetMapEvent()
        {
            CheckIfTeleportExist(Map.MapID.ToString());
            pathfinder.SetMap(account.Game.Map);
        }

        ~Teleport() => Dispose(true);

        public void Dispose() => Dispose(true);

        protected virtual void Dispose(bool D)
        {
            if (!disposed)
            {
                if (D)
                {
                    pathfinder.Dispose();
                }
                pathfinder = null;
                account = null;
                disposed = true;
            }
        }
    }
}
