using System;

namespace Sillet.App.Services
{
    public static class Performance
    {
        public static Func<bool> LiteSource { get; set; }
        public static Action Restart { get; set; }
        public static bool Lite => LiteSource != null && LiteSource();
        public static event Action Changed;

        public static void Notify()
        {
            var handler = Changed;
            if (handler != null) handler();
        }
    }
}
