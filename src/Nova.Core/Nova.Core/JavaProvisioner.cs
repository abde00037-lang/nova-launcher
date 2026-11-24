using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nova.Core;

public static class JavaProvisioner
{
	private const string Adoptium = "https://api.adoptium.net/v3/assets/latest";

	private static readonly HttpClient Http = new HttpClient(new SocketsHttpHandler
	{
		AutomaticDecompression = DecompressionMethods.All
	})
	{
		Timeout = TimeSpan.FromMinutes(10.0)
	};

	public static string DirectoryFor(LauncherPaths paths, int major)
	{
		return Path.Combine(paths.JavaHome, "jre" + major);
	}

	public static JavaInstall? Find(LauncherPaths paths, int major)
	{
		string text = Path.Combine(DirectoryFor(paths, major), "bin", "java.exe");
		if (!File.Exists(text))
		{
			return null;
		}
		return JavaLocator.Probe(text, "nova");
	}

	public static async Task<JavaInstall?> EnsureAsync(LauncherPaths paths, int major, IProgress<string>? log = null, CancellationToken ct = default(CancellationToken))
	{
		JavaInstall javaInstall = Find(paths, major);
		if ((object)javaInstall != null)
		{
			log?.Report($"java {major}: already installed ({javaInstall.RawVersion})");
			return javaInstall;
		}
		JavaInstall javaInstall2 = JavaLocator.Discover(refresh: true).FirstOrDefault((JavaInstall j) => j.Major == major);
		if ((object)javaInstall2 != null)
		{
			log?.Report($"java {major}: found on this machine ({javaInstall2.Path})");
			return javaInstall2;
		}
		log?.Report($"java {major}: not installed - downloading Temurin JRE {major}");
		string requestUri = $"{"https://api.adoptium.net/v3/assets/latest"}/{major}/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse";
		string link = null;
		long size = 0L;
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(await Http.GetStringAsync(requestUri, ct).ConfigureAwait(continueOnCapturedContext: false));
			if (jsonDocument.RootElement.ValueKind == JsonValueKind.Array && jsonDocument.RootElement.GetArrayLength() > 0 && jsonDocument.RootElement[0].TryGetProperty("binary", out var value) && value.TryGetProperty("package", out var value2) && value2.TryGetProperty("link", out var value3))
			{
				link = value3.GetString();
				if (value2.TryGetProperty("size", out var value4) && value4.ValueKind == JsonValueKind.Number)
				{
					size = value4.GetInt64();
				}
			}
		}
		catch (Exception ex)
		{
			log?.Report("adoptium lookup failed: " + ex.Message);
		}
		if (string.IsNullOrWhiteSpace(link))
		{
			throw new InvalidOperationException($"could not find a Java {major} download - install one manually (see Settings)");
		}
		Directory.CreateDirectory(paths.JavaHome);
		Directory.CreateDirectory(paths.Cache);
		string archive = Path.Combine(paths.Cache, $"jre{major}.zip");
		using (Downloader downloader = new Downloader(2))
		{
			log?.Report($"java {major}: {((size > 0) ? (((double)size / 1048576.0).ToString("0") + " MB") : "downloading")}");
			if (await downloader.EnsureAsync(archive, link, null, ct).ConfigureAwait(continueOnCapturedContext: false) == FetchResult.Failed)
			{
				throw new InvalidOperationException("java download failed");
			}
		}
		string target = DirectoryFor(paths, major);
		if (Directory.Exists(target))
		{
			Directory.Delete(target, recursive: true);
		}
		log?.Report("java: extracting…");
		await Task.Run(() =>
		{
			Extract(archive, target);
		}, ct).ConfigureAwait(continueOnCapturedContext: false);
		JavaInstall javaInstall3 = Find(paths, major);
		if ((object)javaInstall3 == null)
		{
			throw new InvalidOperationException("the downloaded java could not be started");
		}
		log?.Report($"java {major} ready: {javaInstall3.RawVersion}");
		return javaInstall3;
	}

	private static void Extract(string archive, string target)
	{
		string text = target + ".tmp";
		if (Directory.Exists(text))
		{
			Directory.Delete(text, recursive: true);
		}
		Directory.CreateDirectory(text);
		ZipFile.ExtractToDirectory(archive, text, overwriteFiles: true);
		string[] directories = Directory.GetDirectories(text);
		string path = ((directories.Length == 1 && Directory.GetFiles(text).Length == 0) ? directories[0] : text);
		Directory.CreateDirectory(target);
		foreach (string item in Directory.EnumerateFiles(path))
		{
			File.Move(item, Path.Combine(target, Path.GetFileName(item)));
		}
		foreach (string item2 in Directory.EnumerateDirectories(path))
		{
			Directory.Move(item2, Path.Combine(target, Path.GetFileName(item2)));
		}
		try
		{
			Directory.Delete(text, recursive: true);
		}
		catch
		{
		}
	}
}
