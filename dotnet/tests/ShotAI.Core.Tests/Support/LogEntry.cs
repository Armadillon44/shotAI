using Microsoft.Extensions.Logging;

namespace ShotAI.Core.Tests.Support;

/// <summary>One log call captured by <see cref="CapturingLoggerProvider"/>.</summary>
public sealed record LogEntry(string Category, LogLevel Level, EventId EventId, string Message, Exception? Exception);
