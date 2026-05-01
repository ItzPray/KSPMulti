using System;

namespace LmpCommon.PersistentSync
{
    /// <summary>Domain registration key: human-readable name plus optional legacy caller-supplied wire id metadata.</summary>
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
    /// Fluent helper for tests and explicit registrations. Normal domains use inferred names via RegisterCurrent.
    /// </summary>
}
