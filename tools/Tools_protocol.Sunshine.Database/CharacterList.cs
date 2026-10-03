using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools_protocol.Managers;

namespace Tools_protocol.Sunshine.Database
{
    [EmuManager("Sunshine", "CharacterList")]
    public class CharacterList
    {
        public static Dictionary<int, string> IdCompte = new Dictionary<int, string>();
    }
}
