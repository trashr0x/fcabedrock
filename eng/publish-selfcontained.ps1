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
    [switch] $SkipArchive
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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

$project = Join-Path $repository 'src' 'FcaBedrock.Cli' 'FcaBedrock.Cli.csproj'
$publish = Join-Path $OutputRoot $Rid

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

# The apphost is named from the assembly, not from the tool command. There is deliberately no rename
# to `fcabedrock`: the command name belongs to the global-tool shim, and a second name for the same
# binary would make the two distributions disagree about what the program is called.
$executable = if ($Rid -like 'win-*') { 'FcaBedrock.Cli.exe' } else { 'FcaBedrock.Cli' }
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
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $archive -CompressionLevel Optimal

$size = [math]::Round((Get-Item $archive).Length / 1MB, 1)
Write-Host "published $Rid : $executable, archive ${size} MiB"
