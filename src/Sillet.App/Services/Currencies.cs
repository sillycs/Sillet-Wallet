using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Sillet.App.Services
{
    public sealed class CurrencyInfo
    {
        public CurrencyInfo(string code, string symbol, string name, decimal perUsd, bool noCents, string countries = "", bool direct = false)
        {
            Code = code;
            Symbol = symbol;
            Name = name;
            PerUsd = perUsd;
            NoCents = noCents;
            Countries = countries;
            Direct = direct;
        }

        public string Code { get; }
        public string Symbol { get; }
        public string Name { get; }
        public decimal PerUsd { get; internal set; }
        public bool NoCents { get; }
        public string Countries { get; }
        /// <summary>True when CoinGecko prices coins in this currency itself; otherwise we convert from US dollars.</summary>
        public bool Direct { get; }
        public string Label => Code.ToUpperInvariant();
        public string Display => Label + "  ·  " + Name;
        public string IconText
        {
            get
            {
                var t = Symbol.Trim();
                return t.Length <= 3 ? t : Label;
            }
        }
        public string Description
        {
            get
            {
                var first = (Countries ?? "").Split(',')[0].Trim();
                return first.Length == 0 ? Label : Loc.F("Used in {0}", first);
            }
        }
        public string Title => Label + "  ·  " + Name;

        public bool Matches(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            var q = query.Trim().ToLowerInvariant();
            return Code.Contains(q) || Name.ToLowerInvariant().Contains(q) || (Countries ?? "").ToLowerInvariant().Contains(q) || Symbol.Trim().ToLowerInvariant() == q;
        }
    }

    public static class Currencies
    {
        public static readonly IReadOnlyList<CurrencyInfo> All = new[]
        {
            new CurrencyInfo("usd", "US$", "US dollar", 1m, false, "United States, Ecuador, El Salvador, Puerto Rico, Panama", true),
            new CurrencyInfo("eur", "€", "Euro", 0.92m, false, "Eurozone, Germany, France, Spain, Italy, Portugal, Ireland, Netherlands, Belgium, Austria, Finland, Greece", true),
            new CurrencyInfo("gbp", "£", "British pound", 0.78m, false, "United Kingdom, England, Scotland, Wales", true),
            new CurrencyInfo("clp", "$", "Chilean peso", 950m, true, "Chile", true),
            new CurrencyInfo("ars", "AR$", "Argentine peso", 1000m, true, "Argentina", true),
            new CurrencyInfo("brl", "R$", "Brazilian real", 5.5m, false, "Brazil", true),
            new CurrencyInfo("mxn", "MX$", "Mexican peso", 18.5m, false, "Mexico", true),
            new CurrencyInfo("cop", "COL$", "Colombian peso", 4100m, true, "Colombia", false),
            new CurrencyInfo("pen", "S/ ", "Peruvian sol", 3.75m, false, "Peru", false),
            new CurrencyInfo("uyu", "$U ", "Uruguayan peso", 40m, false, "Uruguay", false),
            new CurrencyInfo("pyg", "₲", "Paraguayan guarani", 7500m, true, "Paraguay", false),
            new CurrencyInfo("bob", "Bs ", "Bolivian boliviano", 6.9m, false, "Bolivia", false),
            new CurrencyInfo("ves", "Bs.S ", "Venezuelan bolivar", 40m, false, "Venezuela", false),
            new CurrencyInfo("crc", "₡", "Costa Rican colon", 510m, true, "Costa Rica", false),
            new CurrencyInfo("gtq", "Q ", "Guatemalan quetzal", 7.7m, false, "Guatemala", false),
            new CurrencyInfo("hnl", "L ", "Honduran lempira", 25m, false, "Honduras", false),
            new CurrencyInfo("nio", "C$", "Nicaraguan cordoba", 36.8m, false, "Nicaragua", false),
            new CurrencyInfo("dop", "RD$", "Dominican peso", 60m, false, "Dominican Republic", false),
            new CurrencyInfo("cup", "CUP ", "Cuban peso", 24m, false, "Cuba", false),
            new CurrencyInfo("jmd", "J$", "Jamaican dollar", 157m, false, "Jamaica", false),
            new CurrencyInfo("ttd", "TT$", "Trinidad and Tobago dollar", 6.8m, false, "Trinidad and Tobago", false),
            new CurrencyInfo("bbd", "Bds$", "Barbadian dollar", 2m, false, "Barbados", false),
            new CurrencyInfo("bsd", "B$", "Bahamian dollar", 1m, false, "Bahamas", false),
            new CurrencyInfo("cad", "CA$", "Canadian dollar", 1.37m, false, "Canada", true),
            new CurrencyInfo("aud", "A$", "Australian dollar", 1.52m, false, "Australia", true),
            new CurrencyInfo("nzd", "NZ$", "New Zealand dollar", 1.65m, false, "New Zealand", true),
            new CurrencyInfo("fjd", "FJ$", "Fijian dollar", 2.25m, false, "Fiji", false),
            new CurrencyInfo("chf", "CHF ", "Swiss franc", 0.88m, false, "Switzerland, Liechtenstein", true),
            new CurrencyInfo("sek", "SEK ", "Swedish krona", 10.5m, false, "Sweden", true),
            new CurrencyInfo("nok", "NOK ", "Norwegian krone", 10.6m, false, "Norway", true),
            new CurrencyInfo("dkk", "DKK ", "Danish krone", 6.9m, false, "Denmark", true),
            new CurrencyInfo("isk", "ISK ", "Icelandic krona", 138m, true, "Iceland", false),
            new CurrencyInfo("pln", "zł ", "Polish zloty", 3.9m, false, "Poland", true),
            new CurrencyInfo("czk", "Kč ", "Czech koruna", 23m, false, "Czechia, Czech Republic", true),
            new CurrencyInfo("huf", "Ft ", "Hungarian forint", 360m, true, "Hungary", true),
            new CurrencyInfo("ron", "lei ", "Romanian leu", 4.6m, false, "Romania", false),
            new CurrencyInfo("bgn", "лв ", "Bulgarian lev", 1.8m, false, "Bulgaria", false),
            new CurrencyInfo("rsd", "din ", "Serbian dinar", 108m, true, "Serbia", false),
            new CurrencyInfo("mkd", "ден ", "Macedonian denar", 57m, false, "North Macedonia", false),
            new CurrencyInfo("all", "L ", "Albanian lek", 92m, true, "Albania", false),
            new CurrencyInfo("bam", "KM ", "Bosnia and Herzegovina mark", 1.8m, false, "Bosnia and Herzegovina", false),
            new CurrencyInfo("mdl", "L ", "Moldovan leu", 17.8m, false, "Moldova", false),
            new CurrencyInfo("gel", "₾", "Georgian lari", 2.7m, false, "Georgia", true),
            new CurrencyInfo("amd", "֏", "Armenian dram", 390m, true, "Armenia", false),
            new CurrencyInfo("azn", "₼", "Azerbaijani manat", 1.7m, false, "Azerbaijan", false),
            new CurrencyInfo("byn", "Br ", "Belarusian ruble", 3.3m, false, "Belarus", false),
            new CurrencyInfo("rub", "₽", "Russian ruble", 90m, false, "Russia", true),
            new CurrencyInfo("uah", "₴", "Ukrainian hryvnia", 41m, false, "Ukraine", true),
            new CurrencyInfo("kzt", "₸", "Kazakhstani tenge", 480m, true, "Kazakhstan", false),
            new CurrencyInfo("uzs", "soʻm ", "Uzbekistani som", 12700m, true, "Uzbekistan", false),
            new CurrencyInfo("kgs", "som ", "Kyrgyzstani som", 86m, false, "Kyrgyzstan", false),
            new CurrencyInfo("try", "₺", "Turkish lira", 34m, false, "Turkey, Türkiye", true),
            new CurrencyInfo("ils", "₪", "Israeli shekel", 3.7m, false, "Israel", true),
            new CurrencyInfo("aed", "AED ", "UAE dirham", 3.67m, false, "United Arab Emirates, UAE, Dubai", true),
            new CurrencyInfo("sar", "SAR ", "Saudi riyal", 3.75m, false, "Saudi Arabia", true),
            new CurrencyInfo("qar", "QAR ", "Qatari riyal", 3.64m, false, "Qatar", false),
            new CurrencyInfo("kwd", "KWD ", "Kuwaiti dinar", 0.31m, false, "Kuwait", true),
            new CurrencyInfo("bhd", "BHD ", "Bahraini dinar", 0.38m, false, "Bahrain", true),
            new CurrencyInfo("omr", "OMR ", "Omani rial", 0.385m, false, "Oman", false),
            new CurrencyInfo("jod", "JOD ", "Jordanian dinar", 0.71m, false, "Jordan", false),
            new CurrencyInfo("lbp", "L£", "Lebanese pound", 89500m, true, "Lebanon", false),
            new CurrencyInfo("iqd", "IQD ", "Iraqi dinar", 1310m, true, "Iraq", false),
            new CurrencyInfo("irr", "IRR ", "Iranian rial", 42000m, true, "Iran", false),
            new CurrencyInfo("egp", "E£", "Egyptian pound", 49m, false, "Egypt", false),
            new CurrencyInfo("mad", "MAD ", "Moroccan dirham", 9.9m, false, "Morocco", false),
            new CurrencyInfo("dzd", "DZD ", "Algerian dinar", 134m, false, "Algeria", false),
            new CurrencyInfo("tnd", "TND ", "Tunisian dinar", 3.1m, false, "Tunisia", false),
            new CurrencyInfo("lyd", "LYD ", "Libyan dinar", 4.8m, false, "Libya", false),
            new CurrencyInfo("ngn", "₦", "Nigerian naira", 1500m, true, "Nigeria", true),
            new CurrencyInfo("ghs", "GH₵", "Ghanaian cedi", 15m, false, "Ghana", false),
            new CurrencyInfo("kes", "KSh ", "Kenyan shilling", 129m, false, "Kenya", false),
            new CurrencyInfo("tzs", "TSh ", "Tanzanian shilling", 2600m, true, "Tanzania", false),
            new CurrencyInfo("ugx", "USh ", "Ugandan shilling", 3700m, true, "Uganda", false),
            new CurrencyInfo("etb", "Br ", "Ethiopian birr", 120m, false, "Ethiopia", false),
            new CurrencyInfo("zar", "R ", "South African rand", 18m, false, "South Africa", true),
            new CurrencyInfo("zmw", "ZK ", "Zambian kwacha", 27m, false, "Zambia", false),
            new CurrencyInfo("bwp", "P ", "Botswana pula", 13.6m, false, "Botswana", false),
            new CurrencyInfo("mur", "₨", "Mauritian rupee", 46m, false, "Mauritius", false),
            new CurrencyInfo("xof", "CFA ", "West African CFA franc", 600m, true, "Senegal, Ivory Coast, Mali, Benin, Burkina Faso", false),
            new CurrencyInfo("xaf", "FCFA ", "Central African CFA franc", 600m, true, "Cameroon, Gabon, Chad, Congo", false),
            new CurrencyInfo("inr", "₹", "Indian rupee", 84m, false, "India", true),
            new CurrencyInfo("pkr", "Rs ", "Pakistani rupee", 280m, true, "Pakistan", true),
            new CurrencyInfo("bdt", "৳", "Bangladeshi taka", 120m, false, "Bangladesh", true),
            new CurrencyInfo("lkr", "Rs ", "Sri Lankan rupee", 300m, true, "Sri Lanka", true),
            new CurrencyInfo("npr", "Rs ", "Nepalese rupee", 134m, false, "Nepal", false),
            new CurrencyInfo("mmk", "K ", "Myanmar kyat", 2100m, true, "Myanmar, Burma", true),
            new CurrencyInfo("cny", "CN¥", "Chinese yuan", 7.2m, false, "China", true),
            new CurrencyInfo("hkd", "HK$", "Hong Kong dollar", 7.8m, false, "Hong Kong", true),
            new CurrencyInfo("mop", "MOP$", "Macanese pataca", 8m, false, "Macau", false),
            new CurrencyInfo("twd", "NT$", "Taiwan dollar", 32m, true, "Taiwan", true),
            new CurrencyInfo("jpy", "¥", "Japanese yen", 150m, true, "Japan", true),
            new CurrencyInfo("krw", "₩", "South Korean won", 1380m, true, "South Korea, Korea", true),
            new CurrencyInfo("mnt", "₮", "Mongolian tugrik", 3400m, true, "Mongolia", false),
            new CurrencyInfo("sgd", "S$", "Singapore dollar", 1.34m, false, "Singapore", true),
            new CurrencyInfo("myr", "RM ", "Malaysian ringgit", 4.4m, false, "Malaysia", true),
            new CurrencyInfo("thb", "฿", "Thai baht", 35m, false, "Thailand", true),
            new CurrencyInfo("idr", "Rp ", "Indonesian rupiah", 16000m, true, "Indonesia", true),
            new CurrencyInfo("php", "₱", "Philippine peso", 58m, false, "Philippines", true),
            new CurrencyInfo("vnd", "₫", "Vietnamese dong", 25000m, true, "Vietnam", true),
            new CurrencyInfo("khr", "៛", "Cambodian riel", 4100m, true, "Cambodia", false),
            new CurrencyInfo("lak", "₭", "Lao kip", 21500m, true, "Laos", false),
            new CurrencyInfo("bnd", "B$", "Brunei dollar", 1.34m, false, "Brunei", false),
            new CurrencyInfo("kpw", "₩", "North Korean won", 900m, true, "North Korea", false),
            new CurrencyInfo("afn", "؋", "Afghan afghani", 70m, false, "Afghanistan", false),
            new CurrencyInfo("png", "K ", "Papua New Guinean kina", 3.9m, false, "Papua New Guinea", false),
            new CurrencyInfo("xpf", "₣", "CFP franc", 110m, true, "French Polynesia, New Caledonia", false)
        };

        /// <summary>Replaces the built-in approximate rates with fresh ones (units per US dollar).</summary>
        public static void SetRates(IDictionary<string, decimal> rates)
        {
            foreach (var c in All)
            {
                decimal value;
                if (rates.TryGetValue(c.Code, out value) && value > 0) c.PerUsd = value;
            }
        }

        public static CurrencyInfo Find(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            code = code.Trim().ToLowerInvariant();
            return All.FirstOrDefault(c => c.Code == code);
        }

        public static CurrencyInfo Get(string code)
        {
            return Find(code) ?? All[0];
        }

        public static bool IsSupported(string code)
        {
            return Find(code) != null;
        }

        private static readonly Dictionary<string, string> ZoneCurrency = new Dictionary<string, string>
        {
            { "America/Santiago", "clp" }, { "America/Punta_Arenas", "clp" }, { "America/Argentina", "ars" }, { "America/Buenos_Aires", "ars" },
            { "America/Sao_Paulo", "brl" }, { "America/Bogota", "cop" }, { "America/Lima", "pen" }, { "America/Montevideo", "uyu" },
            { "America/Asuncion", "pyg" }, { "America/La_Paz", "bob" }, { "America/Caracas", "ves" }, { "America/Mexico_City", "mxn" },
            { "America/Costa_Rica", "crc" }, { "America/Guatemala", "gtq" }, { "America/Tegucigalpa", "hnl" }, { "America/Managua", "nio" },
            { "America/Santo_Domingo", "dop" }, { "America/Toronto", "cad" }, { "America/Vancouver", "cad" }, { "America/Panama", "usd" },
            { "America/Guayaquil", "usd" }, { "America/El_Salvador", "usd" }, { "Europe/London", "gbp" }, { "Europe/Madrid", "eur" },
            { "Europe/Paris", "eur" }, { "Europe/Berlin", "eur" }, { "Europe/Rome", "eur" }, { "Europe/Lisbon", "eur" }, { "Europe/Amsterdam", "eur" },
            { "Europe/Dublin", "eur" }, { "Europe/Vienna", "eur" }, { "Europe/Brussels", "eur" }, { "Europe/Athens", "eur" }, { "Europe/Helsinki", "eur" },
            { "Europe/Zurich", "chf" }, { "Europe/Stockholm", "sek" }, { "Europe/Oslo", "nok" }, { "Europe/Copenhagen", "dkk" }, { "Europe/Warsaw", "pln" },
            { "Europe/Prague", "czk" }, { "Europe/Budapest", "huf" }, { "Europe/Bucharest", "ron" }, { "Europe/Kiev", "uah" }, { "Europe/Kyiv", "uah" },
            { "Europe/Moscow", "rub" }, { "Europe/Istanbul", "try" }, { "Asia/Istanbul", "try" }, { "Asia/Dubai", "aed" }, { "Asia/Riyadh", "sar" },
            { "Asia/Jerusalem", "ils" }, { "Asia/Kolkata", "inr" }, { "Asia/Calcutta", "inr" }, { "Asia/Karachi", "pkr" }, { "Asia/Dhaka", "bdt" },
            { "Asia/Shanghai", "cny" }, { "Asia/Hong_Kong", "hkd" }, { "Asia/Taipei", "twd" }, { "Asia/Tokyo", "jpy" }, { "Asia/Seoul", "krw" },
            { "Asia/Singapore", "sgd" }, { "Asia/Kuala_Lumpur", "myr" }, { "Asia/Bangkok", "thb" }, { "Asia/Jakarta", "idr" }, { "Asia/Manila", "php" },
            { "Asia/Ho_Chi_Minh", "vnd" }, { "Asia/Saigon", "vnd" }, { "Australia/Sydney", "aud" }, { "Australia/Melbourne", "aud" },
            { "Pacific/Auckland", "nzd" }, { "Africa/Lagos", "ngn" }, { "Africa/Johannesburg", "zar" }, { "Africa/Nairobi", "kes" },
            { "Africa/Cairo", "egp" }, { "Africa/Casablanca", "mad" }, { "Africa/Accra", "ghs" }
        };

        public static string Detect()
        {
            try
            {
                var region = RegionInfo.CurrentRegion;
                if (region != null && region.TwoLetterISORegionName.Length == 2 && region.TwoLetterISORegionName != "IV")
                {
                    var found = Find(region.ISOCurrencySymbol);
                    if (found != null) return found.Code;
                }
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is PlatformNotSupportedException)
            {
            }
            try
            {
                var id = TimeZoneInfo.Local.Id ?? "";
                string code;
                if (ZoneCurrency.TryGetValue(id, out code)) return code;
                foreach (var pair in ZoneCurrency)
                    if (id.StartsWith(pair.Key + "/", StringComparison.Ordinal)) return pair.Value;
            }
            catch (Exception e) when (e is InvalidOperationException || e is TimeZoneNotFoundException)
            {
            }
            return "usd";
        }

        /// <summary>Currencies matching the query. Without a query, the user's own and the US dollar come first.</summary>
        public static List<CurrencyInfo> Search(string query, string localCode)
        {
            if (!string.IsNullOrWhiteSpace(query)) return All.Where(c => c.Matches(query)).OrderBy(c => c.Code == query.Trim().ToLowerInvariant() ? 0 : 1).ToList();
            var first = new List<CurrencyInfo>();
            var local = Find(localCode);
            if (local != null) first.Add(local);
            if (local == null || local.Code != "usd") first.Add(All[0]);
            return first.Concat(All.Where(c => !first.Contains(c))).ToList();
        }

        public static decimal Nice(decimal value)
        {
            if (value <= 0) return 0;
            var magnitude = 1m;
            while (value >= magnitude * 10) magnitude *= 10;
            while (value < magnitude && magnitude > 0.0001m) magnitude /= 10;
            var ratio = value / magnitude;
            var steps = new[] { 1m, 2m, 2.5m, 5m, 10m };
            var best = steps.OrderBy(s => Math.Abs(s - ratio)).First();
            return best * magnitude;
        }
    }
}
