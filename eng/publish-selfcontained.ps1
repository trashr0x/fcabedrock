#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Publishes and archives the self-contained `fcabedrock` distribution for one runtime identifier.

.DESCRIPTION
    The global tool is framework-dependent: it needs a matching .NET runtime already on the machine.
    This produces the other distribution - a folder carrying the runtime beside the executable, and a
    zip of that folder - for a user who has no .NET installed.

    It is deliberately a thin wrapper over `dotnet publish`. Nothing here decides anything the project
    file does not already decide, with one exception: `PackAsTool` is overridden, because a tool
    package and a self-contained apphost are two distributions of the same program and the SDK will
    not emit the second while the first is declared.

    What it does NOT do, on purpose: no trimming, no single-file bundle, no ahead-of-time
    compilation, and no invariant globalization. Each of those changes what the program does, and
    this is meant to be the same program in a different wrapper. Trimming in particular would need
    its own correctness evidence, which M8 does not have.

.PARAMETER Rid
    The runtime identifier to publish for. Defaults to the running one.

    A cross-published folder is PREPARATION, never proof: it shows the SDK can emit files for another
    platform and says nothing about whether the result runs there. Only a publish executed on the
    target platform, followed by the gated self-contained smoke, is evidence.

.PARAMETER OutputRoot
    Where the publish folder and the archive are written. Defaults to `artifacts/publish` at the
    repository root, which is ignored by Git.

.PARAMETER SkipArchive
    Publish without producing the zip.

.PARAMETER ArchiveOnly
    Archive an existing publish folder without republishing it.

    The same archive code as an ordinary run - it skips the `dotnet publish` in front of it and
    nothing else - so a test can exercise the writer against a folder it controls in seconds rather
    than by publishing a whole runtime. It is not a second way to produce a distribution: the folder
    must already be there, and what comes out is the same archive the full command produces.

.EXAMPLE
    ./eng/publish-selfcontained.ps1
    Publish and archive for the running platform.

.EXAMPLE
    ./eng/publish-selfcontained.ps1 -Rid linux-x64
    Cross-publish for Linux. Preparation only - run the smoke on Linux to have evidence.
