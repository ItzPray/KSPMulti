using LunaConfigNode;
using LunaConfigNode.CfgNode;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Server.System.Vessel.Classes
{
    public class Part
    {
        public MixedCollection<string, string> Fields;
        /// <summary>
        /// MODULE entries in vessel file order. Keys repeat for some parts (e.g. multiple FXModuleThrottleEffects on one engine).
        /// Do not use MixedCollection here — duplicate module type names are valid in KSP.
        /// </summary>
        private readonly List<CfgNodeValue<string, ConfigNode>> _modules;

        /// <summary>
        /// RESOURCE entries in file order (names are normally unique per part).
        /// </summary>
        private readonly List<CfgNodeValue<string, ConfigNode>> _resources;

        public ConfigNode Events;
        public ConfigNode Actions;
        public ConfigNode Effects;
        public ConfigNode Partdata;
        public ConfigNode VesselNaming;

        public Part(ConfigNode cfgNode)
        {
            Fields = new MixedCollection<string, string>(cfgNode.GetAllValues());
            _modules = cfgNode.GetNodes("MODULE")
                .Select(m => new CfgNodeValue<string, ConfigNode>(m.Value.GetValue("name").Value, m.Value))
                .ToList();
            _resources = cfgNode.GetNodes("RESOURCE")
                .Select(m => new CfgNodeValue<string, ConfigNode>(m.Value.GetValue("name").Value, m.Value))
                .ToList();

            Events = cfgNode.GetNode("EVENTS")?.Value;
            Actions = cfgNode.GetNode("ACTIONS")?.Value;
            Effects = cfgNode.GetNode("EFFECTS")?.Value;
            Partdata = cfgNode.GetNode("PARTDATA")?.Value;
            VesselNaming = cfgNode.GetNode("VESSELNAMING")?.Value;
        }

        /// <summary>
        /// All MODULE nodes of the given module type name, in vessel order.
        /// </summary>
        public IEnumerable<ConfigNode> GetModulesNamed(string moduleTypeName)
        {
            foreach (var module in _modules)
            {
                if (string.Equals(module.Key, moduleTypeName, StringComparison.Ordinal))
                {
                    yield return module.Value;
                }
            }
        }

        /// <summary>
        /// Returns the first MODULE of the given type when several exist (see <see cref="GetModulesNamed"/>).
        /// </summary>
        public ConfigNode GetSingleModule(string moduleName)
        {
            ConfigNode first = null;
            foreach (var module in _modules)
            {
                if (!string.Equals(module.Key, moduleName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (first != null)
                {
                    return first;
                }

                first = module.Value;
            }

            return first;
        }

        /// <summary>
        /// First RESOURCE node matching <paramref name="resourceName"/>, or null.
        /// </summary>
        public ConfigNode GetResourceNode(string resourceName)
        {
            foreach (var resource in _resources)
            {
                if (string.Equals(resource.Key, resourceName, StringComparison.Ordinal))
                {
                    return resource.Value;
                }
            }

            return null;
        }

        public override string ToString()
        {
            var builder = new StringBuilder();

            CfgNodeWriter.InitializeNode("PART", 1, builder);

            CfgNodeWriter.WriteValues(Fields.GetAll(), 1, builder);

            if (Events != null) builder.AppendLine(CfgNodeWriter.WriteConfigNode(Events));
            if (Actions != null) builder.AppendLine(CfgNodeWriter.WriteConfigNode(Actions));
            if (Effects != null) builder.AppendLine(CfgNodeWriter.WriteConfigNode(Effects));
            if (Partdata != null) builder.AppendLine(CfgNodeWriter.WriteConfigNode(Partdata));

            foreach (var module in _modules)
            {
                builder.AppendLine(CfgNodeWriter.WriteConfigNode(module.Value));
            }

            foreach (var resource in _resources)
            {
                builder.AppendLine(CfgNodeWriter.WriteConfigNode(resource.Value));
            }

            if (VesselNaming != null) builder.AppendLine(CfgNodeWriter.WriteConfigNode(VesselNaming));

            CfgNodeWriter.FinishNode(1, builder);

            return builder.ToString();
        }
    }
}
