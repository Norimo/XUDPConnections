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
List<XUdpConnection> connections = new();

while (true)
{
    var conn = await listener.AcceptConnectionAsync();
    connections.Add(conn);

    _ = Task.Run(async () =>
    {
        Console.WriteLine($"Client connected: {conn.RemoteEndPoint}");

        while (true)
        {
            var data = await conn.ReceiveAsync();
            Console.WriteLine($"Received: {System.Text.Encoding.UTF8.GetString(data)} from: {conn.RemoteEndPoint}");

            foreach (var c in connections)
            {
                if (c != conn)
                    await c.SendAsync(data);
            }
        }
    });
}
```

### Client
```csharp
using XUDPConnections;

var client = new XUdpClient();
await client.ConnectAsync("10.0.10.80", 5460);

_ = Task.Run(async () =>
{
    while (true)
    {
        var data = await client.ReceiveAsync();
        Console.WriteLine($"Client received: {System.Text.Encoding.UTF8.GetString(data)}");
    }
});

while (true)
{
    string? line = Console.ReadLine();
    await client.SendAsync(System.Text.Encoding.UTF8.GetBytes(line));
}
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

