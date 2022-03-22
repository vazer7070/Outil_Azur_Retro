using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Tools_protocol.Json
{
    public class UpdateJsonManager
    {
        private static string NewToolversion { get; set; }
        private static string NewProtocolVersion { get; set; }
        private static string NewBotVersion { get; set; }
        private static string NewEditorVersion { get; set; }

        private static bool IsTool;
        private static bool IsProtocol;
        private static bool IsBot;
        private static bool IsEditor;

        private const string FolderPath = @"https://azurtoolretro.com/amaj/";
        public const string ZipPath = @".\MAJ\";
        public static bool NeedMaj(string path, string Tv, string Pv, string AZv, string Ev)
        {
            Directory.CreateDirectory(ZipPath);
            try
            {
                using (WebClient wc = new WebClient())
                {
                    var json = wc.DownloadString(path);
                    UpdateEnums.UpdateEntries maj = JsonConvert.DeserializeObject<UpdateEnums.UpdateData>(json).UpdateEntries;
                    if (maj.ToolVersion != Tv)
                    {
                        NewToolversion = maj.ToolVersion;
                        IsTool = true;
                        return true;

                    }
                    else if (maj.ProtocolVersion != Pv)
                    {
                        NewProtocolVersion = maj.ProtocolVersion;
                        IsProtocol = true;
                        return true;

                    }
                    else if (maj.AzurBotVersion != AZv)
                    {
                        NewBotVersion = maj.AzurBotVersion;
                        IsBot = true;
                        return true;

                    }else if(maj.EditorVersion != Ev)
                    {
                        NewEditorVersion = maj.EditorVersion;
                        IsEditor = true;
                        return true;
                    }
                    else
                    {
                        IsEditor = false;
                        IsTool = false;
                        IsProtocol=false;
                        IsBot =false;
                        return false;
                    }
                        
                }

            }catch(Exception)
            {
                return false;
            }
        }
        public static string GetFile()
        {
            return GetNewVersion().Split('/')[4];
        }
        public static string GetNewVersion()
        {
            if (IsTool)
                return $"{FolderPath}azT{NewToolversion}.zip";
            else if (IsProtocol)
                return $"{FolderPath}Tp{NewProtocolVersion}.zip";
            else if (IsBot)
                return $"{FolderPath}Ab{NewBotVersion}.zip";
            else if(IsEditor)
                return $"{FolderPath}Ed{NewEditorVersion}.zip";
            else
                return "";
        }
       
    }
}
