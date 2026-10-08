#Requires -Version 7.0

<#
.SYNOPSIS
    Behaviour tests for eng/check-authored-text.ps1 (D-130).

.DESCRIPTION
    Each case builds a small repository from inline content under the session temporary directory,
    runs the command in a child PowerShell process, and compares the exit code, the complete
    standard output (CR LF read as LF) and the standard error with hand-written expectations. No
    case loads the command or calls anything inside it. The session temporary directory must not
    lie beneath a reparse point, because the command refuses a root whose path passes through one.
    On Windows its path must also stay under about 140 characters: the runner-meta- cases nest three
    runner sessions, and Windows cannot start a process in a directory whose path reaches MAX_PATH.

    Every case hashes its whole case directory, every path and every byte, immediately before and
    after the run, and fails on any difference or any created path. A case that needs a platform
    object this host cannot create is reported as skipped with its reason and is never counted as
    passed. Before any case runs, a comparator self-check must see deliberately wrong expectations
    rejected; otherwise the runner exits 2. The runner-child- and runner-meta- cases, described
    below, check a temporary directory of their own instead.

    A capability probe and one case deny the current user the right to list a directory for a
    moment, and record its permissions before changing them. A skip for that is reported only when
    nothing was changed, and a denial that was undone exactly but did not work fails the cases that
    need it. Once a change may have begun, a failure to deny or to restore, or a restore that does
    not compare equal to the record, is a runner infrastructure failure: no further case runs, and
    the session directory is kept instead of removed, with its path, the directory and the recorded
    permissions printed for recovery.

    The runner-child- cases run this runner as a child process whose temporary directory is their
    own, and check what that child removed or kept there. Some set CHECK_AUTHORED_TEXT_TESTS_FAULT
    to make one permission step fail. That variable is a test seam only: unset, nothing changes; a
    value that is not one of its modes is refused; a mode is announced on the first line; and every
    child process starts without it unless such a case sets it.

    The temporary directory of every child runner is registered as kept before the child starts.
    The registration is dropped only after every check has passed, the kept session was removed and
    the directory is empty again, so a failure or an exception at any step keeps this run's session
    too. If the final removal of the session directory fails, the run fails, and what remains is
    reported and kept. The runner-meta- cases prove both through a second seam of the same kind,
    CHECK_AUTHORED_TEXT_TESTS_META_FAULT, which makes the verification of a kept child session
    throw, or the final removal throw before it deletes anything.

    Exit 0: at least one case ran and none failed. Exit 1: a case failed, no case ran, or the
    session directory could not be removed. Exit 2: the comparator self-check failed.

.PARAMETER Filter
    Runs only the cases whose name contains this text.

.EXAMPLE
    pwsh -NoProfile -File eng/check-authored-text.tests.ps1
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [string] $Filter = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Runtime = [IO.Path]::Combine($PSScriptRoot, 'check-authored-text.ps1')
$RepositoryRoot = [IO.Path]::GetDirectoryName($PSScriptRoot)
$PowerShell = [Environment]::ProcessPath
$SessionRoot = [IO.Path]::Combine([IO.Path]::GetTempPath(), 'check-authored-text-' + [guid]::NewGuid().ToString('N').Substring(0, 12))

# The test seam of the runner-child- cases. A mode names the site (the capability probe or the
# case) and the permission step that fails there; it must match one of these exactly.
$FaultVariable = 'CHECK_AUTHORED_TEXT_TESTS_FAULT'
$FaultModes = [Collections.Generic.HashSet[string]]::new([string[]]@(
        'probe-deny-fails-before-mutation', 'probe-deny-fails-after-mutation-began', 'probe-restore-reports-false',
        'case-deny-fails-after-mutation-began', 'case-restore-reports-false'), [StringComparer]::Ordinal)
$FaultMode = [Environment]::GetEnvironmentVariable($FaultVariable) ?? ''

# The test seam of the runner-meta- cases, which check this runner's own cleanup: the verification
# of a kept child session throws, or the final removal of the session directory throws.
$MetaFaultVariable = 'CHECK_AUTHORED_TEXT_TESTS_META_FAULT'
$MetaFaultModes = [Collections.Generic.HashSet[string]]::new([string[]]@('child-verification-throws', 'final-cleanup-throws'), [StringComparer]::Ordinal)
$MetaFaultMode = [Environment]::GetEnvironmentVariable($MetaFaultVariable) ?? ''

# The command reports these spellings, so the cases need them; they are assembled from fragments so
# that this file passes the command it tests.
$Id7 = 'P' + '-7'
$Id8 = 'P' + '-8'
$Id12 = 'P' + '-12'
$IdArabicDigit = 'P' + '-' + [char]0x0663
$Stage0 = 'P' + '0'
$Stage2 = 'P' + '2'
$Stage3 = 'P' + '3'
$OldName = 'principles' + '.md'

$PolicySentence = 'Read `docs/writing-principles.md` before authoring or editing any prose or comment in this repository.'
$HygieneOwner = 'tests/FcaBedrock.Architecture.Tests/SourceHygieneTests.cs'
$ReparseMessage = 'a reparse point is refused at this segment and is not followed'
$Scope = "checker-scope`t-`t0`tinfo`tthis command does not assess local links or fragments or writing style, " +
    "and does not own byte hygiene for .cs under src/ and tests/, which $HygieneOwner owns"

$BaseFiles = [ordered]@{
    'AGENTS.md' = "# Agents`n`n$PolicySentence`n"
    'CLAUDE.md' = "@AGENTS.md`n"
    'docs/writing-principles.md' = "# Writing`n"
}
$BaseManifest = "min-files 1`n+ **/*.md`n+ eng/authored-files.txt`n"

function Get-Summary {
    param([int] $Selected, [int] $Decoded, [int] $Owned, [int] $Refused, [int] $Errors)
    "checker-summary`t-`t0`tinfo`tselected=$Selected decoded=$Decoded owned-elsewhere=$Owned refused=$Refused errors=$Errors"
}

function Get-Record {
    param([string] $RuleId, [string] $Location, [int] $LineNumber, [string] $Severity, [string] $Message)
    "$RuleId`t$Location`t$LineNumber`t$Severity`t$Message"
}

function Get-IdRecord {
    param([string] $Location, [int] $LineNumber, [string] $Value)
    Get-Record 'identifier-obsolete' $Location $LineNumber 'error' "obsolete engineering-principle identifier '$Value'; the family is EP-<n>"
}

function Get-FileNameRecord {
    param([string] $Location, [int] $LineNumber)
    Get-Record 'path-obsolete' $Location $LineNumber 'error' "obsolete engineering-principles file name '$OldName'; the owner is docs/engineering-principles.md"
}

function Get-StageRecord {
    param([string] $Location, [int] $LineNumber, [string] $Value)
    Get-Record 'stage-label-obsolete' $Location $LineNumber 'error' "obsolete stage label '$Value'; write phase 0 through phase 3"
}

function Get-ControlRecord {
    param([string] $Location, [int] $LineNumber, [int] $Code)
    Get-Record 'encoding-control-byte' $Location $LineNumber 'error' ('prohibited control character U+' + $Code.ToString('X4'))
}

function Get-MalformedRecord {
    param([int] $LineNumber, [string] $Message)
    Get-Record 'checker-manifest-malformed' 'eng/authored-files.txt' $LineNumber 'fatal' $Message
}

function Get-UndecodableRecord {
    param([string] $Location)
    Get-Record 'checker-file-undecodable' $Location 0 'fatal' "the file is not valid UTF-8; $HygieneOwner owns the bytes of .cs under src/ and tests/, and the spelling checks of this command did not run on it"
}

$LineMessage = 'a line must be blank, a # comment, min-files <n>, + <glob> or - <glob>'
$GlobMessage = 'a glob may not start with a slash or a drive letter, contain a backslash, or have a . or .. segment'
$ImportMessage = 'the first nonblank line must be exactly @AGENTS.md'
$RequiredMessage = 'the required file does not exist as a regular file'
$InvalidUtf8Message = 'the file is not valid UTF-8, so no other check ran on it'
$UnreadableMessage = 'the file could not be read, so no check ran on it'
$PointerRecord = Get-Record 'policy-pointer-missing' 'AGENTS.md' 0 'error' "the file does not contain the sentence: $PolicySentence"

function ConvertTo-Bytes {
    param([string] $Text)
    return , [Text.UTF8Encoding]::new($false).GetBytes($Text)
}

function Write-RunnerLine {
    param([string] $Text)
    [Console]::Out.WriteLine($Text)
}

# ---------------------------------------------------------------------------
# Fixtures, processes and hashing
# ---------------------------------------------------------------------------

function Write-FixtureFile {
    param([string] $Root, [string] $FilePath, $Content)
    $full = [IO.Path]::Combine($Root, $FilePath)
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($full))
    if ($Content -is [byte[]]) { [IO.File]::WriteAllBytes($full, $Content) }
    else { [IO.File]::WriteAllText($full, [string]$Content, [Text.UTF8Encoding]::new($false)) }
}

function New-DirectoryLink {
    param([string] $Link, [string] $Target)
    if ($IsWindows) { $null = New-Item -ItemType Junction -Path $Link -Target $Target }
    else { $null = [IO.Directory]::CreateSymbolicLink($Link, $Target) }
}

# Every path under the directory with its kind, and every file's SHA-256. A reparse point is
# recorded as itself and never followed, so an outside target is hashed only where it really is.
function Get-TreeState {
    param([string] $Directory, [string] $Skip = '')
    $options = [IO.EnumerationOptions]::new()
    $options.AttributesToSkip = [IO.FileAttributes]0
    $options.IgnoreInaccessible = $false
    $records = [Collections.Generic.List[string]]::new()
    $pending = [Collections.Generic.Queue[string]]::new()
    $pending.Enqueue('')
    while ($pending.Count -gt 0) {
        $current = $pending.Dequeue()
        $entries = [Collections.Generic.SortedDictionary[string, IO.FileSystemInfo]]::new([StringComparer]::Ordinal)
        foreach ($entry in [IO.DirectoryInfo]::new([IO.Path]::Combine($Directory, $current)).GetFileSystemInfos('*', $options)) {
            $entries.Add($entry.Name, $entry)
        }
        foreach ($entry in $entries.Values) {
            $relative = $current.Length -eq 0 ? $entry.Name : $current + '/' + $entry.Name
            if ($current.Length -eq 0 -and $entry.Name.Equals($Skip)) { continue }
            if ($entry.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { $records.Add("link $relative") }
            elseif ($entry -is [IO.DirectoryInfo]) { $records.Add("directory $relative"); $pending.Enqueue($relative) }
            else {
                $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($entry.FullName)))
                $records.Add("file $relative $digest")
            }
        }
    }
    return [string]::Join("`n", $records)
}

