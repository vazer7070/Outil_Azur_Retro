using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Combats;
using Tool_BotProtocol.Game.Managers;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Network;
using Tool_BotProtocol.Utils.Interfaces;

namespace Tool_BotProtocol.Game
{
    public  class GameClass: IEliminable, IDisposable
    {
        private readonly object lifecycleSync = new object();
        private bool disposed;
        public GameServer Server { get; set; }
        public Map Map { get; private set; }
        public CharacterClass character { get; private set; }
        public Fights Fight { get; private set; }
        public Manager Manager { get; private set; }
        public ConcurrentDictionary<int, Dictionary<string, Cell>> PersoInWorld;
         internal GameClass(Accounts.Accounts A)
        {
            Server = new GameServer();
            Map = new Map();
            character= new CharacterClass(A);
            Manager = new Manager(A, Map, character);
            Fight = new Fights(A);
            PersoInWorld = new ConcurrentDictionary<int, Dictionary<string, Cell>>();
        }
        public void Clear()
        {
            lock (lifecycleSync)
            {
                if (disposed) return;
           Manager.Clear();
           Fight.Clear(false);
           Map.Clear();
            character.Clear();
            Server.Clear();
            PersoInWorld.Clear();
            }
        }

        public void Dispose()
        {
            lock (lifecycleSync)
            {
                if (disposed) return;
                disposed = true;
            Manager?.Dispose();
            Fight?.Dispose();
            character?.Dispose();
            Map?.Dispose();
            PersoInWorld.Clear();
            }
        }
    }
}
