using OutWit.Database.Types;
using System.Text.Json;

namespace OutWit.Database.Values
{
    public readonly partial struct WitSqlValue
    {
        #region Json Operations

        /// <summary>
        /// Extracts a value from JSON using a JSON path.
        /// </summary>
        /// <param name="path">JSON path (e.g., "$.name" or "name").</param>
        /// <returns>The extracted value, or NULL if not found.</returns>
        public WitSqlValue JsonExtract(string path)
        {
            if (m_type != WitSqlType.Json || m_objectValue is null)
                return Null;

            var element = m_objectValue switch
            {
                JsonDocument doc => doc.RootElement,
                JsonElement elem => elem,
                _ => default
            };

            return ExtractFromElement(element, NormalizePath(path));
        }

        /// <summary>
        /// Gets the type of JSON value.
        /// </summary>
        /// <returns>JSON type name: "null", "boolean", "number", "string", "array", "object".</returns>
        public string JsonType()
        {
            if (m_type != WitSqlType.Json)
                return "null";

            var element = m_objectValue switch
            {
                JsonDocument doc => doc.RootElement,
                JsonElement elem => elem,
                _ => default
            };

            return element.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => "null",
                JsonValueKind.True or JsonValueKind.False => "boolean",
                JsonValueKind.Number => "number",
                JsonValueKind.String => "string",
                JsonValueKind.Array => "array",
                JsonValueKind.Object => "object",
                _ => "null"
            };
        }

        /// <summary>
        /// Gets the length of a JSON array.
        /// </summary>
        /// <returns>Array length, or NULL if not an array.</returns>
        public WitSqlValue JsonArrayLength()
        {
            if (m_type != WitSqlType.Json)
                return Null;

            var element = m_objectValue switch
            {
                JsonDocument doc => doc.RootElement,
                JsonElement elem => elem,
                _ => default
            };

            return element.ValueKind == JsonValueKind.Array
                ? FromInt(element.GetArrayLength())
                : Null;
        }

        #endregion

        #region Json Tools

        private string JsonToString()
        {
            return m_objectValue switch
            {
                JsonDocument doc => doc.RootElement.GetRawText(),
                JsonElement elem => elem.GetRawText(),
                _ => string.Empty
            };
        }

        private static string NormalizePath(string path)
        {
            // Remove leading "$." if present
            if (path.StartsWith("$."))
                return path[2..];
            if (path.StartsWith("$"))
                return path[1..];
            return path;
        }

        private static WitSqlValue ExtractFromElement(JsonElement element, string path)
        {
            if (string.IsNullOrEmpty(path))
                return JsonElementToSqlValue(element);

            // Split path by '.' handling array indices
            var parts = path.Split('.');
            var current = element;

            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part))
                    continue;

                // Check for array index: "items[0]" or just "[0]"
                var bracketIndex = part.IndexOf('[');
                if (bracketIndex >= 0)
                {
                    // Get property name before bracket (if any)
                    if (bracketIndex > 0)
                    {
                        var propName = part[..bracketIndex];
                        if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(propName, out current))
                            return Null;
                    }

                    // Parse array index
                    var endBracket = part.IndexOf(']', bracketIndex);
                    if (endBracket < 0)
                        return Null;

                    var indexStr = part[(bracketIndex + 1)..endBracket];
                    if (!int.TryParse(indexStr, out var index))
                        return Null;

                    if (current.ValueKind != JsonValueKind.Array || index < 0 || index >= current.GetArrayLength())
                        return Null;

                    current = current[index];
                }
                else
                {
                    // Regular property access
                    if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
                        return Null;
                }
            }

            return JsonElementToSqlValue(current);
        }

        private static WitSqlValue JsonElementToSqlValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => Null,
                JsonValueKind.True => True,
                JsonValueKind.False => False,
                JsonValueKind.Number when element.TryGetInt64(out var l) => FromInt(l),
                JsonValueKind.Number => FromReal(element.GetDouble()),
                JsonValueKind.String => FromText(element.GetString()!),
                JsonValueKind.Array or JsonValueKind.Object => FromJson(element),
                _ => Null
            };
        }

        #endregion
    }
}
