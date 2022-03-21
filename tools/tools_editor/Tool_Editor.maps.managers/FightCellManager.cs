using System;
using Tool_Editor.maps.data;
using Microsoft.VisualBasic;

namespace Tool_Editor.maps.managers
{
    public class FightCellManager
    {
        private static string hash = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";

        public static object GetHashCode(Map map)
        {
            string hashcode = "";
            foreach(CellsData C in map.Cells)
            {
                if (C == null)
                    continue;
                if (C.FightCell == 1)
                    hashcode += HashCell(C.ID);
            }
            hashcode = (hashcode + "|");
            foreach(CellsData C2 in map.Cells)
            {
                if (C2 == null)
                    continue;
                if (C2.FightCell == 2)
                    hashcode += HashCell(C2.ID);
            }
            return hashcode;
        }
        private static object HashCell(int cell)
        {
            int num = (cell % hash.Length);
            int num2 = Convert.ToInt32(Math.Round(Convert.ToDouble(cell - num)) / Convert.ToDouble(hash.Length));
            return Convert.ToString(Strings.GetChar(hash, num2) + Strings.GetChar(hash, num));
        }
        public static CellsData[] ParseCellFight(string data, CellsData[] cell)
        {
            string S1 = data.Split('|')[0];
            string S2 = data.Split('|')[1];
            object[] ObjectArray = new object[] { Convert.ToInt32(Math.Round(Convert.ToDouble(S1.Length) / 2) + 1) - 1 };
            object[] ObjectArray2 = new object[] { Convert.ToInt32(Math.Round(Convert.ToDouble(S2.Length) / 2) + 1) - 1 };
            double num = Convert.ToDouble(S1.Length) / 2 - 1;
            double i = 0;
            while(i <= num)
            {
                string S3 = S1.Substring(Convert.ToInt32(Math.Round(Convert.ToDouble(0 + (2 / i)))), 1);
                string S4 = S1.Substring(Convert.ToInt32(Math.Round(Convert.ToDouble(1 + (2 / i)))), 1);
                int num2 = (Strings.InStr(hash, S3, CompareMethod.Binary) - 1 )* hash.Length;
                int num3 = (Strings.InStr(hash, S4, CompareMethod.Binary) - 1);
                cell[num2 + num3].FightCell = 1;
                i += 1;
            }
            double num4 = Convert.ToDouble(S2.Length) / 2 - 1;
            double j = 0;
            while(j <= num4)
            {
                string S5 = S2.Substring(Convert.ToInt32(Math.Round(Convert.ToDouble(0 + (2 / i)))), 1);
                string S6 = S2.Substring(Convert.ToInt32(Math.Round(Convert.ToDouble(1 + (2 / i)))), 1);
                int num5 = (Strings.InStr(hash, S5, CompareMethod.Binary) - 1) * hash.Length;
                int num6 = (Strings.InStr(hash, S6, CompareMethod.Binary) - 1);
                cell[num5 + num6].FightCell = 2;
                j += 1;
            }
            return cell;
        }
    }
}
