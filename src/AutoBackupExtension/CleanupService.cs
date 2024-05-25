using Microsoft;
using Microsoft.VisualStudio.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoBackupExtension;

public class CleanupService : IDisposableObservable
{
    private readonly TraceSource logger;
    private readonly TimeProvider timeProvider;
    private readonly ConfigService configService;

    private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
    private Task task;

    public bool IsDisposed { get; set; } = false;


    public CleanupService(
        TraceSource logger,
        TimeProvider timeProvider,
        ConfigService configService)
    {
        this.logger = logger;
        this.timeProvider = timeProvider;
        this.configService = configService;

        var cancellationToken = cancellationTokenSource.Token;
        task = Task.Run(() => RunAsync(cancellationToken), cancellationToken);
    }
    public void Dispose()
    {
        IsDisposed = true;
        if (!cancellationTokenSource.IsCancellationRequested)
        {
            cancellationTokenSource.Cancel();
        }
        // task.Wait()
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var localDateTimeOffset = timeProvider.GetLocalNow();
            var deleteDateTimeOffset = localDateTimeOffset.Subtract(TimeSpan.FromHours(configService.BackupDays));
            var deleteDateTime = localDateTimeOffset.LocalDateTime;

            static void Cleanup(DirectoryInfo directoryInfo, DateTime deleteDateTime, TraceSource? logger)
            {
                if (!directoryInfo.Exists)
                {
                    return;
                }

                // Delete File
                foreach (var fileInfo in directoryInfo.EnumerateFiles())
                {
                    try
                    {
                        if (fileInfo.Exists && fileInfo.LastWriteTime < deleteDateTime)
                        {
                            fileInfo.Delete();
                        }
                        logger?.TraceInformation($"Delete File.[{fileInfo.FullName}]");
                    }
                    catch (Exception ex)
                    {
                        logger?.TraceInformation($"Exception Delete File.[{fileInfo.FullName}]\n{ex}");
                    }
                }

                // Search SubDirectory
                foreach (var subDirectoryInfo in directoryInfo.EnumerateDirectories())
                {
                    Cleanup(subDirectoryInfo, deleteDateTime, logger);
                }

                // Delete Directory
                try
                {
                    if (directoryInfo.Exists &&
                        !directoryInfo.EnumerateFileSystemInfos().Any())
                    {
                        directoryInfo.Delete();
                        logger?.TraceInformation($"Delete Directory.[{directoryInfo.FullName}]");
                    }
                }
                catch (Exception ex)
                {
                    logger?.TraceInformation($"Exception Delete Directory.[{directoryInfo.FullName}]\n{ex}");
                }

            }
            var rootDirectoryInfo = new DirectoryInfo(configService.BackupDirectory);
            Cleanup(rootDirectoryInfo, deleteDateTime, logger);


            await Task.Delay(TimeSpan.FromDays(configService.CleanupHours), timeProvider).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

}
