using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

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
					JsonObject node = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? throw new InvalidDataException("Invalid account file.");
					NovaAccount? novaAccount = node.Deserialize<NovaAccount>();
					if (novaAccount != null && !string.IsNullOrWhiteSpace(novaAccount.Name))
					{
						string? access = node["protectedAccessToken"]?.GetValue<string>();
						string? refresh = node["protectedRefreshToken"]?.GetValue<string>();
						if (!string.IsNullOrWhiteSpace(access)) novaAccount.AccessToken = WindowsTokenProtector.Unprotect(access);
						if (!string.IsNullOrWhiteSpace(refresh)) novaAccount.RefreshToken = WindowsTokenProtector.Unprotect(refresh);
						// Migrate legacy plaintext only after the account was read successfully.
						if (OperatingSystem.IsWindows() && novaAccount.IsMicrosoft &&
						    ((!string.IsNullOrEmpty(novaAccount.AccessToken) && string.IsNullOrEmpty(access)) ||
						     (!string.IsNullOrEmpty(novaAccount.RefreshToken) && string.IsNullOrEmpty(refresh))))
						{
							try { Save(novaAccount); } catch { /* Keep the original account file if migration fails. */ }
						}
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
		JsonObject node = JsonSerializer.SerializeToNode(account)?.AsObject() ?? throw new InvalidDataException("Could not serialize account.");
		if (!string.IsNullOrEmpty(account.AccessToken)) node["protectedAccessToken"] = WindowsTokenProtector.Protect(account.AccessToken);
		if (!string.IsNullOrEmpty(account.RefreshToken)) node["protectedRefreshToken"] = WindowsTokenProtector.Protect(account.RefreshToken);
		node.Remove("accessToken");
		node.Remove("refreshToken");
		string target = Path.Combine(Folder, SafeName(account.Id) + ".json");
		string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			File.WriteAllText(temp, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
			File.Move(temp, target, overwrite: true);
		}
		finally { if (File.Exists(temp)) File.Delete(temp); }
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
