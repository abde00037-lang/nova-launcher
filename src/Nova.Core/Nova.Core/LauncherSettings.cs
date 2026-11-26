using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Nova.Core;

public sealed class LauncherSettings
{
    [JsonPropertyName("accent")]
    public string Accent { get; set; } = "Green";

    // Never serialize the cleartext secret. A failed unlock is kept intact on later saves.
    private string? _curseForgeKey;
    private string? _protectedCurseForgeKey;
    private bool _curseForgeKeyChanged;

    [JsonIgnore]
    public string? CurseForgeKey
    {
        get => _curseForgeKey;
        set { _curseForgeKey = value; _curseForgeKeyChanged = true; }
    }

    [JsonIgnore]
    public bool CurseForgeKeyUnlockFailed => !string.IsNullOrEmpty(_protectedCurseForgeKey) && _curseForgeKey == null && !_curseForgeKeyChanged;
    [JsonIgnore]
    public bool CurseForgeKeyNeedsSave => _curseForgeKeyChanged;

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

    private static string FileFor(LauncherPaths paths) => Path.Combine(paths.Root, "settings.json");

    public static LauncherSettings Load(LauncherPaths paths)
    {
        try
        {
            string path = FileFor(paths);
            if (!File.Exists(path)) return new LauncherSettings();
            JsonObject node = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? throw new InvalidDataException("Invalid settings.");
            LauncherSettings settings = node.Deserialize<LauncherSettings>() ?? new LauncherSettings();
            settings._protectedCurseForgeKey = node["protectedCurseForgeKey"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(settings._protectedCurseForgeKey))
            {
                // Failure to unlock must never silently destroy the encrypted value on save.
                try { settings._curseForgeKey = WindowsTokenProtector.Unprotect(settings._protectedCurseForgeKey); }
                catch { settings._curseForgeKey = null; }
            }
            else
            {
                settings._curseForgeKey = node["curseForgeKey"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(settings._curseForgeKey))
                {
                    settings._curseForgeKeyChanged = true;
                    if (OperatingSystem.IsWindows()) settings.Save(paths); // best-effort atomic migration
                }
            }
            return settings;
        }
        catch
        {
            // Corrupt settings must not disappear silently: preserve the original file for recovery.
            try
            {
                string corruptPath = FileFor(paths);
                string corruptBackup = corruptPath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                if (File.Exists(corruptPath) && !File.Exists(corruptBackup)) File.Copy(corruptPath, corruptBackup);
            }
            catch { }
            NovaLog.Write(paths.Root, "WARN", "settings.json is corrupt - defaults applied, original kept as backup");
            return new LauncherSettings();
        }
    }

    public bool Save(LauncherPaths paths)
    {
        string target = FileFor(paths);
        string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            string? protectedValue = _protectedCurseForgeKey;
            if (_curseForgeKeyChanged)
                protectedValue = string.IsNullOrEmpty(_curseForgeKey) ? null : WindowsTokenProtector.Protect(_curseForgeKey);
            JsonObject node = JsonSerializer.SerializeToNode(this)?.AsObject() ?? throw new InvalidDataException("Invalid settings.");
            if (!string.IsNullOrEmpty(protectedValue)) node["protectedCurseForgeKey"] = protectedValue;
            Directory.CreateDirectory(paths.Root);
            File.WriteAllText(temp, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, target, overwrite: true);
            _protectedCurseForgeKey = protectedValue;
            _curseForgeKeyChanged = false;
            return true;
        }
        catch (Exception saveEx) { NovaLog.Write(paths.Root, "WARN", "settings save failed - " + saveEx.Message); return false; /* keep the old settings file */ }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
    }
}
