using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.RemoteConfig;

namespace TagGame.Services
{
    [Serializable]
    public class TagGameConfig
    {
        public int taggerCount = 2;
        public float roundDurationSeconds = 60f;
        public float taggerSpeedMultiplier = 1f;
        public float tagRadius = 1.1f;
    }

    /// <summary>
    /// Fetches tunable difficulty/balance values from Remote Config so the
    /// game can be re-balanced from the Unity Dashboard without a client
    /// rebuild. See RemoteConfig/TagGameConfig.rc for the deployed definitions.
    /// </summary>
    public class RemoteConfigManager : MonoBehaviour
    {
        public static RemoteConfigManager Instance { get; private set; }

        private struct UserAttributes { }
        private struct AppAttributes { }

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

        public async Task<TagGameConfig> FetchTagGameConfigAsync()
        {
            try
            {
                if (UGSBootstrap.Instance != null && !UGSBootstrap.Instance.IsReady)
                {
                    await UGSBootstrap.Instance.InitializeAsync();
                }

                await RemoteConfigService.Instance.FetchConfigsAsync(new UserAttributes(), new AppAttributes());

                var config = new TagGameConfig
                {
                    taggerCount = RemoteConfigService.Instance.appConfig.GetInt("tagger_count", 2),
                    roundDurationSeconds = RemoteConfigService.Instance.appConfig.GetFloat("round_duration_seconds", 60f),
                    taggerSpeedMultiplier = RemoteConfigService.Instance.appConfig.GetFloat("tagger_speed_multiplier", 1f),
                    tagRadius = RemoteConfigService.Instance.appConfig.GetFloat("tag_radius", 1.1f),
                };

                return config;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RemoteConfig] Fetch failed, using defaults: {e.Message}");
                return null;
            }
        }
    }
}
