# FcaBedrock

`fcabedrock` is a command-line tool for **Formal Concept Analysis (FCA)
preprocessing**. It ingests structured data — wide CSV/TSV, or
subject–predicate–value triples — applies a user-curated *Bedrock spec*
describing how raw values become formal-context attributes via conceptual
scaling, and emits deterministic Burmeister `.cxt` and FIMI `.dat` formal
contexts for downstream FCA tools.

## Install

`FcaBedrock.Cli` is a **technical preview**. It is **not yet published to a
public NuGet feed**, so the command below applies only once the package is
available on a NuGet source you have already configured:

```text
dotnet tool install --global FcaBedrock.Cli
```

Until then, the route is a local pack from a clone of the sources:

```text
dotnet pack src/FcaBedrock.Cli/FcaBedrock.Cli.csproj -c Release -o <dir>
dotnet tool install --global --add-source <dir> FcaBedrock.Cli
```

Both routes need the .NET 10 SDK.

## Use

```text
fcabedrock convert spec.toml data.csv --out out/context --format both
```

The eight commands are `convert`, `validate`, `plan`, `stats`, `calibrate`,
`probe`, `migrate` and `fingerprint`. `fcabedrock --help` prints the full
grammar; `fcabedrock --version` prints the tool version. To remove the tool:

```text
dotnet tool uninstall --global FcaBedrock.Cli
```

## Technical preview

Validated on **x64**, with Windows as the primary host; broader platform
validation follows at M8. The global tool is explicitly a **temporary**
technical-preview distribution — a standalone route is committed for the public
release.

## License

MIT.

Copyright © Constantinos Orphanides.
