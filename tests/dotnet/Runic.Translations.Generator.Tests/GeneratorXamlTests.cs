using System;
using System.Globalization;
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
        heading = Editing {$name} ({$email})
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
        runner.Add("XAML warns when several inputs bind by position and names the positional order", PositionalInputs);
        runner.Add("XAML input diagnostics name the problem and the expected inputs", InputMessages);
        runner.Add("XAML suggests the closest readable key for an unknown key", KeySuggestions);
        runner.Add("XAML checks plain, inline rich and document content kinds", MessageKinds);
        runner.Add("XAML narrows explicit sources to their scope and reports what is not checked", SourceNarrowing);
        runner.Add("XAML distinguishes binding data sources from Message and attached catalog sources", SourceScope);
        runner.Add("XAML preserves dynamic keys and external/inherited sources without catalog assumptions", UnresolvedDeclarations);
        runner.Add("XAML validates only the Runic namespace and explicitly marked files", NamespaceAndOptIn);
        runner.Add("XAML reports malformed XML and declarations without crashing or resolving entities", InvalidDeclarations);
        runner.Add("XAML skips mc:Ignorable design-time content and mc:AlternateContent", DesignTimeContent);
        runner.Add("XAML is not checked without the readable surface; only RTR0068 is reported", WithoutReadableSurface);
    }

    private static void DesignTimeContent()
    {
        const string Compatibility = """xmlns:d="http://schemas.microsoft.com/expression/blend/2008" xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" mc:Ignorable="d" """;
        Assert.Equal(0, Run("<StackPanel " + Compatibility + """
            d:Tag="{rt:Message typo}" d:DataContext="{d:DesignInstance other:Model}">
              <TextBlock d:Text="{rt:Message typo}" Text="{rt:Message application_title}"/>
              <d:Designer><TextBlock Text="{rt:Message typo}"/><rt:Message Key="typo"/></d:Designer>
              <rt:Message Key="greeting" Arg0="{Binding Name}"><d:Note>design</d:Note></rt:Message>
              <mc:AlternateContent>
                <mc:Choice Requires="d"><TextBlock Text="{rt:Message typo}"/></mc:Choice>
                <mc:Fallback><TextBlock Text="{rt:Message typo}"/></mc:Fallback>
              </mc:AlternateContent>
            </StackPanel>
            """, catalog: null, defaultCatalog: "app").Length, "design-time content");
        Assert.Equal("RTR0081", Run("<StackPanel " + Compatibility + """
            d:Tag="{rt:Message outside, Source={StaticResource External}}"><TextBlock Text="{rt:Message typo}"/></StackPanel>
            """, catalog: null, defaultCatalog: "app").Single().Id, "a design-time source does not disable default-catalog checks");
        Assert.Equal("RTR0081", Run("""
            <TextBlock xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" mc:Ignorable="rt" Text="{rt:Message typo}"/>
            """).Single().Id, "the Runic namespace is understood and stays checked");
        Assert.Equal("RTR0081", Run("""<TextBlock xmlns:o="urn:other" o:Tag="{rt:Message typo}"/>""").Single().Id, "non-ignorable attached namespaces stay checked");
    }

    private static void WithoutReadableSurface()
    {
        GeneratorRun run = GeneratorTestHost.Run(RuntimeReferenceMode.Rmf2V3, new TestInput("C:/repo/translations/runic.json", "Project", Project),
            new TestInput("C:/repo/translations/en.rmf2", "Rmf2", English),
            new TestInput("C:/repo/Views/Main.xaml", "Xaml", """
                <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:rt="clr-namespace:Runic.Translations.Wpf;assembly=Runic.Translations.Wpf">
                  <TextBlock Text="{rt:Message application_title}"/><TextBlock Text="{rt:Message greeting, Arg0={Binding Name}}"/>
                </Window>
                """, "app"));
        Diagnostic diagnostic = run.SingleResult.Diagnostics.Single();
        Assert.Equal("RTR0068", diagnostic.Id, diagnostic.ToString());
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
            <TextBlock Text="{rt:Message greeting, Arg0={Binding Two, ConverterParameter={other:Extension A,B}}}"/>
            <TextBlock rt:TranslationProperties.RichMessage="help"/>
            <TextBlock Text="{Binding Name, ConverterParameter={rt:Message greeting, Arg0={Binding Name}}}"/>
            <TextBlock><TextBlock.Text><rt:Message Key="pair">
              <rt:MessageInput Name="second" Value="{Binding Two}"/>
              <rt:MessageInput Name="first"><rt:MessageInput.Value><Binding Path="One"/></rt:MessageInput.Value></rt:MessageInput>
            </rt:Message></TextBlock.Text></TextBlock>
            <TextBlock><TextBlock.Text><rt:Message Key="badge"><rt:Message.Inputs><rt:MessageInput Name="r_757365722d6e616d65" Value="{Binding Name}"/></rt:Message.Inputs></rt:Message></TextBlock.Text></TextBlock>
            <TextBlock xmlns:t="clr-namespace:Runic.Translations.Wpf;assembly=Runic.Translations.Wpf" Text="{t:Message application_title}"/>
            <TextBlock xmlns:u="https://runic-artifex.eu/xaml/translations" Text="{u:Message application_title}"/>
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

    // An explicit source hides the default-catalog check of its scope and says so once, as information.
    private static void AssertSkipped(Diagnostic[] diagnostics, string message)
    {
        Assert.Equal(1, diagnostics.Length, message + ": " + string.Join("\n", diagnostics.AsEnumerable()));
        Diagnostic diagnostic = diagnostics[0];
        Assert.Equal("RTR0085", diagnostic.Id, message + ": " + diagnostic);
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity, message);
    }

    private static void PositionalInputs()
    {
        // heading = Editing {$name} ({$email}): the generated parameters are (email, name).
        Diagnostic swapped = Run("<TextBlock Text=\"{rt:Message heading, Arg0={Binding Name}, Arg1={Binding Email}}\"/>").Single();
        Assert.Equal("RTR0084", swapped.Id, swapped.ToString());
        Assert.Equal(DiagnosticSeverity.Warning, swapped.Severity, "positional severity");
        Assert.True(swapped.GetMessage(CultureInfo.InvariantCulture).Contains("Arg0=email, Arg1=name", StringComparison.Ordinal), swapped.GetMessage(CultureInfo.InvariantCulture));
        Assert.True(swapped.GetMessage(CultureInfo.InvariantCulture).Contains("MessageInput", StringComparison.Ordinal), swapped.GetMessage(CultureInfo.InvariantCulture));
        Assert.Equal("RTR0084", Run("<rt:Message Key=\"pair\" Arg0=\"{Binding One}\" Arg1=\"{Binding Two}\"/>").Single().Id, "object form");
        Assert.Equal(0, Run("""
            <TextBlock><TextBlock.Text><rt:Message Key="heading">
              <rt:MessageInput Name="name" Value="{Binding Name}"/>
              <rt:MessageInput Name="email" Value="{Binding Email}"/>
            </rt:Message></TextBlock.Text></TextBlock>
            <TextBlock Text="{rt:Message greeting, Arg0={Binding Name}}"/>
            """).Length, "named form and a single positional input");
        Assert.Equal(0, Run("<TextBlock Text=\"{rt:Message pair, Arg0={Binding One}, Arg1={Binding Two}}\"/>", catalog: null).Length, "order unknown without a catalog");
    }

    private static void InputMessages()
    {
        (string Content, string Expected)[] cases =
        [
            ("<TextBlock Text=\"{rt:Message greeting}\"/>", "Message 'greeting' takes 1 input: Arg0=name, but no Arg is set."),
            ("<TextBlock Text=\"{rt:Message application_title, Arg0={Binding Name}}\"/>", "Message 'application_title' takes no inputs, but Arg0 is set."),
            ("<TextBlock Text=\"{rt:Message heading, Arg0={Binding Name}}\"/>", "Message 'heading' takes 2 inputs: Arg0=email, Arg1=name, but Arg0 is set."),
            ("<TextBlock Text=\"{rt:Message greeting, Arg0={Binding Name}, Arg1={Binding Other}}\"/>", "takes 1 input: Arg0=name, but Arg0..Arg1 are set."),
            ("<rt:Message Key=\"greeting\"><rt:MessageInput Name=\"nmae\" Value=\"{Binding Name}\"/></rt:Message>", "Message 'greeting' has no input 'nmae' and is missing 'name'; it takes 1 input: name."),
            ("<rt:Message Key=\"pair\"><rt:MessageInput Name=\"first\" Value=\"{Binding One}\"/></rt:Message>", "Message 'pair' is missing 'second'; it takes 2 inputs: first, second."),
            ("<rt:Message Key=\"badge\"><rt:MessageInput Name=\"user-name\" Value=\"{Binding Name}\"/></rt:Message>", "has no input 'user-name' (use its readable name 'r_757365722d6e616d65')"),
            ("<TextBlock Text=\"{rt:Message pair, Arg1={Binding Two}}\"/>", "Positional inputs must start at Arg0 without gaps; Arg0 is not set."),
            ("<rt:Message Key=\"greeting\" Arg0=\"{Binding Name}\"><rt:MessageInput Name=\"name\" Value=\"{Binding Name}\"/></rt:Message>", "sets both Arg0..Arg3 and MessageInput entries"),
            ("<rt:Message Key=\"pair\"><rt:MessageInput Name=\"first\" Value=\"{Binding One}\"/><rt:MessageInput Name=\"first\" Value=\"{Binding Two}\"/></rt:Message>", "MessageInput 'first' is set more than once."),
        ];
        foreach ((string content, string expected) in cases)
        {
            Diagnostic diagnostic = Run(content).Single();
            Assert.Equal("RTR0082", diagnostic.Id, content);
            Assert.True(diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains(expected, StringComparison.Ordinal), content + ": " + diagnostic.GetMessage(CultureInfo.InvariantCulture));
            Assert.True(!diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains(": .", StringComparison.Ordinal), diagnostic.GetMessage(CultureInfo.InvariantCulture));
        }
    }

    private static void KeySuggestions()
    {
        foreach ((string key, string suggestion) in new[] { ("applicaton_title", "application_title"), ("application.title", "application_title"),
            ("Application_Title", "application_title"), ("greting", "greeting"), ("pairs", "pair") })
        {
            Diagnostic diagnostic = Run("<TextBlock Text=\"{rt:Message " + key + "}\"/>").Single();
            Assert.Equal("RTR0081", diagnostic.Id, key);
            Assert.True(diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains("Did you mean '" + suggestion + "'?", StringComparison.Ordinal), diagnostic.GetMessage(CultureInfo.InvariantCulture));
        }
        Diagnostic unrelated = Run("<TextBlock Text=\"{rt:Message checkout_summary}\"/>").Single();
        Assert.True(!unrelated.GetMessage(CultureInfo.InvariantCulture).Contains("Did you mean", StringComparison.Ordinal), unrelated.GetMessage(CultureInfo.InvariantCulture));
        Assert.True(unrelated.GetMessage(CultureInfo.InvariantCulture).Contains("flattened readable name", StringComparison.Ordinal), unrelated.GetMessage(CultureInfo.InvariantCulture));
    }

    private static void SourceNarrowing()
    {
        // The WPF trial's case: one Message with its own Source no longer hides a typo elsewhere in the file.
        Diagnostic[] own = Run("""
            <TextBlock Text="{rt:Message outside, Source={x:Static other:Catalogs.External}}"/>
            <TextBlock Text="{rt:Message typo}"/>
            """, catalog: null, defaultCatalog: "app");
        Assert.Equal("RTR0081", own.Single(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Id, string.Join("\n", own.AsEnumerable()));
        Diagnostic info = own.Single(static diagnostic => diagnostic.Id == "RTR0085");
        Assert.Equal(4, info.Location.GetLineSpan().StartLinePosition.Line, "info at the source's line");
        Assert.True(info.GetMessage(CultureInfo.InvariantCulture).Contains("Message 'outside' sets its own Source", StringComparison.Ordinal) &&
            info.GetMessage(CultureInfo.InvariantCulture).Contains("Catalog=\"app\"", StringComparison.Ordinal), info.GetMessage(CultureInfo.InvariantCulture));

        // An attached source covers its element's content and the file's reusable content, nothing else.
        Diagnostic[] attached = Run("""
            <Window.Resources><DataTemplate x:Key="Row"><TextBlock Text="{rt:Message template_key}"/></DataTemplate></Window.Resources>
            <StackPanel>
              <StackPanel rt:TranslationProperties.Source="{StaticResource External}"><TextBlock Text="{rt:Message outside}"/><TextBlock Text="{rt:Message other_outside}"/></StackPanel>
              <TextBlock Text="{rt:Message typo}"/>
            </StackPanel>
            """, catalog: null, defaultCatalog: "app");
        Assert.Equal(2, attached.Length, string.Join("\n", attached.AsEnumerable()));
        Assert.True(attached.Single(static diagnostic => diagnostic.Id == "RTR0081").GetMessage(CultureInfo.InvariantCulture).StartsWith("'typo'", StringComparison.Ordinal), "sibling typo is checked");
        Diagnostic scoped = attached.Single(static diagnostic => diagnostic.Id == "RTR0085");
        Assert.Equal(6, scoped.Location.GetLineSpan().StartLinePosition.Line, "one info at the attached source");
        Assert.True(scoped.GetMessage(CultureInfo.InvariantCulture).Contains("this element's content and in this file's templates", StringComparison.Ordinal) &&
            scoped.GetMessage(CultureInfo.InvariantCulture).Contains("'template_key' at line 5", StringComparison.Ordinal), scoped.GetMessage(CultureInfo.InvariantCulture));

        Diagnostic[] property = Run("""
            <StackPanel><rt:TranslationProperties.Source><StaticResource ResourceKey="External"/></rt:TranslationProperties.Source><TextBlock Text="{rt:Message outside}"/></StackPanel>
            <TextBlock Text="{rt:Message typo}"/>
            """, catalog: null, defaultCatalog: "app");
        Assert.Equal("RTR0081,RTR0085", string.Join(",", property.Select(static diagnostic => diagnostic.Id).Order(StringComparer.Ordinal)), "property element source");

        // A style setter can apply anywhere, so the whole file stays unchecked and says why.
        Diagnostic setter = Run("""
            <Window.Resources><Style TargetType="StackPanel"><Setter Property="rt:TranslationProperties.Source" Value="{StaticResource External}"/></Style></Window.Resources>
            <TextBlock Text="{rt:Message typo}"/>
            """, catalog: null, defaultCatalog: "app").Single();
        Assert.Equal("RTR0085", setter.Id, setter.ToString());
        Assert.True(setter.GetMessage(CultureInfo.InvariantCulture).Contains("whole file", StringComparison.Ordinal) && setter.GetMessage(CultureInfo.InvariantCulture).Contains("'typo' at line 6", StringComparison.Ordinal), setter.GetMessage(CultureInfo.InvariantCulture));

        Assert.Equal(0, Run("<TextBlock Text=\"{rt:Message outside, Source={StaticResource External}}\"/>", catalog: null).Length, "no default catalog, nothing to report");
        Assert.Equal(0, Run("<TextBlock Text=\"{rt:Message application_title, Source={StaticResource App}}\"/>", catalog: "app").Length, "file assertion checks explicit sources");
    }

    private static void MessageKinds()
    {
        (string Content, string Expected)[] wording =
        [
            ("<TextBlock Text=\"{rt:Message help}\"/>", "Use rt:TranslationProperties.RichMessage=\"help\""),
            ("<TextBlock Text=\"{rt:Message notice}\"/>", "Render it with WpfDocumentRenderer."),
            ("<TextBlock rt:TranslationProperties.RichMessage=\"application_title\"/>", "Use {rt:Message application_title} instead."),
            ("<TextBlock rt:TranslationProperties.RichMessage=\"notice\"/>", "is a document"),
        ];
        foreach ((string content, string expected) in wording)
            Assert.True(Run(content).Single().GetMessage(CultureInfo.InvariantCulture).Contains(expected, StringComparison.Ordinal), content + ": " + Run(content).Single().GetMessage(CultureInfo.InvariantCulture));
        foreach (string content in new[] { "<TextBlock Text=\"{rt:Message help}\"/>", "<TextBlock Text=\"{rt:Message notice}\"/>",
            "<TextBlock rt:TranslationProperties.RichMessage=\"application_title\"/>", "<TextBlock rt:TranslationProperties.RichMessage=\"notice\"/>" })
            Assert.Equal("RTR0083", Run(content).Single().Id, content);
    }

    private static void UnresolvedDeclarations()
    {
        Assert.Equal(0, Run("<TextBlock Text=\"{rt:Message outside}\"/>", catalog: null).Length, "unknown catalog");
        AssertSkipped(Run("<TextBlock Text=\"{rt:Message outside, Source={StaticResource External}}\"/>", catalog: null, defaultCatalog: "app"), "external resource source");
        AssertSkipped(Run("<TextBlock Text=\"{rt:Message outside, Source = {Binding Catalog}}\"/>", catalog: null, defaultCatalog: "app"), "dynamic source");
        AssertSkipped(Run("<StackPanel rt:TranslationProperties.Source=\"{StaticResource External}\"><TextBlock Text=\"{rt:Message outside}\"/></StackPanel>", catalog: null, defaultCatalog: "app"), "inherited source");
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
            AssertSkipped(Run(content, catalog: null, defaultCatalog: "app"), content);
            Assert.Equal("RTR0081", Run(content).Single().Id, "per-file assertion still checks " + property);
        }
        AssertSkipped(Run("""
            <Style><Setter Value="{StaticResource External}"><Setter.Property><x:Static Member="rt:TranslationProperties.SourceProperty"/></Setter.Property></Setter></Style>
            <TextBlock Text="{rt:Message outside}"/>
            """, catalog: null, defaultCatalog: "app"), "object x:Static source setter");
        AssertSkipped(Run("""
            <TextBlock xmlns:y="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:t="clr-namespace:Runic.Translations.Wpf;assembly=Runic.Translations.Wpf">
              <TextBlock.Style><Style><Setter Property="{y:Static Member='t:TranslationProperties.SourceProperty'}" Value="{StaticResource External}"/></Style></TextBlock.Style>
              <TextBlock.Text><rt:Message Key="outside"/></TextBlock.Text>
            </TextBlock>
            """, catalog: null, defaultCatalog: "app"), "namespace aliases on x:Static source setter");
        AssertSkipped(Run("""
            <p:Style xmlns:p="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns="http://schemas.microsoft.com/winfx/2006/xaml">
              <p:Setter Property="{Static rt:TranslationProperties.SourceProperty}" Value="{p:StaticResource External}"/>
            </p:Style><TextBlock Text="{rt:Message outside}"/>
            """, catalog: null, defaultCatalog: "app"), "default XAML namespace on static source setter");
        AssertSkipped(Run("""<TextBlock Text="{ rt:Message outside, Source = '{StaticResource External}' }"/>""", catalog: null, defaultCatalog: "app"), "quoted and spaced Message.Source");
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
