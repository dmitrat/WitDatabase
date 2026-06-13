namespace OutWit.Database.Engine;

internal static class WitSqlParameterKeys
{
    internal static string ToContextKey(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        if (name.StartsWith('@') || name.StartsWith(':') || name.StartsWith('$'))
            return name;

        return $"@{name}";
    }
}
