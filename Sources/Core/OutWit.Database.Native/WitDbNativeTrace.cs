namespace OutWit.Database.Native;

/// <summary>Temporary file trace for NativeAOT bring-up (WITDB_NATIVE_TRACE=1).</summary>
internal static class WitDbNativeTrace
{
    private static readonly int s_enabled = string.Equals(
        Environment.GetEnvironmentVariable("WITDB_NATIVE_TRACE"),
        "1",
        StringComparison.Ordinal) ? 1 : 0;

    public static void Write(string message)
    {
        if (s_enabled == 0)
        {
            return;
        }

        try
        {
            var line = $"{DateTime.UtcNow:O} tid={Environment.CurrentManagedThreadId} {message}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "witdb-native.trace"), line);
        }
        catch
        {
            // ignore
        }
    }
}
