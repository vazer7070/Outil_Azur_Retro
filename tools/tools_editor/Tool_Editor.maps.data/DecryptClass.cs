using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tool_Editor.maps.data
{
   public class DecryptClass
    {
        public static string CheckSum(string data)
        {
            int num = 0;
            int num2 = (int)(data.Length - 1);
            int i = 0;
            while(i <= num2)
            {
                num = num + (Strings.Asc(data.Substring(i, 1)) % 0x10);
                i += 1;
            }
            string[] STR = new string[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "A", "B", "C", "D", "E", "F" };
            return STR[num % 0x10];
        }

        public static string DecypherData(string data, string key, int checksum)
        {
            string sata = "";
            int num3 = data.Length - 2;
            int i = 0;
            while(i <= num3)
            {
                int num = Convert.ToInt32(Convert.ToInt64(data.Substring(i, 2), 0x10));
                int num2 = Strings.Asc(key.Substring((int)Math.Round(Convert.ToDouble((i / 2) + checksum) % key.Length, 1)));
                sata = sata + Convert.ToString(Strings.Chr(CharCode: num ^ num2));
                i = (i + 2);
            }
            return Unescape(sata);
        }
        public static string PrepareKey(string data)
        {
            string d = "";
            int num = (data.Length - 2);
            int i = 0;
            while(i <= num)
            {
                d += Convert.ToString(Strings.Chr(Convert.ToInt32(Convert.ToInt64(d.Substring(i, 2), 0x10))));
                i = (i + 2);
            }
            return Unescape(d);
        }
        public static object HashCode(string a)
        {
            string s = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";
            return s.IndexOf(a);
        }
        private static string Unescape(string DataToDecrypt)
        {
            return Uri.EscapeDataString(DataToDecrypt);
        }
    }
}
