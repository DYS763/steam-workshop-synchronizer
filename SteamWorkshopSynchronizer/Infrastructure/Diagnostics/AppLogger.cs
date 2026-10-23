using System.Globalization;
using System.IO;
using System.Text;

namespace SteamWorkshopSynchronizer.Infrastructure.Diagnostics;

/// <summary>
/// Lightweight file logger that is deliberately independent from the UI and database.
/// Logging must never be allowed to break the synchronizer workflow.
/// </summary>
public sealed class AppLogger
{
    private readonly string logDirectory;
    private readonly object writeLock = new();
    private readonly int retentionDays;
    private bool developerMode;

    public AppLogger(string logDirectory, bool developerMode = false, int retentionDays = 14)
    {
        if (string.IsNullOrWhiteSpace(logDirectory))
        {
            throw new ArgumentException("日志目录不能为空", nameof(logDirectory));
        }

        this.logDirectory = Path.GetFullPath(logDirectory);
        this.developerMode = developerMode;
        this.retentionDays = Math.Clamp(retentionDays, 1, 365);
    }

    public string LogDirectory => logDirectory;

    public string CurrentLogPath => Path.Combine(logDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");

    public bool DeveloperMode
    {
        get
        {
            lock (writeLock)
            {
                return developerMode;
            }
        }
        set
        {
            lock (writeLock)
            {
                developerMode = value;
            }
        }
    }

    public void Debug(string category, string message) => Write(AppLogLevel.Debug, category, message, null);

    public void Info(string category, string message) => Write(AppLogLevel.Info, category, message, null);

    public void Warning(string category, string message, Exception? exception = null) => Write(AppLogLevel.Warning, category, message, exception);

    public void Error(string category, string message, Exception? exception = null) => Write(AppLogLevel.Error, category, message, exception);

    public string ReadCurrentLog()
    {
        try
        {
            return File.Exists(CurrentLogPath)
                ? File.ReadAllText(CurrentLogPath, Encoding.UTF8)
                : "当前日志文件还没有内容。";
        }
        catch (Exception exception)
        {
            return $"读取日志失败：{exception.Message}";
        }
    }

    public void ClearCurrentLog()
    {
        try
        {
            lock (writeLock)
            {
                if (File.Exists(CurrentLogPath))
                {
                    File.Delete(CurrentLogPath);
                }
            }
        }
        catch
        {
            // The log viewer should remain usable even when a file is locked.
        }
    }

    private void Write(AppLogLevel level, string category, string message, Exception? exception)
    {
        try
        {
            lock (writeLock)
            {
                if (!developerMode && level is AppLogLevel.Debug or AppLogLevel.Info)
                {
                    return;
                }

                Directory.CreateDirectory(logDirectory);
                var builder = new StringBuilder();
                builder.Append(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                builder.Append(" [").Append(level.ToString().ToUpperInvariant()).Append("]");
                builder.Append(" [").Append(Sanitize(category)).Append("]");
                builder.Append(" [T").Append(Environment.CurrentManagedThreadId).Append("] ");
                builder.AppendLine(Sanitize(message));
                if (exception is not null)
                {
                    builder.Append("    ").Append(exception.GetType().FullName).Append(": ");
                    builder.AppendLine(Sanitize(exception.Message));
                    builder.AppendLine(Sanitize(exception.StackTrace ?? "(无堆栈)"));
                }

                File.AppendAllText(CurrentLogPath, builder.ToString(), Encoding.UTF8);
                CleanupOldLogs(DateTime.Now.Date);
            }
        }
        catch
        {
            // Diagnostics are best effort. Never replace the original application error.
        }
    }

    private void CleanupOldLogs(DateTime today)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(logDirectory, "app-*.log"))
            {
                if (File.GetLastWriteTime(path).Date < today.AddDays(-retentionDays))
                {
                    File.Delete(path);
                }
            }
        }
        catch
        {
            // Retention is best effort.
        }
    }

    private static string Sanitize(string value) => value.Replace("\r", "\\r").Replace("\n", "\\n");
}
