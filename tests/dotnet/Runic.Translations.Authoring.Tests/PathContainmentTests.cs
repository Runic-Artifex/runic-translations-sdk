using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Runic.Translations.Compiler;
using TUnit.Core;

namespace Runic.Translations.Authoring.Tests;

// WIN-04: containment checks once tested Path.GetRelativePath for a "../" prefix, which Windows returns as "..\".
// CI runs this class and Rmf2AuthoringTests on Windows too (the Localized formatting / Windows job).
internal sealed class PathContainmentTests
{
    private static readonly string[] SourceDirectories = ["apps", "eng", "examples", "packages", "tests", "tools"];
    private static readonly string[] SkippedDirectories = ["bin", "obj", "node_modules", ".git", "artifacts", ".cache", ".direnv"];
    private static TranslationSource Source(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));

    [Test, DisplayName("Relative paths leave their directory by either separator, a drive or a share, by Windows rules")]
    [Arguments(".", true)]
    [Arguments("de.rmf2", true)]
    [Arguments(@"shop\de.rmf2", true)]
    [Arguments("..notes", true)]
    [Arguments(@"..notes\de.rmf2", true)]
    [Arguments("..", false)]
    [Arguments("../shop/de.rmf2", false)]
    [Arguments(@"..\shop\de.rmf2", false)]
    [Arguments(@"..\..", false)]
    [Arguments(@"D:\translations\de.rmf2", false)]
    [Arguments("D:translations", false)]
    [Arguments(@"\\server\share\de.rmf2", false)]
    [Arguments(@"\translations", false)]
    public void WindowsRelativePaths(string relative, bool within) =>
        // The "..\" cases are what Windows returns for a sibling mount, which the former "../" prefix check took as within.
        Assert.Equal(within, PathContainment.IsWithinRelative(relative, windows: true), relative);

    [Test, DisplayName("Relative paths leave their directory by '..' and either separator, by Unix rules")]
    [Arguments("de.rmf2", true)]
    [Arguments("..notes/de.rmf2", true)]
    [Arguments("C:translations", true)]
    [Arguments("..", false)]
    [Arguments("../shop", false)]
    [Arguments(@"..\shop", false)]
    [Arguments("/translations", false)]
    public void UnixRelativePaths(string relative, bool within) =>
        Assert.Equal(within, PathContainment.IsWithinRelative(relative, windows: false), relative);

    [Test, DisplayName("IsWithin and IsBelow compare full paths with the platform's rules")]
    public void FullPaths()
    {
        string root = Path.Combine(Path.GetTempPath(), "runic-containment", "app");
        string sibling = Path.Combine(Path.GetTempPath(), "runic-containment", "app-shop", "de.rmf2");
        Assert.True(PathContainment.IsWithin(root, root), "A directory is within itself.");
        Assert.False(PathContainment.IsBelow(root, root), "A directory is not below itself.");
        Assert.True(PathContainment.IsBelow(root, Path.Combine(root, "shop", "de.rmf2")), "A file below the directory is outside it.");
        Assert.False(PathContainment.IsWithin(root, sibling), "A sibling sharing the directory's name as a prefix is within it.");
        Assert.False(PathContainment.IsWithin(root, Path.GetDirectoryName(root)!), "The parent directory is within the directory.");
        Assert.Equal("shop/de.rmf2", PathContainment.RelativeWithin(root, Path.Combine(root, "shop", "de.rmf2")));
        Assert.Equal<string?>(null, PathContainment.RelativeWithin(root, sibling));
        if (OperatingSystem.IsWindows())
        {
            Assert.True(PathContainment.IsWithin(@"C:\Work\App", @"c:\work\app\Shop\de.rmf2"), "Windows paths differing in case only are outside.");
            Assert.False(PathContainment.IsWithin(@"C:\Work\App", @"D:\Work\App\de.rmf2"), "A path on another drive is within the directory.");
            Assert.False(PathContainment.IsWithin(@"C:\Work\App", @"\\server\share\de.rmf2"), "A path on a share is within the directory.");
        }
        else if (OperatingSystem.IsLinux())
        {
            Assert.False(PathContainment.IsWithin("/work/app", "/work/App/de.rmf2"), "Linux paths differing in case are the same.");
        }
    }

    [Test, DisplayName("Sibling source roots attribute each file to its own mount and reject files outside every root")]
    public void SiblingSourceRoots()
    {
        // The second mount is "..\shop" relative to the first on Windows: the former check attributed its files to "base".
        const string config = """{"schemaVersion":1,"catalog":"app","code":{"namespace":"Example","className":"AppText"},"baseLocale":"en","sourceRoots":[{"path":"../base","namespace":["base"]},{"path":"../shop","namespace":["shop"]}]}""";
        var workspace = new Rmf2Workspace(Path.GetTempPath(), Source("app/runic.json", config), [
            Source("base/en.rmf2", "title = Base\n"),
            Source("shop/en.rmf2", "title = Shop\n"),
            Source("shop/cart/en.rmf2", "empty = Empty\n"),
        ]);
        string Logical(string path) =>
            string.Join('.', workspace.LogicalPath(path, workspace.Documents.Single(document => document.Source.Path == path).Nodes.Single()));
        Assert.Equal("base.title", Logical("base/en.rmf2"));
        Assert.Equal("shop.title", Logical("shop/en.rmf2"));
        Assert.Equal("shop.cart.empty", Logical("shop/cart/en.rmf2"));
        Assert.Equal("total", string.Join('.', workspace.LocalPath("shop/en.rmf2", ["shop", "total"])));
        Assert.Throws<TranslationAuthoringException>(() => workspace.LocalPath("shop/en.rmf2", ["base", "total"]), "outside the destination namespace");
        Rmf2ResourceNode node = workspace.Documents.Single(document => document.Source.Path == "base/en.rmf2").Nodes.Single();
        Assert.Throws<TranslationAuthoringException>(() => workspace.LogicalPath("other/en.rmf2", node), "outside every configured source root");

        // Direct MF2 resources select their mount the same way.
        var direct = new Rmf2Workspace(Path.GetTempPath(), Source("app/runic.json", config), [
            Source("base/en/title.mf2", "Base\n"),
            Source("shop/en/title.mf2", "Shop\n"),
        ]);
        Rmf2ResourceNode shop = direct.Documents.Single(document => document.Source.Path == "shop/en/title.mf2").Nodes.Single();
        Assert.Equal("shop.title", string.Join('.', direct.LogicalPath("shop/en/title.mf2", shop)));
        Assert.Equal("total", string.Join('.', direct.LocalPath("shop/en/title.mf2", ["shop", "total"])));
        Assert.Throws<TranslationAuthoringException>(() => direct.LogicalPath("other/en/title.mf2", shop), "outside every configured source root");
    }

    [Test, DisplayName("C# sources check path containment only through PathContainment")]
    public void NoRawParentPrefixChecks()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Runic.Translations.slnx")))
            root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("The repository root was not found.");
        // A StartsWith call whose argument begins with "..", such as "../", "..\\" or ".." + Path.DirectorySeparatorChar, on a
        // relative path misses a separator, a drive or a share on some platform.
        var parentPrefix = new Regex(@"StartsWith\(\s*""\.\.", RegexOptions.CultureInvariant);
        string[] offenders = SourceDirectories
            .Select(directory => Path.Combine(root, directory))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", new EnumerationOptions { RecurseSubdirectories = true }))
            .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(SkippedDirectories.Contains))
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, index)))
            .Where(entry => parentPrefix.IsMatch(entry.line))
            .Select(entry => $"{Path.GetRelativePath(root, entry.path).Replace('\\', '/')}:{entry.index + 1}")
            .ToArray();
        Assert.True(offenders.Length == 0,
            "Use PathContainment instead of a '..' prefix check on a relative path: " + string.Join(", ", offenders));
    }
}
