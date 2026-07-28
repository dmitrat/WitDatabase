using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OutWit.Database.AdoNet;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.AuditVerification;

/// <summary>
/// An owned collection gets a composite key of (owner key, generated ordinal) unless configured
/// otherwise, and nothing can fill that ordinal - value generation is tied to the row counter, which
/// can only stand behind a key of one column.
///
/// This fixture has been wrong twice, and both corrections came from running EF Core's suites
/// against SQLite rather than from reading:
///
/// 1. It first asserted that WitDatabase ought to generate the value. SQLite fails on the identical
///    model, so nothing was missing.
/// 2. It then asserted that the model must be refused outright. SQLite accepts it - EF Core's own
///    CompositeKeyEndToEnd suite passes two of its three tests on SQLite with exactly this shape -
///    so refusing it broke models that work, and a provider stricter than SQLite is not a drop-in
///    one.
///
/// What is left is the real defect and the one both corrections agree on: the caller was told
/// nothing until an insert failed on a NOT NULL constraint naming a column they had never written
/// to. The model is now accepted and the problem is stated when the model is built.
/// </summary>
[TestFixture]
public sealed class OwnedCollectionKeyFindingsTests
{
    #region Fields

    private string m_directory = null!;
    private string m_databasePath = null!;
    private CollectingLoggerProvider m_logger = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"witdb_owned_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);

        m_databasePath = Path.Combine(m_directory, "app.witdb");
        m_logger = new CollectingLoggerProvider();
    }

    [TearDown]
    public void TearDown()
    {
        m_logger.Dispose();

        if (!Directory.Exists(m_directory))
            return;

        try
        {
            Directory.Delete(m_directory, recursive: true);
        }
        catch (IOException)
        {
            // Cleanup only.
        }
    }

    #endregion

    #region Tests

    [Test]
    public void GeneratedValueInACompositeKeyIsReportedWhenTheModelIsBuiltTest()
    {
        using var context = new OwnedContext(m_databasePath, m_logger);

        context.Database.EnsureCreated();

        var warning = m_logger.Warnings.FirstOrDefault(w => w.Contains("composite key"));

        Assert.That(warning, Is.Not.Null,
            "nothing can fill the generated part of this key, so the model must not be built in "
            + "silence - the caller used to learn of it from a NOT NULL error on the first insert");

        Assert.Multiple(() =>
        {
            Assert.That(warning, Does.Contain("Item"), "the warning must name the entity type");
            Assert.That(warning, Does.Contain("HasKey"), "and what to do about it");
        });
    }

    /// <summary>
    /// The warning must stay a warning. Such a model is usable whenever the caller supplies the
    /// values, and EF Core's SQLite provider accepts it - so refusing it would break working code
    /// and diverge from the provider WitDatabase is meant to substitute for.
    /// </summary>
    [Test]
    public void TheModelIsStillAcceptedTest()
    {
        using var context = new OwnedContext(m_databasePath, m_logger);

        Assert.DoesNotThrow(() => context.Database.EnsureCreated());

        Assert.That(File.Exists(m_databasePath), Is.True,
            "the schema is created - the warning does not stop the model being used");
    }

    /// <summary>
    /// The other half: a composite key with no generated member must draw no warning at all, or
    /// every join table in every model would carry one.
    /// </summary>
    [Test]
    public void CompositeKeyWithoutGeneratedValuesDrawsNoWarningTest()
    {
        using var context = new ExplicitKeyContext(m_databasePath, m_logger);

        context.Database.EnsureCreated();
        context.Add(new Owner { Id = 1, Items = { new Item { Ordinal = 1, Prop = "a" } } });

        Assert.DoesNotThrow(() => context.SaveChanges());

        Assert.That(m_logger.Warnings.Any(w => w.Contains("composite key")), Is.False,
            "this key is supplied by the caller, so there is nothing to warn about");
    }

    #endregion

    #region Model

    public class Owner
    {
        public int Id { get; set; }

        public List<Item> Items { get; } = [];
    }

    public class Item
    {
        public int Ordinal { get; set; }

        public string Prop { get; set; } = null!;
    }

    /// <summary>
    /// An owned collection with EF's default key: (OwnerId, generated ordinal).
    /// </summary>
    private sealed class OwnedContext(string path, ILoggerProvider logger) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder
                .UseWitDb(new WitDbConnection($"Data Source={path}"))
                .UseLoggerFactory(LoggerFactory.Create(b => b.AddProvider(logger).SetMinimumLevel(LogLevel.Warning)));

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Owner>(e =>
            {
                e.HasKey(x => x.Id);
                e.OwnsMany(x => x.Items);
            });
    }

    /// <summary>
    /// The same shape with the ordinal supplied by the caller - a composite key that works.
    /// </summary>
    private sealed class ExplicitKeyContext(string path, ILoggerProvider logger) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder
                .UseWitDb(new WitDbConnection($"Data Source={path}"))
                .UseLoggerFactory(LoggerFactory.Create(b => b.AddProvider(logger).SetMinimumLevel(LogLevel.Warning)));

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Owner>(e =>
            {
                e.HasKey(x => x.Id);
                e.OwnsMany(x => x.Items, b =>
                {
                    b.Property(x => x.Ordinal).ValueGeneratedNever();
                    b.HasKey("OwnerId", nameof(Item.Ordinal));
                });
            });
    }

    #endregion

    #region Logging

    /// <summary>
    /// Collects warnings so a test can assert on what the model build said.
    /// </summary>
    private sealed class CollectingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> m_warnings = [];

        public IReadOnlyList<string> Warnings
        {
            get
            {
                lock (m_warnings)
                {
                    return m_warnings.ToList();
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new CollectingLogger(m_warnings);

        public void Dispose()
        {
        }

        private sealed class CollectingLogger(List<string> warnings) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel < LogLevel.Warning)
                    return;

                lock (warnings)
                {
                    warnings.Add(formatter(state, exception));
                }
            }
        }
    }

    #endregion
}
