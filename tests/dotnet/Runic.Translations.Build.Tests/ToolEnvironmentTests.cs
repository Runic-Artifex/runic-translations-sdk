using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Runic.CommandLine;
using Runic.Translations.Tool;
using TUnit.Core;

namespace Runic.Translations.Build.Tests;

// The tool's CommandApp reads the process environment itself. Until Runic.CommandLine 0.8 the tool built its own
// ParseSettings with only the output value, so RUNIC_COMMANDLINE_NO_INPUT and option environment fallbacks never
// reached its commands. The case runs the tool's application in process, with the tool's catalog and one probe
// command that reports whether its prompts would ask; the suite is NotInParallel, so no other case sees the variable.
internal sealed class ToolEnvironmentTests
{
    private const string NoInput = "RUNIC_COMMANDLINE_NO_INPUT";

    [Test, DisplayName("RUNIC_COMMANDLINE_NO_INPUT reaches the tool's commands")]
    public async Task NoInputReachesTheTool()
    {
        string? original = Environment.GetEnvironmentVariable(NoInput);
        try
        {
            Environment.SetEnvironmentVariable(NoInput, null);
            Assert.Equal("interactive", await RunProbeAsync());
            Environment.SetEnvironmentVariable(NoInput, "1");
            Assert.Equal("disabled", await RunProbeAsync(), $"{NoInput}=1 did not reach the command.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(NoInput, original);
        }
    }

    private static async Task<string> RunProbeAsync()
    {
        CommandCatalog catalog = CommandCatalog.Combine(
            Program.CreateCatalog(),
            new CommandCatalogBuilder().Command<ProbeOptions, ProbeHandler, string>("probe-prompts", command => command
                .BindWith(ProbeBinder.Instance)
                .CreateHandlerWith(ProbeHandler.Factory)
                .Produces(CommandResultCodecs.String)).Build());
        var console = new InteractiveConsole();
        int exitCode = await Program.CreateApp(catalog, console, new ToolExecutionSink()).RunAsync(["probe-prompts"]);
        Assert.Equal(0, exitCode, console.Text);
        return console.Text.Trim();
    }

    private sealed class ProbeOptions;

    private sealed class ProbeBinder : ICommandOptionsBinder<ProbeOptions>
    {
        internal static ProbeBinder Instance { get; } = new();
        public ValueTask<CommandOutcome<ProbeOptions>> BindAsync(ParsedInvocation invocation, CancellationToken cancellationToken) =>
            ValueTask.FromResult(CommandOutcome.Success(new ProbeOptions()));
    }

    private sealed class ProbeHandler : ICommandHandler<ProbeOptions, string>
    {
        internal static ICommandHandlerFactory<ProbeHandler> Factory { get; } = new HandlerFactory();

        public ValueTask<CommandOutcome<string>> ExecuteAsync(ProbeOptions options, CommandExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(CommandOutcome.Success(new CommandPrompts(context).IsInteractive ? "interactive" : "disabled"));

        private sealed class HandlerFactory : ICommandHandlerFactory<ProbeHandler>
        {
            public ProbeHandler Create(IServiceProvider services) => new();
        }
    }

    private sealed class InteractiveConsole : ICommandConsole
    {
        private readonly StringBuilder _text = new();
        internal string Text => _text.ToString();
        public bool IsInteractive => true;
        public bool IsInputRedirected => false;
        public bool IsOutputRedirected => false;
        public bool IsErrorRedirected => false;
        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);
        public ValueTask WriteOutAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken) { _text.Append(value.Span); return ValueTask.CompletedTask; }
        public ValueTask WriteOutBytesAsync(ReadOnlyMemory<byte> value, CancellationToken cancellationToken) { _text.Append(Encoding.UTF8.GetString(value.Span)); return ValueTask.CompletedTask; }
        public ValueTask WriteErrorAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken) { _text.Append(value.Span); return ValueTask.CompletedTask; }
    }
}
