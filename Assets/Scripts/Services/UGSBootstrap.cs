using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;

namespace TagGame.Services
{
    /// <summary>
    /// Boots Unity Gaming Services once at game start:
    /// UnityServices.InitializeAsync() -> Authentication sign-in -> everything
    /// else (Cloud Code, Remote Config, Leaderboards, Lobby, Relay) becomes safe to call.
    /// Put this on a persistent bootstrap object (DontDestroyOnLoad) that loads
    /// before your menu/gameplay scenes.
    /// </summary>
    public class UGSBootstrap : MonoBehaviour
    {
        public static UGSBootstrap Instance { get; private set; }

        public bool IsReady { get; private set; }
        public event Action OnReady;

        // Several scripts (this one's own Start, RemoteConfigManager, LeaderboardManager)
        // can all call InitializeAsync() on the same frame before any of them finishes.
        // Cache the in-flight task so concurrent callers share one sign-in attempt
        // instead of racing — AuthenticationService throws if a second sign-in is
        // started while the first is still in progress.
        private Task _initializationTask;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private async void Start()
        {
            await InitializeAsync();
        }

        public Task InitializeAsync()
        {
            if (IsReady)
            {
                return Task.CompletedTask;
            }

            _initializationTask ??= InitializeInternalAsync();
            return _initializationTask;
        }

        private async Task InitializeInternalAsync()
        {
            try
            {
                await UnityServices.InitializeAsync();

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    // Anonymous sign-in is enough for leaderboards/cloud save/relay.
                    // Swap in a social/Unity sign-in flow later if you want
                    // persistent cross-device identity.
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                Debug.Log($"[UGS] Signed in as {AuthenticationService.Instance.PlayerId}");
                IsReady = true;
                OnReady?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError($"[UGS] Initialization failed: {e}");
                _initializationTask = null; // allow a retry on the next call
            }
        }
    }
}
