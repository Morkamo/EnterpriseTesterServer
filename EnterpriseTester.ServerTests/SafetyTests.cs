using System.IO.Compression;
using System.Text.Json;
using EnterpriseTester.ServerCore;

namespace EnterpriseTester.ServerTests;

public class SafetyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void InvalidPortsAreRejected(int port) =>
        Assert.Throws<ArgumentException>(() =>
            (new ServerConfig { Server = new WebConfig { Port = port } }).Validate()
        );

    [Theory]
    [InlineData("db;DROP TABLE users")]
    [InlineData("../db")]
    [InlineData("")]
    public void DatabaseNameCannotInjectJdbcOrSql(string name) =>
        Assert.Throws<ArgumentException>(() =>
            (new ServerConfig { Database = new DatabaseConfig { Name = name } }).Validate()
        );

    [Fact]
    public void RemoteDatabaseRequiresVerifiedTls()
    {
        Assert.Throws<ArgumentException>(() =>
            (new ServerConfig { Database = new DatabaseConfig { Host = "example.org" } }).Validate()
        );
        new ServerConfig
        {
            Database = new DatabaseConfig { Host = "example.org", SslMode = "VerifyFull" },
        }.Validate();
    }

    [Fact]
    public void ConfigurationSupportsCommentsButRejectsMisspelledFields()
    {
        var config = JsonSerializer.Deserialize<ServerConfig>(
            "{/*cfg*/\"Database\":{\"Name\":\"enterprisetesterdb\",},}",
            ServerConfig.JsonOptions
        )!;
        Assert.Equal("enterprisetesterdb", config.Validate().Database.Name);
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ServerConfig>("{\"Databse\":{}}", ServerConfig.JsonOptions)
        );
    }

    [Fact]
    public void CredentialsStayOutOfCommandArgumentsAndSchemaMutationsAreDisabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "server-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "backend"));
        File.WriteAllText(Path.Combine(root, "backend", "EnterpriseTester.jar"), "fixture");
        try
        {
            var config = new ServerConfig
            {
                Database = new DatabaseConfig { Password = "private-test-password" },
            };
            var info = BackendProcess.Build(root, config, "java");
            Assert.DoesNotContain(info.ArgumentList, a => a.Contains("private-test-password"));
            Assert.Equal("private-test-password", info.Environment["SPRING_DATASOURCE_PASSWORD"]);
            Assert.Contains("--spring.jpa.hibernate.ddl-auto=validate", info.ArgumentList);
            Assert.Contains("--spring.sql.init.mode=never", info.ArgumentList);
            Assert.Contains("--spring.flyway.enabled=false", info.ArgumentList);
            Assert.Contains("--spring.liquibase.enabled=false", info.ArgumentList);
            Assert.Contains(
                "--spring.config.location=classpath:/application.properties",
                info.ArgumentList
            );
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SecretsAreRedactedEvenInBackendErrors()
    {
        var redactor = new SecretRedactor("secret-value", "tls-value");
        var value = redactor.Scrub("password=secret-value tls-value jdbc password=other");
        Assert.DoesNotContain("secret-value", value);
        Assert.DoesNotContain("tls-value", value);
        Assert.DoesNotContain("other", value);
    }

    [Fact]
    public void SecretCanComeFromEnvironment()
    {
        var name = "ET_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(name, "some-value");
        try
        {
            Assert.Equal("some-value", ServerPaths.ResolveSecret("${ENV:" + name + "}"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("a/../../outside")]
    public void ManagedPathsCannotEscape(string path) =>
        Assert.Throws<ArgumentException>(() => ServerPaths.Child(Path.GetTempPath(), path));

    [Fact]
    public void JavaZipTraversalIsRejected()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "java-archive-tests-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            var zip = Path.Combine(root, "malicious.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("../outside.txt").Open()))
                writer.Write("bad");
            var output = Path.Combine(root, "extract");
            Directory.CreateDirectory(output);
            Assert.Throws<IOException>(() => JavaRuntime.SafeExtractZip(zip, output));
            Assert.False(File.Exists(Path.Combine(root, "outside.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
