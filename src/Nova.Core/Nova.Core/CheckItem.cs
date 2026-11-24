namespace Nova.Core;

public sealed record CheckItem(string Area, CheckStatus Status, string Message, string? Suggestion = null, bool Fixable = false);
