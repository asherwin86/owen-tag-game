using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

namespace TagGame.Networking
{
    [Serializable]
    public struct DiscoveredGame
    {
        public string hostName;
        public string ipAddress;
        public ushort port;
        public float lastSeenTime;
    }

    /// <summary>
    /// WIRELESS / LAN multiplayer: no internet, no UGS Relay, no lobby code —
    /// just players on the same WiFi network (e.g. a hotspot with no internet
    /// uplink). The host broadcasts a UDP beacon; nearby clients listen for it
    /// and can connect directly by IP through UnityTransport.
    ///
    /// This intentionally does NOT depend on any Unity Gaming Services call,
    /// so it keeps working even if the device has no internet connection at all.
    /// </summary>
    public class LanDiscoveryService : MonoBehaviour
    {
        public static LanDiscoveryService Instance { get; private set; }

        private const int DiscoveryPort = 47776;
        private const string BeaconPrefix = "TAGGAME_HOST|";
        private const float BeaconIntervalSeconds = 1f;
        private const float StaleAfterSeconds = 4f;

        private UdpClient _broadcastSocket;
        private UdpClient _listenSocket;
        private Thread _listenThread;
        private volatile bool _listening;
        private float _beaconTimer;
        private bool _isBroadcastingHost;

        public event Action<DiscoveredGame> OnGameDiscovered;
        private readonly Dictionary<string, DiscoveredGame> _discovered = new Dictionary<string, DiscoveredGame>();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // ---------- Hosting on the local network ----------

        public void StartHostOnLan(ushort gamePort, string hostDisplayName)
        {
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetConnectionData("0.0.0.0", gamePort);

            NetworkManager.Singleton.StartHost();

            _isBroadcastingHost = true;
            _broadcastSocket = new UdpClient { EnableBroadcast = true };
            _beaconTimer = 0f;
            _hostDisplayName = hostDisplayName;
            _hostGamePort = gamePort;
        }

        private string _hostDisplayName;
        private ushort _hostGamePort;

        private void Update()
        {
            if (_isBroadcastingHost)
            {
                _beaconTimer -= Time.deltaTime;
                if (_beaconTimer <= 0f)
                {
                    _beaconTimer = BeaconIntervalSeconds;
                    BroadcastBeacon();
                }
            }

            // Prune stale entries so a closed game disappears from the list.
            if (_discovered.Count > 0)
            {
                var staleKeys = new List<string>();
                foreach (var kvp in _discovered)
                {
                    if (Time.unscaledTime - kvp.Value.lastSeenTime > StaleAfterSeconds)
                        staleKeys.Add(kvp.Key);
                }
                foreach (var key in staleKeys) _discovered.Remove(key);
            }
        }

        private void BroadcastBeacon()
        {
            try
            {
                string message = $"{BeaconPrefix}{_hostDisplayName}|{_hostGamePort}";
                byte[] data = Encoding.UTF8.GetBytes(message);
                _broadcastSocket.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LAN] Beacon broadcast failed: {e.Message}");
            }
        }

        // ---------- Discovering games as a client ----------

        public void StartListeningForGames()
        {
            if (_listening) return;
            _listening = true;
            _discovered.Clear();

            _listenSocket = new UdpClient(DiscoveryPort) { EnableBroadcast = true };
            _listenThread = new Thread(ListenLoop) { IsBackground = true };
            _listenThread.Start();
        }

        public void StopListeningForGames()
        {
            _listening = false;
            _listenSocket?.Close();
            _listenThread = null;
        }

        private void ListenLoop()
        {
            var remoteEndPoint = new IPEndPoint(IPAddress.Any, DiscoveryPort);
            while (_listening)
            {
                try
                {
                    byte[] data = _listenSocket.Receive(ref remoteEndPoint);
                    string message = Encoding.UTF8.GetString(data);
                    if (!message.StartsWith(BeaconPrefix)) continue;

                    string[] parts = message.Substring(BeaconPrefix.Length).Split('|');
                    if (parts.Length != 2 || !ushort.TryParse(parts[1], out ushort port)) continue;

                    var game = new DiscoveredGame
                    {
                        hostName = parts[0],
                        ipAddress = remoteEndPoint.Address.ToString(),
                        port = port,
                        lastSeenTime = Time.unscaledTime
                    };

                    _discovered[game.ipAddress] = game;
                    OnGameDiscovered?.Invoke(game);
                }
                catch (SocketException)
                {
                    break; // socket closed via StopListeningForGames
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[LAN] Discovery listen error: {e.Message}");
                }
            }
        }

        public void JoinGame(DiscoveredGame game)
        {
            StopListeningForGames();
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetConnectionData(game.ipAddress, game.port);
            NetworkManager.Singleton.StartClient();
        }

        public void StopHostingBeacon()
        {
            _isBroadcastingHost = false;
            _broadcastSocket?.Close();
            _broadcastSocket = null;
        }

        private void OnDestroy()
        {
            StopListeningForGames();
            StopHostingBeacon();
        }
    }
}
