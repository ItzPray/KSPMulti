using System;
using LmpCommon.PersistentSync;
using LmpCommon.PersistentSync.Audit.Domains;

namespace LmpCommon.PersistentSync.Audit
{
    public static class PersistentSyncAuditComparer
    {
        public static PersistentSyncAuditComparisonResult Compare(
            string domainId,
            byte[] localBytes,
            int localNumBytes,
            byte[] serverBytes,
            int serverNumBytes,
            long clientKnownRevision,
            long serverRevision,
            string serverError)
        {
            var r = new PersistentSyncAuditComparisonResult
            {
                KnownRevisionMatchesServer = clientKnownRevision == serverRevision
            };

            if (!string.IsNullOrEmpty(serverError))
            {
                PersistentSyncAuditSeverityMapping.SetPrimaryAndSeverity(r, PersistentSyncAuditDifferenceKind.ServerError);
                r.Summary = $"Server error: {serverError}";
                r.Details.Add($"clientKnownRevision={clientKnownRevision} serverRevision={serverRevision} (error path)");
                PersistentSyncAuditSeverityMapping.AddRecord(
                    r,
                    PersistentSyncAuditDifferenceKind.ServerError,
                    "server",
                    serverError,
                    string.Empty);
                return r;
            }

            if (localBytes == null || localNumBytes <= 0)
            {
                PersistentSyncAuditSeverityMapping.SetPrimaryAndSeverity(r, PersistentSyncAuditDifferenceKind.LocalUnavailable);
                r.Summary = "Local audit payload is empty (TrySerializeLocalAuditPayload failed or not implemented)";
                return r;
            }

            if (serverBytes == null || serverNumBytes <= 0)
            {
                PersistentSyncAuditSeverityMapping.SetPrimaryAndSeverity(r, PersistentSyncAuditDifferenceKind.ServerPayloadMissing);
                r.Summary = "Server audit payload is empty";
                return r;
            }

            try
            {
                switch (domainId)
                {
                    case PersistentSyncDomainNames.Funds:
                        return ScalarAuditDomains.CompareFunds(r, localBytes, localNumBytes, serverBytes, serverNumBytes);
                    case PersistentSyncDomainNames.Science:
                        return ScalarAuditDomains.CompareScience(r, localBytes, localNumBytes, serverBytes, serverNumBytes);
                    case PersistentSyncDomainNames.Reputation:
                        return ScalarAuditDomains.CompareReputation(r, localBytes, localNumBytes, serverBytes, serverNumBytes);
                    case PersistentSyncDomainNames.GameLaunchId:
                        return ScalarAuditDomains.CompareGameLaunchId(r, localBytes, localNumBytes, serverBytes, serverNumBytes);
                    case PersistentSyncDomainNames.Achievements:
                        return AchievementsAuditDomain.Compare(r, localBytes, localNumBytes, serverBytes, serverNumBytes);
                    case PersistentSyncDomainNames.Contracts:
                        return ContractsAuditDomain.Compare(r, localBytes, localNumBytes, serverBytes, serverNumBytes);
                    case PersistentSyncDomainNames.Technology:
                        return TechnologyAuditDomain.Compare(r, localBytes, localNumBytes, serverBytes, serverNumBytes);
                    case PersistentSyncDomainNames.Strategy:
                        return StrategyAuditDomain.Compare(r, localBytes, localNumBytes, serverBytes, serverNumBytes);
                    case PersistentSyncDomainNames.UpgradeableFacilities:
                        return FacilitiesAuditDomain.Compare(r, localBytes, localNumBytes, serverBytes, serverNumBytes);
                    case PersistentSyncDomainNames.ScienceSubjects:
                        return ScienceSubjectsAuditDomain.Compare(r, localBytes, localNumBytes, serverBytes, serverNumBytes);
                    case PersistentSyncDomainNames.ExperimentalParts:
                        return ExperimentalPartsAuditDomain.Compare(r, localBytes, localNumBytes, serverBytes, serverNumBytes);
                    case PersistentSyncDomainNames.PartPurchases:
                        return PartPurchasesAuditDomain.Compare(r, localBytes, localNumBytes, serverBytes, serverNumBytes);
                    default:
                        PersistentSyncAuditSeverityMapping.SetPrimaryAndSeverity(r, PersistentSyncAuditDifferenceKind.NoSemanticAdapter);
                        r.Summary = "No semantic comparer for this domain; use raw hash / hex preview";
                        r.Details.Add($"localBytes={localNumBytes} serverBytes={serverNumBytes}");
                        return r;
                }
            }
            catch (Exception ex)
            {
                PersistentSyncAuditSeverityMapping.SetPrimaryAndSeverity(r, PersistentSyncAuditDifferenceKind.DecodeError);
                r.Summary = "Decode/compare failed";
                r.Details.Add(ex.ToString());
                return r;
            }
        }
    }
}
