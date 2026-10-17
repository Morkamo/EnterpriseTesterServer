using System.Diagnostics;
using System.Text.RegularExpressions;
using EnterpriseTester.ServerCore;

namespace EnterpriseTester.ServerHost;

internal static class MysqlService
{
    public static async Task TryStartAsync(
        DatabaseConfig database,
        ConsoleLog log,
        CancellationToken token
    )
    {
        if (!database.IsLoopback || !database.TryStartLocalService)
            return;
        var name = string.IsNullOrWhiteSpace(database.LocalServiceName)
            ? OperatingSystem.IsWindows()
                ? "MySQL80"
                : "mysql"
            : database.LocalServiceName;
        if (!Regex.IsMatch(name, "^[A-Za-z0-9_.-]+$"))
            throw new ArgumentException("Неверное имя службы MySQL.");
        log.Write("Запускаем существующую службу MySQL: " + name);
        try
        {
            var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "sc.exe" : "systemctl")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            if (!OperatingSystem.IsWindows())
                start.ArgumentList.Add("--no-ask-password");
            start.ArgumentList.Add("start");
            start.ArgumentList.Add(name);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(token);
            var error = process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);
            await output;
            await error;
            if (process.ExitCode != 0)
                log.Write(
                    "Нужен отдельный запуск службы, возможно с правами администратора. Новая MySQL и база автоматически не инициализируются."
                );
            else
                await Task.Delay(1500, token);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            log.Write(
                "Менеджер служб не найден. Запустите MySQL отдельно или задайте удалённый адрес."
            );
        }
    }
}
