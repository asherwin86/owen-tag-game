using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TagGame.Networking;

namespace TagGame.UI
{
    /// <summary>
    /// Minimal main-menu wiring for the three ways to play:
    /// Practice (offline vs AI), Online (Relay + Lobby code), Wireless/LAN.
    /// Hook these methods up to Buttons in your menu Canvas.
    /// </summary>
    public class MultiplayerMenuUI : MonoBehaviour
    {
        [Header("Online")]
        [SerializeField] private TMP_InputField playerNameField;
        [SerializeField] private TMP_InputField lobbyCodeField;
        [SerializeField] private TextMeshProUGUI onlineStatusText;

        [Header("Wireless / LAN")]
        [SerializeField] private Transform lanGameListContent;
        [SerializeField] private Button lanGameListItemPrefab;
        [SerializeField] private TextMeshProUGUI lanStatusText;

        private readonly Dictionary<string, DiscoveredGame> _knownGames = new Dictionary<string, DiscoveredGame>();

        private void OnEnable()
        {
            if (LanDiscoveryService.Instance != null)
            {
                LanDiscoveryService.Instance.OnGameDiscovered += HandleGameDiscovered;
            }
        }

        private void OnDisable()
        {
            if (LanDiscoveryService.Instance != null)
            {
                LanDiscoveryService.Instance.OnGameDiscovered -= HandleGameDiscovered;
            }
        }

        // ---------- Online (Relay + Lobby) ----------

        public async void OnClickHostOnline()
        {
            onlineStatusText.text = "Creating online game...";
            string name = string.IsNullOrWhiteSpace(playerNameField.text) ? "Player" : playerNameField.text;
            string code = await LobbyRelayService.Instance.CreateOnlineGameAsync(name);
            onlineStatusText.text = $"Share this code: {code}";
        }

        public async void OnClickJoinOnline()
        {
            if (string.IsNullOrWhiteSpace(lobbyCodeField.text))
            {
                onlineStatusText.text = "Enter a lobby code first.";
                return;
            }
            onlineStatusText.text = "Joining...";
            await LobbyRelayService.Instance.JoinOnlineGameAsync(lobbyCodeField.text.Trim().ToUpperInvariant());
            onlineStatusText.text = "Joined!";
        }

        public async void OnClickQuickMatch()
        {
            onlineStatusText.text = "Finding a match...";
            bool found = await LobbyRelayService.Instance.QuickJoinAsync();
            onlineStatusText.text = found ? "Joined!" : "No open games right now — try hosting instead.";
        }

        // ---------- Wireless / LAN ----------

        public void OnClickHostLan()
        {
            lanStatusText.text = "Hosting on local network...";
            string name = string.IsNullOrWhiteSpace(playerNameField.text) ? "Player" : playerNameField.text;
            LanDiscoveryService.Instance.StartHostOnLan(7777, name);
        }

        public void OnClickScanForLanGames()
        {
            lanStatusText.text = "Scanning local network...";
            _knownGames.Clear();
            foreach (Transform child in lanGameListContent) Destroy(child.gameObject);
            LanDiscoveryService.Instance.StartListeningForGames();
        }

        private void HandleGameDiscovered(DiscoveredGame game)
        {
            if (_knownGames.ContainsKey(game.ipAddress)) return; // already listed
            _knownGames[game.ipAddress] = game;

            var button = Instantiate(lanGameListItemPrefab, lanGameListContent);
            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = $"{game.hostName} ({game.ipAddress})";
            button.onClick.AddListener(() => LanDiscoveryService.Instance.JoinGame(game));
        }
    }
}
