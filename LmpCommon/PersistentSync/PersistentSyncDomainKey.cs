using System;

namespace LmpCommon.PersistentSync
{
    /// <summary>Stable domain registration key. Runtime wire ids live only on PersistentSyncDomainDefinition.</summary>
    public struct PersistentSyncDomainKey : IEquatable<PersistentSyncDomainKey>
    {
        public PersistentSyncDomainKey(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Domain name is required.", nameof(name));
            }

            Name = name;
        }

        public string Name { get; }
        public bool Equals(PersistentSyncDomainKey other)
        {
            return string.Equals(Name, other.Name, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is PersistentSyncDomainKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Name != null ? StringComparer.Ordinal.GetHashCode(Name) : 0;
        }

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// Fluent helper for tests and explicit registrations. Normal domains use inferred names via RegisterCurrent.
    /// </summary>
}
