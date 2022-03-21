using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tool_BotProtocol.Utils.Crypto
{
    internal class Randomize
    {
        private static int seed = Environment.TickCount;
        private static readonly ThreadLocal<Random> random = new ThreadLocal<Random>(() => new Random(Interlocked.Increment(ref seed)));
        private static readonly object sema = new object();

        public static int get_Random(int min, int max)
        {
            lock (sema)
            {
                return min <= max ? random.Value.Next(min, max) : random.Value.Next(max, min);
            }
        }

        public static double get_Random_Numero()
        {
            lock (sema)
            {
                return random.Value.NextDouble();
            }
        }
    }
}
