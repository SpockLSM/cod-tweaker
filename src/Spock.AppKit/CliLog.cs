using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Spock.AppKit;

/// <summary>
/// Output for a window app's command-line mode. A WinExe has no console, so this borrows the parent's
/// console when started from one, and always keeps a log file as well.
/// </summary>
public static class CliLog
{
    /// <param name="logsFolder">Where the log file goes.</param>
    /// <param name="prefix">File name prefix, e.g. "apply" for apply-2026-10-23_19-20-31.txt.</param>
    public static TextWriter Open(string logsFolder, string prefix)
    {
        var writers = new List<TextWriter>();
        try
        {
            if (AttachConsole(-1))
            {
                var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                stdout.WriteLine();
                writers.Add(stdout);
            }
        }
        catch { }
        try
        {
            Directory.CreateDirectory(logsFolder);
            writers.Add(new StreamWriter(Path.Combine(logsFolder, $"{prefix}-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt")) { AutoFlush = true });
        }
        catch { }
        return new TeeWriter(writers);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AttachConsole(int processId);

    sealed class TeeWriter : TextWriter
    {
        readonly List<TextWriter> _w;
        public TeeWriter(List<TextWriter> w) => _w = w;
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value) { foreach (var w in _w) w.Write(value); }
        public override void Write(string? value) { foreach (var w in _w) w.Write(value); }
        public override void WriteLine(string? value) { foreach (var w in _w) w.WriteLine(value); }
        protected override void Dispose(bool disposing) { if (disposing) foreach (var w in _w) w.Dispose(); base.Dispose(disposing); }
    }
}
