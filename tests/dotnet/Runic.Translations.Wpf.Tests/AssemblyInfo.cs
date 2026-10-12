using Runic.Translations.Wpf.Tests;
using TUnit.Core;
using TUnit.Core.Executors;

// Every case runs on the suite's one WPF dispatcher thread (WpfDispatcherExecutor). A case's Flush pushes a nested
// dispatcher frame, which would also run another case queued on the same dispatcher, so cases never run in parallel.
// The binding cases share process-wide state: TranslationSource.Default (set once, guarded after first use), the
// Catalogs used by x:Static in XAML, the sources' managers and their locale, Trace.Listeners and, in the design-mode
// case, a permanent IsInDesignMode metadata override. Every case therefore keeps the former runner's order through
// [NotInParallel(WpfSuite.Key, Order = n)]; the shared key makes TUnit honour Order across classes.
[assembly: TestExecutor<WpfDispatcherExecutor>]
[assembly: NotInParallel]

namespace Runic.Translations.Wpf.Tests
{
    internal static class WpfSuite
    {
        /// <summary>The constraint key shared by every case, so that Order applies across classes.</summary>
        internal const string Key = "WpfSuite";
    }
}
