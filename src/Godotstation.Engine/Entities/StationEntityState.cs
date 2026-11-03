using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Godotstation.Engine.Entities;

[Flags]
public enum StationEntityFlags : byte
{
    None = 0,
    /// <summary>Clients may aim a click at this entity.</summary>
    Targetable = 1,
    /// <summary>A pickupable item; its holder can change hand contents.</summary>
    Item = 2,
    /// <summary>Occupies its tile for movement.</summary>
    Solid = 4,
    /// <summary>A solid entity that players can push.</summary>
    Pushable = 8,
}

/// <summary>
/// One replicated station entity: the identity, placement and small state map
/// that every peer needs to draw it and aim clicks at it. The server stays
/// authoritative; this is the only shape objects take on the wire.
/// </summary>
public sealed class StationEntityState : IEquatable<StationEntityState>
{
    private static readonly IReadOnlyDictionary<string, double> NoFields = new SortedDictionary<string, double>();

    public StationEntityState(int id, string kind, Vector2 position, int? holder, StationEntityFlags flags,
        IReadOnlyDictionary<string, double>? fields = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        Id = id;
        Kind = kind;
        Position = position;
        Holder = holder;
        Flags = flags;
        if (fields == null || fields.Count == 0)
        {
            Fields = NoFields;
            return;
        }
        var sorted = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach ((string key, double value) in fields) sorted[key] = value;
        Fields = sorted;
    }

    public int Id { get; }
    public string Kind { get; }
    public Vector2 Position { get; }
    /// <summary>Peer whose inventory holds this entity, if any.</summary>
    public int? Holder { get; }
    public StationEntityFlags Flags { get; }
    public IReadOnlyDictionary<string, double> Fields { get; }

    public bool IsTargetable => Flags.HasFlag(StationEntityFlags.Targetable);
    public bool IsItem => Flags.HasFlag(StationEntityFlags.Item);
    public bool IsSolid => Flags.HasFlag(StationEntityFlags.Solid);
    public bool IsPushable => Flags.HasFlag(StationEntityFlags.Pushable);

    public double Get(string field) => Fields.TryGetValue(field, out double value) ? value : 0;
    public bool Flag(string field) => Get(field) != 0;
    public int Count(string field) => (int)Get(field);

    public bool Equals(StationEntityState? other) =>
        other != null && Id == other.Id && Kind == other.Kind && Position == other.Position &&
        Holder == other.Holder && Flags == other.Flags && Fields.Count == other.Fields.Count &&
        Fields.All(field => other.Fields.TryGetValue(field.Key, out double value) && value.Equals(field.Value));

    public override bool Equals(object? obj) => Equals(obj as StationEntityState);
    public override int GetHashCode() => HashCode.Combine(Id, Kind, Position, Holder, Flags, Fields.Count);
    public override string ToString() =>
        $"{Kind}#{Id} at {Position} holder={Holder?.ToString() ?? "-"} {Flags} " +
        string.Join(" ", Fields.Select(field => $"{field.Key}={field.Value}"));
}

/// <summary>Compact binary form of entity states, so one RPC carries any set of objects.</summary>
public static class StationEntityStateCodec
{
    private const byte Version = 1;
    private const int MaximumEntities = 4096;
    private const int MaximumFields = 64;

    public static byte[] Encode(IEnumerable<StationEntityState> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        List<StationEntityState> list = states.ToList();
        if (list.Count > MaximumEntities) throw new ArgumentException("Too many entities in one update.", nameof(states));
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Version);
            writer.Write(list.Count);
            foreach (StationEntityState state in list)
            {
                writer.Write(state.Id);
                writer.Write(state.Kind);
                writer.Write(state.Position.X);
                writer.Write(state.Position.Y);
                writer.Write(state.Holder ?? 0);
                writer.Write((byte)state.Flags);
                writer.Write((byte)state.Fields.Count);
                foreach ((string key, double value) in state.Fields)
                {
                    writer.Write(key);
                    writer.Write(value);
                }
            }
        }
        return stream.ToArray();
    }

    /// <summary>Decodes an update, rejecting malformed or oversized payloads instead of throwing.</summary>
    public static bool TryDecode(byte[]? payload, out IReadOnlyList<StationEntityState> states)
    {
        states = Array.Empty<StationEntityState>();
        if (payload == null || payload.Length == 0) return false;
        try
        {
            using var reader = new BinaryReader(new MemoryStream(payload, writable: false), Encoding.UTF8);
            if (reader.ReadByte() != Version) return false;
            int count = reader.ReadInt32();
            if (count is < 0 or > MaximumEntities) return false;
            var decoded = new List<StationEntityState>(count);
            for (int i = 0; i < count; i++)
            {
                int id = reader.ReadInt32();
                string kind = reader.ReadString();
                var position = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                int holder = reader.ReadInt32();
                var flags = (StationEntityFlags)reader.ReadByte();
                int fieldCount = reader.ReadByte();
                if (kind.Length == 0 || fieldCount > MaximumFields ||
                    !float.IsFinite(position.X) || !float.IsFinite(position.Y)) return false;
                var fields = new Dictionary<string, double>(fieldCount, StringComparer.Ordinal);
                for (int f = 0; f < fieldCount; f++)
                {
                    string key = reader.ReadString();
                    double value = reader.ReadDouble();
                    if (!double.IsFinite(value) || !fields.TryAdd(key, value)) return false;
                }
                decoded.Add(new StationEntityState(id, kind, position, holder == 0 ? null : holder, flags, fields));
            }
            if (reader.BaseStream.Position != payload.Length) return false;
            states = decoded;
            return true;
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException or FormatException
                                              or DecoderFallbackException or ArgumentException)
        {
            return false;
        }
    }
}
