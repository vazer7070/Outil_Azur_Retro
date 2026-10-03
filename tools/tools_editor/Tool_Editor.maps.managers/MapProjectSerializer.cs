using System;
using System.IO;
using Tool_Editor.maps.data;

namespace Tool_Editor.maps.managers
{
    public static class MapProjectSerializer
    {
        private const string Magic = "AZUR-AME";
        private const int Version = 3;
        private const long MaxFileSize = 32L * 1024L * 1024L;

        public static void Save(string path, Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (map.Cells == null || map.Cells.Length == 0)
                throw new InvalidOperationException("La carte ne contient aucune cellule.");
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Chemin invalide.", nameof(path));
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            string temporary = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(Magic);
                    writer.Write(Version);
                    WriteMap(writer, map);
                    writer.Flush();
                    stream.Flush(true);
                }
                if (File.Exists(fullPath)) File.Replace(temporary, fullPath, null);
                else File.Move(temporary, fullPath);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        public static Map Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Chemin invalide.", nameof(path));
            var file = new FileInfo(path);
            if (!file.Exists) throw new FileNotFoundException("Le projet de carte est introuvable.", path);
            if (file.Length <= 0 || file.Length > MaxFileSize)
                throw new InvalidDataException("La taille du projet de carte est invalide.");
            using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadString() != Magic) throw new InvalidDataException("Ce fichier n'est pas un projet Azur AME.");
                int version = reader.ReadInt32();
                if (version != 1 && version != 2 && version != Version) throw new InvalidDataException($"Version AME non prise en charge : {version}.");
                Map map = ReadMap(reader, version);
                if (stream.Position != stream.Length)
                    throw new InvalidDataException("Le projet AME contient des données supplémentaires inattendues.");
                return map;
            }
        }

        private static void WriteMap(BinaryWriter writer, Map map)
        {
            ValidateDimensionsAndCells(map);
            writer.Write(map.ID);
            writer.Write(map.DateMap ?? string.Empty);
            writer.Write(map.BackGroundID);
            writer.Write(map.Musique);
            writer.Write(map.MusiqueName ?? string.Empty);
            writer.Write(map.Ambiance);
            writer.Write(map.IsOutDoor);
            writer.Write(map.Capabilities);
            writer.Write(map.Width);
            writer.Write(map.Height);
            writer.Write(map.Key ?? string.Empty);
            writer.Write(map.MapData ?? string.Empty);
            writer.Write(map.fightPlaces ?? string.Empty);
            writer.Write(map.NbGroups);
            writer.Write(map.GroupMaxSize);
            writer.Write(map.X);
            writer.Write(map.Y);
            writer.Write(map.Area);
            writer.Write(map.SubArea);
            writer.Write(map.SuperArea);
            writer.Write(map.NextRoom);
            writer.Write(map.NextCell);
            writer.Write(map.Mobs ?? string.Empty);
            writer.Write(map.GroupFixe_Mobs ?? string.Empty);
            writer.Write(map.Groupefixe_Cell);
            writer.Write(map.Cells.Length);
            foreach (CellsData cell in map.Cells)
            {
                writer.Write(cell != null);
                if (cell == null) continue;
                writer.Write(cell.ID);
                writer.Write(cell.GFX1?.ID ?? -1);
                writer.Write(cell.GFX2?.ID ?? -1);
                writer.Write(cell.GFX3?.ID ?? -1);
                writer.Write(CellFlags(cell));
                writer.Write(cell.FightCell);
                writer.Write(cell.RotaGFX1);
                writer.Write(cell.RotaGFX2);
                writer.Write(cell.IncliSol);
                writer.Write(cell.NivSol);
                writer.Write(cell.TriggerName ?? string.Empty);
            }
        }

        private static Map ReadMap(BinaryReader reader, int version)
        {
            var map = new Map
            {
                ID = reader.ReadInt32(),
                DateMap = reader.ReadString(),
                BackGroundID = reader.ReadInt32(),
                Musique = reader.ReadInt32(),
                MusiqueName = reader.ReadString(),
                Ambiance = reader.ReadInt32(),
                IsOutDoor = reader.ReadBoolean(),
                Capabilities = reader.ReadInt32(),
                Width = reader.ReadInt32(),
                Height = reader.ReadInt32(),
                Key = reader.ReadString(),
                MapData = reader.ReadString(),
                fightPlaces = reader.ReadString(),
                NbGroups = reader.ReadInt32(),
                GroupMaxSize = reader.ReadInt32(),
                X = reader.ReadInt32(),
                Y = reader.ReadInt32(),
                Area = reader.ReadInt32(),
                SubArea = reader.ReadInt32(),
                SuperArea = reader.ReadInt32(),
                NextRoom = reader.ReadInt32(),
                NextCell = reader.ReadInt32(),
                Mobs = reader.ReadString(),
                GroupFixe_Mobs = reader.ReadString(),
                Groupefixe_Cell = reader.ReadInt32()
            };
            if (map.Width < 2 || map.Width > 100 || map.Height < 2 || map.Height > 100)
                throw new InvalidDataException("Les dimensions du projet AME sont invalides.");
            int count = reader.ReadInt32();
            int expected = version < 3 ? ExpectedCellCount(map.Width, map.Height) - 1 : ExpectedCellCount(map.Width, map.Height);
            if (count != expected)
                throw new InvalidDataException("Le nombre de cellules du projet AME est invalide.");
            map.Cells = new CellsData[ExpectedCellCount(map.Width, map.Height)];
            for (int index = 0; index < count; index++)
            {
                if (!reader.ReadBoolean()) continue;
                var cell = new CellsData
                {
                    ID = reader.ReadInt32()
                };
                int ground = reader.ReadInt32();
                int object1 = reader.ReadInt32();
                int object2 = reader.ReadInt32();
                if (ground >= 0) cell.GFX1 = TilesData.GetGrounds(ground);
                if (object1 >= 0) cell.GFX2 = TilesData.GetObjects(object1);
                if (object2 >= 0) cell.GFX3 = TilesData.GetObjects(object2);
                if (ground >= 0 && cell.GFX1 == null || object1 >= 0 && cell.GFX2 == null || object2 >= 0 && cell.GFX3 == null)
                    throw new InvalidDataException($"Une tuile utilisée par la cellule {index} manque dans les ressources.");
                ApplyFlags(cell, reader.ReadInt32());
                if (version == 1)
                {
                    cell.Movement = (int)MoveEnums.WALKABLE;
                    cell.Active = true;
                    if (map.MapData.Length == count * 10)
                    {
                        int code = (int)DecryptClass.HashCode(map.MapData[index * 10].ToString());
                        if (code >= 0) cell.Active = (code & 32) != 0;
                    }
                }
                cell.FightCell = reader.ReadInt32();
                cell.RotaGFX1 = reader.ReadInt32();
                cell.RotaGFX2 = reader.ReadInt32();
                cell.IncliSol = reader.ReadInt32();
                cell.NivSol = reader.ReadInt32();
                cell.TriggerName = reader.ReadString();
                if (cell.ID != index)
                    throw new InvalidDataException("L'ordre des cellules du projet AME est invalide.");
                map.Cells[index] = cell;
            }
            // Versions 1 and 2 used a grid one cell short. Keep every saved
            // cell and add only the missing last cell when opening the project.
            if (version < 3) map.Cells[count] = new CellsData { ID = count };
            map.Background = TilesData.GetBackgrounds(map.BackGroundID);
            map.HasProjectCells = true;
            return map;
        }

        private static int CellFlags(CellsData cell)
        {
            int flags = 0;
            if (cell.UnWalk) flags |= 1 << 0;
            if (cell.Path) flags |= 1 << 1;
            if (cell.Los) flags |= 1 << 2;
            if (cell.Paddock) flags |= 1 << 3;
            if (cell.TriggerCell) flags |= 1 << 4;
            if (cell.Door) flags |= 1 << 5;
            if (cell.IO) flags |= 1 << 6;
            if (cell.FlipGFX1) flags |= 1 << 7;
            if (cell.FlipGFX2) flags |= 1 << 8;
            if (cell.FlipGFX3) flags |= 1 << 9;
            if (cell.Trigger) flags |= 1 << 10;
            if (cell.Active) flags |= 1 << 11;
            if (cell.Movement < 0 || cell.Movement > 7) throw new InvalidDataException("Type de déplacement de cellule invalide.");
            flags |= cell.Movement << 12;
            return flags;
        }

        private static void ApplyFlags(CellsData cell, int flags)
        {
            cell.UnWalk = (flags & 1 << 0) != 0;
            cell.Path = (flags & 1 << 1) != 0;
            cell.Los = (flags & 1 << 2) != 0;
            cell.Paddock = (flags & 1 << 3) != 0;
            cell.TriggerCell = (flags & 1 << 4) != 0;
            cell.Door = (flags & 1 << 5) != 0;
            cell.IO = (flags & 1 << 6) != 0;
            cell.FlipGFX1 = (flags & 1 << 7) != 0;
            cell.FlipGFX2 = (flags & 1 << 8) != 0;
            cell.FlipGFX3 = (flags & 1 << 9) != 0;
            cell.Trigger = (flags & 1 << 10) != 0;
            cell.Active = (flags & 1 << 11) != 0;
            cell.Movement = (flags >> 12) & 7;
        }

        private static void ValidateDimensionsAndCells(Map map)
        {
            if (map.Width < 2 || map.Width > 100 || map.Height < 2 || map.Height > 100)
                throw new InvalidOperationException("Les dimensions de la carte sont invalides.");
            int expected = ExpectedCellCount(map.Width, map.Height);
            if (map.Cells.Length != expected)
                throw new InvalidOperationException($"La carte doit contenir exactement {expected} cellules.");
            for (int index = 0; index < map.Cells.Length; index++)
                if (map.Cells[index] != null && map.Cells[index].ID != index)
                    throw new InvalidOperationException($"La cellule à la position {index} porte un identifiant invalide.");
        }

        private static int ExpectedCellCount(int width, int height)
        {
            return Map.CellCount(width, height);
        }
    }
}
