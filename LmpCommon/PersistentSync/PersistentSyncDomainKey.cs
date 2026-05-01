using System;

namespace LmpCommon.PersistentSync
{
    /// <summary>
    /// Stable identity for a persistent sync domain: human-readable <see cref="Name"/> plus legacy <see cref="WireId"/> backing <see cref="PersistentSyncDomainId"/>.
    /// </summary>
    public struct PersistentSyncDomainKey : IEquatable<PersistentSyncDomainKey>
    {
        public PersistentSyncDomainKey(string name, ushort wireId)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Domain name is required.", nameof(name));
            }

            Name = name;
            WireId = wireId;
        }

        public string Name { get; }
        public ushort WireId { get; }
        public PersistentSyncDomainId LegacyId => (PersistentSyncDomainId)WireId;

        public bool Equals(PersistentSyncDomainKey other)
        {
            return WireId == other.WireId && string.Equals(Name, other.Name, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is PersistentSyncDomainKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Name != null ? StringComparer.Ordinal.GetHashCode(Name) : 0) * 397) ^ WireId.GetHashCode();
            }
        }

        public override string ToString()
        {
            return $"{Name}({WireId})";
        }
    }

    /// <summary>
    /// Fluent helper for declaring <see cref="PersistentSyncDomainKey"/> literals used by registrar DSL and tests.
    /// </summary>
    public static class PersistentSyncDomain
    {
        public static PersistentSyncDomainKey Define(string name, ushort wireId)
        {
            return new PersistentSyncDomainKey(name, wireId);
        }
    }
}
