using System.Diagnostics;

namespace EnterpriseTester.ServerCore;

public static class BackendProcess
{
    public static ProcessStartInfo Build(string root,ServerConfig config,string java)
    {
        config.Validate(); var jar=ServerPaths.Resolve(root,config.Runtime.BackendJar);
        if(!File.Exists(jar))throw new FileNotFoundException("Не найден backend JAR: "+jar);
        var info=new ProcessStartInfo(java) { WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=false,
            RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=false };
        foreach(var argument in new[]{"-Djava.awt.headless=true","-Denterprisetester.home="+root,"-Xms"+config.Runtime.MinHeapMb+"m","-Xmx"+config.Runtime.MaxHeapMb+"m","-jar",jar,
            "--server.address="+config.Server.BindAddress,"--server.port="+config.Server.Port,
            "--spring.config.location=classpath:/application.properties",
            "--spring.jpa.hibernate.ddl-auto=validate","--spring.jpa.generate-ddl=false","--spring.sql.init.mode=never",
            "--spring.flyway.enabled=false","--spring.liquibase.enabled=false",
            "--testing.upload-dir="+ServerPaths.Resolve(root,config.Server.ImagesDirectory),
            "--testing.default-questions-count="+config.Testing.DefaultQuestionsCount,"--testing.show-countdown="+config.Testing.ShowCountdown.ToString().ToLowerInvariant(),
            "--testing.enable-time-limit="+config.Testing.EnableTimeLimit.ToString().ToLowerInvariant(),"--testing.multi-answer-mode="+config.Testing.MultiAnswerMode,
            "--server.servlet.session.cookie.http-only=true","--server.servlet.session.cookie.same-site=strict",
            "--server.servlet.session.cookie.secure="+config.Server.TlsEnabled.ToString().ToLowerInvariant(),
            "--server.ssl.enabled="+config.Server.TlsEnabled.ToString().ToLowerInvariant(),
            "--server.error.include-message=never","--server.error.include-stacktrace=never","--server.shutdown=graceful",
            "--spring.lifecycle.timeout-per-shutdown-phase=15s","--spring.datasource.hikari.maximum-pool-size=10","--spring.datasource.hikari.minimum-idle=2"})info.ArgumentList.Add(argument);
        var host=config.Database.Host.Contains(':')?"["+config.Database.Host+"]":config.Database.Host;
        var ssl=config.Database.SslMode switch {"Disabled"=>"DISABLED","VerifyCA"=>"VERIFY_CA","VerifyFull"=>"VERIFY_IDENTITY",_=>config.Database.SslMode.ToUpperInvariant()};
        var jdbc=$"jdbc:mysql://{host}:{config.Database.Port}/{config.Database.Name}?sslMode={ssl}&connectTimeout=5000&socketTimeout=30000&connectionTimeZone=UTC";
        info.Environment["SPRING_DATASOURCE_URL"]=jdbc;
        info.Environment["SPRING_DATASOURCE_USERNAME"]=config.Database.User;
        info.Environment["SPRING_DATASOURCE_PASSWORD"]=ServerPaths.ResolveSecret(config.Database.Password);
        if(config.Server.TlsEnabled)
        {
            info.Environment["SERVER_SSL_KEY_STORE"]=new Uri(ServerPaths.Resolve(root,config.Server.KeyStorePath)).AbsoluteUri;
            info.Environment["SERVER_SSL_KEY_STORE_TYPE"]="PKCS12";
            info.Environment["SERVER_SSL_KEY_STORE_PASSWORD"]=ServerPaths.ResolveSecret(config.Server.KeyStorePassword);
        }
        // Inherited Spring/Hibernate settings must not override our configuration or enable a migration by accident.
        foreach(var name in info.Environment.Keys.Where(k=>k.StartsWith("SPRING_",StringComparison.OrdinalIgnoreCase) || k.StartsWith("SERVER_",StringComparison.OrdinalIgnoreCase) || k.StartsWith("TESTING_",StringComparison.OrdinalIgnoreCase) || k is "JAVA_TOOL_OPTIONS" or "_JAVA_OPTIONS" or "JDK_JAVA_OPTIONS").ToArray())
            if(name is not ("SPRING_DATASOURCE_URL" or "SPRING_DATASOURCE_USERNAME" or "SPRING_DATASOURCE_PASSWORD" or "SERVER_SSL_KEY_STORE" or "SERVER_SSL_KEY_STORE_TYPE" or "SERVER_SSL_KEY_STORE_PASSWORD"))info.Environment.Remove(name);
        if(!string.IsNullOrWhiteSpace(config.Database.CaCertificatePath))info.Environment["JAVA_TOOL_OPTIONS"]="-Djavax.net.ssl.trustStore=\""+ServerPaths.Child(root,"runtime/mysql-trust.p12")+"\" -Djavax.net.ssl.trustStoreType=PKCS12 -Djavax.net.ssl.trustStorePassword=changeit";
        return info;
    }
}
