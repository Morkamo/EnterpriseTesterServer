using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using EnterpriseTester.ServerCore;

namespace EnterpriseTester.ServerHost;

internal static class Configuration
{
    public static void Save(string path, ServerConfig config)
    {
        config.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(config, ServerConfig.JsonOptions));
        if (OperatingSystem.IsWindows())
        {
            var security = new FileSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(
                new FileSystemAccessRule(
                    WindowsIdentity.GetCurrent().User!,
                    FileSystemRights.FullControl,
                    AccessControlType.Allow
                )
            );
            security.AddAccessRule(
                new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    FileSystemRights.FullControl,
                    AccessControlType.Allow
                )
            );
            new FileInfo(path).SetAccessControl(security);
        }
        else
            ServerPaths.ProtectFile(path);
    }

    public static string? DesktopPassword()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        var file = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EnterpriseTester",
            "local-server-password.bin"
        );
        if (!File.Exists(file))
            return null;
        var bytes = ProtectedData.Unprotect(
            File.ReadAllBytes(file),
            Encoding.UTF8.GetBytes("EnterpriseTester.Desktop.LocalServer.v1"),
            DataProtectionScope.CurrentUser
        );
        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public static ServerConfig Wizard(ServerConfig current)
    {
        string Ask(string text, string fallback)
        {
            Console.Write(text + " [" + fallback + "]: ");
            var value = Console.ReadLine();
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
        Console.WriteLine("Настройка существующей MySQL. База не создаётся и не импортируется.");
        var host = Ask("Адрес MySQL", current.Database.Host);
        var port = int.Parse(Ask("Порт MySQL", current.Database.Port.ToString()));
        var name = Ask("Имя базы", current.Database.Name);
        var user = Ask("Пользователь", current.Database.User);
        Console.Write("Пароль (пусто — оставить прежний): ");
        var password = new StringBuilder();
        if (Console.IsInputRedirected)
            password.Append(Console.ReadLine());
        else
            while (true)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    break;
                }
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (password.Length > 0)
                    {
                        password.Length--;
                        Console.Write("\b \b");
                    }
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    password.Append(key.KeyChar);
                    Console.Write('*');
                }
            }
        var ssl = Ask("SSL: Preferred/Required/VerifyCA/VerifyFull", current.Database.SslMode);
        var bind = Ask("IP сервера (0.0.0.0 — LAN)", current.Server.BindAddress);
        var http = int.Parse(Ask("Порт приложения", current.Server.Port.ToString()));
        var images = Ask("Каталог изображений", current.Server.ImagesDirectory);
        return (
            current with
            {
                Database = current.Database with
                {
                    Host = host,
                    Port = port,
                    Name = name,
                    User = user,
                    Password =
                        password.Length == 0 ? current.Database.Password : password.ToString(),
                    SslMode = ssl,
                },
                Server = current.Server with
                {
                    BindAddress = bind,
                    Port = http,
                    ImagesDirectory = images,
                },
            }
        ).Validate();
    }
}
