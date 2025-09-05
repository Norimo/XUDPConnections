using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace XUDPConnections
{
    public class XUdpClient : IDisposable
    {
        private readonly UdpClient udpClient;
        private readonly IPEndPoint serverEndPoint;
        private readonly ConcurrentQueue<byte[]> receivedPackets = new();
        private readonly SemaphoreSlim packetSignal = new(0);
        private readonly CancellationTokenSource cts = new();
        private DateTime lastMessageTime;
        private readonly TimeSpan timeout = TimeSpan.FromSeconds(10);

        public bool IsConnected { get; private set; }
        public bool IsDisconnected { get; private set; }

        public XUdpClient(string host, int port)
        {
            udpClient = new UdpClient();
            serverEndPoint = new IPEndPoint(Dns.GetHostAddresses(host)[0], port);
            lastMessageTime = DateTime.UtcNow;
        }

        public async Task ConnectAsync()
        {
            if (IsConnected) return;

            await udpClient.SendAsync(new byte[] { 0x01 }, 1, serverEndPoint);

            _ = Task.Run(ReceiveLoopAsync);
            _ = MonitorTimeoutAsync();
            _ = SendPingLoopAsync();

            IsConnected = true;
        }

        private async Task ReceiveLoopAsync()
        {
            while (!cts.IsCancellationRequested && !IsDisconnected)
            {
                var result = await udpClient.ReceiveAsync();
                var buffer = result.Buffer;

                if (buffer.Length == 0) continue;

                lastMessageTime = DateTime.UtcNow;

                switch (buffer[0])
                {
                    case 0x02:
                        receivedPackets.Enqueue(buffer.Skip(1).ToArray());
                        packetSignal.Release();
                        break;
                    case 0x04: // ping
                        await SendControlAsync(0x05);
                        break;
                    case 0x05: // pong
                        break;
                    case 0x03: // disconnect
                        Disconnect();
                        return;
                }
            }
        }

        private async Task SendPingLoopAsync()
        {
            while (!IsDisconnected && !cts.IsCancellationRequested)
            {
                await Task.Delay(3000, cts.Token);
                await SendControlAsync(0x04);
            }
        }

        private async Task MonitorTimeoutAsync()
        {
            while (!IsDisconnected && !cts.IsCancellationRequested)
            {
                await Task.Delay(2000, cts.Token);
                if (DateTime.UtcNow - lastMessageTime > timeout)
                    Disconnect();
            }
        }

        private async Task SendControlAsync(byte code)
        {
            var packet = new byte[] { code };
            await udpClient.SendAsync(packet, packet.Length, serverEndPoint);
        }

        public async Task SendAsync(byte[] data)
        {
            if (!IsConnected) throw new InvalidOperationException("Not connected.");
            var packet = new byte[data.Length + 1];
            packet[0] = 0x02;
            Buffer.BlockCopy(data, 0, packet, 1, data.Length);
            await udpClient.SendAsync(packet, packet.Length, serverEndPoint);
        }

        public async Task<byte[]?> ReceiveAsync()
        {
            await packetSignal.WaitAsync();
            if (IsDisconnected) return null;
            return receivedPackets.TryDequeue(out var data) ? data : null;
        }

        public async Task DisconnectAsync()
        {
            if (IsConnected && !IsDisconnected)
            {
                await SendControlAsync(0x03);
                Disconnect();
            }
        }

        private void Disconnect()
        {
            IsDisconnected = true;
            packetSignal.Release();
            cts.Cancel();
        }

        public void Dispose()
        {
            _ = DisconnectAsync();
            udpClient?.Close();
        }
    }
}
