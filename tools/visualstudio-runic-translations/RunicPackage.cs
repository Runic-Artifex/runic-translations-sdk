using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;

namespace Runic.Translations.VisualStudio;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[Guid("38646832-a96f-4fd1-b33a-108353a854da")]
public sealed class RunicPackage : AsyncPackage
{
    private static readonly Guid Commands = new("f381baca-8a08-46bd-8a1e-e2c7c4972886");
    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var menu = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService ?? throw new InvalidOperationException("Visual Studio command service is unavailable.");
        menu.AddCommand(new MenuCommand((_, _) => Execute(false), new CommandID(Commands, 0x100)));
        menu.AddCommand(new MenuCommand((_, _) => Execute(true), new CommandID(Commands, 0x101)));
    }
    private void Execute(bool restart)
    {
        _ = JoinableTaskFactory.RunAsync(async () => {
            try
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                var components = await GetServiceAsync(typeof(SComponentModel)) as IComponentModel ?? throw new InvalidOperationException("Visual Studio component service is unavailable.");
                var client = components.GetService<RunicLanguageClient>();
                if (restart) { await client.RestartAsync(); return; }
                var dte = await GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE.DTE ?? throw new InvalidOperationException("Visual Studio document service is unavailable.");
                var document = dte.ActiveDocument;
                if (document == null || (!document.FullName.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase) && !document.FullName.EndsWith(".mf2", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Place the cursor inside an MF2 message.");
                var selection = (EnvDTE.TextSelection)document.Selection;
                string uri = new Uri(document.FullName).AbsoluteUri;
                var info = await client.RequestAsync("runic/message", new { textDocument = new { uri }, position = new { line = selection.ActivePoint.Line - 1, character = selection.ActivePoint.LineCharOffset - 1 } }, DisposalToken);
                if (info.Value<bool>("isGroup")) throw new InvalidOperationException("Place the cursor inside a message to preview it.");
                var preview = new PreviewWindow(client, uri, info);
                new WindowInteropHelper(preview).Owner = Process.GetCurrentProcess().MainWindowHandle;
                preview.Show();
                await preview.LoadAsync();
            }
            catch (Exception error) { MessageBox.Show(error.Message, "Runic Translations", MessageBoxButton.OK, MessageBoxImage.Error); }
        });
    }
}
