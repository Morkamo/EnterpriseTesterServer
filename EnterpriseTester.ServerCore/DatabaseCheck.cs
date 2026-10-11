using System.Text;
using System.Security.Cryptography;
using MySqlConnector;

namespace EnterpriseTester.ServerCore;

public sealed record DatabaseReport(string Version,int Tables,int ImageReferences,string SchemaHash);
public static class DatabaseCheck
{
    private static readonly string[] RequiredTables=["answers","attempt_options","attempt_question_images","attempt_questions","question_images","questions","test_attempts","test_questions","test_testers","tests","user_roles","users"];
    public static async Task<DatabaseReport> CheckAsync(DatabaseConfig config,CancellationToken token=default,string? root=null)
    {
        var builder=new MySqlConnectionStringBuilder {Server=config.Host,Port=(uint)config.Port,Database=config.Name,UserID=config.User,
            Password=ServerPaths.ResolveSecret(config.Password),ConnectionTimeout=5,DefaultCommandTimeout=10,Pooling=false,
            SslMode=Enum.Parse<MySqlSslMode>(config.SslMode)};
        if(!string.IsNullOrWhiteSpace(config.CaCertificatePath))builder.SslCa=ServerPaths.Resolve(root??Environment.CurrentDirectory,config.CaCertificatePath);
        await using var connection=new MySqlConnection(builder.ConnectionString); await connection.OpenAsync(token);
        await using var command=connection.CreateCommand();
        command.CommandText="SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA=@db ORDER BY TABLE_NAME";command.Parameters.AddWithValue("db",config.Name);
        var tables=new List<string>(); await using(var reader=await command.ExecuteReaderAsync(token))while(await reader.ReadAsync(token))tables.Add(reader.GetString(0));
        var missing=RequiredTables.Except(tables).ToArray();if(missing.Length>0)throw new InvalidOperationException("В существующей базе отсутствуют таблицы: "+string.Join(", ",missing)+". Автоматический импорт и изменение схемы отключены.");
        var schema=new StringBuilder(); command.Parameters.Clear();
        foreach(var table in RequiredTables)
        {
            command.CommandText="SHOW CREATE TABLE `"+table+"`"; await using var reader=await command.ExecuteReaderAsync(token);await reader.ReadAsync(token);
            schema.Append(System.Text.RegularExpressions.Regex.Replace(reader.GetString(1),@" AUTO_INCREMENT=\d+",""));
        }
        command.CommandText="SELECT COUNT(*) FROM question_images";var images=Convert.ToInt32(await command.ExecuteScalarAsync(token));
        return new DatabaseReport(connection.ServerVersion,tables.Count,images,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(schema.ToString()))));
    }
}
