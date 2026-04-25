using LmpCommon.Enums;
using System;
using System.Reflection;

namespace LmpCommon
{
    /// <summary>
    /// Canonical session admission contract: exact protocol/fork identifier plus exact build string.
    /// Used by server handshake, client discovery filtering, and master-server registration metadata.
    /// </summary>
    public static class SessionAdmission
    {
        /// <summary>
        /// Stable identifier for this custom multiplayer fork family. Must match between client and server.
        /// </summary>
        public const string LocalProtocolForkId = "KSPMP.KSPMulti";

        /// <summary>
        /// Exact build identity for this assembly (informational version when present, else full assembly version).
        /// </summary>
        public static readonly string LocalExactBuild = ComputeLocalExactBuild();

        private static string ComputeLocalExactBuild()
        {
            var asm = typeof(SessionAdmission).Assembly;
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (!string.IsNullOrWhiteSpace(info?.InformationalVersion))
            {
                return info.InformationalVersion.Trim();
            }

            var v = asm.GetName().Version ?? new Version(0, 0, 0, 0);
            return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return value.Trim();
        }

        /// <summary>
        /// Validates a client handshake against this process's fork and build (server-side).
        /// </summary>
        public static bool TryValidateClientHandshake(string clientProtocolForkId, string clientExactBuild, out string reason, out HandshakeReply reply)
        {
            var fork = Normalize(clientProtocolForkId);
            var build = Normalize(clientExactBuild);

            if (string.IsNullOrEmpty(fork))
            {
                reason = "Client did not send a protocol/fork identifier.";
                reply = HandshakeReply.ProtocolForkMismatch;
                return false;
            }

            if (string.IsNullOrEmpty(build))
            {
                reason = "Client did not send an exact build identity.";
                reply = HandshakeReply.ProtocolBuildMismatch;
                return false;
            }

            if (!string.Equals(fork, LocalProtocolForkId, StringComparison.Ordinal))
            {
                reason = $"Protocol/fork mismatch: server requires '{LocalProtocolForkId}', client sent '{fork}'.";
                reply = HandshakeReply.ProtocolForkMismatch;
                return false;
            }

            if (!string.Equals(build, LocalExactBuild, StringComparison.Ordinal))
            {
                reason = $"Exact build mismatch: server requires '{LocalExactBuild}', client sent '{build}'.";
                reply = HandshakeReply.ProtocolBuildMismatch;
                return false;
            }

            reason = string.Empty;
            reply = HandshakeReply.HandshookSuccessfully;
            return true;
        }

        /// <summary>
        /// Discovery filter: true when advertised metadata matches this client's fork and exact build.
        /// Missing metadata is treated as incompatible (do not show in list).
        /// </summary>
        public static bool IsAdvertisedServerCompatible(string advertisedProtocolForkId, string advertisedExactBuild)
        {
            return TryValidateClientHandshake(advertisedProtocolForkId, advertisedExactBuild, out _, out _);
        }

        /// <summary>
        /// User-facing disconnect line for handshake failures (client UI / logs).
        /// </summary>
        public static string FormatClientDisconnectMessage(HandshakeReply reply, string detailReason)
        {
            switch (reply)
            {
                case HandshakeReply.ProtocolForkMismatch:
                    return $"[KSPMP] Protocol mismatch. {detailReason}";
                case HandshakeReply.ProtocolBuildMismatch:
                    return $"[KSPMP] Incompatible client version. {detailReason}";
                default:
                    return $"[KSPMP] Handshake failure: {detailReason}";
            }
        }
    }
}
