using System;
using System.IO;
using Sillet.Net;
using Sillet.Storage;

namespace Sillet.App.Services
{
    internal static class JsonFileStore
    {
        public static JsonValue Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return JsonValue.Parse(LocalData.ReadText(path));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException || e is InvalidOperationException)
            {
                return null;
            }
        }

        public static void Write(string path, object value)
        {
            try
            {
                LocalData.WriteText(path, Json.Serialize(value));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
        }

        public static string Text(JsonValue item, string name)
        {
            JsonValue v;
            return item.TryGet(name, out v) && v.IsString ? v.AsString() : null;
        }

        public static decimal? Number(JsonValue item, string name)
        {
            JsonValue v;
            decimal d;
            if (!item.TryGet(name, out v)) return null;
            if (v.IsNumber && v.TryDecimal(out d)) return d;
            if (v.IsString && decimal.TryParse(v.AsString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) return d;
            return null;
        }
    }
}
