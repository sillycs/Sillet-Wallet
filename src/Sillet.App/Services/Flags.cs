using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sillet.Net;

namespace Sillet.App.Services
{
    /// <summary>
    /// Flags for the currency list. The pictures come from the disease.sh countries API (countryInfo.flag) and are kept on disk;
    /// while a picture is not there (offline, or a currency without a country such as the euro) the flag emoji is shown instead.
    /// </summary>
    public static class Flags
    {
        public const string CountriesUrl = "https://disease.sh/v3/covid-19/countries";

        private static readonly HttpClient Http = new HttpClient(Sillet.Net.Http.NewHandler()) { Timeout = TimeSpan.FromSeconds(20) };
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(4);
        private static readonly SemaphoreSlim Once = new SemaphoreSlim(1);
        private static Dictionary<string, string> _urls;

        private static readonly Dictionary<string, string> Country = new Dictionary<string, string>
        {
            { "usd", "US" }, { "eur", "EU" }, { "gbp", "GB" }, { "clp", "CL" }, { "ars", "AR" }, { "brl", "BR" }, { "mxn", "MX" }, { "cop", "CO" }, { "pen", "PE" },
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

        /// <summary>The flag as an emoji (two regional indicator letters), or an empty string when the currency has no country.</summary>
        public static string Emoji(string currency)
        {
            var iso = Iso2(currency);
            if (iso == null || iso.Length != 2) return "";
            var text = new StringBuilder();
            foreach (var letter in iso) text.Append(char.ConvertFromUtf32(0x1F1E6 + (char.ToUpperInvariant(letter) - 'A')));
            return text.ToString();
        }

        /// <summary>The saved picture for a currency's country, or null while it has not been downloaded.</summary>
        public static string PathFor(string currency)
        {
            var iso = Iso2(currency);
            if (iso == null) return null;
            var path = Path.Combine(Folder, iso.ToLowerInvariant() + ".png");
            return File.Exists(path) ? path : null;
        }

        /// <summary>Downloads the country list once, then any flag picture still missing. Safe to call often; returns true when something new was saved.</summary>
        public static async Task<bool> EnsureAsync()
        {
            if (!await Once.WaitAsync(0)) return false;
            try
            {
                var missing = Country.Values.Distinct().Where(iso => !File.Exists(Path.Combine(Folder, iso.ToLowerInvariant() + ".png"))).ToList();
                if (missing.Count == 0) return false;
                if (_urls == null) _urls = await LoadUrlsAsync();
                var tasks = missing.Where(iso => _urls.ContainsKey(iso)).Select(iso => DownloadAsync(iso, _urls[iso])).ToList();
                var results = await Task.WhenAll(tasks);
                return results.Any(r => r);
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is IOException || e is UnauthorizedAccessException || e is FormatException || e is InvalidOperationException)
            {
                return false;
            }
            finally
            {
                Once.Release();
            }
        }

        private static async Task<Dictionary<string, string>> LoadUrlsAsync()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var json = JsonValue.Parse(await Http.GetStringAsync(CountriesUrl));
            if (!json.IsArray) return map;
            foreach (var country in json.Items)
            {
                JsonValue info;
                if (!country.TryGet("countryInfo", out info) || !info.IsObject) continue;
                JsonValue iso, flag;
                if (!info.TryGet("iso2", out iso) || iso.IsNull || !info.TryGet("flag", out flag) || flag.IsNull) continue;
                var url = flag.AsString();
                if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) map[iso.AsString()] = url;
            }
            return map;
        }

        private static async Task<bool> DownloadAsync(string iso, string url)
        {
            await Gate.WaitAsync();
            try
            {
                var bytes = await Http.GetByteArrayAsync(url);
                if (!IconCache.IsSupportedImage(bytes)) return false;
                Directory.CreateDirectory(Folder);
                var path = Path.Combine(Folder, iso.ToLowerInvariant() + ".png");
                var temp = path + ".tmp";
                File.WriteAllBytes(temp, bytes);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
                return true;
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
            finally
            {
                Gate.Release();
            }
        }
    }
}
