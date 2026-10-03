using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Tool_Editor.swf;

namespace Tool_Editor.items
{
    // A bounded literal decoder, not an ActionScript runtime. No calls, loops or imported code execute.
    public sealed class ItemClientSwf
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private sealed class LiteralObject : Dictionary<string, object>
        {
            internal bool Array;
            internal LiteralObject(bool array = false) : base(StringComparer.Ordinal) { Array = array; }
        }
        private sealed class Tag { internal int Code, Start, Data, Length, End; }
        private readonly byte[] movie;
        private readonly List<Tag> tags;
        private readonly LiteralObject items;
        private readonly string sourcePath;
        private ItemClientSwf(byte[] movie, List<Tag> tags, LiteralObject items, string sourcePath)
        { this.movie = movie; this.tags = tags; this.items = items; this.sourcePath = sourcePath; }

        public int Count { get { return items.Keys.Count(key => int.TryParse(key, out int id) && id > 0); } }
        public IDictionary<int, string> ItemNames
        {
            get {
                var names = new SortedDictionary<int, string>();
                foreach (var pair in items) if (int.TryParse(pair.Key, out int id) && id > 0) {
                    var value = pair.Value as LiteralObject;
                    object name;
                    names[id] = value != null && value.TryGetValue("n", out name) ? Convert.ToString(name, CultureInfo.InvariantCulture) : "Objet " + id;
                }
                return names;
            }
        }

        public static ItemClientSwf Load(string path)
        { return Parse(SwfMovieFile.Read(path), Path.GetFullPath(path)); }

