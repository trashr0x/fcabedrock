#Requires -Version 7.0
<#
.SYNOPSIS
    Checks the mechanical integrity of the authored text that eng/authored-files.txt selects (D-130).

.DESCRIPTION
    Reads every selected file and decides each finding on its raw decoded text, on its repository
    path, or on two fixed instruction-entry predicates. It parses no Markdown and no source
    language, so no syntax can hide a finding and none can create one.

    It reports:
      - invalid UTF-8 and prohibited control characters (C0 other than TAB, LF and CR, plus DEL);
      - the obsolete engineering-principle identifier, file name and stage-label spellings, as
        three case-sensitive .NET regular expressions over the whole decoded text, and the file
        name expression over each selected path as well;
      - a missing required file, the mandatory writing-policy sentence absent from AGENTS.md, and a
        first nonblank line of CLAUDE.md other than the exact @AGENTS.md import;
      - every condition that stops it from completing that assessment.

    It does not assess local links, fragments or writing style, and it does not own the bytes of
    .cs under src/ and tests/: tests/FcaBedrock.Architecture.Tests/SourceHygieneTests.cs does, so
    this command decodes those files for the spelling checks and runs no control-character check
    on them. Exit 0 is not a statement about any of those.

    The command is read-only and offline. It assumes a stable, trusted checkout: an edit during a
    run invalidates the result, and the command is not a security boundary. A reparse point met on
    the root's own path from the file-system root, on the manifest's access path or on the walk is
    refused at that segment and never followed.

.PARAMETER RepoRoot
    The repository root. Defaults to the parent of the directory that holds this script.

.PARAMETER Path
    Repository-relative files or directories to check instead of the whole selected set. The
    whole manifest is still validated, its floor still applies to the whole set, and the
    instruction-entry checks still run. Each operand must select at least one file of that set.

.OUTPUTS
    UTF-8 text on standard output, one LF-terminated record per line, five TAB-separated fields:
    rule, repository-relative path or '-', line or 0, severity (fatal, error or info), message.
    Records are ordered fatal before error, then by path, line, rule and message, all ordinal; the
    last two records are checker-scope and checker-summary.

    Exit 0: the manifest and selection were valid, every selected file was decoded or accounted,
    and no owned violation was found. Exit 1: at least one content error. Exit 2: the command
    refused to complete the assessment; this takes precedence over exit 1.

.EXAMPLE
    pwsh -NoProfile -File eng/check-authored-text.ps1
    Check the whole authored set.

.EXAMPLE
    pwsh -NoProfile -File eng/check-authored-text.ps1 -Path docs/roadmap.md
    Check one file. Several operands need an in-session call, such as
    ./eng/check-authored-text.ps1 -Path docs, README.md, because pwsh -File passes a comma list
    as one operand.
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [string] $RepoRoot = '',
    [string[]] $Path = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:Findings = [Collections.Generic.List[object]]::new()
$script:FatalCount = 0
$script:Completing = $false
$script:Selected = 0
$script:Decoded = 0
$script:OwnedElsewhere = 0
$script:Refused = 0
$script:NewlineIndex = $null

# Records go straight to the standard output stream as UTF-8 with LF line ends, so the bytes are
# the same on every host and code page. Nothing is written through the PowerShell pipeline.
$script:Output = [IO.StreamWriter]::new([Console]::OpenStandardOutput(), [Text.UTF8Encoding]::new($false))

$ManifestPath = 'eng/authored-files.txt'
$RequiredContent = @('AGENTS.md', 'CLAUDE.md', 'docs/writing-principles.md')
$RequiredFiles = $RequiredContent + $ManifestPath
$PolicySentence = 'Read `docs/writing-principles.md` before authoring or editing any prose or comment in this repository.'
$ImportLine = '@AGENTS.md'
$SourceHygieneOwner = 'tests/FcaBedrock.Architecture.Tests/SourceHygieneTests.cs'
$ScopeMessage = 'this command does not assess local links or fragments or writing style, and does not own ' +
    "byte hygiene for .cs under src/ and tests/, which $SourceHygieneOwner owns"
$UnreadableMessage = 'the file could not be read, so no check ran on it'
$InvalidUtf8Message = 'the file is not valid UTF-8, so no other check ran on it'

