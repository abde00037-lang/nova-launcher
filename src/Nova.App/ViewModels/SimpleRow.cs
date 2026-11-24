namespace Nova.App.ViewModels;

public sealed class SimpleRow
{
	public string Title { get; }

	public string Subtitle { get; }

	public string Detail { get; }

	public string Badge { get; }

	public object? Tag { get; }

	public bool HasBadge => !string.IsNullOrEmpty(Badge);

	public SimpleRow(string title, string subtitle, string detail = "", string badge = "", object? tag = null)
	{
		Title = title;
		Subtitle = subtitle;
		Detail = detail;
		Badge = badge;
		Tag = tag;
	}
}