# Links are removed as links first; a recursive delete must never be the thing that meets one.
function Remove-Tree {
    param([string] $Directory)
    if (-not [IO.Directory]::Exists($Directory)) { return }
    $options = [IO.EnumerationOptions]::new()
    $options.AttributesToSkip = [IO.FileAttributes]0
    $options.RecurseSubdirectories = $false
    $pending = [Collections.Generic.Queue[string]]::new()
    $pending.Enqueue($Directory)
    while ($pending.Count -gt 0) {
        foreach ($entry in [IO.DirectoryInfo]::new($pending.Dequeue()).GetFileSystemInfos('*', $options)) {
            if ($entry.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { $entry.Delete() }
            elseif ($entry -is [IO.DirectoryInfo]) { $pending.Enqueue($entry.FullName) }
        }
    }
    [IO.Directory]::Delete($Directory, $true)
}

# Every directory whose listing a run denies is registered before its permissions change. Its
# State says how far the change got: untouched (nothing changed, though the original permissions
# may already be recorded), mutating (a change may have begun), denied, restored (the original
# permissions are back and compared equal to the record) or uncertain. Only untouched and restored
# are settled.
$ListingDenials = [Collections.Generic.List[object]]::new()

# Paths kept for a person. A case that runs this runner as a child registers the child's temporary
# directory here before the child starts, and removes it only when every check has passed.
$KeptPaths = [Collections.Generic.List[object]]::new()

function New-ListingDenial {
    param([string] $Directory, [string] $Site)
    $record = [pscustomobject]@{ Directory = $Directory; Site = $Site; State = 'untouched'; Original = $null; Rule = $null; Reason = '' }
    $ListingDenials.Add($record)
    return $record
}

function Test-ListingSettled {
    param($ListingDenial)
    return $ListingDenial.State.Equals('untouched') -or $ListingDenial.State.Equals('restored')
}

# The session directory may be removed, recursively, only while this holds: every registered
# directory is settled and no kept path is waiting for a person.
function Test-SessionSettled {
    foreach ($record in $ListingDenials) { if (-not (Test-ListingSettled $record)) { return $false } }
    return $KeptPaths.Count -eq 0
}

# True only in a runner-child- case that set this mode; an ordinary run never sees true.
function Test-Fault {
    param([string] $Step)
    return $FaultMode.Equals($Step)
}

# Denies the current user the right to list the registered directory. The original permissions are
# recorded, and the state becomes mutating, before anything changes, so a failure leaves either an
# untouched directory or an uncertain one whose original permissions are on record. Returns '' when
# the listing is denied, otherwise the failure's type name.
function Set-ListingDenied {
    param($ListingDenial)
    try {
        if ($IsWindows) {
            $info = [IO.DirectoryInfo]::new($ListingDenial.Directory)
            $security = [IO.FileSystemAclExtensions]::GetAccessControl($info)
            $ListingDenial.Original = $security.GetSecurityDescriptorSddlForm([Security.AccessControl.AccessControlSections]::Access)
            $ListingDenial.Rule = [Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.WindowsIdentity]::GetCurrent().User,
                [Security.AccessControl.FileSystemRights]::ListDirectory, [Security.AccessControl.AccessControlType]::Deny)
            $security.AddAccessRule($ListingDenial.Rule)
        }
        else { $ListingDenial.Original = [IO.File]::GetUnixFileMode($ListingDenial.Directory) }
        if (Test-Fault "$($ListingDenial.Site)-deny-fails-before-mutation") { throw 'injected fault' }
        $ListingDenial.State = 'mutating'
        $ListingDenial.Reason = 'a change to its permissions began and did not complete'
        if (Test-Fault "$($ListingDenial.Site)-deny-fails-after-mutation-began") { throw 'injected fault' }
        if ($IsWindows) { [IO.FileSystemAclExtensions]::SetAccessControl($info, $security) }
        else { [IO.File]::SetUnixFileMode($ListingDenial.Directory, $ListingDenial.Original -band -bnot [IO.UnixFileMode]::UserRead) }
        $ListingDenial.State = 'denied'
        $ListingDenial.Reason = 'its listing was still denied when the run ended'
        return ''
    }
    catch {
        $failure = $_.Exception.GetBaseException().GetType().Name
        if ($ListingDenial.State.Equals('mutating')) {
            $ListingDenial.State = 'uncertain'
            $ListingDenial.Reason = "denying its listing failed after the change may have begun ($failure)"
        }
        return $failure
    }
}

# Puts the original permissions back and compares them with the record. The state becomes restored
# only when they compare equal; any other outcome leaves it uncertain, with the reason.
function Restore-Listing {
    param($ListingDenial)
    try {
        if ($IsWindows) {
            $info = [IO.DirectoryInfo]::new($ListingDenial.Directory)
            $security = [IO.FileSystemAclExtensions]::GetAccessControl($info)
            $security.RemoveAccessRuleSpecific($ListingDenial.Rule)
            [IO.FileSystemAclExtensions]::SetAccessControl($info, $security)
            $current = [IO.FileSystemAclExtensions]::GetAccessControl($info).GetSecurityDescriptorSddlForm([Security.AccessControl.AccessControlSections]::Access)
            $exact = $current.Equals($ListingDenial.Original)
        }
        else {
            [IO.File]::SetUnixFileMode($ListingDenial.Directory, $ListingDenial.Original)
            $exact = [IO.File]::GetUnixFileMode($ListingDenial.Directory) -eq $ListingDenial.Original
        }
        if (Test-Fault "$($ListingDenial.Site)-restore-reports-false") { $exact = $false }
        if ($exact) {
            $ListingDenial.State = 'restored'
            $ListingDenial.Reason = ''
            return
        }
        $ListingDenial.Reason = 'they did not compare equal to the recorded original after restoration'
    }
    catch { $ListingDenial.Reason = "restoring them failed ($($_.Exception.GetBaseException().GetType().Name))" }
    $ListingDenial.State = 'uncertain'
}

# The permissions a denial records, read directly: the access SDDL on Windows, the mode elsewhere.
function Get-PermissionText {
    param([string] $Directory)
    if ($MetaFaultMode.Equals('child-verification-throws')) { throw 'injected fault' }
    if ($IsWindows) {
        return [IO.FileSystemAclExtensions]::GetAccessControl([IO.DirectoryInfo]::new($Directory)).GetSecurityDescriptorSddlForm([Security.AccessControl.AccessControlSections]::Access)
    }
    return [IO.File]::GetUnixFileMode($Directory).ToString()
}

# Checked here, not taken from a child's own comparison: the directory can be listed, and it has
# no permission entry of its own (Windows) or keeps its owner's read permission (Unix). Returns ''
# when both hold, otherwise what failed.
function Test-OrdinaryPermission {
    param([string] $Directory)
    try {
        [void][IO.Directory]::GetFileSystemEntries($Directory)
        if ($IsWindows) {
            $own = [IO.FileSystemAclExtensions]::GetAccessControl([IO.DirectoryInfo]::new($Directory)).GetAccessRules($true, $false, [Security.Principal.SecurityIdentifier])
            if ($own.Count -gt 0) { return "$Directory has $($own.Count) permission entries of its own" }
        }
        elseif (([IO.File]::GetUnixFileMode($Directory) -band [IO.UnixFileMode]::UserRead) -eq 0) { return "$Directory lacks its owner's read permission" }
        return ''
    }
    catch { return "$Directory could not be inspected ($($_.Exception.GetBaseException().GetType().Name))" }
}