#>
[CmdletBinding()]
param(
    [string] $Rid = '',
    [string] $OutputRoot = '',
    [switch] $SkipArchive,
    [switch] $ArchiveOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The zip external-attribute values this writer records on a Linux or macOS distribution: a regular
# file readable by all, and the same plus the execute bits for the apphost. They are `0100644` and
# `0100755` in the octal form `ls` prints, shifted into the high half of the external-attributes
# field where the zip format keeps a Unix mode. A Windows distribution records neither - it has no
# Unix mode to claim - and the writer says so with an explicit zero rather than by assigning nothing.
$RegularFileAttributes = 0x81A4 -shl 16
$ExecutableFileAttributes = 0x81ED -shl 16

<#
.SYNOPSIS
    Writes the distribution archive for one published folder.

.DESCRIPTION
    Entry by entry rather than through `Compress-Archive`, for one reason: on Linux and macOS the
    apphost has to be recorded as EXECUTABLE. `Compress-Archive` gives every entry the default Unix
    mode `0100644`, so unzipping a Linux distribution produced `-rw-r--r-- FcaBedrock.Cli` and the
    documented `./FcaBedrock.Cli` could not be run at all. The mode lives in the ARCHIVE - the zip's
    external-attributes field - so recording it is the archive writer's job; the published file's own
    mode does not survive a zip that carries none.

    The mode field is written for EVERY entry, and what it says is decided by the TARGET rather than
    by the machine doing the writing. `ZipArchive.CreateEntry` leaves a host-dependent default there:
    zero on a Windows host, and the creating platform's own mode on Linux and macOS. So a `win-*`
    archive assigned nothing would record `0100644` when it happened to be built on a Unix machine -
    a Unix claim about a distribution that has none to make - and the same folder would produce two
    different archives depending on where the command ran.

    Everything else is deliberately what it already was: a flat payload, one entry per published file
    under its relative name with forward slashes, ordinary files still non-executable, and each
    entry's last-write time taken from the file so the payload's build-time provenance survives the
    round trip. Entries are written in ordinal name order, so one folder always produces one
    sequence.

    Names are checked rather than assumed. A rooted name, a `..` segment, a volume qualifier, a link,
    or two names that collide on a case-insensitive filesystem all stop the archive: an archive is
    something a user extracts, and every one of those is a way for extraction to write somewhere it
    was not asked to.
#>
function Write-DistributionArchive {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $SourceDirectory,
        [Parameter(Mandatory)] [string] $ArchivePath,
        [Parameter(Mandatory)] [string] $ExecutableName,
        [Parameter(Mandatory)] [bool]   $Unix
    )

    $root = (Resolve-Path -LiteralPath $SourceDirectory).ProviderPath
    $byName = [System.Collections.Generic.Dictionary[string, System.IO.FileInfo]]::new(
        [System.StringComparer]::Ordinal)
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -Force -File) {
        if ($file.Attributes.HasFlag([System.IO.FileAttributes]::ReparsePoint)) {
            throw "the publish folder holds a link at '$($file.FullName)'; a distribution archive carries files only."
        }

        $name = [System.IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
        if ([System.IO.Path]::IsPathRooted($name) -or $name.StartsWith('/') -or $name.Contains(':')) {
            throw "the publish folder produced the unsafe entry name '$name'."
        }

        if ($name -split '/' | Where-Object { $_ -eq '..' -or $_ -eq '.' -or $_ -eq '' }) {
            throw "the publish folder produced the unsafe entry name '$name'."
        }

        if (-not $seen.Add($name)) {
            throw "two published files share the name '$name' where case does not distinguish them."
        }

        $byName[$name] = $file
    }

    if ($byName.Count -eq 0) {
        throw "there is nothing to archive in '$root'."
    }

    if (-not $byName.ContainsKey($ExecutableName)) {
        throw "the publish folder holds no '$ExecutableName' to archive."
    }

    $ordered = [System.Collections.Generic.List[string]]::new([string[]] $byName.Keys)
    $ordered.Sort([System.StringComparer]::Ordinal)

    # CreateNew, not Create: the caller removes an existing archive first, so anything at this path
    # now is something else, and overwriting it is not this command's business.
    $stream = [System.IO.File]::Open(
        $ArchivePath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try {
        $zip = [System.IO.Compression.ZipArchive]::new(
            $stream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            foreach ($name in $ordered) {
                $file = $byName[$name]
                $entry = $zip.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [System.DateTimeOffset] $file.LastWriteTime

                # Every entry, every target: the archive states its own mode rather than
                # inheriting whatever the creating host's default happens to be.
                $entry.ExternalAttributes = if (-not $Unix) {
                    0
                } elseif ($name -ceq $ExecutableName) {
                    $ExecutableFileAttributes
                } else {
                    $RegularFileAttributes
                }

                $target = $entry.Open()
                try {
                    $source = [System.IO.File]::OpenRead($file.FullName)
                    try { $source.CopyTo($target) } finally { $source.Dispose() }
                } finally { $target.Dispose() }
            }
        } finally { $zip.Dispose() }
    } finally { $stream.Dispose() }

    return $ordered.Count
}

# The repository root is wherever the solution file is, found by walking up - never a fixed number
# of `..` hops, so the script works from any working directory.
$repository = $PSScriptRoot
while ($repository -and -not (Test-Path (Join-Path $repository 'FcaBedrock.slnx'))) {
    $parent = Split-Path -Parent $repository
    if ($parent -eq $repository) { throw 'FcaBedrock.slnx was not found above this script.' }
    $repository = $parent
}

if (-not $Rid) {
    $Rid = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
}

if (-not $OutputRoot) {
    $OutputRoot = Join-Path $repository 'artifacts' 'publish'
}

if ($SkipArchive -and $ArchiveOnly) {
    throw '-SkipArchive and -ArchiveOnly ask for opposite things.'
}

$project = Join-Path $repository 'src' 'FcaBedrock.Cli' 'FcaBedrock.Cli.csproj'
$publish = Join-Path $OutputRoot $Rid

if ($ArchiveOnly) {
    if (-not (Test-Path -LiteralPath $publish -PathType Container)) {
        throw "-ArchiveOnly needs an existing publish folder; '$publish' is not there."
    }
} else {
    if (Test-Path $publish) {
        Remove-Item -Recurse -Force $publish
    }

    New-Item -ItemType Directory -Force -Path $publish | Out-Null

    Write-Host "publishing $Rid -> $publish"
    & dotnet publish $project `
        -c Release `
        -r $Rid `
        --self-contained true `
        -p:PackAsTool=false `
        -o $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
}

# The apphost is named from the assembly, not from the tool command. There is deliberately no rename
# to `fcabedrock`: the command name belongs to the global-tool shim, and a second name for the same
# binary would make the two distributions disagree about what the program is called.
$unix = $Rid -notlike 'win-*'
$executable = if ($unix) { 'FcaBedrock.Cli' } else { 'FcaBedrock.Cli.exe' }
$executablePath = Join-Path $publish $executable
if (-not (Test-Path $executablePath)) {
    throw "the publish produced no '$executable' in '$publish'."
}

if ($SkipArchive) {
    Write-Host "published $Rid (archive skipped)"
    return
}

$archive = Join-Path $OutputRoot "fcabedrock-$Rid.zip"
if (Test-Path $archive) { Remove-Item -Force $archive }

Write-Host "archiving -> $archive"
$count = Write-DistributionArchive `
    -SourceDirectory $publish `
    -ArchivePath $archive `
    -ExecutableName $executable `
    -Unix $unix

# A Unix mode recorded on a zip written by a Windows host is a mode most extractors will ignore: the
# format keeps the creating platform beside it, and `unzip` reads the DOS attributes instead when
# that platform is not Unix. The archive is still produced - cross-publishing is a documented
# preparation step - but saying so is the difference between preparation and a delivery archive
# nobody can run.
if ($unix -and -not ($IsLinux -or $IsMacOS)) {
    Write-Warning (
        "this $Rid archive was written on a non-Unix host, so its recorded file modes will not " +
        'survive an ordinary extraction. Cross-publishing is preparation; produce the delivery ' +
        'archive on the target platform.')
}

$size = [math]::Round((Get-Item $archive).Length / 1MB, 1)
Write-Host "published $Rid : $executable, $count entries, archive ${size} MiB"
