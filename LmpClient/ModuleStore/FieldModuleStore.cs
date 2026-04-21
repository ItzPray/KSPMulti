using LmpClient.Extensions;
using LmpClient.ModuleStore.Structures;
using LmpClient.Utilities;
using LmpCommon.Xml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using UnityEngine;

namespace LmpClient.ModuleStore
{
    /// <summary>
    /// This storage class stores all the fields that have the "ispersistent" as true. And also the customizations to the part modules
    /// When we run trough all the part modules looking for changes we will get the fields to check from this class.
    /// Also we will use the customization methods to act accordingly
    /// </summary>
    public class FieldModuleStore
    {
        private static readonly string CustomPartSyncFolder = CommonUtil.CombinePaths(MainSystem.KspPath, "GameData", "LunaMultiplayer", "PartSync");

        /// <summary>
        /// Here we store our customized part modules behaviors
        /// </summary>
        public static Dictionary<string, ModuleDefinition> CustomizedModuleBehaviours = new Dictionary<string, ModuleDefinition>();

        /// <summary>
        /// Here we store all the types that inherit from PartModule including the mod files
        /// </summary>
        private static IEnumerable<Type> PartModuleTypes { get; } = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetLoadableTypes())
            .Where(t => t.IsClass && t.IsSubclassOf(typeof(PartModule)));

        /// <summary>
        /// Reads the module customizations xml.
        /// Basically it fills up the CustomizedModuleBehaviours dictionary
        /// </summary>
        public static void ReadCustomizationXml()
        {
            var moduleValues = new List<ModuleDefinition>();

            if (!Directory.Exists(CustomPartSyncFolder))
            {
                LunaLog.LogWarning($"[LMP]: PartSync folder missing; skipping module customizations: {CustomPartSyncFolder}");
                CustomizedModuleBehaviours = new Dictionary<string, ModuleDefinition>();
                return;
            }

            foreach (var file in Directory.GetFiles(CustomPartSyncFolder, "*.xml", SearchOption.AllDirectories))
            {
                // Ship layout is GameData/LunaMultiplayer/PartSync/<ModName>/*.xml (see BuildOnly / ModuleStore/XML).
                // Some installs accidentally copy UI localization trees under PartSync (e.g. PartSync/Localization/...).
                // Those files are not ModuleDefinition XML and must never be fed to the PartSync deserializer.
                if (IsNonPartSyncCustomizationPath(file))
                {
                    continue;
                }

                if (!LooksLikeModuleDefinitionXml(file))
                {
                    LunaLog.LogWarning(
                        $"[LMP]: Skipping non-PartSync XML under PartSync (expected root <ModuleDefinition>): {file}");
                    continue;
                }

                ModuleDefinition moduleDefinition;
                try
                {
                    moduleDefinition = LunaXmlSerializer.ReadXmlFromPath<ModuleDefinition>(file);
                }
                catch (Exception ex)
                {
                    LunaLog.LogWarning($"[LMP]: Skipping unreadable PartSync XML {file}: {ex.Message}");
                    continue;
                }

                if (moduleDefinition == null)
                {
                    continue;
                }

                moduleDefinition.ModuleName = Path.GetFileNameWithoutExtension(file);

                moduleValues.Add(moduleDefinition);
            }

            CustomizedModuleBehaviours = moduleValues.ToDictionary(m => m.ModuleName, v => v);

            var newChildModulesToAdd = new List<ModuleDefinition>();
            foreach (var value in CustomizedModuleBehaviours.Values)
            {
                var moduleClass = PartModuleTypes.FirstOrDefault(t => t.Name == value.ModuleName);
                if (moduleClass != null)
                {
                    AddParentsCustomizations(value, moduleClass);
                    newChildModulesToAdd.AddRange(GetChildCustomizations(value, moduleClass));
                }
            }

            foreach (var moduleToAdd in newChildModulesToAdd)
            {
                if (!CustomizedModuleBehaviours.ContainsKey(moduleToAdd.ModuleName))
                    CustomizedModuleBehaviours.Add(moduleToAdd.ModuleName, moduleToAdd);
            }

            foreach (var module in CustomizedModuleBehaviours.Values)
                module.Init();
        }

        private static bool IsNonPartSyncCustomizationPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
            {
                return true;
            }

            // Normalized separators so we catch both Windows and odd mixes.
            var normalized = fullPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            foreach (var segment in new[] { $"{Path.DirectorySeparatorChar}Localization{Path.DirectorySeparatorChar}" })
            {
                if (normalized.IndexOf(segment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Cheap root-element probe so we never deserialize unrelated XML (localization, hand-edited junk)
        /// as <see cref="ModuleDefinition"/>.
        /// </summary>
        private static bool LooksLikeModuleDefinitionXml(string path)
        {
            try
            {
                using (var reader = XmlReader.Create(path, new XmlReaderSettings { CloseInput = true }))
                {
                    while (reader.Read())
                    {
                        if (reader.NodeType != XmlNodeType.Element)
                        {
                            continue;
                        }

                        return string.Equals(reader.LocalName, "ModuleDefinition", StringComparison.Ordinal);
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        /// <summary>
        /// Here we return the UNEXISTING CHILD customizations of this moduleDefinition.
        /// Example: ModuleDeployableAntenna inherits from ModuleDeployablePart.
        /// But we don't have ANY ModuleDeployableAntenna customization XML.
        /// Here we return a NEW the ModuleDeployableAntenna XML with the customizations of ModuleDeployablePart
        /// </summary>
        private static List<ModuleDefinition> GetChildCustomizations(ModuleDefinition moduleDefinition, Type moduleClass)
        {
            var newPartModules = new List<ModuleDefinition>();
            foreach (var partModuleType in PartModuleTypes.Where(t => t.BaseType == moduleClass))
            {
                if (!CustomizedModuleBehaviours.ContainsKey(partModuleType.Name))
                {
                    newPartModules.Add(new ModuleDefinition
                    {
                        ModuleName = partModuleType.Name,
                        Fields = moduleDefinition.Fields,
                        Methods = moduleDefinition.Methods
                    });
                }
            }

            return newPartModules;
        }

        /// <summary>
        /// Here we add the PARENT customizations to this moduleDefinition.
        /// Example: ModuleDeployableSolarPanel inherits from ModuleDeployablePart.
        /// Here we add the fields and methods of ModuleDeployablePart into the ModuleDeployableSolarPanel customizations
        /// </summary>
        private static void AddParentsCustomizations(ModuleDefinition moduleDefinition, Type moduleClass)
        {
            if (moduleClass.BaseType == null || moduleClass.BaseType == typeof(MonoBehaviour)) return;

            if (CustomizedModuleBehaviours.TryGetValue(moduleClass.BaseType.Name, out var parentModule))
            {
                moduleDefinition.MergeWith(parentModule);
            }

            AddParentsCustomizations(moduleDefinition, moduleClass.BaseType);
        }
    }
}
