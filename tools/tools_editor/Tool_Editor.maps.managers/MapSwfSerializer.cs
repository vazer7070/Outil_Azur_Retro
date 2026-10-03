using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Tool_Editor.maps.data;

namespace Tool_Editor.maps.managers
{
    // Reads literal AVM1 assignments; never executes code from the imported movie.
    public static class MapSwfSerializer
    {
        private const int MaxMapData = 396000;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly object Unknown = new object();

        public static Map Load(string path)
        {
            byte[] bytes = Tool_Editor.swf.SwfMovieFile.Read(path);
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                stream.Position = 8;
                int bits = reader.ReadByte() >> 3;
                if (bits == 0) throw new InvalidDataException("Le rectangle SWF est invalide.");
                int rectangleBytes = (5 + bits * 4 + 7) / 8;
                Require(reader, rectangleBytes - 1 + 4, stream.Length);
                stream.Position += rectangleBytes - 1;
                reader.ReadUInt16(); // frame rate
                int frames = reader.ReadUInt16();
                if (frames == 0) throw new InvalidDataException("Le SWF ne contient aucune image.");
                int frame = 0;
                bool ended = false;
                while (stream.Position < stream.Length)
                {
                    Require(reader, 2, stream.Length);
                    ushort header = reader.ReadUInt16();
                    int code = header >> 6;
                    uint length = (uint)(header & 63);
                    if (length == 63) { Require(reader, 4, stream.Length); length = reader.ReadUInt32(); }
                    Require(reader, length, stream.Length);
                    long end = stream.Position + length;
                    if (code == 0)
                    {
                        if (length != 0 || end != stream.Length)
                            throw new InvalidDataException("La balise de fin SWF est invalide.");
                        ended = true;
                        break;
                    }
                    if (code == 1)
                    {
                        if (length != 0) throw new InvalidDataException("La balise ShowFrame est invalide.");
                        frame++;
                    }
                    else if (code == 12)
                    {
                        if (frame != 0)
                            throw new InvalidDataException("Les cartes animées ne sont pas prises en charge.");
                        ReadActions(reader, end, values);
                    }
                    else if (code == 82)
                        throw new InvalidDataException("Cette carte utilise AVM2 ; seuls les SWF AVM1 sont pris en charge.");
                    stream.Position = end;
                }
                if (!ended) throw new EndOfStreamException("La balise de fin SWF est absente.");
                if (frame != frames) throw new InvalidDataException("Le nombre d'images SWF est incohérent.");
            }
            var map = new Map
            {
                ID = Integer(values, "id"), Width = Integer(values, "width"), Height = Integer(values, "height"),
                BackGroundID = Integer(values, "backgroundNum"), Ambiance = Integer(values, "ambianceId"),
                Musique = Integer(values, "musicId"), Capabilities = Integer(values, "capabilities")
            };
            object outdoor = Value(values, "bOutdoor");
            if (outdoor is bool) map.IsOutDoor = (bool)outdoor;
            else if (outdoor is string && bool.TryParse((string)outdoor, out bool boolean)) map.IsOutDoor = boolean;
            else if ((outdoor is int || outdoor is string) && int.TryParse(Convert.ToString(outdoor, CultureInfo.InvariantCulture), out int number) && (number == 0 || number == 1))
                map.IsOutDoor = number == 1;
            else throw new InvalidDataException("La propriété bOutdoor est invalide.");
            map.MapData = Value(values, "mapData") as string;
            ValidateMetadata(map);
            ValidateData(map);
            map.Background = TilesData.GetBackgrounds(map.BackGroundID);
            string name = Path.GetFileNameWithoutExtension(path);
            int underscore = name.IndexOf('_');
            if (underscore >= 0 && underscore + 1 < name.Length) map.DateMap = name.Substring(underscore + 1);
            return map;
        }

