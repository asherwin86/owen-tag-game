using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TagGame.Services;

namespace TagGame.Gameplay
{
    /// <summary>
    /// Practice-mode (offline, vs AI) round controller. Player survives against
    /// N AI taggers for a round; survival time is submitted to the leaderboard
    /// through Cloud Code. For real player-vs-player tag, see
    /// Networking/NetworkTagGameManager.cs instead.
    /// </summary>
    public class TagGameManager : MonoBehaviour
    {
        [Header("Scene refs")]
        [SerializeField] private PlayerController player;
        [SerializeField] private TaggerAI taggerPrefab;
        [SerializeField] private Transform[] taggerSpawnPoints;

        [Header("Fallback settings (overridden by Remote Config if available)")]
        [SerializeField] private int taggerCount = 2;
        [SerializeField] private float roundDurationSeconds = 60f;

        public bool RoundInProgress { get; private set; }
        public bool IsPaused { get; private set; }
        public float TimeRemaining { get; private set; }
        public event Action<float> OnTimeRemainingChanged;
        public event Action<bool> OnRoundEnded; // true = player survived
        public event Action<bool> OnPauseChanged; // true = now paused

        private readonly List<TaggerAI> _activeTaggers = new List<TaggerAI>();
        private float _elapsed;

        private async void Start()
        {
            // Pull live-tunable difficulty from Remote Config, falling back to
            // the serialized defaults if the service isn't reachable.
            var config = await RemoteConfigManager.Instance.FetchTagGameConfigAsync();
            if (config != null)
            {
                taggerCount = config.taggerCount;
                roundDurationSeconds = config.roundDurationSeconds;
            }

            // Don't auto-start: the Start screen calls BeginGame() once the
            // player clicks Start, so config has time to arrive first.
        }

        /// <summary>Called by the Start screen's Start button.</summary>
        public void BeginGame()
        {
            StartRound();
        }

        /// <summary>Called by the Pause screen's Restart button and the Game Over screen's Play Again button.</summary>
        public void RestartRound()
        {
            Time.timeScale = 1f;
            IsPaused = false;
            StartRound();
        }

        public void StartRound()
        {
            foreach (var tagger in _activeTaggers)
            {
                if (tagger != null) Destroy(tagger.gameObject);
            }
            _activeTaggers.Clear();

            player.ResetForNewRound();
            _elapsed = 0f;
            TimeRemaining = roundDurationSeconds;
            RoundInProgress = true;

            int spawnCount = Mathf.Min(taggerCount, taggerSpawnPoints.Length);
            for (int i = 0; i < spawnCount; i++)
            {
                TaggerAI tagger = Instantiate(taggerPrefab, taggerSpawnPoints[i].position, taggerSpawnPoints[i].rotation);
                tagger.Initialize(this, player.transform);
                _activeTaggers.Add(tagger);
            }
        }

        /// <summary>Toggles pause. Freezes gameplay via Time.timeScale so AI, movement and
        /// the round timer all stop together without each needing its own pause check.</summary>
        public void TogglePause()
        {
            if (!RoundInProgress) return;

            IsPaused = !IsPaused;
            Time.timeScale = IsPaused ? 0f : 1f;
            OnPauseChanged?.Invoke(IsPaused);
        }

        private void Update()
        {
            if (RoundInProgress && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                TogglePause();
            }

            if (!RoundInProgress || IsPaused)
            {
                return;
            }

            _elapsed += Time.deltaTime;
            TimeRemaining = Mathf.Max(0f, roundDurationSeconds - _elapsed);
            OnTimeRemainingChanged?.Invoke(TimeRemaining);

            if (TimeRemaining <= 0f)
            {
                EndRound(survived: true);
            }
        }

        /// <summary>Called by a TaggerAI when it catches the player.</summary>
        public void OnTaggerReachedPlayer(Transform playerTransform)
        {
            if (!RoundInProgress) return;
            player.MarkTagged();
            EndRound(survived: false);
        }

        private async void EndRound(bool survived)
        {
            RoundInProgress = false;
            float survivalTime = _elapsed;

            OnRoundEnded?.Invoke(survived);

            // Route the score through Cloud Code rather than writing the
            // leaderboard directly from the client, so a modified client
            // can't submit an inflated survival time.
            await LeaderboardManager.Instance.SubmitSurvivalTimeAsync(survivalTime, survived);
        }
    }
}
