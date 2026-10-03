using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using SwfDotNet.IO;
using SwfDotNet.IO.ByteCode;
using SwfDotNet.IO.Tags;
using Tool_Editor.items;

internal static class ItemClientSwfSmoke
{
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s,e) => {
            string name = new AssemblyName(e.Name).Name;
            string path = name == "log4net" ? Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "log4net.dll") : Path.Combine(TestPaths.ApplicationBin, name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        Run();
    }
    private static void Check(bool valid,string message) { if (!valid) throw new Exception(message); }
    private static void Reject(Action action,string message)
    { bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}catch(InvalidDataException){rejected=true;}catch(EndOfStreamException){rejected=true;}catch(InvalidOperationException){rejected=true;}Check(rejected,message); }
    private static void Run()
    {
        string path=Path.Combine(TestPaths.Work,"items-source.swf"),copy=Path.Combine(TestPaths.Work,"items-copy.swf"),bad=Path.Combine(TestPaths.Work,"items-bad.swf");
        var item=new ItemClientDefinition {Id=901,Name="Épée \"Azur\" \\ test",Description="Ligne 1\nLigne 2\tété",Type=6,Graphic=120,Level=25,Weight=15,Price=1200,Condition="CI>30&CS>4",ForgeMagic=true,Usable=false,TwoHands=true,ActionPoints=4,MinimumRange=1,MaximumRange=2,CriticalRate=40,FailureRate=50,CriticalBonus=7};
        ItemClientSwf.Save(path,item);
        byte[] compiled=File.ReadAllBytes(path);
        Check(compiled.Take(3).SequenceEqual(Encoding.ASCII.GetBytes("FWS")),"Item export is not a real SWF");
        Swf movie=new SwfDotNet.IO.SwfReader(path).ReadSwf();var code=new StringBuilder();
        foreach(BaseTag tag in movie.Tags)foreach(byte[] actions in tag)foreach(object action in new Decompiler(movie.Version).Decompile(actions))code.AppendLine(action.ToString());
        Check(movie.Version==6 && code.ToString().Contains("initObject") && code.ToString().Contains("initArray") && code.ToString().Contains("setMember") && code.ToString().Contains("901"),"Independent SWF/AVM1 reader failed");
        var doc=ItemClientSwf.Load(path);var values=doc.GetFields(901);object[] weapon=(object[])values["e"];
        Check(doc.Count==1 && (string)values["n"]==item.Name && (string)values["d"]==item.Description && (bool)values["fm"] && (bool)values["tw"] && !(bool)values["u"],"Unicode/scalars/bools round-trip failed");
        Check(weapon.Take(6).Cast<int>().SequenceEqual(new[]{7,4,1,2,40,50}) && !(bool)weapon[6] && !(bool)weapon[7],"Weapon arguments have the wrong stack order");
        values["n"]="Temporary";Check((string)doc.GetFields(901)["n"]==item.Name,"Read fields mutated the source snapshot");
        // Independently assembled SWF, including constant pools, constructors, registers and an unrelated I.t table.
        byte[] original=Fixture();File.WriteAllBytes(path,original);doc=ItemClientSwf.Load(path);
        Check(doc.Count==2 && doc.ItemNames[100]=="Objet A" && doc.ItemNames[200]=="Objet B","Independent literal fixture was not decoded");
        var a=doc.GetFields(100);Check((double)a["p"]==1.25 && (float)a["l"]==20f && a.ContainsKey("custom"),"Literal number/custom formats were lost");
        a["n"]="Objet A modifié";a["custom"]=new Dictionary<string,object>{{"texte","Essai"},{"nested",new object[]{true,null,123,"é"}}};
        var b=doc.GetFields(200);b["n"]="Objet B modifié";
        byte[] patched=doc.WithChanges(new Dictionary<int,IDictionary<string,object>>{{100,a},{200,b}});
        doc.SaveCopy(copy,patched);
        Check(original.SequenceEqual(File.ReadAllBytes(path)),"Injection changed the source file");
        Check(PreservedTags(original,patched),"Original SWF tags or actions were rewritten");
        var reread=ItemClientSwf.Load(copy);Check(reread.Count==2 && reread.ItemNames[100]=="Objet A modifié" && reread.ItemNames[200]=="Objet B modifié","Multiple drafts dropped an earlier item");
        var extra=(IDictionary<string,object>)reread.GetFields(100)["custom"];var nested=(object[])extra["nested"];
        Check((bool)nested[0] && nested[1]==null && (int)nested[2]==123 && (string)nested[3]=="é","Nested client properties were lost");
        var added=new ItemClientDefinition {Id=902,Name="Objet ajouté",Type=1,Level=1};doc.SaveCopy(copy,doc.WithDefinition(added));
        Check(ItemClientSwf.Load(copy).Count==3 && ItemClientSwf.Load(copy).ItemNames[200]=="Objet B","Adding an item replaced the existing database");
        added.Id=100;Reject(()=>doc.WithDefinition(added),"Duplicate client ID was overwritten silently");
        Reject(()=>doc.SaveCopy(path,patched),"Original source overwrite was accepted");
        File.WriteAllBytes(path,Compress(original));Check(ItemClientSwf.Load(path).Count==2,"CWS item import failed");
        byte[] compressed=Compress(original);compressed[compressed.Length-1]^=1;File.WriteAllBytes(bad,compressed);Reject(()=>ItemClientSwf.Load(bad),"Bad compression checksum accepted");
        File.WriteAllBytes(bad,original.Take(original.Length-1).ToArray());Reject(()=>ItemClientSwf.Load(bad),"Truncated SWF accepted");
        byte[] animated=(byte[])original.Clone();animated[12]=2;File.WriteAllBytes(bad,animated);Reject(()=>ItemClientSwf.Load(bad),"Animated language file accepted");
        File.WriteAllBytes(bad,Movie(new byte[]{0x99,2,0,0,0,0}));Reject(()=>ItemClientSwf.Load(bad),"Dynamic control flow accepted");
        File.WriteAllBytes(bad,Movie(new byte[]{0x96,2,0,0,255,0}));Reject(()=>ItemClientSwf.Load(bad),"Bad string accepted");
        byte[] before=File.ReadAllBytes(copy);added.Id=903;added.Name="Invalid\0name";Reject(()=>ItemClientSwf.Save(copy,added),"NUL string compiled");
        Check(before.SequenceEqual(File.ReadAllBytes(copy)),"Invalid compile destroyed an existing export");
        added.Name="Bad range";added.MinimumRange=5;added.MaximumRange=1;Reject(()=>ItemClientSwf.Compile(added),"Invalid weapon range accepted");
        // Large sparse IDs must not allocate an array of ID+1 elements.
        added.MinimumRange=0;added.MaximumRange=0;added.Id=int.MaxValue;ItemClientSwf.Save(copy,added);Check(ItemClientSwf.Load(copy).Count==1,"Sparse client item allocated by its ID");
        Console.WriteLine("OK: binary item SWF, independent AVM1 reader/fixture, literal constructors/constants/registers, FWS/CWS, nested options, multi-item copy preservation and corruption guards");
    }
    private static bool PreservedTags(byte[] before,byte[] after)
    {
        var original=TagBytes(before);var updated=TagBytes(after);int index=0;
        foreach(byte[] tag in updated)if(index<original.Count && tag.SequenceEqual(original[index]))index++;
        return index==original.Count && updated.Count==original.Count+2;
    }
    private static List<byte[]> TagBytes(byte[] movie)
    {
        var tags=new List<byte[]>();using(var stream=new MemoryStream(movie))using(var r=new BinaryReader(stream)){
            stream.Position=8;int bits=r.ReadByte()>>3;stream.Position=8+(5+bits*4+7)/8+4;
            while(stream.Position<stream.Length){int start=(int)stream.Position;ushort h=r.ReadUInt16();int length=h&63;if(length==63)length=(int)r.ReadUInt32();stream.Position+=length;tags.Add(movie.Skip(start).Take((int)stream.Position-start).ToArray());}
        }return tags;
    }
    private static byte[] Fixture()
    {
        using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream)){
            // Pool: I,u,n,Object,Array,custom,p,l,t. NewObject constructor receives name last.
            string[] constants={"I","u","n","Object","Array","custom","p","l","t"};
            using(var pool=new MemoryStream())using(var p=new BinaryWriter(pool)){p.Write((ushort)constants.Length);foreach(string s in constants){p.Write(Encoding.UTF8.GetBytes(s));p.Write((byte)0);}w.Write((byte)0x88);w.Write((ushort)pool.Length);w.Write(pool.ToArray());}
            Constant(w,0);Number(w,0);Constant(w,3);w.Write((byte)0x40);w.Write((byte)0x1d);
            Constant(w,0);w.Write((byte)0x1c);Constant(w,1);Number(w,0);Constant(w,4);w.Write((byte)0x40);w.Write((byte)0x4f);
            Constant(w,0);w.Write((byte)0x1c);Constant(w,8);Text(w,"unrelated type data");w.Write((byte)0x4f);
            foreach(int id in new[]{100,200}){
                Constant(w,0);w.Write((byte)0x1c);Constant(w,1);w.Write((byte)0x4e);Number(w,id);
                Constant(w,2);Text(w,id==100?"Objet A":"Objet B");
                Constant(w,5);Number(w,77);
                Constant(w,6);w.Write(new byte[]{0x96,9,0,6});byte[] d=BitConverter.GetBytes(1.25d);w.Write(d,4,4);w.Write(d,0,4);
                Constant(w,7);w.Write(new byte[]{0x96,5,0,1});w.Write(20f);
                Number(w,4);w.Write((byte)0x43);w.Write(new byte[]{0x87,1,0,1});w.Write((byte)0x4f);
            }
            w.Write((byte)0x07);w.Write((byte)0);return Movie(stream.ToArray());
        }
    }
    private static void Constant(BinaryWriter w,byte index){w.Write(new byte[]{0x96,2,0,8,index});}
    private static void Number(BinaryWriter w,int value){w.Write(new byte[]{0x96,5,0,7});w.Write(value);}
    private static void Text(BinaryWriter w,string value){byte[] text=Encoding.UTF8.GetBytes(value);w.Write((byte)0x96);w.Write((ushort)(text.Length+2));w.Write((byte)0);w.Write(text);w.Write((byte)0);}
    private static byte[] Movie(byte[] actions)
    {
        using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream)){
            w.Write(new byte[]{70,87,83,6});w.Write((uint)0);w.Write(new byte[]{8,0,0,12,1,0});
            w.Write((ushort)((9<<6)|3));w.Write(new byte[]{240,244,248});
            w.Write((ushort)((12<<6)|63));w.Write((uint)actions.Length);w.Write(actions);w.Write((ushort)64);w.Write((ushort)0);
            stream.Position=4;w.Write((uint)stream.Length);return stream.ToArray();
        }
    }
    private static byte[] Compress(byte[] movie)
    {
        byte[] data=movie.Skip(8).ToArray();using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream)){
            w.Write(movie.Take(8).ToArray());stream.Position=0;w.Write((byte)'C');stream.Position=8;w.Write(new byte[]{0x78,0x01});
            using(var compressor=new System.IO.Compression.DeflateStream(stream,System.IO.Compression.CompressionMode.Compress,true))compressor.Write(data,0,data.Length);
            uint a=1,b=0;foreach(byte value in data){a=(a+value)%65521;b=(b+a)%65521;}uint checksum=(b<<16)|a;w.Write(new byte[]{(byte)(checksum>>24),(byte)(checksum>>16),(byte)(checksum>>8),(byte)checksum});return stream.ToArray();
        }
    }
}
