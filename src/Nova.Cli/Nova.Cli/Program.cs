using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nova.Core;

namespace Nova.Cli;

internal static class Program
{
	private static LauncherPaths _paths = null;

	private static readonly Dictionary<string, string?> Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private static int Main(string[] rawArgs)
	{
		Console.OutputEncoding = Encoding.UTF8;
		List<string> list = rawArgs.ToList();
		string text = TakeOption(list, "--root");
		_paths = ((text == null) ? LauncherPaths.Default() : new LauncherPaths(text));
		if (list.Count == 0)
		{
			return Help();
		}
		string text2 = list[0].ToLowerInvariant();
		List<string> rest = list.Skip(1).ToList();
		for (int i = 0; i < rest.Count; i++)
		{
			if (rest[i].StartsWith("--"))
			{
				string key = rest[i];
				string text3 = ((i + 1 < rest.Count && !rest[i + 1].StartsWith("--")) ? rest[i + 1] : null);
				Options[key] = text3;
				if (text3 != null)
				{
					i++;
				}
			}
		}
		List<string> positional = rest.Where((string _, int index) =>
		{
			if (!rest[index].StartsWith("--"))
			{
				if (index > 0 && rest[index - 1].StartsWith("--"))
				{
					string text4 = rest[index - 1];
					int num;
					switch (text4)
					{
					default:
						num = ((!(text4 == "--json")) ? 1 : 0);
						break;
					case "--deep":
					case "--force":
					case "--no-wait":
					case "--update":
					case "--check-updates":
					case "--no-copy":
					case "--no-deps":
					case "--repair":
					case "--play":
					case "--worlds":
					case "--apply":
						num = 0;
						break;
					}
					return num == 0;
				}
				return true;
			}
			return false;
		}).ToList();
		try
		{
			int result;
			switch (text2)
			{
			case "help":
			case "-h":
			case "/?":
				result = Help();
				break;
			case "paths":
				result = ShowPaths();
				break;
			case "java":
				result = ShowJava(positional);
				break;
			case "instances":
				result = ShowInstances();
				break;
			case "install":
				result = Install(positional);
				break;
			case "add":
				result = AddInstance();
				break;
			case "show":
				result = ShowInstance(positional);
				break;
			case "check":
			case "doctor":
				result = Check(positional);
				break;
			case "fix":
			case "repair":
				result = Repair(positional);
				break;
			case "backup":
				result = Backup(positional);
				break;
			case "backups":
				result = ShowBackups(positional);
				break;
			case "restore":
				result = Restore(positional);
				break;
			case "crash":
				result = Crash(positional);
				break;
			case "pack":
			case "packs":
				result = PackCommand(positional);
				break;
			case "forge":
				result = ForgeCommand(positional);
				break;
			case "fabric":
				result = FabricCommand(positional);
				break;
			case "neoforge":
				result = NeoForgeCommand(positional);
				break;
			case "self-update":
			case "updater":
				result = SelfUpdateCommand(positional);
				break;
			case "store":
				result = StoreCommand(positional);
				break;
			case "mods":
				result = ModsCommand(positional);
				break;
			case "versions":
				result = VersionsCommand();
				break;
			case "login":
			case "account":
			case "accounts":
				result = AccountCommand(positional);
				break;
			case "new":
				result = NewInstanceCommand(positional);
				break;
			case "clone":
			case "duplicate":
				result = CloneCommand(positional);
				break;
			case "plan":
			case "dry-run":
			case "dry":
				result = Plan(positional);
				break;
			case "play":
			case "launch":
				result = Launch(positional);
				break;
			case "ai":
				result = AiCommand(positional);
				break;
			default:
				result = Unknown(text2);
				break;
			}
			return result;
		}
		catch (Exception ex)
		{
			Fail(ex.Message);
			if (Options.ContainsKey("--trace"))
			{
				Console.WriteLine(ex);
			}
			return 3;
		}
	}