# Each required file is read at most once per run, so the selected-content checks and the fixed
# checks see one outcome for it and report it once.
$script:RequiredReads = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
foreach ($required in $RequiredFiles) { $script:RequiredReads.Add($required, $null) }

$StrictUtf8 = [Text.UTF8Encoding]::new($false, $true)

# The three spelling predicates. Written as regular expressions, none of them matches its own
# source text, so this file passes the check it implements.
$IdentifierPattern = [regex]::new('\bP-\d+\b')
$FileNamePattern = [regex]::new('(?<![\w-])principles\.md')
$StageLabelPattern = [regex]::new('\bP[0-3]\b')
$ControlPattern = [regex]::new('[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]')

$NewlinePattern = [regex]::new('\n')
$WhitespaceRun = [regex]::new('\s+')
$RecordSeparator = [regex]::new('[\t\r\n]')
$DrivePrefix = [regex]::new('\A[A-Za-z]:')
$DirectivePattern = [regex]::new('\A(?<kind>[+-])[ \t]+(?<glob>.+)\z')
$FloorPattern = [regex]::new('\Amin-files[ \t]+(?<count>[1-9][0-9]{0,8})\z')
$TrailingBlank = [char[]]"`r`n `t"

function Add-Finding {
    param([string] $RuleId, [string] $Location, [int] $LineNumber, [string] $Severity, [string] $Message)
    $rank = if ($Severity.Equals('fatal')) { 0 } else { 1 }
    if ($rank -eq 0) { $script:FatalCount++ }
    $script:Findings.Add([pscustomobject]@{
        Rule = $RuleId; Path = $Location; Line = $LineNumber; Severity = $Severity; Rank = $rank; Message = $Message
    })
}

# A path or message can carry a TAB, CR or LF only through a file name or a -Path operand; the
# record grammar has five fields on one line, so those characters become spaces.
function Write-Record {
    param([string] $RuleId, [string] $Location, [int] $LineNumber, [string] $Severity, [string] $Message)
    $fields = @(
        $RuleId
        $RecordSeparator.Replace($Location, ' ')
        $LineNumber.ToString([cultureinfo]::InvariantCulture)
        $Severity
        $RecordSeparator.Replace($Message, ' ')
    )
    $script:Output.Write([string]::Join("`t", $fields) + "`n")
}

function Complete-Run {
    $script:Completing = $true
    $script:Findings.Sort([Comparison[object]] {
        param($left, $right)
        $order = $left.Rank - $right.Rank
        if ($order -eq 0) { $order = [string]::CompareOrdinal($left.Path, $right.Path) }
        if ($order -eq 0) { $order = $left.Line - $right.Line }
        if ($order -eq 0) { $order = [string]::CompareOrdinal($left.Rule, $right.Rule) }
        if ($order -eq 0) { $order = [string]::CompareOrdinal($left.Message, $right.Message) }
        $order
    })
    $errors = 0
    foreach ($finding in $script:Findings) {
        if ($finding.Rank -eq 1) { $errors++ }
        Write-Record $finding.Rule $finding.Path $finding.Line $finding.Severity $finding.Message
    }
    Write-Record 'checker-scope' '-' 0 'info' $ScopeMessage
    Write-Record 'checker-summary' '-' 0 'info' ("selected=$($script:Selected) decoded=$($script:Decoded) " +
        "owned-elsewhere=$($script:OwnedElsewhere) refused=$($script:Refused) errors=$errors")
    $script:Output.Flush()
    if ($script:FatalCount -gt 0) { exit 2 }
    if ($errors -gt 0) { exit 1 }
    exit 0
}

# A run-level refusal: nothing after it can be assessed, and nothing collected before it is lost.
function Stop-Run {
    param([string] $RuleId, [string] $Location, [string] $Message)
    Add-Finding $RuleId $Location 0 'fatal' $Message
    Complete-Run
}

