using System;
using System.Globalization;
using System.Numerics;

namespace Sillet.Core
{
    public static class Units
    {
        public static BigInteger ToRaw(decimal amount, int decimals)
        {
            if (amount < 0) throw new WalletException("The amount cannot be negative");
            var text = amount.ToString(CultureInfo.InvariantCulture);
            var parts = text.Split('.');
            var whole = parts[0];
            var fraction = parts.Length > 1 ? parts[1].TrimEnd('0') : "";
            if (fraction.Length > decimals)
                throw new WalletException("Too many decimals: this coin allows at most " + decimals);
            return BigInteger.Parse(whole + fraction.PadRight(decimals, '0'), CultureInfo.InvariantCulture);
        }

        public static decimal FromRaw(BigInteger raw, int decimals)
        {
            bool negative = raw.Sign < 0;
            var digits = BigInteger.Abs(raw).ToString(CultureInfo.InvariantCulture).PadLeft(decimals + 1, '0');
            var whole = digits.Substring(0, digits.Length - decimals);
            var fraction = decimals > 0 ? digits.Substring(digits.Length - decimals).TrimEnd('0') : "";
            int room = Math.Max(0, 27 - whole.TrimStart('0').Length);
            if (fraction.Length > room) fraction = fraction.Substring(0, room);
            var text = (negative ? "-" : "") + whole + (fraction.Length > 0 ? "." + fraction : "");
            return decimal.Parse(text, CultureInfo.InvariantCulture);
        }
    }
}
