# eng/ — the packaging and smoke commands

Two distributions of one program, and the commands that produce and check them. Nothing here is a
build system: every distribution command is a thin wrapper over `dotnet`, or a documented `dotnet`
invocation with an environment variable set, and the authored-text integrity check below is a standalone PowerShell 7
script that needs no build. If a step could be a plain command, it is one.

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
`0100644`, so `unzip` produces a `FcaBedrock.Cli` that can be run. Everything else about the
archive is ordinary: a flat payload, relative names, the published files' own timestamps, and entries in
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
than silently not existing. Each writes its distribution into a disposable directory (unless
`FCABEDROCK_SELFCONTAINED_OUTPUT`, below, names another) and installs nothing globally. The tool
smoke installs only from its own local feed; the self-contained smoke publishes through the script
above, whose restore uses the machine's configured NuGet sources.

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

The benchmark host is deliberately unreachable from `dotnet test` (EP-20): it carries its own
`Directory.Build.props` so it is not a test project, and a multi-minute benchmark can never start
because someone ran the test suite.

## Authored-text integrity

```pwsh
# The whole authored set that eng/authored-files.txt defines.
pwsh -NoProfile -File eng/check-authored-text.ps1

# Chosen files or directories; the whole manifest is still validated.
pwsh -NoProfile -File eng/check-authored-text.ps1 -Path docs/roadmap.md

# The command's own behaviour tests: offline, with no restore or build first.
pwsh -NoProfile -File eng/check-authored-text.tests.ps1
```

`check-authored-text.ps1` is read-only and offline. It checks strict UTF-8 and control characters,
the obsolete engineering-principle identifier, file name and stage-label spellings, and the two
instruction entry points: the writing-policy sentence in `AGENTS.md` and the `@AGENTS.md` import in
`CLAUDE.md`. Each output line has five tab-separated fields: rule, path, line, severity and message.
Exit 0 means no owned violation was found, and 1 means at least one content error. Exit 2 means the
command refused to complete the assessment, for an invalid manifest or selection, a reparse point,
an unreadable file, an undecodable `.cs` under `src/` or `tests/`, or an internal failure; it takes
precedence over 1. Several `-Path` operands need an in-session call, such as
`./eng/check-authored-text.ps1 -Path docs, README.md`, because `pwsh -File` passes a comma list as
one operand.

The command does not check local links, fragments or writing style, and it does not own the bytes
of `.cs` under `src/` and `tests/`, which `tests/FcaBedrock.Architecture.Tests/SourceHygieneTests.cs`
checks. Exit 0 says nothing about any of those.

A change that adds or edits a local link, renames or moves its target, changes a target heading or
explicit anchor, or changes structure that could alter whether a link or heading renders needs a
reviewer to check each affected link against the changed headings. D-130 records the review table
and the rest of that rule.

The runner exits 2 when its comparator self-check fails. Otherwise it exits 1 when a case fails,
when no case runs, or when the session directory is kept because its cleanup was withheld or
failed; it exits 0 only when at least one case ran, none failed and the session directory was
removed. `-Filter <text>` runs only the cases whose names contain that text, and a case this host
cannot set up is reported as skipped with its reason.

## The benchmark smoke

```pwsh
# Prepare what the smoke selects: both corpora are generated here, so preparing them downloads nothing.
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- prepare micro small

# Proves the harness runs. A Dry job measures nothing and is never a performance result.
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --anyCategories Small --filter '*' --job dry
```

This is the smoke CI runs on every native target, and what it proves is exactly what it selects: the
Small-category cases and their oracles on that platform. The three opt-in tiers are outside it
(`Working` and `Scale` because they cost minutes to hours, `External` because its corpus is
acquired from a third-party host), and none is reachable by a name filter, so the same command is
safe to type anywhere.

The real-data (`External`) cases are run explicitly instead, on the final Windows x64 candidate:

```pwsh
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- prepare adult          # downloads
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --anyCategories External --filter '*' --job dry
```

That run covers all three UCI Adult cases: the source drain, the `.dat` conversion and the `.cxt`
conversion. With the current delimited reader and the pinned corpus at acquisition revision 3
(32,561 records), it is a **blocking** acceptance obligation before M8 as a whole is accepted and
for each release candidate, not an optional extra: routine CI can be green while it is still
outstanding, and a result from an earlier acquisition revision does not count. It is enforced by
review rather than by a status check (D-124, `docs/roadmap.md`).

Real runs, corpus preparation, and the target-scale tiers are documented in
`tests/FcaBedrock.Benchmarks/README.md`; the evidence they produce is `docs/benchmarks.md`.
