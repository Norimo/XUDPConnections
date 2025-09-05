using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace XUDPConnections
{
    public class XUdpClient : IDisposable
    {
        private readonly UdpClient udpClient;
        private IPEndPoint serverEndPoint;
        private readonly ConcurrentQueue<byte[]> receivedPackets = new();
        private readonly SemaphoreSlim packetSignal = new(0);
        private readonly CancellationTokenSource cts = new();
        private DateTime lastMessageTime;
        private readonly TimeSpan timeout = TimeSpan.FromSeconds(10);

        public bool IsConnected { get; private set; }
        public bool IsDisconnected { get; private set; }

        public XUdpClient()
        {
            udpClient = new UdpClient();
            lastMessageTime = DateTime.UtcNow;
        }

        public XUdpClient(int port)
        {
            udpClient = new UdpClient(port);
            lastMessageTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Connects to the server and starts listening for messages.
        /// </summary>
        public async Task ConnectAsync(string host, int port)
        {
            if (IsConnected) return;

            serverEndPoint = new IPEndPoint(Dns.GetHostAddresses(host)[0], port);

            // Send connection request
            await udpClient.SendAsync(new byte[] { 0x01 }, 1, serverEndPoint);

            // Start background tasks
            _ = Task.Run(ReceiveLoopAsync);
            _ = MonitorTimeoutAsync();
            _ = SendPingLoopAsync();

            IsConnected = true;
        }

        /// <summary>
        /// Sends arbitrary data to the server.
        /// </summary>
        public async Task SendAsync(byte[] data)
        {
            if (!IsConnected) throw new InvalidOperationException("Not connected.");

            var packet = new byte[data.Length + 1];
            packet[0] = 0x02; // Data packet identifier
            Buffer.BlockCopy(data, 0, packet, 1, data.Length);

            await udpClient.SendAsync(packet, packet.Length, serverEndPoint);
        }

        /// <summary>
        /// Receives the next data packet from the server asynchronously.
        /// </summary>
        public async Task<byte[]?> ReceiveAsync()
        {
            await packetSignal.WaitAsync();
            if (IsDisconnected) return null;
            return receivedPackets.TryDequeue(out var data) ? data : null;
        }

        /// <summary>
        /// Disconnects from the server.
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (IsConnected && !IsDisconnected)
            {
                await SendControlAsync(0x03); // Disconnect signal
                Disconnect();
            }
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
                    case 0x02: // Data packet
                        receivedPackets.Enqueue(buffer[1..]);
                        packetSignal.Release();
                        break;

                    case 0x04: // Ping from server
                        await SendControlAsync(0x05); // Pong response
                        break;

                    case 0x05: // Pong from server
                        break;

                    case 0x03: // Server disconnected
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
                await SendControlAsync(0x04); // Ping
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
