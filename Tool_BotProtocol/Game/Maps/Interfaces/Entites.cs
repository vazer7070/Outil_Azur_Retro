using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tool_BotProtocol.Game.Maps.Interfaces
{
    public interface Entites : IDisposable
    {
        int id { get; set; }
        Cell Cell { get; set; }
        string Name { get; set; }
    }
}
