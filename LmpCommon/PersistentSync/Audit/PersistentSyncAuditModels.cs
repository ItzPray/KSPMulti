using System;
using System.Collections.Generic;

namespace LmpCommon.PersistentSync.Audit
{
    public enum PersistentSyncAuditSeverity
    {
        Ok,
        Warning,
        Error
    }

    /// <summary>Domain audit outcome taxonomy (structured diff).</summary>
    public enum PersistentSyncAuditDifferenceKind
    {
        Ok,
        MissingOnServer,
        MissingOnClient,
        ValueMismatch,
        RevisionMismatch,
        DecodeError,
        LocalUnavailable,
        ServerError,
        /// <summary>Server payload bytes missing (distinct from <see cref="ServerError"/> string from registry).</summary>
        ServerPayloadMissing,
        /// <summary>No semantic comparer registered for this domain id.</summary>
        NoSemanticAdapter
    }

    /// <summary>Single keyed difference row for UI and clipboard bundles.</summary>
    public sealed class PersistentSyncAuditDiffRecord
    {
        public PersistentSyncAuditDifferenceKind Kind { get; set; }

        public string Key { get; set; }

        public string Local { get; set; }

        public string Server { get; set; }
    }

    /// <summary>Caps for optional semantic diagnostics (bundles / IMGUI).</summary>
    public static class PersistentSyncAuditSemanticLimits
    {
        /// <summary>Max lines appended to <see cref="PersistentSyncAuditComparisonResult.SemanticDiagnostics"/> per comparison.</summary>
        public const int MaxSemanticDiagnosticLines = 64;
    }

    /// <summary>Structured outcome from comparing local audit bytes vs server audit snapshot bytes.</summary>
    public sealed class PersistentSyncAuditComparisonResult
    {
        /// <summary>Dominant classification for this domain row.</summary>
        public PersistentSyncAuditDifferenceKind PrimaryKind { get; set; } = PersistentSyncAuditDifferenceKind.Ok;

        public PersistentSyncAuditSeverity Severity { get; set; }

        /// <summary>One-line human readable headline.</summary>
        public string Summary { get; set; }

        public List<string> Details { get; } = new List<string>();

        public List<PersistentSyncAuditDiffRecord> Records { get; } = new List<PersistentSyncAuditDiffRecord>();

        /// <summary>
        /// Optional cfg-text semantic notes (e.g. achievements watch-key comparison). Does not change
        /// <see cref="PrimaryKind"/>; capped by <see cref="PersistentSyncAuditSemanticLimits.MaxSemanticDiagnosticLines"/>.
        /// </summary>
        public List<string> SemanticDiagnostics { get; } = new List<string>();

        public bool KnownRevisionMatchesServer { get; set; } = true;
    }

    /// <summary>Maps <see cref="PersistentSyncAuditDifferenceKind"/> to legacy severity for UI chips.</summary>
    public static class PersistentSyncAuditSeverityMapping
    {
        /// <summary>
        /// Ok → Ok; RevisionMismatch → Warning (stale revision but payload may match); NoSemanticAdapter → Warning;
        /// all other non-Ok kinds → Error.
        /// </summary>
        public static PersistentSyncAuditSeverity FromPrimaryKind(PersistentSyncAuditDifferenceKind kind)
        {
            switch (kind)
            {
                case PersistentSyncAuditDifferenceKind.Ok:
                    return PersistentSyncAuditSeverity.Ok;
                case PersistentSyncAuditDifferenceKind.RevisionMismatch:
                case PersistentSyncAuditDifferenceKind.NoSemanticAdapter:
                    return PersistentSyncAuditSeverity.Warning;
                default:
                    return PersistentSyncAuditSeverity.Error;
            }
        }

        /// <summary>
        /// After semantic comparison: if payload semantics are Ok but client revision ≠ server revision, classify as
        /// <see cref="PersistentSyncAuditDifferenceKind.RevisionMismatch"/> (warning).
        /// </summary>
        public static void ApplyRevisionMismatchIfPayloadMatched(PersistentSyncAuditComparisonResult r)
        {
            if (r.PrimaryKind == PersistentSyncAuditDifferenceKind.Ok && !r.KnownRevisionMatchesServer)
            {
                r.PrimaryKind = PersistentSyncAuditDifferenceKind.RevisionMismatch;
                r.Details.Add("Note: clientKnownRevision != serverRevision while payload compared equal.");
            }

            r.Severity = FromPrimaryKind(r.PrimaryKind);
        }

        public static void SetPrimaryAndSeverity(PersistentSyncAuditComparisonResult r, PersistentSyncAuditDifferenceKind kind)
        {
            r.PrimaryKind = kind;
            r.Severity = FromPrimaryKind(kind);
        }

        public static void AddRecord(
            PersistentSyncAuditComparisonResult r,
            PersistentSyncAuditDifferenceKind kind,
            string key,
            string local,
            string server)
        {
            r.Records.Add(new PersistentSyncAuditDiffRecord
            {
                Kind = kind,
                Key = key ?? string.Empty,
                Local = local ?? string.Empty,
                Server = server ?? string.Empty
            });
        }
    }
}
