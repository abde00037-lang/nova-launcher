using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nova.Core;

public sealed class LauncherSettings
{
	[JsonPropertyName("accent")]
	public string Accent { get; set; } = "Green";

	[JsonPropertyName("curseForgeKey")]
	public string? CurseForgeKey { get; set; }

	[JsonPropertyName("storeSource")]
	public string StoreSource { get; set; } = "Modrinth";

	[JsonPropertyName("activeAccountId")]
	public string? ActiveAccountId { get; set; }

	[JsonPropertyName("msClientId")]
	public string? MicrosoftClientId { get; set; }

	[JsonPropertyName("storeTargetVersion")]
	public string? StoreTargetVersion { get; set; }

	[JsonPropertyName("modsTargetVersion")]
	public string? ModsTargetVersion { get; set; }

	[JsonPropertyName("updateRepo")]
	public string UpdateRepo { get; set; } = "abde00037-lang/nova-launcher";

	[JsonPropertyName("autoCheckUpdates")]
	public bool AutoCheckUpdates { get; set; } = true;

	private static string FileFor(LauncherPaths paths)
	{
		return Path.Combine(paths.Root, "settings.json");
	}

	public static LauncherSettings Load(LauncherPaths paths)
	{
		try
		{
			string path = FileFor(paths);
			if (!File.Exists(path))
			{
				return new LauncherSettings();
			}
			return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path)) ?? new LauncherSettings();
		}
		catch
		{
			return new LauncherSettings();
		}
	}

	public void Save(LauncherPaths paths)
	{
		try
		{
			Directory.CreateDirectory(paths.Root);
			File.WriteAllText(FileFor(paths), JsonSerializer.Serialize(this, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		catch
		{
		}
	}
}
