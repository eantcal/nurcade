using System.Text;
using System.Text.Json;

namespace NuRcade.Editor.Core;

/// <summary>
/// One step in a JSON path: either an object property name or an array index.
/// </summary>
public readonly struct JsonPathSegment
{
    private JsonPathSegment(string? name, int index)
    {
        Name = name;
        Index = index;
    }

    public string? Name { get; }
    public int Index { get; }
    public bool IsIndex => Name is null;

    public static JsonPathSegment Property(string name) => new(name, -1);
    public static JsonPathSegment Element(int index) => new(null, index);

    public bool Matches(JsonPathSegment other) =>
        IsIndex == other.IsIndex
        && (IsIndex
            ? Index == other.Index
            : string.Equals(Name, other.Name, StringComparison.Ordinal));
}

/// <summary>
/// Locates the byte span of a node inside a JSON document, addressed by a path of
/// property names and array indices. Offsets are returned in UTF-8 byte positions,
/// which is the indexing Scintilla uses for a UTF-8 document, so they map directly
/// onto editor positions for highlighting.
/// </summary>
public static class JsonSpanLocator
{
    private sealed class Frame
    {
        public bool IsArray;
        public int NextIndex;
        public string? Pending;
        public JsonPathSegment EntryKey;
        public bool HasEntryKey;
    }

    public static bool TryLocate(
        string json,
        IReadOnlyList<JsonPathSegment> target,
        out int startByte,
        out int lengthByte)
    {
        startByte = 0;
        lengthByte = 0;
        if (string.IsNullOrEmpty(json) || target.Count == 0) {
            return false;
        }

        var bytes = Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(
            bytes,
            new JsonReaderOptions {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

        var frames = new List<Frame>();

        try {
            while (reader.Read()) {
                switch (reader.TokenType) {
                    case JsonTokenType.PropertyName:
                        if (frames.Count > 0) {
                            frames[^1].Pending = reader.GetString();
                        }

                        break;

                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        if (frames.Count > 0) {
                            var childKey = ConsumeChildKey(frames[^1]);
                            if (PathMatches(frames, childKey, target)) {
                                startByte = (int)reader.TokenStartIndex;
                                reader.Skip();
                                lengthByte = (int)reader.BytesConsumed - startByte;
                                return true;
                            }

                            frames.Add(new Frame {
                                IsArray = reader.TokenType == JsonTokenType.StartArray,
                                EntryKey = childKey,
                                HasEntryKey = true
                            });
                        }
                        else {
                            frames.Add(new Frame {
                                IsArray = reader.TokenType == JsonTokenType.StartArray,
                                HasEntryKey = false
                            });
                        }

                        break;

                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        if (frames.Count > 0) {
                            frames.RemoveAt(frames.Count - 1);
                        }

                        break;

                    default:
                        // A scalar value (string, number, true, false, null).
                        if (frames.Count > 0) {
                            var key = ConsumeChildKey(frames[^1]);
                            if (PathMatches(frames, key, target)) {
                                startByte = (int)reader.TokenStartIndex;
                                lengthByte = (int)reader.BytesConsumed - startByte;
                                return true;
                            }
                        }

                        break;
                }
            }
        }
        catch (JsonException) {
            // Malformed JSON: nothing to locate.
            return false;
        }

        return false;
    }

    public static bool TryLocateCell(
        string json,
        int? layerIndex,
        int row,
        int column,
        out int startByte,
        out int lengthByte)
    {
        return TryLocate(json, CellPath(layerIndex, row, column), out startByte, out lengthByte);
    }

    public static bool TryLocateSprite(
        string json,
        int? layerIndex,
        int spriteIndex,
        out int startByte,
        out int lengthByte)
    {
        var path = layerIndex is int li
            ? new[] {
                JsonPathSegment.Property("layers"),
                JsonPathSegment.Element(li),
                JsonPathSegment.Property("spriteInstances"),
                JsonPathSegment.Element(spriteIndex)
            }
            : [
                JsonPathSegment.Property("spriteInstances"),
                JsonPathSegment.Element(spriteIndex)
            ];
        return TryLocate(json, path, out startByte, out lengthByte);
    }

    public static bool TryLocateBlock(
        string json,
        string blockId,
        out int startByte,
        out int lengthByte)
    {
        var path = new[] {
            JsonPathSegment.Property("blocks"),
            JsonPathSegment.Property(blockId)
        };
        return TryLocate(json, path, out startByte, out lengthByte);
    }

    private static JsonPathSegment[] CellPath(int? layerIndex, int row, int column)
    {
        return layerIndex is int li
            ? [
                JsonPathSegment.Property("layers"),
                JsonPathSegment.Element(li),
                JsonPathSegment.Property("cells"),
                JsonPathSegment.Element(row),
                JsonPathSegment.Element(column)
            ]
            : [
                JsonPathSegment.Property("cells"),
                JsonPathSegment.Element(row),
                JsonPathSegment.Element(column)
            ];
    }

    private static JsonPathSegment ConsumeChildKey(Frame top)
    {
        if (top.IsArray) {
            var key = JsonPathSegment.Element(top.NextIndex);
            top.NextIndex++;
            return key;
        }

        var property = JsonPathSegment.Property(top.Pending ?? string.Empty);
        top.Pending = null;
        return property;
    }

    private static bool PathMatches(
        List<Frame> frames,
        JsonPathSegment childKey,
        IReadOnlyList<JsonPathSegment> target)
    {
        // Full path of the value = ancestor container keys + the key within the top container.
        var depth = 0;
        foreach (var frame in frames) {
            if (frame.HasEntryKey) {
                ++depth;
            }
        }

        if (depth + 1 != target.Count) {
            return false;
        }

        var index = 0;
        foreach (var frame in frames) {
            if (!frame.HasEntryKey) {
                continue;
            }

            if (!frame.EntryKey.Matches(target[index])) {
                return false;
            }

            ++index;
        }

        return childKey.Matches(target[index]);
    }
}
