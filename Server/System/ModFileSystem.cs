using LmpCommon.ModFile.Structure;
using LmpCommon.Xml;
using Server.Context;
using Server.Log;
using System;
using System.IO;

namespace Server.System
{
    public class ModFileSystem
    {
        public static ModControlStructure ModControl { get; private set; }

        public static void GenerateNewModFile()
        {
            var defaultModFile = new ModControlStructure();
            defaultModFile.SetDefaultAllowedParts();
            defaultModFile.SetDefaultAllowedResources();

            FileHandler.WriteToFile(ServerContext.ModFilePath, LunaXmlSerializer.SerializeToXml(defaultModFile));
        }

        public static void LoadModFile()
        {
            try
            {
                if (File.Exists(ServerContext.ModFilePath))
                {
                    ModControl = LunaXmlSerializer.ReadXmlFromPath<ModControlStructure>(ServerContext.ModFilePath);
                }
                else if (File.Exists(ServerContext.LegacyModFilePath))
                {
                    ModControl = LunaXmlSerializer.ReadXmlFromPath<ModControlStructure>(ServerContext.LegacyModFilePath);
                    try
                    {
                        FileHandler.WriteToFile(ServerContext.ModFilePath, LunaXmlSerializer.SerializeToXml(ModControl));
                    }
                    catch
                    {
                    }
                }
                else if (File.Exists(ServerContext.OldModFilePath))
                {
                    ModControl = LunaXmlSerializer.ReadXmlFromPath<ModControlStructure>(ServerContext.OldModFilePath);
                }
                else
                {
                    throw new FileNotFoundException("No mod control file in Config/ or app root");
                }
            }
            catch (Exception)
            {
                LunaLog.Error("Cannot read KSPModControl file. Will load the default one. Please regenerate it");
                ModControl = new ModControlStructure();
            }
        }
    }
}