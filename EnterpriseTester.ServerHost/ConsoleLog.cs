using EnterpriseTester.ServerCore;

namespace EnterpriseTester.ServerHost;

internal sealed class ConsoleLog : IDisposable
{
    private readonly object sync = new();
    private readonly StreamWriter file;
    private readonly SecretRedactor redactor;
    public string Path { get; }

    public ConsoleLog(string root, params string[] secrets)
    {
        Directory.CreateDirectory(System.IO.Path.Combine(root, "logs"));
        Path = System.IO.Path.Combine(
            root,
            "logs",
            "server-"
                + DateTime.Now.ToString("yyyyMMdd-HHmmss")
                + "-"
                + Environment.ProcessId
                + ".log"
        );
        file = new StreamWriter(
            new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.Read)
        )
        {
            AutoFlush = true,
        };
        redactor = new SecretRedactor(secrets);
        ServerPaths.ProtectFile(Path);
    }

    public void Write(string message)
    {
        var line = redactor.Scrub($"[{DateTime.Now:HH:mm:ss}] {message}");
        lock (sync)
        {
            Console.WriteLine(line);
            file.WriteLine(line);
        }
    }

    public void Dispose()
    {
        lock (sync)
            file.Dispose();
    }
}
