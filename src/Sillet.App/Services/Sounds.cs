using System;
using System.Collections.Generic;

namespace Sillet.App.Services
{
    /// <summary>Short sound effects. Each platform sets Player; where it stays null nothing plays.</summary>
    public static class Sounds
    {
        public const string Favorite = "favorite";
        public const string Unlocked = "unlocked";
        public const string Error = "error";
        public const string Updated = "updated";
        public const string Payment = "payment";

        /// <summary>Set by the app. Plays the sound with that name.</summary>
        public static Action<string> Player { get; set; }

        /// <summary>Set by the app. True when the person has sounds turned on.</summary>
        public static Func<bool> Enabled { get; set; }

        private static readonly Dictionary<string, DateTime> Last = new Dictionary<string, DateTime>();

        public static void Play(string name)
        {
            var player = Player;
            if (player == null) return;
            var enabled = Enabled;
            if (enabled != null && !enabled()) return;
            lock (Last)
            {
                DateTime when;
                var now = DateTime.UtcNow;
                if (Last.TryGetValue(name, out when) && now - when < TimeSpan.FromMilliseconds(1200)) return;
                Last[name] = now;
            }
            try
            {
                player(name);
            }
            catch (Exception)
            {
                // A missing sound must never break the app
            }
        }
    }
}
