using System.Security.AccessControl;
using System.Security.Principal;

namespace FcaBedrock.Conversion.Tests;

// Confidentiality + ownership of the spool workspace (D-082): the spool holds raw source data, so a
// workspace is created fresh, uniquely named, owner-restricted, and its run files are unshared. The
// platform-specific ACL assertions run on their supported OS at runtime (never a permanent skip).
public sealed class SpoolConfidentialityTests
{
    [Fact]
    public void CreateWorkspace_ProducesFreshDisjointDirectoriesUnderRoot()
    {
        var root = FreshRoot();
        try
        {
            var a = SpoolFileSystem.Default.CreateWorkspace(root);
            var b = SpoolFileSystem.Default.CreateWorkspace(root);

            Assert.True(Directory.Exists(a));
            Assert.True(Directory.Exists(b));
            Assert.NotEqual(a, b); // concurrent enumerations get disjoint workspaces
            Assert.StartsWith(root, a, StringComparison.Ordinal);
            Assert.StartsWith(root, b, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CreateRunForWrite_LocksTheFileWithNoSharing()
    {
        var root = FreshRoot();
        try
        {
            var workspace = SpoolFileSystem.Default.CreateWorkspace(root);
            var path = Path.Combine(workspace, "run.spool");
            using var stream = SpoolFileSystem.Default.CreateRunForWrite(path);

            // FileShare.None: a second open — even read-only — is a sharing violation.
            Assert.Throws<IOException>(() => File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CreateWorkspace_OnUnix_SetsMode700()
    {
        Assert.SkipUnless(!OperatingSystem.IsWindows(), "Unix mode bits — asserted on Unix.");
        if (!OperatingSystem.IsWindows())
        {
            var root = FreshRoot();
            try
            {
                var workspace = SpoolFileSystem.Default.CreateWorkspace(root);
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                    File.GetUnixFileMode(workspace));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateWorkspace_OnWindows_SetsOwnerOnlyProtectedDacl()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows DACL — asserted on Windows.");
        if (OperatingSystem.IsWindows())
        {
            var root = FreshRoot();
            try
            {
                var workspace = SpoolFileSystem.Default.CreateWorkspace(root);
                var security = new DirectoryInfo(workspace).GetAccessControl();

                Assert.True(security.AreAccessRulesProtected); // inheritance disabled
                var owner = WindowsIdentity.GetCurrent().User;
                Assert.Equal(owner, security.GetOwner(typeof(SecurityIdentifier)));

                var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));
                Assert.NotEmpty(rules);
                foreach (FileSystemAccessRule rule in rules)
                {
                    Assert.Equal(owner, rule.IdentityReference); // only the owner has access
                }
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string FreshRoot() =>
        Path.Combine(Path.GetTempPath(), "fcab-conf-" + Guid.NewGuid().ToString("N"));
}
