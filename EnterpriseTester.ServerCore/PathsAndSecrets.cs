using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace EnterpriseTester.ServerCore;

public static class ServerPaths
{
    public static string Resolve(string root, string value) =>
        Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(root, value));

    public static string Child(string root, string relative)
    {
        var resolved = Resolve(root, relative);
        var prefix =
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (
            !resolved.StartsWith(
                prefix,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal
            )
        )
            throw new ArgumentException("Путь выходит из каталога пакета.");
        return resolved;
    }

    public static string ResolveSecret(string value)
    {
        if (!value.StartsWith("${ENV:", StringComparison.Ordinal) || !value.EndsWith('}'))
            return value;
        var name = value[6..^1];
        return Environment.GetEnvironmentVariable(name)
            ?? throw new ArgumentException("Не задана переменная окружения " + name + ".");
    }

    public static void ProtectFile(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public static string HashFile(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }
}

public sealed class SecretRedactor(params string[] secrets)
{
    public string Scrub(string value)
    {
        foreach (
            var secret in secrets
                .Where(s => !string.IsNullOrEmpty(s))
                .OrderByDescending(s => s.Length)
        )
            value = value.Replace(secret, "***", StringComparison.Ordinal);
        return Regex.Replace(value, @"(?i)(password\s*[=:]\s*)([^\s,;]+)", "$1***");
    }
}
