using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LmpCommon.PersistentSync
{
    public static class PersistentSyncPayloadSerializer
    {
        private static readonly object CodecLock = new object();
        private static readonly Dictionary<Type, IPersistentSyncPayloadCodec> CustomCodecs =
            new Dictionary<Type, IPersistentSyncPayloadCodec>();
        private static readonly Dictionary<Type, IPersistentSyncPayloadCodec> ConventionCodecs =
            new Dictionary<Type, IPersistentSyncPayloadCodec>();

        public static void RegisterCustom<T>(Func<PersistentSyncPayloadReader, T> read, Action<PersistentSyncPayloadWriter, T> write)
        {
            if (read == null) throw new ArgumentNullException(nameof(read));
            if (write == null) throw new ArgumentNullException(nameof(write));
            lock (CodecLock)
            {
                CustomCodecs[typeof(T)] = new PersistentSyncPayloadCodec<T>(read, write);
            }
        }

        public static byte[] Serialize<T>(T value)
        {
            using (var stream = new MemoryStream())
            using (var writer = new PersistentSyncPayloadWriter(stream))
            {
                GetCodec(typeof(T)).Write(writer, value);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static T Deserialize<T>(byte[] payload, int numBytes)
        {
            using (var stream = new MemoryStream(payload ?? new byte[0], 0, Math.Max(0, numBytes)))
            using (var reader = new PersistentSyncPayloadReader(stream))
            {
                return (T)GetCodec(typeof(T)).Read(reader);
            }
        }

        public static T Deserialize<T>(byte[] payload)
        {
            return Deserialize<T>(payload, payload?.Length ?? 0);
        }

        private static IPersistentSyncPayloadCodec GetCodec(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            lock (CodecLock)
            {
                RegisterBuiltInCustomCodecs();
                if (CustomCodecs.TryGetValue(type, out var customCodec))
                {
                    return customCodec;
                }

                if (!ConventionCodecs.TryGetValue(type, out var conventionCodec))
                {
                    conventionCodec = BuildConventionCodec(type);
                    ConventionCodecs[type] = conventionCodec;
                }

                return conventionCodec;
            }
        }

        private static void RegisterBuiltInCustomCodecs()
        {
            if (CustomCodecs.ContainsKey(typeof(ContractIntentPayload)))
            {
                return;
            }

            RegisterCustom<ContractIntentPayload>(
                PersistentSyncContractPayloadCodec.ReadContractIntentPayload,
                PersistentSyncContractPayloadCodec.WriteContractIntentPayload);
            RegisterCustom<ContractSnapshotPayload>(
                PersistentSyncContractPayloadCodec.ReadContractSnapshotPayload,
                PersistentSyncContractPayloadCodec.WriteContractSnapshotPayload);
            RegisterCustom<ContractMutationPayload>(
                PersistentSyncContractPayloadCodec.ReadContractMutationPayload,
                PersistentSyncContractPayloadCodec.WriteContractMutationPayload);
        }

        private static IPersistentSyncPayloadCodec BuildConventionCodec(Type type)
        {
            return new PersistentSyncPayloadCodec<object>(
                reader => ReadValue(reader, type),
                (writer, value) => WriteValue(writer, type, value));
        }

        private static object ReadValue(PersistentSyncPayloadReader reader, Type type)
        {
            if (type == typeof(string)) return reader.ReadString();
            if (type == typeof(int)) return reader.ReadInt32();
            if (type == typeof(uint)) return reader.ReadUInt32Padded();
            if (type == typeof(float)) return reader.ReadSinglePadded();
            if (type == typeof(double)) return reader.ReadDoublePadded();
            if (type == typeof(bool)) return reader.ReadBoolean();
            if (type == typeof(byte)) return reader.ReadByte();
            if (type == typeof(Guid)) return reader.ReadGuid();
            if (type.IsEnum)
            {
                if (Enum.GetUnderlyingType(type) != typeof(byte))
                {
                    throw new NotSupportedException($"PersistentSync payload enum {type.FullName} must be byte-backed.");
                }

                return Enum.ToObject(type, reader.ReadByte());
            }

            if (type == typeof(byte[]))
            {
                return reader.ReadBytesWithLength(out _);
            }

            if (type.IsArray)
            {
                var elementType = type.GetElementType();
                var count = reader.ReadInt32();
                var array = Array.CreateInstance(elementType, count);
                for (var i = 0; i < count; i++)
                {
                    array.SetValue(ReadValue(reader, elementType), i);
                }

                return array;
            }

            if (TryGetListElementType(type, out var listElementType))
            {
                var count = reader.ReadInt32();
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(listElementType));
                for (var i = 0; i < count; i++)
                {
                    list.Add(ReadValue(reader, listElementType));
                }

                return list;
            }

            var instance = Activator.CreateInstance(type);
            foreach (var member in GetPayloadMembers(type))
            {
                member.SetValue(instance, ReadValue(reader, member.MemberType));
            }

            return instance;
        }

        private static void WriteValue(PersistentSyncPayloadWriter writer, Type type, object value)
        {
            if (type == typeof(string))
            {
                writer.WriteString((string)value);
                return;
            }

            if (type == typeof(int))
            {
                writer.WriteInt32(value != null ? (int)value : 0);
                return;
            }

            if (type == typeof(uint))
            {
                writer.WriteUInt32(value != null ? (uint)value : 0u);
                return;
            }

            if (type == typeof(float))
            {
                writer.WriteSingle(value != null ? (float)value : 0f);
                return;
            }

            if (type == typeof(double))
            {
                writer.WriteDouble(value != null ? (double)value : 0d);
                return;
            }

            if (type == typeof(bool))
            {
                writer.WriteBoolean(value != null && (bool)value);
                return;
            }

            if (type == typeof(byte))
            {
                writer.WriteByte(value != null ? (byte)value : (byte)0);
                return;
            }

            if (type == typeof(Guid))
            {
                writer.WriteGuid(value != null ? (Guid)value : Guid.Empty);
                return;
            }

            if (type.IsEnum)
            {
                if (Enum.GetUnderlyingType(type) != typeof(byte))
                {
                    throw new NotSupportedException($"PersistentSync payload enum {type.FullName} must be byte-backed.");
                }

                writer.WriteByte(value != null ? Convert.ToByte(value) : (byte)0);
                return;
            }

            if (type == typeof(byte[]))
            {
                var bytes = value as byte[];
                writer.WriteBytes(bytes, bytes?.Length ?? 0);
                return;
            }

            if (type.IsArray)
            {
                var elementType = type.GetElementType();
                var array = value as Array;
                writer.WriteInt32(array?.Length ?? 0);
                if (array == null) return;
                foreach (var item in array)
                {
                    WriteValue(writer, elementType, item);
                }

                return;
            }

            if (TryGetListElementType(type, out var listElementType))
            {
                var list = value as IEnumerable;
                if (list == null)
                {
                    writer.WriteInt32(0);
                    return;
                }

                var items = list.Cast<object>().ToArray();
                writer.WriteInt32(items.Length);
                foreach (var item in items)
                {
                    WriteValue(writer, listElementType, item);
                }

                return;
            }

            if (value == null)
            {
                value = Activator.CreateInstance(type);
            }

            foreach (var member in GetPayloadMembers(type))
            {
                WriteValue(writer, member.MemberType, member.GetValue(value));
            }
        }

        private static IReadOnlyList<PayloadMember> GetPayloadMembers(Type type)
        {
            if (type.IsPrimitive || type == typeof(string) || type == typeof(Guid) || type.IsEnum || type.IsArray)
            {
                throw new NotSupportedException($"PersistentSync payload type {type.FullName} is not a record payload.");
            }

            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Where(field => !field.IsInitOnly)
                .Select(field => new PayloadMember(field));
            var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
                .Select(property => new PayloadMember(property));

            var members = fields.Concat(properties)
                .OrderBy(member => member.MetadataToken)
                .ToArray();
            if (members.Length == 0)
            {
                throw new NotSupportedException($"PersistentSync payload type {type.FullName} has no public writable fields or properties.");
            }

            foreach (var member in members)
            {
                ValidateSupportedType(member.MemberType);
            }

            return members;
        }

        private static void ValidateSupportedType(Type type)
        {
            if (type == typeof(string) || type == typeof(int) || type == typeof(uint) ||
                type == typeof(float) || type == typeof(double) || type == typeof(bool) ||
                type == typeof(byte) || type == typeof(Guid) || type == typeof(byte[]))
            {
                return;
            }

            if (type.IsEnum)
            {
                if (Enum.GetUnderlyingType(type) != typeof(byte))
                {
                    throw new NotSupportedException($"PersistentSync payload enum {type.FullName} must be byte-backed.");
                }

                return;
            }

            if (type.IsArray)
            {
                ValidateSupportedType(type.GetElementType());
                return;
            }

            if (TryGetListElementType(type, out var elementType))
            {
                ValidateSupportedType(elementType);
                return;
            }

            if (type.GetConstructor(Type.EmptyTypes) == null)
            {
                throw new NotSupportedException($"PersistentSync payload type {type.FullName} must have a public parameterless constructor.");
            }

            GetPayloadMembers(type);
        }

        private static bool TryGetListElementType(Type type, out Type elementType)
        {
            elementType = null;
            if (!type.IsGenericType)
            {
                return false;
            }

            var definition = type.GetGenericTypeDefinition();
            if (definition != typeof(List<>) && definition != typeof(IList<>) && definition != typeof(IReadOnlyList<>))
            {
                return false;
            }

            elementType = type.GetGenericArguments()[0];
            return true;
        }

        private interface IPersistentSyncPayloadCodec
        {
            object Read(PersistentSyncPayloadReader reader);
            void Write(PersistentSyncPayloadWriter writer, object value);
        }

        private sealed class PersistentSyncPayloadCodec<T> : IPersistentSyncPayloadCodec
        {
            private readonly Func<PersistentSyncPayloadReader, T> _read;
            private readonly Action<PersistentSyncPayloadWriter, T> _write;

            public PersistentSyncPayloadCodec(Func<PersistentSyncPayloadReader, T> read, Action<PersistentSyncPayloadWriter, T> write)
            {
                _read = read;
                _write = write;
            }

            public object Read(PersistentSyncPayloadReader reader) => _read(reader);

            public void Write(PersistentSyncPayloadWriter writer, object value)
            {
                _write(writer, value is T typed ? typed : default);
            }
        }

        private sealed class PayloadMember
        {
            private readonly FieldInfo _field;
            private readonly PropertyInfo _property;

            public PayloadMember(FieldInfo field)
            {
                _field = field;
                MemberType = field.FieldType;
                MetadataToken = field.MetadataToken;
            }

            public PayloadMember(PropertyInfo property)
            {
                _property = property;
                MemberType = property.PropertyType;
                MetadataToken = property.MetadataToken;
            }

            public Type MemberType { get; }
            public int MetadataToken { get; }

            public object GetValue(object instance)
            {
                return _field != null ? _field.GetValue(instance) : _property.GetValue(instance);
            }

            public void SetValue(object instance, object value)
            {
                if (_field != null)
                {
                    _field.SetValue(instance, value);
                }
                else
                {
                    _property.SetValue(instance, value);
                }
            }
        }
    }

    public sealed class PersistentSyncPayloadReader : IDisposable
    {
        private readonly BinaryReader _reader;
        private readonly Stream _stream;

        internal PersistentSyncPayloadReader(Stream stream)
        {
            _stream = stream;
            _reader = new BinaryReader(stream, Encoding.UTF8);
        }

        public int ReadInt32() => _reader.ReadInt32();
        public byte ReadByte() => _reader.ReadByte();
        public bool ReadBoolean() => _reader.ReadBoolean();
        public double ReadDouble() => _reader.ReadDouble();
        public float ReadSingle() => _reader.ReadSingle();
        public uint ReadUInt32() => _reader.ReadUInt32();
        public string ReadString() => _reader.ReadString();
        public Guid ReadGuid() => new Guid(_reader.ReadBytes(16));
        public byte[] ReadBytes(int count) => _reader.ReadBytes(count);

        public byte[] ReadBytesWithLength(out int numBytes)
        {
            numBytes = _reader.ReadInt32();
            return numBytes > 0 ? _reader.ReadBytes(numBytes) : new byte[0];
        }

        public string[] ReadStringArray()
        {
            var count = _reader.ReadInt32();
            var values = new string[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = _reader.ReadString();
            }

            return values;
        }

        public double ReadDoublePadded()
        {
            var bytes = ReadPadded(sizeof(double));
            return BitConverter.ToDouble(bytes, 0);
        }

        public float ReadSinglePadded()
        {
            var bytes = ReadPadded(sizeof(float));
            return BitConverter.ToSingle(bytes, 0);
        }

        public uint ReadUInt32Padded()
        {
            var bytes = ReadPadded(sizeof(uint));
            return BitConverter.ToUInt32(bytes, 0);
        }

        private byte[] ReadPadded(int byteCount)
        {
            var bytes = new byte[byteCount];
            var available = Math.Max(0, Math.Min(byteCount, (int)(_stream.Length - _stream.Position)));
            if (available > 0)
            {
                var read = _stream.Read(bytes, 0, available);
                if (read < available)
                {
                    throw new EndOfStreamException();
                }
            }

            return bytes;
        }

        public void Dispose()
        {
            _reader.Dispose();
        }
    }

    public sealed class PersistentSyncPayloadWriter : IDisposable
    {
        private readonly BinaryWriter _writer;

        internal PersistentSyncPayloadWriter(Stream stream)
        {
            _writer = new BinaryWriter(stream, Encoding.UTF8);
        }

        public void WriteInt32(int value) => _writer.Write(value);
        public void WriteByte(byte value) => _writer.Write(value);
        public void WriteBoolean(bool value) => _writer.Write(value);
        public void WriteDouble(double value) => _writer.Write(value);
        public void WriteSingle(float value) => _writer.Write(value);
        public void WriteUInt32(uint value) => _writer.Write(value);
        public void WriteString(string value) => _writer.Write(value ?? string.Empty);
        public void WriteGuid(Guid value) => _writer.Write(value.ToByteArray());

        public void WriteBytes(byte[] bytes, int numBytes)
        {
            var safeBytes = bytes ?? new byte[0];
            var safeCount = Math.Max(0, Math.Min(numBytes, safeBytes.Length));
            _writer.Write(safeCount);
            if (safeCount > 0)
            {
                _writer.Write(safeBytes, 0, safeCount);
            }
        }

        public void WriteRawBytes(byte[] bytes, int numBytes)
        {
            var safeBytes = bytes ?? new byte[0];
            var safeCount = Math.Max(0, Math.Min(numBytes, safeBytes.Length));
            if (safeCount > 0)
            {
                _writer.Write(safeBytes, 0, safeCount);
            }
        }

        public void WriteStringArray(string[] values)
        {
            var safeValues = values ?? new string[0];
            _writer.Write(safeValues.Length);
            foreach (var value in safeValues)
            {
                _writer.Write(value ?? string.Empty);
            }
        }

        public void Flush() => _writer.Flush();

        public void Dispose()
        {
            _writer.Dispose();
        }
    }

    public sealed class PersistentSyncValueWithReason<T>
    {
        public T Value;
        public string Reason = string.Empty;

        public PersistentSyncValueWithReason()
        {
        }

        public PersistentSyncValueWithReason(T value, string reason)
        {
            Value = value;
            Reason = reason ?? string.Empty;
        }
    }

    public sealed class PersistentSyncStringIntPayload
    {
        public string Text = string.Empty;
        public int Number;

        public PersistentSyncStringIntPayload()
        {
        }

        public PersistentSyncStringIntPayload(string text, int number)
        {
            Text = text ?? string.Empty;
            Number = number;
        }
    }
}
