using System;
using System.Globalization;
using LmpCommon.PersistentSync;

namespace LmpCommon.PersistentSync.Audit.Domains
{
    internal static class ScalarAuditDomains
    {
        private const double DoubleTolerance = 1e-6;
        private const float FloatTolerance = 1e-4f;

        public static PersistentSyncAuditComparisonResult CompareFunds(
            PersistentSyncAuditComparisonResult r,
            byte[] localBytes,
            int localNumBytes,
            byte[] serverBytes,
            int serverNumBytes)
        {
            var local = PersistentSyncPayloadSerializer.Deserialize<double>(localBytes, localNumBytes);
            var server = PersistentSyncPayloadSerializer.Deserialize<double>(serverBytes, serverNumBytes);
            return CompareScalar(r, local, server, "funds", (a, b) => Math.Abs(a - b) < DoubleTolerance);
        }

        public static PersistentSyncAuditComparisonResult CompareScience(
            PersistentSyncAuditComparisonResult r,
            byte[] localBytes,
            int localNumBytes,
            byte[] serverBytes,
            int serverNumBytes)
        {
            var local = PersistentSyncPayloadSerializer.Deserialize<float>(localBytes, localNumBytes);
            var server = PersistentSyncPayloadSerializer.Deserialize<float>(serverBytes, serverNumBytes);
            return CompareScalar(r, local, server, "science", (a, b) => Math.Abs(a - b) < FloatTolerance);
        }

        public static PersistentSyncAuditComparisonResult CompareReputation(
            PersistentSyncAuditComparisonResult r,
            byte[] localBytes,
            int localNumBytes,
            byte[] serverBytes,
            int serverNumBytes)
        {
            var local = PersistentSyncPayloadSerializer.Deserialize<float>(localBytes, localNumBytes);
            var server = PersistentSyncPayloadSerializer.Deserialize<float>(serverBytes, serverNumBytes);
            return CompareScalar(r, local, server, "reputation", (a, b) => Math.Abs(a - b) < FloatTolerance);
        }

        public static PersistentSyncAuditComparisonResult CompareGameLaunchId(
            PersistentSyncAuditComparisonResult r,
            byte[] localBytes,
            int localNumBytes,
            byte[] serverBytes,
            int serverNumBytes)
        {
            var local = PersistentSyncPayloadSerializer.Deserialize<uint>(localBytes, localNumBytes);
            var server = PersistentSyncPayloadSerializer.Deserialize<uint>(serverBytes, serverNumBytes);
            return CompareScalar(r, local, server, "launchID", (a, b) => a == b);
        }

        private static PersistentSyncAuditComparisonResult CompareScalar<T>(
            PersistentSyncAuditComparisonResult r,
            T local,
            T server,
            string label,
            Func<T, T, bool> ok)
        {
            if (ok(local, server))
            {
                r.PrimaryKind = PersistentSyncAuditDifferenceKind.Ok;
                r.Summary = $"Match: {label} local={local} server={server}";
            }
            else
            {
                r.PrimaryKind = PersistentSyncAuditDifferenceKind.ValueMismatch;
                r.Summary = $"Mismatch: {label} local={local} server={server}";
                PersistentSyncAuditSeverityMapping.AddRecord(
                    r,
                    PersistentSyncAuditDifferenceKind.ValueMismatch,
                    label,
                    Convert.ToString(local, CultureInfo.InvariantCulture),
                    Convert.ToString(server, CultureInfo.InvariantCulture));
            }

            if (!r.KnownRevisionMatchesServer)
            {
                r.Details.Add("Note: clientKnownRevision != serverRevision (expected when you have not applied the latest snapshot yet).");
            }

            PersistentSyncAuditSeverityMapping.ApplyRevisionMismatchIfPayloadMatched(r);
            return r;
        }
    }
}
