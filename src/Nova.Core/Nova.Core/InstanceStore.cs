using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nova.Core;

public sealed class InstanceStore
{
	private readonly LauncherPaths _paths;

	public InstanceStore(LauncherPaths paths)
	{
		_paths = paths;
	}

	public string FolderFor(string id)
	{
		if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || !id.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'))
		{
			throw new ArgumentException("Invalid instance ID: use letters, digits, hyphens or underscores only.", nameof(id));
		}
		return Path.Combine(_paths.Instances, id);
	}

	public string ConfigFileFor(string id)
	{
		return Path.Combine(FolderFor(id), "instance.json");
	}

	public List<InstanceConfig> All()
	{
		List<InstanceConfig> list = new List<InstanceConfig>();
		if (!Directory.Exists(_paths.Instances))
		{
			return list;
		}
		string[] directories = Directory.GetDirectories(_paths.Instances);
		for (int i = 0; i < directories.Length; i++)
		{
			string path = Path.Combine(directories[i], "instance.json");
			if (!File.Exists(path))
			{
				continue;
			}
			try
			{
				InstanceConfig instanceConfig = Json.Read<InstanceConfig>(path);
				if (instanceConfig != null)
				{
					list.Add(instanceConfig);
				}
			}
			catch (Exception instanceEx)
			{
				NovaLog.Write(_paths.Root, "WARN", "instance config skipped: " + directories[i] + " - " + instanceEx.Message);
			}
		}
		return list.OrderBy((InstanceConfig instanceConfig2) => instanceConfig2.Name, StringComparer.OrdinalIgnoreCase).ToList();
	}

	public InstanceConfig? Find(string id)
	{
		string path = ConfigFileFor(id);
		if (!File.Exists(path))
		{
			return null;
		}
		return Json.Read<InstanceConfig>(path);
	}

	public void Save(InstanceConfig instance)
	{
		Directory.CreateDirectory(FolderFor(instance.Id));
		Json.Write(ConfigFileFor(instance.Id), instance);
	}

	public bool Remove(string id)
	{
		string fullPath = Path.GetFullPath(FolderFor(id));
		string value = Path.GetFullPath(_paths.Instances).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		if (!fullPath.StartsWith(value, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (!Directory.Exists(fullPath))
		{
			return false;
		}
		Directory.Delete(fullPath, recursive: true);
		return true;
	}

	public static string Slug(string name)
	{
		string text = new string((from c in name.Trim().ToLowerInvariant()
			select (!char.IsLetterOrDigit(c)) ? '-' : c).ToArray());
		while (text.Contains("--"))
		{
			text = text.Replace("--", "-");
		}
		text = text.Trim('-');
		if (!string.IsNullOrEmpty(text))
		{
			return text;
		}
		return "instance";
	}
}
