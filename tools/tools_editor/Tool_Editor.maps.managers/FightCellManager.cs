using System;
using Tool_Editor.maps.data;
using System.Collections.Generic;

namespace Tool_Editor.maps.managers
{
    public class FightCellManager
    {
        private static string hash = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";

        public static object GetHashCode(Map map)
        {
            if (map?.Cells == null) throw new ArgumentNullException(nameof(map));
            string hashcode = "";
            foreach (CellsData cell in map.Cells)
                if (cell != null && cell.FightCell == 1)
                    hashcode += HashCell(cell.ID);
            hashcode += "|";
            foreach (CellsData cell in map.Cells)
                if (cell != null && cell.FightCell == 2)
                    hashcode += HashCell(cell.ID);
            return hashcode;
        }

        private static object HashCell(int cell)
        {
            if (cell < 0 || cell >= hash.Length * hash.Length)
                throw new ArgumentOutOfRangeException(nameof(cell), "L'identifiant de cellule ne peut pas être encodé.");
            return string.Concat(hash[cell / hash.Length], hash[cell % hash.Length]);
        }

        public static CellsData[] ParseCellFight(string data, CellsData[] cells)
        {
            if (cells == null) throw new ArgumentNullException(nameof(cells));
            if (string.IsNullOrEmpty(data)) return cells;
            string[] teams = data.Split('|');
            if (teams.Length != 2) throw new FormatException("Les cellules de combat sont mal formées.");
            int[] first = DecodeTeam(teams[0], cells);
            int[] second = DecodeTeam(teams[1], cells);
            var used = new HashSet<int>();
            foreach (int id in first)
                if (!used.Add(id)) throw new FormatException("Une cellule de combat est définie plusieurs fois.");
            foreach (int id in second)
                if (!used.Add(id)) throw new FormatException("Une cellule de combat est définie dans plusieurs équipes.");
            foreach (CellsData cell in cells) if (cell != null) cell.FightCell = 0;
            foreach (int id in first) cells[id].FightCell = 1;
            foreach (int id in second) cells[id].FightCell = 2;
            return cells;
        }

        private static int[] DecodeTeam(string encoded, CellsData[] cells)
        {
            if ((encoded.Length & 1) != 0)
                throw new FormatException("Une équipe contient une cellule incomplète.");
            var ids = new int[encoded.Length / 2];
            for (int offset = 0; offset < encoded.Length; offset += 2)
            {
                int high = hash.IndexOf(encoded[offset]);
                int low = hash.IndexOf(encoded[offset + 1]);
                if (high < 0 || low < 0)
                    throw new FormatException("Une cellule de combat contient un caractère invalide.");
                int id = high * hash.Length + low;
                if (id >= cells.Length || cells[id] == null)
                    throw new FormatException($"La cellule de combat {id} n'existe pas dans cette carte.");
                ids[offset / 2] = id;
            }
            return ids;
        }
    }
}
