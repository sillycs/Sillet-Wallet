// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Sillet.Net
{
    internal static class ProxyBridge
    {
        private static readonly object Gate = new object();
        private static TcpListener _listener;
        private static string _target;

        public static int Ensure(string socksHost, int socksPort)
        {
            lock (Gate)
            {
                var target = socksHost + ":" + socksPort;
                if (_listener != null && _target == target) return ((IPEndPoint)_listener.LocalEndpoint).Port;
                if (_listener != null) _listener.Stop();
                _target = target;
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                var listener = _listener;
                Task.Run(() => AcceptLoop(listener, socksHost, socksPort));
                return ((IPEndPoint)_listener.LocalEndpoint).Port;
            }
        }

        private static async Task AcceptLoop(TcpListener listener, string socksHost, int socksPort)
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception e) when (e is ObjectDisposedException || e is SocketException || e is InvalidOperationException)
                {
                    return;
                }
                var ignored = Task.Run(() => Handle(client, socksHost, socksPort));
            }
        }

        private static async Task Handle(TcpClient client, string socksHost, int socksPort)
        {
            TcpClient upstream = null;
            try
            {
                var stream = client.GetStream();
                var head = await ReadHead(stream).ConfigureAwait(false);
                var first = head.Split('\n')[0].Trim().Split(' ');
                if (first.Length < 2 || !first[0].Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
                {
                    await Write(stream, "HTTP/1.1 405 Method Not Allowed\r\nConnection: close\r\n\r\n").ConfigureAwait(false);
                    return;
                }
                var colon = first[1].LastIndexOf(':');
                if (colon <= 0) return;
                var host = first[1].Substring(0, colon);
                int port;
                if (!int.TryParse(first[1].Substring(colon + 1), out port) || port <= 0 || port > 65535) return;
                upstream = new TcpClient();
                await upstream.ConnectAsync(socksHost, socksPort).ConfigureAwait(false);
                var up = upstream.GetStream();
                if (!await Socks5Connect(up, host, port).ConfigureAwait(false))
                {
                    await Write(stream, "HTTP/1.1 502 Bad Gateway\r\nConnection: close\r\n\r\n").ConfigureAwait(false);
                    return;
                }
                await Write(stream, "HTTP/1.1 200 Connection established\r\n\r\n").ConfigureAwait(false);
                var a = stream.CopyToAsync(up);
                var b = up.CopyToAsync(stream);
                await Task.WhenAny(a, b).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException || e is SocketException || e is ObjectDisposedException || e is InvalidOperationException)
            {
            }
            finally
            {
                try { client.Close(); } catch (Exception e) when (e is SocketException || e is ObjectDisposedException) { }
                try { if (upstream != null) upstream.Close(); } catch (Exception e) when (e is SocketException || e is ObjectDisposedException) { }
            }
        }

        private static async Task<string> ReadHead(Stream stream)
        {
            var sb = new StringBuilder();
            var one = new byte[1];
            while (sb.Length < 8192)
            {
                if (await stream.ReadAsync(one, 0, 1).ConfigureAwait(false) == 0) break;
                sb.Append((char)one[0]);
                if (sb.Length >= 4 && sb[sb.Length - 1] == '\n' && sb[sb.Length - 2] == '\r' && sb[sb.Length - 3] == '\n' && sb[sb.Length - 4] == '\r') break;
            }
            return sb.ToString();
        }

        private static async Task Write(Stream stream, string text)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(text);
            await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        }

        private static async Task<bool> Socks5Connect(Stream s, string host, int port)
        {
            await s.WriteAsync(new byte[] { 5, 1, 0 }, 0, 3).ConfigureAwait(false);
            var reply = await ReadExact(s, 2).ConfigureAwait(false);
            if (reply == null || reply[0] != 5 || reply[1] != 0) return false;
            var name = System.Text.Encoding.ASCII.GetBytes(host);
            if (name.Length == 0 || name.Length > 255) return false;
            var request = new byte[7 + name.Length];
            request[0] = 5; request[1] = 1; request[2] = 0; request[3] = 3; request[4] = (byte)name.Length;
            Buffer.BlockCopy(name, 0, request, 5, name.Length);
            request[5 + name.Length] = (byte)(port >> 8);
            request[6 + name.Length] = (byte)(port & 0xff);
            await s.WriteAsync(request, 0, request.Length).ConfigureAwait(false);
            var head = await ReadExact(s, 4).ConfigureAwait(false);
            if (head == null || head[0] != 5 || head[1] != 0) return false;
            int skip;
            if (head[3] == 1) skip = 4 + 2;
            else if (head[3] == 4) skip = 16 + 2;
            else if (head[3] == 3)
            {
                var len = await ReadExact(s, 1).ConfigureAwait(false);
                if (len == null) return false;
                skip = len[0] + 2;
            }
            else return false;
            return await ReadExact(s, skip).ConfigureAwait(false) != null;
        }

        private static async Task<byte[]> ReadExact(Stream s, int count)
        {
            var buffer = new byte[count];
            int read = 0;
            while (read < count)
            {
                var n = await s.ReadAsync(buffer, read, count - read).ConfigureAwait(false);
                if (n == 0) return null;
                read += n;
            }
            return buffer;
        }
    }
}
