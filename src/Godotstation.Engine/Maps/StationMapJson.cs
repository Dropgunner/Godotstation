using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godotstation.Engine.Entities;

namespace Godotstation.Engine.Maps;

/// <summary>
/// The on-disk and on-the-wire form of a <see cref="StationMap"/>: a small JSON document
/// whose tile rows read as ASCII art (<c>#</c> wall, <c>.</c> floor) so community maps can be
/// diffed and hand-edited. The same text is what the host sends to a joining client.
/// </summary>
public static class StationMapJson
{
    public const char WallChar = '#';
    public const char FloorChar = '.';
    public const int MaximumDocumentBytes = 4 * 1024 * 1024;

    public static string Serialize(StationMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("format", StationMap.FormatVersion);
            writer.WriteString("name", map.Name);
            writer.WriteString("author", map.Author);
            writer.WriteString("description", map.Description);

            writer.WriteStartObject("bounds");
            writer.WriteNumber("minX", map.Bounds.MinX);
            writer.WriteNumber("minY", map.Bounds.MinY);
            writer.WriteNumber("maxX", map.Bounds.MaxX);
            writer.WriteNumber("maxY", map.Bounds.MaxY);
            writer.WriteEndObject();

            writer.WriteStartArray("tiles");
            var row = new StringBuilder(map.Bounds.Width);
            for (int y = map.Bounds.MinY; y <= map.Bounds.MaxY; y++)
            {
                row.Clear();
                for (int x = map.Bounds.MinX; x <= map.Bounds.MaxX; x++)
                    row.Append(map.IsWall(x, y) ? WallChar : FloorChar);
                writer.WriteStringValue(row.ToString());
            }
            writer.WriteEndArray();

            writer.WriteStartArray("spawns");
            foreach (Vector2 spawn in map.Spawns)
            {
                (int x, int y) = StationMap.ToTile(spawn);
                writer.WriteStartArray();
                writer.WriteNumberValue(x);
                writer.WriteNumberValue(y);
                writer.WriteEndArray();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("rooms");
            foreach (StationMapRoom room in map.Rooms)
            {
                writer.WriteStartObject();
                writer.WriteString("name", room.Name);
                writer.WriteNumber("minX", room.MinX);
                writer.WriteNumber("minY", room.MinY);
                writer.WriteNumber("maxX", room.MaxX);
                writer.WriteNumber("maxY", room.MaxY);
                writer.WriteString("floorColor", room.FloorColor);
                writer.WriteString("labelColor", room.LabelColor);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("entities");
            foreach (StationEntitySpawn entity in map.Entities)
            {
                (int x, int y) = StationMap.ToTile(entity.Position);
                writer.WriteStartObject();
                writer.WriteNumber("id", entity.Id);
                writer.WriteString("kind", entity.Kind);
                writer.WriteNumber("x", x);
                writer.WriteNumber("y", y);
                if (entity.Amount != 0) writer.WriteNumber("amount", entity.Amount);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        string text = Encoding.UTF8.GetString(stream.ToArray());
        return CompactPairs.Replace(text, "[$1, $2]") + "\n";
    }

    /// <summary>Folds the writer's one-number-per-line spawn pairs back into <c>[x, y]</c> so files stay hand-editable.</summary>
    private static readonly Regex CompactPairs = new(@"\[\s*(-?\d+),\s*(-?\d+)\s*\]", RegexOptions.Compiled);

    /// <summary>Parses a map document, throwing <see cref="FormatException"/> with the first problem found.</summary>
    public static StationMap Parse(string json)
    {
        if (!TryParse(json, out StationMap? map, out string error)) throw new FormatException(error);
        return map;
    }

    /// <summary>Parses untrusted text into a map without throwing; <paramref name="error"/> explains a rejection.</summary>
    public static bool TryParse(string? json, out StationMap map, out string error)
    {
        map = null!;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(json)) return Fail("The map document is empty.", out error);
        if (Encoding.UTF8.GetByteCount(json) > MaximumDocumentBytes) return Fail("The map document is too large.", out error);
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 8,
            });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Fail("The map document must be a JSON object.", out error);

            if (!TryReadInt(root, "format", out int format) || format != StationMap.FormatVersion)
                return Fail($"Unsupported map format; expected \"format\": {StationMap.FormatVersion}.", out error);
            if (!root.TryGetProperty("bounds", out JsonElement boundsElement) || boundsElement.ValueKind != JsonValueKind.Object ||
                !TryReadInt(boundsElement, "minX", out int minX) || !TryReadInt(boundsElement, "minY", out int minY) ||
                !TryReadInt(boundsElement, "maxX", out int maxX) || !TryReadInt(boundsElement, "maxY", out int maxY))
                return Fail("\"bounds\" must contain integer minX, minY, maxX and maxY.", out error);
            var bounds = new StationMapBounds(minX, minY, maxX, maxY);
            if (!bounds.IsValidSize)
                return Fail($"Map sides must be between {StationMap.MinimumSide} and {StationMap.MaximumSide} tiles.", out error);

            var result = new StationMap(bounds)
            {
                Name = ReadString(root, "name", "Untitled Station"),
                Author = ReadString(root, "author", string.Empty),
                Description = ReadString(root, "description", string.Empty),
            };
            if (result.Name.Length > StationMap.MaximumNameLength)
                return Fail($"The map name may not exceed {StationMap.MaximumNameLength} characters.", out error);

            if (!root.TryGetProperty("tiles", out JsonElement tiles) || tiles.ValueKind != JsonValueKind.Array ||
                tiles.GetArrayLength() != bounds.Height)
                return Fail($"\"tiles\" must hold exactly {bounds.Height} rows.", out error);
            int rowIndex = 0;
            foreach (JsonElement rowElement in tiles.EnumerateArray())
            {
                string? row = rowElement.ValueKind == JsonValueKind.String ? rowElement.GetString() : null;
                if (row == null || row.Length != bounds.Width)
                    return Fail($"Tile row {rowIndex} must be a string of {bounds.Width} characters.", out error);
                for (int column = 0; column < row.Length; column++)
                {
                    char tile = row[column];
                    if (tile == WallChar) result.SetWall(bounds.MinX + column, bounds.MinY + rowIndex);
                    else if (tile != FloorChar)
                        return Fail($"Tile row {rowIndex} has unknown tile '{tile}' at column {column}; use '{WallChar}' or '{FloorChar}'.", out error);
                }
                rowIndex++;
            }

            if (root.TryGetProperty("spawns", out JsonElement spawns))
            {
                if (spawns.ValueKind != JsonValueKind.Array) return Fail("\"spawns\" must be an array of [x, y] pairs.", out error);
                foreach (JsonElement spawn in spawns.EnumerateArray())
                {
                    if (!TryReadPair(spawn, out int x, out int y)) return Fail("Each spawn must be an [x, y] integer pair.", out error);
                    if (!result.AddSpawn(x, y)) return Fail($"Spawn ({x}, {y}) is outside the map or duplicated.", out error);
                }
            }

            if (root.TryGetProperty("rooms", out JsonElement rooms))
            {
                if (rooms.ValueKind != JsonValueKind.Array) return Fail("\"rooms\" must be an array.", out error);
                foreach (JsonElement room in rooms.EnumerateArray())
                {
                    if (room.ValueKind != JsonValueKind.Object ||
                        !TryReadInt(room, "minX", out int roomMinX) || !TryReadInt(room, "minY", out int roomMinY) ||
                        !TryReadInt(room, "maxX", out int roomMaxX) || !TryReadInt(room, "maxY", out int roomMaxY))
                        return Fail("Each room needs integer minX, minY, maxX and maxY.", out error);
                    string floor = ReadString(room, "floorColor", StationMaps.DefaultFloorColor);
                    string label = ReadString(room, "labelColor", StationMaps.DefaultLabelColor);
                    if (!IsHexColor(floor) || !IsHexColor(label))
                        return Fail("Room colors must be #rrggbb hex strings.", out error);
                    string name = ReadString(room, "name", string.Empty);
                    if (name.Length > StationMap.MaximumNameLength)
                        return Fail($"Room names may not exceed {StationMap.MaximumNameLength} characters.", out error);
                    if (!result.AddRoom(new StationMapRoom(name, roomMinX, roomMinY, roomMaxX, roomMaxY, floor, label)))
                        return Fail($"Room \"{name}\" lies entirely outside the map.", out error);
                }
            }

            if (root.TryGetProperty("entities", out JsonElement entities))
            {
                if (entities.ValueKind != JsonValueKind.Array) return Fail("\"entities\" must be an array.", out error);
                foreach (JsonElement entity in entities.EnumerateArray())
                {
                    if (entity.ValueKind != JsonValueKind.Object || !TryReadInt(entity, "id", out int id) ||
                        !TryReadInt(entity, "x", out int x) || !TryReadInt(entity, "y", out int y))
                        return Fail("Each entity needs an integer id, x and y.", out error);
                    string kind = ReadString(entity, "kind", string.Empty);
                    if (string.IsNullOrWhiteSpace(kind) || kind.Length > StationMap.MaximumNameLength)
                        return Fail($"Entity {id} needs a non-empty \"kind\".", out error);
                    int amount = TryReadInt(entity, "amount", out int parsedAmount) ? parsedAmount : 0;
                    if (result.PlaceEntity(kind, x, y, amount, id) == null)
                        return Fail($"Entity {id} ({kind}) is outside the map or reuses an id.", out error);
                }
            }

            map = result;
            return true;
        }
        catch (JsonException exception)
        {
            return Fail($"The map document is not valid JSON: {exception.Message}", out error);
        }
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }

    private static bool TryReadInt(JsonElement element, string property, out int value)
    {
        value = 0;
        return element.TryGetProperty(property, out JsonElement found) &&
               found.ValueKind == JsonValueKind.Number && found.TryGetInt32(out value);
    }

    private static bool TryReadPair(JsonElement element, out int x, out int y)
    {
        x = y = 0;
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != 2) return false;
        JsonElement first = element[0], second = element[1];
        return first.ValueKind == JsonValueKind.Number && first.TryGetInt32(out x) &&
               second.ValueKind == JsonValueKind.Number && second.TryGetInt32(out y);
    }

    private static string ReadString(JsonElement element, string property, string fallback) =>
        element.TryGetProperty(property, out JsonElement found) && found.ValueKind == JsonValueKind.String
            ? found.GetString() ?? fallback
            : fallback;

    public static bool IsHexColor(string? value)
    {
        if (value == null || value.Length != 7 || value[0] != '#') return false;
        return int.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
    }
}
