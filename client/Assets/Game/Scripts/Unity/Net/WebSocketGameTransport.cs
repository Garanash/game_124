using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Realmforge.Client.Core.Net;
using UnityEngine;

namespace Realmforge.Client.Unity.Net
{
    /// <summary>
    /// Browser WebSocket for WebGL builds, where raw UDP is unavailable. The jslib queues frames per socket;
    /// <see cref="Poll"/> drains them on the main thread so callers see the same contract as UDP.
    /// </summary>
    public sealed class WebSocketGameTransport : IGameTransport
    {
        private const string Path = "/ws";
        private const int MaxFramesPerPoll = 256;
        private const int StateOpen = 1;
        private const int StateClosed = 3;

        private readonly Queue<byte[]> _pending = new Queue<byte[]>();
        private int _socketId = -1;
        private bool _announcedOpen;

        public event Action? Connected;
        public event Action<byte[]>? Received;
        public event Action<string>? Disconnected;

        public bool IsConnected => _socketId >= 0 && RealmforgeSocketState(_socketId) == StateOpen;

        public void Connect(string host, int udpPort, int webSocketPort)
        {
            Disconnect();
            string scheme = Application.absoluteURL.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
            _socketId = RealmforgeSocketOpen($"{scheme}://{host}:{webSocketPort}{Path}");
            _announcedOpen = false;
        }

        public void Send(byte[] payload, bool reliable)
        {
            if (IsConnected)
            {
                RealmforgeSocketSend(_socketId, payload, payload.Length);
            }
        }

        public void Poll()
        {
            if (_socketId < 0)
            {
                return;
            }
            int state = RealmforgeSocketState(_socketId);
            if (state == StateOpen && !_announcedOpen)
            {
                _announcedOpen = true;
                Connected?.Invoke();
            }
            DrainFrames();
            while (_pending.Count > 0)
            {
                Received?.Invoke(_pending.Dequeue());
            }
            if (state == StateClosed)
            {
                RealmforgeSocketRelease(_socketId);
                _socketId = -1;
                Disconnected?.Invoke("closed");
            }
        }

        public void Disconnect()
        {
            if (_socketId < 0)
            {
                return;
            }
            RealmforgeSocketClose(_socketId);
            RealmforgeSocketRelease(_socketId);
            _socketId = -1;
        }

        private void DrainFrames()
        {
            for (int frame = 0; frame < MaxFramesPerPoll; frame++)
            {
                int length = RealmforgeSocketNextFrameLength(_socketId);
                if (length < 0)
                {
                    return;
                }
                var buffer = new byte[length];
                RealmforgeSocketReadFrame(_socketId, buffer, length);
                _pending.Enqueue(buffer);
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int RealmforgeSocketOpen(string url);
        [DllImport("__Internal")] private static extern int RealmforgeSocketState(int socketId);
        [DllImport("__Internal")] private static extern void RealmforgeSocketSend(int socketId, byte[] data, int length);
        [DllImport("__Internal")] private static extern int RealmforgeSocketNextFrameLength(int socketId);
        [DllImport("__Internal")] private static extern void RealmforgeSocketReadFrame(int socketId, byte[] buffer, int length);
        [DllImport("__Internal")] private static extern void RealmforgeSocketClose(int socketId);
        [DllImport("__Internal")] private static extern void RealmforgeSocketRelease(int socketId);
#else
        private static int RealmforgeSocketOpen(string url) => throw new PlatformNotSupportedException("WebSocket transport is WebGL-only; native builds use UDP.");
        private static int RealmforgeSocketState(int socketId) => StateClosed;
        private static void RealmforgeSocketSend(int socketId, byte[] data, int length) { }
        private static int RealmforgeSocketNextFrameLength(int socketId) => -1;
        private static void RealmforgeSocketReadFrame(int socketId, byte[] buffer, int length) { }
        private static void RealmforgeSocketClose(int socketId) { }
        private static void RealmforgeSocketRelease(int socketId) { }
#endif
    }
}