# A kept session is removed recursively only when it is an ordinary directory and every directory
# under it, walked without following a link, passes Test-OrdinaryPermission. Returns '' when all do,
# otherwise the first that did not.
function Test-OrdinaryTree {
    param([string] $Directory)
    $attributes = [IO.FileInfo]::new($Directory).Attributes
    if (-not $attributes.HasFlag([IO.FileAttributes]::Directory) -or $attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { return "$Directory is not an ordinary directory" }
    $options = [IO.EnumerationOptions]::new()
    $options.AttributesToSkip = [IO.FileAttributes]0
    $options.IgnoreInaccessible = $false
    $pending = [Collections.Generic.Queue[string]]::new()
    $pending.Enqueue($Directory)
    while ($pending.Count -gt 0) {
        $current = $pending.Dequeue()
        $problem = Test-OrdinaryPermission $current
        if ($problem.Length -gt 0) { return $problem }
        foreach ($entry in [IO.DirectoryInfo]::new($current).GetDirectories('*', $options)) {
            if (-not $entry.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { $pending.Enqueue($entry.FullName) }
        }
    }
    return ''
}

# A fixture path inside a kept session, reached without passing a link. The segment * stands for
# the only entry of the directory before it: the session a grandchild runner kept there. Returns ''
# when a segment is missing or a link, or when such a directory does not hold exactly one entry.
function Resolve-KeptPath {
    param([string] $KeptSession, [string] $FixtureName)
    $current = $KeptSession
    foreach ($segment in $FixtureName.Split('/')) {
        if ($segment.Equals('*')) {
            $entries = [IO.Directory]::GetFileSystemEntries($current)
            if ($entries.Length -ne 1) { return '' }
            $current = $entries[0]
        }
        else { $current = [IO.Path]::Combine($current, $segment) }
        $attributes = [IO.FileInfo]::new($current).Attributes
        if ([int]$attributes -eq -1 -or $attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { return '' }
    }
    return $current
}

function Invoke-Child {
    param([string[]] $Arguments, [hashtable] $Variables = @{})
    $start = [Diagnostics.ProcessStartInfo]::new($PowerShell)
    foreach ($argument in @('-NoProfile', '-NonInteractive') + $Arguments) { $start.ArgumentList.Add($argument) }
    # No child inherits a test seam; a runner-child- or runner-meta- case hands its mode to its
    # child explicitly.
    [void]$start.Environment.Remove($FaultVariable)
    [void]$start.Environment.Remove($MetaFaultVariable)
    foreach ($pair in $Variables.GetEnumerator()) { $start.Environment[$pair.Key] = [string]$pair.Value }
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.UseShellExecute = $false
    $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    $start.WorkingDirectory = $SessionRoot
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    return [pscustomobject]@{ ExitCode = $process.ExitCode; Stdout = $stdout.Result; Stderr = $stderr.Result }
}

function ConvertTo-Quoted {
    param([string] $Text)
    return "'" + $Text.Replace("'", "''") + "'"
}

# pwsh -File passes one value per parameter, so several -Path operands, or a changed culture, go
# through -Command. Either way the command runs in its own child process.
function Invoke-Runtime {
    param([string] $ScriptFile, [string] $RootPath, [string[]] $Operands, [string] $Culture)
    if ($Operands.Count -le 1 -and $Culture.Length -eq 0) {
        $childArguments = @('-File', $ScriptFile, '-RepoRoot', $RootPath)
        if ($Operands.Count -eq 1) { $childArguments += @('-Path', $Operands[0]) }
        return Invoke-Child $childArguments
    }
    $command = ''
    if ($Culture.Length -gt 0) {
        $command = "[cultureinfo]::CurrentCulture = '$Culture'; [cultureinfo]::CurrentUICulture = '$Culture'; "
    }
    $command += '& ' + (ConvertTo-Quoted $ScriptFile) + ' -RepoRoot ' + (ConvertTo-Quoted $RootPath)
    if ($Operands.Count -gt 0) { $command += ' -Path ' + [string]::Join(',', @($Operands | ForEach-Object { ConvertTo-Quoted $_ })) }
    $command += '; exit $LASTEXITCODE'
    return Invoke-Child @('-Command', $command)
}

# Returns '' when the outcome matches, otherwise the first difference found.
function Compare-Outcome {
    param([int] $ExpectedExit, [string[]] $ExpectedLines, [string] $ExpectedError, $Outcome)
    $expected = $ExpectedLines.Count -eq 0 ? '' : [string]::Join("`n", $ExpectedLines) + "`n"
    $actualText = $Outcome.Stdout.Replace("`r`n", "`n")
    if ($Outcome.ExitCode -ne $ExpectedExit) { return "exit code $($Outcome.ExitCode), expected $ExpectedExit" }
    if (-not $actualText.Equals($expected)) {
        $wanted = $expected.Split("`n")
        $got = $actualText.Split("`n")
        for ($index = 0; $index -lt [Math]::Max($wanted.Length, $got.Length); $index++) {
            $left = $index -lt $wanted.Length ? $wanted[$index] : '<none>'
            $right = $index -lt $got.Length ? $got[$index] : '<none>'
            if (-not $left.Equals($right)) { return "stdout line $($index + 1): expected [$left], actual [$right]" }
        }
    }
    if (-not $Outcome.Stderr.Replace("`r`n", "`n").Equals($ExpectedError)) { return "stderr [$($Outcome.Stderr.Trim())], expected [$ExpectedError]" }
    return ''
}

# ---------------------------------------------------------------------------
# Platform capabilities
# ---------------------------------------------------------------------------

function Get-Capability {
    $probe = [IO.Path]::Combine($SessionRoot, 'probe')
    [void][IO.Directory]::CreateDirectory([IO.Path]::Combine($probe, 'target'))
    [IO.File]::WriteAllText([IO.Path]::Combine($probe, 'target', 'file.txt'), 'x')
    $found = @{}
    try {
        New-DirectoryLink ([IO.Path]::Combine($probe, 'dir-link')) ([IO.Path]::Combine($probe, 'target'))
        $found['directory-link'] = ''
    }
    catch { $found['directory-link'] = "this host could not create a directory link ($($_.Exception.GetBaseException().GetType().Name))" }
    try {
        $null = [IO.File]::CreateSymbolicLink([IO.Path]::Combine($probe, 'file-link.txt'), [IO.Path]::Combine($probe, 'target', 'file.txt'))
        $found['file-link'] = ''
    }
    catch { $found['file-link'] = "this host refuses to create a file symbolic link ($($_.Exception.GetBaseException().GetType().Name))" }
    $held = [IO.File]::Open([IO.Path]::Combine($probe, 'target', 'file.txt'), 'Open', 'Read', 'None')
    try { [void][IO.File]::ReadAllBytes([IO.Path]::Combine($probe, 'target', 'file.txt')); $found['exclusive-open'] = 'an exclusive open does not stop another reader on this platform' }
    catch [IO.IOException] { $found['exclusive-open'] = '' }
    finally { $held.Dispose() }
    try { [IO.File]::WriteAllText([IO.Path]::Combine($probe, "tab`tname.md"), 'x'); $found['tab-file-name'] = '' }
    catch { $found['tab-file-name'] = 'the file system does not accept a tab in a file name' }
    # A listing-denial skip is reported only when nothing was changed. After a change that was undone
    # exactly, a denial that did not work fails each case that needs it. After a change that was not
    # undone exactly, the run stops before any case, so the value left here is never used.
    $unlisted = [IO.Path]::Combine($probe, 'unlisted')
    [void][IO.Directory]::CreateDirectory($unlisted)
    $denial = New-ListingDenial $unlisted 'probe'
    $listingOutcome = ''
    if (-not $IsWindows -and [Environment]::UserName.Equals('root')) { $listingOutcome = 'the root user is not bound by directory permissions' }
    else {
        try {
            $failure = Set-ListingDenied $denial
            if ($failure.Length -gt 0) { $listingOutcome = "this host could not deny a directory listing ($failure)" }
            else {
                try { [void][IO.Directory]::GetFileSystemEntries($unlisted); $listingOutcome = 'a directory whose listing is denied can still be listed here' }
                catch [UnauthorizedAccessException] { $listingOutcome = '' }
                catch { $listingOutcome = "listing a denied directory failed with $($_.Exception.GetBaseException().GetType().Name)" }
            }
        }
        finally { if ($denial.State.Equals('denied')) { Restore-Listing $denial } }
    }
    if (-not (Test-ListingSettled $denial)) { $found['listing-denial'] = 'failed: the listing-denial probe left permissions uncertain' }
    elseif ($denial.State.Equals('restored') -and $listingOutcome.Length -gt 0) { $found['listing-denial'] = "failed: the listing-denial probe changed permissions and restored them exactly, but $listingOutcome" }
    else { $found['listing-denial'] = $listingOutcome }
    try {
        $turkish = [cultureinfo]::GetCultureInfo('tr-TR')
        $found['turkish-culture'] = 'i'.ToUpper($turkish).Equals([string][char]0x0130) ? '' : 'the tr-TR culture has no Turkish casing here'
    }
    catch { $found['turkish-culture'] = 'the tr-TR culture is not available here' }
    return $found
}

# ---------------------------------------------------------------------------
# Cases
# ---------------------------------------------------------------------------

$Cases = [Collections.Generic.List[hashtable]]::new()
function Add-Case {
    param([hashtable] $Case)
    $Cases.Add($Case)
}

# Bytes

Add-Case @{
    Name = 'bytes-utf8-with-and-without-byte-order-mark'
    Files = [ordered]@{
        'plain.md' = 'caf' + [char]0x00E9 + "`n"
        'marked.md' = [byte[]](@(0xEF, 0xBB, 0xBF) + (ConvertTo-Bytes "first`n$Id7`n"))
        'CLAUDE.md' = [byte[]](@(0xEF, 0xBB, 0xBF) + (ConvertTo-Bytes "@AGENTS.md`n"))
    }
    Manifest = [byte[]](@(0xEF, 0xBB, 0xBF) + (ConvertTo-Bytes $BaseManifest))
    Exit = 1
    Expected = @((Get-IdRecord 'marked.md' 2 $Id7), $Scope, (Get-Summary 6 6 0 0 1))
}

Add-Case @{
    Name = 'bytes-invalid-utf8-outside-cs-is-an-error-and-stops-that-file'
    Files = [ordered]@{ 'bad.md' = [byte[]]((ConvertTo-Bytes "ok`n") + @(0xC3, 0x28) + (ConvertTo-Bytes "`n$Id7`n")) }
    Exit = 1
    Expected = @(
        (Get-Record 'encoding-invalid-utf8' 'bad.md' 1 'error' 'the file is not valid UTF-8, so no other check ran on it')
        $Scope
        (Get-Summary 5 4 0 0 1)
    )
}

$ForbiddenControls = @(0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x0B, 0x0C, 0x0E, 0x0F, 0x10, 0x11,
    0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x7F)
$controlRecords = [Collections.Generic.List[string]]::new()
for ($index = 0; $index -lt $ForbiddenControls.Count; $index++) { $controlRecords.Add((Get-ControlRecord 'controls.md' ($index + 1) $ForbiddenControls[$index])) }
Add-Case @{
    Name = 'bytes-every-forbidden-control-character'
    Files = [ordered]@{ 'controls.md' = [string]::Join("`n", @($ForbiddenControls | ForEach-Object { 'a' + [char]$_ + 'b' })) + "`n" }
    Exit = 1
    Expected = @($controlRecords) + @($Scope, (Get-Summary 5 5 0 0 30))
}

Add-Case @{
    Name = 'bytes-tab-cr-lf-and-other-unicode-controls-allowed'
    Files = [ordered]@{ 'allowed.md' = "a`tb`r`nc`rd`n" + [char]0x0085 + [char]0x2028 + [char]0x00A0 + " e`n" }
    Exit = 0
    Expected = @($Scope, (Get-Summary 5 5 0 0 0))
}

Add-Case @{
    Name = 'bytes-empty-file'
    Files = [ordered]@{ 'empty.md' = '' }
    Exit = 0
    Expected = @($Scope, (Get-Summary 5 5 0 0 0))
}

Add-Case @{
    Name = 'bytes-one-line-with-and-without-terminal-newline'
    Files = [ordered]@{ 'bare.md' = $Id7; 'ended.md' = "$Id8`n" }
    Exit = 1
    Expected = @((Get-IdRecord 'bare.md' 1 $Id7), (Get-IdRecord 'ended.md' 1 $Id8), $Scope, (Get-Summary 6 6 0 0 2))
}

Add-Case @{
    Name = 'bytes-lines-count-lf-and-ignore-cr'
    Files = [ordered]@{
        'crlf.md' = "a`r`nb`r`n$Id7`r`n"
        'cr-only.md' = "a`rb`r$Id7"
        'lf.md' = "a`nb`n$Id7"
        'separator.md' = 'a' + [char]0x2028 + $Id7
        'control-crlf.md' = "a`r`n" + [char]0x01 + "`r`n"
    }
    Exit = 1
    Expected = @(
        (Get-ControlRecord 'control-crlf.md' 2 0x01)
        (Get-IdRecord 'cr-only.md' 1 $Id7)
        (Get-IdRecord 'crlf.md' 3 $Id7)
        (Get-IdRecord 'lf.md' 3 $Id7)
        (Get-IdRecord 'separator.md' 1 $Id7)
        $Scope
        (Get-Summary 9 9 0 0 5)
    )
}

# Ownership

Add-Case @{
    Name = 'ownership-cs-gets-spelling-checks-but-no-control-check'
    Files = [ordered]@{
        'src/A.cs' = "class A { string s = `"$Id7`"; }`n"
        'tests/B.cs' = '// ' + [char]0x01 + "`n"
    }
    Manifest = $BaseManifest + "+ src/**/*.cs`n+ tests/**/*.cs`n"
    Exit = 1
    Expected = @((Get-IdRecord 'src/A.cs' 1 $Id7), $Scope, (Get-Summary 6 4 2 0 1))
}

Add-Case @{
    Name = 'ownership-undecodable-cs-is-refused-and-the-scan-continues'
    Files = [ordered]@{
        'tests/Bad.cs' = [byte[]](0x63, 0x6C, 0xFF, 0x0A)
        'src/Hit.cs' = "// $Id7`n"
        'notes.md' = "$Id8`n"
    }
    Manifest = $BaseManifest + "+ src/**/*.cs`n+ tests/**/*.cs`n"
    Exit = 2
    Expected = @(
        (Get-UndecodableRecord 'tests/Bad.cs')
        (Get-IdRecord 'notes.md' 1 $Id8)
        (Get-IdRecord 'src/Hit.cs' 1 $Id7)
        $Scope
        (Get-Summary 7 5 1 1 2)
    )
}

Add-Case @{
    Name = 'ownership-other-files-under-src-and-cs-elsewhere-are-owned-here'
    Files = [ordered]@{
        'tools/C.cs' = '// ' + [char]0x02 + "`n"
        'src/notes.txt' = 'x' + [char]0x7F + "`n"
        'src/bad.txt' = [byte[]](0xFF)
    }
    Manifest = $BaseManifest + "+ tools/**/*.cs`n+ src/**/*.txt`n"
    Exit = 1
    Expected = @(
        (Get-Record 'encoding-invalid-utf8' 'src/bad.txt' 1 'error' 'the file is not valid UTF-8, so no other check ran on it')
        (Get-ControlRecord 'src/notes.txt' 1 0x7F)
        (Get-ControlRecord 'tools/C.cs' 1 0x02)
        $Scope
        (Get-Summary 7 6 0 0 3)
    )
}

# Raw spellings

Add-Case @{
    Name = 'raw-identifier-boundaries'
    Files = [ordered]@{
        'ids.md' = [string]::Join("`n", @(
            $Id7, "($Id12).", $IdArabicDigit, "x$Id7", "${Id7}x", ([string][char]0x00E9 + $Id7), 'EP-7 WP-3 IP-10', 'P- p-7', "$Id8 $Id7")) + "`n"
    }
    Exit = 1
    Expected = @(
        (Get-IdRecord 'ids.md' 1 $Id7)
        (Get-IdRecord 'ids.md' 2 $Id12)
        (Get-IdRecord 'ids.md' 3 $IdArabicDigit)
        (Get-IdRecord 'ids.md' 9 $Id7)
        (Get-IdRecord 'ids.md' 9 $Id8)
        $Scope
        (Get-Summary 5 5 0 0 5)
    )
}

Add-Case @{
    Name = 'raw-file-name-boundaries'
    Files = [ordered]@{
        'names.md' = [string]::Join("`n", @(
            $OldName, "docs/$OldName", "($OldName)", "${OldName}x", 'docs/engineering-principles.md docs/writing-principles.md',
            "x$OldName", "_$OldName", 'Principles.md', 'principles_md')) + "`n"
    }
    Exit = 1
    Expected = @(
        (Get-FileNameRecord 'names.md' 1)
        (Get-FileNameRecord 'names.md' 2)
        (Get-FileNameRecord 'names.md' 3)
        (Get-FileNameRecord 'names.md' 4)
        $Scope
        (Get-Summary 5 5 0 0 4)
    )
}

Add-Case @{
    Name = 'raw-stage-label-boundaries'
    Files = [ordered]@{
        'stages.md' = [string]::Join("`n", @(
            $Stage0, "$Stage3.", "($Stage2)", 'P4 P10', "x$Stage2", "${Stage2}x", 'p2 Case_P1 phase 2', ('P' + [char]0x0662))) + "`n"
    }
    Exit = 1
    Expected = @(
        (Get-StageRecord 'stages.md' 1 $Stage0)
        (Get-StageRecord 'stages.md' 2 $Stage3)
        (Get-StageRecord 'stages.md' 3 $Stage2)
        $Scope
        (Get-Summary 5 5 0 0 3)
    )
}

Add-Case @{
    Name = 'raw-canonical-spellings-are-clean'
    Files = [ordered]@{
        'clean.md' = "EP-1, EP-22, WP-3, IP-10, Case_P1, phase 0 through phase 3,`ndocs/engineering-principles.md and docs/writing-principles.md`n"
    }
    Exit = 0
    Expected = @($Scope, (Get-Summary 5 5 0 0 0))
}

$fence = '```'
Add-Case @{
    Name = 'raw-hits-inside-literals-comments-and-attribution-are-reported'
    Files = [ordered]@{
        'fence.md' = "$fence text`n$Id7`n$fence`n"
        'span.md' = "Use ``$Id7`` here.`n"
        'src/Literal.cs' = "class C { const string S = `"$Id7`"; }`n"
        'eng/here.ps1' = "`$x = @'`n$Id7`n'@`n"
        'fixtures/v2/ATTRIBUTION.md' = "Dataset $Id7`n"
    }
    Manifest = "min-files 1`n+ **/*.md`n+ eng/authored-files.txt`n+ src/**/*.cs`n+ eng/**/*.ps1`n# $Stage2 in a comment`n"
    Exit = 1
    Expected = @(
        (Get-StageRecord 'eng/authored-files.txt' 6 $Stage2)
        (Get-IdRecord 'eng/here.ps1' 2 $Id7)
        (Get-IdRecord 'fence.md' 2 $Id7)
        (Get-IdRecord 'fixtures/v2/ATTRIBUTION.md' 1 $Id7)
        (Get-IdRecord 'span.md' 1 $Id7)
        (Get-IdRecord 'src/Literal.cs' 1 $Id7)
        $Scope
        (Get-Summary 9 8 1 0 6)
    )
}

Add-Case @{
    Name = 'raw-obsolete-file-name-is-reported-for-an-empty-selected-file'
    Files = [ordered]@{ "docs/$OldName" = '' }
    Exit = 1
    Expected = @(
        (Get-Record 'path-obsolete' "docs/$OldName" 0 'error' "the path contains the obsolete engineering-principles file name '$OldName'")
        $Scope
        (Get-Summary 5 5 0 0 1)
    )
}

# Instruction entry points

Add-Case @{
    Name = 'entry-policy-sentence-rewrapped-and-bold-passes'
    Files = [ordered]@{
        'AGENTS.md' = "Intro.`r`n`r`n**Read ``docs/writing-principles.md`` before authoring or editing any prose or`r`ncomment   in this`trepository.** More text.`r`n"
    }
    Exit = 0
    Expected = @($Scope, (Get-Summary 4 4 0 0 0))
}

Add-Case @{
    Name = 'entry-policy-sentence-with-a-changed-word-fails'
    Files = [ordered]@{ 'AGENTS.md' = $PolicySentence.Replace('any prose or comment', 'any prose or comments') + "`n" }
    Exit = 1
    Expected = @($PointerRecord, $Scope, (Get-Summary 4 4 0 0 1))
}

Add-Case @{
    Name = 'entry-incidental-policy-path-fails'
    Files = [ordered]@{ 'AGENTS.md' = "Style lives in docs/writing-principles.md.`n" }
    Exit = 1
    Expected = @($PointerRecord, $Scope, (Get-Summary 4 4 0 0 1))
}

Add-Case @{
    Name = 'entry-missing-policy-file'
    Omit = @('docs/writing-principles.md')
    Exit = 1
    Expected = @((Get-Record 'required-file-missing' 'docs/writing-principles.md' 0 'error' $RequiredMessage), $Scope, (Get-Summary 3 3 0 0 1))
}

Add-Case @{
    Name = 'entry-missing-agents-file'
    Omit = @('AGENTS.md')
    Exit = 1
    Expected = @((Get-Record 'required-file-missing' 'AGENTS.md' 0 'error' $RequiredMessage), $Scope, (Get-Summary 3 3 0 0 1))
}

Add-Case @{
    Name = 'entry-missing-claude-file'
    Omit = @('CLAUDE.md')
    Exit = 1
    Expected = @((Get-Record 'required-file-missing' 'CLAUDE.md' 0 'error' $RequiredMessage), $Scope, (Get-Summary 3 3 0 0 1))
}

Add-Case @{
    Name = 'entry-claude-crlf-leading-blank-lines-and-later-guidance-pass'
    Files = [ordered]@{ 'CLAUDE.md' = "`r`n  `t`r`n@AGENTS.md `t`r`nClaude-only guidance.`r`n" }
    Exit = 0
    Expected = @($Scope, (Get-Summary 4 4 0 0 0))
}

foreach ($variant in @(
        @{ Suffix = 'later-line-import'; Text = "Guidance first.`n@AGENTS.md`n"; Line = 1 }
        @{ Suffix = 'different-import'; Text = "`n@OTHER.md`n"; Line = 2 }
        @{ Suffix = 'extra-text-after-import'; Text = "@AGENTS.md please`n"; Line = 1 }
        @{ Suffix = 'indented-import'; Text = "  @AGENTS.md`n"; Line = 1 })) {
    Add-Case @{
        Name = "entry-claude-$($variant.Suffix)-fails"
        Files = [ordered]@{ 'CLAUDE.md' = $variant.Text }
        Exit = 1
        Expected = @((Get-Record 'instruction-import-invalid' 'CLAUDE.md' $variant.Line 'error' $ImportMessage), $Scope, (Get-Summary 4 4 0 0 1))
    }
}

Add-Case @{
    Name = 'entry-claude-without-a-nonblank-line-fails'
    Files = [ordered]@{ 'CLAUDE.md' = " `r`n`n" }
    Exit = 1
    Expected = @(
        (Get-Record 'instruction-import-invalid' 'CLAUDE.md' 0 'error' 'the file has no nonblank line; its first must be exactly @AGENTS.md')
        $Scope
        (Get-Summary 4 4 0 0 1)
    )
}

Add-Case @{
    Name = 'entry-checks-run-when-path-selects-another-file'
    Files = [ordered]@{ 'AGENTS.md' = "No sentence here.`n"; 'CLAUDE.md' = "@OTHER.md`n"; 'notes.md' = "Notes.`n" }
    Operands = @('notes.md')
    Exit = 1
    Expected = @($PointerRecord, (Get-Record 'instruction-import-invalid' 'CLAUDE.md' 1 'error' $ImportMessage), $Scope, (Get-Summary 1 1 0 0 2))
}

# An instruction entry file that cannot be read or decoded is reported once, in one wording: by
# the selected-file checks when the run selects it, otherwise by the fixed checks, which then stay
# out of the selection counts. No entry check runs on bytes that did not decode, and each invalid
# file below would pass its entry check if its invalid byte were ignored.
foreach ($variant in @(
        @{ Label = 'agents'; File = 'AGENTS.md'; Bytes = [byte[]]((ConvertTo-Bytes "# Agents`n`n$PolicySentence`n") + @(0xFF) + (ConvertTo-Bytes "`n$Id7`n")) }
        @{ Label = 'claude'; File = 'CLAUDE.md'; Bytes = [byte[]]((ConvertTo-Bytes "@AGENTS.md`n") + @(0xFF) + (ConvertTo-Bytes "`n")) })) {
    Add-Case @{
        Name = "entry-invalid-utf8-$($variant.Label)-gets-one-encoding-error"
        Files = [ordered]@{ $variant.File = $variant.Bytes }
        Exit = 1
        Expected = @((Get-Record 'encoding-invalid-utf8' $variant.File 1 'error' $InvalidUtf8Message), $Scope, (Get-Summary 4 3 0 0 1))
    }
    Add-Case @{
        Name = "entry-invalid-utf8-$($variant.Label)-outside-the-selection-gets-one-encoding-error"
        Files = [ordered]@{ $variant.File = $variant.Bytes; 'notes.md' = "Notes.`n" }
        Operands = @('notes.md')
        Exit = 1
        Expected = @((Get-Record 'encoding-invalid-utf8' $variant.File 1 'error' $InvalidUtf8Message), $Scope, (Get-Summary 1 1 0 0 1))
    }
    Add-Case @{
        Name = "entry-unreadable-$($variant.Label)-is-refused-once-and-the-scan-continues"
        Requires = 'exclusive-open'
        Files = [ordered]@{ 'hit.md' = "$Id7`n" }
        Hold = $variant.File
        Exit = 2
        Expected = @((Get-Record 'checker-file-unreadable' $variant.File 0 'fatal' $UnreadableMessage), (Get-IdRecord 'hit.md' 1 $Id7), $Scope, (Get-Summary 5 4 0 1 1))
    }
    Add-Case @{
        Name = "entry-unreadable-$($variant.Label)-outside-the-selection-is-refused-once"
        Requires = 'exclusive-open'
        Files = [ordered]@{ 'notes.md' = "Notes.`n" }
        Hold = $variant.File
        Operands = @('notes.md')
        Exit = 2
        Expected = @((Get-Record 'checker-file-unreadable' $variant.File 0 'fatal' $UnreadableMessage), $Scope, (Get-Summary 1 1 0 0 0))
    }
}

# The fixed checks need only the writing policy's existence, so its bytes are assessed where a run
# selects it and nowhere else.
$InvalidPolicy = [byte[]]((ConvertTo-Bytes "# Writing`n") + @(0xFF) + (ConvertTo-Bytes "`n"))
Add-Case @{
    Name = 'entry-invalid-utf8-writing-policy-gets-one-encoding-error'
    Files = [ordered]@{ 'docs/writing-principles.md' = $InvalidPolicy }
    Exit = 1
    Expected = @((Get-Record 'encoding-invalid-utf8' 'docs/writing-principles.md' 1 'error' $InvalidUtf8Message), $Scope, (Get-Summary 4 3 0 0 1))
}

Add-Case @{
    Name = 'entry-invalid-utf8-writing-policy-outside-the-selection-is-not-assessed'
    Files = [ordered]@{ 'docs/writing-principles.md' = $InvalidPolicy; 'notes.md' = "Notes.`n" }
    Operands = @('notes.md')
    Exit = 0
    Expected = @($Scope, (Get-Summary 1 1 0 0 0))
}

# Membership

Add-Case @{
    Name = 'membership-star-stays-in-a-segment-and-double-star-spans-zero-or-more'
    Files = [ordered]@{
        'one/a.txt' = "$Id7`n"
        'one/sub/b.txt' = "$Id7`n"
        'deep/end.txt' = "$Id7`n"
        'deep/x/y/end.txt' = "$Id7`n"
        'deep/x/other.txt' = "$Id7`n"
    }
    Manifest = $BaseManifest + "+ one/*.txt`n+ deep/**/end.txt`n"
    Exit = 1
    Expected = @(
        (Get-IdRecord 'deep/end.txt' 1 $Id7)
        (Get-IdRecord 'deep/x/y/end.txt' 1 $Id7)
        (Get-IdRecord 'one/a.txt' 1 $Id7)
        $Scope
        (Get-Summary 7 7 0 0 3)
    )
}

Add-Case @{
    Name = 'membership-globs-match-case-sensitively'
    Files = [ordered]@{ 'upper.MD' = "$Id7`n" }
    Exit = 0
    Expected = @($Scope, (Get-Summary 4 4 0 0 0))
}

Add-Case @{
    Name = 'membership-exclude-wins-over-include'
    Files = [ordered]@{ 'keep.md' = "$Id7`n"; 'skip.md' = "$Id7`n" }
    Manifest = $BaseManifest + "- skip.md`n"
    Exit = 1
    Expected = @((Get-IdRecord 'keep.md' 1 $Id7), $Scope, (Get-Summary 5 5 0 0 1))
}

Add-Case @{
    Name = 'membership-include-matching-no-file-is-malformed'
    Manifest = $BaseManifest + "+ nothing/*.txt`n- nothing/**`n"
    Exit = 2
    Expected = @((Get-MalformedRecord 4 'the include matches no file'), $Scope, (Get-Summary 0 0 0 0 0))
}

Add-Case @{
    Name = 'membership-retired-and-unknown-directives-are-malformed'
    Manifest = $BaseManifest + "! identifier-obsolete AGENTS.md reason=`"kept`"`n~ preserved-prose AGENTS.md reason=`"kept`"`n@ instruction AGENTS.md`ninclude AGENTS.md`n+`n"
    Exit = 2
    Expected = @(
        (Get-MalformedRecord 4 $LineMessage)
        (Get-MalformedRecord 5 $LineMessage)
        (Get-MalformedRecord 6 $LineMessage)
        (Get-MalformedRecord 7 $LineMessage)
        (Get-MalformedRecord 8 $LineMessage)
        $Scope
        (Get-Summary 0 0 0 0 0)
    )
}

Add-Case @{
    Name = 'membership-min-files-declared-twice-is-malformed'
    Manifest = "min-files 1`n+ **/*.md`n+ eng/authored-files.txt`nmin-files 2`n"
    Exit = 2
    Expected = @((Get-MalformedRecord 4 'min-files is declared more than once'), $Scope, (Get-Summary 0 0 0 0 0))
}

Add-Case @{
    Name = 'membership-min-files-missing-or-invalid-is-malformed'
    Manifest = "+ **/*.md`n+ eng/authored-files.txt`nmin-files 0`nmin-files x`n"
    Exit = 2
    Expected = @(
        (Get-MalformedRecord 0 'min-files is not declared')
        (Get-MalformedRecord 3 $LineMessage)
        (Get-MalformedRecord 4 $LineMessage)
        $Scope
        (Get-Summary 0 0 0 0 0)
    )
}

Add-Case @{
    Name = 'membership-illegal-globs-are-malformed'
    Manifest = $BaseManifest + "+ /AGENTS.md`n+ C:/AGENTS.md`n+ docs\writing-principles.md`n+ ./AGENTS.md`n+ docs/../AGENTS.md`n- ../outside/**`n"
    Exit = 2
    Expected = @(
        (Get-MalformedRecord 4 $GlobMessage)
        (Get-MalformedRecord 5 $GlobMessage)
        (Get-MalformedRecord 6 $GlobMessage)
        (Get-MalformedRecord 7 $GlobMessage)
        (Get-MalformedRecord 8 $GlobMessage)
        (Get-MalformedRecord 9 $GlobMessage)
        $Scope
        (Get-Summary 0 0 0 0 0)
    )
}

Add-Case @{
    Name = 'membership-manifest-that-is-not-valid-utf8-is-malformed'
    Manifest = [byte[]]((ConvertTo-Bytes $BaseManifest) + @(0xFF) + (ConvertTo-Bytes "`n"))
    Exit = 2
    Expected = @((Get-MalformedRecord 0 'the manifest is not valid UTF-8'), $Scope, (Get-Summary 0 0 0 0 0))
}

Add-Case @{
    Name = 'membership-floor-met-exactly'
    Files = [ordered]@{ 'extra.md' = "Extra.`n" }
    Manifest = "min-files 5`n+ **/*.md`n+ eng/authored-files.txt`n"
    Exit = 0
    Expected = @($Scope, (Get-Summary 5 5 0 0 0))
}

Add-Case @{
    Name = 'membership-floor-not-met-is-refused'
    Files = [ordered]@{ 'extra.md' = "Extra.`n" }
    Manifest = "min-files 6`n+ **/*.md`n+ eng/authored-files.txt`n"
    Exit = 2
    Expected = @(
        (Get-Record 'checker-scan-empty' 'eng/authored-files.txt' 1 'fatal' 'the manifest selects 5 files, fewer than min-files 6')
        $Scope
        (Get-Summary 0 0 0 0 0)
    )
}

Add-Case @{
    Name = 'membership-prefix-exclude-prunes-a-directory-holding-an-outside-link'
    Requires = 'directory-link'
    Files = [ordered]@{ 'generated/out.md' = "$Id7`n" }
    Manifest = $BaseManifest + "- generated/**`n"
    Arrange = {
        param($Fixture)
        Write-FixtureFile $Fixture.CaseDirectory 'outside/secret.md' "$Id7`n"
        New-DirectoryLink ([IO.Path]::Combine($Fixture.Repo, 'generated', 'link')) ([IO.Path]::Combine($Fixture.CaseDirectory, 'outside'))
    }
    Exit = 0
    Expected = @($Scope, (Get-Summary 4 4 0 0 0))
}

Add-Case @{
    Name = 'membership-exclude-removing-a-required-file-is-malformed'
    Manifest = $BaseManifest + "- CLAUDE.md`n"
    Exit = 2
    Expected = @((Get-MalformedRecord 4 'the exclude removes the required file CLAUDE.md'), $Scope, (Get-Summary 0 0 0 0 0))
}

Add-Case @{
    Name = 'membership-required-file-left-unselected-is-malformed'
    Manifest = "min-files 1`n+ AGENTS.md`n+ docs/*.md`n+ eng/authored-files.txt`n"
    Exit = 2
    Expected = @((Get-MalformedRecord 0 'no include selects the required file CLAUDE.md'), $Scope, (Get-Summary 0 0 0 0 0))
}

Add-Case @{
    Name = 'membership-ignored-files-are-selected-and-git-is-skipped'
    Files = [ordered]@{ '.gitignore' = "ignored.md`n"; 'ignored.md' = "$Id7`n"; '.git/hidden.md' = "$Id8`n" }
    Exit = 1
    Expected = @((Get-IdRecord 'ignored.md' 1 $Id7), $Scope, (Get-Summary 5 5 0 0 1))
}

# -Path selection. The manifest floor equals the whole set, so a narrower selection would fail it
# if the floor were applied to the selection.

$PathFiles = [ordered]@{ 'a.md' = "$Id7`n"; 'docs/b.md' = "$Id7`n"; 'docs/sub/c.md' = "$Id7`n"; 'skip.md' = "$Id7`n" }
$PathManifest = "min-files 7`n+ **/*.md`n+ eng/authored-files.txt`n- skip.md`n"

foreach ($variant in @(
        @{ Suffix = 'file'; Operands = @('a.md'); Records = @((Get-IdRecord 'a.md' 1 $Id7)); Selected = 1 }
        @{ Suffix = 'directory'; Operands = @('docs'); Records = @((Get-IdRecord 'docs/b.md' 1 $Id7), (Get-IdRecord 'docs/sub/c.md' 1 $Id7)); Selected = 3 }
        @{ Suffix = 'dot'; Operands = @('.'); Records = @((Get-IdRecord 'a.md' 1 $Id7), (Get-IdRecord 'docs/b.md' 1 $Id7), (Get-IdRecord 'docs/sub/c.md' 1 $Id7)); Selected = 7 }
        @{ Suffix = 'duplicates-and-several-operands'; Operands = @('a.md', 'a.md', 'docs/sub/'); Records = @((Get-IdRecord 'a.md' 1 $Id7), (Get-IdRecord 'docs/sub/c.md' 1 $Id7)); Selected = 2 }
        @{ Suffix = 'non-escaping-parent-segment'; Operands = @('docs/../a.md'); Records = @((Get-IdRecord 'a.md' 1 $Id7)); Selected = 1 })) {
    Add-Case @{
        Name = "path-$($variant.Suffix)"
        Files = $PathFiles
        Manifest = $PathManifest
        Operands = $variant.Operands
        Exit = 1
        Expected = @($variant.Records) + @($Scope, (Get-Summary $variant.Selected $variant.Selected 0 0 $variant.Records.Count))
    }
}

Add-Case @{
    Name = 'path-missing-operand-beside-a-valid-one-is-refused'
    Files = $PathFiles
    Manifest = $PathManifest
    Operands = @('a.md', 'missing.md')
    Exit = 2
    Expected = @(
        (Get-Record 'checker-scan-empty' 'missing.md' 0 'fatal' "the -Path operand 'missing.md' selects no file of the manifest set")
        $Scope
        (Get-Summary 0 0 0 0 0)
    )
}

Add-Case @{
    Name = 'path-excluded-operand-is-refused'
    Files = $PathFiles
    Manifest = $PathManifest
    Operands = @('skip.md')
    Exit = 2
    Expected = @(
        (Get-Record 'checker-scan-empty' 'skip.md' 0 'fatal' "the -Path operand 'skip.md' selects no file of the manifest set")
        $Scope
        (Get-Summary 0 0 0 0 0)
    )
}

Add-Case @{
    Name = 'path-operand-differing-only-in-case-is-refused'
    Files = $PathFiles
    Manifest = $PathManifest
    Operands = @('A.md')
    Exit = 2
    Expected = @(
        (Get-Record 'checker-scan-empty' 'A.md' 0 'fatal' "the -Path operand 'A.md' selects no file of the manifest set")
        $Scope
        (Get-Summary 0 0 0 0 0)
    )
}

Add-Case @{
    Name = 'path-empty-operand-is-refused'
    Files = $PathFiles
    Manifest = $PathManifest
    Operands = @('')
    Exit = 2
    Expected = @((Get-Record 'checker-scan-empty' '-' 0 'fatal' 'a -Path operand is empty'), $Scope, (Get-Summary 0 0 0 0 0))
}

Add-Case @{
    Name = 'path-unsafe-operands-are-refused'
    Files = $PathFiles
    Manifest = $PathManifest
    Operands = @('../x.md', '/x.md', 'C:/x.md', '//server/share/x.md', '../repo-sibling/x.md')
    Exit = 2
    Expected = @(
        (Get-Record 'checker-path-escape' '-' 0 'fatal' "the -Path operand '../repo-sibling/x.md' leaves the repository")
        (Get-Record 'checker-path-escape' '-' 0 'fatal' "the -Path operand '../x.md' leaves the repository")
        (Get-Record 'checker-path-escape' '-' 0 'fatal' "the -Path operand '//server/share/x.md' is a UNC path")
        (Get-Record 'checker-path-escape' '-' 0 'fatal' "the -Path operand '/x.md' is rooted")
        (Get-Record 'checker-path-escape' '-' 0 'fatal' "the -Path operand 'C:/x.md' is drive-qualified")
        $Scope
        (Get-Summary 0 0 0 0 0)
    )
}

Add-Case @{
    Name = 'path-floor-still-applies-to-the-manifest-set'
    Files = $PathFiles
    Manifest = $PathManifest.Replace('min-files 7', 'min-files 8')
    Operands = @('a.md')
    Exit = 2
    Expected = @(
        (Get-Record 'checker-scan-empty' 'eng/authored-files.txt' 1 'fatal' 'the manifest selects 7 files, fewer than min-files 8')
        $Scope
        (Get-Summary 0 0 0 0 0)
    )
}

# The child process starts in the session directory, not in the directory of the command, so a
# relative root resolves only if the command resolves it from the process location.
Add-Case @{
    Name = 'path-relative-root-from-another-process-location-with-several-and-duplicate-operands'
    Files = [ordered]@{ 'notes.md' = "Notes.`n" }
    Operands = @('notes.md', 'AGENTS.md', 'notes.md', 'docs')
    Arrange = { param($Fixture) $Fixture.RepoRoot = [IO.Path]::GetRelativePath($SessionRoot, $Fixture.Repo) }
    Exit = 0
    Expected = @($Scope, (Get-Summary 3 3 0 0 0))
}

# File system

Add-Case @{
    Name = 'filesystem-outside-link-is-refused-at-its-segment'
    Requires = 'directory-link'
    Arrange = {
        param($Fixture)
        Write-FixtureFile $Fixture.CaseDirectory 'outside/secret.md' "$Id7`n"
        New-DirectoryLink ([IO.Path]::Combine($Fixture.Repo, 'jump')) ([IO.Path]::Combine($Fixture.CaseDirectory, 'outside'))
    }
    Exit = 2
    Expected = @((Get-Record 'checker-path-escape' 'jump' 0 'fatal' $ReparseMessage), $Scope, (Get-Summary 0 0 0 0 0))
}

Add-Case @{
    Name = 'filesystem-inside-alias-is-refused-not-skipped'
    Requires = 'directory-link'
    Files = [ordered]@{ 'a-real/bad.md' = "$Id7`n" }
    Manifest = $BaseManifest + "+ z-alias/**/*.md`n"
    Arrange = {
        param($Fixture)
        New-DirectoryLink ([IO.Path]::Combine($Fixture.Repo, 'z-alias')) ([IO.Path]::Combine($Fixture.Repo, 'a-real'))
    }
    Exit = 2
    Expected = @((Get-Record 'checker-path-escape' 'z-alias' 0 'fatal' $ReparseMessage), $Scope, (Get-Summary 0 0 0 0 0))
}

Add-Case @{
    Name = 'filesystem-link-under-a-selected-directory-is-refused'
    Requires = 'directory-link'
    Files = [ordered]@{ 'dir/ok.md' = "Fine.`n" }
    Operands = @('dir')
    Arrange = {
        param($Fixture)
        Write-FixtureFile $Fixture.CaseDirectory 'outside/secret.md' "$Id7`n"
        New-DirectoryLink ([IO.Path]::Combine($Fixture.Repo, 'dir', 'jump')) ([IO.Path]::Combine($Fixture.CaseDirectory, 'outside'))
    }
    Exit = 2
    Expected = @((Get-Record 'checker-path-escape' 'dir/jump' 0 'fatal' $ReparseMessage), $Scope, (Get-Summary 0 0 0 0 0))
}

Add-Case @{
    Name = 'filesystem-inside-link-under-a-selected-directory-is-refused'
    Requires = 'directory-link'
    Files = [ordered]@{ 'dir/ok.md' = "Fine.`n"; 'real/target.md' = "Fine.`n" }
    Operands = @('dir')
    Arrange = {
        param($Fixture)
        New-DirectoryLink ([IO.Path]::Combine($Fixture.Repo, 'dir', 'alias')) ([IO.Path]::Combine($Fixture.Repo, 'real'))
    }
    Exit = 2
    Expected = @((Get-Record 'checker-path-escape' 'dir/alias' 0 'fatal' $ReparseMessage), $Scope, (Get-Summary 0 0 0 0 0))
}

Add-Case @{
    Name = 'filesystem-dangling-link-is-refused'
    Requires = 'directory-link'
    Arrange = {
        param($Fixture)
        $goneTarget = [IO.Path]::Combine($Fixture.CaseDirectory, 'gone')
        [void][IO.Directory]::CreateDirectory($goneTarget)
        New-DirectoryLink ([IO.Path]::Combine($Fixture.Repo, 'dangle')) $goneTarget
        [IO.Directory]::Delete($goneTarget)
    }
    Exit = 2
    Expected = @((Get-Record 'checker-path-escape' 'dangle' 0 'fatal' $ReparseMessage), $Scope, (Get-Summary 0 0 0 0 0))
}

Add-Case @{
    Name = 'filesystem-file-link-is-refused'
    Requires = 'file-link'
    Arrange = {
        param($Fixture)
        Write-FixtureFile $Fixture.CaseDirectory 'outside/secret.md' "$Id7`n"
        $null = [IO.File]::CreateSymbolicLink([IO.Path]::Combine($Fixture.Repo, 'link.md'), [IO.Path]::Combine($Fixture.CaseDirectory, 'outside', 'secret.md'))
    }
    Exit = 2
    Expected = @((Get-Record 'checker-path-escape' 'link.md' 0 'fatal' $ReparseMessage), $Scope, (Get-Summary 0 0 0 0 0))
}

Add-Case @{
    Name = 'filesystem-unreadable-file-is-refused-and-the-scan-continues'
    Requires = 'exclusive-open'
    Files = [ordered]@{ 'locked.md' = "$Id8`n"; 'hit.md' = "$Id7`n" }
    Hold = 'locked.md'
    Exit = 2
    Expected = @(
        (Get-Record 'checker-file-unreadable' 'locked.md' 0 'fatal' 'the file could not be read, so no check ran on it')
        (Get-IdRecord 'hit.md' 1 $Id7)
        $Scope
        (Get-Summary 6 5 0 1 1)
    )
}

# Only for the run itself, the current user may not list 'locked'; its permissions are restored
# exactly, and checked, before the case directory is hashed again.
Add-Case @{
    Name = 'filesystem-directory-that-cannot-be-listed-is-refused-without-naming-its-entries'
    Requires = 'listing-denial'
    Files = [ordered]@{ 'locked/secret.md' = "$Id7`n" }
    Deny = 'locked'
    Exit = 2
    Expected = @(
        (Get-Record 'checker-file-unreadable' 'locked' 0 'fatal' 'the directory could not be listed, so the selection cannot be resolved')
        $Scope
        (Get-Summary 0 0 0 0 0)
    )
}

Add-Case @{
    Name = 'filesystem-root-that-is-a-link-is-refused'
    Requires = 'directory-link'
    Arrange = {
        param($Fixture)
        $rootLink = [IO.Path]::Combine($Fixture.CaseDirectory, 'root-link')
        New-DirectoryLink $rootLink $Fixture.Repo
        $Fixture.RepoRoot = $rootLink
    }
    Exit = 2
    Expected = @((Get-Record 'checker-path-escape' '-' 0 'fatal' 'the repository root is a reparse point and is not followed'), $Scope, (Get-Summary 0 0 0 0 0))
}

# Each link below points back at its case directory, so the root's spelling passes through a link
# to an ordinary fixture whose raw hit would be reported if anything behind the link were read.
Add-Case @{
    Name = 'filesystem-root-beneath-a-linked-ancestor-is-refused-before-the-manifest-is-read'
    Requires = 'directory-link'
    Files = [ordered]@{ 'hit.md' = "$Id7`n" }
    Arrange = {
        param($Fixture)
        $linked = [IO.Path]::Combine($Fixture.CaseDirectory, 'linked-parent')
        New-DirectoryLink $linked $Fixture.CaseDirectory
        $Fixture.RepoRoot = [IO.Path]::Combine($linked, 'repo')
    }
    Exit = 2
    Expected = @(
        (Get-Record 'checker-path-escape' '-' 0 'fatal' "the ancestor 'linked-parent' of the repository root is a reparse point and is not followed")
        $Scope
        (Get-Summary 0 0 0 0 0)
    )
}

Add-Case @{
    Name = 'filesystem-first-linked-ancestor-of-the-root-is-the-one-refused'
    Requires = 'directory-link'
    Files = [ordered]@{ 'hit.md' = "$Id7`n" }
    Arrange = {
        param($Fixture)
        New-DirectoryLink ([IO.Path]::Combine($Fixture.CaseDirectory, 'outer')) $Fixture.CaseDirectory
        New-DirectoryLink ([IO.Path]::Combine($Fixture.CaseDirectory, 'inner')) $Fixture.CaseDirectory
        $Fixture.RepoRoot = [IO.Path]::Combine($Fixture.CaseDirectory, 'outer', 'inner', 'repo')
    }
    Exit = 2
    Expected = @(
        (Get-Record 'checker-path-escape' '-' 0 'fatal' "the ancestor 'outer' of the repository root is a reparse point and is not followed")
        $Scope
        (Get-Summary 0 0 0 0 0)
    )
}

Add-Case @{
    Name = 'filesystem-missing-root-is-refused'
    Arrange = { param($Fixture) $Fixture.RepoRoot = [IO.Path]::Combine($Fixture.CaseDirectory, 'absent') }
    Exit = 2
    Expected = @((Get-Record 'checker-manifest-missing' '-' 0 'fatal' 'the repository root is not an existing directory'), $Scope, (Get-Summary 0 0 0 0 0))
}

Add-Case @{
    Name = 'filesystem-missing-manifest-is-refused'
    Manifest = $null
    Exit = 2
    Expected = @((Get-Record 'checker-manifest-missing' 'eng/authored-files.txt' 0 'fatal' 'the manifest does not exist as a regular file'), $Scope, (Get-Summary 0 0 0 0 0))
}

Add-Case @{
    Name = 'filesystem-unreadable-manifest-is-refused'
    Requires = 'exclusive-open'
    Hold = 'eng/authored-files.txt'
    Exit = 2
    Expected = @((Get-Record 'checker-file-unreadable' 'eng/authored-files.txt' 0 'fatal' 'the manifest could not be read'), $Scope, (Get-Summary 0 0 0 0 0))
}

Add-Case @{
    Name = 'filesystem-manifest-directory-that-is-a-link-is-refused'
    Requires = 'directory-link'
    Manifest = $null
    Arrange = {
        param($Fixture)
        Write-FixtureFile $Fixture.CaseDirectory 'outside/authored-files.txt' $BaseManifest
        New-DirectoryLink ([IO.Path]::Combine($Fixture.Repo, 'eng')) ([IO.Path]::Combine($Fixture.CaseDirectory, 'outside'))
    }
    Exit = 2
    Expected = @((Get-Record 'checker-path-escape' 'eng' 0 'fatal' $ReparseMessage), $Scope, (Get-Summary 0 0 0 0 0))
}

Add-Case @{
    Name = 'filesystem-selected-name-with-a-tab-is-refused'
    Requires = 'tab-file-name'
    Files = [ordered]@{ "a`tb.md" = "Fine.`n" }
    Exit = 2
    Expected = @(
        (Get-Record 'checker-path-escape' 'a b.md' 0 'fatal' 'the selected path contains a tab, carriage return or line feed, shown here as a space')
        $Scope
        (Get-Summary 0 0 0 0 0)
    )
}

# Reporting

$OrderingFiles = [ordered]@{
    'B.md' = "x`n$Id7 $Stage2`nx`nx`nx`nx`nx`nx`nx`n$Id7`n"
    '_c.md' = "$Id7`n"
    'a.md' = "$Id8 $Id7`n"
    'tests/Z.cs' = [byte[]](0xFF)
}
$OrderingManifest = $BaseManifest + "+ tests/**/*.cs`n"
$OrderingExpected = @(
    (Get-UndecodableRecord 'tests/Z.cs')
    (Get-IdRecord 'B.md' 2 $Id7)
    (Get-StageRecord 'B.md' 2 $Stage2)
    (Get-IdRecord 'B.md' 10 $Id7)
    (Get-IdRecord '_c.md' 1 $Id7)
    (Get-IdRecord 'a.md' 1 $Id7)
    (Get-IdRecord 'a.md' 1 $Id8)
    $Scope
    (Get-Summary 8 7 0 1 6)
)

Add-Case @{
    Name = 'reporting-ordinal-order-and-byte-identical-repeat'
    Files = $OrderingFiles
    Manifest = $OrderingManifest
    Repeat = $true
    Exit = 2
    Expected = $OrderingExpected
}

Add-Case @{
    Name = 'reporting-same-output-under-tr-tr'
    Requires = 'turkish-culture'
    Files = $OrderingFiles
    Manifest = $OrderingManifest
    Culture = 'tr-TR'
    Exit = 2
    Expected = $OrderingExpected
}

Add-Case @{
    Name = 'reporting-internal-failure-keeps-collected-findings'
    Files = [ordered]@{ 'hit.md' = "$Id7`n" }
    Arrange = {
        param($Fixture)
        # A private copy fails just before its final statement, the one that writes the report.
        $lines = [Collections.Generic.List[string]]::new([IO.File]::ReadAllText($Runtime).Replace("`r`n", "`n").Split("`n"))
        $last = $lines.Count - 1
        while ($lines[$last].Trim().Length -eq 0) { $last-- }
        $lines.Insert($last, "throw 'injected failure'")
        $copy = [IO.Path]::Combine($Fixture.CaseDirectory, 'tool', 'check-authored-text.ps1')
        Write-FixtureFile $Fixture.CaseDirectory 'tool/check-authored-text.ps1' ([string]::Join("`n", $lines))
        $Fixture.Runtime = $copy
    }
    Exit = 2
    Expected = @(
        (Get-Record 'checker-internal-failure' '-' 0 'fatal' 'unexpected failure (System.Management.Automation.RuntimeException); the assessment is incomplete')
        (Get-IdRecord 'hit.md' 1 $Id7)
        $Scope
        (Get-Summary 5 5 0 0 1)
    )
}

Add-Case @{
    Name = 'reporting-summary-accounts-every-selected-file'
    Files = [ordered]@{
        'bad.md' = [byte[]](0xC3, 0x28)
        'src/Ok.cs' = "class Ok { }`n"
        'tests/Bad.cs' = [byte[]](0xFF)
    }
    Manifest = $BaseManifest + "+ src/**/*.cs`n+ tests/**/*.cs`n"
    Exit = 2
    Expected = @(
        (Get-UndecodableRecord 'tests/Bad.cs')
        (Get-Record 'encoding-invalid-utf8' 'bad.md' 1 'error' 'the file is not valid UTF-8, so no other check ran on it')
        $Scope
        (Get-Summary 7 4 1 1 1)
    )
}

# What the command does not check: it has no Markdown, link, anchor, style or suppression rule,
# and no syntax can hide a spelling finding.

$Dash = [string][char]0x2014
Add-Case @{
    Name = 'markdown-links-and-style-produce-no-finding'
    Files = [ordered]@{
        'doc.md' = [string]::Join("`n", @(
                '- item', '', "    $fence text", '    inside', "    $fence", '', 'Heading', '=======', '',
                '<a id="x"></a> <A ID=''y''></A>', '\[escaped](missing.md) [broken](missing.md) [fragment](#nowhere)', '',
                '[ref]: missing.md', '[r][ref]', '', "It is a crucial and robust point $Dash truly.",
                'The file is read by the tool - and then it is kept.',
                ('word ' * 40).Trim() + '.', '<!-- a comment -->')) + "`n"
    }
    Exit = 0
    Expected = @($Scope, (Get-Summary 5 5 0 0 0))
}

Add-Case @{
    Name = 'markdown-syntax-cannot-hide-a-spelling'
    Files = [ordered]@{
        'doc.md' = [string]::Join("`n", @(
                '- item', '', "    $fence text", "    $Id7", "    $fence$fence", '', $Stage2, '===', '',
                "<a id=`"$Id8`"></a>", '', "\[$Id7](missing.md)", '', "[x]($OldName)", '', "[r]: $OldName")) + "`n"
    }
    Exit = 1
    Expected = @(
        (Get-IdRecord 'doc.md' 4 $Id7)
        (Get-StageRecord 'doc.md' 7 $Stage2)
        (Get-IdRecord 'doc.md' 10 $Id8)
        (Get-IdRecord 'doc.md' 12 $Id7)
        (Get-FileNameRecord 'doc.md' 14)
        (Get-FileNameRecord 'doc.md' 16)
        $Scope
        (Get-Summary 5 5 0 0 6)
    )
}

Add-Case @{
    Name = 'inline-directive-cannot-hide-a-spelling'
    Files = [ordered]@{
        'doc.md' = "<!-- check-writing: disable=identifier-obsolete reason=`"kept`" -->`n$Id7`n<!-- check-writing: disable=stage-label-obsolete reason=`"kept`" --> $Stage2`n"
    }
    Exit = 1
    Expected = @((Get-IdRecord 'doc.md' 2 $Id7), (Get-StageRecord 'doc.md' 3 $Stage2), $Scope, (Get-Summary 5 5 0 0 2))
}

# The runner itself

Add-Case @{
    Name = 'runner-fails-when-no-case-runs'
    Runner = @('-Filter', 'no case has this name')
    Exit = 1
    Expected = @('comparator self-check: 4 wrong expectations rejected', 'cases=0 executed=0 passed=0 failed=0 skipped=0', 'no case ran')
}

Add-Case @{
    Name = 'runner-real-tree-selects-this-runner'
    RealTree = $true
    Operands = @('eng/check-authored-text.tests.ps1')
    Exit = 0
    Expected = @($Scope, (Get-Summary 1 1 0 0 0))
}

# The runner's own permission handling, each case running this runner as a child with at most one
# fault mode. Where the child keeps its session, {session} stands for that directory and
# {original} for the permissions read from the uncertain directory once it is shown ordinary.
$ListingCase = 'filesystem-directory-that-cannot-be-listed-is-refused-without-naming-its-entries'
$SelfCheckLine = 'comparator self-check: 4 wrong expectations rejected'
$HaltBeforeCases = 'runner infrastructure failure: the session directory is no longer safe to remove, so no case runs'
$HaltAfterCase = 'runner infrastructure failure: the session directory is no longer safe to remove, so no further case runs'
$KeptLine = 'cleanup withheld: the session directory is kept for recovery: {session}'
$ProbeMarkers = @('self-check/repo/eng/authored-files.txt', 'probe/target/file.txt')
$CaseMarkers = $ProbeMarkers + @('c000/repo/locked/secret.md')

function Get-UncertainLine {
    param([string] $Location, [string] $Reason)
    'permissions uncertain: ' + [IO.Path]::Combine([string[]](@('{session}') + $Location.Split('/'))) + ": $Reason; recorded original: {original}"
}

foreach ($variant in @(
        @{ Suffix = 'restore-reported-false'; Step = 'restore-reports-false'; Reason = 'they did not compare equal to the recorded original after restoration' }
        @{ Suffix = 'denial-failing-after-the-change-began'; Step = 'deny-fails-after-mutation-began'; Reason = 'denying its listing failed after the change may have begun (RuntimeException)' })) {
    Add-Case @{
        Name = "runner-child-probe-$($variant.Suffix)-fails-and-keeps-its-session"
        Requires = 'listing-denial'
        Nested = @{ Fault = "probe-$($variant.Step)"; Filter = 'bytes-empty-file'; Kept = 'probe/unlisted'; Markers = $ProbeMarkers }
        Exit = 1
        Expected = @(
            "fault injection active: probe-$($variant.Step)"
            $SelfCheckLine
            $HaltBeforeCases
            'cases=1 executed=0 passed=0 failed=0 skipped=0'
            'no case ran'
            $KeptLine
            (Get-UncertainLine 'probe/unlisted' $variant.Reason)
        )
    }
    Add-Case @{
        Name = "runner-child-case-$($variant.Suffix)-fails-and-keeps-its-session"
        Requires = 'listing-denial'
        Nested = @{ Fault = "case-$($variant.Step)"; Filter = 'cannot-be-listed'; Kept = 'c000/repo/locked'; Markers = $CaseMarkers }
        Exit = 1
        Expected = @(
            "fault injection active: case-$($variant.Step)"
            $SelfCheckLine
            "failed  ${ListingCase}: runner infrastructure failure: the permissions of the denied directory are uncertain"
            $HaltAfterCase
            'cases=1 executed=1 passed=0 failed=1 skipped=0'
            $KeptLine
            (Get-UncertainLine 'c000/repo/locked' $variant.Reason)
        )
    }
}

# A failure before anything changed is still a skip, and the other chosen case still runs.
Add-Case @{
    Name = 'runner-child-probe-denial-failing-before-any-change-is-a-skip-and-cleans-up'
    Requires = @('listing-denial', 'directory-link')
    Nested = @{ Fault = 'probe-deny-fails-before-mutation'; Filter = 'filesystem-d' }
    Exit = 0
    Expected = @(
        'fault injection active: probe-deny-fails-before-mutation'
        $SelfCheckLine
        'passed  filesystem-dangling-link-is-refused'
        "skipped ${ListingCase}: this host could not deny a directory listing (RuntimeException)"
        'cases=2 executed=1 passed=1 failed=0 skipped=1'
    )
}

Add-Case @{
    Name = 'runner-child-normal-denial-is-restored-exactly-and-cleans-up'
    Requires = 'listing-denial'
    Nested = @{ Filter = 'cannot-be-listed' }
    Exit = 0
    Expected = @($SelfCheckLine, "passed  $ListingCase", 'cases=1 executed=1 passed=1 failed=0 skipped=0')
}

# The seam cannot be switched on by accident: a near miss is refused, and a mode never reaches a
# child that did not ask for it, so the grandchild below prints no announcement.
Add-Case @{
    Name = 'runner-child-unknown-fault-mode-is-refused'
    Nested = @{ Fault = 'probe-restore-reports-False'; Filter = 'bytes-empty-file' }
    Exit = 1
    Expected = @("fault injection refused: 'probe-restore-reports-False' is not a fault mode of this runner", 'no case ran')
}

Add-Case @{
    Name = 'runner-child-fault-mode-is-not-inherited-by-its-children'
    Nested = @{ Fault = 'case-restore-reports-false'; Filter = 'runner-fails-when-no-case-runs' }
    Exit = 0
    Expected = @(
        'fault injection active: case-restore-reports-false'
        $SelfCheckLine
        'passed  runner-fails-when-no-case-runs'
        'cases=1 executed=1 passed=1 failed=0 skipped=0'
    )
}

# This runner's own cleanup, each case running it as a child with one meta fault mode. In the first,
# the child runs the first kept-session case above, whose grandchild keeps an ordinary session, and
# the child's verification of that session throws: the child must keep its session and the
# grandchild's, and name the grandchild's temporary directory. Its markers are fixtures of the
# child's session and, below c000/tmp/*, of the grandchild's.
$VerifyingCase = 'runner-child-probe-restore-reported-false-fails-and-keeps-its-session'
Add-Case @{
    Name = 'runner-meta-child-verification-that-throws-fails-and-keeps-both-sessions'
    Requires = 'listing-denial'
    Nested = @{
        MetaFault = 'child-verification-throws'; Filter = $VerifyingCase
        Markers = $ProbeMarkers + @('c000/tmp/*/self-check/repo/eng/authored-files.txt', 'c000/tmp/*/probe/target/file.txt')
    }
    Exit = 1
    Expected = @(
        'meta fault injection active: child-verification-throws'
        $SelfCheckLine
        "failed  ${VerifyingCase}: unexpected RuntimeException in the runner"
        $HaltAfterCase
        'cases=1 executed=1 passed=0 failed=1 skipped=0'
        $KeptLine
        ('kept unverified: ' + [IO.Path]::Combine('{session}', 'c000', 'tmp') + ': a child runner used it and it was not verified')
    )
}

Add-Case @{
    Name = 'runner-meta-final-cleanup-that-throws-fails-and-keeps-the-session'
    Nested = @{ MetaFault = 'final-cleanup-throws'; Filter = 'bytes-empty-file'; Markers = $ProbeMarkers + @('c000/repo/empty.md') }
    Exit = 1
    Expected = @(
        'meta fault injection active: final-cleanup-throws'
        $SelfCheckLine
        'passed  bytes-empty-file'
        'cases=1 executed=1 passed=1 failed=0 skipped=0'
        'cleanup failed: removing the session directory failed (RuntimeException), and what remains of it is kept for recovery: {session}'
    )
}

Add-Case @{
    Name = 'runner-meta-unknown-fault-mode-is-refused'
    Nested = @{ MetaFault = 'final-cleanup-Throws'; Filter = 'bytes-empty-file' }
    Exit = 1
    Expected = @("meta fault injection refused: 'final-cleanup-Throws' is not a meta fault mode of this runner", 'no case ran')
}

# The mode is inert here, since the chosen case verifies no kept session; a grandchild that
# inherited it would announce it and fail the case.
Add-Case @{
    Name = 'runner-meta-fault-mode-is-not-inherited-by-its-children'
    Nested = @{ MetaFault = 'child-verification-throws'; Filter = 'runner-fails-when-no-case-runs' }
    Exit = 0
    Expected = @(
        'meta fault injection active: child-verification-throws'
        $SelfCheckLine
        'passed  runner-fails-when-no-case-runs'
        'cases=1 executed=1 passed=1 failed=0 skipped=0'
    )
}

# ---------------------------------------------------------------------------
# Execution
# ---------------------------------------------------------------------------

function New-CaseContext {
    param([hashtable] $Case, [string] $CaseDirectory)
    $repo = [IO.Path]::Combine($CaseDirectory, 'repo')
    $context = [pscustomobject]@{ CaseDirectory = $CaseDirectory; Repo = $repo; RepoRoot = $repo; Runtime = $Runtime }
    [void][IO.Directory]::CreateDirectory($repo)
    $omit = $Case.ContainsKey('Omit') ? $Case.Omit : @()
    foreach ($pair in $BaseFiles.GetEnumerator()) {
        if ($omit -notcontains $pair.Key) { Write-FixtureFile $repo $pair.Key $pair.Value }
    }
    if ($Case.ContainsKey('Files')) { foreach ($pair in $Case.Files.GetEnumerator()) { Write-FixtureFile $repo $pair.Key $pair.Value } }
    $manifest = $Case.ContainsKey('Manifest') ? $Case.Manifest : $BaseManifest
    if ($null -ne $manifest) { Write-FixtureFile $repo 'eng/authored-files.txt' $manifest }
    return $context
}

# Runs this runner as a child whose temporary directory is <case>/tmp, with the case's fault modes,
# and compares its exit code and complete output. A child expected to clean up must leave that
# directory empty. A child expected to keep its session, a case with Markers, must leave exactly
# that one directory, ordinary throughout by Test-OrdinaryTree, with its fixtures still present, so
# no cleanup began; only then is the kept session removed. The directory is registered as kept
# before the child starts, and nothing below catches an exception, so a failure or an exception at
# any step leaves it registered: this run then keeps its own session too. Only the last step, after
# the directory is shown empty again, drops the registration.
function Invoke-NestedRunner {
    param([hashtable] $Case, [string] $CaseDirectory)
    $nested = $Case.Nested
    $childTemp = [IO.Path]::Combine($CaseDirectory, 'tmp')
    $registration = [pscustomobject]@{ Directory = $childTemp; Reason = 'a child runner used it and it was not verified' }
    $KeptPaths.Add($registration)
    [void][IO.Directory]::CreateDirectory($childTemp)
    $childVariables = @{ TMP = $childTemp; TEMP = $childTemp; TMPDIR = $childTemp }
    if ($nested.ContainsKey('Fault')) { $childVariables[$FaultVariable] = $nested.Fault }
    if ($nested.ContainsKey('MetaFault')) { $childVariables[$MetaFaultVariable] = $nested.MetaFault }
    $actual = Invoke-Child @('-File', $PSCommandPath, '-Filter', $nested.Filter) $childVariables
    $left = [IO.Directory]::GetFileSystemEntries($childTemp)
    if ($nested.ContainsKey('Markers')) {
        if ($left.Length -eq 0) {
            return "failed: the child runner removed its session directory instead of keeping it ($(Compare-Outcome $Case.Exit $Case.Expected '' $actual))"
        }
        if ($left.Length -gt 1) { return "failed: the child runner left $($left.Length) entries, not one session directory" }
        $session = $left[0]
        $problem = Test-OrdinaryTree $session
        if ($problem.Length -gt 0) { return "failed: $problem" }
        foreach ($marker in $nested.Markers) {
            $markerPath = Resolve-KeptPath $session $marker
            if ($markerPath.Length -eq 0 -or -not [IO.File]::Exists($markerPath)) { return "failed: the kept session has lost $marker, so a cleanup began" }
        }
        $expected = @($Case.Expected | ForEach-Object { $_.Replace('{session}', $session) })
        if ($nested.ContainsKey('Kept')) {
            $original = Get-PermissionText ([IO.Path]::Combine([string[]](@($session) + $nested.Kept.Split('/'))))
            $expected = @($expected | ForEach-Object { $_.Replace('{original}', $original) })
        }
        $problem = Compare-Outcome $Case.Exit $expected '' $actual
        if ($problem.Length -gt 0) { return "failed: $problem" }
        Remove-Tree $session
    }
    elseif ($left.Length -gt 0) { return 'failed: the child runner did not remove its session directory' }
    else {
        $problem = Compare-Outcome $Case.Exit $Case.Expected '' $actual
        if ($problem.Length -gt 0) { return "failed: $problem" }
    }
    if ([IO.Directory]::GetFileSystemEntries($childTemp).Length -gt 0) { return 'failed: the child temporary directory is not empty after the case' }
    [void]$KeptPaths.Remove($registration)
    return 'passed'
}

# Returns 'passed', 'skipped: <reason>' or 'failed: <reason>'.
function Invoke-Case {
    param([hashtable] $Case, [string] $CaseDirectory, [hashtable] $Capabilities)
    # A capability is '' when available, a skip reason, or a failure that begins 'failed: '.
    if ($Case.ContainsKey('Requires')) {
        foreach ($requirement in @($Case.Requires)) {
            $capability = $Capabilities[$requirement]
            if ($capability.StartsWith('failed: ', [StringComparison]::Ordinal)) { return $capability }
            if ($capability.Length -gt 0) { return "skipped: $capability" }
        }
    }
    [void][IO.Directory]::CreateDirectory($CaseDirectory)
    if ($Case.ContainsKey('Nested')) { return Invoke-NestedRunner $Case $CaseDirectory }
    $pathOperands = $Case.ContainsKey('Operands') ? [string[]]$Case.Operands : [string[]]@()
    $cultureName = $Case.ContainsKey('Culture') ? $Case.Culture : ''
    if ($Case.ContainsKey('RealTree')) {
        $targetRoot = $RepositoryRoot
        $watched = $RepositoryRoot
        $skipName = '.git'
        $runtimeFile = $Runtime
    }
    else {
        $context = New-CaseContext $Case $CaseDirectory
        if ($Case.ContainsKey('Arrange')) {
            try { & $Case.Arrange $context }
            catch { return "skipped: the platform object could not be created ($($_.Exception.GetBaseException().GetType().Name))" }
        }
        $targetRoot = $context.RepoRoot
        $watched = $CaseDirectory
        $skipName = ''
        $runtimeFile = $context.Runtime
    }
    $runs = $Case.ContainsKey('Repeat') ? 2 : 1
    $outputs = [Collections.Generic.List[string]]::new()
    for ($run = 0; $run -lt $runs; $run++) {
        $before = Get-TreeState $watched $skipName
        $held = $Case.ContainsKey('Hold') ? [IO.File]::Open([IO.Path]::Combine($targetRoot, $Case.Hold), 'Open', 'Read', 'None') : $null
        $denial = $null
        $failure = ''
        try {
            if ($Case.ContainsKey('Deny')) {
                $denial = New-ListingDenial ([IO.Path]::Combine($targetRoot, $Case.Deny)) 'case'
                $failure = Set-ListingDenied $denial
            }
            if ($failure.Length -eq 0) {
                if ($Case.ContainsKey('Runner')) { $actual = Invoke-Child (@('-File', $PSCommandPath) + $Case.Runner) }
                else { $actual = Invoke-Runtime $runtimeFile $targetRoot $pathOperands $cultureName }
            }
        }
        finally {
            if ($null -ne $held) { $held.Dispose() }
            if ($null -ne $denial -and $denial.State.Equals('denied')) { Restore-Listing $denial }
        }
        if ($null -ne $denial -and -not (Test-ListingSettled $denial)) { return 'failed: runner infrastructure failure: the permissions of the denied directory are uncertain' }
        if ($failure.Length -gt 0) { return "skipped: the directory listing could not be denied ($failure)" }
        $after = Get-TreeState $watched $skipName
        if (-not $before.Equals($after)) { return 'failed: the run changed or created a path under the case directory' }
        $difference = Compare-Outcome $Case.Exit $Case.Expected '' $actual
        if ($difference.Length -gt 0) { return "failed: $difference" }
        $outputs.Add($actual.Stdout)
    }
    if ($runs -eq 2 -and -not $outputs[0].Equals($outputs[1])) { return 'failed: two runs over the same fixture wrote different bytes' }
    return 'passed'
}

# A comparator that accepted everything would pass every case, so it must first reject expectations
# known to be wrong against a real output.
function Test-Comparator {
    $context = New-CaseContext @{} ([IO.Path]::Combine($SessionRoot, 'self-check'))
    $actual = Invoke-Runtime $Runtime $context.RepoRoot @() ''
    $right = @($Scope, (Get-Summary 4 4 0 0 0))
    $difference = Compare-Outcome 0 $right '' $actual
    if ($difference.Length -gt 0) { return "the known fixture did not produce its known output ($difference)" }
    $wrong = @(
        @{ Exit = 1; Lines = $right; Error = '' }
        @{ Exit = 0; Lines = @($Scope); Error = '' }
        @{ Exit = 0; Lines = $right + @('extra'); Error = '' }
        @{ Exit = 0; Lines = $right; Error = 'unexpected' }
    )
    foreach ($expectation in $wrong) {
        if ((Compare-Outcome $expectation.Exit $expectation.Lines $expectation.Error $actual).Length -eq 0) { return 'a wrong expectation was accepted' }
    }
    return ''
}

if ($FaultMode.Length -gt 0 -and -not $FaultModes.Contains($FaultMode)) {
    Write-RunnerLine "fault injection refused: '$FaultMode' is not a fault mode of this runner"
    Write-RunnerLine 'no case ran'
    exit 1
}
if ($MetaFaultMode.Length -gt 0 -and -not $MetaFaultModes.Contains($MetaFaultMode)) {
    Write-RunnerLine "meta fault injection refused: '$MetaFaultMode' is not a meta fault mode of this runner"
    Write-RunnerLine 'no case ran'
    exit 1
}
if ($FaultMode.Length -gt 0) { Write-RunnerLine "fault injection active: $FaultMode" }
if ($MetaFaultMode.Length -gt 0) { Write-RunnerLine "meta fault injection active: $MetaFaultMode" }

$exitCode = 1
try {
    [void][IO.Directory]::CreateDirectory($SessionRoot)
    $selfCheck = Test-Comparator
    if ($selfCheck.Length -gt 0) {
        Write-RunnerLine "comparator self-check failed: $selfCheck"
        $exitCode = 2
    }
    else {
        Write-RunnerLine 'comparator self-check: 4 wrong expectations rejected'
        $available = Get-Capability
        $chosenCases = @($Cases | Where-Object { $_.Name.Contains($Filter) })
        $passed = 0; $failed = 0; $skipped = 0
        $halted = -not (Test-SessionSettled)
        if ($halted) { Write-RunnerLine 'runner infrastructure failure: the session directory is no longer safe to remove, so no case runs' }
        for ($index = 0; -not $halted -and $index -lt $chosenCases.Count; $index++) {
            $chosen = $chosenCases[$index]
            try { $result = Invoke-Case $chosen ([IO.Path]::Combine($SessionRoot, 'c' + $index.ToString('D3'))) $available }
            catch { $result = "failed: unexpected $($_.Exception.GetBaseException().GetType().Name) in the runner" }
            if ($result.Equals('passed')) { $passed++; Write-RunnerLine "passed  $($chosen.Name)" }
            elseif ($result.StartsWith('skipped', [StringComparison]::Ordinal)) { $skipped++; Write-RunnerLine "skipped $($chosen.Name): $($result.Substring(9))" }
            else { $failed++; Write-RunnerLine "failed  $($chosen.Name): $($result.Substring(8))" }
            if (-not (Test-SessionSettled)) {
                $halted = $true
                Write-RunnerLine 'runner infrastructure failure: the session directory is no longer safe to remove, so no further case runs'
            }
        }
        $executed = $passed + $failed
        Write-RunnerLine "cases=$($chosenCases.Count) executed=$executed passed=$passed failed=$failed skipped=$skipped"
        if ($executed -eq 0) { Write-RunnerLine 'no case ran' }
        $exitCode = ($executed -gt 0 -and $failed -eq 0 -and -not $halted) ? 0 : 1
    }
}
finally {
    # A recursive removal would reach every path below the session directory, so it runs only when
    # nothing there is uncertain; otherwise the directory stays, and is reported, for a person. A
    # removal that fails is not retried: the run fails, and what remains is reported and kept.
    if (Test-SessionSettled) {
        $cleanupFailure = ''
        try {
            if ($MetaFaultMode.Equals('final-cleanup-throws')) { throw 'injected fault' }
            Remove-Tree $SessionRoot
            if ([IO.Path]::Exists($SessionRoot)) { $cleanupFailure = 'the session directory still exists after its removal' }
        }
        catch { $cleanupFailure = "removing the session directory failed ($($_.Exception.GetBaseException().GetType().Name))" }
        if ($cleanupFailure.Length -gt 0) {
            if ($exitCode -eq 0) { $exitCode = 1 }
            Write-RunnerLine "cleanup failed: $cleanupFailure, and what remains of it is kept for recovery: $SessionRoot"
        }
    }
    else {
        if ($exitCode -eq 0) { $exitCode = 1 }
        Write-RunnerLine "cleanup withheld: the session directory is kept for recovery: $SessionRoot"
        foreach ($record in $ListingDenials) {
            if (-not (Test-ListingSettled $record)) { Write-RunnerLine "permissions uncertain: $($record.Directory): $($record.Reason); recorded original: $($record.Original)" }
        }
        foreach ($kept in $KeptPaths) { Write-RunnerLine "kept unverified: $($kept.Directory): $($kept.Reason)" }
    }
}
exit $exitCode
