using System.Collections.Generic;
using UnityEngine;

namespace TagGame.Juice
{
    /// <summary>
    /// Sound effects made from maths at startup (no audio files): tag pop, "you're IT" alarm, whistle,
    /// countdown beeps, buzzer, dash whoosh, pickup chime and footsteps.
    /// </summary>
    public static class GameAudio
    {
        private const int Rate = 22050;
        private static AudioSource _src;
        private static Dictionary<string, AudioClip> _clips;
        public static float Volume = 0.55f;

        public static void Play(string name, float volume = 1f, float pitch = 1f)
        {
            Ensure();
            if (!_clips.TryGetValue(name, out var clip) || clip == null) return;
            _src.pitch = pitch;
            _src.PlayOneShot(clip, volume * Volume);
        }

        private static void Ensure()
        {
            if (_src != null) return;
            var go = new GameObject("GameAudio");
            Object.DontDestroyOnLoad(go);
            _src = go.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;
            _clips = new Dictionary<string, AudioClip>
            {
                { "tag", Make("tag", 0.18f, (t, d) => Sine(Mathf.Lerp(420f, 980f, t / d), t) * Env(t, d, 0.005f) + Noise() * 0.25f * Mathf.Exp(-t * 60f)) },
                { "it", Make("it", 0.5f, (t, d) => Square(((int)(t * 8f) % 2 == 0) ? 880f : 660f, t) * 0.5f * Env(t, d, 0.01f)) },
                { "whistle", Make("whistle", 0.55f, (t, d) => Sine(2500f + Mathf.Sin(t * 40f) * 90f, t) * Env(t, d, 0.02f)) },
                { "beep", Make("beep", 0.09f, (t, d) => Sine(1000f, t) * Env(t, d, 0.005f)) },
                { "go", Make("go", 0.32f, (t, d) => Sine(1500f, t) * Env(t, d, 0.005f)) },
                { "buzzer", Make("buzzer", 0.7f, (t, d) => Saw(170f, t) * 0.45f * Env(t, d, 0.01f)) },
                { "dash", Make("dash", 0.22f, (t, d) => Noise() * Mathf.Sin(t / d * Mathf.PI) * 0.6f) },
                { "pickup", Make("pickup", 0.3f, (t, d) => Sine(t < 0.1f ? 660f : (t < 0.2f ? 880f : 1320f), t) * Env(t, d, 0.005f)) },
                { "shot", Make("shot", 0.16f, (t, d) => (Noise() * 0.9f + Sine(140f, t) * 0.5f) * Mathf.Exp(-t * 30f)) },
                { "smg", Make("smg", 0.09f, (t, d) => (Noise() * 0.8f + Sine(220f, t) * 0.4f) * Mathf.Exp(-t * 45f)) },
                { "rifle", Make("rifle", 0.2f, (t, d) => (Noise() * 0.9f + Sine(110f, t) * 0.6f) * Mathf.Exp(-t * 22f)) },
                { "shotgun", Make("shotgun", 0.35f, (t, d) => (Noise() + Sine(80f, t) * 0.7f) * Mathf.Exp(-t * 12f)) },
                { "sniper", Make("sniper", 0.6f, (t, d) => (Noise() * Mathf.Exp(-t * 18f) + Sine(Mathf.Lerp(320f, 60f, t / d), t) * 0.6f * Mathf.Exp(-t * 6f))) },
                { "click", Make("click", 0.03f, (t, d) => Square(1800f, t) * Mathf.Exp(-t * 200f) * 0.5f) },
                { "reload", Make("reload", 0.28f, (t, d) => Noise() * ((t < 0.04f || (t > 0.18f && t < 0.22f)) ? 0.7f : 0f)) },
                { "hit", Make("hit", 0.08f, (t, d) => Sine(1400f, t) * Env(t, d, 0.002f) * 0.6f) },
                { "hurt", Make("hurt", 0.18f, (t, d) => (Saw(Mathf.Lerp(260f, 120f, t / d), t) * 0.5f + Noise() * 0.3f) * Env(t, d, 0.005f)) },
                { "knock", Make("knock", 0.45f, (t, d) => Square(Mathf.Lerp(400f, 90f, t / d), t) * 0.4f * Env(t, d, 0.01f)) },
                { "heal", Make("heal", 0.45f, (t, d) => Sine(Mathf.Lerp(500f, 1200f, t / d), t) * Env(t, d, 0.02f) * 0.7f) },
                { "laces", Make("laces", 0.35f, (t, d) => Sine(Mathf.Lerp(900f, 300f, t / d), t) * Env(t, d, 0.01f) * 0.6f) },
                { "tie", Make("tie", 0.25f, (t, d) => Sine(t < 0.12f ? 700f : 1050f, t) * Env(t, d, 0.005f) * 0.6f) },
                { "step", Make("step", 0.04f, (t, d) => Noise() * Mathf.Exp(-t * 120f) * 0.5f) },
            };
        }

        private static float Phase(float f, float t) => (f * t) % 1f;
        private static float Sine(float f, float t) => Mathf.Sin(2f * Mathf.PI * f * t);
        private static float Square(float f, float t) => Phase(f, t) < 0.5f ? 1f : -1f;
        private static float Saw(float f, float t) => Phase(f, t) * 2f - 1f;
        private static float Noise() => Random.value * 2f - 1f;
        private static float Env(float t, float d, float attack) => Mathf.Clamp01(t / attack) * Mathf.Clamp01((d - t) / (d * 0.6f));

        private static AudioClip Make(string name, float seconds, System.Func<float, float, float> f)
        {
            int n = Mathf.Max(1, (int)(seconds * Rate));
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate, seconds), -1f, 1f) * 0.8f;
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
