using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Nova.Core;

public sealed class AccountStore
{
	private readonly LauncherPaths _paths;

	private string Folder
	{
		get
		{
			string text = Path.Combine(_paths.Root, "accounts");
			Directory.CreateDirectory(text);
			return text;
		}
	}

	public AccountStore(LauncherPaths paths)
	{
		_paths = paths;
	}

	public List<NovaAccount> All()
	{
		List<NovaAccount> list = new List<NovaAccount>();
		try
		{
			string[] files = Directory.GetFiles(Folder, "*.json");
			foreach (string path in files)
			{
				try
				{
					NovaAccount novaAccount = JsonSerializer.Deserialize<NovaAccount>(File.ReadAllText(path));
					if (novaAccount != null && !string.IsNullOrWhiteSpace(novaAccount.Name))
					{
						list.Add(novaAccount);
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		return list.OrderBy((NovaAccount a) => a.Kind).ThenBy((NovaAccount a) => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
	}

	public NovaAccount? Find(string id)
	{
		return All().FirstOrDefault((NovaAccount a) => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
	}

	public void Save(NovaAccount account)
	{
		File.WriteAllText(Path.Combine(Folder, SafeName(account.Id) + ".json"), JsonSerializer.Serialize(account, new JsonSerializerOptions
		{
			WriteIndented = true
		}));
	}

	public void Remove(string id)
	{
		string path = Path.Combine(Folder, SafeName(id) + ".json");
		if (File.Exists(path))
		{
			File.Delete(path);
		}
	}

	public NovaAccount Active(LauncherSettings settings)
	{
		List<NovaAccount> list = All();
		if (list.Count == 0)
		{
			return NovaAccount.CreateOffline("Player");
		}
		return list.FirstOrDefault((NovaAccount a) => string.Equals(a.Id, settings.ActiveAccountId, StringComparison.OrdinalIgnoreCase)) ?? list.FirstOrDefault((NovaAccount a) => a.IsMicrosoft) ?? list[0];
	}

	private static string SafeName(string id)
	{
		return new string(id.Select((char c) => (!char.IsLetterOrDigit(c) && c != '-' && c != '_') ? '-' : c).ToArray());
	}
}
