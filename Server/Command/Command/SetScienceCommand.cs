using LmpCommon.PersistentSync;
using Server.Command.Command.Base;
using Server.Command.Common;
using Server.Log;
using Server.System.PersistentSync;

namespace Server.Command.Command
{
    public class SetScienceCommand : SimpleCommand
    {
        public override bool Execute(string commandArgs)
        {
            CommandSystemHelperMethods.SplitCommandParamArray(commandArgs, out var parameters);
            if (!CheckParameter(parameters)) return false;

            var science = parameters[0];
            if (!float.TryParse(science, out var parsedScience))
            {
                LunaLog.Error("Syntax error. Use valid number as parameter!");
                return false;
            }

            SetScience(parsedScience);
            return true;
        }

        private static void SetScience(float science)
        {
            var reason = "Server Command";
            var payload = PersistentSyncPayloadSerializer.Serialize(new PersistentSyncValueWithReason<float>(science, reason));
            PersistentSyncRegistry.ApplyServerMutation(PersistentSyncDomainId.Science, payload, payload.Length, reason);
            LunaLog.Debug($"Science set to {science} via persistent sync");
        }

        private static bool CheckParameter(string[] parameters)
        {
            if (parameters == null || parameters.Length != 1)
            {
                LunaLog.Error("Syntax error. Use valid number as parameter!");
                return false;
            }

            return true;
        }
    }
}
