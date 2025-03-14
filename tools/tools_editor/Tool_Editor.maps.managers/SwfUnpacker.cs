using SwfDotNet.IO.Tags;
using SwfDotNet.IO.Utils;
using SwfDotNet.IO;
using System.Collections.Generic;
using System.IO;
using System;
using SwfDotNet.IO.ByteCode;
using System.Collections;
using Tool_Editor.maps.data;
using System.Windows.Forms;

public class SwfReader
{
    private static BufferedBinaryReader br;
    private static List<BaseTag> tagList;
    private static ushort tagCode;
    private static uint tagLength;

    private static void ReadData(BufferedBinaryReader binaryReader)
    {
        if (tagLength > binaryReader.BaseStream.Length - binaryReader.BaseStream.Position)
            throw new EndOfStreamException($"Tag trop long: {tagLength} octets restants {binaryReader.BaseStream.Length - binaryReader.BaseStream.Position}");


        ushort num = binaryReader.ReadUInt16();
        tagCode = (ushort)(num >> 6);
        tagLength = (uint)(num - (tagCode << 6));

        if (tagLength == 63)
        {
            if (binaryReader.BaseStream.Position + 4 > binaryReader.BaseStream.Length)
                throw new EndOfStreamException("Impossible de lire la longueur étendue du tag");
            tagLength = binaryReader.ReadUInt32();
        }

        if (binaryReader.BaseStream.Position + tagLength > binaryReader.BaseStream.Length)
            tagLength = (uint)(binaryReader.BaseStream.Length - binaryReader.BaseStream.Position);
    }
    static void Init(string path, bool useBuffer)
    {
        Stream stream = File.OpenRead(path);
        if (useBuffer)
        {
            FileInfo fileInfo = new FileInfo(path);
            byte[] buffer = new byte[fileInfo.Length];
            stream.Read(buffer, 0, Convert.ToInt32(fileInfo.Length));
            stream.Close();
            MemoryStream stream2 = new MemoryStream(buffer);
            br = new BufferedBinaryReader(stream2);
        }
        else
        {
            br = new BufferedBinaryReader(stream);
        }
    }
    private static BaseTag ReadTag(byte version, BufferedBinaryReader binaryReader, List<BaseTag> tagList)
    {
        if (binaryReader.BaseStream.Position >= binaryReader.BaseStream.Length)
            return null;

        long position = binaryReader.BaseStream.Position;
        ReadData(binaryReader);
        int bytesRead = (int)(binaryReader.BaseStream.Position - position);

        if (binaryReader.BaseStream.Position + tagLength > binaryReader.BaseStream.Length)
            throw new EndOfStreamException("Tentative de lecture hors limites");

        binaryReader.BaseStream.Position = position;
        BaseTag baseTag;
        switch (tagCode)
        {
            case 6: baseTag = new DefineBitsTag(); break;
            case 21: baseTag = new DefineBitsJpeg2Tag(); break;
            case 35: baseTag = new DefineBitsJpeg3Tag(); break;
            case 20: baseTag = new DefineBitsLossLessTag(); break;
            case 36: baseTag = new DefineBitsLossLess2Tag(); break;
            case 7: baseTag = new DefineButtonTag(); break;
            case 34: baseTag = new DefineButton2Tag(); break;
            case 23: baseTag = new DefineButtonCxFormTag(); break;
            case 17: baseTag = new DefineButtonSoundTag(); break;
            case 37: baseTag = new DefineEditTextTag(); break;
            case 10: baseTag = new DefineFontTag(); break;
            case 48: baseTag = new DefineFont2Tag(); break;
            case 13: baseTag = new DefineFontInfoTag(); break;
            case 62: baseTag = new DefineFontInfo2Tag(); break;
            case 46: baseTag = new DefineMorphShapeTag(); break;
            case 2: baseTag = new DefineShapeTag(); break;
            case 22: baseTag = new DefineShape2Tag(); break;
            case 32: baseTag = new DefineShape3Tag(); break;
            case 14: baseTag = new DefineSoundTag(); break;
            case 39: baseTag = new DefineSpriteTag(); break;
            case 11: baseTag = new DefineTextTag(); break;
            case 33: baseTag = new DefineText2Tag(); break;
            case 60: baseTag = new DefineVideoStreamTag(); break;
            case 12: baseTag = new DoActionTag(); break;
            case 58: baseTag = new EnableDebuggerTag(); break;
            case 64: baseTag = new EnableDebugger2Tag(); break;
            case 0: baseTag = new EndTag(); break;
            default: baseTag = new BaseTag(binaryReader.ReadBytes((int)Math.Min(tagLength + bytesRead, binaryReader.BaseStream.Length - binaryReader.BaseStream.Position))); break;
        }

        if (baseTag == null)
            throw new InvalidOperationException($"Impossible de créer le tag avec le code {tagCode}");

        baseTag.ReadData(version, binaryReader);
        return baseTag;
    }
    public static Map UnPackerSwf(string path)
    {
        List<string> list = new List<string>();
        Init(path, false);
        Swf swf = TestSwf(path);
        IEnumerator enumerator = swf.Tags.GetEnumerator();

        while (enumerator.MoveNext())
        {
            BaseTag current = (BaseTag)enumerator.Current;
            if (current.ActionRecCount != 0)
            {
                string str = null;
                IEnumerator enumerator2 = current.GetEnumerator();
                while (enumerator2.MoveNext())
                {
                    ArrayList list2 = new Decompiler(swf.Version).Decompile((byte[])enumerator2.Current);
                    IEnumerator enumerator3 = list2.GetEnumerator();
                    try
                    {
                        while (enumerator3.MoveNext())
                        {
                            str += enumerator3.Current.ToString() + "\r\n";
                        }
                    }
                    finally
                    {
                        (enumerator3 as IDisposable)?.Dispose();
                    }
                }

                Map aMap = new Map
                {
                    MapData = str.Split('\'')[0x1D],
                    ID = int.Parse(str.Split(new string[] { "push" }, StringSplitOptions.None)[8].Split(' ')[1]),
                    Background = TilesData.GetBackgrounds(int.Parse(str.Split(new string[] { "push" }, StringSplitOptions.None)[14].Split(' ')[1])),
                    Height = int.Parse(str.Split(new string[] { "push" }, StringSplitOptions.None)[12].Split(' ')[1]),
                    Width = int.Parse(str.Split(new string[] { "push" }, StringSplitOptions.None)[10].Split(' ')[1]),
                    Ambiance = int.Parse(str.Split(new string[] { "push" }, StringSplitOptions.None)[0x10].Split(' ')[1]),
                    Musique = int.Parse(str.Split(new string[] { "push" }, StringSplitOptions.None)[0x12].Split(' ')[1]),
                    Capabilities = int.Parse(str.Split(new string[] { "push" }, StringSplitOptions.None)[0x16].Split(' ')[1]),
                    IsOutDoor = bool.Parse(str.Split(new string[] { "push" }, StringSplitOptions.None)[20].Split(' ')[1])
                };

                FileInfo fileInfo = new FileInfo(path);
                if (fileInfo.Name.Contains("_"))
                {
                    aMap.DateMap = fileInfo.Name.Split('_')[1].Split(new string[] { ".swf" }, StringSplitOptions.None)[0];
                }

                return aMap;
            }
        }
        return null;
    }
     static Swf TestSwf(string path)
    {
        SwfHeader swfHeader = new SwfHeader();
        tagList = new List<BaseTag>();
        BaseTagCollection collection = new BaseTagCollection();
        try
        {
            
            using (Stream stream = File.OpenRead(path))
            {
                br = new BufferedBinaryReader(stream);
                swfHeader.ReadData(br);
                byte version = swfHeader.Version;

                bool flag = false;
                while (br.BaseStream.Position < br.BaseStream.Length && !flag)
                {
                    BaseTag baseTag = ReadTag(version, br, tagList);
                    if (baseTag != null)
                    {
                        if (baseTag is EndTag) flag = true;
                        tagList.Add(baseTag);
                    }
                }
            }

            foreach(BaseTag tag in tagList)
                collection.Add(tag);

            return new Swf(swfHeader,collection);
        }
        catch 
        {
            BaseTagCollection collection2 = new BaseTagCollection();
            foreach (BaseTag tag in tagList)
                collection2.Add(tag);
            return new Swf(swfHeader, collection2);
        }
    }
}
