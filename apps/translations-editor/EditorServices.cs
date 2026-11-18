using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using ReactiveUI.Primitives.Concurrency;
using Runic.Application.Views;
using Runic.Application.Views.ReactiveUI;
using Runic.Navigation;
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
    internal static ServiceProvider Create(string workspacePath, TimeSpan? closeQuestionTimeout = null)
    {
        AppLocator.CurrentMutable.RegisterConstant(new NullLogger(), typeof(ILogger));
        AppLocator.CurrentMutable.RegisterConstant(new DefaultLogManager(AppLocator.Current), typeof(ILogManager));
        var services = new ServiceCollection();
        services.AddRunicReactiveModelContext();
        services.AddScoped(_ => new EditorSession(workspacePath));
        services.AddScoped(provider => new EditorViewModel(provider.GetRequiredService<EditorSession>(),
            provider.GetRequiredService<IRunicModelContext>(), provider.GetRequiredService<ISequencer>(),
            provider.GetRequiredService<IRunicWindowLocale>(), provider.GetRequiredService<IRunicWindowContext>(),
            closeQuestionTimeout));
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
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
