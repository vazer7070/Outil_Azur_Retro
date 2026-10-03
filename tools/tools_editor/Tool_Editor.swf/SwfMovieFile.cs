using System;
using System.IO;
using ICSharpCode.SharpZipLib.Zip.Compression;

namespace Tool_Editor.swf
{
    internal static class SwfMovieFile
    {
        internal const int MaximumSize = 32 * 1024 * 1024;
        internal static byte[] Read(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                if (stream.Length < 8 || stream.Length > MaximumSize)
                    throw new InvalidDataException("La taille du fichier SWF est invalide (maximum 32 Mo).");
                byte[] header = reader.ReadBytes(8);
                uint length = BitConverter.ToUInt32(header, 4);
                if (length < 14 || length > MaximumSize || header[1] != 'W' || header[2] != 'S' || header[3] < 4)
                    throw new InvalidDataException("L'en-tête SWF est invalide.");
                if (header[0] == 'F')
                {
                    if (length != stream.Length) throw new InvalidDataException("La taille annoncée dans le SWF est incorrecte.");
                    stream.Position = 0;
                    return reader.ReadBytes((int)length);
                }
                if (header[0] != 'C' || header[3] < 6)
                    throw new InvalidDataException("Seuls les SWF FWS et CWS sont pris en charge.");
                byte[] compressed = reader.ReadBytes((int)stream.Length - 8);
                var inflater = new Inflater();
                inflater.SetInput(compressed);
                byte[] bytes = new byte[length];
                Array.Copy(header, bytes, 8);
                bytes[0] = (byte)'F';
                int offset = 8;
                try
                {
                    while (offset < bytes.Length)
                    {
                        int read = inflater.Inflate(bytes, offset, bytes.Length - offset);
                        if (read == 0) break;
                        offset += read;
                    }
                    // Also consume the zlib checksum when the output buffer was filled exactly.
                    int extra = inflater.Inflate(new byte[1]);
                    if (offset != bytes.Length || extra != 0 || !inflater.IsFinished || inflater.RemainingInput != 0)
                        throw new InvalidDataException("Les données compressées SWF sont tronquées ou leur taille est incorrecte.");
                }
                catch (ICSharpCode.SharpZipLib.SharpZipBaseException error)
                { throw new InvalidDataException("La compression SWF est invalide.", error); }
                return bytes;
            }
        }

        internal static void WriteAtomic(string path, byte[] movie)
        {
            if (movie == null || movie.Length > MaximumSize) throw new InvalidDataException("Le SWF dépasse la limite de 32 Mo.");
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            string temporary = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { file.Write(movie, 0, movie.Length); file.Flush(true); }
                if (File.Exists(fullPath)) File.Replace(temporary, fullPath, null);
                else File.Move(temporary, fullPath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
