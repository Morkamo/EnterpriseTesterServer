using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace EnterpriseTester.ServerCore;

public sealed record ServerConfig
{
    public WebConfig Server { get; init; } = new();
    public DatabaseConfig Database { get; init; } = new();
    public RuntimeConfig Runtime { get; init; } = new();
    public TestingConfig Testing { get; init; } = new();
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public ServerConfig Validate()
    {
        if (Server is null || Database is null || Runtime is null || Testing is null)
            throw new ArgumentException("В конфигурации отсутствует обязательный раздел.");
        if (!IPAddress.TryParse(Server.BindAddress, out _) || Server.Port is < 1 or > 65535)
            throw new ArgumentException("Server: укажите IP прослушивания и порт 1–65535.");
        if (
            string.IsNullOrWhiteSpace(Database.Host)
            || Uri.CheckHostName(Database.Host) == UriHostNameType.Unknown
            || Database.Port is < 1 or > 65535
        )
            throw new ArgumentException("Database: неверный адрес или порт MySQL.");
        if (
            !Regex.IsMatch(Database.Name ?? "", @"^[A-Za-z0-9_]+$")
            || string.IsNullOrWhiteSpace(Database.User)
        )
            throw new ArgumentException("Database: задайте имя существующей базы и пользователя.");
        if (
            !new[] { "Disabled", "Preferred", "Required", "VerifyCA", "VerifyFull" }.Contains(
                Database.SslMode
            )
        )
            throw new ArgumentException(
                "Database.SslMode: Disabled, Preferred, Required, VerifyCA или VerifyFull."
            );
        if (
            !Database.IsLoopback
            && Database.SslMode is not ("VerifyCA" or "VerifyFull")
            && !Database.AllowInsecureRemote
        )
            throw new ArgumentException(
                "Для удалённой MySQL задайте SslMode=VerifyFull. AllowInsecureRemote допускает отдельный осознанный тестовый режим."
            );
        if (
            Runtime.StartupTimeoutSeconds is < 10 or > 300
            || Runtime.ShutdownTimeoutSeconds is < 1 or > 120
            || Runtime.RestartDelaySeconds is < 1 or > 60
            || Runtime.MaxRestarts is < 0 or > 10
        )
            throw new ArgumentException("Runtime: некорректные таймауты или лимит перезапусков.");
        if (
            Runtime.MaxHeapMb is < 128 or > 16384
            || Runtime.MinHeapMb < 64
            || Runtime.MinHeapMb > Runtime.MaxHeapMb
        )
            throw new ArgumentException("Runtime: некорректный размер памяти Java.");
        if (
            Testing.DefaultQuestionsCount < 1
            || !new[] { "PARTIAL", "FULL", "NONE" }.Contains(Testing.MultiAnswerMode)
        )
            throw new ArgumentException("Testing: неверные настройки тестирования.");
        if (Server.TlsEnabled && string.IsNullOrWhiteSpace(Server.KeyStorePath))
            throw new ArgumentException("Для HTTPS нужен Server.KeyStorePath (PKCS12).");
        if (Server.TlsEnabled && string.IsNullOrWhiteSpace(Server.HealthHost))
            throw new ArgumentException(
                "Для HTTPS задайте Server.HealthHost — имя/IP из сертификата, доступное этому серверу."
            );
        return this;
    }

    public static ServerConfig Load(string path) =>
        (
            JsonSerializer.Deserialize<ServerConfig>(File.ReadAllText(path), JsonOptions)
            ?? throw new ArgumentException("Пустая конфигурация.")
        ).Validate();
}

public sealed record WebConfig
{
    public string BindAddress { get; init; } = "0.0.0.0";
    public int Port { get; init; } = 8080;
    public bool TlsEnabled { get; init; }
    public string HealthHost { get; init; } = "";
    public string KeyStorePath { get; init; } = "";
    public string KeyStorePassword { get; init; } = "";
    public string ImagesDirectory { get; init; } = "data/questions";
}

public sealed record DatabaseConfig
{
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 3306;
    public string Name { get; init; } = "enterprisetesterdb";
    public string User { get; init; } = "root";
    public string Password { get; init; } = "";
    public string SslMode { get; init; } = "Preferred";
    public string CaCertificatePath { get; init; } = "";
    public bool AllowInsecureRemote { get; init; }
    public string LocalServiceName { get; init; } = "";
    public bool TryStartLocalService { get; init; } = true;

    [JsonIgnore]
    public bool IsLoopback =>
        Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || IPAddress.TryParse(Host, out var ip) && IPAddress.IsLoopback(ip);
}

public sealed record RuntimeConfig
{
    public string BackendJar { get; init; } = "backend/EnterpriseTester.jar";
    public string JavaExecutable { get; init; } = "";
    public bool AutoDownloadJava { get; init; } = true;
    public int StartupTimeoutSeconds { get; init; } = 90;
    public int ShutdownTimeoutSeconds { get; init; } = 25;
    public int MinHeapMb { get; init; } = 128;
    public int MaxHeapMb { get; init; } = 768;
    public int MaxRestarts { get; init; } = 0;
    public int RestartDelaySeconds { get; init; } = 5;
}

public sealed record TestingConfig
{
    public int DefaultQuestionsCount { get; init; } = 20;
    public bool ShowCountdown { get; init; } = true;
    public bool EnableTimeLimit { get; init; } = true;
    public string MultiAnswerMode { get; init; } = "PARTIAL";
}
