using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using TagGame.Services;

namespace TagGame.Networking
{
    /// <summary>
    /// ONLINE multiplayer: create/join a Lobby (for discovery + a short room
    /// code players can share) and connect the actual game traffic through
    /// Unity Relay, so neither host nor joiners need port forwarding or a
    /// public IP. This is the "play with a friend over the internet" path.
    /// </summary>
    public class LobbyRelayService : MonoBehaviour
    {
        public static LobbyRelayService Instance { get; private set; }

        private const string RelayJoinCodeKey = "relayJoinCode";
        private const int MaxPlayers = 8;

        private Lobby _currentLobby;
        private float _heartbeatTimer;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            // Lobbies expire without a heartbeat; only the host needs to send one.
            if (_currentLobby == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            {
                return;
            }

            _heartbeatTimer -= Time.deltaTime;
            if (_heartbeatTimer <= 0f)
            {
                _heartbeatTimer = 15f;
                _ = LobbyService.Instance.SendHeartbeatPingAsync(_currentLobby.Id);
            }
        }

        /// <summary>Host: allocate a Relay server, wrap it in a joinable Lobby, and start hosting.</summary>
        public async Task<string> CreateOnlineGameAsync(string hostDisplayName)
        {
            await EnsureSignedInAsync();

            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MaxPlayers - 1);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            var relayServerData = BuildRelayServerData(allocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            var options = new CreateLobbyOptions
            {
                IsPrivate = false,
                Data = new Dictionary<string, DataObject>
                {
                    { RelayJoinCodeKey, new DataObject(DataObject.VisibilityOptions.Member, joinCode) }
                }
            };
            _currentLobby = await LobbyService.Instance.CreateLobbyAsync($"{hostDisplayName}'s Tag Game", MaxPlayers, options);
            _heartbeatTimer = 15f;

            NetworkManager.Singleton.StartHost();
            Debug.Log($"[Online] Hosting lobby {_currentLobby.LobbyCode} (share this code with friends).");
            return _currentLobby.LobbyCode;
        }

        /// <summary>Client: join by the short lobby code the host shared, then connect via the same Relay allocation.</summary>
        public async Task JoinOnlineGameAsync(string lobbyCode)
        {
            await EnsureSignedInAsync();

            _currentLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode);
            string relayJoinCode = _currentLobby.Data[RelayJoinCodeKey].Value;

            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(relayJoinCode);
            var relayServerData = BuildRelayServerData(joinAllocation, "dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            NetworkManager.Singleton.StartClient();
            Debug.Log($"[Online] Joined lobby {lobbyCode}.");
        }

        /// <summary>Quick match: join any open public lobby instead of typing a code.</summary>
        public async Task<bool> QuickJoinAsync()
        {
            await EnsureSignedInAsync();
            try
            {
                _currentLobby = await LobbyService.Instance.QuickJoinLobbyAsync();
                string relayJoinCode = _currentLobby.Data[RelayJoinCodeKey].Value;

                JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(relayJoinCode);
                var relayServerData = BuildRelayServerData(joinAllocation, "dtls");
                NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

                NetworkManager.Singleton.StartClient();
                return true;
            }
            catch (LobbyServiceException)
            {
                return false; // no open lobbies right now
            }
        }

        public async Task LeaveAsync()
        {
            if (_currentLobby == null) return;
            try
            {
                if (NetworkManager.Singleton.IsHost)
                {
                    await LobbyService.Instance.DeleteLobbyAsync(_currentLobby.Id);
                }
                else
                {
                    await LobbyService.Instance.RemovePlayerAsync(_currentLobby.Id, AuthenticationService.Instance.PlayerId);
                }
            }
            finally
            {
                _currentLobby = null;
                if (NetworkManager.Singleton.IsListening) NetworkManager.Singleton.Shutdown();
            }
        }

        private async Task EnsureSignedInAsync()
        {
            if (UGSBootstrap.Instance != null && !UGSBootstrap.Instance.IsReady)
            {
                await UGSBootstrap.Instance.InitializeAsync();
            }
        }

        // Unity's own AllocationUtils.ToRelayServerData() extension method would do this,
        // but it only ships inside com.unity.services.multiplayer, whose Runtime asmdef
        // hard-references Netcode for Entities / Unity.Entities — packages this project
        // doesn't have and doesn't need just for Relay. This reproduces the same
        // conversion using only the standalone Relay package + Transport's own
        // RelayServerData constructor, which has no such dependency.
        private static RelayServerData BuildRelayServerData(Allocation allocation, string connectionType)
        {
            var endpoint = FindEndpoint(allocation.ServerEndpoints, connectionType);
            return new RelayServerData(
                endpoint.Host,
                (ushort)endpoint.Port,
                allocation.AllocationIdBytes,
                allocation.ConnectionData,
                allocation.ConnectionData, // host allocation has no separate host-connection-data
                allocation.Key,
                endpoint.Secure);
        }

        private static RelayServerData BuildRelayServerData(JoinAllocation allocation, string connectionType)
        {
            var endpoint = FindEndpoint(allocation.ServerEndpoints, connectionType);
            return new RelayServerData(
                endpoint.Host,
                (ushort)endpoint.Port,
                allocation.AllocationIdBytes,
                allocation.ConnectionData,
                allocation.HostConnectionData,
                allocation.Key,
                endpoint.Secure);
        }

        private static RelayServerEndpoint FindEndpoint(List<RelayServerEndpoint> endpoints, string connectionType)
        {
            if (endpoints != null)
            {
                foreach (var endpoint in endpoints)
                {
                    if (string.Equals(endpoint.ConnectionType, connectionType, StringComparison.OrdinalIgnoreCase))
                    {
                        return endpoint;
                    }
                }
            }
            throw new ArgumentException($"No Relay endpoint found for connection type \"{connectionType}\".");
        }
    }
}
