using System.Diagnostics;

namespace OutWit.Database.Parser.Tests.Grammar;

/// <summary>
/// Records how long the corpus takes to parse, so the cost of phase 3's extra grammar layers is
/// attributable rather than discovered later.
/// </summary>
/// <remarks>
/// <para>
/// Splitting <c>expression</c> into three rules adds nesting depth to every expression parse, and
/// phase 5 will be measuring the engine. Taking the number now means the restructure's cost can be
/// separated from everything else that changes between here and there.
/// </para>
/// <para>
/// <b>It asserts nothing about the time.</b> This suite already carries 17 wall-clock assertions
/// under <c>Performance/</c> that measure machine load rather than the engine and fail under the
/// suite's own parallelism — the recorded verdict on them is that they should be logged diagnostics.
/// This is written as one from the start: it prints, and passes.
/// </para>
/// </remarks>
[TestFixture]
[Category("Grammar")]
public class GrammarThroughputTests
{
    private const int WarmupIterations = 3;
    private const int MeasuredIterations = 20;

    [Test]
    public void CorpusParseThroughputIsRecordedTest()
    {
        var corpus = GrammarCorpus.All.ToArray();

        // ANTLR builds its DFA cache lazily, so an unwarmed first pass measures cache construction
        // rather than parsing.
        for (var i = 0; i < WarmupIterations; i++)
        {
            ParseAll(corpus);
        }

        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < MeasuredIterations; i++)
        {
            ParseAll(corpus);
        }

        stopwatch.Stop();

        var totalParses = (long)corpus.Length * MeasuredIterations;
        var microsecondsEach = stopwatch.Elapsed.TotalMilliseconds * 1000 / totalParses;

        TestContext.Out.WriteLine($"corpus entries    : {corpus.Length}");
        TestContext.Out.WriteLine($"iterations        : {MeasuredIterations}");
        TestContext.Out.WriteLine($"total parses      : {totalParses}");
        TestContext.Out.WriteLine($"elapsed           : {stopwatch.Elapsed.TotalMilliseconds:F1} ms");
        TestContext.Out.WriteLine($"per parse         : {microsecondsEach:F1} us");

        Assert.Pass("characterisation only - see the printed result");
    }

    private static void ParseAll(IReadOnlyList<string> corpus)
    {
        foreach (var sql in corpus)
        {
            // TryParse rather than Parse: a few corpus entries are deliberately unusual, and an
            // exception path would measure exception throwing rather than parsing.
            WitSql.TryParse(sql);
        }
    }
}
