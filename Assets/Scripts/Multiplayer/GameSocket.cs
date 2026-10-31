using System;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#else
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
#endif

namespace TagGame.Multiplayer
{
    /// <summary>Tiny WebSocket wrapper. Browser builds use the .jslib bridge; the editor and
    /// standalone builds use System.Net.WebSockets. Poll TryReceive from Update.</summary>
    public static class GameSocket
    {
        public const int Connecting = 0, Open = 1, Closed = 3;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void TG_Connect(string url);
        [DllImport("__Internal")] private static extern int TG_State();
        [DllImport("__Internal")] private static extern void TG_Send(string msg);
        [DllImport("__Internal")] private static extern string TG_Recv();
        [DllImport("__Internal")] private static extern void TG_Close();

        public static void Connect(string url) => TG_Connect(url);
        public static int State => TG_State();
        public static void Send(string msg) { if (TG_State() == Open) TG_Send(msg); }
        public static bool TryReceive(out string msg) { msg = TG_Recv(); return msg != null; }
        public static void Close() => TG_Close();
#else
        private static ClientWebSocket _ws;
        private static ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
        private static int _state = Closed;
        private static readonly object SendLock = new object();

        public static int State => _state;

        public static void Connect(string url)
        {
            Close();
            _queue = new ConcurrentQueue<string>();
            _state = Connecting;
            RunAsync(url, _ws = new ClientWebSocket(), _queue);
        }

        private static async void RunAsync(string url, ClientWebSocket ws, ConcurrentQueue<string> queue)
        {
            try
            {
                await ws.ConnectAsync(new Uri(url), CancellationToken.None);
                if (ws == _ws) _state = Open;
                var buf = new byte[8192];
                while (ws.State == WebSocketState.Open)
                {
                    var sb = new StringBuilder();
                    WebSocketReceiveResult r;
                    do
                    {
                        r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
                        if (r.MessageType == WebSocketMessageType.Close) break;
                        sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                    } while (!r.EndOfMessage);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    if (sb.Length > 0) queue.Enqueue(sb.ToString());
                }
            }
            catch (Exception) { }
            if (ws == _ws) _state = Closed;
        }

        public static void Send(string msg)
        {
            var ws = _ws;
            if (ws == null || ws.State != WebSocketState.Open) return;
            try
            {
                lock (SendLock)
                {
                    var bytes = Encoding.UTF8.GetBytes(msg);
                    ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).Wait(1000);
                }
            }
            catch (Exception) { }
        }

        public static bool TryReceive(out string msg) => _queue.TryDequeue(out msg);

        public static void Close()
        {
            var ws = _ws;
            _ws = null;
            _state = Closed;
            if (ws != null) { try { ws.Abort(); ws.Dispose(); } catch (Exception) { } }
        }
#endif
    }
}
