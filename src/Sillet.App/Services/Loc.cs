using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace Sillet.App.Services
{
    public sealed class LanguageOption
    {
        public LanguageOption(string code, string name)
        {
            Code = code;
            Name = name;
        }

        public override string ToString() { return Name; }

        public string Code { get; }
        public string Name { get; }
    }

    public sealed class Loc : INotifyPropertyChanged
    {
        public static readonly Loc Instance = new Loc();

        public static readonly LanguageOption[] Languages =
        {
            new LanguageOption("en", "English"),
            new LanguageOption("es", "Español"),
            new LanguageOption("ru", "Русский"),
            new LanguageOption("zh", "中文"),
            new LanguageOption("fr", "Français")
        };

        private static readonly string[] Codes = { "es", "ru", "zh", "fr" };
        private static List<string> _prefixes;
        public static readonly Dictionary<string, string> Overrides = new Dictionary<string, string>();
        private string _current = "en";
        private int _index = -1;

        public event PropertyChangedEventHandler PropertyChanged;
        public static event Action Changed;

        public static bool Enabled { get; set; }

        public string Current
        {
            get { return _current; }
            set
            {
                var code = Normalize(value);
                if (code == _current) return;
                _current = code;
                _index = Array.IndexOf(Codes, code);
                var handler = PropertyChanged;
                if (handler != null)
                {
                    handler(this, new PropertyChangedEventArgs(nameof(Current)));
                    handler(this, new PropertyChangedEventArgs(nameof(Culture)));
                }
                var changed = Changed;
                if (changed != null) changed();
            }
        }

        public CultureInfo Culture
        {
            get
            {
                switch (_current)
                {
                    case "es": return new CultureInfo("es-CL");
                    case "ru": return new CultureInfo("ru-RU");
                    case "zh": return new CultureInfo("zh-CN");
                    case "fr": return new CultureInfo("fr-FR");
                    default: return new CultureInfo("en-US");
                }
            }
        }

        public static string Normalize(string code)
        {
            if (string.IsNullOrEmpty(code)) return "en";
            code = code.Trim().ToLowerInvariant();
            return code == "es" || code == "ru" || code == "zh" || code == "fr" ? code : "en";
        }

        public static string T(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var trimmed = text.Trim();
            if (trimmed.Length == 0) return text;
            var start = text.IndexOf(trimmed, StringComparison.Ordinal);
            var lead = text.Substring(0, start);
            var trail = text.Substring(start + trimmed.Length);
            string replaced;
            if (Overrides.Count > 0 && Overrides.TryGetValue(trimmed, out replaced))
            {
                trimmed = replaced;
                text = lead + replaced + trail;
            }
            var index = Instance._index;
            if (index < 0) return text;
            string[] row;
            if (LocTable.Rows.TryGetValue(trimmed, out row)) return index < row.Length ? lead + row[index] + trail : text;
            foreach (var key in Prefixes())
            {
                if (trimmed.Length <= key.Length || !trimmed.StartsWith(key, StringComparison.Ordinal)) continue;
                var last = key[key.Length - 1];
                var next = trimmed[key.Length];
                if (next != ' ' && next != ':' && next != '(' && last != ':' && last != '(' && last != '~') continue;
                var prefixRow = LocTable.Rows[key];
                if (index >= prefixRow.Length) return text;
                return lead + prefixRow[index] + trimmed.Substring(key.Length) + trail;
            }
            return text;
        }

        /// <summary>Runs <paramref name="build"/> as if the app were set to another language, without touching the UI.</summary>
        public static TResult With<TResult>(string code, Func<TResult> build)
        {
            var language = Normalize(code);
            var oldCurrent = Instance._current;
            var oldIndex = Instance._index;
            try
            {
                Instance._current = language;
                Instance._index = Array.IndexOf(Codes, language);
                return build();
            }
            finally
            {
                Instance._current = oldCurrent;
                Instance._index = oldIndex;
            }
        }

        public static string F(string format, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, T(format), args);
        }

        private static List<string> Prefixes()
        {
            if (_prefixes == null)
                _prefixes = LocTable.Rows.Keys.Where(k => k.Length >= 6 && k.IndexOf('{') < 0).OrderByDescending(k => k.Length).ToList();
            return _prefixes;
        }
    }
}
