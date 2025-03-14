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

            int gfx1 = Cell.GFX1 != null ? Cell.GFX1.ID : 0;
            int gfx2 = Cell.GFX2 != null ? Cell.GFX2.ID : 0;
            int gfx3 = Cell.GFX3 != null ? Cell.GFX3.ID : 0;

            int[] IntArray = new int[10];
            IntArray[0] = (Cell.Los ? 1 : 0) | ((gfx1 & 0x600) >> 6) | ((gfx2 & 0x2000) >> 11) | ((gfx3 & 0x2000) >> 12);
            IntArray[1] = (Cell.RotaGFX1 << 4) | (Cell.NivSol & 15);
            IntArray[2] = (Cell.Type() & 7) << 3 | ((gfx1 >> 6) & 7);
            IntArray[3] = gfx1 & 0x3f;
            IntArray[4] = ((Cell.IncliSol & 15) << 2) | (Cell.FlipGFX1 ? 2 : 0) | ((gfx2 >> 12) & 1);
            IntArray[5] = (gfx2 >> 6) & 0x3f;
            IntArray[6] = gfx2 & 0x3f;
            IntArray[7] = (Cell.RotaGFX2 & 3) << 4 | (Cell.FlipGFX2 ? 8 : 0) | (Cell.FlipGFX3 ? 4 : 0) | (Cell.IO ? 2 : 0) | ((gfx3 >> 12) & 1);
            IntArray[8] = (gfx3 >> 6) & 0x3f;
            IntArray[9] = gfx3 & 0x3f;

            StringBuilder sb = new StringBuilder();
            foreach (int num in IntArray)
            {
                sb.Append(StringArray[num]);
            }
            string STR = sb.ToString();
            return STR;
        }
    }

}
