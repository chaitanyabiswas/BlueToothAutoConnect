using System.Diagnostics;
using System.Reflection;
using System.Security;

namespace BlueToothAutoConnect_UserAgent.Services;

public static class DiagnosticLog
{
    private const long MaxLogSizeBytes = 5 * 1024 * 1024;
    private static readonly object Sync = new();

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BlueToothAutoConnect");

    public static string LogFilePath => Path.Combine(LogDirectory, "diagnostics.log");

    public static void StartSession()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        Write($"--- Application session started; version={version}; runtime={Environment.Version}; os={Environment.OSVersion}; process={Environment.ProcessId} ---");
    }

    public static void Write(string message)
    {
        var line = $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}";
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length + line.Length > MaxLogSizeBytes)
                {
                    var previousLog = Path.Combine(LogDirectory, "diagnostics.previous.log");
                    File.Move(LogFilePath, previousLog, overwrite: true);
                }

                File.AppendAllText(LogFilePath, line);
            }
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"Diagnostic log write failed: {ex}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"Diagnostic log write failed: {ex}");
        }
        catch (SecurityException ex)
        {
            Debug.WriteLine($"Diagnostic log write failed: {ex}");
        }
    }

    public static void WriteException(string source, Exception exception) =>
        Write($"EXCEPTION source={source}{Environment.NewLine}{exception}");
}
