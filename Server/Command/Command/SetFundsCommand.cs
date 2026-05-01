using LmpCommon.PersistentSync;
using Server.Command.Command.Base;
using Server.Command.Common;
using Server.Log;
using Server.System.PersistentSync;

namespace Server.Command.Command
{
    public class SetFundsCommand : SimpleCommand
    {
        public override bool Execute(string commandArgs)
        {
            CommandSystemHelperMethods.SplitCommandParamArray(commandArgs, out var parameters);
            if (!CheckParameter(parameters)) return false;

            var funds = parameters[0];
            if (!double.TryParse(funds, out var parsedFunds))
            {
                LunaLog.Error("Syntax error. Use valid number as parameter!");
                return false;
            }

            SetFunds(parsedFunds);
            return true;
        }

        private static void SetFunds(double funds)
        {
            var reason = "Server Command";
            var payload = PersistentSyncPayloadSerializer.Serialize(new PersistentSyncValueWithReason<double>(funds, reason));
            PersistentSyncRegistry.ApplyServerMutation(PersistentSyncDomainId.Funds, payload, payload.Length, reason);
            LunaLog.Debug($"Funds set to {funds} via persistent sync");
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
