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
            if (data == null) throw new ArgumentNullException(nameof(data));
            int sum = 0;
            foreach (char value in data) sum = (sum + value % 16) % 16;
            return "0123456789ABCDEF"[sum].ToString();
        }

        public static string DecypherData(string data, string key, int checksum)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("La clé de carte est vide.", nameof(key));
            ValidateHex(data);
            var decoded = new StringBuilder(data.Length / 2);
            int offset = ((checksum % key.Length) + key.Length) % key.Length;
            for (int i = 0; i < data.Length; i += 2)
            {
                int value = Convert.ToInt32(data.Substring(i, 2), 16);
                decoded.Append((char)(value ^ key[(i / 2 + offset) % key.Length]));
            }
            return Uri.UnescapeDataString(decoded.ToString());
        }
        public static string PrepareKey(string data)
        {
            ValidateHex(data);
            var decoded = new StringBuilder(data.Length / 2);
            for (int i = 0; i < data.Length; i += 2)
                decoded.Append((char)Convert.ToInt32(data.Substring(i, 2), 16));
            return Uri.UnescapeDataString(decoded.ToString());
        }
        public static object HashCode(string a)
        {
            string s = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";
            return s.IndexOf(a);
        }
        private static void ValidateHex(string data)
        {
            if (string.IsNullOrEmpty(data) || (data.Length & 1) != 0 ||
                data.Any(value => !Uri.IsHexDigit(value)))
                throw new FormatException("La clé ou les données chiffrées doivent contenir des paires de caractères hexadécimaux.");
        }
    }
}
