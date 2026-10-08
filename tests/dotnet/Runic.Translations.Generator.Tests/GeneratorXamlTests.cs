using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Runic.Translations.Generator.Tests;

internal static class GeneratorXamlTests
{
    private const string Project = """
        { "schemaVersion": 1, "catalog": "app", "code": { "namespace": "Example", "className": "AppText" }, "baseLocale": "en" }
        """;
    private const string English = """
        application {
          title = App
        }
        greeting = Hello {$name}
        pair = {$first} and {$second}
        badge = {$user-name}
        help = Read {#link ref=guide}this{/link}.
        notice =
          {#p}Document{/p}
        """;

    internal static void Register(TestRunner runner)
    {
        runner.Add("XAML accepts positional/named bindings, aliases, namespace scopes and nested markup", ValidDeclarations);
        runner.Add("XAML reports unknown readable keys and exact source locations", UnknownKeys);
        runner.Add("XAML checks named input identifiers, arity, duplicates, gaps and mixed forms", InvalidInputs);
        runner.Add("XAML checks plain, inline rich and document content kinds", MessageKinds);
        runner.Add("XAML distinguishes binding data sources from Message and attached catalog sources", SourceScope);
        runner.Add("XAML preserves dynamic keys and external/inherited sources without catalog assumptions", UnresolvedDeclarations);
        runner.Add("XAML validates only the Runic namespace and explicitly marked files", NamespaceAndOptIn);
        runner.Add("XAML reports malformed XML and declarations without crashing or resolving entities", InvalidDeclarations);
    }

    private static Diagnostic[] Run(string content, string? catalog = "app", string? defaultCatalog = null, bool raw = false)
    {
        string xaml = raw ? content : """
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:rt="clr-namespace:Runic.Translations.Wpf;assembly=Runic.Translations.Wpf"
                    xmlns:other="clr-namespace:Example">
            """ + "\n" + content + "\n</Window>";
        GeneratorRun run = GeneratorTestHost.Run(new TestInput("C:/repo/translations/runic.json", "Project", Project),
            new TestInput("C:/repo/translations/en.rmf2", "Rmf2", English), new TestInput("C:/repo/Views/Main.xaml", "Xaml", xaml, catalog, defaultCatalog));
        Assert.Equal(5, run.SingleResult.GeneratedSources.Length, string.Join("\n", run.SingleResult.Diagnostics));
        return run.SingleResult.Diagnostics.Where(static diagnostic => diagnostic.Id.StartsWith("RTR008", StringComparison.Ordinal)).ToArray();
    }

    private static void ValidDeclarations()
    {
        Diagnostic[] diagnostics = Run("""
            <TextBlock Text="{rt:Message application_title}"/>
            <TextBlock Text="{rt:Message application_title, Arg0={x:Null}}"/>
            <rt:Message Key="application_title"><rt:Message.Arg0><x:Null/></rt:Message.Arg0></rt:Message>
            <TextBlock Text="{rt:MessageExtension Key='greeting', Arg0={Binding Name, FallbackValue='Doe, Ada'}}"/>
            <TextBlock Text="{rt:Message pair, Arg0={Binding One}, Arg1={Binding Two, ConverterParameter={other:Extension A,B}}}"/>
            <TextBlock rt:TranslationProperties.RichMessage="help"/>
            <TextBlock Text="{Binding Name, ConverterParameter={rt:Message greeting, Arg0={Binding Name}}}"/>
            <TextBlock><TextBlock.Text><rt:Message Key="pair">
              <rt:MessageInput Name="second" Value="{Binding Two}"/>
              <rt:MessageInput Name="first"><rt:MessageInput.Value><Binding Path="One"/></rt:MessageInput.Value></rt:MessageInput>
            </rt:Message></TextBlock.Text></TextBlock>
            <TextBlock><TextBlock.Text><rt:Message Key="badge"><rt:Message.Inputs><rt:MessageInput Name="r_757365722d6e616d65" Value="{Binding Name}"/></rt:Message.Inputs></rt:Message></TextBlock.Text></TextBlock>
            <TextBlock xmlns:t="clr-namespace:Runic.Translations.Wpf;assembly=Runic.Translations.Wpf" Text="{t:Message application_title}"/>
            <rt:Message><rt:Message.Key><x:String>greeting</x:String></rt:Message.Key><rt:Message.Arg0><Binding Path="Name"/></rt:Message.Arg0></rt:Message>
            <rt:MessageExtension Key="greeting"><rt:Message.Arg0><Binding Path="Name"/></rt:Message.Arg0></rt:MessageExtension>
            """);
        Assert.Equal(0, diagnostics.Length, string.Join("\n", diagnostics.Select(static diagnostic => diagnostic.ToString())));
    }

