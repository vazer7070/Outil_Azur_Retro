using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using SwfDotNet.IO;
using SwfDotNet.IO.ByteCode;
using SwfDotNet.IO.Tags;
using Tool_Editor.maps.data;
using Tool_Editor.maps.managers;

internal static class MapSwfSmoke
{
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) => {
            if (new AssemblyName(args.Name).Name == "log4net")
                return Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "log4net.dll"));
            string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(args.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        Run();
    }
    private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static void Rejected(string path, byte[] bytes)
    {
        File.WriteAllBytes(path, bytes);
        bool rejected = false;
        try { MapSwfSerializer.Load(path); }
        catch (InvalidDataException) { rejected = true; }
        catch (EndOfStreamException) { rejected = true; }
        Check(rejected, "Malformed SWF accepted");
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }
    private static void Run()
    {
        string path = Path.Combine(TestPaths.Work, "swf-roundtrip.swf");
        string invalid = Path.Combine(TestPaths.Work, "swf-malformed.swf");
        string ame = Path.Combine(TestPaths.Work, "swf-cell-flags.ame");
        byte[] fixture = Movie(Assignments(false));
        File.WriteAllBytes(path, fixture);
        Map imported = MapSwfSerializer.Load(path);
        Check(imported.ID == 12345 && imported.Width == 3 && imported.Height == 4 && imported.BackGroundID == 7 &&
            imported.IsOutDoor && imported.Musique == 12 && imported.Ambiance == 3 && imported.Capabilities == 9, "Named metadata/constants decoding");
        Check(imported.MapData == string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)), "Literal map data decoding");
        imported.Load();
        Check(imported.Cells[0].Active && imported.Cells[0].Type() == 4, "Known active/walkable fixture");
        Task.WaitAll(Enumerable.Range(0, 16).Select(i => Task.Run(() => Check(MapSwfSerializer.Load(path).ID == 12345, "Concurrent import"))).ToArray());
        File.WriteAllBytes(path, Compressed(fixture));
        Check(MapSwfSerializer.Load(path).MapData == imported.MapData, "CWS import");
        File.WriteAllBytes(path, Compressed(fixture, true));
        Check(MapSwfSerializer.Load(path).MapData == imported.MapData, "Compressed DEFLATE import");
        byte[] compressed = Compressed(fixture);
        compressed[compressed.Length - 1] ^= 1;
        Rejected(invalid, compressed);
        compressed = Compressed(fixture);
        Array.Copy(BitConverter.GetBytes((uint)(fixture.Length - 1)), 0, compressed, 4, 4);
        Rejected(invalid, compressed);
        Rejected(invalid, fixture.Take(fixture.Length - 1).ToArray());
        Rejected(invalid, Movie(Assignments(true)));
        Rejected(invalid, Movie(new byte[] { 0x96, 5, 0, 0, (byte)'x', 0, 0x1d, 0 }));
        Rejected(invalid, Movie(new byte[] { 0x88, 4, 0, 1, 0, (byte)'x', (byte)'y', 0 }));
        Rejected(invalid, Movie(new byte[] { 0x99, 2, 0, 0, 0, 0 })); // unsupported control flow
        Rejected(invalid, Movie(new byte[] { 0x96, 2, 0, 8, 7, 0 })); // absent constant
        Rejected(invalid, Movie(new byte[] { 0x96, 3, 0, 0, 0xff, 0, 0 })); // invalid UTF-8
        Rejected(invalid, Movie(new byte[] { 0x96, 5, 0, 7, 0, 0, 0, 0 }.Concat(Enumerable.Repeat((byte)0x4c, 4097)).Concat(new byte[] { 0 }).ToArray()));

        string nowelFixture = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "..", "Fixtures", "10000_0612041200.swf");
        Map nowel = MapSwfSerializer.Load(nowelFixture);
        Check(nowel.ID == 10000 && nowel.Width == 15 && nowel.Height == 17 && nowel.BackGroundID == 341 &&
            nowel.Musique == 128 && nowel.IsOutDoor && nowel.Capabilities == 98 && nowel.DateMap == "0612041200" &&
            nowel.MapData.Length == 4790 && Map.CellCount(15, 17) == 479, "Nowel CWS metadata and complete grid");
        RegisterFixtureTiles(nowel.MapData);
        string nowelData = nowel.MapData;
        nowel.Load();
        Check(nowel.Cells.Length == 479 && string.Concat(nowel.Cells.Select(BuilderClass.GetCellData)) == nowelData,
            "Nowel cell decoding changes the official map data");
        string nowelExport = Path.Combine(TestPaths.Work, "nowel-roundtrip.swf");
        MapSwfSerializer.Save(nowelExport, nowel);
        Map exportedNowel = MapSwfSerializer.Load(nowelExport);
        Check(exportedNowel.MapData == nowelData && exportedNowel.Capabilities == 98 &&
            exportedNowel.BackGroundID == 341 && exportedNowel.ID == 10000,
            "Nowel export changes the map or its upper capability bits");
        File.Delete(nowelExport);

        var map = new Map { ID = 98765, Width = 3, Height = 4, BackGroundID = 7, Ambiance = 3, Musique = 12, IsOutDoor = true, Capabilities = 9, Cells = new CellsData[18] };
        TilesData.ListGrounds[2047] = new TilesData(2047, "ground.png", "test", TilesData.TileType.ground);
        TilesData.ListObject[16383] = new TilesData(16383, "object.png", "test", TilesData.TileType.objet);
        for (int i = 0; i < map.Cells.Length; i++)
        {
            var cell = new CellsData { ID = i, Active = i % 2 == 0, Los = true, GFX1 = TilesData.ListGrounds[2047],
                GFX2 = TilesData.ListObject[16383], GFX3 = TilesData.ListObject[16383], RotaGFX1 = 3, RotaGFX2 = 3,
                NivSol = 15, IncliSol = 15, FlipGFX1 = true, FlipGFX2 = true, FlipGFX3 = true, IO = true };
            cell.Type(i % 8);
            map.Cells[i] = cell;
        }
        MapSwfSerializer.Save(path, map);
        Check(File.ReadAllBytes(path).Take(3).SequenceEqual(Encoding.ASCII.GetBytes("FWS")), "Real SWF signature");
        VerifyIndependentReader(path);
        Map loaded = MapSwfSerializer.Load(path);
        loaded.Load();
        for (int i = 0; i < map.Cells.Length; i++)
            Check(BuilderClass.GetCellData(map.Cells[i]) == BuilderClass.GetCellData(loaded.Cells[i]) && loaded.Cells[i].Type() == i % 8, "Cell data round-trip: " + i);
        var bot = new Tool_BotProtocol.Game.Maps.Map { MapWidth = 3, MapHeight = 4 };
        bot.DecompressMap(loaded.MapData);
        Check(bot.MapCells[0].IsActive && !bot.MapCells[1].IsActive && bot.MapCells[4].IsWalkable(), "Bot reads active and movement flags");
        MapProjectSerializer.Save(ame, loaded);
        Map project = MapProjectSerializer.Load(ame);
        Check(!project.Cells[1].Active && project.Cells[3].Type() == 3 && project.Cells[6].Type() == 6, "AME v2 preserves SWF flags");
        byte[] before = File.ReadAllBytes(path);
        map.Cells[0].GFX1 = new TilesData(2048, "invalid.png", "test", TilesData.TileType.ground);
        try { MapSwfSerializer.Save(path, map); throw new Exception("Out-of-range tile accepted"); }
        catch (InvalidOperationException) { }
        Check(before.SequenceEqual(File.ReadAllBytes(path)), "Invalid export overwrote existing file");
        TilesData.ListGrounds[2047] = null;
        Map missing = MapSwfSerializer.Load(path);
        CellsData[] previous = missing.Cells;
        try { missing.Load(); throw new Exception("Missing tile silently discarded"); }
        catch (FormatException) { }
        Check(ReferenceEquals(previous, missing.Cells), "Missing tile destroyed map state");

        var large = new Map { ID = 100, Width = 100, Height = 100, Cells = new CellsData[19801] };
        for (int i = 0; i < large.Cells.Length; i++) large.Cells[i] = new CellsData { ID = i };
        MapSwfSerializer.Save(path, large);
        loaded = MapSwfSerializer.Load(path);
        loaded.Load();
        Check(loaded.Cells.Length == 19801 && loaded.MapData.Length == 198010 && loaded.Cells.Last().Active, "Large map action chunks");
        File.WriteAllBytes(path, Compressed(File.ReadAllBytes(path), true));
        Check(MapSwfSerializer.Load(path).MapData.Length == 198010, "Large compressed map import");
        File.Delete(path); File.Delete(invalid); File.Delete(ame);
        Console.WriteLine("OK: official Nowel CWS import and exact cell export, AVM1/FWS/CWS, independent SWF reader, bot/AME flags and corruption guards");
    }

    private static void RegisterFixtureTiles(string data)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";
        for (int offset = 0; offset < data.Length; offset += 10)
        {
            int[] cell = data.Substring(offset, 10).Select(character => alphabet.IndexOf(character)).ToArray();
            int ground = ((cell[0] & 24) << 6) + ((cell[2] & 7) << 6) + cell[3];
            int object1 = ((cell[0] & 4) << 11) + ((cell[4] & 1) << 12) + (cell[5] << 6) + cell[6];
            int object2 = ((cell[0] & 2) << 12) + ((cell[7] & 1) << 12) + (cell[8] << 6) + cell[9];
            if (ground != 0 && TilesData.ListGrounds[ground] == null)
                TilesData.ListGrounds[ground] = new TilesData(ground, "", "", TilesData.TileType.ground);
            foreach (int id in new[] { object1, object2 })
                if (id != 0 && TilesData.ListObject[id] == null)
                    TilesData.ListObject[id] = new TilesData(id, "", "", TilesData.TileType.objet);
        }
    }
    private static void VerifyIndependentReader(string path)
    {
        Swf swf = new SwfDotNet.IO.SwfReader(path).ReadSwf();
        Check(swf.Version == 6, "Independent SWF header reader");
        var text = new StringBuilder();
        foreach (BaseTag tag in swf.Tags)
            foreach (byte[] actions in tag)
                foreach (object action in new Decompiler(swf.Version).Decompile(actions)) text.AppendLine(action.ToString());
        Check(text.ToString().Contains("mapData") && text.ToString().Contains("setVariable") && text.ToString().Contains("98765"), "Independent AVM1 decompiler");
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }
    // Independent fixture assembler following Adobe's AVM1 records and Astria's field names.
    private static byte[] Assignments(bool invalidData)
    {
        var poolValues = new System.Collections.Generic.List<string> { "height", "width", "mapData", "id", "bOutdoor", "capabilities", "backgroundNum", "musicId", "ambianceId", "unused 'quote'", "_parent", "_url", "System", "security", "allowDomain" };
        while (poolValues.Count < 269) poolValues.Add("padding-" + poolValues.Count);
        poolValues.Add(string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)));
        string[] constants = poolValues.ToArray();
        using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
        {
            using (var pool = new MemoryStream()) using (var p = new BinaryWriter(pool))
            {
                p.Write((ushort)constants.Length);
                foreach (string text in constants) { p.Write(Encoding.UTF8.GetBytes(text)); p.Write((byte)0); }
                writer.Write((byte)0x88); writer.Write((ushort)pool.Length); writer.Write(pool.ToArray());
            }
            writer.Write(new byte[] { 0x96, 2, 0, 8, 10, 0x1c, 0x96, 2, 0, 8, 11, 0x4e });
            writer.Write(new byte[] { 0x96, 7, 0, 7, 1, 0, 0, 0, 8, 12, 0x1c });
            writer.Write(new byte[] { 0x96, 2, 0, 8, 13, 0x4e, 0x96, 2, 0, 8, 14, 0x52, 0x17 });
            int[] keys = { 7, 3, 0, 5, 1, 8, 6 };
            int[] numbers = { 12, 12345, 4, 9, 3, 3, 7 };
            for (int i = 0; i < keys.Length; i++)
            {
                writer.Write(new byte[] { 0x96, 2, 0, 8, (byte)keys[i] });
                writer.Write(new byte[] { 0x96, 5, 0, (byte)(keys[i] == 0 ? 1 : 7) });
                if (keys[i] == 0) writer.Write((float)numbers[i]); else writer.Write(numbers[i]);
                writer.Write((byte)0x1d);
            }
            writer.Write(new byte[] { 0x96, 2, 0, 8, 4, 0x96, 6, 0, 0, (byte)'T', (byte)'r', (byte)'u', (byte)'e', 0, 0x1d });
            writer.Write(new byte[] { 0x96, 2, 0, 8, 2 });
            if (invalidData) writer.Write(new byte[] { 0x96, 5, 0, 7, 0, 0, 0, 0 });
            else writer.Write(new byte[] { 0x96, 3, 0, 9, 13, 1 });
            writer.Write((byte)0x1d); writer.Write((byte)0);
            return stream.ToArray();
        }
    }
    private static byte[] Movie(byte[] actions)
    {
        using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
        {
            writer.Write(new byte[] { 70, 87, 83, 6 }); writer.Write((uint)0);
            writer.Write(new byte[] { 8, 0, 0, 12, 1, 0 });
            writer.Write((ushort)((12 << 6) | 63)); writer.Write((uint)actions.Length); writer.Write(actions);
            writer.Write((ushort)64); writer.Write((ushort)0);
            stream.Position = 4; writer.Write((uint)stream.Length);
            return stream.ToArray();
        }
    }
    private static byte[] Compressed(byte[] movie, bool deflate = false)
    {
        // Build a zlib stream with an uncompressed DEFLATE block, independently of SharpZipLib.
        byte[] data = movie.Skip(8).ToArray();
        using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
        {
            writer.Write(movie.Take(8).ToArray()); stream.Position = 0; writer.Write((byte)'C'); stream.Position = 8;
            writer.Write(new byte[] { 0x78, 0x01 });
            if (deflate)
            {
                using (var compressor = new System.IO.Compression.DeflateStream(stream, System.IO.Compression.CompressionMode.Compress, true))
                    compressor.Write(data, 0, data.Length);
            }
            else
            {
                writer.Write((byte)0x01);
                writer.Write((ushort)data.Length); writer.Write((ushort)~data.Length); writer.Write(data);
            }
            uint a = 1, b = 0;
            foreach (byte value in data) { a = (a + value) % 65521; b = (b + a) % 65521; }
            uint checksum = (b << 16) | a;
            writer.Write(new byte[] { (byte)(checksum >> 24), (byte)(checksum >> 16), (byte)(checksum >> 8), (byte)checksum });
            return stream.ToArray();
        }
    }
}
