using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace XUDPConnections
{
    public class XUdpListener : IDisposable
    {
        private readonly UdpClient udpClient;
        private readonly ConcurrentDictionary<IPEndPoint, XUdpConnection> connections;
        private readonly CancellationTokenSource cts;

        public int Port { get; }
        public XUdpListener(int port)
        {
            Port = port;
            udpClient = new UdpClient(port);
            connections = new ConcurrentDictionary<IPEndPoint, XUdpConnection>();
            cts = new CancellationTokenSource();
        }

        public void Start()
        {
            _ = Task.Run(ReceiveLoopAsync);
        }

        public async Task<XUdpConnection> AcceptConnectionAsync()
        {
            while (!cts.IsCancellationRequested)
            {
                foreach (var conn in connections.Values)
                {
                    if (!conn.HandedOut)
                    {
                        conn.HandedOut = true;
                        return conn;
                    }
                }

                await Task.Delay(50, cts.Token);
            }

            throw new OperationCanceledException("Listener stopped.");
        }

        private async Task ReceiveLoopAsync()
        {
            while (!cts.IsCancellationRequested)
            {
                var result = await udpClient.ReceiveAsync();
                var buffer = result.Buffer;

                if (buffer.Length == 0) continue;

                if (!connections.TryGetValue(result.RemoteEndPoint, out var conn))
                {
                    if (buffer[0] == 0x01) // connection request
                    {
                        conn = new XUdpConnection(udpClient, result.RemoteEndPoint);
                        connections[result.RemoteEndPoint] = conn;
                    }
                    else continue; // ignore packets from unknown endpoints
                }

                conn.HandlePacket(buffer);
            }
        }

        public void Dispose()
        {
            cts.Cancel();
            udpClient.Close();
        }

    }
    public class XUdpConnection
    {
        private readonly UdpClient udpClient;
        private readonly IPEndPoint remoteEndPoint;
        private readonly ConcurrentQueue<byte[]> receivedPackets = new();
        private readonly SemaphoreSlim packetSignal = new(0);
        private readonly CancellationTokenSource cts = new();
        private DateTime lastMessageTime;
        private readonly TimeSpan timeout = TimeSpan.FromSeconds(10);

        public bool IsConnected { get; private set; } = true;
        public bool HandedOut { get; set; } = false;
        public IPEndPoint RemoteEndPoint => remoteEndPoint;

        public XUdpConnection(UdpClient udpClient, IPEndPoint remoteEndPoint)
        {
            this.udpClient = udpClient;
            this.remoteEndPoint = remoteEndPoint;
            lastMessageTime = DateTime.UtcNow;

            _ = SendPingLoopAsync();
            _ = MonitorTimeoutAsync();
        }

        internal void HandlePacket(byte[] buffer)
        {
            lastMessageTime = DateTime.UtcNow;

            switch (buffer[0])
            {
                case 0x02: // data
                    receivedPackets.Enqueue(buffer.Skip(1).ToArray());
                    packetSignal.Release();
                    break;
                case 0x04: // ping
                    _ = SendControlAsync(0x05);
                    break;
                case 0x05: // pong
                    break;
                case 0x03: // disconnect
                    Disconnect();
                    break;
            }
        }

        private async Task SendPingLoopAsync()
        {
            while (IsConnected && !cts.IsCancellationRequested)
            {
                await Task.Delay(3000, cts.Token);
                await SendControlAsync(0x04);
            }
        }

        private async Task MonitorTimeoutAsync()
        {
            while (IsConnected && !cts.IsCancellationRequested)
            {
                await Task.Delay(2000, cts.Token);
                if (DateTime.UtcNow - lastMessageTime > timeout)
                    Disconnect();
            }
        }

        private async Task SendControlAsync(byte code)
        {
            var packet = new byte[] { code };
            await udpClient.SendAsync(packet, packet.Length, remoteEndPoint);
        }

        public async Task SendAsync(byte[] data)
        {
            if (!IsConnected) throw new InvalidOperationException("Not connected");
            var packet = new byte[data.Length + 1];
            packet[0] = 0x02;
            Buffer.BlockCopy(data, 0, packet, 1, data.Length);
            await udpClient.SendAsync(packet, packet.Length, remoteEndPoint);
        }

        public async Task<byte[]?> ReceiveAsync()
        {
            await packetSignal.WaitAsync();
            if (!IsConnected) return null;
            return receivedPackets.TryDequeue(out var data) ? data : null;
        }

        public async Task DisconnectAsync()
        {
            if (IsConnected)
            {
                await SendControlAsync(0x03);
                Disconnect();
            }
        }

        private void Disconnect()
        {
            IsConnected = false;
            packetSignal.Release();
            cts.Cancel();
        }
    }
}
