using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Tool_Editor.maps.data
{
   public class BuilderClass
    {
        public static string[] StringArray = { "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z", "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z", "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "-", "_" };

       
        public static string GetCellData(CellsData Cell)
        {
            if (Cell == null)
                return "";
            int gfx1 = 0;
            if (Cell.GFX1 != null)
                gfx1 = Cell.GFX1.ID;
            int gfx2 = 0;
            if (Cell.GFX2 != null)
                gfx2 = Cell.GFX2.ID;
            int gfx3 = 0;
            if (Cell.GFX3 != null)
                gfx3 = Cell.GFX3.ID;

            string STR = "";
            int[] IntArray = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            if (Cell.Los)
            {
                IntArray[0] = (IntArray[0] | 1);
            }
            else
            {
                IntArray[0] = (IntArray[0] | 0);
            }

            IntArray[0] = (IntArray[0] | ((gfx1 & 0x600) >> 6));
            IntArray[0] = (IntArray[0] | ((gfx2 & 0x2000) >> 11));
            IntArray[0] = (IntArray[0] | ((gfx3 & 0x2000) >> 12));
            IntArray[1] = ((Cell.RotaGFX1 & 3) << 4);
            IntArray[1] = (IntArray[1] | (Cell.NivSol & 15));
            IntArray[2] = ((Cell.Type() & 7) << 3);
            IntArray[2] = (IntArray[2] | ((gfx1 >> 6) & 7));
            IntArray[3] = (gfx1 & 0x3f);
            IntArray[4] = ((Cell.IncliSol & 15) << 2);

            if (Cell.FlipGFX1)
            {
                IntArray[4] = (IntArray[4] | 2);
            }
            else
            {
                IntArray[4] = (IntArray[4] | 0);
            }

            IntArray[4] = (IntArray[4] | ((gfx2 >> 12) & 1));
            IntArray[5] = ((gfx2 >> 6) & 0x3f);
            IntArray[6] = ((gfx2 & 0x3f));
            IntArray[7] = ((Cell.RotaGFX2 & 3) << 4);

            if (Cell.FlipGFX2)
            {
                IntArray[7] = (IntArray[7] | 8);
            }
            else
            {
                IntArray[7] = (IntArray[7] | 0);
            }
            if (Cell.FlipGFX3)
            {
                IntArray[7] = (IntArray[7] | 4);
            }
            else
            {
                IntArray[7] = (IntArray[7] | 0);
            }
            if (Cell.IO)
            {
                IntArray[7] = (IntArray[7] | 2);
            }
            else
            {
                IntArray[7] = (IntArray[7] | 0);
            }

            IntArray[7] = (IntArray[7] | ((gfx3 >> 12) & 1));
            IntArray[8] = ((gfx3 >> 6) & 0x3f);
            IntArray[9] = (gfx3 & 0x3f);

            foreach(int num in IntArray)
            {
                STR = StringArray[num];
                continue;
            }
            return STR;
        }
    }
}
