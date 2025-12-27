using System.Text.Json;
using OutWit.Database.Values;

namespace OutWit.Database.Expressions;

/// <summary>
/// JSON function implementations: JSON_VALUE, JSON_QUERY, JSON_EXTRACT, JSON_SET, etc.
/// </summary>
public sealed partial class ExpressionEvaluator
{
    #region JSON_VALUE / JSON_QUERY / JSON_EXTRACT

    /// <summary>
    /// Evaluates JSON_EXTRACT(json, path) - extracts any JSON value at path.
    /// Returns scalar values as their SQL type, objects/arrays as JSON.
    /// </summary>
    private static WitSqlValue EvaluateJsonExtract(WitSqlValue[] args)
    {
        if (args.Length < 2 || args[0].IsNull || args[1].IsNull)
            return WitSqlValue.Null;

        var json = EnsureJson(args[0]);
        if (json.IsNull)
            return WitSqlValue.Null;

        var path = args[1].AsString();
        return json.JsonExtract(path);
    }

    /// <summary>
    /// Evaluates JSON_VALUE(json, path) - extracts scalar value as SQL type.
    /// Returns NULL for objects/arrays (unlike JSON_EXTRACT which returns them as JSON).
    /// </summary>
    private static WitSqlValue EvaluateJsonValue(WitSqlValue[] args)
    {
        var result = EvaluateJsonExtract(args);
        
        // JSON_VALUE returns NULL for non-scalar values (objects/arrays)
        if (result.Type == Types.WitSqlType.Json)
            return WitSqlValue.Null;

        return result;
    }

    /// <summary>
    /// Evaluates JSON_QUERY(json, path) - extracts object/array as JSON string.
    /// Returns NULL for scalar values (unlike JSON_EXTRACT which returns them as-is).
    /// </summary>
    private static WitSqlValue EvaluateJsonQuery(WitSqlValue[] args)
    {
        var result = EvaluateJsonExtract(args);
        
        // JSON_QUERY returns NULL for scalar values, returns JSON for objects/arrays
        if (result.Type != Types.WitSqlType.Json)
            return WitSqlValue.Null;

        // Return as text (JSON string representation)
        return WitSqlValue.FromText(result.AsString());
    }

    #endregion

    #region JSON_TYPE

    /// <summary>
    /// Evaluates JSON_TYPE(json) - returns the type of JSON value.
    /// Returns: "null", "boolean", "number", "string", "array", "object".
    /// </summary>
    private static WitSqlValue EvaluateJsonType(WitSqlValue[] args)
    {
        if (args.Length < 1 || args[0].IsNull)
            return WitSqlValue.FromText("null");

        var json = EnsureJson(args[0]);
        if (json.IsNull)
            return WitSqlValue.FromText("null");

        return WitSqlValue.FromText(json.JsonType());
    }

    #endregion

    #region JSON_ARRAY_LENGTH

    /// <summary>
    /// Evaluates JSON_ARRAY_LENGTH(json) - returns length of JSON array.
    /// Returns NULL if not an array.
    /// </summary>
    private static WitSqlValue EvaluateJsonArrayLength(WitSqlValue[] args)
    {
        if (args.Length < 1 || args[0].IsNull)
            return WitSqlValue.Null;

        var json = EnsureJson(args[0]);
        if (json.IsNull)
            return WitSqlValue.Null;

        return json.JsonArrayLength();
    }

    #endregion

    #region JSON_VALID

    /// <summary>
    /// Evaluates JSON_VALID(string) - checks if string is valid JSON.
    /// Returns TRUE if valid, FALSE otherwise.
    /// </summary>
    private static WitSqlValue EvaluateJsonValid(WitSqlValue[] args)
    {
        if (args.Length < 1 || args[0].IsNull)
            return WitSqlValue.False;

        var str = args[0].AsString();
        try
        {
            using var doc = JsonDocument.Parse(str);
            return WitSqlValue.True;
        }
        catch
        {
            return WitSqlValue.False;
        }
    }

    #endregion

    #region JSON_SET / JSON_INSERT / JSON_REPLACE / JSON_REMOVE

    /// <summary>
    /// Evaluates JSON_SET(json, path, value) - sets value at path (creates if not exists, replaces if exists).
    /// </summary>
    private static WitSqlValue EvaluateJsonSet(WitSqlValue[] args)
    {
        return ModifyJson(args, insertOnly: false, replaceOnly: false);
    }

    /// <summary>
    /// Evaluates JSON_INSERT(json, path, value) - inserts value at path only if not exists.
    /// </summary>
    private static WitSqlValue EvaluateJsonInsert(WitSqlValue[] args)
    {
        return ModifyJson(args, insertOnly: true, replaceOnly: false);
    }

    /// <summary>
    /// Evaluates JSON_REPLACE(json, path, value) - replaces value at path only if exists.
    /// </summary>
    private static WitSqlValue EvaluateJsonReplace(WitSqlValue[] args)
    {
        return ModifyJson(args, insertOnly: false, replaceOnly: true);
    }

