using System;
using System.Collections.Generic;
using System.Linq;

namespace Sillet.App.Services
{
    public sealed class ThemeDefinition
    {
        public ThemeDefinition(string key, string name, IDictionary<string, string> colors, string[] palette)
        {
            Key = key;
            Name = name;
            Colors = colors;
            Palette = palette;
        }

        public string Key { get; }
        public bool MobileOnly { get; set; }
        public string Name { get; }
        public IDictionary<string, string> Colors { get; }
        public string[] Palette { get; }
        public string Background => Colors["BgBrush"];
        public string Surface => Colors["SurfaceBrush"];
        public string Accent => Colors["AccentBrush"];
        public string Text => Colors["TextBrush"];
    }

    public static class ThemeCatalog
    {
        public const string DefaultKey = "dark";

        private static readonly string[] Keys =
        {
            "BgBrush", "SurfaceBrush", "SurfaceAltBrush", "SurfacePressedBrush", "BorderSubtleBrush", "BorderStrongBrush",
            "TextBrush", "TextSecondaryBrush", "TextTertiaryBrush",
            "AccentBrush", "AccentHoverBrush", "AccentSoftBrush", "OnAccentBrush",
            "PositiveBrush", "PositiveSoftBrush", "NegativeBrush", "NegativeSoftBrush", "WarningBrush", "WarningSoftBrush",
            "ToastBrush", "OnToastBrush", "HoverOverlayBrush",
        };

        public static readonly IReadOnlyList<ThemeDefinition> All = new[]
        {
            Build("dark", "Dark", new[]
            {
                "#09090B", "#111113", "#18181B", "#222226", "#1C1C20", "#3A3A41",
                "#F4F4F5", "#8C8C96", "#56565F",
                "#F4F4F5", "#FFFFFF", "#1C1C20", "#09090B",
                "#3DD68C", "#0E2419", "#F2555A", "#2A1114", "#E2B464", "#1F190D",
                "#F4F4F5", "#09090B", "#FFFFFF",
            }, new[] { "#F4F4F5", "#B4B4BC", "#85858F", "#5F5F68", "#45454D", "#303036" }),

            Build("light", "Light", new[]
            {
                "#F6F6F7", "#FFFFFF", "#F1F1F3", "#E6E6EA", "#E6E6EA", "#C8C8CF",
                "#111113", "#5E5E68", "#9B9BA4",
                "#111113", "#000000", "#EDEDF0", "#FFFFFF",
                "#12A150", "#E2F5E9", "#D93A3F", "#FCE7E7", "#B7791F", "#FBF0DC",
                "#111113", "#FFFFFF", "#000000",
            }, new[] { "#111113", "#3F3F46", "#6B6B74", "#9B9BA4", "#C3C3CA", "#DEDEE3" }),

            Build("binance", "Binance", new[]
            {
                "#0B0E11", "#161A1E", "#1E2329", "#2B3139", "#232830", "#3A424D",
                "#EAECEF", "#848E9C", "#5E6673",
                "#FCD535", "#FFE066", "#2B2610", "#181A20",
                "#0ECB81", "#0B2A20", "#F6465D", "#2E1218", "#F0B90B", "#2A2208",
                "#EAECEF", "#0B0E11", "#FFFFFF",
            }, new[] { "#FCD535", "#F0B90B", "#B8890A", "#EAECEF", "#848E9C", "#2B3139" }),

            Build("raton", "Raton", new[]
            {
                "#141217", "#1C1A21", "#25222C", "#2F2B38", "#2A2632", "#5A4878",
                "#F2EEF8", "#A69EB4", "#6C6479",
                "#6D45AA", "#7D54BE", "#2A2139", "#FFFFFF",
                "#3DD68C", "#0E2419", "#F2555A", "#2C1115", "#E2B464", "#1F190D",
                "#F2EEF8", "#141217", "#FFFFFF",
            }, new[] { "#9C74E3", "#6D45AA", "#4D3180", "#F2EEF8", "#A69EB4", "#3A3247" }),
            Build("litecoin", "Litecoin", new[]
            {
                "#0A0E15", "#111722", "#18202D", "#212B3B", "#1B2432", "#33445E",
                "#EEF2F8", "#8E9BB0", "#5B677A",
                "#345D9D", "#4A74B8", "#16233A", "#FFFFFF",
                "#3DD68C", "#0E2419", "#F2555A", "#2A1114", "#E2B464", "#1F190D",
                "#EEF2F8", "#0A0E15", "#FFFFFF",
            }, new[] { "#BFBBBB", "#345D9D", "#4A74B8", "#EEF2F8", "#8E9BB0", "#212B3B" }),
            Build("telegram", "Telegram", new[]
            {
                "#0E1621", "#17212B", "#1F2B38", "#2B3A4B", "#202D3B", "#34475B",
                "#F5F7FA", "#7F91A4", "#566778",
                "#2AABEE", "#5BC0F2", "#143045", "#FFFFFF",
                "#5CC76B", "#12291A", "#F2555A", "#2A1418", "#E2B464", "#1F190D",
                "#F5F7FA", "#0E1621", "#FFFFFF",
            }, new[] { "#2AABEE", "#1D8BC4", "#17212B", "#F5F7FA", "#7F91A4", "#34475B" }, true),
        };

        public static bool Mobile { get; set; }

        public static IEnumerable<ThemeDefinition> Available
        {
            get { return All.Where(t => !t.MobileOnly || Mobile); }
        }

        public static ThemeDefinition Find(string key)
        {
            if (string.Equals(key, "midnight", StringComparison.OrdinalIgnoreCase)) key = "binance";
            if (string.Equals(key, "aurora", StringComparison.OrdinalIgnoreCase)) key = "telegram";
            return All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        public static ThemeDefinition Get(string key)
        {
            return Find(key) ?? Find(DefaultKey);
        }

        private static ThemeDefinition Build(string key, string name, string[] values, string[] palette, bool mobileOnly = false)
        {
            if (values.Length != Keys.Length) throw new ArgumentException("Theme " + key + " has the wrong number of colors");
            var colors = new Dictionary<string, string>();
            for (int i = 0; i < Keys.Length; i++) colors[Keys[i]] = values[i];
            return new ThemeDefinition(key, name, colors, palette) { MobileOnly = mobileOnly };
        }
    }
}
