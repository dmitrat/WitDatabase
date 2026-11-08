using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.Integration;

/// <summary>
/// An enum property through the whole provider: tracked, saved, found by key, copied with
/// <c>SetValues</c>, filtered and ordered in a query, as an int-backed and a long-backed enum,
/// nullable and not.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every one of these failed before 14.0.2 with "Argument types do not match".</b> The type
/// mapping source answered an enum property with the plain INT mapping - a mapping whose CLR type
/// is <c>int</c> and which carries no converter. The model said INT, and <c>GetColumnType()</c> was
/// right, so the model-level test passed; but the accessors EF compiles for the property were built
/// for an <c>int</c> and were handed the enum, and <c>Add</c> threw on the first entity. An
/// application had to declare <c>HasConversion&lt;int&gt;()</c> on every enum to get past it.
/// </para>
/// <para>
/// The mapping is now composed with EF's own enum converter, which is what every other provider
/// does by not answering enums at all.
/// </para>
/// </remarks>
[TestFixture]
public class EnumPropertyRoundTripTests
{
    #region Fields

    private string m_testDbPath = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void Setup()
    {
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"WitDbEf_{Guid.NewGuid():N}.witdb");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(m_testDbPath)!, Path.GetFileName(m_testDbPath) + "*"))
        {
            try { File.Delete(file); } catch { }
        }
    }

    #endregion

    #region Round Trip Tests

    [Test]
    public void AddAndSaveAnEntityWithEnumsTest()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();

        context.Orders.Add(new Order { Id = 1, Status = OrderStatus.Shipped, Priority = Priority.High, Channel = Channel.Web });

        Assert.That(() => context.SaveChanges(), Throws.Nothing);
    }

    [Test]
    public void EnumsSurviveReopeningTheFileTest()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Orders.Add(new Order { Id = 1, Status = OrderStatus.Shipped, Priority = Priority.High, Channel = Channel.Web });
            context.Orders.Add(new Order { Id = 2, Status = OrderStatus.Cancelled, Priority = null, Channel = Channel.Phone });
            context.SaveChanges();
        }

        using var reopened = CreateContext();
        var shipped = reopened.Orders.Find(1L)!;
        var cancelled = reopened.Orders.Find(2L)!;

        Assert.That(shipped.Status, Is.EqualTo(OrderStatus.Shipped));
        Assert.That(shipped.Priority, Is.EqualTo(Priority.High));
        Assert.That(shipped.Channel, Is.EqualTo(Channel.Web));
        Assert.That(cancelled.Status, Is.EqualTo(OrderStatus.Cancelled));
        Assert.That(cancelled.Priority, Is.Null);
        Assert.That(cancelled.Channel, Is.EqualTo(Channel.Phone));
    }

    [Test]
    public void SetValuesCopiesEnumsOntoATrackedEntityTest()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();
        context.Orders.Add(new Order { Id = 1, Status = OrderStatus.Placed, Channel = Channel.Web });
        context.SaveChanges();

        var tracked = context.Orders.Find(1L)!;
        context.Entry(tracked).CurrentValues.SetValues(new Order { Id = 1, Status = OrderStatus.Shipped, Priority = Priority.Low, Channel = Channel.Phone });
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var reread = context.Orders.Find(1L)!;
        Assert.That(reread.Status, Is.EqualTo(OrderStatus.Shipped));
        Assert.That(reread.Priority, Is.EqualTo(Priority.Low));
        Assert.That(reread.Channel, Is.EqualTo(Channel.Phone));
    }

    [Test]
    public void QueriesFilterAndOrderByEnumsTest()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();
        context.Orders.AddRange(
            new Order { Id = 1, Status = OrderStatus.Placed, Priority = Priority.Low, Channel = Channel.Web },
            new Order { Id = 2, Status = OrderStatus.Shipped, Priority = Priority.High, Channel = Channel.Web },
            new Order { Id = 3, Status = OrderStatus.Shipped, Priority = null, Channel = Channel.Phone });
        context.SaveChanges();

        var shipped = context.Orders.Where(order => order.Status == OrderStatus.Shipped).OrderBy(order => order.Id).Select(order => order.Id).ToList();
        var web = context.Orders.Where(order => order.Channel == Channel.Web).Count();
        var unprioritized = context.Orders.Where(order => order.Priority == null).Select(order => order.Id).ToList();
        var byPriority = context.Orders.Where(order => order.Priority != null).OrderByDescending(order => order.Priority).Select(order => order.Id).ToList();

        Assert.That(shipped, Is.EqualTo(new long[] { 2, 3 }));
        Assert.That(web, Is.EqualTo(2));
        Assert.That(unprioritized, Is.EqualTo(new long[] { 3 }));
        Assert.That(byPriority, Is.EqualTo(new long[] { 2, 1 }));
    }

    [Test]
    public void EnumColumnsKeepTheirIntegerStoreTypesTest()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(Order))!;

        Assert.That(entity.FindProperty(nameof(Order.Status))!.GetColumnType(), Is.EqualTo("INT"));
        Assert.That(entity.FindProperty(nameof(Order.Priority))!.GetColumnType(), Is.EqualTo("INT"));
        Assert.That(entity.FindProperty(nameof(Order.Channel))!.GetColumnType(), Is.EqualTo("BIGINT"), "a long-backed enum is stored as its own width");
    }

    #endregion

    #region Tools

    private OrdersContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OrdersContext>()
            .UseWitDb($"Data Source={m_testDbPath}")
            .Options;

        return new OrdersContext(options);
    }

    #endregion

    #region Model

    public enum OrderStatus { Placed, Shipped, Cancelled }

    public enum Priority { Low = 1, High = 2 }

    public enum Channel : long { Web = 1, Phone = 2 }

    public class Order
    {
        public long Id { get; set; }
        public OrderStatus Status { get; set; }
        public Priority? Priority { get; set; }
        public Channel Channel { get; set; }
    }

    public class OrdersContext : DbContext
    {
        public OrdersContext(DbContextOptions<OrdersContext> options)
            : base(options)
        {
        }

        public DbSet<Order> Orders => Set<Order>();
    }

    #endregion
}
