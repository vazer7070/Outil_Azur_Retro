using System;
using System.IO;
using System.Reflection;
using Tool_Editor.maps.data;
using Tool_Editor.maps.managers;

internal static class MapProjectSmoke
{
    private static void Main()
    {
        string bin = TestPaths.ApplicationBin;
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string candidate = Path.Combine(bin, new AssemblyName(args.Name).Name + ".dll");
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        };
        Run();
    }

    private static void Run()
    {
        string work = TestPaths.Work;
        string path = Path.Combine(work, "map-project-roundtrip.ame");
        string invalid = Path.Combine(work, "map-project-invalid.ame");
        try
        {
            string fixture = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "..", "Fixtures", "azur-v1.ame");
            Map oldProject = MapProjectSerializer.Load(fixture);
            Check(oldProject.ID == 777 && oldProject.Cells.Length == 18 && oldProject.Cells[17].ID == 17 && oldProject.Cells[0].Active && oldProject.Cells[0].Type() == 4, "AME v1 backward compatibility");
            string v2Fixture = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "..", "Fixtures", "azur-v2.ame");
            Map previousProject = MapProjectSerializer.Load(v2Fixture);
            Check(previousProject.ID == 778 && previousProject.Cells.Length == 18 && previousProject.Cells[17].ID == 17 &&
                !previousProject.Cells[5].Active && previousProject.Cells[5].Type() == 6,
                "AME v2 backward compatibility and added final cell");
            TilesData.ListGrounds[42] = new TilesData(42, "ground.png", "test", TilesData.TileType.ground);
            TilesData.ListObject[84] = new TilesData(84, "object.png", "test", TilesData.TileType.objet);
            TilesData.Backgrounds_Tiles[7] = new TilesData(7, "background.png", "test", TilesData.TileType.background);
            var map = new Map
            {
                ID = 12345, DateMap = "AZ-test", BackGroundID = 7, Musique = 12,
                MusiqueName = "theme", Ambiance = 3, IsOutDoor = true, Capabilities = 9,
                Width = 3, Height = 4, Key = "key", MapData = "raw", fightPlaces = "fight",
                NbGroups = 6, GroupMaxSize = 7, X = -4, Y = 8, Area = 1, SubArea = 2,
                SuperArea = 3, NextRoom = 456, NextCell = 9, Mobs = "10,20",
                GroupFixe_Mobs = "30,40", Groupefixe_Cell = 11,
                Cells = new CellsData[18], HasProjectCells = true
            };
            for (int i = 0; i < map.Cells.Length; i++) map.Cells[i] = new CellsData { ID = i };
            map.Cells[5].GFX1 = TilesData.ListGrounds[42];
            map.Cells[5].GFX2 = TilesData.ListObject[84];
            map.Cells[5].UnWalk = true;
            map.Cells[5].Los = true;
            map.Cells[5].Door = true;
            map.Cells[5].FlipGFX2 = true;
            map.Cells[5].FightCell = 2;
            map.Cells[5].RotaGFX1 = 3;
            map.Cells[5].RotaGFX2 = 1;
            map.Cells[5].IncliSol = 4;
            map.Cells[5].NivSol = 6;
            map.Cells[5].Trigger = true;
            map.Cells[5].TriggerName = "sortie";

            MapProjectSerializer.Save(path, map);
            Map loaded = MapProjectSerializer.Load(path);
            Check(loaded.ID == map.ID && loaded.Width == 3 && loaded.Height == 4, "metadata");
            Check(loaded.Cells.Length == 18 && loaded.Cells[5].ID == 5, "cells");
            Check(loaded.Cells[5].GFX1 != null && loaded.Cells[5].GFX1.ID == 42, "ground");
            Check(loaded.Cells[5].GFX2 != null && loaded.Cells[5].GFX2.ID == 84, "object");
            Check(loaded.Cells[5].UnWalk && loaded.Cells[5].Los && loaded.Cells[5].Door, "flags");
            Check(loaded.Cells[5].FlipGFX2 && loaded.Cells[5].Trigger, "more flags");
            Check(loaded.Cells[5].FightCell == 2 && loaded.Cells[5].TriggerName == "sortie", "cell metadata");
            Check(loaded.Background != null && loaded.Background.ID == 7 && loaded.HasProjectCells, "background/project marker");

            File.WriteAllBytes(invalid, new byte[] { 1, 2, 3, 4 });
            bool rejected = false;
            try { MapProjectSerializer.Load(invalid); }
            catch (Exception) { rejected = true; }
            Check(rejected, "invalid format accepted");
            Console.WriteLine("OK: AME map project round-trip and corruption guard");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(invalid)) File.Delete(invalid);
        }
    }

    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception("Failed: " + name);
    }
}
