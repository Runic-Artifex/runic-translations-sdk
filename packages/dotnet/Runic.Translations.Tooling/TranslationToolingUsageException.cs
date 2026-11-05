using System;

namespace Runic.Translations.Tooling;

/// <summary>
/// An invalid request to a shared tooling operation (status, locale changes, catalog import): its message is the text
/// the command line prints as a usage error, and the Translations Editor shows as the reason the operation was refused.
/// </summary>
internal sealed class TranslationToolingUsageException(string message) : Exception(message);
