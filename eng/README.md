# eng/ — the packaging and smoke commands

Two distributions of one program, and the commands that produce and check them. Nothing here is a
build system: every command is a thin wrapper over `dotnet`, or a documented `dotnet` invocation with
a gate set. If a step could be a plain command, it is one.

## The two distributions

| | Global tool | Self-contained folder |
| --- | --- | --- |
| What it is | `FcaBedrock.Cli` packed as a .NET global tool, command `fcabedrock` | a folder carrying the .NET runtime beside `FcaBedrock.Cli(.exe)`, plus a zip of it |
| Needs | a matching .NET runtime already installed | no .NET installed; still needs the OS's own prerequisites |
| Produced by | `dotnet pack src/FcaBedrock.Cli` | `eng/publish-selfcontained.ps1` |
| Checked by | `ToolSmokeTests` (gated) | `SelfContainedSmokeTests` (gated) |

The self-contained executable keeps its **assembly** name rather than taking the tool's command name.
The command name belongs to the global-tool shim; giving the same binary a second name would make the
two distributions disagree about what the program is called.

## Publish and archive

```pwsh
# The running platform.
./eng/publish-selfcontained.ps1

# A specific runtime identifier. PREPARATION ONLY - see below.
./eng/publish-selfcontained.ps1 -Rid linux-x64
```

Output goes to `artifacts/publish/<rid>/` with `artifacts/publish/fcabedrock-<rid>.zip` beside it.
That tree is ignored by Git.

The required release archives are `win-x64`, `linux-x64`, and `osx-arm64`.

The zip is written entry by entry rather than with `Compress-Archive`, for one reason: a zip carries
a file's Unix mode in its own metadata, and `Compress-Archive` records `0100644` for every entry. On
Linux and macOS the apphost is therefore recorded **`0100755`** and every other entry left
`0100644`, so `unzip` produces a `FcaBedrock.Cli` that can actually be run. Nothing else about the
archive changed: a flat payload, relative names, the published files' own timestamps, and entries in
ordinal name order.

> **Cross-publishing is not evidence.** A folder produced for another platform shows that the SDK can
> emit files for it and says nothing about whether the result runs there. Only a publish executed
> *on* the target platform, followed by the smoke below, is evidence — which is why the required
> native targets run on their own machines rather than being cross-published from one.
>
> The archive says so too: a zip records the platform that created it, and an extractor reads the DOS
> attributes instead of the Unix mode when that platform is not Unix. Cross-publishing a Unix RID
> from Windows still produces a zip, and the command warns that its modes will not survive
> extraction.

## The smokes

Both are ordinary tests, gated by an environment variable so they always report as *skipped* rather
than silently not existing. Neither touches the machine: each works inside one disposable directory,
installs nothing globally, and consults no network feed.

```pwsh
# The packed global tool: pack, install to a private tool path from a local feed, run a real
# convert, check the manifest, uninstall.
$env:FCABEDROCK_TOOL_SMOKE = '1'
dotnet test tests/FcaBedrock.Cli.Tests -c Release --filter-class '*ToolSmokeTests*'

# The self-contained distribution: run the publish script above for the RUNNING rid, check the
# runtime is bundled, inspect the archive it produced, EXTRACT that archive, and make every
# behavioural check against the extracted apphost - the SDK's environment removed, context bytes
# compared against this process's, a real BCP-47 locale resolved, a refused convert committing
# nothing.
$env:FCABEDROCK_SELFCONTAINED_SMOKE = '1'
dotnet test tests/FcaBedrock.Cli.Tests -c Release --filter-class '*SelfContainedSmokeTests*'
```

Running the extracted archive rather than the publish folder is the point, not a detail: the folder
is not what a user receives. `FCABEDROCK_SELFCONTAINED_OUTPUT` names where the script writes, so CI
verifies and then uploads the same file; unset, the smoke uses its own disposable directory.

The archive writer itself is checked separately and *ungated*, in `DistributionArchiveTests`: it
archives a folder the test controls through `-ArchiveOnly` and reads the recorded modes back, so
every platform answers "is the Linux apphost executable?" in seconds without publishing a runtime.

## The ordinary checks

```pwsh
dotnet build FcaBedrock.slnx -c Release          # warnings are errors
dotnet test --solution FcaBedrock.slnx -c Release
```

The benchmark host is deliberately unreachable from `dotnet test` (P-20): it carries its own
`Directory.Build.props` so it is not a test project, and a multi-minute benchmark can never start
because someone ran the test suite.

## The benchmark smoke

```pwsh
# Prepare what the smoke selects: both corpora are generated here, so this needs no network.
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- prepare micro small

# Proves the harness runs. A Dry job measures nothing and is never a performance result.
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --anyCategories Small --filter '*' --job dry
```

This is the smoke CI runs on every native target, and what it proves is exactly what it selects: the
Small-category cases and their oracles on that platform. The two opt-in tiers are outside it —
`Scale` because it costs hours, `External` because its corpus is acquired from a third-party host —
and neither is reachable by a name filter, so the same command is safe to type anywhere.

The real-data (`External`) cases are run explicitly instead, on the final Windows x64 candidate:

```pwsh
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- prepare adult          # downloads
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --anyCategories External --filter '*' --job dry
```

That run is a **blocking** acceptance obligation for M8 and for each release candidate, not an
optional extra: routine CI can be green while it is still outstanding. It is enforced by review and
by the closure checklist rather than by a status check — see D-124 and `docs/roadmap.md`.

Real runs, corpus preparation, and the target-scale tiers are documented in
`tests/FcaBedrock.Benchmarks/README.md`; the evidence they produce is `docs/benchmarks.md`.
