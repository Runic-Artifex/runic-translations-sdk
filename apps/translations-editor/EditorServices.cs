using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using ReactiveUI.Primitives.Concurrency;
using Runic.Application.Views;
using Runic.Application.Views.Desktop;
using Runic.Application.Views.ReactiveUI;
using Runic.Navigation;
using Runic.Platform;
using Splat;

namespace Runic.Translations.Editor;

/// <summary>The services of the editor's window: its session and ViewModel per window scope, and the Views.</summary>
/// <remarks>Outside Program.cs, so the frontend bootstrap build (a library without Program.cs) and the smoke test use
/// the same registrations as <c>edit</c> and <c>serve</c>.</remarks>
internal static class EditorServices
{
    /// <param name="workspacePath">The workspace each window's session opens.</param>
    /// <param name="closeQuestionTimeout">How long the page may take to answer the close question; the default otherwise
    /// (<see cref="EditorViewModel.DefaultCloseQuestionTimeout"/>).</param>
    /// <param name="configure">Further registrations after the editor's, such as the smoke test's platform fakes.</param>
    internal static ServiceProvider Create(string workspacePath, TimeSpan? closeQuestionTimeout = null,
        Action<IServiceCollection>? configure = null)
    {
        AppLocator.CurrentMutable.RegisterConstant(new NullLogger(), typeof(ILogger));
        AppLocator.CurrentMutable.RegisterConstant(new DefaultLogManager(AppLocator.Current), typeof(ILogManager));
        var services = new ServiceCollection();
        services.AddRunicReactiveModelContext();
        services.AddScoped(_ => new EditorSession(workspacePath));
        services.AddScoped(provider => new EditorViewModel(provider.GetRequiredService<EditorSession>(),
            provider.GetRequiredService<IRunicModelContext>(), provider.GetRequiredService<ISequencer>(),
            provider.GetRequiredService<IRunicWindowLocale>(), provider.GetRequiredService<IRunicWindowContext>(),
            provider.GetRequiredService<IFileDialogs>(), closeQuestionTimeout));
        var locator = new DefaultViewLocator();
        locator.CreateMappingBuilder()
            .Map<EditorWorkspaceViewModel>(() => new EditorWorkspaceView())
            .Map<EditorDocumentToolsViewModel>(() => new EditorDocumentToolsView())
            .Map<EditorReviewViewModel>(() => new EditorReviewView())
            .Map<EditorInterchangeViewModel>(() => new EditorInterchangeView())
            .Map<EditorDiagnosticsViewModel>(() => new EditorDiagnosticsView())
            .Map<EditorLocalStateViewModel>(() => new EditorLocalStateView())
            .Map<EditorProjectViewModel>(() => new EditorProjectView())
            .Map<EditorLibrariesViewModel>(() => new EditorLibrariesView())
            .Map<EditorDocumentViewModel>(() => new EditorDocumentView());
        services.AddScoped<IRunicViewLocator>(_ => new ReactiveRunicViewLocator(locator));
        services.AddRunicWindowService();
        services.AddRunicViews();
        // The window's native services, bound to its owner when it opens: the workspace folder dialog belongs to the
        // editor window (EditorWorkspacePicker).
        services.AddRunicPlatformServices();
        configure?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
