using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EnterpriseTester.ServerCore;

namespace EnterpriseTester.ServerHost;

internal static class BackendImport
{
    public static void Import(string root, string source)
    {
        if (!File.Exists(source))
            throw new FileNotFoundException("Не найден исходный JAR.");
        var destination = ServerPaths.Child(root, "backend/EnterpriseTester.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".tmp";
        File.Copy(source, temporary, true);
        using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Update))
        {
            var entry =
                archive.GetEntry("BOOT-INF/classes/application.properties")
                ?? throw new InvalidOperationException("Это не Spring Boot JAR EnterpriseTester.");
            using var reader = new StreamReader(entry.Open());
            var properties = reader.ReadToEnd();
            reader.Dispose();
            entry.Delete();
            properties = Regex.Replace(
                properties,
                @"(?m)^spring\.datasource\.(url|username|password)=.*\r?$",
                match =>
                    "spring.datasource."
                    + match.Groups[1].Value
                    + "=${SPRING_DATASOURCE_"
                    + (
                        match.Groups[1].Value == "username"
                            ? "USERNAME"
                            : match.Groups[1].Value.ToUpperInvariant()
                    )
                    + "}"
            );
            properties +=
                "\nspring.jpa.hibernate.ddl-auto=validate\nspring.sql.init.mode=never\nspring.flyway.enabled=false\nspring.liquibase.enabled=false\n";
            using var writer = new StreamWriter(
                archive.CreateEntry("BOOT-INF/classes/application.properties").Open(),
                new UTF8Encoding(false)
            );
            writer.Write(properties);
        }
        File.Move(temporary, destination, true);
        File.WriteAllText(
            Path.Combine(root, "backend", "backend.json"),
            JsonSerializer.Serialize(
                new
                {
                    sourceName = Path.GetFileName(source),
                    sourceSha256 = ServerPaths.HashFile(source),
                    packageSha256 = ServerPaths.HashFile(destination),
                    importedUtc = DateTimeOffset.UtcNow,
                },
                new JsonSerializerOptions { WriteIndented = true }
            )
        );
    }

    public static void Verify(string root, string jar)
    {
        var manifest = Path.Combine(root, "backend", "backend.json");
        if (!File.Exists(manifest))
            throw new InvalidOperationException(
                "Нет backend/backend.json. Выполните import-backend."
            );
        using var json = JsonDocument.Parse(File.ReadAllText(manifest));
        if (
            !ServerPaths
                .HashFile(jar)
                .Equals(
                    json.RootElement.GetProperty("packageSha256").GetString(),
                    StringComparison.OrdinalIgnoreCase
                )
        )
            throw new InvalidOperationException(
                "JAR не соответствует backend.json. Выполните import-backend для намеренного обновления."
            );
    }
}
