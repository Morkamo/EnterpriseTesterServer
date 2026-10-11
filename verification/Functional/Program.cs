using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using EnterpriseTester.ServerCore;
using MySqlConnector;
var config=ServerConfig.Load("config/server.json");var before=await DatabaseCheck.CheckAsync(config.Database);
var cs=new MySqlConnectionStringBuilder{Server=config.Database.Host,Port=(uint)config.Database.Port,Database=config.Database.Name,UserID=config.Database.User,Password=ServerPaths.ResolveSecret(config.Database.Password)};
await using var connection=new MySqlConnection(cs.ConnectionString);await connection.OpenAsync();
var email="dedicated-check-"+Guid.NewGuid().ToString("N")+"@example.invalid";var password=Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
await using var insert=connection.CreateCommand();insert.CommandText="INSERT INTO users(first_name,last_name,email,password,user_role_id,deleted) VALUES('Dedicated','Verifier',@email,@password,2,0)";
insert.Parameters.AddWithValue("email",email);insert.Parameters.AddWithValue("password",BCrypt.Net.BCrypt.HashPassword(password));await insert.ExecuteNonQueryAsync();var id=insert.LastInsertedId;
try{
 foreach(var address in new[]{"http://localhost:8080/","http://localhost:8082/"}){
  using var client=new HttpClient(new HttpClientHandler{CookieContainer=new CookieContainer()}){BaseAddress=new Uri(address)};
  using var csrf=JsonDocument.Parse(await client.GetStringAsync("api/auth/csrf"));
  using var bad=await client.PostAsJsonAsync("api/auth/login",new{email,password});if(bad.StatusCode!=HttpStatusCode.Forbidden)throw new Exception("CSRF not enforced");
  using var request=new HttpRequestMessage(HttpMethod.Post,"api/auth/login"){Content=JsonContent.Create(new{email,password})};request.Headers.Add("X-CSRF-TOKEN",csrf.RootElement.GetProperty("token").GetString());
  using var response=await client.SendAsync(request);using var login=JsonDocument.Parse(await response.Content.ReadAsStringAsync());if(!login.RootElement.GetProperty("success").GetBoolean())throw new Exception("Login failed");
  var management=await client.GetAsync("management");if(management.StatusCode!=HttpStatusCode.OK)throw new Exception("Management inaccessible");
  Console.WriteLine("PASS "+address+" login, CSRF and admin management");
 }
}finally{await using var delete=connection.CreateCommand();delete.CommandText="DELETE FROM users WHERE id=@id AND email=@email";delete.Parameters.AddWithValue("id",id);delete.Parameters.AddWithValue("email",email);await delete.ExecuteNonQueryAsync();}
var after=await DatabaseCheck.CheckAsync(config.Database);if(before.SchemaHash!=after.SchemaHash)throw new Exception("Schema mismatch");Console.WriteLine("PASS schema unchanged; temporary account removed");
