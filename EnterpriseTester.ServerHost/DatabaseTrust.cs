using System.Diagnostics;
using EnterpriseTester.ServerCore;

namespace EnterpriseTester.ServerHost;

internal static class DatabaseTrust
{
    public static async Task PrepareAsync(
        string root,
        ServerConfig config,
        string java,
        CancellationToken token
    )
    {
        if (string.IsNullOrWhiteSpace(config.Database.CaCertificatePath))
            return;
        var cert = ServerPaths.Resolve(root, config.Database.CaCertificatePath);
        if (!File.Exists(cert))
            throw new FileNotFoundException("Не найден CA MySQL: " + cert);
        var keytool = Path.Combine(
            Path.GetDirectoryName(java)!,
            OperatingSystem.IsWindows() ? "keytool.exe" : "keytool"
        );
        if (!File.Exists(keytool))
            throw new InvalidOperationException(
                "Для собственного CA MySQL нужна Java JDK с keytool. Выполните install-java."
            );
        var store = ServerPaths.Child(root, "runtime/mysql-trust.p12");
        var stamp = store + ".sha256";
        var hash = ServerPaths.HashFile(cert);
        if (File.Exists(store) && File.Exists(stamp) && File.ReadAllText(stamp) == hash)
            return;
        if (File.Exists(store))
            File.Delete(store);
        var info = new ProcessStartInfo(keytool)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (
            var arg in new[]
            {
                "-importcert",
                "-noprompt",
                "-alias",
                "mysql-ca",
                "-file",
                cert,
                "-keystore",
                store,
                "-storetype",
                "PKCS12",
                "-storepass",
                "changeit",
            }
        )
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        await stdout;
        await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                "keytool не смог подготовить доверенное хранилище CA MySQL."
            );
        File.WriteAllText(stamp, hash);
    }
}