    private static void UnknownKeys()
    {
        Diagnostic diagnostic = Run("<TextBlock Text=\"{rt:Message applicaton_title}\"/>").Single();
        Assert.Equal("RTR0081", diagnostic.Id, diagnostic.ToString());
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity, "severity");
        Assert.Equal("Views/Main.xaml", diagnostic.Location.GetLineSpan().Path, "path");
        Assert.Equal(4, diagnostic.Location.GetLineSpan().StartLinePosition.Line, "line");
        Assert.Equal(11, diagnostic.Location.GetLineSpan().StartLinePosition.Character, "attribute column");
        Assert.Equal("RTR0081", Run("<TextBlock Text=\"{rt:Message application.title}\"/>").Single().Id, "flattened name");
    }

    private static void InvalidInputs()
    {
        foreach (string content in new[]
        {
            "<TextBlock Text=\"{rt:Message greeting}\"/>",
            "<TextBlock Text=\"{rt:Message greeting, Arg1={Binding Name}}\"/>",
            "<TextBlock Text=\"{rt:Message greeting, Arg0={Binding Name}, Arg1={Binding Other}}\"/>",
            "<rt:Message Key=\"greeting\"><rt:MessageInput Name=\"typo\" Value=\"{Binding Name}\"/></rt:Message>",
            "<rt:Message Key=\"greeting\" Arg0=\"{Binding Name}\"><rt:MessageInput Name=\"name\" Value=\"{Binding Name}\"/></rt:Message>",
            "<rt:Message Key=\"pair\"><rt:MessageInput Name=\"first\" Value=\"{Binding One}\"/><rt:MessageInput Name=\"first\" Value=\"{Binding Two}\"/></rt:Message>",
            "<rt:Message Key=\"greeting\"><rt:MessageInput Name=\"name\"/></rt:Message>",
            "<rt:Message Key=\"greeting\"><rt:MessageInput Name=\"name\" Value=\"{x:Null}\"/></rt:Message>",
            "<rt:Message Key=\"badge\"><rt:MessageInput Name=\"user-name\" Value=\"{Binding Name}\"/></rt:Message>"
        }) Assert.Equal("RTR0082", Run(content).Single().Id, content);
        Assert.Equal("RTR0082", Run("<TextBlock Text=\"{rt:Message external, Arg2={Binding Name}}\"/>", catalog: null).Single().Id, "provable gap without catalog");
    }

    private static void MessageKinds()
    {
        foreach (string content in new[] { "<TextBlock Text=\"{rt:Message help}\"/>", "<TextBlock Text=\"{rt:Message notice}\"/>",
            "<TextBlock rt:TranslationProperties.RichMessage=\"application_title\"/>", "<TextBlock rt:TranslationProperties.RichMessage=\"notice\"/>" })
            Assert.Equal("RTR0083", Run(content).Single().Id, content);
    }

    private static void UnresolvedDeclarations()
    {
        Assert.Equal(0, Run("<TextBlock Text=\"{rt:Message outside}\"/>", catalog: null).Length, "unknown catalog");
        Assert.Equal(0, Run("<TextBlock Text=\"{rt:Message outside, Source={StaticResource External}}\"/>", catalog: null, defaultCatalog: "app").Length, "external resource source");
        Assert.Equal(0, Run("<TextBlock Text=\"{rt:Message outside, Source = {Binding Catalog}}\"/>", catalog: null, defaultCatalog: "app").Length, "dynamic source");
        Assert.Equal(0, Run("<StackPanel rt:TranslationProperties.Source=\"{StaticResource External}\"><TextBlock Text=\"{rt:Message outside}\"/></StackPanel>", catalog: null, defaultCatalog: "app").Length, "inherited source");
        Assert.Equal(0, Run("<rt:Message Key=\"{x:Static other:Keys.Greeting}\"/>").Length, "static C# key remains unresolved");
        Assert.Equal(0, Run("<rt:Message Key=\"greeting\"><rt:MessageInput Value=\"{Binding Name}\"><rt:MessageInput.Name><x:Static Member=\"other:Keys.Name\"/></rt:MessageInput.Name></rt:MessageInput></rt:Message>").Length, "runtime-valued input name");
        Assert.Equal(0, Run("<TextBlock rt:TranslationProperties.RichMessage=\"{Binding Key}\"/>").Length, "bound rich key");
        Assert.Equal("RTR0081", Run("<TextBlock Text=\"{rt:Message typo}\"/>", catalog: null, defaultCatalog: "app").Single().Id, "known default catalog");
        Assert.Equal("RTR0081", Run("<TextBlock Text=\"{rt:Message typo, Source={StaticResource App}}\"/>").Single().Id, "file assertion covers explicit sources");
        Assert.Equal("RTR0080", Run("<TextBlock Text=\"{rt:Message greeting}\"/>", catalog: "external").Single().Id, "incorrect local catalog assertion");
    }

    private static void SourceScope()
    {
        foreach (string content in new[]
        {
            """<TextBlock Text="{rt:Message typo, Arg0={Binding Source={StaticResource User}, Path=Name}}"/>""",
            """<TextBlock Text="{rt:Message typo, Arg0={Binding Name, FallbackValue='Source=literal'}}"/>""",
            """<TextBlock Text="{rt:Message typo}" Tag="{Binding Source = {StaticResource User}}"/>""",
            """<TextBlock Text="{rt:Message typo}"/><Binding Source="{StaticResource User}" Path="Name"/>"""
        }) Assert.Equal("RTR0081", Run(content, catalog: null, defaultCatalog: "app").Single().Id, content);

        foreach (string property in new[]
        {
            "rt:TranslationProperties.Source",
            "{x:Static rt:TranslationProperties.SourceProperty}",
            "{x:Static Member = 'rt:TranslationProperties.SourceProperty'}",
            "{ x:StaticExtension Member = 'rt:TranslationProperties.SourceProperty' }"
        })
        {
            string content = "<Style><Setter Property=\"" + property + "\" Value=\"{StaticResource External}\"/></Style><TextBlock Text=\"{rt:Message outside}\"/>";
            Assert.Equal(0, Run(content, catalog: null, defaultCatalog: "app").Length, content);
            Assert.Equal("RTR0081", Run(content).Single().Id, "per-file assertion still checks " + property);
        }
        Assert.Equal(0, Run("""
            <Style><Setter Value="{StaticResource External}"><Setter.Property><x:Static Member="rt:TranslationProperties.SourceProperty"/></Setter.Property></Setter></Style>
            <TextBlock Text="{rt:Message outside}"/>
            """, catalog: null, defaultCatalog: "app").Length, "object x:Static source setter");
        Assert.Equal(0, Run("""
            <TextBlock xmlns:y="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:t="clr-namespace:Runic.Translations.Wpf;assembly=Runic.Translations.Wpf">
              <TextBlock.Style><Style><Setter Property="{y:Static Member='t:TranslationProperties.SourceProperty'}" Value="{StaticResource External}"/></Style></TextBlock.Style>
              <TextBlock.Text><rt:Message Key="outside"/></TextBlock.Text>
            </TextBlock>
            """, catalog: null, defaultCatalog: "app").Length, "namespace aliases on x:Static source setter");
        Assert.Equal(0, Run("""
            <p:Style xmlns:p="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns="http://schemas.microsoft.com/winfx/2006/xaml">
              <p:Setter Property="{Static rt:TranslationProperties.SourceProperty}" Value="{p:StaticResource External}"/>
            </p:Style><TextBlock Text="{rt:Message outside}"/>
            """, catalog: null, defaultCatalog: "app").Length, "default XAML namespace on static source setter");
        Assert.Equal(0, Run("""<TextBlock Text="{ rt:Message outside, Source = '{StaticResource External}' }"/>""", catalog: null, defaultCatalog: "app").Length, "quoted and spaced Message.Source");
        Assert.Equal("RTR0081", Run("""<Setter Property="{x:Static other:Properties.BackgroundProperty}"/><TextBlock Text="{rt:Message typo}"/>""", catalog: null, defaultCatalog: "app").Single().Id, "unrelated dependency property");
    }

    private static void NamespaceAndOptIn()
    {
        Assert.Equal(0, Run("<TextBlock Text=\"{other:Message typo}\"/><TextBlock Text=\"{}{rt:Message typo}\"/><TextBlock xmlns:rt=\"clr-namespace:Other;assembly=Other\" Text=\"{rt:Message typo}\"/>").Length, "namespace/escaped literals");
        GeneratorRun run = GeneratorTestHost.Run(new TestInput("C:/repo/translations/runic.json", "Project", Project),
            new TestInput("C:/repo/translations/en.rmf2", "Rmf2", English), new TestInput("C:/repo/unmarked.xaml", "None", "not XML", "app"));
        Assert.Equal(0, run.SingleResult.Diagnostics.Length, "unmarked input ignored");
    }

    private static void InvalidDeclarations()
    {
        foreach (string content in new[] { "<rt:Message/>", "<TextBlock Text=\"{rt:Message greeting, Key=application_title}\"/>",
            "<TextBlock Text=\"{rt:Message greeting, Arg0={Binding Name}\"/>", "<rt:Message Key=\"\"/>",
            "<rt:Message Key=\"greeting\"><rt:Message.Key>application_title</rt:Message.Key></rt:Message>" })
            Assert.Equal("RTR0080", Run(content).Single().Id, content);
        Assert.Equal("RTR0080", Run("<Window>", raw: true).Single().Id, "malformed XML");
        Assert.Equal("RTR0080", Run("<!DOCTYPE Window [<!ENTITY injected SYSTEM 'file:///etc/passwd'>]><Window>&injected;</Window>", raw: true).Single().Id, "DTD forbidden");
    }
}