        public static void Save(string path, Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            ValidateMetadata(map);
            int count = Map.CellCount(map.Width, map.Height);
            if (map.Cells == null || map.Cells.Length != count)
                throw new InvalidOperationException($"La carte doit contenir exactement {count} cellules.");
            var data = new StringBuilder(count * 10);
            for (int index = 0; index < count; index++)
            {
                CellsData cell = map.Cells[index];
                if (cell == null || cell.ID != index)
                    throw new InvalidOperationException($"La cellule {index} est absente ou porte un identifiant invalide.");
                data.Append(BuilderClass.GetCellData(cell));
            }
            byte[] movie;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new byte[] { (byte)'F', (byte)'W', (byte)'S', 6 });
                writer.Write((uint)0);
                writer.Write(new byte[] { 8, 0 }); // RECT: four zero coordinates, one bit each
                writer.Write((ushort)0x0c00);
                writer.Write((ushort)1);
                using (var actions = new MemoryStream())
                using (var actionWriter = new BinaryWriter(actions))
                {
                    // Preserve Astria's domain grant for a map loaded from the parent's URL.
                    Push(actionWriter, "_parent"); actionWriter.Write((byte)0x1c);
                    Push(actionWriter, "_url"); actionWriter.Write((byte)0x4e);
                    Push(actionWriter, 1); Push(actionWriter, "System"); actionWriter.Write((byte)0x1c);
                    Push(actionWriter, "security"); actionWriter.Write((byte)0x4e);
                    Push(actionWriter, "allowDomain"); actionWriter.Write((byte)0x52); actionWriter.Write((byte)0x17);
                    Assign(actionWriter, "id", map.ID); Assign(actionWriter, "width", map.Width);
                    Assign(actionWriter, "height", map.Height); Assign(actionWriter, "backgroundNum", map.BackGroundID);
                    Assign(actionWriter, "ambianceId", map.Ambiance); Assign(actionWriter, "musicId", map.Musique);
                    Assign(actionWriter, "bOutdoor", map.IsOutDoor); Assign(actionWriter, "capabilities", map.Capabilities);
                    Push(actionWriter, "mapData");
                    string text = data.ToString();
                    for (int offset = 0; offset < text.Length; offset += 60000)
                    {
                        Push(actionWriter, text.Substring(offset, Math.Min(60000, text.Length - offset)));
                        if (offset != 0) actionWriter.Write((byte)0x21); // StringAdd: avoid UI16 action length overflow
                    }
                    actionWriter.Write((byte)0x1d);
                    actionWriter.Write((byte)0x07); // Stop
                    actionWriter.Write((byte)0);
                    WriteTag(writer, 12, actions.ToArray());
                }
                WriteTag(writer, 1, new byte[0]);
                WriteTag(writer, 0, new byte[0]);
                stream.Position = 4;
                writer.Write((uint)stream.Length);
                movie = stream.ToArray();
            }
            Tool_Editor.swf.SwfMovieFile.WriteAtomic(path, movie);
        }

        private sealed class Reference
        {
            public readonly string Path;
            public Reference(string path) { Path = path; }
        }

        private static void ReadActions(BinaryReader reader, long end, Dictionary<string, object> values)
        {
            var stack = new Stack<object>();
            var registers = new Dictionary<byte, object>();
            string[] constants = new string[0];
            bool ended = false;
            while (reader.BaseStream.Position < end)
            {
                int code = reader.ReadByte();
                if (code == 0)
                {
                    if (reader.BaseStream.Position != end) throw new InvalidDataException("Données après la fin des actions SWF.");
                    ended = true; break;
                }
                int length = 0;
                if (code >= 0x80) { Require(reader, 2, end); length = reader.ReadUInt16(); }
                Require(reader, length, end);
                long actionEnd = reader.BaseStream.Position + length;
                switch (code)
                {
                    case 0x88:
                        Require(reader, 2, actionEnd);
                        constants = new string[reader.ReadUInt16()];
                        for (int i = 0; i < constants.Length; i++) constants[i] = CString(reader, actionEnd);
                        break;
                    case 0x96:
                        while (reader.BaseStream.Position < actionEnd)
                        {
                            int type = reader.ReadByte();
                            switch (type)
                            {
                                case 0: stack.Push(CString(reader, actionEnd)); break;
                                case 1: Require(reader, 4, actionEnd); stack.Push(reader.ReadSingle()); break;
                                case 2: case 3: stack.Push(Unknown); break;
                                case 4:
                                    Require(reader, 1, actionEnd); byte register = reader.ReadByte();
                                    stack.Push(registers.TryGetValue(register, out object value) ? value : Unknown); break;
                                case 5:
                                    Require(reader, 1, actionEnd); byte boolean = reader.ReadByte();
                                    if (boolean > 1) throw new InvalidDataException("Booléen SWF invalide.");
                                    stack.Push(boolean != 0); break;
                                case 6:
                                    Require(reader, 8, actionEnd); byte[] number = reader.ReadBytes(8);
                                    Array.Reverse(number, 0, 4); Array.Reverse(number, 4, 4); Array.Reverse(number);
                                    stack.Push(BitConverter.ToDouble(number, 0)); break;
                                case 7: Require(reader, 4, actionEnd); stack.Push(reader.ReadInt32()); break;
                                case 8: case 9:
                                    Require(reader, type == 8 ? 1 : 2, actionEnd);
                                    int index = type == 8 ? reader.ReadByte() : reader.ReadUInt16();
                                    if (index >= constants.Length) throw new InvalidDataException("Référence à une constante SWF absente.");
                                    stack.Push(constants[index]); break;
                                default: throw new InvalidDataException($"Type ActionPush non pris en charge : {type}.");
                            }
                            if (stack.Count > 4096) throw new InvalidDataException("La pile d'actions SWF est trop grande.");
                        }
                        break;
                    case 0x17: Pop(stack); break;
                    case 0x4c: stack.Push(Peek(stack)); break;
                    case 0x87:
                        Require(reader, 1, actionEnd); registers[reader.ReadByte()] = Peek(stack); break;
                    case 0x1c:
                        string variable = Pop(stack) as string;
                        stack.Push(variable != null && values.TryGetValue(variable, out object existing) ? existing :
                            variable != null ? (object)new Reference(variable) : Unknown); break;
                    case 0x1d: case 0x3c:
                        object assigned = Pop(stack); string name = Pop(stack) as string;
                        if (name == null) throw new InvalidDataException("Nom de variable SWF indéterminé.");
                        values[name] = assigned; break;
                    case 0x4e:
                        string member = Pop(stack) as string; Reference reference = Pop(stack) as Reference;
                        stack.Push(reference != null && member != null ? (object)new Reference(reference.Path + "." + member) : Unknown); break;
                    case 0x4f:
                        object memberValue = Pop(stack); string memberName = Pop(stack) as string; Reference target = Pop(stack) as Reference;
                        if (target == null || (target.Path != "this" && target.Path != "_root") || memberName == null)
                            throw new InvalidDataException("Affectation SWF dans un objet non pris en charge.");
                        values[memberName] = memberValue; break;
                    case 0x52:
                        string method = Pop(stack) as string; Reference owner = Pop(stack) as Reference;
                        object argumentCount = Pop(stack);
                        if (method != "allowDomain" || owner == null || owner.Path != "System.security" || !(argumentCount is int) || (int)argumentCount != 1)
                            throw new InvalidDataException("La carte contient un appel de méthode non pris en charge.");
                        Pop(stack); stack.Push(Unknown); break;
                    case 0x21: case 0x47:
                        object right = Pop(stack); object left = Pop(stack);
                        if (!(left is string) || !(right is string) || ((string)left).Length + ((string)right).Length > MaxMapData)
                            throw new InvalidDataException("Concaténation SWF invalide ou trop grande.");
                        stack.Push((string)left + (string)right); break;
                    case 0x07: case 0x06: break; // Stop / Play, no effect on literal assignments
                    default: throw new InvalidDataException($"Action SWF non prise en charge : 0x{code:X2}. La carte n'a pas été exécutée.");
                }
                if (reader.BaseStream.Position != actionEnd) throw new InvalidDataException("La longueur d'une action SWF est incorrecte.");
                if (stack.Count > 4096 || values.Count > 4096)
                    throw new InvalidDataException("La pile ou le nombre de variables SWF dépasse la limite autorisée.");
            }
            if (!ended) throw new EndOfStreamException("La balise de fin des actions SWF est absente.");
        }

        private static object Pop(Stack<object> stack)
        {
            if (stack.Count == 0) throw new InvalidDataException("La pile d'actions SWF est incomplète.");
            return stack.Pop();
        }
        private static object Peek(Stack<object> stack)
        {
            if (stack.Count == 0) throw new InvalidDataException("La pile d'actions SWF est incomplète.");
            return stack.Peek();
        }
        private static void Require(BinaryReader reader, long length, long end)
        {
            if (length < 0 || length > end - reader.BaseStream.Position)
                throw new EndOfStreamException("Le tag ou l'action SWF dépasse la fin des données.");
        }
        private static string CString(BinaryReader reader, long end)
        {
            using (var stream = new MemoryStream())
            {
                while (reader.BaseStream.Position < end)
                {
                    byte value = reader.ReadByte();
                    if (value == 0)
                    {
                        try { return Utf8.GetString(stream.ToArray()); }
                        catch (DecoderFallbackException error) { throw new InvalidDataException("L'encodage d'une chaîne SWF est invalide.", error); }
                    }
                    stream.WriteByte(value);
                }
            }
            throw new EndOfStreamException("Chaîne SWF sans terminateur.");
        }
        private static object Value(Dictionary<string, object> values, string name)
        {
            if (!values.TryGetValue(name, out object value) || value == Unknown || value is Reference)
                throw new InvalidDataException($"La propriété de carte {name} est absente ou indéterminée.");
            return value;
        }
        private static int Integer(Dictionary<string, object> values, string name)
        {
            object value = Value(values, name);
            if (!(value is int || value is float || value is double || value is string) ||
                !double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ||
                double.IsNaN(number) || number < int.MinValue || number > int.MaxValue || number != Math.Truncate(number))
                throw new InvalidDataException($"La propriété de carte {name} n'est pas un entier valide.");
            return (int)number;
        }
        private static void ValidateMetadata(Map map)
        {
            if (map.ID <= 0 || map.Width < 2 || map.Width > 100 || map.Height < 2 || map.Height > 100 ||
                map.BackGroundID < 0 || map.Ambiance < 0 || map.Musique < 0 || map.Capabilities < 0)
                throw new InvalidDataException("Les propriétés de la carte sont invalides.");
        }
        private static void ValidateData(Map map)
        {
            int expected = Map.CellCount(map.Width, map.Height) * 10;
            if (map.MapData == null) throw new InvalidDataException("La propriété mapData n'est pas une chaîne.");
            bool cipher = map.MapData.Length == expected * 2;
            if (map.MapData.Length != expected && !cipher) throw new InvalidDataException("La longueur des données de carte est incorrecte.");
            foreach (char character in map.MapData)
                if (cipher ? !Uri.IsHexDigit(character) : "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_".IndexOf(character) < 0)
                    throw new InvalidDataException("Les données de carte contiennent un caractère invalide.");
        }
        private static void Assign(BinaryWriter writer, string name, object value)
        { Push(writer, name); Push(writer, value); writer.Write((byte)0x1d); }
        private static void Push(BinaryWriter writer, object value)
        {
            using (var stream = new MemoryStream())
            using (var payload = new BinaryWriter(stream))
            {
                if (value is string) { payload.Write((byte)0); payload.Write(Utf8.GetBytes((string)value)); payload.Write((byte)0); }
                else if (value is bool) { payload.Write((byte)5); payload.Write((byte)((bool)value ? 1 : 0)); }
                else { payload.Write((byte)7); payload.Write((int)value); }
                if (stream.Length > ushort.MaxValue) throw new InvalidOperationException("L'action SWF est trop grande.");
                writer.Write((byte)0x96); writer.Write((ushort)stream.Length); writer.Write(stream.ToArray());
            }
        }
        private static void WriteTag(BinaryWriter writer, int code, byte[] payload)
        {
            writer.Write((ushort)((code << 6) | (payload.Length < 63 ? payload.Length : 63)));
            if (payload.Length >= 63) writer.Write((uint)payload.Length);
            writer.Write(payload);
        }
    }
}
