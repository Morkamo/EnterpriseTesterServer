using System.IO.Compression;
using EnterpriseTester.ServerHost;

namespace EnterpriseTester.ServerTests;

public sealed class BackendPackagingTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "backend-package-" + Guid.NewGuid().ToString("N")
    );

    [Fact]
    public void ImportProducesPortableVerifiedPackageWithoutDatabaseCredentials()
    {
        ImportFixture();
        var jar = Path.Combine(root, "backend", "EnterpriseTester.jar");
        BackendImport.Verify(root, jar);
        using var archive = ZipFile.OpenRead(jar);
        using var reader = new StreamReader(
            archive.GetEntry("BOOT-INF/classes/application.properties")!.Open()
        );
        var properties = reader.ReadToEnd();
        Assert.Contains("spring.datasource.password=${SPRING_DATASOURCE_PASSWORD}", properties);
        Assert.Contains("spring.datasource.username=${SPRING_DATASOURCE_USERNAME}", properties);
        Assert.Contains("spring.datasource.url=${SPRING_DATASOURCE_URL}", properties);
        Assert.DoesNotContain("fixture-secret", properties);
        Assert.Contains("spring.jpa.hibernate.ddl-auto=validate", properties);
    }

    [Fact]
    public void MissingJarExplainsThatBothBackendFilesMustBeTransferred()
    {
        var error = Assert.Throws<FileNotFoundException>(() =>
            BackendImport.Verify(root, Path.Combine(root, "backend", "EnterpriseTester.jar"))
        );
        Assert.Contains("backend/EnterpriseTester.jar", error.Message);
        Assert.Contains("backend/backend.json", error.Message);
    }

    [Fact]
    public void MissingManifestDoesNotDisableIntegrityValidation()
    {
        ImportFixture();
        File.Delete(Path.Combine(root, "backend", "backend.json"));
        Assert.Throws<InvalidOperationException>(() =>
            BackendImport.Verify(root, Path.Combine(root, "backend", "EnterpriseTester.jar"))
        );
    }

    [Fact]
    public void ModifiedJarIsRejected()
    {
        ImportFixture();
        var jar = Path.Combine(root, "backend", "EnterpriseTester.jar");
        File.AppendAllText(jar, "unexpected modification");
        Assert.Throws<InvalidOperationException>(() => BackendImport.Verify(root, jar));
    }

    private void ImportFixture()
    {
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source.jar");
        using (var archive = ZipFile.Open(source, ZipArchiveMode.Create))
        using (
            var writer = new StreamWriter(
                archive.CreateEntry("BOOT-INF/classes/application.properties").Open()
            )
        )
        {
            writer.WriteLine("spring.datasource.url=jdbc:mysql://fixture-host/fixture-db");
            writer.WriteLine("spring.datasource.username=fixture-user");
            writer.WriteLine("spring.datasource.password=fixture-secret");
        }
        BackendImport.Import(root, source);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}
