using System;
using System.Collections.Generic;

namespace Sillet.App.Services
{
    public static class Sounds
    {
        public const string Favorite = "favorite";
        public const string Unlocked = "unlocked";
        public const string Error = "error";
        public const string Updated = "updated";
        public const string Payment = "payment";

        public static Action<string> Player { get; set; }

        public static Func<bool> Enabled { get; set; }

        public static Action<string> Preloader { get; set; }

        public static readonly string[] All = { Favorite, Unlocked, Error, Updated, Payment };

        public static void Preload()
        {
            var preloader = Preloader;
            if (preloader == null) return;
            foreach (var name in All)
            {
                try
                {
                    preloader(name);
                }
                catch (Exception)
                {
                }
            }
        }

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
            }
        }
    }
}
