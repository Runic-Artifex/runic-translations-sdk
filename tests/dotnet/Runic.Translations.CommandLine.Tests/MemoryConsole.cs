using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Runic.CommandLine;

namespace Runic.Translations.CommandLine.Tests;

internal sealed class MemoryConsole : ICommandConsole
{
    private readonly StringBuilder _out = new();
    private readonly StringBuilder _error = new();

    public bool IsInteractive => false;
    public bool IsInputRedirected => true;
    public bool IsOutputRedirected => true;
    public bool IsErrorRedirected => true;
    public string StandardOutput => _out.ToString();
    public string StandardError => _error.ToString();

    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);

    public ValueTask WriteOutAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken)
    {
        _out.Append(value.Span);
        return ValueTask.CompletedTask;
    }

    public ValueTask WriteOutBytesAsync(ReadOnlyMemory<byte> value, CancellationToken cancellationToken)
    {
        _out.Append(Encoding.UTF8.GetString(value.Span));
        return ValueTask.CompletedTask;
    }

    public ValueTask WriteErrorAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken)
    {
        _error.Append(value.Span);
        return ValueTask.CompletedTask;
    }
}