    /// <summary>
    /// Evaluates JSON_REMOVE(json, path) - removes value at path.
    /// </summary>
    private static WitSqlValue EvaluateJsonRemove(WitSqlValue[] args)
    {
        if (args.Length < 2 || args[0].IsNull || args[1].IsNull)
            return args.Length > 0 ? args[0] : WitSqlValue.Null;

        var json = EnsureJson(args[0]);
        if (json.IsNull)
            return WitSqlValue.Null;

        var path = NormalizePath(args[1].AsString());
        var jsonStr = json.AsString();

        try
        {
            using var doc = JsonDocument.Parse(jsonStr);
            var result = RemoveAtPath(doc.RootElement, path);
            return WitSqlValue.FromJsonString(result);
        }
        catch
        {
            return args[0];
        }
    }

    #endregion

    #region JSON_ARRAY / JSON_OBJECT

    /// <summary>
    /// Evaluates JSON_ARRAY(values...) - creates JSON array from arguments.
    /// </summary>
    private static WitSqlValue EvaluateJsonArray(WitSqlValue[] args)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartArray();
        foreach (var arg in args)
        {
            WriteJsonValue(writer, arg);
        }
        writer.WriteEndArray();
        writer.Flush();

        var jsonStr = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        return WitSqlValue.FromJsonString(jsonStr);
    }

    /// <summary>
    /// Evaluates JSON_OBJECT(key1, value1, key2, value2, ...) - creates JSON object from key-value pairs.
    /// </summary>
    private static WitSqlValue EvaluateJsonObject(WitSqlValue[] args)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();
        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            var key = args[i].AsString();
            var value = args[i + 1];
            writer.WritePropertyName(key);
            WriteJsonValue(writer, value);
        }
        writer.WriteEndObject();
        writer.Flush();

        var jsonStr = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        return WitSqlValue.FromJsonString(jsonStr);
    }

    #endregion

    #region JSON Helpers

    /// <summary>
    /// Ensures a value is in JSON format. Parses text as JSON if needed.
    /// </summary>
    private static WitSqlValue EnsureJson(WitSqlValue value)
    {
        if (value.Type == Types.WitSqlType.Json)
            return value;

        if (value.Type == Types.WitSqlType.Text)
        {
            try
            {
                var str = value.AsString();
                return WitSqlValue.FromJsonString(str);
            }
            catch
            {
                return WitSqlValue.Null;
            }
        }

        return WitSqlValue.Null;
    }

    /// <summary>
    /// Normalizes a JSON path by removing leading "$." or "$".
    /// </summary>
    private static string NormalizePath(string path)
    {
        if (path.StartsWith("$."))
            return path[2..];
        if (path.StartsWith("$"))
            return path[1..];
        return path;
    }

    /// <summary>
    /// Modifies JSON at specified path.
    /// </summary>
    private static WitSqlValue ModifyJson(WitSqlValue[] args, bool insertOnly, bool replaceOnly)
    {
        if (args.Length < 3 || args[0].IsNull || args[1].IsNull)
            return args.Length > 0 ? args[0] : WitSqlValue.Null;

        var json = EnsureJson(args[0]);
        if (json.IsNull)
            return WitSqlValue.Null;

        var path = NormalizePath(args[1].AsString());
        var newValue = args[2];
        var jsonStr = json.AsString();

        try
        {
            using var doc = JsonDocument.Parse(jsonStr);
            var result = SetAtPath(doc.RootElement, path, newValue, insertOnly, replaceOnly);
            return WitSqlValue.FromJsonString(result);
        }
        catch
        {
            return args[0];
        }
    }

    /// <summary>
    /// Sets a value at a JSON path, returning modified JSON string.
    /// </summary>
    private static string SetAtPath(JsonElement root, string path, WitSqlValue value, bool insertOnly, bool replaceOnly)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        if (string.IsNullOrEmpty(path))
        {
            // Replace root
            WriteJsonValue(writer, value);
            writer.Flush();
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }

        SetAtPathRecursive(writer, root, path.Split('.'), 0, value, insertOnly, replaceOnly);
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Recursively sets value at path.
    /// </summary>
    private static void SetAtPathRecursive(
        Utf8JsonWriter writer,
        JsonElement current,
        string[] pathParts,
        int partIndex,
        WitSqlValue newValue,
        bool insertOnly,
        bool replaceOnly)
    {
        if (current.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();

            var targetKey = partIndex < pathParts.Length ? pathParts[partIndex] : null;
            bool keyFound = false;

            foreach (var prop in current.EnumerateObject())
            {
                if (targetKey != null && prop.Name.Equals(targetKey, StringComparison.OrdinalIgnoreCase))
                {
                    keyFound = true;
                    writer.WritePropertyName(prop.Name);

                    if (partIndex == pathParts.Length - 1)
                    {
                        // Last path segment - set value
                        if (insertOnly)
                        {
                            // INSERT: keep existing value
                            WriteJsonElement(writer, prop.Value);
                        }
                        else
                        {
                            // SET or REPLACE: write new value
                            WriteJsonValue(writer, newValue);
                        }
                    }
                    else
                    {
                        // Recurse deeper
                        SetAtPathRecursive(writer, prop.Value, pathParts, partIndex + 1, newValue, insertOnly, replaceOnly);
                    }
                }
                else
                {
                    writer.WritePropertyName(prop.Name);
                    WriteJsonElement(writer, prop.Value);
                }
            }

            // Add new key if not found and not replaceOnly
            if (targetKey != null && !keyFound && !replaceOnly)
            {
                writer.WritePropertyName(targetKey);
                if (partIndex == pathParts.Length - 1)
                {
                    WriteJsonValue(writer, newValue);
                }
                else
                {
                    // Create nested object
                    writer.WriteStartObject();
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndObject();
        }
        else if (current.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in current.EnumerateArray())
            {
                WriteJsonElement(writer, item);
            }
            writer.WriteEndArray();
        }
        else
        {
            WriteJsonElement(writer, current);
        }
    }

    /// <summary>
    /// Removes a value at a JSON path, returning modified JSON string.
    /// </summary>
    private static string RemoveAtPath(JsonElement root, string path)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        if (string.IsNullOrEmpty(path))
        {
            // Cannot remove root - return null JSON
            writer.WriteNullValue();
            writer.Flush();
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }

        RemoveAtPathRecursive(writer, root, path.Split('.'), 0);
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Recursively removes value at path.
    /// </summary>
    private static void RemoveAtPathRecursive(
        Utf8JsonWriter writer,
        JsonElement current,
        string[] pathParts,
        int partIndex)
    {
        if (current.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();

            var targetKey = partIndex < pathParts.Length ? pathParts[partIndex] : null;

            foreach (var prop in current.EnumerateObject())
            {
                if (targetKey != null && prop.Name.Equals(targetKey, StringComparison.OrdinalIgnoreCase))
                {
                    if (partIndex == pathParts.Length - 1)
                    {
                        // Last path segment - skip this property (remove it)
                        continue;
                    }

                    // Recurse deeper
                    writer.WritePropertyName(prop.Name);
                    RemoveAtPathRecursive(writer, prop.Value, pathParts, partIndex + 1);
                }
                else
                {
                    writer.WritePropertyName(prop.Name);
                    WriteJsonElement(writer, prop.Value);
                }
            }

            writer.WriteEndObject();
        }
        else if (current.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in current.EnumerateArray())
            {
                WriteJsonElement(writer, item);
            }
            writer.WriteEndArray();
        }
        else
        {
            WriteJsonElement(writer, current);
        }
    }

    /// <summary>
    /// Writes a WitSqlValue as JSON.
    /// </summary>
    private static void WriteJsonValue(Utf8JsonWriter writer, WitSqlValue value)
    {
        if (value.IsNull)
        {
            writer.WriteNullValue();
            return;
        }

        switch (value.Type)
        {
            case Types.WitSqlType.Boolean:
                writer.WriteBooleanValue(value.AsBool());
                break;

            case Types.WitSqlType.Integer:
                writer.WriteNumberValue(value.AsInt64());
                break;

            case Types.WitSqlType.Real:
                writer.WriteNumberValue(value.AsDouble());
                break;

            case Types.WitSqlType.Decimal:
                writer.WriteNumberValue(value.AsDecimal());
                break;

            case Types.WitSqlType.Text:
                writer.WriteStringValue(value.AsString());
                break;

            case Types.WitSqlType.Json:
                // Write JSON inline
                var jsonStr = value.AsString();
                using (var innerDoc = JsonDocument.Parse(jsonStr))
                {
                    WriteJsonElement(writer, innerDoc.RootElement);
                }
                break;

            case Types.WitSqlType.Guid:
                writer.WriteStringValue(value.AsGuid().ToString());
                break;

            case Types.WitSqlType.DateTime:
                writer.WriteStringValue(value.AsDateTime().ToString("o"));
                break;

            case Types.WitSqlType.DateOnly:
                writer.WriteStringValue(value.AsDateOnly().ToString("yyyy-MM-dd"));
                break;

            case Types.WitSqlType.TimeOnly:
                writer.WriteStringValue(value.AsTimeOnly().ToString("HH:mm:ss"));
                break;

            case Types.WitSqlType.TimeSpan:
                writer.WriteStringValue(value.AsTimeSpan().ToString());
                break;

            case Types.WitSqlType.DateTimeOffset:
                writer.WriteStringValue(value.AsDateTimeOffset().ToString("o"));
                break;

            case Types.WitSqlType.Blob:
                writer.WriteStringValue(Convert.ToBase64String(value.AsBlob()));
                break;

            default:
                writer.WriteStringValue(value.AsString());
                break;
        }
    }

    /// <summary>
    /// Writes a JsonElement to the writer.
    /// </summary>
    private static void WriteJsonElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject())
                {
                    writer.WritePropertyName(prop.Name);
                    WriteJsonElement(writer, prop.Value);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteJsonElement(writer, item);
                }
                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                if (element.TryGetInt64(out var l))
                    writer.WriteNumberValue(l);
                else
                    writer.WriteNumberValue(element.GetDouble());
                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            default:
                writer.WriteNullValue();
                break;
        }
    }

    #endregion
}
