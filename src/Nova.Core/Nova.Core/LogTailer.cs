using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nova.Core;

public sealed class LogTailer : IDisposable
{
	private readonly CancellationTokenSource _cts = new CancellationTokenSource();

	private Task? _worker;

	public void Follow(string path, Action<string> onLine, int pollMs = 250, bool fromEnd = true)
	{
		_worker = Task.Run(async () =>
		{
			long offset = ((fromEnd && File.Exists(path)) ? new FileInfo(path).Length : 0);
			List<byte> buffer = new List<byte>();
			while (!_cts.IsCancellationRequested)
			{
				try
				{
					if (File.Exists(path))
					{
						using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
						if (stream.Length < offset)
						{
							offset = 0L;
						}
						if (stream.Length > offset)
						{
							stream.Seek(offset, SeekOrigin.Begin);
							byte[] chunk = new byte[stream.Length - offset];
							int num = await stream.ReadAsync(chunk, _cts.Token).ConfigureAwait(continueOnCapturedContext: false);
							offset += num;
							buffer.AddRange(chunk.Take(num));
							string[] array = Encoding.UTF8.GetString(buffer.ToArray()).Split('\n');
							for (int i = 0; i < array.Length - 1; i++)
							{
								onLine(array[i].TrimEnd('\r'));
							}
							buffer.Clear();
							buffer.AddRange(Encoding.UTF8.GetBytes(array[^1]));
						}
					}
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch
				{
				}
				try
				{
					await Task.Delay(pollMs, _cts.Token).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch (OperationCanceledException)
				{
					break;
				}
			}
		}, _cts.Token);
	}

	public void Dispose()
	{
		_cts.Cancel();
		try
		{
			_worker?.Wait(1000);
		}
		catch
		{
		}
		_cts.Dispose();
	}
}
