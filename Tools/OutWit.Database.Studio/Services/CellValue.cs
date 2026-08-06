using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// What a value is, as far as showing it is concerned.
/// </summary>
public enum CellKind
{
    Null,
    Text,
    Json,
    Number,
    Boolean,
    Moment,
    Guid,
    Binary
}

/// <summary>
/// One node of a JSON value, for the tree in the viewer.
/// </summary>
public sealed class JsonNode
{
    public required string Name { get; init; }

    public string? Value { get; init; }

    public string? Kind { get; init; }

    public List<JsonNode> Children { get; } = [];
}

/// <summary>
/// Reading a cell for a person: what it is, what it says in a row 26 pixels high, and what it says in
/// the viewer (4.4, 4.5, WS-33, WS-34).
///
/// The engine gives exact types back - measured 2026-08-06 through the provider: a DECIMAL arrives as
/// <c>decimal</c>, a GUID as <c>Guid</c>, a BOOLEAN as <c>bool</c>, a BLOB as <c>byte[]</c>. So the
/// risk WS-34 is about is not conversion in the engine, it is a client that renders them all through
/// <c>ToString()</c> and parses them back through <c>double</c>. Nothing here goes through a double.
/// </summary>
public static class CellValue
{
    #region Constants

    /// <summary>
    /// The first bytes of the formats worth naming. Everything else stays a dump: guessing an encoding
    /// for an arbitrary BLOB produces "text" with broken characters, which is worse than hex.
    /// </summary>
    private static readonly (byte[] Signature, string Name)[] SIGNATURES =
    [
        ([0x89, 0x50, 0x4E, 0x47], "PNG image"),
        ([0xFF, 0xD8, 0xFF], "JPEG image"),
        ([0x47, 0x49, 0x46, 0x38], "GIF image"),
        ([0x25, 0x50, 0x44, 0x46], "PDF document"),
        ([0x50, 0x4B, 0x03, 0x04], "ZIP archive"),
        ([0x1F, 0x8B], "GZIP data"),
        ([0x42, 0x4D], "BMP image")
    ];

    #endregion

    #region Functions

    public static CellKind KindOf(object? value)
    {
        return value switch
        {
            null or DBNull => CellKind.Null,
            byte[] => CellKind.Binary,
            bool => CellKind.Boolean,
            DateTime or DateTimeOffset or TimeSpan => CellKind.Moment,
            Guid => CellKind.Guid,
            string text when LooksLikeJson(text) => CellKind.Json,
            string => CellKind.Text,
            _ => CellKind.Number
        };
    }

    /// <summary>
    /// What goes in the cell. One line always: a newline becomes a mark, a BLOB becomes its size, and
    /// a GUID is shortened - the full value is a keystroke away in the viewer.
    /// </summary>
    public static string Display(object? value)
    {
        switch (value)
        {
            case null or DBNull:
                // WS-33. Never an empty string: an empty string is a value, and a grid that shows both
                // as an empty cell costs somebody an hour of debugging every time.
                return "NULL";

            case byte[] bytes:
                return $"BLOB · {Size(bytes.Length)}";

            case bool flag:
                return flag ? "true" : "false";

            case DateTime moment:
                return moment.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            case DateTimeOffset moment:
                return moment.ToString("yyyy-MM-dd HH:mm:sszzz", CultureInfo.InvariantCulture);

            case Guid guid:
                var text = guid.ToString();
                return $"{text[..4]}…{text[^3..]}";

            case decimal number:
                // Every digit the column declared, and no double anywhere on the way.
                return number.ToString(CultureInfo.InvariantCulture);

            case double number:
                return number.ToString("R", CultureInfo.InvariantCulture);

            case string line when line.Contains('\n') || line.Contains('\r'):
                return line.ReplaceLineEndings(" ¶ ");

            default:
                return value as string ?? System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }
    }

    /// <summary>
    /// The whole value, for the viewer: no shortening, no marks.
    /// </summary>
    public static string Full(object? value)
    {
        return value switch
        {
            null or DBNull => "NULL",
            byte[] bytes => Hex(bytes),
            string text => text,
            _ => Display(value)
        };
    }

    /// <summary>
    /// What the viewer's title says about the value: its type and its size.
    /// </summary>
    public static string Describe(object? value)
    {
        return value switch
        {
            null or DBNull => "NULL",
            byte[] bytes => $"{Signature(bytes) ?? "BLOB"} · {bytes.Length} bytes",
            string text when LooksLikeJson(text) => $"JSON · {Encoding.UTF8.GetByteCount(text)} bytes",
            string text => $"text · {text.Length} characters",
            _ => value.GetType().Name
        };
    }

    /// <summary>
    /// A hex dump with the printable bytes beside it, the way every tool that has to show binary data
    /// has shown it for forty years.
    /// </summary>
    public static string Hex(byte[] bytes, int limit = 4096)
    {
        var builder = new StringBuilder();
        var shown = Math.Min(bytes.Length, limit);

        for (var offset = 0; offset < shown; offset += 16)
        {
            var line = bytes.AsSpan(offset, Math.Min(16, shown - offset));

            builder.Append(offset.ToString("X8", CultureInfo.InvariantCulture)).Append("  ");

            for (var i = 0; i < 16; i++)
            {
                builder.Append(i < line.Length ? line[i].ToString("X2", CultureInfo.InvariantCulture) : "  ");
                builder.Append(i == 7 ? "  " : " ");
            }

            builder.Append(' ');

            foreach (var b in line)
                builder.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');

            builder.AppendLine();
        }

        if (bytes.Length > shown)
            builder.AppendLine($"… {bytes.Length - shown} more bytes");

        return builder.ToString();
    }

    /// <summary>
    /// What the first bytes say the value is, or null when they say nothing recognisable.
    /// </summary>
    public static string? Signature(byte[] bytes)
    {
        foreach (var (signature, name) in SIGNATURES)
        {
            if (bytes.Length < signature.Length)
                continue;

            if (bytes.AsSpan(0, signature.Length).SequenceEqual(signature))
                return name;
        }

        return null;
    }

    /// <summary>
    /// The JSON as a tree, or null when the text is not JSON after all. Never throws: the value is a
    /// string in a database, and nothing promised it was well formed.
    /// </summary>
    public static JsonNode? Tree(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            using var document = JsonDocument.Parse(text);

            return Node("root", document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonNode Node(string name, JsonElement element)
    {
        var node = new JsonNode
        {
            Name = name,
            Kind = element.ValueKind.ToString(),
            Value = element.ValueKind switch
            {
                JsonValueKind.Object or JsonValueKind.Array => null,
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Null => "null",
                _ => element.GetRawText()
            }
        };

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    node.Children.Add(Node(property.Name, property.Value));
                break;

            case JsonValueKind.Array:
                var index = 0;

                foreach (var item in element.EnumerateArray())
                    node.Children.Add(Node($"[{index++}]", item));
                break;
        }

        return node;
    }

    private static bool LooksLikeJson(string text)
    {
        var trimmed = text.AsSpan().Trim();

        return trimmed.Length > 1
            && ((trimmed[0] == '{' && trimmed[^1] == '}') || (trimmed[0] == '[' && trimmed[^1] == ']'));
    }

    private static string Size(int bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
        };
    }

    #endregion
}
