using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// Writes the application log to a file next to the settings.
///
/// Studio is built as a WinExe, which has no console, so the console provider it used to configure
/// alone sent every message - including the one in DatabaseService's catch - nowhere at all. An
/// application whose reputation is instability needs somewhere to look.
///
/// Deliberately small and dependency-free: one file, appended under a lock, rolled when it gets large,
/// and never allowed to throw into the application it is trying to describe.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    #region Constants

    private const long MAX_BYTES = 5 * 1024 * 1024;
    private const int KEEP_ROLLED = 3;

    #endregion

    #region Fields

    private readonly ConcurrentDictionary<string, FileLogger> m_loggers = new();
    private readonly Lock m_lock = new();
    private readonly string m_filePath;
    private readonly LogLevel m_minimumLevel;

    #endregion

    #region Constructors

    public FileLoggerProvider(string filePath, LogLevel minimumLevel = LogLevel.Information)
    {
        m_filePath = filePath;
        m_minimumLevel = minimumLevel;

        var directory = Path.GetDirectoryName(filePath);

        try
        {
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
        }
        catch
        {
            // if the log cannot be created the application still has to start
        }
    }

    #endregion

    #region Functions

    /// <summary>
    /// The path the log is written to, so that Help > About can show a user where to look.
    /// </summary>
    public static string DefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        return Path.Combine(appData, "WitDatabase.Studio", "logs", "studio.log");
    }

    public ILogger CreateLogger(string categoryName)
    {
        return m_loggers.GetOrAdd(categoryName, name => new FileLogger(this, name));
    }

    internal bool IsEnabled(LogLevel logLevel)
    {
        return logLevel >= m_minimumLevel && logLevel != LogLevel.None;
    }

    internal void Write(string line)
    {
        lock (m_lock)
        {
            try
            {
                RollIfNeeded();
                File.AppendAllText(m_filePath, line, Encoding.UTF8);
            }
            catch
            {
                // a logger that throws turns a recoverable failure into a crash
            }
        }
    }

    private void RollIfNeeded()
    {
        var info = new FileInfo(m_filePath);

        if (!info.Exists || info.Length < MAX_BYTES)
            return;

        for (var i = KEEP_ROLLED; i >= 1; i--)
        {
            var older = $"{m_filePath}.{i}";
            var newer = i == 1 ? m_filePath : $"{m_filePath}.{i - 1}";

            if (!File.Exists(newer))
                continue;

            if (File.Exists(older))
                File.Delete(older);

            File.Move(newer, older);
        }
    }

    public void Dispose()
    {
        m_loggers.Clear();
    }

    #endregion

    #region Logger

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider m_provider;
        private readonly string m_category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            m_provider = provider;
            m_category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => m_provider.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var builder = new StringBuilder();

            builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            builder.Append(" [").Append(Level(logLevel)).Append("] ");
            builder.Append(m_category).Append(" - ");
            builder.AppendLine(formatter(state, exception));

            if (exception != null)
                builder.AppendLine(exception.ToString());

            m_provider.Write(builder.ToString());
        }

        private static string Level(LogLevel logLevel) => logLevel switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???"
        };
    }

    #endregion
}
