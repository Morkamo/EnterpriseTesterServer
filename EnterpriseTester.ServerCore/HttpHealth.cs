using System.Net;
using System.Text.Json;

namespace EnterpriseTester.ServerCore;

public sealed class HttpHealth : IDisposable
{
    private readonly HttpClient client=new(new HttpClientHandler{AllowAutoRedirect=false,CookieContainer=new CookieContainer()}) {Timeout=TimeSpan.FromSeconds(4)};
    public async Task<bool> CheckAsync(Uri uri,CancellationToken token=default)
    {
        try
        {
            using var response=await client.GetAsync(new Uri(uri,"api/auth/csrf"),HttpCompletionOption.ResponseHeadersRead,token);
            if(!response.IsSuccessStatusCode || response.Content.Headers.ContentLength>65536)return false;
            await using var stream=await response.Content.ReadAsStreamAsync(token);var bytes=new byte[65537];var length=0;
            while(length<bytes.Length) {var count=await stream.ReadAsync(bytes.AsMemory(length),token);if(count==0)break;length+=count;}
            if(length>65536)return false;using var json=JsonDocument.Parse(bytes.AsMemory(0,length));
            return json.RootElement.TryGetProperty("token",out var value) && value.ValueKind==JsonValueKind.String;
        }
        catch(Exception ex) when(ex is HttpRequestException or IOException or JsonException or OperationCanceledException) {return false;}
    }
    public void Dispose()=>client.Dispose();
}
