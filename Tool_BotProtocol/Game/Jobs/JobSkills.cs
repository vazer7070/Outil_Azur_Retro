using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Maps.Interactives;

namespace Tool_BotProtocol.Game.Jobs
{
    public class JobSkills
    {
        public short Id { get;  private set; }
        public byte QuaMini { get; private set; }
        public byte QuaMax { get; private set; }
        public InteractivesParent Interactive { get; private set; }
        public bool CanCraft { get; private set; } = true;
        public float Time { get; private set; }

        public JobSkills(short id, byte min, byte max, float T)
        {
            Id = id;
            QuaMini = min;
            QuaMax = max;
            InteractivesParent parent = InteractivesParent.GetInteractiveBySkill(id);
            if (parent != null)
            {
                Interactive = parent;
                if (Interactive.Recoltable)
                    CanCraft = false;
            }
            Time = T;
        }
        public void Actualise(short id, byte min, byte max, float T)
        {
            Id = id;
            QuaMini = min;
            QuaMax =max;
            Time = T;
        }
    }
}
