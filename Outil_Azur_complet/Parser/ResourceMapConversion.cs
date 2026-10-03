using System;
using System.Linq;
using Tool_Editor.maps.data;
using Tool_Editor.maps.managers;

namespace Outil_Azur_complet.Parser
{
    public static class ResourceMapConversion
    {
        public static string Decrypt(string data, string key, int width, int height)
        {
            ValidateDimensions(width, height);
            string prepared = DecryptClass.PrepareKey((key ?? "").Trim());
            string clear = DecryptClass.DecypherData((data ?? "").Trim(), prepared,
                Convert.ToInt32(DecryptClass.CheckSum(prepared), 16) * 2);
            ValidateData(clear, width, height);
            return clear;
        }

        // Work on a separate map so a failed conversion never changes the import.
        public static Map Prepare(Map original, int id, int width, int height, string data, string key)
        {
            ValidateDimensions(width, height);
            if (id <= 0) throw new FormatException("L'identifiant de carte doit être positif.");
            data = (data ?? "").Trim(); key = (key ?? "").Trim();
            if (key.Length != 0) data = Decrypt(data, key, width, height);
            ValidateData(data, width, height);
            var map = new Map { ID = id, Width = width, Height = height, MapData = data, Key = "" };
            if (original != null)
            {
                map.DateMap = original.DateMap; map.BackGroundID = original.BackGroundID;
                map.Musique = original.Musique; map.MusiqueName = original.MusiqueName;
                map.Ambiance = original.Ambiance; map.IsOutDoor = original.IsOutDoor;
                map.Capabilities = original.Capabilities; map.NbGroups = original.NbGroups;
                map.GroupMaxSize = original.GroupMaxSize; map.X = original.X; map.Y = original.Y;
                map.Area = original.Area; map.SubArea = original.SubArea; map.SuperArea = original.SuperArea;
                map.NextRoom = original.NextRoom; map.NextCell = original.NextCell;
                map.Mobs = original.Mobs; map.GroupFixe_Mobs = original.GroupFixe_Mobs;
                map.Groupefixe_Cell = original.Groupefixe_Cell;
                if (width != original.Width || height != original.Height)
                    throw new FormatException("Pour redimensionner une carte importée, utilisez l'éditeur de cartes. Le convertisseur conserve ses dimensions et ses placements.");
                map.fightPlaces = original.HasProjectCells ? "" : original.fightPlaces;
            }
            map.Load();
            if (original != null && original.HasProjectCells && original.Cells != null && original.Cells.Length == map.Cells.Length)
                for (int index = 0; index < map.Cells.Length; index++)
                {
                    var source = original.Cells[index]; if (source == null) continue;
                    map.Cells[index].FightCell = source.FightCell;
                    map.Cells[index].Trigger = source.Trigger;
                    map.Cells[index].TriggerName = source.TriggerName;
                }
            map.HasProjectCells = true;
            if (original != null && original.HasProjectCells) map.fightPlaces = original.fightPlaces;
            return map;
        }

        private static void ValidateDimensions(int width, int height)
        {
            if (width < 2 || width > 100 || height < 2 || height > 100)
                throw new FormatException("Les dimensions doivent être comprises entre 2 et 100.");
        }

        private static void ValidateData(string data, int width, int height)
        {
            int expected = checked(Map.CellCount(width, height) * 10);
            if (data.Length != expected || data.Any(c => (int)DecryptClass.HashCode(c.ToString()) < 0))
                throw new FormatException("Les données doivent contenir " + expected + " caractères valides (" + Map.CellCount(width, height) + " cellules). Vérifiez la clef et les dimensions.");
        }
    }
}
