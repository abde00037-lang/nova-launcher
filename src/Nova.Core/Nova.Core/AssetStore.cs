using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Nova.Core;

public sealed class AssetStore
{
	private readonly Downloader _downloader;

	public string AssetsDirectory { get; }

	public AssetStore(string assetsDirectory, Downloader downloader)
	{
		AssetsDirectory = assetsDirectory;
		_downloader = downloader;
	}

	public string IndexFile(string indexId)
	{
		return Path.Combine(AssetsDirectory, "indexes", indexId + ".json");
	}

	public string ObjectFile(string hash)
	{
		return Path.Combine(AssetsDirectory, "objects", hash.Substring(0, 2), hash);
	}

	public AssetIndexJson? LoadIndex(string indexId)
	{
		if (!File.Exists(IndexFile(indexId)))
		{
			return null;
		}
		return Json.Read<AssetIndexJson>(IndexFile(indexId));
	}

	public async Task<AssetReport> VerifyAsync(string indexId, bool download, IProgress<string>? log, CancellationToken ct = default(CancellationToken), IProgress<DownloadProgress>? progress = null)
	{
		AssetReport report = new AssetReport
		{
			IndexId = indexId
		};
		AssetIndexJson? index = LoadIndex(indexId);
		if (index?.Objects == null)
		{
			log?.Report("asset index '" + indexId + "' not found in " + AssetsDirectory);
			return report;
		}
		report.Total = index.Objects.Count;
		int failedLocal = 0;
		List<(string Name, AssetObject Obj, string Target)> missingItems = new List<(string, AssetObject, string)>();
		foreach (KeyValuePair<string, AssetObject> @object in index.Objects)
		{
			@object.Deconstruct(out var key, out var value);
			string name = key;
			AssetObject obj = value;
			ct.ThrowIfCancellationRequested();
			string target = ObjectFile(obj.Hash);
			if (File.Exists(target) && new FileInfo(target).Length == obj.Size)
			{
				report.Present++;
				if (index.Virtual || index.MapToResources)
				{
					EnsureVirtualCopy(index, name, target);
				}
				continue;
			}
			report.Missing++;
			if (report.MissingSamples.Count < 8)
			{
				report.MissingSamples.Add(name);
			}
			if (download)
			{
				missingItems.Add((name, obj, target));
			}
		}
		if (download && missingItems.Count > 0)
		{
			int toFetch = missingItems.Count;
			int completed = 0;
			long lastTick = 0L;
			progress?.Report(new DownloadProgress("Assets", report.Present, report.Total, _downloader.BytesDownloaded, $"assets 0/{toFetch} missing objects…"));
			List<Task> tasks = new List<Task>();
			using SemaphoreSlim gate = new SemaphoreSlim(10);
			foreach (var item in missingItems)
			{
				ct.ThrowIfCancellationRequested();
				await gate.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
				string aName = item.Name;
				AssetObject aObj = item.Obj;
				string aTarget = item.Target;
				tasks.Add(Task.Run(async delegate
				{
					try
					{
						string url = "https://resources.download.minecraft.net/" + aObj.Hash.Substring(0, 2) + "/" + aObj.Hash;
						FetchResult res = await _downloader.EnsureAsync(aTarget, url, aObj.Hash, ct).ConfigureAwait(continueOnCapturedContext: false);
						if (res == FetchResult.Failed || res == FetchResult.HashMismatch)
						{
							Interlocked.Increment(ref failedLocal);
							lock (report.FailedSamples)
							{
								if (report.FailedSamples.Count < 10)
								{
									report.FailedSamples.Add(aName);
								}
							}
						}
						if (index.Virtual || index.MapToResources)
						{
							EnsureVirtualCopy(index, aName, aTarget);
						}
						int doneNow = Interlocked.Increment(ref completed);
						long now = Environment.TickCount64;
						if (now - Interlocked.Read(ref lastTick) >= 90 || doneNow == toFetch)
						{
							Interlocked.Exchange(ref lastTick, now);
							progress?.Report(new DownloadProgress("Assets", report.Present + doneNow, report.Total, _downloader.BytesDownloaded, $"assets {report.Present + doneNow}/{report.Total} · {(double)_downloader.BytesDownloaded / 1048576.0:F1} MB"));
						}
						if (doneNow % 200 == 0 || doneNow == toFetch)
						{
							log?.Report($"assets: {doneNow}/{toFetch} downloaded ({(double)_downloader.BytesDownloaded / 1048576.0:F1} MB)");
						}
					}
					finally
					{
						gate.Release();
					}
				}, ct));
				if (tasks.Count >= 160)
				{
					await Task.WhenAll(tasks).ConfigureAwait(continueOnCapturedContext: false);
					tasks.Clear();
				}
			}
			if (tasks.Count > 0)
			{
				await Task.WhenAll(tasks).ConfigureAwait(continueOnCapturedContext: false);
			}
			report.DownloadFailed += failedLocal;
			int stillMissing = 0;
			foreach (KeyValuePair<string, AssetObject> object2 in index.Objects)
			{
				AssetObject assetObject = object2.Value;
				string text = ObjectFile(assetObject.Hash);
				if (!File.Exists(text) || new FileInfo(text).Length != assetObject.Size)
				{
					stillMissing++;
				}
			}
			report.Missing = stillMissing;
			report.Present = report.Total - stillMissing;
		}
		else if (download)
		{
			progress?.Report(new DownloadProgress("Assets", report.Total, Math.Max(1, report.Total), _downloader.BytesDownloaded, $"all {report.Total} assets verified"));
		}
		log?.Report($"assets[{indexId}]: {report.Present}/{report.Total} present, {report.Missing} missing, {report.DownloadFailed} failed");
		return report;
	}

	public string VirtualRoot(string indexId)
	{
		return Path.Combine(AssetsDirectory, "virtual", indexId);
	}

	private void EnsureVirtualCopy(AssetIndexJson index, string name, string sourceFile)
	{
		string text = Path.Combine(index.MapToResources ? Path.Combine(AssetsDirectory, "resources") : VirtualRoot("legacy"), name.Replace('/', Path.DirectorySeparatorChar));
		if (File.Exists(text))
		{
			return;
		}
		try
		{
			string? dir = Path.GetDirectoryName(text);
			if (!string.IsNullOrEmpty(dir))
			{
				Directory.CreateDirectory(dir);
			}
			File.Copy(sourceFile, text, overwrite: true);
		}
		catch
		{
		}
	}
}
