using System.Text;
using EnterpriseTester.ServerCore;
using EnterpriseTester.ServerHost;
Console.OutputEncoding=new UTF8Encoding(false);Console.InputEncoding=new UTF8Encoding(false);
string? Value(string flag){var i=Array.IndexOf(args,flag);return i>=0 && i+1<args.Length?args[i+1]:null;}
var root=Path.GetFullPath(Value("--root")??Environment.CurrentDirectory);var configPath=ServerPaths.Resolve(root,Value("--config")??"config/server.json");
var command=args.Contains("--help")?"help":args.FirstOrDefault(a=>!a.StartsWith('-') && a!=Value("--root") && a!=Value("--config") && a!=Value("--source"))??"start";
try
{
    if(command=="help"){Console.WriteLine("start | configure | create-config | setup-local | doctor | status | stop | install-java | import-backend --source file.jar\nПараметры: --root folder --config file --non-interactive");return 0;}
    if(command=="import-backend"){BackendImport.Import(root,Value("--source")??throw new ArgumentException("Укажите --source JAR."));Console.WriteLine("Backend импортирован; встроенные параметры БД очищены.");return 0;}
    if(command is "create-config" or "setup-local")
    {
        if(File.Exists(configPath))throw new InvalidOperationException("Конфигурация уже существует и не будет перезаписана.");var config=new ServerConfig();if(command=="setup-local")config=config with{Database=config.Database with{Password=Configuration.DesktopPassword()??""}};
        Configuration.Save(configPath,config);Console.WriteLine("Создан приватный конфиг "+configPath+"; пароль не выводится.");return 0;
    }
    if(command=="configure"){Configuration.Save(configPath,Configuration.Wizard(File.Exists(configPath)?ServerConfig.Load(configPath):new ServerConfig()));Console.WriteLine("Настройки сохранены.");return 0;}
    if(command=="install-java"){await new JavaRuntime(Console.WriteLine).InstallAsync(root);return 0;}
    if(!File.Exists(configPath)){Configuration.Save(configPath,new ServerConfig());Console.WriteLine("Создан config/server.json. Укажите параметры существующей базы.");if(Console.IsInputRedirected || args.Contains("--non-interactive"))return 2;Configuration.Save(configPath,Configuration.Wizard(ServerConfig.Load(configPath)));}
    var settings=ServerConfig.Load(configPath);
    if(command=="stop"){if(!File.Exists(ServerPaths.Child(root,"runtime/server-state.json"))){Console.WriteLine("Нет запущенного backend этого пакета.");return 0;}File.WriteAllText(ServerPaths.Child(root,"runtime/stop.request"),"stop");Console.WriteLine("Запрошена остановка своего сервера.");return 0;}
    if(command=="status")
    {
        using var health=new HttpHealth();var host=settings.Server.BindAddress is "0.0.0.0" or "::"?"127.0.0.1":settings.Server.BindAddress;if(!string.IsNullOrWhiteSpace(settings.Server.HealthHost))host=settings.Server.HealthHost;var uri=new Uri($"{(settings.Server.TlsEnabled?"https":"http")}://{host}:{settings.Server.Port}/");var available=await health.CheckAsync(uri);Console.WriteLine(available?"EnterpriseTester отвечает: "+uri:"Сервер не отвечает: "+uri);return available?0:3;
    }
    if(command=="doctor"){var report=await DatabaseCheck.CheckAsync(settings.Database);Console.WriteLine($"MySQL {report.Version}; таблиц: {report.Tables}; SHA-256 схемы: {report.SchemaHash}");Console.WriteLine("Java: "+await new JavaRuntime(Console.WriteLine).FindAsync(root,settings.Runtime));BackendImport.Verify(root,ServerPaths.Resolve(root,settings.Runtime.BackendJar));Console.WriteLine("Контрольная сумма backend верна; DDL не выполнялся.");return 0;}
    if(command!="start")throw new ArgumentException("Неизвестная команда: "+command);return await ServerRunner.RunAsync(root,configPath,!Console.IsInputRedirected && !args.Contains("--non-interactive"));
}
catch(Exception error){Console.Error.WriteLine("ОШИБКА: "+new SecretRedactor(SafeSecrets(configPath)).Scrub(error.Message));return 1;}
static string[] SafeSecrets(string path){try{var config=ServerConfig.Load(path);return [ServerPaths.ResolveSecret(config.Database.Password),ServerPaths.ResolveSecret(config.Server.KeyStorePassword)];}catch{return [];}}
