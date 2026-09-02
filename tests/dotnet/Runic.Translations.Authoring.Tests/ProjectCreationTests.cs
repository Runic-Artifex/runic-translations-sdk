using System;
using System.IO;
using System.Linq;
using System.Text;
using Runic.Translations.Authoring;

namespace Runic.Translations.Authoring.Tests;

internal static class ProjectCreationTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("German-only project is compiler-valid", GermanOnlyIsValid);
        runner.Add("Three-locale project canonicalizes tags and fallbacks", ThreeLocalesAreCanonical);
        runner.Add("Equivalent project requests render byte-identically", RenderingIsDeterministic);
        runner.Add("Unknown and cyclic fallbacks are rejected", InvalidFallbacksAreRejected);
        runner.Add("Creation commits the complete rendered project", CreationCommitsCompleteProject);
        runner.Add("Conflicting target remains unchanged", ConflictDoesNotWrite);
        runner.Add("Creation permits a real parent beneath an ancestor alias", AncestorAliasIsAllowed);
        runner.Add("Creation rejects a linked target parent", LinkedParentIsRejected);
        runner.Add("Project without starter messages remains compiler-valid", NoStarterIsValid);
        runner.Add("Project config carries its editor schema declaration", ProjectConfigDeclaresSchema);
    }

    private static void GermanOnlyIsValid()
    {
        TranslationProjectPlan plan = TranslationProjectScaffolder.Render(Request("unused", "de"));
        Assert.True(plan.Compilation.Success, "Generated project did not compile.");
        Assert.Equal(2, plan.Files.Count);
        Assert.Equal("de", plan.Locales[0].Tag);
        Assert.True(Utf8(plan, "de/application_title.mf2").Contains("ProductText", StringComparison.Ordinal), "Starter message is missing.");
    }

    private static void ThreeLocalesAreCanonical()
    {
        TranslationProjectPlan plan = TranslationProjectScaffolder.Render(new TranslationProjectCreationRequest(
            "unused",
            "product",
            "de-de",
            "Customer.Product",
            "ProductText",
            [new("en-us"), new("zh-hans-cn", "EN-us")]));

        Assert.True(plan.Compilation.Success, "Generated project did not compile.");
        Assert.Equal("de-DE|en-US:de-DE|zh-Hans-CN:en-US", string.Join('|', plan.Locales.Select(LocaleText)));
        Assert.Equal(
            "de-DE/application_title.mf2|en-US/application_title.mf2|runic.json|zh-Hans-CN/application_title.mf2",
            string.Join('|', plan.Files.Select(file => file.RelativePath)));
    }

    private static void RenderingIsDeterministic()
    {
        TranslationProjectPlan first = TranslationProjectScaffolder.Render(Request("first", "en"));
        TranslationProjectPlan second = TranslationProjectScaffolder.Render(Request("second", "EN"));
        Assert.Equal(first.Files.Count, second.Files.Count);
        for (int index = 0; index < first.Files.Count; index++)
        {
            Assert.Equal(first.Files[index].RelativePath, second.Files[index].RelativePath);
            Assert.True(
                first.Files[index].GetUtf8Bytes().AsSpan().SequenceEqual(second.Files[index].GetUtf8Bytes()),
                $"Rendered bytes differ for {first.Files[index].RelativePath}.");
        }
    }

    private static void InvalidFallbacksAreRejected()
    {
        Assert.Throws<TranslationAuthoringException>(
            () => TranslationProjectScaffolder.Render(new TranslationProjectCreationRequest(
                "unused", "product", "de", "Customer.Product", "ProductText", [new("fr", "es")])),
            "is not declared");
        Assert.Throws<TranslationAuthoringException>(
            () => TranslationProjectScaffolder.Render(new TranslationProjectCreationRequest(
                "unused", "product", "de", "Customer.Product", "ProductText", [new("en", "fr"), new("fr", "en")])),
            "Fallback cycle");
    }

    private static void CreationCommitsCompleteProject()
    {
        using TemporaryDirectory temporary = new();
        string target = Path.Combine(temporary.Path, "Resources");
        TranslationProjectPlan plan = TranslationProjectScaffolder.Render(Request(target, "en"));
        string result = TranslationProjectWriter.Create(plan);
        Assert.Equal(Path.GetFullPath(target), result);
        Assert.Equal(
            string.Join('|', plan.Files.Select(file => file.RelativePath)),
            string.Join('|', Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(target, file).Replace('\\', '/')).Order(StringComparer.Ordinal)));
        for (int index = 0; index < plan.Files.Count; index++)
        {
            Assert.True(
                plan.Files[index].GetUtf8Bytes().AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(target, plan.Files[index].RelativePath))),
                $"Committed bytes differ for {plan.Files[index].RelativePath}.");
        }
    }

    private static void ConflictDoesNotWrite()
    {
        using TemporaryDirectory temporary = new();
        string target = Path.Combine(temporary.Path, "Resources");
        Directory.CreateDirectory(target);
        string sentinel = Path.Combine(target, "customer.txt");
        File.WriteAllText(sentinel, "keep", new UTF8Encoding(false));
        TranslationProjectPlan plan = TranslationProjectScaffolder.Render(Request(target, "en"));
        Assert.Throws<TranslationAuthoringException>(() => TranslationProjectWriter.Create(plan), "already exists");
        Assert.Equal("keep", File.ReadAllText(sentinel, Encoding.UTF8));
        Assert.Equal("customer.txt", string.Join('|', Directory.EnumerateFiles(target).Select(Path.GetFileName)));
    }

    private static void AncestorAliasIsAllowed()
    {
        using TemporaryDirectory temporary = new();
        string real = Path.Combine(temporary.Path, "real");
        string alias = Path.Combine(temporary.Path, "alias");
        Directory.CreateDirectory(Path.Combine(real, "projects"));
        if (!TryCreateDirectoryLink(alias, real)) return;

        string target = Path.Combine(alias, "projects", "Resources");
        string result = TranslationProjectWriter.Create(TranslationProjectScaffolder.Render(Request(target, "en")));
        Assert.Equal(Path.GetFullPath(target), result);
        Assert.True(File.Exists(Path.Combine(real, "projects", "Resources", "en", "application_title.mf2")), "Project was not created beneath the resolved ancestor.");
    }

    private static void LinkedParentIsRejected()
    {
        using TemporaryDirectory temporary = new();
        string real = Path.Combine(temporary.Path, "real");
        string alias = Path.Combine(temporary.Path, "alias");
        Directory.CreateDirectory(real);
        if (!TryCreateDirectoryLink(alias, real)) return;

        string target = Path.Combine(alias, "Resources");
        Assert.Throws<TranslationAuthoringException>(
            () => TranslationProjectWriter.Create(TranslationProjectScaffolder.Render(Request(target, "en"))),
            "symbolic link or reparse point");
        Assert.False(Directory.Exists(Path.Combine(real, "Resources")), "Rejected creation wrote through the linked parent.");
    }

    private static void NoStarterIsValid()
    {
        TranslationProjectPlan plan = TranslationProjectScaffolder.Render(new TranslationProjectCreationRequest(
            "unused",
            "product",
            "de",
            "Customer.Product",
            "ProductText",
            includeStarterMessage: false));
        Assert.True(plan.Compilation.Success, "Empty MF2 project did not compile.");
        Assert.Equal("runic.json", string.Join('|', plan.Files.Select(file => file.RelativePath)));
    }

    private static void ProjectConfigDeclaresSchema()
    {
        TranslationProjectPlan plan = TranslationProjectScaffolder.Render(new TranslationProjectCreationRequest(
            "unused",
            "product",
            "en",
            "Customer.Product",
            "ProductText"));
        string config = Utf8(plan, "runic.json");
        Assert.True(config.Contains("project-v1.schema.json", StringComparison.Ordinal), "Project schema declaration is missing.");
    }

    private static TranslationProjectCreationRequest Request(string directory, string locale) => new(
        directory,
        "product",
        locale,
        "Customer.Product",
        "ProductText");

    private static string Utf8(TranslationProjectPlan plan, string name) => Encoding.UTF8.GetString(
        plan.Files.Single(file => file.RelativePath == name).GetUtf8Bytes());

    private static string LocaleText(TranslationProjectLocale locale) =>
        locale.Fallback is null ? locale.Tag : $"{locale.Tag}:{locale.Fallback}";

    private static bool TryCreateDirectoryLink(string path, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(path, target);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"runic-authoring-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
