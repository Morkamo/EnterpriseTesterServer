using System.Net;
using System.Net.Sockets;
var listener=new TcpListener(IPAddress.Loopback,13306);listener.Start();Console.WriteLine("Verification MySQL relay: 127.0.0.1:13306");
while(true){var client=await listener.AcceptTcpClientAsync();_=Copy(client);}
static async Task Copy(TcpClient client){using(client)using(var backend=new TcpClient()){try{await backend.ConnectAsync(IPAddress.Loopback,3306);var a=client.GetStream();var b=backend.GetStream();await Task.WhenAny(a.CopyToAsync(b),b.CopyToAsync(a));}catch(IOException){}catch(SocketException){}}}
