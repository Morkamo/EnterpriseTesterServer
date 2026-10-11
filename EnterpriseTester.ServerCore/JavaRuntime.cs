using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EnterpriseTester.ServerCore;

public sealed class JavaRuntime(Action<string> report)
{
    public static async Task<int?> MajorVersionAsync(string executable,CancellationToken token=default)
    {
        try
        {
            using var process=new Process {StartInfo=new ProcessStartInfo(executable) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}};
            process.StartInfo.ArgumentList.Add("-version");process.Start();var stderr=process.StandardError.ReadToEndAsync(token);var stdout=process.StandardOutput.ReadToEndAsync(token);
            await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(10),token);
            var match=Regex.Match(await stderr+await stdout,@"version\s+""(\d+)");return process.ExitCode==0 && match.Success?int.Parse(match.Groups[1].Value):null;
        }
        catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or IOException or TimeoutException) {return null;}
    }
    public async Task<string> FindAsync(string root,RuntimeConfig config,CancellationToken token=default)
    {
        if(!string.IsNullOrWhiteSpace(config.JavaExecutable))
        {
            var explicitJava=config.JavaExecutable.Contains(Path.DirectorySeparatorChar) || config.JavaExecutable.Contains(Path.AltDirectorySeparatorChar)?ServerPaths.Resolve(root,config.JavaExecutable):config.JavaExecutable;
            if(await MajorVersionAsync(explicitJava,token)>=21)return explicitJava;
            throw new InvalidOperationException("Runtime.JavaExecutable не является доступной Java 21+. Исправьте путь или очистите параметр для автоматического выбора.");
        }
        var candidates=new List<string>();var marker=Path.Combine(root,"runtime","java-home.txt");
        if(File.Exists(marker))candidates.Add(Path.Combine(ServerPaths.Resolve(root,File.ReadAllText(marker).Trim()),"bin",OperatingSystem.IsWindows()?"java.exe":"java"));
        if(Environment.GetEnvironmentVariable("JAVA_HOME") is {Length:>0} home)candidates.Add(Path.Combine(home,"bin",OperatingSystem.IsWindows()?"java.exe":"java"));
        candidates.Add("java");
        if(OperatingSystem.IsWindows()) {candidates.Add(@"G:\Android\Android Studio\jbr\bin\java.exe");candidates.Add(@"C:\Program Files\Android\Android Studio\jbr\bin\java.exe");}
        foreach(var candidate in candidates.Distinct())if(await MajorVersionAsync(candidate,token)>=21) {report("Java 21+ найдена: "+candidate);return candidate;}
        if(!config.AutoDownloadJava)throw new InvalidOperationException("Java 21+ не найдена. Включите Runtime.AutoDownloadJava или укажите Runtime.JavaExecutable.");
        return await InstallAsync(root,token);
    }
    public async Task<string> InstallAsync(string root,CancellationToken token=default)
    {
        var os=OperatingSystem.IsWindows()?"windows":OperatingSystem.IsLinux()?"linux":throw new PlatformNotSupportedException("Поддерживаются Windows и Linux.");
        var architecture=RuntimeInformation.ProcessArchitecture switch {Architecture.X64=>"x64",Architecture.Arm64=>"aarch64",_=>throw new PlatformNotSupportedException("Нужна архитектура x64 или ARM64.")};
        var runtime=ServerPaths.Child(root,"runtime");Directory.CreateDirectory(runtime);
        using var client=new HttpClient {Timeout=TimeSpan.FromMinutes(15)};
        report("Получаем сведения о Java 21 от Adoptium…");
        var url=$"https://api.adoptium.net/v3/assets/latest/21/hotspot?architecture={architecture}&heap_size=normal&image_type=jdk&jvm_impl=hotspot&os={os}&vendor=eclipse";
        using var metadata=JsonDocument.Parse(await client.GetStringAsync(url,token));
        var release=metadata.RootElement[0];var package=release.GetProperty("binary").GetProperty("package");
        var link=package.GetProperty("link").GetString()!;var hash=package.GetProperty("checksum").GetString()!;
        var name=package.GetProperty("name").GetString()!;
        if(!link.StartsWith("https://github.com/adoptium/temurin21-binaries/releases/download/",StringComparison.Ordinal) || name!=Path.GetFileName(name) || !Regex.IsMatch(hash,"^[a-fA-F0-9]{64}$"))throw new InvalidOperationException("Неверные метаданные официального архива Java.");
        var archive=ServerPaths.Child(runtime,name);
        if(!File.Exists(archive) || !ServerPaths.HashFile(archive).Equals(hash,StringComparison.OrdinalIgnoreCase))
        {
            var partial=archive+".partial";report("Скачиваем "+release.GetProperty("release_name").GetString()+" в папку runtime (без системной установки)…");
            using var response=await client.GetAsync(link,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
            await using(var input=await response.Content.ReadAsStreamAsync(token))
            await using(var output=File.Create(partial))
            {
                var total=response.Content.Headers.ContentLength;var buffer=new byte[128*1024];long received=0;var previous=DateTime.UtcNow;
                int count;while((count=await input.ReadAsync(buffer,token))>0)
                {
                    received+=count;if(received>600L*1024*1024)throw new IOException("Слишком большой архив Java.");await output.WriteAsync(buffer.AsMemory(0,count),token);
                    if((DateTime.UtcNow-previous).TotalSeconds>=1) {report(total>0?$"Java: {received*100/total}% ({received/1048576} МБ)":$"Java: {received/1048576} МБ");previous=DateTime.UtcNow;}
                }
            }
            if(!ServerPaths.HashFile(partial).Equals(hash,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("SHA-256 Java не совпал. Архив не будет исполняться.");
            File.Move(partial,archive,true);
        }
        report("SHA-256 проверен. Распаковываем Java…");
        var extraction=ServerPaths.Child(runtime,"java-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(extraction);
        if(os=="windows")SafeExtractZip(archive,extraction);else SafeExtractTarGz(archive,extraction);
        var suffix=OperatingSystem.IsWindows()?"java.exe":"java";
        var java=Directory.EnumerateFiles(extraction,suffix,SearchOption.AllDirectories).First(p=>Path.GetFileName(Path.GetDirectoryName(p))=="bin");
        if(!(await MajorVersionAsync(java,token)>=21))throw new InvalidOperationException("Скачанная Java не прошла проверку запуска.");
        var javaHome=Path.GetDirectoryName(Path.GetDirectoryName(java))!;
        File.WriteAllText(Path.Combine(runtime,"java-home.txt"),Path.GetRelativePath(root,javaHome));report("Java готова: "+java);return java;
    }
    public static void SafeExtractZip(string archive,string directory)
    {
        using var zip=ZipFile.OpenRead(archive);
        foreach(var entry in zip.Entries)
        {
            var path=ArchivePath(directory,entry.FullName);
            if(entry.FullName.EndsWith('/')) {Directory.CreateDirectory(path);continue;}
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);entry.ExtractToFile(path,false);
        }
    }
    public static void SafeExtractTarGz(string archive,string directory)
    {
        using var file=File.OpenRead(archive);using var gzip=new GZipStream(file,CompressionMode.Decompress);using var tar=new TarReader(gzip);
        TarEntry? entry;var links=new List<(string Path,string Target)>();
        while((entry=tar.GetNextEntry()) is not null)
        {
            var path=ArchivePath(directory,entry.Name);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if(entry.EntryType==TarEntryType.Directory) {Directory.CreateDirectory(path);continue;}
            if(entry.EntryType==TarEntryType.SymbolicLink)
            {
                var target=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!,entry.LinkName));EnsureInside(directory,target);
                File.CreateSymbolicLink(path,entry.LinkName);continue;
            }
            if(entry.EntryType==TarEntryType.HardLink) {links.Add((path,ArchivePath(directory,entry.LinkName)));continue;}
            if(entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))continue;
            using(var output=new FileStream(path,FileMode.CreateNew))entry.DataStream?.CopyTo(output);
            if(!OperatingSystem.IsWindows())File.SetUnixFileMode(path,entry.Mode);
        }
        foreach(var link in links)File.Copy(link.Target,link.Path,false);
    }
    private static string ArchivePath(string directory,string name)
    {
        if(Path.IsPathRooted(name) || name.Replace('\\','/').Split('/').Contains(".."))throw new IOException("Небезопасный путь в архиве.");
        var path=Path.GetFullPath(Path.Combine(directory,name.Replace('/',Path.DirectorySeparatorChar)));EnsureInside(directory,path);return path;
    }
    private static void EnsureInside(string directory,string path)
    {if(!path.StartsWith(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new IOException("Путь архива выходит из runtime.");}
}