# Reads the entry itself, never its target: a missing path reports attributes of -1, and a link
# reports its own reparse attribute whether or not its target exists.
function Get-EntryKind {
    param([string] $FullPath)
    $attributes = [IO.FileInfo]::new($FullPath).Attributes
    if ([int]$attributes -eq -1) { return 'missing' }
    if ($attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { return 'link' }
    if ($attributes.HasFlag([IO.FileAttributes]::Directory)) { return 'directory' }
    return 'file'
}

# The manifest is read before the walk, because the walk takes its pruning from the manifest; so
# every existing segment from the file-system root down to the repository root, and each segment
# of the manifest's access path, is inspected here first. An ancestor that is a reparse point is
# named by its own segment of the root's spelling, never by anything it points to.
function Assert-ManifestAccess {
    $ancestor = [IO.Path]::GetPathRoot($script:Root)
    $segment = $ancestor
    foreach ($name in $script:Root.Substring($ancestor.Length).Split([IO.Path]::DirectorySeparatorChar, [StringSplitOptions]::RemoveEmptyEntries)) {
        $kind = Get-EntryKind $ancestor
        if ($kind.Equals('link')) { Stop-Run 'checker-path-escape' '-' "the ancestor '$segment' of the repository root is a reparse point and is not followed" }
        if (-not $kind.Equals('directory')) { break }
        $ancestor = [IO.Path]::Combine($ancestor, $name)
        $segment = $name
    }
    $kind = Get-EntryKind $script:Root
    if ($kind.Equals('link')) { Stop-Run 'checker-path-escape' '-' 'the repository root is a reparse point and is not followed' }
    if (-not $kind.Equals('directory')) { Stop-Run 'checker-manifest-missing' '-' 'the repository root is not an existing directory' }
    $kind = Get-EntryKind ([IO.Path]::Combine($script:Root, 'eng'))
    if ($kind.Equals('link')) { Stop-Run 'checker-path-escape' 'eng' 'a reparse point is refused at this segment and is not followed' }
    if ($kind.Equals('directory')) {
        $kind = Get-EntryKind ([IO.Path]::Combine($script:Root, $ManifestPath))
        if ($kind.Equals('link')) { Stop-Run 'checker-path-escape' $ManifestPath 'a reparse point is refused at this segment and is not followed' }
    }
    if (-not $kind.Equals('file')) { Stop-Run 'checker-manifest-missing' $ManifestPath 'the manifest does not exist as a regular file' }
}

# Strict UTF-8, as SourceHygieneTests decodes: an invalid byte is never replaced. A UTF-8 byte
# order mark is accepted and is not part of the text. A required file's outcome is kept for the run.
function Read-FileText {
    param([string] $File)
    $isRequired = $script:RequiredReads.ContainsKey($File)
    if ($isRequired -and $null -ne $script:RequiredReads[$File]) { return $script:RequiredReads[$File] }
    $read = [pscustomobject]@{ State = 'decoded'; Text = $null }
    try {
        $bytes = [IO.File]::ReadAllBytes([IO.Path]::Combine($script:Root, $File))
        $start = 0
        if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { $start = 3 }
        $read.Text = $StrictUtf8.GetString($bytes, $start, $bytes.Length - $start)
    }
    catch [IO.IOException], [UnauthorizedAccessException] { $read.State = 'unreadable' }
    catch [Text.DecoderFallbackException] { $read.State = 'undecodable' }
    if ($isRequired) { $script:RequiredReads[$File] = $read }
    return $read
}

# '**/' matches zero or more whole segments, any other '**' matches across segments, and '*'
# stays inside one segment. Matching is case-sensitive, so a result does not depend on whether
# the file system folds case.
function ConvertTo-GlobRegex {
    param([string] $Pattern)
    $builder = [Text.StringBuilder]::new('\A')
    $position = 0
    while ($position -lt $Pattern.Length) {
        if ([string]::CompareOrdinal($Pattern, $position, '**/', 0, 3) -eq 0) { [void]$builder.Append('(?:.*/)?'); $position += 3 }
        elseif ([string]::CompareOrdinal($Pattern, $position, '**', 0, 2) -eq 0) { [void]$builder.Append('.*'); $position += 2 }
        elseif ([string]::CompareOrdinal($Pattern, $position, '*', 0, 1) -eq 0) { [void]$builder.Append('[^/]*'); $position += 1 }
        else { [void]$builder.Append([regex]::Escape($Pattern.Substring($position, 1))); $position += 1 }
    }
    [void]$builder.Append('\z')
    return [regex]::new($builder.ToString(), [Text.RegularExpressions.RegexOptions]::Singleline)
}

function Test-GlobSyntax {
    param([string] $Pattern)
    if ($Pattern.StartsWith('/', [StringComparison]::Ordinal) -or $Pattern.Contains('\') -or $DrivePrefix.IsMatch($Pattern)) { return $false }
    foreach ($segment in $Pattern.Split('/')) {
        if ($segment.Equals('.') -or $segment.Equals('..')) { return $false }
    }
    return $true
}

function Read-Manifest {
    $read = Read-FileText $ManifestPath
    if ($read.State.Equals('unreadable')) { Stop-Run 'checker-file-unreadable' $ManifestPath 'the manifest could not be read' }
    if ($read.State.Equals('undecodable')) { Stop-Run 'checker-manifest-malformed' $ManifestPath 'the manifest is not valid UTF-8' }
    $parsed = [pscustomobject]@{
        MinFiles = 0; MinFilesLine = 0
        Includes = [Collections.Generic.List[object]]::new()
        Excludes = [Collections.Generic.List[object]]::new()
        Prunes = [Collections.Generic.List[regex]]::new()
    }
    $lines = $read.Text.Split("`n")
    for ($index = 0; $index -lt $lines.Length; $index++) {
        $line = $lines[$index].Trim($TrailingBlank)
        $number = $index + 1
        if ($line.Length -eq 0 -or $line.StartsWith('#', [StringComparison]::Ordinal)) { continue }
        $floor = $FloorPattern.Match($line)
        $directive = $DirectivePattern.Match($line)
        if ($floor.Success) {
            if ($parsed.MinFilesLine -ne 0) { Add-Finding 'checker-manifest-malformed' $ManifestPath $number 'fatal' 'min-files is declared more than once'; continue }
            $parsed.MinFiles = [int]$floor.Groups['count'].Value
            $parsed.MinFilesLine = $number
        }
        elseif ($directive.Success -and (Test-GlobSyntax $directive.Groups['glob'].Value)) {
            $glob = $directive.Groups['glob'].Value
            $rule = [pscustomobject]@{ Line = $number; Regex = (ConvertTo-GlobRegex $glob) }
            if ($directive.Groups['kind'].Value.Equals('+')) { $parsed.Includes.Add($rule) }
            else {
                $parsed.Excludes.Add($rule)
                # A directory that '<prefix>/**' excludes is never entered: nothing under it can be
                # selected, and a reparse point inside it is never met.
                if ($glob.EndsWith('/**', [StringComparison]::Ordinal)) { $parsed.Prunes.Add((ConvertTo-GlobRegex $glob.Substring(0, $glob.Length - 3))) }
            }
        }
        elseif ($directive.Success) {
            Add-Finding 'checker-manifest-malformed' $ManifestPath $number 'fatal' 'a glob may not start with a slash or a drive letter, contain a backslash, or have a . or .. segment'
        }
        else {
            Add-Finding 'checker-manifest-malformed' $ManifestPath $number 'fatal' 'a line must be blank, a # comment, min-files <n>, + <glob> or - <glob>'
        }
    }
    if ($parsed.MinFilesLine -eq 0) { Add-Finding 'checker-manifest-malformed' $ManifestPath 0 'fatal' 'min-files is not declared' }
    if ($script:FatalCount -gt 0) { Complete-Run }
    $script:Manifest = $parsed
}

# One directory at a time, so every entry is inspected before anything behind it is opened. A
# reparse point is refused at its own segment: its target is never resolved, entered or named.
function Get-WalkedFile {
    $options = [IO.EnumerationOptions]::new()
    $options.AttributesToSkip = [IO.FileAttributes]0
    $options.IgnoreInaccessible = $false
    $script:Walked = [Collections.Generic.List[string]]::new()
    $pending = [Collections.Generic.Queue[string]]::new()
    $pending.Enqueue('')
    while ($pending.Count -gt 0) {
        $directory = $pending.Dequeue()
        $entries = [Collections.Generic.SortedDictionary[string, IO.FileSystemInfo]]::new([StringComparer]::Ordinal)
        try {
            foreach ($entry in [IO.DirectoryInfo]::new([IO.Path]::Combine($script:Root, $directory)).GetFileSystemInfos('*', $options)) {
                $entries.Add($entry.Name, $entry)
            }
        }
        catch [IO.IOException], [UnauthorizedAccessException] {
            Stop-Run 'checker-file-unreadable' ($directory.Length -eq 0 ? '.' : $directory) 'the directory could not be listed, so the selection cannot be resolved'
        }
        foreach ($entry in $entries.Values) {
            $relative = $directory.Length -eq 0 ? $entry.Name : $directory + '/' + $entry.Name
            $isDirectory = $entry -is [IO.DirectoryInfo]
            if ($isDirectory -and $entry.Name.Equals('.git')) { continue }
            $pruned = $false
            if ($isDirectory) { foreach ($prune in $script:Manifest.Prunes) { if ($prune.IsMatch($relative)) { $pruned = $true } } }
            if ($pruned) { continue }
            if ($entry.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
                Stop-Run 'checker-path-escape' $relative 'a reparse point is refused at this segment and is not followed'
            }
            if ($isDirectory) { $pending.Enqueue($relative) } else { $script:Walked.Add($relative) }
        }
    }
    $script:Walked.Sort([StringComparer]::Ordinal)
    $script:WalkedSet = [Collections.Generic.HashSet[string]]::new($script:Walked, [StringComparer]::Ordinal)
}

function Resolve-ManifestSet {
    $includes = $script:Manifest.Includes
    $excludes = $script:Manifest.Excludes
    $hits = [int[]]::new($includes.Count)
    $script:ManifestSet = [Collections.Generic.List[string]]::new()
    foreach ($relative in $script:Walked) {
        $included = $false
        for ($index = 0; $index -lt $includes.Count; $index++) {
            if ($includes[$index].Regex.IsMatch($relative)) { $hits[$index]++; $included = $true }
        }
        $excluded = $false
        foreach ($rule in $excludes) { if ($rule.Regex.IsMatch($relative)) { $excluded = $true } }
        if ($included -and -not $excluded) { $script:ManifestSet.Add($relative) }
    }
    $script:ManifestLookup = [Collections.Generic.HashSet[string]]::new($script:ManifestSet, [StringComparer]::Ordinal)
    for ($index = 0; $index -lt $includes.Count; $index++) {
        if ($hits[$index] -eq 0) { Add-Finding 'checker-manifest-malformed' $ManifestPath $includes[$index].Line 'fatal' 'the include matches no file' }
    }
    foreach ($required in $RequiredFiles) {
        $excluded = $false
        foreach ($rule in $excludes) {
            if ($rule.Regex.IsMatch($required)) { Add-Finding 'checker-manifest-malformed' $ManifestPath $rule.Line 'fatal' "the exclude removes the required file $required"; $excluded = $true }
        }
        if (-not $excluded -and $script:WalkedSet.Contains($required) -and -not $script:ManifestLookup.Contains($required)) {
            Add-Finding 'checker-manifest-malformed' $ManifestPath 0 'fatal' "no include selects the required file $required"
        }
    }
    if ($script:ManifestSet.Count -lt $script:Manifest.MinFiles) {
        Add-Finding 'checker-scan-empty' $ManifestPath $script:Manifest.MinFilesLine 'fatal' "the manifest selects $($script:ManifestSet.Count) files, fewer than min-files $($script:Manifest.MinFiles)"
    }
    foreach ($relative in $script:ManifestSet) {
        if ($relative.IndexOfAny([char[]]"`t`r`n") -ge 0) {
            Add-Finding 'checker-path-escape' $relative 0 'fatal' 'the selected path contains a tab, carriage return or line feed, shown here as a space'
        }
    }
    if ($script:FatalCount -gt 0) { Complete-Run }
}

# Each operand is judged on its own and a bad one is named even beside a good one. Identity is the
# exact walked spelling, so an operand that differs only in case selects nothing.
function Resolve-Selection {
    if ($Path.Count -eq 0) { $script:Selection = $script:ManifestSet; return }
    $union = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    foreach ($operand in $Path) {
        $shown = [string]$operand
        if ($shown.Length -eq 0) { Add-Finding 'checker-scan-empty' '-' 0 'fatal' 'a -Path operand is empty'; continue }
        $relative = $null
        $reason = $null
        if ($shown.StartsWith('\\', [StringComparison]::Ordinal) -or $shown.StartsWith('//', [StringComparison]::Ordinal)) { $reason = 'is a UNC path' }
        elseif ($DrivePrefix.IsMatch($shown)) { $reason = 'is drive-qualified' }
        elseif ([IO.Path]::IsPathRooted($shown)) { $reason = 'is rooted' }
        else {
            $full = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath([IO.Path]::Combine($script:Root, $shown)))
            if ($full.Equals($script:Root)) { $relative = '.' }
            elseif ($full.StartsWith($script:RootPrefix, [StringComparison]::Ordinal)) {
                $relative = $full.Substring($script:RootPrefix.Length).Replace([IO.Path]::DirectorySeparatorChar, [char]'/')
            }
            else { $reason = 'leaves the repository' }
        }
        if ($null -ne $reason) { Add-Finding 'checker-path-escape' '-' 0 'fatal' "the -Path operand '$shown' $reason"; continue }
        $contributed = 0
        foreach ($candidate in $script:ManifestSet) {
            if ($relative.Equals('.') -or $candidate.Equals($relative) -or $candidate.StartsWith($relative + '/', [StringComparison]::Ordinal)) {
                [void]$union.Add($candidate)
                $contributed++
            }
        }
        if ($contributed -eq 0) { Add-Finding 'checker-scan-empty' $relative 0 'fatal' "the -Path operand '$shown' selects no file of the manifest set" }
    }
    if ($script:FatalCount -gt 0) { Complete-Run }
    $script:Selection = $union
}

# Lines count LF only; a CR is ignored, as SourceHygieneTests counts. The LF positions are found
# once per file, and only when that file has a finding.
function Get-LineNumber {
    param([string] $Content, [int] $Offset)
    if ($null -eq $script:NewlineIndex) {
        $positions = [Collections.Generic.List[int]]::new()
        foreach ($newline in $NewlinePattern.Matches($Content)) { $positions.Add($newline.Index) }
        $script:NewlineIndex = $positions.ToArray()
    }
    return (-bnot [Array]::BinarySearch($script:NewlineIndex, $Offset)) + 1
}

function Test-SelectedFile {
    param([string] $File)
    foreach ($match in $FileNamePattern.Matches($File)) {
        Add-Finding 'path-obsolete' $File 0 'error' "the path contains the obsolete engineering-principles file name '$($match.Value)'"
    }
    $isOwnedElsewhere = ($File.StartsWith('src/', [StringComparison]::Ordinal) -or $File.StartsWith('tests/', [StringComparison]::Ordinal)) -and
        $File.EndsWith('.cs', [StringComparison]::Ordinal)
    $read = Read-FileText $File
    if ($read.State.Equals('unreadable')) {
        Add-Finding 'checker-file-unreadable' $File 0 'fatal' $UnreadableMessage
        $script:Refused++
        return
    }
    if ($read.State.Equals('undecodable') -and $isOwnedElsewhere) {
        Add-Finding 'checker-file-undecodable' $File 0 'fatal' "the file is not valid UTF-8; $SourceHygieneOwner owns the bytes of .cs under src/ and tests/, and the spelling checks of this command did not run on it"
        $script:Refused++
        return
    }
    if ($read.State.Equals('undecodable')) {
        Add-Finding 'encoding-invalid-utf8' $File 1 'error' $InvalidUtf8Message
        return
    }
    $text = $read.Text
    $script:NewlineIndex = $null
    if ($isOwnedElsewhere) { $script:OwnedElsewhere++ }
    else {
        $script:Decoded++
        foreach ($match in $ControlPattern.Matches($text)) {
            $code = ([int]$match.Value[0]).ToString('X4', [cultureinfo]::InvariantCulture)
            Add-Finding 'encoding-control-byte' $File (Get-LineNumber $text $match.Index) 'error' "prohibited control character U+$code"
        }
    }
    foreach ($match in $IdentifierPattern.Matches($text)) {
        Add-Finding 'identifier-obsolete' $File (Get-LineNumber $text $match.Index) 'error' "obsolete engineering-principle identifier '$($match.Value)'; the family is EP-<n>"
    }
    foreach ($match in $FileNamePattern.Matches($text)) {
        Add-Finding 'path-obsolete' $File (Get-LineNumber $text $match.Index) 'error' "obsolete engineering-principles file name '$($match.Value)'; the owner is docs/engineering-principles.md"
    }
    foreach ($match in $StageLabelPattern.Matches($text)) {
        Add-Finding 'stage-label-obsolete' $File (Get-LineNumber $text $match.Index) 'error' "obsolete stage label '$($match.Value)'; write phase 0 through phase 3"
    }
}

# The walk inspected every entry on the way to each of these files, so a file absent from it is
# missing or is not a regular file. One behind a refused or excluded segment never gets here.
function Test-RequiredFile {
    foreach ($required in $RequiredContent) {
        if (-not $script:WalkedSet.Contains($required)) {
            Add-Finding 'required-file-missing' $required 0 'error' 'the required file does not exist as a regular file'
        }
    }
}

# The instruction-entry checks run on every invocation and only on decoded text. A file the run
# selected has had its read outcome reported with the selected files; one outside a targeted
# selection that cannot be read or decoded is reported here, once, in the same words, without
# entering the selection counts. Returns the decoded text, or $null when there is nothing to check.
function Get-EntryText {
    param([string] $File)
    if (-not $script:WalkedSet.Contains($File)) { return $null }
    $read = Read-FileText $File
    if ($read.State.Equals('decoded')) { return $read.Text }
    if (-not $script:Selection.Contains($File)) {
        if ($read.State.Equals('unreadable')) { Add-Finding 'checker-file-unreadable' $File 0 'fatal' $UnreadableMessage }
        else { Add-Finding 'encoding-invalid-utf8' $File 1 'error' $InvalidUtf8Message }
    }
    return $null
}

# Only the sentence is fixed, never the paragraph around it: collapsing whitespace lets it be
# rewrapped, and any change to its words fails.
function Test-PolicyPointer {
    $text = Get-EntryText 'AGENTS.md'
    if ($null -eq $text -or $WhitespaceRun.Replace($text, ' ').Contains($PolicySentence)) { return }
    Add-Finding 'policy-pointer-missing' 'AGENTS.md' 0 'error' "the file does not contain the sentence: $PolicySentence"
}

# D-048: the import is the first nonblank line; Claude-only guidance may follow it.
function Test-InstructionImport {
    $text = Get-EntryText 'CLAUDE.md'
    if ($null -eq $text) { return }
    $lines = $text.Split("`n")
    for ($index = 0; $index -lt $lines.Length; $index++) {
        $line = $lines[$index].TrimEnd($TrailingBlank)
        if ($line.Length -eq 0) { continue }
        if (-not $line.Equals($ImportLine)) {
            Add-Finding 'instruction-import-invalid' 'CLAUDE.md' ($index + 1) 'error' "the first nonblank line must be exactly $ImportLine"
        }
        return
    }
    Add-Finding 'instruction-import-invalid' 'CLAUDE.md' 0 'error' "the file has no nonblank line; its first must be exactly $ImportLine"
}

# An unexpected failure keeps the record grammar and the findings already collected. 'exit' is
# flow control, not an error, so the deliberate refusals above never reach this trap.
trap {
    if ($script:Completing) { exit 2 }
    Add-Finding 'checker-internal-failure' '-' 0 'fatal' "unexpected failure ($($_.Exception.GetBaseException().GetType().FullName)); the assessment is incomplete"
    Complete-Run
}

if ([string]::IsNullOrEmpty($RepoRoot)) { $RepoRoot = [IO.Path]::GetDirectoryName($PSScriptRoot) }
$script:Root = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($RepoRoot, (Get-Location -PSProvider FileSystem).ProviderPath))
$script:RootPrefix = [IO.Path]::EndsInDirectorySeparator($script:Root) ? $script:Root : $script:Root + [IO.Path]::DirectorySeparatorChar

Assert-ManifestAccess
Read-Manifest
Get-WalkedFile
Resolve-ManifestSet
Resolve-Selection
$script:Selected = $script:Selection.Count
foreach ($selectedFile in $script:Selection) { Test-SelectedFile $selectedFile }
Test-RequiredFile
Test-PolicyPointer
Test-InstructionImport
Complete-Run
