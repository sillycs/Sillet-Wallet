using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

namespace Sillet.App.Services
{
    public static class Flags
    {
        private static readonly Dictionary<string, string> Country = new Dictionary<string, string>
        {
            { "usd", "US" }, { "eur", "ES" }, { "gbp", "GB" }, { "clp", "CL" }, { "ars", "AR" }, { "brl", "BR" }, { "mxn", "MX" }, { "cop", "CO" }, { "pen", "PE" },
            { "uyu", "UY" }, { "pyg", "PY" }, { "bob", "BO" }, { "ves", "VE" }, { "crc", "CR" }, { "gtq", "GT" }, { "hnl", "HN" }, { "nio", "NI" }, { "dop", "DO" },
            { "cup", "CU" }, { "jmd", "JM" }, { "ttd", "TT" }, { "bbd", "BB" }, { "bsd", "BS" }, { "cad", "CA" }, { "aud", "AU" }, { "nzd", "NZ" }, { "fjd", "FJ" },
            { "chf", "CH" }, { "sek", "SE" }, { "nok", "NO" }, { "dkk", "DK" }, { "isk", "IS" }, { "pln", "PL" }, { "czk", "CZ" }, { "huf", "HU" }, { "ron", "RO" },
            { "bgn", "BG" }, { "rsd", "RS" }, { "mkd", "MK" }, { "all", "AL" }, { "bam", "BA" }, { "mdl", "MD" }, { "gel", "GE" }, { "amd", "AM" }, { "azn", "AZ" },
            { "byn", "BY" }, { "rub", "RU" }, { "uah", "UA" }, { "kzt", "KZ" }, { "uzs", "UZ" }, { "kgs", "KG" }, { "try", "TR" }, { "ils", "IL" }, { "aed", "AE" },
            { "sar", "SA" }, { "qar", "QA" }, { "kwd", "KW" }, { "bhd", "BH" }, { "omr", "OM" }, { "jod", "JO" }, { "lbp", "LB" }, { "iqd", "IQ" }, { "irr", "IR" },
            { "egp", "EG" }, { "mad", "MA" }, { "dzd", "DZ" }, { "tnd", "TN" }, { "lyd", "LY" }, { "ngn", "NG" }, { "ghs", "GH" }, { "kes", "KE" }, { "tzs", "TZ" },
            { "ugx", "UG" }, { "etb", "ET" }, { "zar", "ZA" }, { "zmw", "ZM" }, { "bwp", "BW" }, { "mur", "MU" }, { "xof", "SN" }, { "xaf", "CM" }, { "inr", "IN" },
            { "pkr", "PK" }, { "bdt", "BD" }, { "lkr", "LK" }, { "npr", "NP" }, { "mmk", "MM" }, { "cny", "CN" }, { "hkd", "HK" }, { "mop", "MO" }, { "twd", "TW" },
            { "jpy", "JP" }, { "krw", "KR" }, { "mnt", "MN" }, { "sgd", "SG" }, { "myr", "MY" }, { "thb", "TH" }, { "idr", "ID" }, { "php", "PH" }, { "vnd", "VN" },
            { "khr", "KH" }, { "lak", "LA" }, { "bnd", "BN" }, { "kpw", "KP" }, { "afn", "AF" }, { "png", "PG" }, { "xpf", "PF" },
        };

        private static string Folder
        {
            get { return Path.Combine(AppSettings.DataFolder, "flags"); }
        }

        public static string Iso2(string currency)
        {
            string iso;
            return currency != null && Country.TryGetValue(currency.ToLowerInvariant(), out iso) ? iso : null;
        }

        public static string Emoji(string currency)
        {
            var iso = Iso2(currency);
            if (iso == null || iso.Length != 2) return "";
            var text = new StringBuilder();
            foreach (var letter in iso) text.Append(char.ConvertFromUtf32(0x1F1E6 + (char.ToUpperInvariant(letter) - 'A')));
            return text.ToString();
        }

        public static string PathFor(string currency)
        {
            var iso = Iso2(currency);
            if (iso == null) return null;
            var path = Path.Combine(Folder, iso.ToLowerInvariant() + ".png");
            if (File.Exists(path)) return path;
            return Extract(iso, path) ? path : null;
        }

        public static Task<bool> EnsureAsync(bool force = false)
        {
            return Task.FromResult(false);
        }

        private static bool Extract(string iso, string path)
        {
            try
            {
                using (var stream = typeof(Flags).Assembly.GetManifestResourceStream("Sillet.flag." + iso.ToLowerInvariant()))
                {
                    if (stream == null) return false;
                    Directory.CreateDirectory(Folder);
                    var temp = path + ".tmp";
                    using (var target = File.Create(temp)) stream.CopyTo(target);
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(temp, path);
                    return true;
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
