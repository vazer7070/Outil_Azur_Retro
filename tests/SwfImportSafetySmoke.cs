using System;
using System.IO;
using System.Reflection;

internal static class SwfImportSafetySmoke
{
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve+=(sender,args)=>{
            string file=Path.Combine(TestPaths.ApplicationBin,new AssemblyName(args.Name).Name+".dll");
            return File.Exists(file)?Assembly.LoadFrom(file):null;
        };
        Run();
    }
    private static void Run()
    {
        string path=Path.Combine(TestPaths.Work,"truncated.swf");
        try
        {
            using(var stream=File.Create(path)) using(var writer=new BinaryWriter(stream))
            {
                writer.Write(new byte[] { (byte)'F',(byte)'W',(byte)'S',6 });
                writer.Write((uint)21);
                writer.Write(new byte[] { 8,0 });
                writer.Write((ushort)0x0c00); writer.Write((ushort)1);
                writer.Write((ushort)((12<<6)|63)); writer.Write((uint)32); writer.Write((byte)0);
            }
            bool rejected=false;
            try { Tool_Editor.maps.managers.MapSwfSerializer.Load(path); }
            catch(EndOfStreamException) { rejected=true; }
            if(!rejected)throw new Exception("Truncated SWF returned partial content");
            using(File.Open(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) { }
            Console.WriteLine("OK: truncated SWF rejected and input file released");
        }
        finally { if(File.Exists(path))File.Delete(path); }
    }
}
