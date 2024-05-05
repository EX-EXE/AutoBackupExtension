using Microsoft.VisualStudio.Extensibility.Editor;
using Microsoft.VisualStudio.Extensibility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Threading;

namespace AutoBackupExtension;

[VisualStudioContribution]
public class TextViewOperationListener(
	TimeProvider timeProvider,
	BackupProvider backupProvider,
	BackupService backupService)
	: ExtensionPart
	, ITextViewOpenClosedListener
	, ITextViewChangedListener
{
	private ConcurrentDictionary<string, Debouncer> debouncerDictionary = new();

	public TextViewExtensionConfiguration TextViewExtensionConfiguration => new()
	{
		AppliesTo = new[] { DocumentFilter.FromGlobPattern("**/*", relativePath: true) },
	};

	public async Task TextViewOpenedAsync(ITextViewSnapshot textView, CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(textView.FilePath))
		{
			return;
		}
		await backupProvider.WriteTextViewAsync(
			new BackupInfo()
			{
				Action = "Opened",
				DateTime = timeProvider.GetLocalNow(),
				TextView = textView
			}, cancellationToken).ConfigureAwait(false);

		debouncerDictionary.TryAdd(
			textView.FilePath,
			new Debouncer(TimeSpan.FromMinutes(1.0)));
	}

	public async Task TextViewClosedAsync(ITextViewSnapshot textView, CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(textView.FilePath))
		{
			return;
		}
		await backupProvider.WriteTextViewAsync(
			new BackupInfo()
			{
				Action = "Closed",
				DateTime = timeProvider.GetLocalNow(),
				TextView = textView
			}, cancellationToken).ConfigureAwait(false);

		if (debouncerDictionary.TryRemove(textView.FilePath, out var debouncer))
		{
			debouncer.Dispose();
		}

	}

	public Task TextViewChangedAsync(TextViewChangedArgs args, CancellationToken cancellationToken)
	{
		var textView = args.AfterTextView;
		if (string.IsNullOrEmpty(textView.FilePath))
		{
			return Task.CompletedTask;
		}

		if (debouncerDictionary.TryGetValue(textView.FilePath, out var debouncer))
		{
			var info = new BackupInfo()
			{
				Action = "Changed",
				DateTime = timeProvider.GetLocalNow(),
				TextView = textView,
			};
			debouncer.Debounce(info, async (info, cancellationToken) =>
			{
				await backupProvider.WriteTextViewAsync(
					info, cancellationToken).ConfigureAwait(false);
			});
		}
		return Task.CompletedTask;
	}

}

public class Debouncer : IDisposable
{
	private bool disposed = false;
	private readonly TimeSpan delay;
	private CancellationTokenSource? cancellationTokenSource = null;

	private readonly object lockObject = new object();

	public Debouncer(TimeSpan delay)
	{
		this.delay = delay;
	}

	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			Cancel();
		}
	}

	public void Cancel()
	{
		lock (lockObject)
		{
			if (cancellationTokenSource != null && !cancellationTokenSource.IsCancellationRequested)
			{
				cancellationTokenSource.Cancel();
				cancellationTokenSource = null;
			}
		}
	}
	public CancellationTokenSource CancelAndCreate()
	{
		var newCancellationTokenSource = new CancellationTokenSource();
		lock (lockObject)
		{
			if (cancellationTokenSource != null && !cancellationTokenSource.IsCancellationRequested)
			{
				cancellationTokenSource.Cancel();
			}
			cancellationTokenSource = newCancellationTokenSource;
		}
		return newCancellationTokenSource;
	}

	public void Debounce<T>(T state, Func<T, CancellationToken, ValueTask> func)
	{
		if (disposed)
		{
			return;
		}

		var cancellationTokenSource = CancelAndCreate();
		var canecllationToken = cancellationTokenSource.Token;

		_ = Task.Run(async () =>
		{
			if (!canecllationToken.IsCancellationRequested)
			{
				await Task.Delay(delay, canecllationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
				if (!canecllationToken.IsCancellationRequested)
				{
					await func(state, canecllationToken).ConfigureAwait(false);
				}
			}
		}, cancellationToken: canecllationToken);
	}
}