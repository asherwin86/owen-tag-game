using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.CloudCode;
using Unity.Services.Leaderboards;
using Unity.Services.Leaderboards.Models;

namespace TagGame.Services
{
    /// <summary>
    /// Reads leaderboard rankings directly (safe, read-only) but routes score
    /// SUBMISSION through the SubmitTagScore Cloud Code module, which is the
    /// only thing allowed to write to the leaderboard (see AccessControl/tag_game.ac).
    /// This stops a modified client from just POSTing a fake high score.
    /// </summary>
    public class LeaderboardManager : MonoBehaviour
    {
        public static LeaderboardManager Instance { get; private set; }

        [SerializeField] private string leaderboardId = "tag_survival_time";

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

        public async Task SubmitSurvivalTimeAsync(float survivalTimeSeconds, bool survivedFullRound)
        {
            try
            {
                if (UGSBootstrap.Instance != null && !UGSBootstrap.Instance.IsReady)
                {
                    await UGSBootstrap.Instance.InitializeAsync();
                }

                // Cloud Code binds each dictionary key to a like-named parameter on the
                // module function directly (flat), not to a single nested object.
                var args = new Dictionary<string, object>
                {
                    ["survivalTimeSeconds"] = survivalTimeSeconds,
                    ["survivedFullRound"] = survivedFullRound
                };

                await CloudCodeService.Instance.CallModuleEndpointAsync<object>(
                    "TagGameModule", "SubmitTagScore", args);

                Debug.Log($"[Leaderboard] Submitted survival time {survivalTimeSeconds:F1}s via Cloud Code.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Leaderboard] Score submission failed: {e}");
            }
        }

        public async Task<LeaderboardScoresPage> GetTopScoresAsync(int count = 10)
        {
            try
            {
                var options = new GetScoresOptions { Limit = count };
                return await LeaderboardsService.Instance.GetScoresAsync(leaderboardId, options);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Leaderboard] Fetch failed: {e}");
                return null;
            }
        }
    }
}
