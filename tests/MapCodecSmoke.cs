using System;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Text;
using Tool_Editor.maps.data;
using Tool_Editor.maps.managers;

internal static class MapCodecSmoke
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
        var map = new Map { Cells = NewCells(130) };
        map.Cells[0].FightCell = 1;
        map.Cells[63].FightCell = 1;
        map.Cells[64].FightCell = 2;
        map.Cells[129].FightCell = 2;
        map.SaveFightCell();
        Check(map.fightPlaces == "aaa_|bacb", "fight encoding: " + map.fightPlaces);

        CellsData[] decoded = NewCells(130);
        FightCellManager.ParseCellFight(map.fightPlaces, decoded);
        Check(decoded[0].FightCell == 1 && decoded[63].FightCell == 1, "team 1 decoding");
        Check(decoded[64].FightCell == 2 && decoded[129].FightCell == 2, "team 2 decoding");
        bool malformedRejected = false;
        try { FightCellManager.ParseCellFight("a|", decoded); }
        catch (FormatException) { malformedRejected = true; }
        Check(malformedRejected, "malformed fight cells accepted");
        Check(decoded[0].FightCell == 1, "invalid fight data changed existing cells");
        try { FightCellManager.ParseCellFight("aa|!a", decoded); throw new Exception("bad second team accepted"); }
        catch (FormatException) { }
        Check(decoded[0].FightCell == 1, "bad second team changed first team");
        try { FightCellManager.ParseCellFight("aa|aa", decoded); throw new Exception("duplicate team cell accepted"); }
        catch (FormatException) { }

        var walkable = new CellsData { ID = 0 };
        Check(walkable.Type() == (int)MoveEnums.WALKABLE, "default cell type");
        walkable.TriggerCell = true;
        Check(walkable.Type() == (int)MoveEnums.TRIGGER, "trigger cell type");
        walkable.TriggerCell = false;
        string encoded = BuilderClass.GetCellData(walkable);
        var codecMap = new Map { Cells = NewCells(1) };
        codecMap.DecompressCells(encoded, 0);
        Check(!codecMap.Cells[0].UnWalk && codecMap.Cells[0].Type() == (int)MoveEnums.WALKABLE, "walkable cell round-trip");

        codecMap.MapData = "short";
        bool lengthRejected = false;
        try { codecMap.DecompressMap(); }
        catch (FormatException) { lengthRejected = true; }
        Check(lengthRejected, "invalid map-data length accepted");
        Check(DecryptClass.PrepareKey("6B6579") == "key", "hex key decoding");
        Check(DecryptClass.PrepareKey("253246") == "/", "escaped key decoding");
        Check(DecryptClass.CheckSum("key") == "9", "key checksum");
        string plain = string.Concat(Enumerable.Repeat(encoded, 18));
        var cipher = new StringBuilder();
        for (int i = 0; i < plain.Length; i++) cipher.Append(((int)(plain[i] ^ "key"[(i + 18) % 3])).ToString("X2"));
        var encrypted = new Map { Width = 3, Height = 4, Cells = NewCells(18), Key = "6B6579", MapData = cipher.ToString() };
        encrypted.Load();
        Check(encrypted.Key == "" && encrypted.MapData == plain && !encrypted.Cells[0].UnWalk, "encrypted map load");
        encrypted.Load();
        Check(encrypted.MapData == plain, "repeated map load");
        var bad = new Map { Width = 3, Height = 4, Cells = NewCells(18), Key = "F", MapData = cipher.ToString() };
        CellsData[] original = bad.Cells;
        try { bad.Load(); throw new Exception("invalid key accepted"); } catch (FormatException) { }
        Check(ReferenceEquals(original, bad.Cells) && bad.Key == "F" && bad.MapData == cipher.ToString(), "failed decoding destroyed input");
        bad.Key = ""; bad.MapData = plain.Substring(0, plain.Length - 1) + "!";
        try { bad.Load(); throw new Exception("invalid cell character accepted"); } catch (FormatException) { }
        Check(ReferenceEquals(original, bad.Cells), "invalid plain data replaced cells");
        Console.WriteLine("OK: map/fight codecs, encrypted loading and atomic malformed-data guards");
    }

    private static CellsData[] NewCells(int count)
    {
        var result = new CellsData[count];
        for (int i = 0; i < count; i++) result[i] = new CellsData { ID = i };
        return result;
    }

    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception("Failed: " + name);
    }
}
