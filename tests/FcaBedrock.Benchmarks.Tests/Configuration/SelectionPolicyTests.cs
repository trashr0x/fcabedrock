using FcaBedrock.Benchmarks.Configuration;

namespace FcaBedrock.Benchmarks.Tests.Configuration;

/// <summary>
/// The one selection rule the suite adds on top of BenchmarkDotNet's own filtering. It decides
/// whether hours of machine time start, and whether a run depends on a third-party host being
/// reachable, so it is tested directly rather than inferred from a run.
/// </summary>
public sealed class SelectionPolicyTests
{
    private static readonly string[] SmallCase = [BenchmarkCategories.Source, BenchmarkCategories.Small];
    private static readonly string[] WorkingCase = [BenchmarkCategories.Source, BenchmarkCategories.Working];
    private static readonly string[] ScaleCase = [BenchmarkCategories.Source, BenchmarkCategories.Scale];
    private static readonly string[] ExternalCase = [BenchmarkCategories.Source, BenchmarkCategories.External];

    [Fact]
    public void Policy_WhenNoCategoryIsNamed_ThenOnlySmallIsSelected()
    {
        var policy = SelectionPolicy.FromArguments([]);

        Assert.True(policy.Includes(SmallCase));
        Assert.False(policy.Includes(WorkingCase));
        Assert.False(policy.Includes(ScaleCase));
        Assert.False(policy.Includes(ExternalCase));
    }

    [Fact]
    public void Policy_WhenABroadNameFilterIsGiven_ThenScaleIsStillExcluded()
    {
        // The load-bearing case: `--filter *` must not be able to start a 73M-record run.
        var policy = SelectionPolicy.FromArguments(["--filter", "*"]);

        Assert.True(policy.Includes(SmallCase));
        Assert.False(policy.Includes(ScaleCase));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("*Adult*")]
    [InlineData("*AdultSourceDrain*")]
    public void Policy_WhenOnlyANameFilterIsGiven_ThenExternalIsStillExcluded(string filter)
    {
        // The External equivalent, and the shape that matters most: naming the CASE is not opting
        // in. Someone filtering for `*Adult*` has said which case they mean, not that this run may
        // depend on a third-party host - and if it could opt in, the routine CI Dry run's own
        // `--filter *` would drag the download back into every native job.
        var policy = SelectionPolicy.FromArguments(["--filter", filter]);

        Assert.False(policy.Includes(ExternalCase));
        Assert.False(policy.ExternalRequested);
    }

    [Fact]
    public void Policy_WhenOnlyASurfaceCategoryIsNamed_ThenExternalIsStillExcluded()
    {
        // Naming some other category lifts the Small default - that is deliberate, and tested
        // below for Working - but it must not reach either opt-in tier.
        var policy = SelectionPolicy.FromArguments(["--anyCategories", BenchmarkCategories.Source]);

        Assert.True(policy.CategorySelectionPresent);
        Assert.False(policy.Includes(ExternalCase));
        Assert.False(policy.Includes(ScaleCase));
    }

    [Theory]
    [InlineData("--anyCategories External")]
    [InlineData("--allCategories External")]
    [InlineData("--anyCategories=External")]
    [InlineData("--anyCategories=Source,External")]
    [InlineData("--anyCategories external --filter *")]
    public void Policy_WhenExternalIsNamed_ThenExternalIsSelected(string commandLine)
    {
        // Both category options, both value forms, and case-insensitively - the same shapes the
        // Scale opt-in is tested through, because it is the same guarantee.
        var policy = SelectionPolicy.FromArguments(commandLine.Split(' '));

        Assert.True(policy.ExternalRequested);
        Assert.True(policy.Includes(ExternalCase));
    }

    [Fact]
    public void Policy_WhenSmallAndExternalAreNamedTogether_ThenBothAreSelectedAndScaleIsNot()
    {
        // The union a real-data acceptance run uses when it wants the fixture cases beside the
        // acquired ones.
        var policy = SelectionPolicy.FromArguments(["--anyCategories", "Small", "External"]);

        Assert.True(policy.Includes(SmallCase));
        Assert.True(policy.Includes(ExternalCase));
        Assert.False(policy.Includes(ScaleCase));

        // Working is not asserted here, and deliberately: this policy never vetoes a category the
        // run named, and BenchmarkDotNet's own category filter is what narrows `Small External`
        // down to those two. The opt-in tiers are the exception, because BenchmarkDotNet cannot
        // know that reaching one costs hours or a download.
    }

