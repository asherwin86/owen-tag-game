using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using TagGame.Services;

namespace TagGame.Networking
{
    /// <summary>
    /// Server-authoritative round controller for real player-vs-player tag,
    /// whether the session came from Online (Relay) or Wireless/LAN hosting —
    /// this script doesn't care which transport connected the players.
    ///
    /// Only the server ever writes IsIt or ends the round; clients just render
    /// state and request tags, which the server validates.
    /// </summary>
    public class NetworkTagGameManager : NetworkBehaviour
    {
        public static NetworkTagGameManager Instance { get; private set; }

        [SerializeField] private float roundDurationSeconds = 90f;
        [SerializeField] private float minSecondsBetweenTagSwaps = 1.5f; // prevents instant tag-back

        public NetworkVariable<float> TimeRemaining = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<ulong> CurrentItClientId = new NetworkVariable<ulong>(
            ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly Dictionary<ulong, float> _timeSpentAsIt = new Dictionary<ulong, float>();
        private float _lastTagSwapTime = -999f;
        private bool _roundRunning;

        private void Awake()
        {
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            // Pull round length from Remote Config on the server before starting.
            _ = InitializeRoundFromRemoteConfigAsync();
        }

        private async System.Threading.Tasks.Task InitializeRoundFromRemoteConfigAsync()
        {
            var config = await RemoteConfigManager.Instance.FetchTagGameConfigAsync();
            if (config != null)
            {
                roundDurationSeconds = config.roundDurationSeconds;
            }
            StartRoundServer();
        }

        private void StartRoundServer()
        {
            if (!IsServer) return;

            var clientIds = NetworkManager.Singleton.ConnectedClientsIds;
            if (clientIds.Count == 0) return;

            _timeSpentAsIt.Clear();
            foreach (var id in clientIds) _timeSpentAsIt[id] = 0f;

            ulong firstIt = clientIds.ElementAt(Random.Range(0, clientIds.Count));
            AssignIt(firstIt);

            TimeRemaining.Value = roundDurationSeconds;
            _roundRunning = true;
        }

        private void Update()
        {
            if (!IsServer || !_roundRunning) return;

            TimeRemaining.Value = Mathf.Max(0f, TimeRemaining.Value - Time.deltaTime);

            if (CurrentItClientId.Value != ulong.MaxValue && _timeSpentAsIt.ContainsKey(CurrentItClientId.Value))
            {
                _timeSpentAsIt[CurrentItClientId.Value] += Time.deltaTime;
            }

            if (TimeRemaining.Value <= 0f)
            {
                EndRoundServer();
            }
        }

        /// <summary>Called by NetworkPlayerController after server-side distance/state validation passes.</summary>
        public void ServerHandleTag(NetworkPlayerController tagger, NetworkPlayerController target)
        {
            if (!IsServer || !_roundRunning) return;
            if (Time.time - _lastTagSwapTime < minSecondsBetweenTagSwaps) return;

            tagger.IsIt.Value = false;
            target.IsIt.Value = true;
            CurrentItClientId.Value = target.OwnerClientId;
            _lastTagSwapTime = Time.time;
        }

        private void AssignIt(ulong clientId)
        {
            foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
            {
                var controller = kvp.Value.PlayerObject?.GetComponent<NetworkPlayerController>();
                if (controller != null)
                {
                    controller.IsIt.Value = (kvp.Key == clientId);
                }
            }
            CurrentItClientId.Value = clientId;
        }

        private async void EndRoundServer()
        {
            _roundRunning = false;

            // Whoever spent the LEAST time as "it" wins the round (best evader).
            var winner = _timeSpentAsIt.OrderBy(kvp => kvp.Value).FirstOrDefault();

            EndRoundClientRpc(winner.Key);

            // Submit each player's "time spent evading" (round length minus their
            // time as it) as their survival score, same Cloud Code path used by
            // practice mode, so online/LAN games and solo practice share one leaderboard.
            foreach (var kvp in _timeSpentAsIt)
            {
                float evadedTime = roundDurationSeconds - kvp.Value;
                bool wasWinner = kvp.Key == winner.Key;
                // Only the server can attribute time-as-it per client; submission
                // still happens through Cloud Code for the anti-cheat ceiling check.
                await LeaderboardManager.Instance.SubmitSurvivalTimeAsync(evadedTime, wasWinner);
            }
        }

        [ClientRpc]
        private void EndRoundClientRpc(ulong winnerClientId)
        {
            Debug.Log(NetworkManager.Singleton.LocalClientId == winnerClientId
                ? "[TagGame] You won the round — best evader!"
                : "[TagGame] Round over.");
        }

        /// <summary>Call from a UI button once players have gathered, or auto-start when the lobby fills.</summary>
        public void RequestStartRound()
        {
            if (IsServer) StartRoundServer();
        }
    }
}
