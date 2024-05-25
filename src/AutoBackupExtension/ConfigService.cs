namespace AutoBackupExtension;

public class ConfigService
{
    public string BackupDirectory { get; set; } = System.IO.Path.Combine(
                            System.IO.Path.GetTempPath(),
                            nameof(AutoBackupExtension),
                            "Backup");

    public double BackupDays { get; set; } = 7.0;
    public double CleanupHours { get; set; } = 1.0;
}
