using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Newtonsoft.Json.Linq;
using Span = System.Windows.Documents.Span;

namespace Runic.Translations.VisualStudio;

internal sealed class PreviewWindow : Window
{
    private readonly RunicLanguageClient client;
    private readonly string uri;
    private readonly JObject info;
    private readonly ComboBox locales = new();
    private readonly StackPanel inputs = new();
    private readonly TextBlock result = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Dictionary<string, TextBox> samples = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Button render = new() { Content = "Render preview", Margin = new Thickness(0, 8, 0, 0) };
    internal PreviewWindow(RunicLanguageClient client, string uri, JObject info)
    {
        this.client = client; this.uri = uri; this.info = info;
        Title = "Runic · " + info.Value<string>("key"); Width = 600; Height = 480;
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Locale" });
        foreach (var locale in (JArray)info["locales"]!) locales.Items.Add(locale.ToString());
        locales.SelectedItem = info.Value<string>("locale"); AutomationProperties.SetName(locales, "Preview locale");
        panel.Children.Add(locales); panel.Children.Add(inputs); panel.Children.Add(render); panel.Children.Add(status); panel.Children.Add(result);
        panel.Children.Add(new TextBlock { Text = "Links and actions are inert. Icons and custom components use labeled placeholders. Save runic.json before previewing configuration changes.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0) });
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        render.Click += (_, _) => { _ = Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.RunAsync(RenderAsync); };
        Closed += (_, _) => lifetime.Cancel();
    }
    internal async Task LoadAsync()
    {
        try
        {
            var preview = await client.RequestAsync("workspace/executeCommand", new { command = "runic.preview", arguments = new object[] { uri, info.Value<string>("key")!, locales.SelectedItem.ToString()! } }, lifetime.Token);
            if (lifetime.IsCancellationRequested) return;
            var example = preview["examples"]?.First as JObject;
            foreach (var input in ((JObject)preview["ast"]!["inputs"]!).Properties())
            {
                var box = new TextBox { Text = example?[input.Name]?.ToString() ?? "" };
                AutomationProperties.SetName(box, input.Name); samples.Add(input.Name, box);
                inputs.Children.Add(new TextBlock { Text = input.Name, Margin = new Thickness(0, 6, 0, 0) }); inputs.Children.Add(box);
            }
            await RenderAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { status.Text = error.Message; }
    }
    private async Task RenderAsync()
    {
        render.IsEnabled = false;
        try
        {
            var values = new JObject(); foreach (var pair in samples) values[pair.Key] = pair.Value.Text;
            var preview = await client.RequestAsync("workspace/executeCommand", new { command = "runic.renderPreview", arguments = new object[] { uri, info.Value<string>("key")!, locales.SelectedItem.ToString()!, values } }, lifetime.Token);
            if (lifetime.IsCancellationRequested) return;
            result.Inlines.Clear();
            foreach (JObject run in (JArray)preview["runs"]!) result.Inlines.Add(Build(run));
            status.Text = "";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { result.Inlines.Clear(); status.Text = error.Message; }
        finally { render.IsEnabled = true; }
    }
    private static Inline Build(JObject run)
    {
        if (run["text"]?.Type == JTokenType.String) return new Run(run.Value<string>("text"));
        string name = run.Value<string>("name")!;
        if (name == "runic:br") return new LineBreak();
        if (name == "runic:icon") return new Run("[icon: " + run["options"]?["ref"] + "]");
        Span span = name is "runic:strong" or "runic:bold" ? new Bold() : name is "runic:em" or "runic:italic" ? new Italic() : new Span();
        if (name == "runic:code") span.FontFamily = new System.Windows.Media.FontFamily("Consolas");
        if (name == "runic:link") span.TextDecorations = TextDecorations.Underline;
        if (!name.StartsWith("runic:", StringComparison.Ordinal)) span.Inlines.Add(new Run("[" + name + "] "));
        foreach (JObject child in (JArray)run["children"]!) span.Inlines.Add(Build(child));
        if (name == "runic:action")
        {
            var button = new Button { IsEnabled = false, Content = new TextBlock(span) };
            return new InlineUIContainer(button);
        }
        return span;
    }
}