	private static int AiCommand(List<string> args)
	{
		string sub = (args.FirstOrDefault() ?? string.Empty).ToLowerInvariant();
		switch (sub)
		{
		case "providers":
		{
			Header("AI providers (keys encrypted at rest)");
			List<AiProvider> providers = AiStore.LoadProviders(_paths);
			if (providers.Count == 0) Console.WriteLine("  (none configured)");
			foreach (AiProvider provider in providers) Row(provider.Name, provider.Protocol + " " + AiClient.NormalizeBase(provider.BaseUrl) + " models=" + string.Join(",", provider.Models));
			return 0;
		}
		case "add":
		{
			List<string> rest = args.Skip(1).ToList();
			if (rest.Count < 4) throw new ArgumentException("usage: nova-cli ai add <name> <openai|anthropic> <baseUrl> <model1,model2>");
			AiProvider provider = new AiProvider
			{
				Name = rest[0],
				Protocol = rest[1].StartsWith("anth", StringComparison.OrdinalIgnoreCase) ? AiProtocol.Anthropic : AiProtocol.OpenAiCompatible,
				BaseUrl = rest[2],
				Models = rest[3].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(m => m.Trim()).ToList()
			};
			AiStore.SaveProvider(_paths, provider);
			Console.WriteLine("Paste the API key for " + provider.Name + ":");
			AiStore.SetKey(_paths, provider.Name, ReadSecretLine());
			Row("saved", provider.Name);
			return 0;
		}
		case "key":
		{
			string name = args.Skip(1).FirstOrDefault() ?? throw new ArgumentException("usage: nova-cli ai key <name>");
			if (AiStore.Find(_paths, name) == null) throw new ArgumentException("unknown provider " + name);
			Console.WriteLine("Paste the new API key for " + name + ":");
			AiStore.SetKey(_paths, name, ReadSecretLine());
			Row("updated", name);
			return 0;
		}
		case "remove":
		{
			string name = args.Skip(1).FirstOrDefault() ?? throw new ArgumentException("usage: nova-cli ai remove <name>");
			Console.WriteLine(AiStore.RemoveProvider(_paths, name) ? "removed " + name : "unknown provider " + name);
			return 0;
		}
		case "models":
		{
			string name = args.Skip(1).FirstOrDefault() ?? throw new ArgumentException("usage: nova-cli ai models <name>");
			AiProvider provider = AiStore.Find(_paths, name) ?? throw new ArgumentException("unknown provider " + name);
			string key = AiStore.GetKey(_paths, name) ?? throw new ArgumentException("no key stored for " + name);
			using AiClient client = new AiClient(60);
			foreach (string id in client.ListModelsAsync(provider, key).GetAwaiter().GetResult()) Console.WriteLine("  " + id);
			return 0;
		}
		case "agent":
		{
			List<string> rest = args.Skip(1).ToList();
			if (rest.Count < 2) throw new ArgumentException("usage: nova-cli ai agent <name> <task>");
			AiProvider provider = AiStore.Find(_paths, rest[0]) ?? throw new ArgumentException("unknown provider " + rest[0]);
			string key = AiStore.GetKey(_paths, rest[0]) ?? throw new ArgumentException("no key stored for " + rest[0]);
			string task = string.Join(" ", rest.Skip(1));
			using AiClient client = new AiClient(120);
			AiAgent agent = new AiAgent(client);
			AiAgentResult result = agent.RunAsync(provider, key, "You are the Nova Workbench assistant. Use the launcher tools when needed; write tools require explicit approval. Be concise.", task, (call, ct) => AiToolbox.HandleAsync(_paths, call, ct), (spec, call, ct) =>
			{
				Console.WriteLine("APPROVE WRITE TOOL " + spec.Name + " " + call.ArgumentsJson + " ? [y/N]");
				string? answer = Console.ReadLine();
				return Task.FromResult(string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase));
			}).GetAwaiter().GetResult();
			foreach (string line in result.Trace) Console.WriteLine("  tool " + line);
			Console.WriteLine(result.Text);
			return 0;
		}
		case "chat":
		{
			List<string> rest = args.Skip(1).ToList();
			if (rest.Count < 2) throw new ArgumentException("usage: nova-cli ai chat <name> <text>");
			AiProvider provider = AiStore.Find(_paths, rest[0]) ?? throw new ArgumentException("unknown provider " + rest[0]);
			string key = AiStore.GetKey(_paths, rest[0]) ?? throw new ArgumentException("no key stored for " + rest[0]);
			string text = string.Join(" ", rest.Skip(1));
			int maxTokens = int.TryParse(Option("--max-tokens") ?? "512", out int mt) ? mt : 512;
			int timeout = int.TryParse(Option("--timeout") ?? "120", out int to) ? to : 120;
			using AiClient client = new AiClient(timeout);
			AiReply reply = client.ChatAsync(provider, key, Option("--system"), text, maxTokens).GetAwaiter().GetResult();
			Row("model", reply.Model);
			Console.WriteLine(reply.Text);
			return 0;
		}
		default:
			throw new ArgumentException("usage: nova-cli ai <providers|add|key|remove|models|chat>");
		}
	}

	private static string ReadSecretLine()
	{
		string? value;
		if (Console.IsInputRedirected)
		{
			value = Console.ReadLine();
		}
		else
		{
			StringBuilder secret = new StringBuilder();
			while (true)
			{
				ConsoleKeyInfo key = Console.ReadKey(true);
				if (key.Key == ConsoleKey.Enter) break;
				if (key.Key == ConsoleKey.Backspace) { if (secret.Length > 0) secret.Remove(secret.Length - 1, 1); continue; }
				secret.Append(key.KeyChar);
			}
			Console.WriteLine();
			value = secret.ToString();
		}
		value = (value ?? string.Empty).Trim();
		if (value.Length == 0) throw new ArgumentException("empty API key");
		return value;
	}

	private static int Help()
	{
		Header("nova-cli - Minecraft pack launcher (core)");
		Console.WriteLine("usage: nova-cli <command> [options]\n\ncommands\n  paths                     show the portable folder layout\n  java                      list detected Java installations\n  instances                 list instances\n  install <version>         download a version json + client jar from Mojang\n  add                       create an instance\n  show <id>                 print one instance\n  doctor <id> [--deep]      health check (read-only)\n  repair <id>               download/verify everything that is missing\n  backup <id> [--world n]   back up changed worlds (or one world) to backups\\\n  backups <id>              list backups of an instance\n  restore <id> [--file f]   restore a backup next to the world (nothing is deleted)\n  crash <id>                analyse the newest crash report\n  pack inspect <file>       read a .mrpack / pack zip without installing it\n  pack import <file>        install a pack into its own instance (--id, --name)\n  pack export <id>          write a pack zip (--out, --worlds)\n  forge install <mc> <ver>  download + run the official Forge installer\n  store search <id> <text>  search the mod store for this instance\n  store add <id> <slug>     download a mod (and its dependencies) into the pack\n  mods <id> [--on f|--off f]  list or toggle mods (renames to .disabled)\n  versions                  releases from 1.8 + available mod loaders\n  account                   saved accounts (offline / microsoft) and the active one\n  account offline <name>    add an offline (no internet) player\n  account ms [--client-id]  sign in with a real Microsoft account (device code)\n  account use <id>          switch the active account\n  new <mc> [--forge b]      download a version and create a fresh instance for it\n  clone <id> <name>         duplicate an instance (mods included)\n  plan <id>                 dry-run: build the launch command, run nothing\n  launch <id> [--force]     check, then start the game\n  ai providers              list configured AI providers (keys stay DPAPI-encrypted)\n  ai add <name> <openai|anthropic> <baseUrl> <models,csv>   save provider, read API key from stdin\n  ai key <name>             replace the stored API key of a provider\n  ai remove <name>          delete a provider and its stored key\n  ai models <name>          list models the provider exposes\n  ai chat <name> <text>     one-shot chat (--system, --max-tokens, --timeout)\n  ai agent <name> <task>    supervised agent: read-only tools auto, write tools ask [y/N]\n\noptions\n  --root <dir>              portable root (default: next to nova.exe)\n  --name --game --version --versions --libraries --assets --java --memory --player");
		return 0;
	}

	private static int ShowPaths()
	{
		Header("Portable layout");
		Row("root", _paths.Root);
		Row("versions", _paths.Versions);
		Row("libraries", _paths.Libraries);
		Row("assets", _paths.Assets);
		Row("natives", _paths.Natives);
		Row("instances", _paths.Instances);
		return 0;
	}

	private static int ShowJava(List<string> positional)
	{
		JavaLocator.RegisterRoot(_paths.JavaHome);
		bool flag;
		switch (positional.FirstOrDefault())
		{
		case "install":
		case "add":
		case "get":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			if (!int.TryParse(positional.Skip(1).FirstOrDefault() ?? Option("--major") ?? "8", out var result))
			{
				throw new ArgumentException("usage: nova java install <major>");
			}
			Header("Java install - " + result);
			Progress<string> log = new Progress<string>((string line) =>
			{
				Console.WriteLine("  " + line);
			});
			JavaInstall result2 = JavaProvisioner.EnsureAsync(_paths, result, log).GetAwaiter().GetResult();
			Console.WriteLine();
			Row("java", ((object)result2 == null) ? "failed" : (result2.RawVersion + "  " + result2.Path));
			Console.WriteLine();
			return ((object)result2 == null) ? 1 : 0;
		}
		Header("Java installations");
		IReadOnlyList<JavaInstall> readOnlyList = JavaLocator.Discover(refresh: true);
		if (readOnlyList.Count == 0)
		{
			Warn("none found - run: nova java install 8");
			return 1;
		}
		foreach (JavaInstall item in readOnlyList)
		{
			Console.WriteLine($"  Java {item.Major,-2}  {item.RawVersion,-12} [{item.Source}] {item.Path}");
		}
		return 0;
	}

	private static int Install(List<string> positional)
	{
		string text = positional.FirstOrDefault((string v) => !v.StartsWith("--")) ?? throw new ArgumentException("version id is required (e.g. nova install 1.20.1)");
		Header("Install - " + text);
		Progress<string> log = new Progress<string>((string line) =>
		{
			Console.WriteLine("  " + line);
		});
		string result = MojangMeta.InstallAsync(text, _paths, log).GetAwaiter().GetResult();
		Row("version json", result);
		Console.WriteLine();
		Write("  next: create an instance for it, then run repair", ConsoleColor.DarkGray);
		Console.WriteLine();
		return 0;
	}

	private static int ShowInstances()
	{
		Header("Instances");
		List<InstanceConfig> list = new InstanceStore(_paths).All();
		if (list.Count == 0)
		{
			Warn("no instances yet - use: nova add --name \"My Pack\" --game D:\\pack --version 1.12.2-forge-14.23.5.2860");
			return 0;
		}
		foreach (InstanceConfig item in list)
		{
			Console.WriteLine($"  {item.Id,-24} {item.Name,-24} {item.VersionId,-30} {item.MemoryMb} MB  {item.GameDirectory}");
		}
		return 0;
	}

	private static int AddInstance()
	{
		string name = Option("--name") ?? throw new ArgumentException("--name is required");
		string text = Option("--game") ?? throw new ArgumentException("--game is required");
		string versionId = Option("--version") ?? throw new ArgumentException("--version is required");
		InstanceStore instanceStore = new InstanceStore(_paths);
		string text2 = (Options.ContainsKey("--id") ? Option("--id") : InstanceStore.Slug(name));
		if (instanceStore.Find(text2) != null)
		{
			throw new ArgumentException("instance '" + text2 + "' already exists");
		}
		string path = Path.Combine(text, "mods");
		int modCount = (Directory.Exists(path) ? Directory.GetFiles(path, "*.jar").Length : 0);
		InstanceConfig instanceConfig = new InstanceConfig
		{
			Id = text2,
			Name = name,
			GameDirectory = Path.GetFullPath(text),
			VersionId = versionId,
			VersionHome = Option("--versions"),
			LibrariesHome = Option("--libraries"),
			AssetsDirectory = Option("--assets"),
			JavaPath = Option("--java"),
			MemoryMb = (int.TryParse(Option("--memory"), out var result) ? result : LaunchBuilder.RecommendMemoryMb(modCount)),
			PlayerName = (Option("--player") ?? "Player"),
			BackupWorldsBeforeLaunch = Options.ContainsKey("--worlds-backup")
		};
		if (Options.ContainsKey("--jvm"))
		{
			instanceConfig.JvmArgs.AddRange(Option("--jvm").Split(' ', StringSplitOptions.RemoveEmptyEntries));
		}
		instanceStore.Save(instanceConfig);
		Header("Instance created");
		Row("id", instanceConfig.Id);
		Row("game", instanceConfig.GameDirectory);
		Row("version", instanceConfig.VersionId);
		Row("mods found", modCount.ToString());
		Row("memory", instanceConfig.MemoryMb + " MB");
		Row("config", instanceStore.ConfigFileFor(text2));
		Console.WriteLine();
		Console.WriteLine("  next:  nova doctor " + instanceConfig.Id);
		return 0;
	}

	private static int ShowInstance(List<string> positional)
	{
		InstanceConfig instanceConfig = Require(positional);
		Header(instanceConfig.Name + "  (" + instanceConfig.Id + ")");
		Row("game", instanceConfig.GameDirectory);
		Row("version", instanceConfig.VersionId);
		Row("versions home", instanceConfig.VersionHome ?? "(portable)");
		Row("libraries home", instanceConfig.LibrariesHome ?? "(portable)");
		Row("assets", instanceConfig.AssetsDirectory ?? "(portable)");
		Row("java", instanceConfig.JavaPath ?? "(auto)");
		Row("memory", instanceConfig.MemoryMb + " MB");
		Row("player", instanceConfig.PlayerName + "  uuid " + LaunchBuilder.OfflineUuid(instanceConfig.PlayerName));
		Row("world backup", instanceConfig.BackupWorldsBeforeLaunch ? "on" : "off");
		Row("last exit", instanceConfig.LastExitSummary ?? "-");
		return 0;
	}

	private static int Check(List<string> positional)
	{
		InstanceConfig instanceConfig = Require(positional);
		DoctorReport result = Doctor.RunAsync(instanceConfig, _paths, Options.ContainsKey("--deep")).GetAwaiter().GetResult();
		Header("Doctor - " + instanceConfig.Name);
		foreach (CheckItem item in result.Items)
		{
			var (text, color) = item.Status switch
			{
				CheckStatus.Ok => (" OK ", ConsoleColor.DarkGreen), 
				CheckStatus.Warn => ("WARN", ConsoleColor.Yellow), 
				CheckStatus.Fail => ("FAIL", ConsoleColor.Red), 
				_ => ("SKIP", ConsoleColor.DarkGray), 
			};
			Write(text, color);
			Console.Write($" {item.Area,-12} {item.Message}");
			if (item.Suggestion != null)
			{
				Write("  -> " + item.Suggestion, ConsoleColor.DarkGray);
			}
			Console.WriteLine();
		}
		Console.WriteLine();
		if (result.Healthy)
		{
			Write($"  healthy - {result.Warns} warning(s)", ConsoleColor.Green);
		}
		else
		{
			Write($"  {result.Fails} problem(s), {result.Warns} warning(s) - run: nova repair {instanceConfig.Id}", ConsoleColor.Red);
		}
		Console.WriteLine();
		return (!result.Healthy) ? 1 : 0;
	}

	private static int Repair(List<string> positional)
	{
		InstanceConfig instanceConfig = Require(positional);
		Header("Repair - " + instanceConfig.Name);
		Progress<string> log = new Progress<string>((string line) =>
		{
			Console.WriteLine("  " + line);
		});
		foreach (string item in Doctor.RepairAsync(instanceConfig, _paths, log).GetAwaiter().GetResult())
		{
			Console.WriteLine("  " + item);
		}
		DoctorReport result = Doctor.RunAsync(instanceConfig, _paths, deep: true).GetAwaiter().GetResult();
		Console.WriteLine();
		Write($"  after repair: {result.Fails} problem(s), {result.Warns} warning(s)", (result.Fails == 0) ? ConsoleColor.Green : ConsoleColor.Red);
		Console.WriteLine();
		return (result.Fails != 0) ? 1 : 0;
	}

	private static int AccountCommand(List<string> positional)
	{
		AccountStore accountStore = new AccountStore(_paths);
		LauncherSettings launcherSettings = LauncherSettings.Load(_paths);
		string text = positional.FirstOrDefault()?.ToLowerInvariant();
		if ((text == "offline" || text == "add") ? true : false)
		{
			NovaAccount novaAccount = NovaAccount.CreateOffline(positional.Skip(1).FirstOrDefault() ?? Option("--name") ?? throw new ArgumentException("usage: nova account offline <player name>"));
			if (accountStore.Find(novaAccount.Id) != null) throw new ArgumentException("That offline account already exists; use 'account use' instead.");
			accountStore.Save(novaAccount);
			launcherSettings.ActiveAccountId = novaAccount.Id;
			launcherSettings.Save(_paths);
			Header("Offline account added");
			Row("player", novaAccount.Name);
			Row("uuid", novaAccount.Uuid);
			Row("active", "yes");
			Console.WriteLine();
			return 0;
		}
		bool flag;
		switch (text)
		{
		case "ms":
		case "microsoft":
		case "online":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			string text2 = Option("--client-id") ?? launcherSettings.MicrosoftClientId;
			if (!MicrosoftAuth.LooksLikeClientId(text2))
			{
				Warn("a Microsoft client id is required (free Azure app, see the docs)");
				Write("  set it once with: nova account ms --client-id <guid>", ConsoleColor.DarkGray);
				return 2;
			}
			Header("Microsoft sign-in");
			using MicrosoftAuth microsoftAuth = new MicrosoftAuth();
			DeviceCodeInfo result = microsoftAuth.StartDeviceCodeAsync(text2).GetAwaiter().GetResult();
			Console.WriteLine();
			Row("open", result.VerificationUri);
			Row("code", result.UserCode);
			Console.WriteLine();
			Write("  waiting for the code to be approved…", ConsoleColor.DarkGray);
			Console.WriteLine();
			Progress<string> log = new Progress<string>((string line) =>
			{
				Console.WriteLine("  " + line);
			});
			NovaAccount result2 = microsoftAuth.WaitForSignInAsync(text2, result, log).GetAwaiter().GetResult();
			if (result2 == null)
			{
				Fail("sign-in did not complete (expired or declined)");
				return 1;
			}
			accountStore.Save(result2);
			launcherSettings.ActiveAccountId = result2.Id;
			launcherSettings.MicrosoftClientId = text2;
			launcherSettings.Save(_paths);
			Header("Signed in");
			Row("player", result2.Name);
			Row("uuid", result2.Uuid);
			Row("online play", "ready");
			Console.WriteLine();
			return 0;
		}
		switch (text)
		{
		case "use":
		{
			string text3 = positional.Skip(1).FirstOrDefault() ?? Option("--id") ?? throw new ArgumentException("usage: nova account use <id>");
			NovaAccount novaAccount2 = accountStore.Find(text3) ?? throw new ArgumentException("no account with id " + text3);
			launcherSettings.ActiveAccountId = novaAccount2.Id;
			launcherSettings.Save(_paths);
			Row("active", novaAccount2.Name + "  (" + novaAccount2.KindLabel + ")");
			return 0;
		}
		case "remove":
		case "logout":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			string text4 = positional.Skip(1).FirstOrDefault() ?? throw new ArgumentException("usage: nova account remove <id>");
			accountStore.Remove(text4);
			if (string.Equals(launcherSettings.ActiveAccountId, text4, StringComparison.OrdinalIgnoreCase))
			{
				launcherSettings.ActiveAccountId = null;
				launcherSettings.Save(_paths);
			}
			Row("removed", text4);
			return 0;
		}
		Header("Accounts");
		List<NovaAccount> list = accountStore.All();
		if (list.Count == 0)
		{
			Warn("none saved - nova account offline <name>");
		}
		NovaAccount novaAccount3 = accountStore.Active(launcherSettings);
		foreach (NovaAccount item in list)
		{
			string value = (string.Equals(item.Id, novaAccount3.Id, StringComparison.OrdinalIgnoreCase) ? "*" : " ");
			Console.WriteLine($"  {value} {item.Id,-40} {item.Name,-18} {item.KindLabel,-10} {item.Uuid}");
		}
		Console.WriteLine();
		Row("client id", string.IsNullOrWhiteSpace(launcherSettings.MicrosoftClientId) ? "not set (Microsoft sign-in disabled)" : launcherSettings.MicrosoftClientId);
		Console.WriteLine();
		return 0;
	}

	private static int VersionsCommand()
	{
		Header("Versions · 1.8 → latest");
		Progress<string> log = new Progress<string>((string line) =>
		{
			Console.WriteLine("  " + line);
		});
		List<MinecraftVersion> result = VersionLibrary.GetMinecraftVersionsAsync(_paths, log).GetAwaiter().GetResult();
		foreach (MinecraftVersion item in result)
		{
			string value = (item.Installed ? "*" : " ");
			Console.WriteLine($"  {value} {item.Id,-9} {item.ReleaseLabel}  {item.ForgeLabel}");
		}
		Console.WriteLine();
		Row("releases", result.Count.ToString());
		Row("installed", Enumerable.Count(result, (MinecraftVersion v) => v.Installed).ToString());
		Row("with forge", Enumerable.Count(result, (MinecraftVersion v) => v.HasForge).ToString());
		Console.WriteLine();
		return 0;
	}

	private static int NewInstanceCommand(List<string> positional)
	{
		string mc = positional.FirstOrDefault() ?? throw new ArgumentException("usage: nova new <mcVersion> [--forge recommended|latest|<build>] [--name \"My pack\"] [--memory 4096] [--play]");
		string text = Option("--forge");
		bool flag;
		switch (text)
		{
		case "recommended":
		case "latest":
			flag = true;
			goto IL_0054;
		default:
			flag = false;
			goto IL_0054;
		case null:
			break;
			IL_0054:
			if (flag)
			{
				MinecraftVersion minecraftVersion = VersionLibrary.GetMinecraftVersionsAsync(_paths).GetAwaiter().GetResult()
					.FirstOrDefault((MinecraftVersion v) => v.Id.Equals(mc, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException($"'{mc}' is not a supported release from 1.8 onward");
				text = ((text == "recommended") ? minecraftVersion.ForgeRecommended : minecraftVersion.ForgeLatest);
				if (text == null)
				{
					throw new ArgumentException("no forge build is published for " + mc);
				}
			}
			break;
		}
		string versionId = ((text == null) ? mc : (mc + "-forge-" + text));
		string text2 = Option("--name") ?? ((text == null) ? ("Minecraft " + mc) : (mc + " Forge " + text));
		int memoryMb = (int.TryParse(Option("--memory"), out var result) ? result : 4096);
		Header("New instance · " + text2);
		Progress<string> log = new Progress<string>((string line) =>
		{
			Console.WriteLine("  " + line);
		});
		InstanceConfig result2 = InstanceFactory.CreateAsync(_paths, text2, versionId, memoryMb, log).GetAwaiter().GetResult();
		JavaInstall result3 = JavaProvisioner.EnsureAsync(_paths, VersionResolver.GuessJavaMajor(mc), log).GetAwaiter().GetResult();
		if ((object)result3 != null)
		{
			result2.JavaPath = result3.Path;
		}
		new InstanceStore(_paths).Save(result2);
		Row("instance", result2.Id);
		Row("java", ((object)result3 == null) ? "not found" : result3.RawVersion);
		Row("version", result2.VersionId);
		Row("folder", result2.GameDirectory);
		if (Options.ContainsKey("--repair"))
		{
			Console.WriteLine();
			Row("repair", "downloading libraries + assets (this is the big one)");
			Doctor.RepairAsync(result2, _paths, log).GetAwaiter().GetResult();
		}
		if (Options.ContainsKey("--play"))
		{
			Console.WriteLine();
			Launch(new List<string> { result2.Id });
		}
		Console.WriteLine();
		return 0;
	}

	private static int CloneCommand(List<string> positional)
	{
		if (positional.Count < 2)
		{
			throw new ArgumentException("usage: nova clone <instance-id> <new name> [--no-copy]");
		}
		InstanceConfig instanceConfig = Require(positional.Take(1).ToList());
		string text = string.Join(" ", positional.Skip(1));
		Header("Clone · " + instanceConfig.Name + " -> " + text);
		Progress<string> log = new Progress<string>((string line) =>
		{
			Console.WriteLine("  " + line);
		});
		bool flag = !Options.ContainsKey("--no-copy");
		InstanceConfig result = InstanceFactory.DuplicateAsync(_paths, instanceConfig, text, flag, log).GetAwaiter().GetResult();
		Row("instance", result.Id);
		Row("version", result.VersionId);
		Row("folder", result.GameDirectory);
		Row("game files", flag ? "copied" : "not copied");
		Console.WriteLine();
		return 0;
	}

	private static int ModsCommand(List<string> positional)
	{
		InstanceConfig instanceConfig = Require(positional);
		string modsDirectory = Path.Combine(instanceConfig.GameDirectory, "mods");
		string text = Option("--on");
		string text2 = Option("--off");
		if (text != null || text2 != null)
		{
			string text3 = text ?? text2;
			bool flag = text != null;
			Header((flag ? "Enable " : "Disable ") + text3);
			bool flag2 = ModManager.SetEnabled(modsDirectory, text3, flag);
			if (!flag2)
			{
				Fail("could not change " + text3);
			}
			else
			{
				Row("result", (flag ? "enabled " : "disabled ") + ModManager.DisplayName(text3));
			}
			Console.WriteLine();
			return (!flag2) ? 1 : 0;
		}
		if (Options.ContainsKey("--update") || Options.ContainsKey("--check-updates"))
		{
			bool apply = Options.ContainsKey("--update");
			Header((apply ? "Update mods - " : "Check mod updates - ") + instanceConfig.Name);
			Progress<string> log = new Progress<string>((string l) => Console.WriteLine("  " + l));
			Dictionary<string, ModUpdateCandidate> updates = ModUpdater.CheckUpdatesAsync(modsDirectory, ModStore.McVersionOf(instanceConfig.VersionId), ModStore.LoaderOf(instanceConfig.VersionId), log).GetAwaiter().GetResult();
			if (apply && updates.Count > 0)
			{
				Console.WriteLine("  Creating a verified snapshot of mod files before updates...");
				string snapshot = ModSnapshot.CreateAsync(modsDirectory).GetAwaiter().GetResult();
				Row("mod snapshot", snapshot);
			}
			foreach (ModUpdateCandidate cand in updates.Values)
			{
				Console.WriteLine($"  • {cand.CurrentFileName} -> {cand.NewFileName} ({cand.LatestVersionNumber})");
				Console.WriteLine("    changelog: " + (string.IsNullOrWhiteSpace(cand.Changelog) ? "not provided by Modrinth" : cand.Changelog));
				if (apply)
				{
					ModUpdater.ApplyUpdateAsync(modsDirectory, cand).GetAwaiter().GetResult();
				}
			}
			Console.WriteLine();
			Row("updates found", updates.Count.ToString());
			if (apply && updates.Count > 0)
			{
				Row("updated", updates.Count.ToString());
			}
			Console.WriteLine();
			return 0;
		}
		Header("Mods - " + instanceConfig.Name);
		ModScanResult modScanResult = ModScanner.Scan(modsDirectory);
		foreach (ModEntry item in modScanResult.Mods.OrderBy((ModEntry m) => m.Name ?? m.FileName, StringComparer.OrdinalIgnoreCase))
		{
			Console.WriteLine("  " + (item.Enabled ? "[x]" : "[ ]") + " " + item.FileName);
		}
		Console.WriteLine();
		Row("enabled", Enumerable.Count(modScanResult.Mods, (ModEntry m) => m.Enabled).ToString());
		Row("disabled", Enumerable.Count(modScanResult.Mods, (ModEntry m) => !m.Enabled).ToString());
		Console.WriteLine();
		return 0;
	}

	private static int StoreCommand(List<string> positional)
	{
		string text = positional.FirstOrDefault()?.ToLowerInvariant() ?? "search";
		List<string> list = (from v in positional.Skip(1)
			where !v.StartsWith("--")
			select v).ToList();
		StoreSource source = (string.Equals(Option("--source") ?? "modrinth", "curseforge", StringComparison.OrdinalIgnoreCase) ? StoreSource.CurseForge : StoreSource.Modrinth);
		LauncherSettings launcherSettings = LauncherSettings.Load(_paths);
		bool flag;
		switch (text)
		{
		case "search":
		{
			if (list.Count < 2)
			{
				throw new ArgumentException("usage: nova-cli store search <instance-id> <text> [--source curseforge]");
			}
			InstanceConfig instanceConfig = Require(list.Take(1).ToList());
			string text2 = string.Join(" ", list.Skip(1));
			string text3 = ModStore.McVersionOf(instanceConfig.VersionId);
			string text4 = ModStore.LoaderOf(instanceConfig.VersionId);
			int num = (int.TryParse(Option("--offset"), out var result) ? result : 0);
			string text5 = Option("--sort") ?? "downloads";
			Header($"Store · {text2} · for {text3} {text4} · {text5} · offset {num}");
			using ModStore modStore = new ModStore(launcherSettings.CurseForgeKey);
			List<StoreMod> result2 = modStore.SearchAsync(source, text2, text3, text4, 15, default, num, text5).GetAwaiter().GetResult();
			if (!string.IsNullOrWhiteSpace(modStore.LastSearchNote))
			{
				Warn(modStore.LastSearchNote);
			}
			foreach (StoreMod item in result2)
			{
				Console.WriteLine("  " + item.Name.PadRight(42) + item.Author.PadRight(18) + item.DownloadsLabel.PadLeft(7) + "  " + item.Slug);
			}
			if (result2.Count == 0)
			{
				Warn("nothing found for this version");
			}
			Console.WriteLine();
			return 0;
		}
		case "add":
		case "install":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			if (list.Count < 2)
			{
				throw new ArgumentException("usage: nova-cli store add <instance-id> <slug> [--no-deps]");
			}
			InstanceConfig instanceConfig2 = Require(list.Take(1).ToList());
			string slug = list[1];
			string text6 = ModStore.McVersionOf(instanceConfig2.VersionId);
			string text7 = ModStore.LoaderOf(instanceConfig2.VersionId);
			Header("Store install · " + slug + " · " + instanceConfig2.Name);
			Progress<string> log = new Progress<string>((string line) =>
			{
				Console.WriteLine("  " + line);
			});
			using ModStore modStore2 = new ModStore(launcherSettings.CurseForgeKey);
			List<StoreMod> result3 = modStore2.SearchAsync(source, slug, text6, text7, 10).GetAwaiter().GetResult();
			StoreMod storeMod = result3.FirstOrDefault((StoreMod m) => string.Equals(m.Slug, slug, StringComparison.OrdinalIgnoreCase)) ?? result3.FirstOrDefault();
			if ((object)storeMod == null)
			{
				Fail("no mod named " + slug + " for " + text6 + " " + text7);
				return 2;
			}
			Row("mod", storeMod.Name + "  (" + storeMod.Author + ")");
			ModInstallResult result4 = modStore2.InstallAsync(storeMod, instanceConfig2, !Options.ContainsKey("--no-deps"), log).GetAwaiter().GetResult();
			Console.WriteLine();
			Row("result", result4.Summary);
			foreach (string item2 in result4.Installed)
			{
				Console.WriteLine("    + " + item2);
			}
			foreach (string item3 in result4.Failed)
			{
				Warn(item3);
			}
			Console.WriteLine();
			return (result4.Failed.Count != 0) ? 1 : 0;
		}
		Fail("unknown store command: " + text);
		return 2;
	}

	private static int SelfUpdateCommand(List<string> positional)
	{
		string sub = positional.FirstOrDefault()?.ToLowerInvariant() ?? "check";
		LauncherSettings settings = LauncherSettings.Load(_paths);
		string repo = Option("--repo") ?? settings.UpdateRepo ?? LauncherUpdater.DefaultRepo;
		Header($"Nova Self-Updater · v{LauncherUpdater.CurrentVersion}");
		Row("current version", LauncherUpdater.CurrentVersion);
		Row("github repo", repo);

		bool force = Options.ContainsKey("--force");
		LauncherUpdateInfo? info = LauncherUpdater.CheckForUpdateAsync(repo, includeSameOrOlder: force).GetAwaiter().GetResult();
		if (info == null)
		{
			Row("status", "up to date (no newer GitHub release)");
			Console.WriteLine();
			return 0;
		}
		Row("latest release", $"{info.TagName} ({info.Title})");
		Row("package", info.AssetName ?? "none");
		if (sub == "apply" || Options.ContainsKey("--apply"))
		{
			Progress<string> log = new Progress<string>((string l) => Console.WriteLine("  " + l));
			string staged = LauncherUpdater.StageUpdateAsync(_paths, info, log).GetAwaiter().GetResult();
			// Update the running launcher's bin, not the CLI data root (which may be bin/data).
			string executableDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string targetBin = Path.GetFileName(executableDir).Equals("bin", StringComparison.OrdinalIgnoreCase)
				? executableDir
				: Path.Combine(_paths.Root, "bin");
			LauncherUpdater.LaunchDetachedSwapper(staged, targetBin, Environment.ProcessId, relaunchGui: false);
			Row("status", "installer started; check cache/launcher-update/last-update-result.txt for the final result");
		}
		Console.WriteLine();
		return 0;
	}

	private static int FabricCommand(List<string> positional)
	{
		string sub = positional.FirstOrDefault()?.ToLowerInvariant() ?? "install";
		if (sub != "install" && sub != "add")
		{
			Fail("unknown fabric command: " + sub);
			Console.WriteLine("  usage: nova-cli fabric install <minecraft-version> [loader-version]");
			return 2;
		}
		string mc = positional.Skip(1).FirstOrDefault() ?? throw new ArgumentException("usage: nova-cli fabric install <minecraft-version> [loader-version]");
		string? loaderVer = positional.Skip(2).FirstOrDefault();
		Header("Fabric · " + mc);
		Progress<string> log = new Progress<string>((string line) => Console.WriteLine("  " + line));
		string installedId = FabricInstaller.InstallAsync(mc, loaderVer, _paths, log).GetAwaiter().GetResult();
		Console.WriteLine();
		Row("version id", installedId);
		Row("versions folder", _paths.Versions);
		Console.WriteLine();
		return 0;
	}

	private static int NeoForgeCommand(List<string> positional)
	{
		string sub = positional.FirstOrDefault()?.ToLowerInvariant() ?? "install";
		if (sub != "install" && sub != "add")
		{
			Fail("unknown neoforge command: " + sub);
			Console.WriteLine("  usage: nova-cli neoforge install <minecraft-version> [neoforge-version]");
			return 2;
		}
		string mc = positional.Skip(1).FirstOrDefault() ?? throw new ArgumentException("usage: nova-cli neoforge install <minecraft-version> [neoforge-version]");
		string? neoVer = positional.Skip(2).FirstOrDefault();
		Header("NeoForge · " + mc);
		Progress<string> log = new Progress<string>((string line) => Console.WriteLine("  " + line));
		string installedId = NeoForgeInstaller.InstallAsync(mc, neoVer, _paths, log).GetAwaiter().GetResult();
		Console.WriteLine();
		Row("version id", installedId);
		Row("versions folder", _paths.Versions);
		Console.WriteLine();
		return 0;
	}

	private static int ForgeCommand(List<string> positional)
	{
		string text = positional.FirstOrDefault()?.ToLowerInvariant() ?? "install";
		if (text == "list" || text == "builds")
		{
			string mcVer = positional.Skip(1).FirstOrDefault() ?? throw new ArgumentException("usage: nova-cli forge list <minecraft-version>");
			Header("Forge builds · " + mcVer);
			List<string> builds = VersionLibrary.GetForgeBuildsAsync(mcVer).GetAwaiter().GetResult();
			foreach (string b in builds.Take(30))
			{
				Console.WriteLine("  " + b);
			}
			Row("total builds", builds.Count.ToString());
			Console.WriteLine();
			return 0;
		}
		if (!(text == "install") && !(text == "add"))
		{
			Fail("unknown forge command: " + text);
			Console.WriteLine("  usage: nova-cli forge <install|list> <minecraft-version> [forge-version]");
			return 2;
		}
		List<string> list = (from v in positional.Skip(1)
			where !v.StartsWith("--")
			select v).ToList();
		if (list.Count < 2)
		{
			throw new ArgumentException("usage: nova-cli forge install <minecraft-version> <forge-version>");
		}
		Header("Forge " + list[0] + "-" + list[1]);
		Progress<string> log = new Progress<string>((string line) =>
		{
			Console.WriteLine("  " + line);
		});
		string result = ForgeInstaller.InstallAsync(list[0], list[1], _paths, log).GetAwaiter().GetResult();
		Console.WriteLine();
		Row("version id", result);
		Row("versions folder", _paths.Versions);
		Console.WriteLine();
		return 0;
	}

	private static int PackCommand(List<string> positional)
	{
		string text = positional.FirstOrDefault()?.ToLowerInvariant() ?? "inspect";
		string text2 = positional.Skip(1).FirstOrDefault();
		switch (text)
		{
		case "inspect":
		case "info":
		{
			if (text2 == null)
			{
				throw new ArgumentException("usage: nova pack inspect <file.mrpack|file.zip>");
			}
			PackInfo packInfo = PackInspector.Inspect(text2);
			Header("Pack - " + Path.GetFileName(text2));
			Row("format", packInfo.FormatName);
			Row("name", packInfo.Name);
			Row("minecraft", string.IsNullOrWhiteSpace(packInfo.McVersion) ? "?" : packInfo.McVersion);
			Row("loader", (packInfo.Loader == null) ? "vanilla" : (packInfo.Loader + " " + packInfo.LoaderVersion));
			Row("listed files", packInfo.ListedFiles.ToString());
			Row("listed size", ((double)packInfo.ListedBytes / 1024.0 / 1024.0).ToString("F1") + " MB");
			Row("override files", packInfo.OverrideFiles.ToString());
			Row("can download all", packInfo.CanDownloadEverything ? "yes" : "no");
			foreach (string note in packInfo.Notes)
			{
				Warn(note);
			}
			Console.WriteLine();
			if (packInfo.Format != PackFormat.Unknown)
			{
				return 0;
			}
			return 2;
		}
		case "import":
		{
			if (text2 == null)
			{
				throw new ArgumentException("usage: nova pack import <file> [--id name] [--name \"Display name\"]");
			}
			Header("Import - " + Path.GetFileName(text2));
			Progress<string> log2 = new Progress<string>((string line) =>
			{
				Console.WriteLine("  " + line);
			});
			PackImportResult result = PackImporter.ImportAsync(text2, _paths, Option("--id"), Option("--name"), log2).GetAwaiter().GetResult();
			Console.WriteLine();
			Row("instance", result.InstanceId);
			Row("name", result.Name);
			Row("game folder", result.GameDirectory);
			Row("version", result.VersionId);
			Row("files", result.Summary);
			foreach (string note2 in result.Notes)
			{
				Write("  - " + note2, ConsoleColor.DarkGray);
			}
			if (result.Missing.Count > 0)
			{
				Warn($"{result.Missing.Count} file(s) missing:");
				foreach (string item in result.Missing.Take(10))
				{
					Console.WriteLine("      " + item);
				}
			}
			Console.WriteLine();
			Write("  next: nova doctor " + result.InstanceId + "   (installs the version, libraries and assets)", ConsoleColor.Green);
			Console.WriteLine();
			return (result.FilesFailed != 0 || result.SetupFailed) ? 1 : 0;
		}
		case "export":
		{
			if (text2 == null)
			{
				throw new ArgumentException("usage: nova pack export <instance-id> [--out <file.zip>] [--worlds]");
			}
			InstanceConfig instanceConfig = Require(positional.Skip(1).ToList());
			string outputZip = Option("--out") ?? Path.Combine(_paths.Root, PackImporter.MakeId(instanceConfig.Name) + ".zip");
			bool flag = Options.ContainsKey("--worlds");
			Header("Export - " + instanceConfig.Name);
			Progress<string> log = new Progress<string>((string line) =>
			{
				Console.WriteLine("  " + line);
			});
			var (value, num, num2) = PackExporter.Export(instanceConfig, outputZip, flag, log);
			Console.WriteLine();
			Row("archive", value);
			Row("files", num.ToString());
			Row("size", ((double)num2 / 1024.0 / 1024.0).ToString("F1") + " MB");
			Row("worlds", flag ? "included" : "left out (use --worlds)");
			Console.WriteLine();
			return 0;
		}
		default:
			Fail("unknown pack command: " + text);
			Console.WriteLine("  usage: nova pack <inspect|import|export> <file|instance> [options]");
			return 2;
		}
	}

	private static int Backup(List<string> positional)
	{
		InstanceConfig instanceConfig = Require(positional);
		Header("Backup - " + instanceConfig.Name);
		Progress<string> log = new Progress<string>((string line) =>
		{
			Console.WriteLine("  " + line);
		});
		string text = Option("--world");
		List<BackupInfo> list = new List<BackupInfo>();
		if (text != null)
		{
			list.Add(WorldBackup.Create(_paths, instanceConfig, text, log));
		}
		else
		{
			list.AddRange(WorldBackup.CreateChanged(_paths, instanceConfig, log));
		}
		Console.WriteLine();
		if (list.Count == 0)
		{
			Write("  nothing changed since the last backup", ConsoleColor.DarkGray);
		}
		else
		{
			Write($"  {list.Count} backup(s) written to {WorldBackup.BackupRoot(_paths, instanceConfig.Id)}", ConsoleColor.Green);
		}
		if (Options.ContainsKey("--prune"))
		{
			int num = (int.TryParse(Option("--prune"), out var result) ? result : 5);
			List<BackupInfo> list2 = WorldBackup.Prune(_paths, instanceConfig.Id, num, apply: true, log);
			Write($"  pruned {list2.Count} old backup(s), keeping {num} per world", ConsoleColor.DarkGray);
		}
		Console.WriteLine();
		return 0;
	}

	private static int ShowBackups(List<string> positional)
	{
		InstanceConfig instanceConfig = Require(positional);
		Header("Backups - " + instanceConfig.Name);
		List<BackupInfo> list = WorldBackup.List(_paths, instanceConfig.Id);
		if (list.Count == 0)
		{
			Warn("no backups yet - run: nova backup " + instanceConfig.Id);
			return 0;
		}
		Console.WriteLine($"  {"#",-3} {"world",-34} {"created",-17} {"size",9}  integrity");
		for (int i = 0; i < list.Count; i++)
		{
			BackupInfo backupInfo = list[i];
			bool flag = WorldBackup.Verify(backupInfo, out string message);
			Console.Write($"  {i + 1,-3} {Trim(backupInfo.WorldName, 34),-34} {backupInfo.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm} {backupInfo.Megabytes,7} MB  ");
			Write(flag ? "ok" : message, flag ? ConsoleColor.DarkGreen : ConsoleColor.Red);
			Console.WriteLine();
		}
		Console.WriteLine();
		Row("location", WorldBackup.BackupRoot(_paths, instanceConfig.Id));
		Row("worlds in saves", WorldBackup.ListWorlds(instanceConfig.GameDirectory).Count.ToString());
		Console.WriteLine();
		return 0;
	}

	private static int Restore(List<string> positional)
	{
		InstanceConfig instanceConfig = Require(positional);
		List<BackupInfo> list = WorldBackup.List(_paths, instanceConfig.Id);
		if (list.Count == 0)
		{
			Fail("no backups to restore");
			return 1;
		}
		string selector = Option("--file");
		BackupInfo backupInfo;
		if (selector == null)
		{
			backupInfo = list.First();
		}
		else
		{
			backupInfo = ((!int.TryParse(selector, out var result) || result < 1 || result > list.Count) ? (list.FirstOrDefault((BackupInfo b) => b.FileName.Equals(Path.GetFileName(selector), StringComparison.OrdinalIgnoreCase)) ?? list.FirstOrDefault((BackupInfo b) => b.FilePath.Equals(selector, StringComparison.OrdinalIgnoreCase))) : list[result - 1]);
		}
		if (backupInfo == null)
		{
			Fail("backup not found: " + selector + " (see: nova backups " + instanceConfig.Id + ")");
			return 1;
		}
		Header("Restore - " + backupInfo.WorldName);
		if (!WorldBackup.Verify(backupInfo, out string message))
		{
			Fail("backup failed verification: " + message);
			return 1;
		}
		Row("source", backupInfo.FileName);
		Row("created", backupInfo.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
		Row("integrity", message);
		Progress<string> log = new Progress<string>((string line) =>
		{
			Console.WriteLine("  " + line);
		});
		string text = WorldBackup.Restore(_paths, instanceConfig, backupInfo, Option("--name"), Options.ContainsKey("--replace"), log);
		Console.WriteLine();
		Write("  restored world folder: " + text, ConsoleColor.Green);
		if (!Options.ContainsKey("--replace"))
		{
			Write("  the original world was left untouched", ConsoleColor.DarkGray);
		}
		Console.WriteLine();
		return 0;
	}

	private static int Crash(List<string> positional)
	{
		InstanceConfig instanceConfig = Require(positional);
		Header("Crash analysis - " + instanceConfig.Name);
		CrashDiagnosis crashDiagnosis = CrashDiagnostics.AnalyzeLatest(instanceConfig);
		if (crashDiagnosis == null)
		{
			Warn("no crash-reports and no latest.log found in " + instanceConfig.GameDirectory);
			return 0;
		}
		Row("source", Path.GetFileName(crashDiagnosis.SourceFile) + " (" + crashDiagnosis.SourceKind + ")");
		if (crashDiagnosis.Description != null)
		{
			Console.WriteLine();
			Write("  " + crashDiagnosis.Description, ConsoleColor.Yellow);
		}
		if (crashDiagnosis.Causes.Count > 0)
		{
			Console.WriteLine();
			foreach (string item in crashDiagnosis.Causes.Take(5))
			{
				Console.WriteLine("  caused by: " + Trim(item, 160));
			}
		}
		Console.WriteLine();
		if (crashDiagnosis.Suspects.Count == 0)
		{
			Write("  no installed mod appears in the stack trace", ConsoleColor.DarkGray);
			Write("  (vanilla, Forge, Java or the graphics driver are the usual culprits)", ConsoleColor.DarkGray);
		}
		else
		{
			Write("  suspect mods:", ConsoleColor.Red);
			foreach (SuspectMod item2 in crashDiagnosis.Suspects.Take(6))
			{
				Console.WriteLine($"    {item2.Hits,3} frames  {item2.ModFile}   {item2.Evidence()}");
			}
		}
		foreach (CrashFinding item3 in crashDiagnosis.Findings.Take(4))
		{
			Console.WriteLine();
			Write("  " + item3.Title, ConsoleColor.Yellow);
			Console.WriteLine();
			Console.WriteLine("    " + item3.Suggestion);
		}
		string value = CrashDiagnostics.Write(_paths, instanceConfig, crashDiagnosis);
		Console.WriteLine();
		Row("report", value);
		Write("  no file inside the pack was changed", ConsoleColor.DarkGray);
		Console.WriteLine();
		return 0;
	}

	private static int Plan(List<string> positional)
	{
		InstanceConfig instanceConfig = Require(positional);
		ResolvedVersion version = ResolveVersion(instanceConfig);
		JavaInstall java = ResolveJava(instanceConfig, version);
		string nativesDirectory = Path.Combine(_paths.Natives, instanceConfig.Id);
		NovaAccount novaAccount = new AccountStore(_paths).Active(LauncherSettings.Load(_paths));
		LaunchPlan launchPlan = LaunchBuilder.Build(instanceConfig, version, _paths, java, nativesDirectory, novaAccount.Identity);
		Header("Dry run - " + instanceConfig.Name);
		Row("player", novaAccount.Name + "  (" + novaAccount.KindLabel + ")");
		Row("java", launchPlan.JavaPath);
		Row("cwd", launchPlan.WorkingDirectory);
		Row("arguments", launchPlan.Arguments.Count.ToString());
		Row("classpath entries", CountClasspath(launchPlan).ToString());
		foreach (string note in launchPlan.Notes)
		{
			Warn(note);
		}
		Console.WriteLine();
		Console.WriteLine("  " + launchPlan.Preview);
		Console.WriteLine();
		Write("  nothing was started", ConsoleColor.DarkGray);
		Console.WriteLine();
		return 0;
	}

	private static int Launch(List<string> positional)
	{
		InstanceConfig instance = Require(positional);
		bool flag = Options.ContainsKey("--force");
		DoctorReport result = Doctor.RunAsync(instance, _paths, deep: true).GetAwaiter().GetResult();
		Header("Launch - " + instance.Name + "  [" + instance.Id + "]");
		Row("checks", $"{result.Items.Count} run, {result.Fails} failed, {result.Warns} warnings");
		if (!result.Healthy && !flag)
		{
			foreach (CheckItem item in result.Items.Where((CheckItem i) => i.Status == CheckStatus.Fail))
			{
				Fail(item.Area + ": " + item.Message);
			}
			Console.WriteLine();
			Write("  refusing to start - fix with: nova repair " + instance.Id + "   (or --force)", ConsoleColor.Red);
			Console.WriteLine();
			return 1;
		}
		ResolvedVersion resolvedVersion = result.Version ?? ResolveVersion(instance);
		JavaInstall java = result.Java ?? ResolveJava(instance, resolvedVersion);
		string text = Path.Combine(_paths.Natives, instance.Id);
		int value = NativeExtractor.ExtractAll(resolvedVersion.NativeFiles.Select((ResolvedFile n) => Path.Combine((string.IsNullOrWhiteSpace(instance.LibrariesHome) ? _paths.Libraries : instance.LibrariesHome).TrimEnd('\\'), n.RelativePath.Replace('/', '\\'))).Where(File.Exists).ToList(), text);
		Row("natives", $"{value} files");
		if (instance.BackupWorldsBeforeLaunch && !Options.ContainsKey("--no-backup"))
		{
			Console.WriteLine();
			Write("  world backup", ConsoleColor.Cyan);
			Console.WriteLine();
			Progress<string> log = new Progress<string>((string line) =>
			{
				Console.WriteLine("    " + line);
			});
			List<BackupInfo> list = WorldBackup.CreateChanged(_paths, instance, log);
			Row("new backups", list.Count.ToString());
		}
		AccountStore accountStore = new AccountStore(_paths);
		LauncherSettings launcherSettings = LauncherSettings.Load(_paths);
		NovaAccount novaAccount = accountStore.Active(launcherSettings);
		if (novaAccount.IsMicrosoft && (!novaAccount.ExpiresUtc.HasValue || novaAccount.ExpiresUtc < DateTime.UtcNow.AddMinutes(2.0)))
		{
			NovaAccount novaAccount2 = (string.IsNullOrWhiteSpace(launcherSettings.MicrosoftClientId) ? null : new MicrosoftAuth().RefreshAsync(launcherSettings.MicrosoftClientId, novaAccount).GetAwaiter().GetResult());
			if (novaAccount2 != null)
			{
				accountStore.Save(novaAccount2);
				novaAccount = novaAccount2;
			}
		}
		Row("player", novaAccount.Name + "  (" + novaAccount.KindLabel + ")");
		LaunchPlan launchPlan = LaunchBuilder.Build(instance, resolvedVersion, _paths, java, text, novaAccount.Identity);
		foreach (string note in launchPlan.Notes)
		{
			Warn(note);
		}
		string text2 = Path.Combine(instance.GameDirectory, "logs");
		Directory.CreateDirectory(text2);
		string path = Path.Combine(text2, "launcher-console.log");
		string path2 = Path.Combine(text2, "latest.log");
		ProcessStartInfo processStartInfo = new ProcessStartInfo(launchPlan.JavaPath)
		{
			WorkingDirectory = launchPlan.WorkingDirectory,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		foreach (string argument in launchPlan.Arguments)
		{
			processStartInfo.ArgumentList.Add(argument);
		}
		instance.LastPlayedUtc = DateTime.UtcNow;
		new InstanceStore(_paths).Save(instance);
		Console.WriteLine();
		Write("  starting " + launchPlan.JavaPath, ConsoleColor.Cyan);
		Console.WriteLine();
		bool stopped = false;
		Console.CancelKeyPress += (object? _, ConsoleCancelEventArgs eventArgs) =>
		{
			eventArgs.Cancel = true;
			stopped = true;
		};
		using Process process = new Process
		{
			StartInfo = processStartInfo,
			EnableRaisingEvents = true
		};
		StreamWriter logWriter = new StreamWriter(path, append: false)
		{
			AutoFlush = true
		};
		process.OutputDataReceived += (object _, DataReceivedEventArgs e) =>
		{
			if (e.Data != null)
			{
				logWriter.WriteLine(e.Data);
			}
		};
		process.ErrorDataReceived += (object _, DataReceivedEventArgs e) =>
		{
			if (e.Data != null)
			{
				logWriter.WriteLine("[stderr] " + e.Data);
			}
		};
		Stopwatch stopwatch = Stopwatch.StartNew();
		process.Start();
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		using LogTailer logTailer = new LogTailer();
		logTailer.Follow(path2, (string line) =>
		{
			if (line.Contains("Setting user:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Loading") || line.Contains("Forge Mod Loader", StringComparison.OrdinalIgnoreCase) || line.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || line.Contains("FATAL", StringComparison.OrdinalIgnoreCase) || line.Contains("Caused by:", StringComparison.Ordinal) || (line.Contains("OpenAL", StringComparison.OrdinalIgnoreCase) && line.Contains("initialized", StringComparison.OrdinalIgnoreCase)) || line.Contains("LWJGL Version", StringComparison.Ordinal))
			{
				Console.WriteLine("  | " + Trim(line, 200));
			}
		});
		if (Options.ContainsKey("--timeout") && int.TryParse(Option("--timeout"), out var result2))
		{
			process.WaitForExit(result2 * 1000);
		}
		else
		{
			process.WaitForExit();
		}
		stopwatch.Stop();
		int exitCode = process.ExitCode;
		logWriter.Dispose();
		Console.WriteLine();
		Header("Game exited");
		Row("exit code", exitCode.ToString());
		Row("duration", stopwatch.Elapsed.ToString("hh\\:mm\\:ss"));
		if (stopped)
		{
			Row("note", "terminated from the console");
		}
		List<CrashFinding> list2 = (from f in CrashAnalyzer.AnalyzeFile(path2)
			where f.Severity != "INFO"
			select f).ToList();
		foreach (CrashFinding item2 in list2)
		{
			Write("  " + item2.Severity + ": " + item2.Title, (item2.Severity == "FAIL") ? ConsoleColor.Red : ConsoleColor.Yellow);
			Console.WriteLine("        " + item2.Suggestion);
		}
		if (list2.Count == 0)
		{
			Write("  no known crash pattern in latest.log", ConsoleColor.DarkGray);
		}
		instance = new InstanceStore(_paths).Find(instance.Id) ?? instance;
		instance.LastExitSummary = $"exit {exitCode} after {stopwatch.Elapsed:hh\\:mm\\:ss}" + ((list2.Count > 0) ? (" - " + list2[0].Title) : "");
		new InstanceStore(_paths).Save(instance);
		Console.WriteLine();
		return exitCode;
	}

	private static InstanceConfig Require(List<string> positional)
	{
		string text = positional.FirstOrDefault((string v) => !v.StartsWith("--"));
		if (text == null)
		{
			throw new ArgumentException("instance id is required (see: nova instances)");
		}
		return new InstanceStore(_paths).Find(text) ?? throw new ArgumentException("no such instance: " + text);
	}

	private static ResolvedVersion ResolveVersion(InstanceConfig instance)
	{
		string versionsHome = (string.IsNullOrWhiteSpace(instance.VersionHome) ? _paths.Versions : instance.VersionHome);
		return VersionResolver.Resolve(instance.VersionId, versionsHome);
	}

	private static JavaInstall ResolveJava(InstanceConfig instance, ResolvedVersion version)
	{
		if (!string.IsNullOrWhiteSpace(instance.JavaPath) && File.Exists(instance.JavaPath))
		{
			JavaInstall javaInstall = JavaLocator.Probe(instance.JavaPath);
			if ((object)javaInstall != null && javaInstall.Major == version.RequiredJavaMajor)
			{
				return javaInstall;
			}
		}
		return JavaLocator.Pick(version.RequiredJavaMajor) ?? throw new ArgumentException($"no Java {version.RequiredJavaMajor} found - see: nova java");
	}

	private static string? TakeOption(List<string> args, string name)
	{
		int num = args.IndexOf(name);
		if (num < 0 || num + 1 >= args.Count)
		{
			return null;
		}
		string result = args[num + 1];
		args.RemoveRange(num, 2);
		return result;
	}

	private static string? Option(string name)
	{
		if (!Options.TryGetValue(name, out string value))
		{
			return null;
		}
		return value;
	}

	private static int Unknown(string command)
	{
		Fail("unknown command: " + command);
		return Help();
	}

	private static void Header(string text)
	{
		Write("== " + text, ConsoleColor.Cyan);
		Console.WriteLine();
	}

	private static void Row(string label, string value)
	{
		Console.Write("  ");
		Write(label.PadRight(18), ConsoleColor.DarkGray);
		Console.WriteLine(value);
	}

	private static void Warn(string text)
	{
		Write("  ! ", ConsoleColor.Yellow);
		Console.WriteLine(text);
	}

	private static void Fail(string text)
	{
		Write("  x ", ConsoleColor.Red);
		Console.WriteLine(text);
	}

	private static void Write(string text, ConsoleColor color)
	{
		ConsoleColor foregroundColor = Console.ForegroundColor;
		Console.ForegroundColor = color;
		Console.Write(text);
		Console.ForegroundColor = foregroundColor;
	}

	private static int CountClasspath(LaunchPlan plan)
	{
		int num = plan.Arguments.FindIndex((string a) => a == "-cp" || a == "-classpath");
		if (num < 0 || num + 1 >= plan.Arguments.Count)
		{
			return 0;
		}
		return plan.Arguments[num + 1].Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Length;
	}

	private static string Trim(string text, int max)
	{
		if (text.Length > max)
		{
			return text.Substring(0, max) + "...";
		}
		return text;
	}
}
