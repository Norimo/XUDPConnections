# XUDPConnections

A lightweight C# library that provides a **TCP-like connection API over UDP**.  
It wraps `UdpClient` with connection management, keep-alive (ping/pong), and a familiar `Listener`/`Client` pattern.

## ✨ Features

- `XUdpListener` works like `TcpListener`
- `XUdpClient` works like `TcpClient`
- Automatic **ping/pong keep-alive**
- **Timeout detection** if no packets are received
- Async/await friendly API
- Easy to use, minimal dependencies (`System.Net.Sockets`)

---

## 🚀 Getting Started

### Install from source
Build and pack the library:

```sh
dotnet pack -c Release
```

This will produce:

```
bin/Release/XUDPConnections.1.0.0.nupkg
```

You can install it locally:

```sh
dotnet add package XUDPConnections --source ./bin/Release
```

---

## 📦 Usage Example

### Server
```csharp
using XUDPConnections;

var listener = new XUdpListener(5460);
listener.Start();

listener.ConnectionAccepted += async (conn) =>
{
    Console.WriteLine($"Client connected: {conn.RemoteEndPoint}");

    while (true)
    {
        var data = await conn.ReceiveAsync();
        Console.WriteLine($"Received: {System.Text.Encoding.UTF8.GetString(data)}");

        await conn.SendAsync(System.Text.Encoding.UTF8.GetBytes("Pong"));
    }
};
```

### Client
```csharp
using XUDPConnections;

var client = new XUdpClient();
await client.ConnectAsync("127.0.0.1", 5460);

await client.SendAsync(System.Text.Encoding.UTF8.GetBytes("Ping"));

var response = await client.ReceiveAsync();
Console.WriteLine($"Server replied: {System.Text.Encoding.UTF8.GetString(response)}");
```

---

## ⚡ Keep-Alive

The library automatically exchanges **ping/pong** messages in the background.  
If no packets are received within the configured timeout, the connection is closed.

---

## 🛠 Development

Clone the repo:

```sh
git clone https://github.com/yourname/XUDPConnections.git
cd XUDPConnections
dotnet build
```

Run tests (if added):

```sh
dotnet test
```

---

## 📜 License

MIT License – do whatever you want, but no warranty.

---

## 👤 Author

**Ivan Vyshniak**

