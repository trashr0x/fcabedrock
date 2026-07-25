using System.Reflection;

namespace FcaBedrock.Cli;

/// <summary>
/// The single tool-version string (D-122 part 1): the same value <c>--version</c>
/// prints and the run manifest records as <c>tool_version</c> (§15) — one fact, one
/// owner, so the two can never disagree.
/// <para>
/// Form: <c>fcabedrock-vnext &lt;version&gt;</c>, where the version is the assembly's
/// informational version with any <c>+metadata</c> suffix removed. The suffix is build
/// metadata (a commit id, when a build supplies one) and would make an audit field
/// vary with the build host rather than with the release.
/// </para>
/// </summary>
internal static class ToolVersion
{
    /// <summary>The fixed product prefix.</summary>
    internal const string Prefix = "fcabedrock-vnext ";

    /// <summary>The version string for the running tool.</summary>
    public static string Current { get; } = Compose(typeof(ToolVersion).Assembly);

    /// <summary>Composes the version string for <paramref name="assembly"/>.</summary>
    internal static string Compose(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        return Prefix + StripBuildMetadata(version);
    }

    /// <summary>Removes a SemVer <c>+metadata</c> suffix; returns <paramref name="version"/> unchanged when there is none.</summary>
    internal static string StripBuildMetadata(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }
}