    [Fact]
    public void Policy_WhenScaleIsNamed_ThenExternalIsNotDraggedInWithIt()
    {
        // The two opt-in tiers are independent: opting into hours of machine time is not opting
        // into a network dependency, and the converse.
        var scale = SelectionPolicy.FromArguments(["--anyCategories", "Scale"]);
        var external = SelectionPolicy.FromArguments(["--anyCategories", "External"]);

        Assert.False(scale.Includes(ExternalCase));
        Assert.False(external.Includes(ScaleCase));
    }

    [Theory]
    [InlineData("--anyCategories")]
    [InlineData("--allCategories")]
    [InlineData("--ANYCATEGORIES")]
    public void Policy_WhenScaleIsNamedAsASeparateValue_ThenScaleIsSelected(string option)
    {
        var policy = SelectionPolicy.FromArguments([option, "Scale"]);

        Assert.True(policy.Includes(ScaleCase));
        Assert.True(policy.ScaleRequested);
    }

    [Theory]
    [InlineData("--anyCategories=Scale")]
    [InlineData("--anyCategories=Source,Scale")]
    [InlineData("--anyCategories=scale")]
    public void Policy_WhenScaleIsNamedInline_ThenScaleIsSelected(string argument)
    {
        Assert.True(SelectionPolicy.FromArguments([argument]).Includes(ScaleCase));
    }

    [Fact]
    public void Policy_WhenScaleIsAmongSeveralNamedCategories_ThenScaleIsSelected()
    {
        var policy = SelectionPolicy.FromArguments(["--anyCategories", "Source", "Scale", "--filter", "*"]);

        Assert.True(policy.Includes(ScaleCase));
    }

    [Fact]
    public void Policy_WhenAnotherCategoryIsNamed_ThenScaleStaysExcludedButTheSmallDefaultLifts()
    {
        var policy = SelectionPolicy.FromArguments(["--anyCategories", "Working"]);

        // BenchmarkDotNet's own category filter narrows to Working; this policy must not veto it,
        // and must still refuse Scale.
        Assert.True(policy.Includes(WorkingCase));
        Assert.True(policy.Includes(SmallCase));
        Assert.False(policy.Includes(ScaleCase));
    }

    [Fact]
    public void Policy_WhenACategoryOptionIsFollowedByAnotherOption_ThenTheOptionIsNotReadAsAValue()
    {
        var policy = SelectionPolicy.FromArguments(["--anyCategories", "--filter", "Scale"]);

        Assert.True(policy.CategorySelectionPresent);
        Assert.False(policy.ScaleRequested);
    }

    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--filter", "*" }, false)]
    [InlineData(new[] { "--job", "dry" }, true)]
    [InlineData(new[] { "--JOB=short" }, true)]
    public void Policy_ShouldDetectAnExplicitlyRequestedJob(string[] arguments, bool expected)
    {
        // A job named on the command line replaces the suite's own, so the run cannot silently
        // execute every case twice.
        Assert.Equal(expected, SelectionPolicy.FromArguments(arguments).JobRequested);
    }

    [Fact]
    public void LongRun_ShouldFollowTheWorkingAndScaleTiersAndNothingElse()
    {
        // The job shape is a consequence of the selection, not a separate switch: a tier whose
        // operations take seconds needs Monitoring, and the default Small tier does not.
        Assert.False(SelectionPolicy.FromArguments([]).LongRunRequested);
        Assert.False(SelectionPolicy.FromArguments(["--filter", "*"]).LongRunRequested);
        Assert.False(SelectionPolicy.FromArguments(["--anyCategories", "Small"]).LongRunRequested);

        Assert.True(SelectionPolicy.FromArguments(["--anyCategories", "Working"]).LongRunRequested);
        Assert.True(SelectionPolicy.FromArguments(["--anyCategories", "Scale"]).LongRunRequested);
        Assert.True(SelectionPolicy.FromArguments(["--anyCategories=Working,Small"]).LongRunRequested);

        // External is opt-in for a different reason: about 32,000 records is Small-sized work, so
        // it keeps the fresh-iteration job. Being opt-in and being long-running are separate facts.
        Assert.False(SelectionPolicy.FromArguments(["--anyCategories", "External"]).LongRunRequested);
        Assert.True(SelectionPolicy.FromArguments(["--anyCategories=External,Working"]).LongRunRequested);
    }

    [Fact]
    public void Working_ShouldNotBeReachableWithoutNamingIt()
    {
        // Working is opt-in the same way Scale is: it reads 730,000 records per case, and a bare
        // run must stay fast enough that nobody hesitates to make one.
        var bare = SelectionPolicy.FromArguments([]);
        var opted = SelectionPolicy.FromArguments(["--anyCategories", "Working"]);

        Assert.False(bare.Includes(["Working", "Convert"]));
        Assert.True(opted.Includes(["Working", "Convert"]));
    }
}
