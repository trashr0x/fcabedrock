using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Benchmarks.Tests.Oracles;

/// <summary>
/// The Adult plan-shape guard, which is the only independent claim the Adult conversion cases make
/// about their setup: real data with discovered domains has no derivable byte expectation, so what
/// the benchmark can honestly assert is that <b>every</b> curated attribute resolved to a column of
/// its own.
/// <para>
/// The cases below are written against the way that claim can be false while every aggregate still
/// looks right — one attribute emitting nothing while the others emit a dozen columns each — because
/// an aggregate count is exactly what cannot see it.
/// </para>
/// </summary>
public sealed class AdultOracleTests
{
    private const string What = "adult plan shape";

    /// <summary>The curated spec's fourteen attribute names, in spec order (see <c>AdultSpecs</c>).</summary>
    private static readonly string[] AdultAttributes =
    [
        "age", "workclass", "education", "education-num", "marital-status", "occupation",
        "relationship", "race", "sex", "capital-gain", "capital-loss", "hours-per-week",
        "native-country", "class",
    ];

    [Fact]
    public async Task RequirePlanShape_WhenTheRealCuratedSpecPlans_ThenEveryAttributeContributes()
    {
        // The positive case runs the actual curated Adult spec through the actual planner over
        // Adult-shaped rows, so the guard is known to accept the shape it exists to check rather
        // than only the shapes this file builds by hand.
        using var temp = TempDirectory.Create();
        var specPath = temp.File("adult.toml");
        var dataPath = temp.File("adult.data");
        await File.WriteAllTextAsync(specPath, AdultSpecs.Declared, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(dataPath, AdultRows, TestContext.Current.CancellationToken);

        var prepared = await ConversionPipeline.FromSpecFileAsync(
            specPath, dataPath, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(AdultOracle.SpecAttributeCount, prepared.Plan.Attributes.Count);
        AdultOracle.RequirePlanShape(prepared.Plan, What);
    }

    [Fact]
    public void RequirePlanShape_WhenOneAttributeContributesNothing_ThenItThrows()
    {
        // THE counterexample. `occupation` crosses nothing in any bin and carries no missing
        // column, yet fourteen attributes are planned and the schema still holds far more than
        // fourteen columns — so an aggregate `FormalAttributes.Count >= 14` test passes on a plan
        // that emits nothing at all for one of the curated attributes.
        var (attributes, formal) = Shape(columnsEach: 3);
        var index = Array.IndexOf(AdultAttributes, "occupation");
        attributes[index] = attributes[index] with
        {
            CrossesByBin = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal),
            MissingFormalAttributeId = null,
        };

        // What the previous aggregate check looked at, stated so the counterexample is explicit:
        // both of its conditions still hold.
        Assert.Equal(AdultOracle.SpecAttributeCount, attributes.Count);
        Assert.True(formal.Count >= AdultOracle.SpecAttributeCount);

        var failure = Assert.Throws<InvalidOperationException>(
            () => AdultOracle.RequirePlanShape(attributes, formal, What));
        Assert.Contains("'occupation'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("resolved to nothing at all", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequirePlanShape_WhenAnAttributeIsCoveredOnlyByAnotherAttributesColumn_ThenItThrows()
    {
        // The weaker repair the aggregate invited: counting crossed ids rather than attributing
        // them. `race` is not empty here — it points at a column `education` owns — so a check
        // that only asked "does every attribute cross something?" would accept it.
        var (attributes, formal) = Shape(columnsEach: 3);
        var race = Array.IndexOf(AdultAttributes, "race");
        var educationColumn = formal.First(
            column => string.Equals(column.Identity.AttributeName, "education", StringComparison.Ordinal)).Id;

        attributes[race] = attributes[race] with
        {
            CrossesByBin = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
            {
                ["White"] = [educationColumn],
            },
            MissingFormalAttributeId = null,
        };

        var failure = Assert.Throws<InvalidOperationException>(
            () => AdultOracle.RequirePlanShape(attributes, formal, What));
        Assert.Contains("belongs to 'education'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequirePlanShape_WhenTwoAttributesShareANameAndItsColumns_ThenItThrows()
    {
        // The uniqueness guard behind the attribution check: a plan carrying one name twice would
        // let both entries resolve against the same columns, so one of them contributed nothing of
        // its own even though every id it names is real and correctly attributed.
        var (attributes, formal) = Shape(columnsEach: 2);
        attributes[Array.IndexOf(AdultAttributes, "relationship")] =
            attributes[Array.IndexOf(AdultAttributes, "race")];

        var failure = Assert.Throws<InvalidOperationException>(
            () => AdultOracle.RequirePlanShape(attributes, formal, What));
        Assert.Contains("is claimed by both", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequirePlanShape_WhenAnAttributeCrossesAnIdTheSchemaDoesNotHold_ThenItThrows()
    {
        var (attributes, formal) = Shape(columnsEach: 3);
        var index = Array.IndexOf(AdultAttributes, "class");
        attributes[index] = attributes[index] with
        {
            CrossesByBin = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
            {
                [">50K"] = [formal.Count + 7],
            },
            MissingFormalAttributeId = null,
        };

        var failure = Assert.Throws<InvalidOperationException>(
            () => AdultOracle.RequirePlanShape(attributes, formal, What));
        Assert.Contains("the plan's schema does not contain", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequirePlanShape_WhenTwoBinsOfOneAttributeCrossOneColumn_ThenItPasses()
    {
        // The duplicate-claim rule is about two ATTRIBUTES, not two bins: one attribute mapping
        // several bins onto one column is ordinary, and rejecting it would be a rule that fails a
        // correct plan.
        var (attributes, formal) = Shape(columnsEach: 3);
        var index = Array.IndexOf(AdultAttributes, "sex");
        var own = attributes[index].CrossesByBin.Values.First()[0];

        attributes[index] = attributes[index] with
        {
            CrossesByBin = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
            {
                ["Male"] = [own],
                ["Female"] = [own],
            },
            MissingFormalAttributeId = null,
        };

        AdultOracle.RequirePlanShape(attributes, formal, What);
    }

    [Fact]
    public void RequirePlanShape_WhenABinCrossesNothing_ThenTheAttributeStillCounts()
    {
        // The dichotomic shape the curated spec really has: `sex` and `class` recognize two bins
        // and cross one column, so the false pole crosses nothing. A per-BIN rule would reject the
        // correct plan; the union over bins is what has to be non-empty.
        var (attributes, formal) = Shape(columnsEach: 1);
        var index = Array.IndexOf(AdultAttributes, "sex");
        var own = attributes[index].CrossesByBin.Values.First()[0];

        attributes[index] = attributes[index] with
        {
            CrossesByBin = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
            {
                ["Male"] = [own],
                ["Female"] = [],
            },
            MissingFormalAttributeId = null,
        };

        AdultOracle.RequirePlanShape(attributes, formal, What);
    }

    [Fact]
    public void RequirePlanShape_WhenAnAttributeOnlyCarriesItsMissingColumn_ThenItCounts()
    {
        // `workclass`, `occupation` and `native-country` use `missing_policy = "as_attribute"`, so
        // a missing column is a genuine contribution and not a consolation prize.
        var (attributes, formal) = Shape(columnsEach: 2);
        var index = Array.IndexOf(AdultAttributes, "workclass");
        var own = attributes[index].CrossesByBin.Values.First()[0];

        attributes[index] = attributes[index] with
        {
            CrossesByBin = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal),
            MissingFormalAttributeId = own,
        };

        AdultOracle.RequirePlanShape(attributes, formal, What);
    }

    [Fact]
    public void RequirePlanShape_WhenTheAttributeCountIsWrong_ThenItThrows()
    {
        var (attributes, formal) = Shape(columnsEach: 3);
        attributes.RemoveAt(attributes.Count - 1);

        var failure = Assert.Throws<InvalidOperationException>(
            () => AdultOracle.RequirePlanShape(attributes, formal, What));
        Assert.Contains("expected 14", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A well-formed Adult-shaped plan: the fourteen curated attributes, each owning
    /// <paramref name="columnsEach"/> consecutive columns of its own, ids assigned in schema order
    /// exactly as the planner assigns them.
    /// </summary>
    private static (List<PlannedAttribute> Attributes, List<FormalAttribute> Formal) Shape(int columnsEach)
    {
        var attributes = new List<PlannedAttribute>(AdultAttributes.Length);
        var formal = new List<FormalAttribute>(AdultAttributes.Length * columnsEach);

        foreach (var name in AdultAttributes)
        {
            var crosses = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
            for (var bin = 0; bin < columnsEach; bin++)
            {
                var id = formal.Count;
                var label = $"{name}-{bin}";
                formal.Add(new FormalAttribute(
                    id,
                    $"{name}={label}",
                    new FormalAttributeIdentity(name, "nominal", label, ""),
                    new ValueBin(label)));
                crosses[label] = [id];
            }

            attributes.Add(new PlannedAttribute(
                name,
                new ColumnAttributeSource(attributes.Count),
                new IdentityDiscretizer(),
                crosses.Keys.ToHashSet(StringComparer.Ordinal),
                crosses,
                MissingFormalAttributeId: null,
                UnknownValuePolicy.Warn));
        }

        return (attributes, formal);
    }

    // Three Adult-shaped records: fifteen comma-delimited fields with the published spacing, one
    // carrying `?` in each of the three columns whose policy makes missing an attribute.
    private const string AdultRows =
        "39, State-gov, 77516, Bachelors, 13, Never-married, Adm-clerical, Not-in-family, White, Male, 2174, 0, 40, United-States, <=50K\n"
        + "50, Self-emp-not-inc, 83311, Masters, 14, Married-civ-spouse, Exec-managerial, Husband, White, Male, 0, 1500, 60, Cuba, >50K\n"
        + "28, ?, 338409, HS-grad, 9, Divorced, ?, Unmarried, Black, Female, 0, 0, 20, ?, <=50K\n";
}
