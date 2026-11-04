using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;

namespace Sillet.Net
{
    public enum JsonKind
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object,
    }

    public sealed class JsonValue
    {
        public static readonly JsonValue Null = new JsonValue(JsonKind.Null, null, false);

        private readonly string _text;
        private readonly bool _flag;
        private readonly List<JsonValue> _items;
        private readonly List<KeyValuePair<string, JsonValue>> _properties;

        private JsonValue(JsonKind kind, string text, bool flag)
        {
            Kind = kind;
            _text = text;
            _flag = flag;
            if (kind == JsonKind.Array) _items = new List<JsonValue>();
            if (kind == JsonKind.Object) _properties = new List<KeyValuePair<string, JsonValue>>();
        }

        public JsonKind Kind { get; }
        public bool IsNull { get { return Kind == JsonKind.Null; } }
        public bool IsObject { get { return Kind == JsonKind.Object; } }
        public bool IsArray { get { return Kind == JsonKind.Array; } }
        public bool IsNumber { get { return Kind == JsonKind.Number; } }
        public bool IsString { get { return Kind == JsonKind.String; } }
        public bool IsTrue { get { return Kind == JsonKind.Bool && _flag; } }

        public static JsonValue FromString(string value)
        {
            return value == null ? Null : new JsonValue(JsonKind.String, value, false);
        }

        public static JsonValue FromBool(bool value)
        {
            return new JsonValue(JsonKind.Bool, null, value);
        }

        public static JsonValue FromNumber(string raw)
        {
            return new JsonValue(JsonKind.Number, raw, false);
        }

        public static JsonValue NewArray(IEnumerable<JsonValue> items)
        {
            var array = new JsonValue(JsonKind.Array, null, false);
            array._items.AddRange(items);
            return array;
        }

        public static JsonValue NewObject()
        {
            return new JsonValue(JsonKind.Object, null, false);
        }

        public int Count
        {
            get
            {
                if (Kind == JsonKind.Array) return _items.Count;
                if (Kind == JsonKind.Object) return _properties.Count;
                return 0;
            }
        }

        public IEnumerable<JsonValue> Items
        {
            get
            {
                if (Kind != JsonKind.Array) throw new InvalidOperationException("Expected a list in the response");
                return _items;
            }
        }

        public JsonValue this[int index]
        {
            get
            {
                if (Kind != JsonKind.Array) throw new InvalidOperationException("Expected a list in the response");
                return _items[index];
            }
        }

        public JsonValue this[string name]
        {
            get
            {
                JsonValue value;
                if (TryGet(name, out value)) return value;
                throw new KeyNotFoundException("Missing \"" + name + "\" in the response");
            }
        }

        public bool TryGet(string name, out JsonValue value)
        {
            value = null;
            if (Kind != JsonKind.Object) return false;
            foreach (var property in _properties)
            {
                if (property.Key == name)
                {
                    value = property.Value;
                    return true;
                }
            }
            return false;
        }

        public bool Has(string name)
        {
            JsonValue ignored;
            return TryGet(name, out ignored);
        }

        public void Set(string name, JsonValue value)
        {
            if (Kind != JsonKind.Object) throw new InvalidOperationException("Fields can only be added to an object");
            for (int i = 0; i < _properties.Count; i++)
            {
                if (_properties[i].Key == name)
                {
                    _properties[i] = new KeyValuePair<string, JsonValue>(name, value);
                    return;
                }
            }
            _properties.Add(new KeyValuePair<string, JsonValue>(name, value));
        }

        public string AsString()
        {
            if (Kind == JsonKind.String) return _text;
            if (Kind == JsonKind.Null) return null;
            throw new InvalidOperationException("Expected text in the response");
        }

        public bool AsBool()
        {
            if (Kind == JsonKind.Bool) return _flag;
            throw new InvalidOperationException("Expected true or false in the response");
        }

        public decimal AsDecimal()
        {
            return decimal.Parse(NumberText(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        public bool TryDecimal(out decimal value)
        {
            value = 0;
            return Kind == JsonKind.Number && decimal.TryParse(_text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public double AsDouble()
        {
            return double.Parse(NumberText(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        public long AsLong()
        {
            var text = NumberText();
            if (text.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0) return (long)decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            return long.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }

        public ulong AsULong()
        {
            var text = NumberText();
            if (text.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0) return (ulong)decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            return ulong.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
        }

        public int AsInt()
        {
            return checked((int)AsLong());
        }

        public uint AsUInt()
        {
            return checked((uint)AsLong());
        }

        private string NumberText()
        {
            if (Kind != JsonKind.Number) throw new InvalidOperationException("Expected a number in the response");
            return _text;
        }

        public string ToJson()
        {
            var sb = new StringBuilder();
            Write(sb);
            return sb.ToString();
        }

        public override string ToString()
        {
            return ToJson();
        }

        internal void Write(StringBuilder sb)
        {
            switch (Kind)
            {
                case JsonKind.Null:
                    sb.Append("null");
                    break;
                case JsonKind.Bool:
                    sb.Append(_flag ? "true" : "false");
                    break;
                case JsonKind.Number:
                    sb.Append(_text);
                    break;
                case JsonKind.String:
                    Json.WriteString(sb, _text);
                    break;
                case JsonKind.Array:
                    sb.Append('[');
                    for (int i = 0; i < _items.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        _items[i].Write(sb);
                    }
                    sb.Append(']');
                    break;
                case JsonKind.Object:
                    sb.Append('{');
                    for (int i = 0; i < _properties.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        Json.WriteString(sb, _properties[i].Key);
                        sb.Append(':');
                        _properties[i].Value.Write(sb);
                    }
                    sb.Append('}');
                    break;
            }
        }

        public static JsonValue Parse(string json)
        {
            var parser = new Parser(json);
            var value = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd) throw new FormatException("Invalid JSON: trailing text");
            return value;
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _pos;

            public Parser(string s)
            {
                _s = s;
            }

            public bool AtEnd { get { return _pos >= _s.Length; } }

            public void SkipWhitespace()
            {
                while (_pos < _s.Length && (_s[_pos] == ' ' || _s[_pos] == '\t' || _s[_pos] == '\n' || _s[_pos] == '\r')) _pos++;
            }

            public JsonValue ParseValue()
            {
                SkipWhitespace();
                if (AtEnd) throw new FormatException("Invalid JSON: unexpected end");
                char c = _s[_pos];
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return FromString(ParseString());
                if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                if (Match("true")) return FromBool(true);
                if (Match("false")) return FromBool(false);
                if (Match("null")) return Null;
                throw new FormatException("Invalid JSON at position " + _pos);
            }

            private bool Match(string literal)
            {
                if (string.CompareOrdinal(_s, _pos, literal, 0, literal.Length) != 0) return false;
                _pos += literal.Length;
                return true;
            }

            private void Expect(char c)
            {
                SkipWhitespace();
                if (AtEnd || _s[_pos] != c) throw new FormatException("Invalid JSON: expected '" + c + "'");
                _pos++;
            }

            private JsonValue ParseObject()
            {
                var obj = NewObject();
                _pos++;
                SkipWhitespace();
                if (!AtEnd && _s[_pos] == '}')
                {
                    _pos++;
                    return obj;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _s[_pos] != '"') throw new FormatException("Invalid JSON: expected a property name");
                    var name = ParseString();
                    Expect(':');
                    obj._properties.Add(new KeyValuePair<string, JsonValue>(name, ParseValue()));
                    SkipWhitespace();
                    if (AtEnd) throw new FormatException("Invalid JSON: unclosed object");
                    if (_s[_pos] == ',') { _pos++; continue; }
                    if (_s[_pos] == '}') { _pos++; return obj; }
                    throw new FormatException("Invalid JSON: expected ',' or '}'");
                }
            }

            private JsonValue ParseArray()
            {
                var array = new JsonValue(JsonKind.Array, null, false);
                _pos++;
                SkipWhitespace();
                if (!AtEnd && _s[_pos] == ']')
                {
                    _pos++;
                    return array;
                }
                while (true)
                {
                    array._items.Add(ParseValue());
                    SkipWhitespace();
                    if (AtEnd) throw new FormatException("Invalid JSON: unclosed array");
                    if (_s[_pos] == ',') { _pos++; continue; }
                    if (_s[_pos] == ']') { _pos++; return array; }
                    throw new FormatException("Invalid JSON: expected ',' or ']'");
                }
            }

            private string ParseString()
            {
                _pos++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw new FormatException("Invalid JSON: unclosed string");
                    char c = _s[_pos++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (AtEnd) throw new FormatException("Invalid JSON: incomplete escape");
                    char e = _s[_pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_pos + 4 > _s.Length) throw new FormatException("Invalid JSON: incomplete escape");
                            sb.Append((char)int.Parse(_s.Substring(_pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _pos += 4;
                            break;
                        default:
                            throw new FormatException("Invalid JSON: unknown escape");
                    }
                }
            }

            private JsonValue ParseNumber()
            {
                int start = _pos;
                if (_s[_pos] == '-') _pos++;
                while (!AtEnd && char.IsDigit(_s[_pos])) _pos++;
                if (!AtEnd && _s[_pos] == '.')
                {
                    _pos++;
                    while (!AtEnd && char.IsDigit(_s[_pos])) _pos++;
                }
                if (!AtEnd && (_s[_pos] == 'e' || _s[_pos] == 'E'))
                {
                    _pos++;
                    if (!AtEnd && (_s[_pos] == '+' || _s[_pos] == '-')) _pos++;
                    while (!AtEnd && char.IsDigit(_s[_pos])) _pos++;
                }
                return FromNumber(_s.Substring(start, _pos - start));
            }
        }
    }

    public static class Json
    {
        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }
            var json = value as JsonValue;
            if (json != null)
            {
                json.Write(sb);
                return;
            }
            var text = value as string;
            if (text != null)
            {
                WriteString(sb, text);
                return;
            }
            if (value is bool)
            {
                sb.Append((bool)value ? "true" : "false");
                return;
            }
            if (value is int || value is long || value is uint || value is ulong || value is short || value is ushort || value is byte || value is sbyte)
            {
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            }
            if (value is decimal)
            {
                sb.Append(((decimal)value).ToString(CultureInfo.InvariantCulture));
                return;
            }
            if (value is double)
            {
                sb.Append(((double)value).ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (value is float)
            {
                sb.Append(((float)value).ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (value is BigInteger)
            {
                sb.Append(((BigInteger)value).ToString(CultureInfo.InvariantCulture));
                return;
            }
            var dictionary = value as IDictionary;
            if (dictionary != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, Convert.ToString(entry.Key, CultureInfo.InvariantCulture));
                    sb.Append(':');
                    Write(sb, entry.Value);
                }
                sb.Append('}');
                return;
            }
            var list = value as IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Write(sb, item);
                }
                sb.Append(']');
                return;
            }

            sb.Append('{');
            bool firstProperty = true;
            foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0))
            {
                if (!firstProperty) sb.Append(',');
                firstProperty = false;
                WriteString(sb, property.Name);
                sb.Append(':');
                Write(sb, property.GetValue(value, null));
            }
            sb.Append('}');
        }

        internal static void WriteString(StringBuilder sb, string text)
        {
            sb.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