        public static byte[] Compile(ItemClientDefinition item)
        {
            item.Validate();
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream)) {
                writer.Write(new byte[] { 70, 87, 83, 6 }); writer.Write((uint)0);
                writer.Write(new byte[] { 8, 0, 0, 12, 1, 0 });
                using (var actions = new MemoryStream()) using (var a = new BinaryWriter(actions)) {
                    Push(a, "I"); Push(a, 0); a.Write((byte)0x43); a.Write((byte)0x1d);
                    Push(a, "I"); a.Write((byte)0x1c); Push(a, "u"); Push(a, 0); a.Write((byte)0x42); a.Write((byte)0x4f);
                    WriteAssignment(a, item.Id, DefinitionFields(item)); a.Write((byte)0x07); a.Write((byte)0);
                    WriteTag(writer, 12, actions.ToArray());
                }
                WriteTag(writer, 1, new byte[0]); WriteTag(writer, 0, new byte[0]);
                stream.Position = 4; writer.Write((uint)stream.Length);
                byte[] result = stream.ToArray(); Parse(result, null); return result;
            }
        }

        public static void Save(string path, ItemClientDefinition item)
        { SwfMovieFile.WriteAtomic(path, Compile(item)); }

        public static void SaveCompiled(string path, byte[] movie)
        { Parse(movie, null); SwfMovieFile.WriteAtomic(path, movie); }

        public byte[] WithChanges(IDictionary<int, IDictionary<string, object>> changes)
        {
            ItemClientSwf current = this;
            foreach (var change in changes) current = Parse(current.WithFields(change.Key, change.Value), null);
            return current.movie;
        }

        // Independent snapshots keep the caller from modifying the decoded source accidentally.
        public IDictionary<string, object> GetFields(int id)
        {
            object value;
            if (!items.TryGetValue(id.ToString(CultureInfo.InvariantCulture), out value) || !(value is LiteralObject))
                throw new InvalidDataException("Cet identifiant ne contient pas de fiche objet client.");
            return (IDictionary<string, object>)ToPublic(value, 0);
        }

        public byte[] WithDefinition(ItemClientDefinition item)
        {
            item.Validate(); string key = item.Id.ToString(CultureInfo.InvariantCulture);
            if (items.ContainsKey(key))
                throw new InvalidOperationException("L'objet " + item.Id + " existe déjà dans le SWF. Ouvrez l'éditeur client pour le modifier.");
            var fields = new LiteralObject();
            foreach (var pair in DefinitionFields(item)) fields[pair.Key] = pair.Value;
            return Patch(item.Id, fields);
        }

        public byte[] WithFields(int id, IDictionary<string, object> fields)
        {
            if (id <= 0 || !items.ContainsKey(id.ToString(CultureInfo.InvariantCulture)))
                throw new InvalidOperationException("Sélectionnez un objet existant dans le fichier client.");
            if (fields == null || fields.Count == 0) throw new ArgumentException("La fiche client est vide.");
            return Patch(id, (LiteralObject)FromPublic(fields, 0));
        }

        public void SaveCopy(string destination, byte[] patchedMovie)
        {
            if (sourcePath != null && string.Equals(sourcePath, Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choisissez un autre fichier pour conserver le SWF d'origine.");
            Parse(patchedMovie, null);
            SwfMovieFile.WriteAtomic(destination, patchedMovie);
        }

        private byte[] Patch(int id, LiteralObject fields)
        {
            byte[] actions;
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream)) {
                WriteAssignment(writer, id, fields); writer.Write((byte)0); actions = stream.ToArray();
            }
            int insert = tags.Single(tag => tag.Code == 1).Start;
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream)) {
                writer.Write(movie, 0, insert); WriteTag(writer, 12, actions);
                writer.Write(movie, insert, movie.Length - insert);
                if (stream.Length > SwfMovieFile.MaximumSize) throw new InvalidDataException("Le SWF dépasse 32 Mo.");
                stream.Position = 4; writer.Write((uint)stream.Length);
                byte[] result = stream.ToArray(); Parse(result, null); return result;
            }
        }

        private static LiteralObject DefinitionFields(ItemClientDefinition item)
        {
            var weapon = new LiteralObject(true);
            object[] values = { item.CriticalBonus, item.ActionPoints, item.MinimumRange, item.MaximumRange, item.CriticalRate, item.FailureRate, false, false };
            for (int i = 0; i < values.Length; i++) weapon[i.ToString(CultureInfo.InvariantCulture)] = values[i];
            return new LiteralObject {
                { "p", item.Price }, { "t", item.Type }, { "d", string.IsNullOrEmpty(item.Description) ? "#1" : item.Description },
                { "ep", 7 }, { "g", item.Graphic }, { "l", item.Level }, { "fm", item.ForgeMagic }, { "w", item.Weight },
                { "tw", item.TwoHands }, { "e", weapon }, { "c", item.Condition ?? "" }, { "u", item.Usable }, { "n", item.Name }
            };
        }

        private static ItemClientSwf Parse(byte[] bytes, string source)
        {
            if (bytes == null || bytes.Length < 14 || bytes.Length > SwfMovieFile.MaximumSize ||
                bytes[0] != 'F' || bytes[1] != 'W' || bytes[2] != 'S' || bytes[3] < 6 || BitConverter.ToUInt32(bytes, 4) != bytes.Length)
                throw new InvalidDataException("Le fichier n'est pas un SWF AVM1 valide.");
            var tags = new List<Tag>(); var globals = new LiteralObject();
            using (var stream = new MemoryStream(bytes, false)) using (var reader = new BinaryReader(stream)) {
                stream.Position = 8; int bits = reader.ReadByte() >> 3;
                if (bits == 0) throw new InvalidDataException("Le rectangle SWF est invalide.");
                int headerSize = (5 + bits * 4 + 7) / 8;
                Need(reader, headerSize - 1 + 4, stream.Length); stream.Position += headerSize - 1;
                reader.ReadUInt16();
                if (reader.ReadUInt16() != 1) throw Unsupported("plusieurs images");
                bool shown = false, ended = false;
                while (stream.Position < stream.Length) {
                    int start = (int)stream.Position; Need(reader, 2, stream.Length); ushort header = reader.ReadUInt16();
                    int code = header >> 6; uint size = (uint)(header & 63);
                    if (size == 63) { Need(reader, 4, stream.Length); size = reader.ReadUInt32(); }
                    Need(reader, size, stream.Length);
                    var tag = new Tag { Code = code, Start = start, Data = (int)stream.Position, Length = (int)size, End = (int)(stream.Position + size) }; tags.Add(tag);
                    if (code == 0) {
                        if (size != 0 || tag.End != stream.Length || !shown) throw new InvalidDataException("La fin du SWF est invalide.");
                        ended = true; break;
                    }
                    if (code == 1) {
                        if (size != 0 || shown) throw new InvalidDataException("L'image SWF est invalide."); shown = true;
                    }
                    else if (code == 12) {
                        if (shown) throw Unsupported("actions après l'image");
                        ReadActions(reader, tag.End, globals);
                    }
                    else if (code == 9) { if (size != 3) throw new InvalidDataException("Couleur de fond SWF invalide."); }
                    else if (code == 69) {
                        if (size != 4 || (reader.ReadUInt32() & 8) != 0) throw Unsupported("AVM2");
                    }
                    else if (code != 43 && code != 24 && code != 77) throw Unsupported("balise " + code);
                    stream.Position = tag.End;
                    if (tags.Count > 100000) throw new InvalidDataException("Le SWF contient trop de balises.");
                }
                if (!ended) throw new EndOfStreamException("La balise de fin SWF est absente.");
            }
            object root, table;
            if (!globals.TryGetValue("I", out root) || !(root is LiteralObject) ||
                !((LiteralObject)root).TryGetValue("u", out table) || !(table is LiteralObject))
                throw new InvalidDataException("Ce fichier ne contient pas la table d'objets client I.u.");
            var document = new ItemClientSwf(bytes, tags, (LiteralObject)table, source);
            if (document.Count == 0) throw new InvalidDataException("Le fichier client ne contient aucun objet.");
            return document;
        }

        private static void ReadActions(BinaryReader reader, long end, LiteralObject globals)
        {
            var stack = new Stack<object>(); var registers = new Dictionary<byte, object>(); string[] constants = new string[0];
            int entries = 0; bool ended = false;
            while (reader.BaseStream.Position < end) {
                int code = reader.ReadByte();
                if (code == 0) { if (reader.BaseStream.Position != end || stack.Count != 0) throw new InvalidDataException("Fin d'actions SWF incohérente."); ended = true; break; }
                int length = 0;
                if (code >= 0x80) { Need(reader, 2, end); length = reader.ReadUInt16(); }
                Need(reader, length, end); long actionEnd = reader.BaseStream.Position + length;
                switch (code) {
                    case 0x88:
                        Need(reader, 2, actionEnd); constants = new string[reader.ReadUInt16()];
                        for (int i = 0; i < constants.Length; i++) constants[i] = CString(reader, actionEnd);
                        break;
                    case 0x96:
                        while (reader.BaseStream.Position < actionEnd) {
                            int kind = reader.ReadByte(); object value;
                            switch (kind) {
                                case 0: value = CString(reader, actionEnd); break;
                                case 1: Need(reader, 4, actionEnd); value = reader.ReadSingle(); break;
                                case 2: value = null; break;
                                case 4:
                                    Need(reader, 1, actionEnd); if (!registers.TryGetValue(reader.ReadByte(), out value)) throw Unsupported("registre absent"); break;
                                case 5:
                                    Need(reader, 1, actionEnd); byte boolean = reader.ReadByte();
                                    if (boolean > 1) throw new InvalidDataException("Booléen SWF invalide."); value = boolean != 0; break;
                                case 6:
                                    Need(reader, 8, actionEnd); byte[] number = reader.ReadBytes(8);
                                    value = BitConverter.ToDouble(number.Skip(4).Concat(number.Take(4)).ToArray(), 0); break;
                                case 7: Need(reader, 4, actionEnd); value = reader.ReadInt32(); break;
                                case 8: case 9:
                                    Need(reader, kind == 8 ? 1 : 2, actionEnd); int index = kind == 8 ? reader.ReadByte() : reader.ReadUInt16();
                                    if (index >= constants.Length) throw new InvalidDataException("Constante SWF absente."); value = constants[index]; break;
                                default: throw Unsupported("type littéral " + kind);
                            }
                            if (value is float && (float.IsNaN((float)value) || float.IsInfinity((float)value)) ||
                                value is double && (double.IsNaN((double)value) || double.IsInfinity((double)value))) throw new InvalidDataException("Nombre SWF invalide.");
                            stack.Push(value); if (stack.Count > 4096) throw Unsupported("pile trop grande");
                        }
                        break;
                    case 0x1c:
                        string variable = Key(Pop(stack)); object variableValue;
                        if (variable == "this") stack.Push(globals);
                        else if (globals.TryGetValue(variable, out variableValue)) stack.Push(variableValue);
                        else throw Unsupported("variable absente : " + variable);
                        break;
                    case 0x1d: case 0x3c:
                        object assigned = Pop(stack); string name = Key(Pop(stack)); globals[name] = assigned; entries++; break;
                    case 0x4e:
                        string property = Key(Pop(stack)); var target = Object(Pop(stack)); object member;
                        if (!target.TryGetValue(property, out member)) throw Unsupported("propriété absente : " + property);
                        stack.Push(member); break;
                    case 0x4f:
                        object memberValue = Pop(stack); string memberName = Key(Pop(stack)); Object(Pop(stack))[memberName] = memberValue; entries++; break;
                    case 0x42: case 0x43:
                        int count = CountValue(Pop(stack)); var created = new LiteralObject(code == 0x42);
                        for (int i = 0; i < count; i++) {
                            object value = Pop(stack); string key = code == 0x42 ? i.ToString(CultureInfo.InvariantCulture) : Key(Pop(stack)); created[key] = value;
                        }
                        entries += count; stack.Push(created); break;
                    case 0x40:
                        string constructor = Key(Pop(stack)); int args = CountValue(Pop(stack));
                        if (args != 0 || (constructor != "Object" && constructor != "Array")) throw Unsupported("constructeur " + constructor);
                        stack.Push(new LiteralObject(constructor == "Array")); break;
                    case 0x87:
                        if (length != 1 || stack.Count == 0) throw new InvalidDataException("Registre SWF invalide."); registers[reader.ReadByte()] = stack.Peek(); break;
                    case 0x17: Pop(stack); break;
                    case 0x4c:
                        object duplicate = Pop(stack); stack.Push(duplicate); stack.Push(duplicate); break;
                    case 0x4d:
                        object first = Pop(stack), second = Pop(stack); stack.Push(first); stack.Push(second); break;
                    case 0x07: break;
                    default: throw Unsupported("instruction AVM1 0x" + code.ToString("X2"));
                }
                if (reader.BaseStream.Position != actionEnd) throw new InvalidDataException("Longueur d'instruction SWF incohérente.");
                if (entries > 500000 || stack.Count > 4096) throw Unsupported("trop de valeurs");
            }
            if (!ended) throw new EndOfStreamException("Fin d'actions SWF absente.");
        }

        private static object Pop(Stack<object> stack)
        { if (stack.Count == 0) throw new InvalidDataException("La pile SWF est incomplète."); return stack.Pop(); }
        private static LiteralObject Object(object value)
        { var result = value as LiteralObject; if (result == null) throw new InvalidDataException("L'objet SWF attendu est absent."); return result; }
        private static string Key(object value)
        { if (!(value is string) && !(value is int)) throw Unsupported("nom de propriété dynamique"); return Convert.ToString(value, CultureInfo.InvariantCulture); }
        private static int CountValue(object value)
        { if (!(value is int) || (int)value < 0 || (int)value > 4096) throw Unsupported("taille de tableau"); return (int)value; }
        private static InvalidDataException Unsupported(string detail)
        { return new InvalidDataException("Ce SWF utilise un format non pris en charge (" + detail + "). Seuls les fichiers d'objets AVM1 à données littérales sur une image sont acceptés."); }
        private static void Need(BinaryReader reader, long count, long end)
        { if (count < 0 || reader.BaseStream.Position + count > end) throw new EndOfStreamException("Le SWF est tronqué."); }
        private static string CString(BinaryReader reader, long end)
        {
            using (var stream = new MemoryStream()) {
                while (reader.BaseStream.Position < end) { byte value = reader.ReadByte(); if (value == 0) return Utf8.GetString(stream.ToArray()); stream.WriteByte(value); }
            }
            throw new EndOfStreamException("Chaîne SWF incomplète.");
        }

        private static object ToPublic(object value, int depth)
        {
            if (depth > 64) throw Unsupported("objet trop imbriqué ou cyclique");
            var literal = value as LiteralObject; if (literal == null) return value;
            if (literal.Array) {
                int maximum = -1;
                foreach (string key in literal.Keys) {
                    if (!int.TryParse(key, out int index) || index < 0 || index > 4095) throw Unsupported("tableau non séquentiel dans une fiche"); maximum = Math.Max(maximum, index);
                }
                var array = new object[maximum + 1];
                for (int i = 0; i < array.Length; i++) { object entry; if (!literal.TryGetValue(i.ToString(CultureInfo.InvariantCulture), out entry)) throw Unsupported("tableau creux dans une fiche"); array[i] = ToPublic(entry, depth + 1); }
                return array;
            }
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var pair in literal) result[pair.Key] = ToPublic(pair.Value, depth + 1);
            return result;
        }

        private static object FromPublic(object value, int depth)
        {
            if (depth > 64) throw Unsupported("objet trop imbriqué");
            var fields = value as IDictionary<string, object>;
            if (fields != null) {
                if (fields.Count > 1024) throw Unsupported("fiche trop grande");
                var result = new LiteralObject(); foreach (var pair in fields) { ValidateString(pair.Key); result[pair.Key] = FromPublic(pair.Value, depth + 1); } return result;
            }
            var array = value as object[];
            if (array != null) {
                if (array.Length > 1024) throw Unsupported("tableau trop grand");
                var result = new LiteralObject(true); for (int i = 0; i < array.Length; i++) result[i.ToString(CultureInfo.InvariantCulture)] = FromPublic(array[i], depth + 1); return result;
            }
            if (value == null || value is bool || value is int) return value;
            if (value is string) { ValidateString((string)value); return value; }
            if (value is float || value is double || value is decimal || value is long) {
                double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsNaN(number) || double.IsInfinity(number) || (value is long && Math.Abs(number) > 9007199254740991d)) throw new ArgumentException("Valeur numérique client invalide.");
                return number;
            }
            throw new ArgumentException("Cette valeur ne peut pas être représentée dans un fichier client.");
        }

        private static void ValidateString(string value)
        { if (value == null || value.IndexOf('\0') >= 0 || Utf8.GetByteCount(value) > 60000) throw new ArgumentException("Une chaîne client contient un caractère nul ou dépasse 60 000 octets UTF-8."); }
        private static void WriteAssignment(BinaryWriter writer, int id, LiteralObject fields)
        { Push(writer, "I"); writer.Write((byte)0x1c); Push(writer, "u"); writer.Write((byte)0x4e); Push(writer, id); Emit(writer, fields, 0); writer.Write((byte)0x4f); }
        private static void Emit(BinaryWriter writer, object value, int depth)
        {
            if (depth > 64) throw Unsupported("objet trop imbriqué");
            var literal = value as LiteralObject;
            if (literal == null) { Push(writer, value); return; }
            if (literal.Array) {
                for (int i = literal.Count - 1; i >= 0; i--) Emit(writer, literal[i.ToString(CultureInfo.InvariantCulture)], depth + 1);
            }
            else foreach (var pair in literal.Reverse()) { Push(writer, pair.Key); Emit(writer, pair.Value, depth + 1); }
            Push(writer, literal.Count); writer.Write((byte)(literal.Array ? 0x42 : 0x43));
        }
        private static void Push(BinaryWriter writer, object value)
        {
            using (var stream = new MemoryStream()) using (var data = new BinaryWriter(stream)) {
                if (value == null) data.Write((byte)2);
                else if (value is string) { ValidateString((string)value); data.Write((byte)0); data.Write(Utf8.GetBytes((string)value)); data.Write((byte)0); }
                else if (value is bool) { data.Write((byte)5); data.Write((byte)((bool)value ? 1 : 0)); }
                else if (value is int) { data.Write((byte)7); data.Write((int)value); }
                else {
                    double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    if (double.IsInfinity(number) || double.IsNaN(number)) throw new ArgumentException("Nombre client invalide.");
                    data.Write((byte)6); byte[] bytes = BitConverter.GetBytes(number); data.Write(bytes, 4, 4); data.Write(bytes, 0, 4);
                }
                writer.Write((byte)0x96); writer.Write((ushort)stream.Length); writer.Write(stream.ToArray());
            }
        }
        private static void WriteTag(BinaryWriter writer, int code, byte[] data)
        { writer.Write((ushort)((code << 6) | (data.Length < 63 ? data.Length : 63))); if (data.Length >= 63) writer.Write((uint)data.Length); writer.Write(data); }
    }
}
