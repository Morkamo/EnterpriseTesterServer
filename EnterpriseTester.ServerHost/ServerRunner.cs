using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using EnterpriseTester.ServerCore;
namespace EnterpriseTester.ServerHost;
internal static class ServerRunner
{
    [DllImport("libc",SetLastError=true)]private static extern int kill(int pid,int signal);
    public static async Task<int> RunAsync(string root,string configPath,bool interactive)
    {
        var config=ServerConfig.Load(configPath);using var log=new ConsoleLog(root,ServerPaths.ResolveSecret(config.Database.Password),ServerPaths.ResolveSecret(config.Server.KeyStorePassword));
        var runtime=ServerPaths.Child(root,"runtime");Directory.CreateDirectory(runtime);using FileStream instance=AcquireLock(Path.Combine(runtime,"host.lock"));
        using var stop=new CancellationTokenSource();ConsoleCancelEventHandler cancel=(_,e)=>{e.Cancel=true;stop.Cancel();};Console.CancelKeyPress+=cancel;
        var marker=Path.Combine(runtime,"stop.request");if(File.Exists(marker))File.Delete(marker);var control=ControlAsync(stop,marker,interactive,log);
        try
        {
            log.Write("EnterpriseTester Dedicated Server 1.0 · "+RuntimeInformation.OSDescription);log.Write("Конфигурация: "+configPath+" · пароль скрыт");
            if(IsPortOccupied(config.Server.BindAddress,config.Server.Port))throw new InvalidOperationException("Порт "+config.Server.Port+" занят. Возможно, сервер запущен из desktop/IDE. Чужой процесс не будет остановлен.");
            log.Write("Проверяем MySQL: "+config.Database.Host+":"+config.Database.Port+"/"+config.Database.Name);
            DatabaseReport database;try{database=await DatabaseCheck.CheckAsync(config.Database,stop.Token,root);}catch(MySqlConnector.MySqlException ex) when(ex.Number is 0 or 1042){await MysqlService.TryStartAsync(config.Database,log,stop.Token);database=await DatabaseCheck.CheckAsync(config.Database,stop.Token,root);}
            log.Write($"MySQL {database.Version}, таблиц: {database.Tables}. Проверка без DDL успешна.");
            var images=ServerPaths.Resolve(root,config.Server.ImagesDirectory);Directory.CreateDirectory(images);
            if(database.ImageReferences>0 && !Directory.EnumerateFiles(images).Any())throw new InvalidOperationException("В базе есть изображения, но каталог "+images+" пуст. Скопируйте файлы или укажите общий каталог.");
            var java=await new JavaRuntime(log.Write).FindAsync(root,config.Runtime,stop.Token);BackendImport.Verify(root,ServerPaths.Resolve(root,config.Runtime.BackendJar));
            await DatabaseTrust.PrepareAsync(root,config,java,stop.Token);
            var host=config.Server.BindAddress is "0.0.0.0" or "::"?config.Server.BindAddress=="::"?"[::1]":"127.0.0.1":config.Server.BindAddress.Contains(':')?"["+config.Server.BindAddress+"]":config.Server.BindAddress;
            if(!string.IsNullOrWhiteSpace(config.Server.HealthHost))host=config.Server.HealthHost.Contains(':')?"["+config.Server.HealthHost+"]":config.Server.HealthHost;
            var address=new Uri($"{(config.Server.TlsEnabled?"https":"http")}://{host}:{config.Server.Port}/");using var health=new HttpHealth();
            for(var attempt=0;;attempt++)
            {
                stop.Token.ThrowIfCancellationRequested();using var process=new Process{StartInfo=BackendProcess.Build(root,config,java)};
                log.Write("Запускаем backend. Создание схемы и миграции отключены.");process.Start();var stdout=DrainAsync(process.StandardOutput,log);var stderr=DrainAsync(process.StandardError,log);
                try
                {
                    var deadline=DateTime.UtcNow.AddSeconds(config.Runtime.StartupTimeoutSeconds);var ready=false;
                    while(!process.HasExited && DateTime.UtcNow<deadline && !stop.IsCancellationRequested){if(await health.CheckAsync(address,stop.Token)){ready=true;break;}await Task.Delay(500,stop.Token);}
                    if(!ready)throw new InvalidOperationException(process.HasExited?"Backend завершился при запуске. Причина в журнале выше.":"Backend не ответил вовремя. Проверьте журнал и сертификат HTTPS.");
                    File.WriteAllText(Path.Combine(runtime,"server-state.json"),JsonSerializer.Serialize(new{javaPid=process.Id,hostPid=Environment.ProcessId,address=address.AbsoluteUri,startedUtc=DateTimeOffset.UtcNow}));
                    log.Write("ГОТОВО. Сервер принимает подключения.");PrintAddresses(config,log);log.Write("Журнал: "+log.Path+" · остановка: Ctrl+C или stop");
                    while(!process.HasExited && !stop.IsCancellationRequested)await Task.Delay(300,stop.Token);
                }
                finally
                {
                    if(!process.HasExited)
                    {
                        log.Write("Останавливаем свой Java-процесс…");if(OperatingSystem.IsLinux())kill(process.Id,15);
                        try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(OperatingSystem.IsWindows()?3:config.Runtime.ShutdownTimeoutSeconds));}
                        catch(TimeoutException){log.Write("Принудительная остановка после таймаута.");process.Kill(true);await process.WaitForExitAsync();}
                    }
                    await Task.WhenAll(stdout,stderr);var state=Path.Combine(runtime,"server-state.json");if(File.Exists(state))File.Delete(state);
                }
                if(stop.IsCancellationRequested)break;
                if(attempt>=config.Runtime.MaxRestarts)throw new InvalidOperationException("Backend неожиданно завершился; лимит перезапусков исчерпан.");
                log.Write("Повтор запуска через "+config.Runtime.RestartDelaySeconds+" с…");await Task.Delay(config.Runtime.RestartDelaySeconds*1000,stop.Token);
            }
            return 0;
        }
        catch(OperationCanceledException) when(stop.IsCancellationRequested){log.Write("Сервер остановлен.");return 0;}
        finally{stop.Cancel();Console.CancelKeyPress-=cancel;await control;}
    }
    private static FileStream AcquireLock(string path){try{return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException){throw new InvalidOperationException("Пакет уже запущен. Используйте status или stop.");}}
    private static bool IsPortOccupied(string address,int port){try{var listener=new TcpListener(IPAddress.Parse(address),port);listener.Start();listener.Stop();return false;}catch(SocketException){return true;}}
    private static async Task DrainAsync(StreamReader reader,ConsoleLog log){while(await reader.ReadLineAsync() is { } line)log.Write(line);}
    private static async Task ControlAsync(CancellationTokenSource stop,string marker,bool interactive,ConsoleLog log)
    {
        if(interactive)_=Task.Run(async()=>{try{while(!stop.IsCancellationRequested){var line=await Console.In.ReadLineAsync(stop.Token);if(line is null)return;if(line.Trim().Equals("stop",StringComparison.OrdinalIgnoreCase)){stop.Cancel();return;}if(line.Trim().Equals("status",StringComparison.OrdinalIgnoreCase))log.Write("Управляющий процесс активен.");}}catch(OperationCanceledException){}});
        try{while(!stop.IsCancellationRequested){if(File.Exists(marker)){File.Delete(marker);stop.Cancel();break;}await Task.Delay(300,stop.Token);}}catch(OperationCanceledException){}
    }
    private static void PrintAddresses(ServerConfig config,ConsoleLog log)
    {
        var scheme=config.Server.TlsEnabled?"https":"http";log.Write($"На этом ПК: {scheme}://localhost:{config.Server.Port}/");if(config.Server.BindAddress is not ("0.0.0.0" or "::")){log.Write("Прослушиваем только "+config.Server.BindAddress);return;}
        foreach(var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up))foreach(var item in adapter.GetIPProperties().UnicastAddresses.Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address)))log.Write($"LAN [{adapter.Name}]: {scheme}://{item.Address}:{config.Server.Port}/");
    }
}
