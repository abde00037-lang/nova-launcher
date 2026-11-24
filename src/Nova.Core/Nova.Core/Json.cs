using System.IO;
using System.Text.Json;

namespace Nova.Core;

public static class Json
{
	public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true
	};

	public static T? Read<T>(string path)
	{
		using FileStream utf8Json = File.OpenRead(path);
		return JsonSerializer.Deserialize<T>(utf8Json, Options);
	}

	public static void Write<T>(string path, T value)
	{
		string directoryName = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		string text = path + ".tmp";
		File.WriteAllText(text, JsonSerializer.Serialize(value, new JsonSerializerOptions(Options)
		{
			WriteIndented = true
		}));
		if (File.Exists(path))
		{
			File.Delete(path);
		}
		File.Move(text, path);
	}
}
