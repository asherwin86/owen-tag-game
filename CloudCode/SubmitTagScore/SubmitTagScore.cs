using System;
using System.Threading.Tasks;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Apis;

namespace TagGame.CloudCode
{
    /// <summary>
    /// Server-authoritative score submission. This is the ONLY path allowed to
    /// write to the tag_survival_time leaderboard (enforced by AccessControl/tag_game.ac,
    /// which denies direct player writes to Leaderboards).
    ///
    /// Basic anti-cheat: reject impossible survival times before writing.
    /// Extend this with a per-round-duration ceiling pulled from Remote Config
    /// if you want it to track live balance changes automatically.
    /// </summary>
    public class TagGameModule
    {
        private const float MaxPlausibleSurvivalSeconds = 600f; // hard sanity ceiling

        // Cloud Code binds each key in the client's args dictionary directly to a
        // like-named parameter here (flat binding) — see LeaderboardManager.SubmitSurvivalTimeAsync.
        [CloudCodeFunction("SubmitTagScore")]
        public async Task<string> SubmitTagScore(IExecutionContext context, ILeaderboardsApi leaderboardsApi, float survivalTimeSeconds, bool survivedFullRound)
        {
            if (survivalTimeSeconds < 0f || survivalTimeSeconds > MaxPlausibleSurvivalSeconds)
            {
                throw new Exception("Rejected: survival time outside plausible range.");
            }

            await leaderboardsApi.AddPlayerScoreAsync(
                context,
                context.AccessToken,
                context.ProjectId,
                context.PlayerId,
                "tag_survival_time",
                new Unity.Services.Leaderboards.Http.Generated.Model.LeaderboardScore(survivalTimeSeconds)
            );

            return "ok";
        }
    }
}
