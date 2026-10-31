using UnityEngine;

namespace TagGame.Spectate
{
    /// <summary>"Allow spectators" setting shared by Practice and Multiplayer. On by default; saved.</summary>
    public static class SpectateSettings
    {
        private const string Pref = "TagGame.AllowSpectate";

        public static bool Allow
        {
            get => PlayerPrefs.GetInt(Pref, 1) == 1;
            set { PlayerPrefs.SetInt(Pref, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static string Label => "Allow spectators: " + (Allow ? "ON" : "OFF");

        /// <summary>Name shown to watchers (letters, digits, spaces only).</summary>
        public static string DisplayName()
        {
            string n = PlayerPrefs.GetString("TagGame.MpName", "");
            var sb = new System.Text.StringBuilder();
            foreach (char c in n) if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_') sb.Append(c);
            string s = sb.ToString().Trim();
            return s.Length > 0 ? (s.Length > 12 ? s.Substring(0, 12) : s) : "Player";
        }
    }
}
