using Microsoft;
using Microsoft.VisualStudio.Extensibility.Editor;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AutoBackupExtension;

public class BackupInfo
{
	public required string Action { get; init; }
	public required ITextViewSnapshot TextView { get; init; }
	public required DateTimeOffset DateTime { get; init; }
}

public class BackupProvider
{
	private readonly Channel<BackupInfo> channel = Channel.CreateBounded<BackupInfo>(256);

	public ChannelReader<BackupInfo> ChannelReader => channel.Reader;

	public ValueTask WriteTextViewAsync(BackupInfo backupInfo, CancellationToken cancellationToken)
	{
		return channel.Writer.WriteAsync(backupInfo, cancellationToken);
	}
	public bool TryWriteTextView(BackupInfo backupInfo)
	{
		return channel.Writer.TryWrite(backupInfo);
	}
}

public class BackupService : IDisposableObservable
{
	private readonly BackupProvider backupProvider;

	private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
	private Task task;

	public BackupService(BackupProvider backupProvider)
	{
		this.backupProvider = backupProvider;
		task = Task.Run(() => RunAsync(cancellationTokenSource.Token), cancellationTokenSource.Token);
	}

	public async Task RunAsync(CancellationToken cancellationToken)
	{
		using var buffer = new CharBuffer(1024 * 1024);
		while (!cancellationToken.IsCancellationRequested)
		{
			BackupInfo? backupInfo;
			while (await backupProvider.ChannelReader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
			{
				while (backupProvider.ChannelReader.TryRead(out backupInfo))
				{
					if (string.IsNullOrEmpty(backupInfo.TextView.FilePath))
					{
						continue;
					}
					try
					{
						var saveFile = System.IO.Path.Combine(
							System.IO.Path.GetTempPath(),
							nameof(BackupService),
							backupInfo.TextView.FilePath.Replace(":", "_"));
						var saveDir = System.IO.Path.GetDirectoryName(saveFile);
						if (saveDir == null)
						{
							throw new InvalidOperationException($"Failed SaveDir.");
						}
						if (!System.IO.Directory.Exists(saveDir))
						{
							System.IO.Directory.CreateDirectory(saveDir);
						}
						var saveBackupFile = System.IO.Path.Combine(
							saveDir,
							$"{backupInfo.DateTime.ToString("yyyyMMdd_HHmmssfffffff")}-{backupInfo.Action}-{System.IO.Path.GetFileName(backupInfo.TextView.FilePath)}");


						static void WriteFile(string filePath, CharBuffer buffer, BackupInfo backupInfo)
						{
							using var fileStream = new FileStream(
								filePath,
								FileMode.Create,
								FileAccess.Write,
								FileShare.Read,
								4096,
								true);
							fileStream.Write(Encoding.UTF8.Preamble);

							var position = 0;
							while (position < backupInfo.TextView.Document.Length)
							{
								var length = buffer.Length < (backupInfo.TextView.Document.Length - position)
									? buffer.Length
									: (backupInfo.TextView.Document.Length - position);
								var textRange = backupInfo.TextView.Document.Text.Slice(position, length);
								var sliceBuffer = buffer.AsSpan(0, textRange.Length);
								textRange.CopyTo(sliceBuffer);
								fileStream.Write(Encoding.UTF8.GetBytes(sliceBuffer.ToArray()));
								position += textRange.Length;
							}
						}
						WriteFile(saveBackupFile, buffer, backupInfo);
					}
					catch
					{

					}
				}
			}
		}
	}


	public bool IsDisposed { get; private set; }

	public void Dispose()
	{
		if (!IsDisposed)
		{
			IsDisposed = true;
			cancellationTokenSource.Cancel();
			//task.Wait();
		}

	}
}

public class CharBuffer : IDisposable
{
	private char[] buffer = [];

	public CharBuffer(int size)
	{
		buffer = ArrayPool<char>.Shared.Rent(size);
	}

	public void Dispose()
	{
		ArrayPool<char>.Shared.Return(buffer);
	}

	public int Length => buffer.Length;
	public Span<char> AsSpan() => buffer.AsSpan();
	public Span<char> AsSpan(int start, int length) => buffer.AsSpan(start, length);

}