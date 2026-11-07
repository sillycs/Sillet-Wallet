// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Sillet.Crypto
{
    public static class Bip39
    {
        private static readonly Lazy<string[]> WordList = new Lazy<string[]>(LoadWords);
        private static readonly Lazy<Dictionary<string, int>> WordIndex = new Lazy<Dictionary<string, int>>(BuildIndex);

        public static IReadOnlyList<string> Words
        {
            get { return WordList.Value; }
        }

        public static string Generate(int wordCount = 12)
        {
            int entropyBytes;
            switch (wordCount)
            {
                case 12: entropyBytes = 16; break;
                case 15: entropyBytes = 20; break;
                case 18: entropyBytes = 24; break;
                case 21: entropyBytes = 28; break;
                case 24: entropyBytes = 32; break;
                default: throw new ArgumentException("The phrase must have 12, 15, 18, 21 or 24 words");
            }
            var entropy = new byte[entropyBytes];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(entropy);
            try { return FromEntropy(entropy); }
            finally { Array.Clear(entropy, 0, entropy.Length); }
        }

        public static string FromEntropy(byte[] entropy)
        {
            if (entropy.Length < 16 || entropy.Length > 32 || entropy.Length % 4 != 0) throw new ArgumentException("Invalid entropy");
            int checksumBits = entropy.Length * 8 / 32;
            var hash = Hashes.Sha256(entropy);
            var bits = new bool[entropy.Length * 8 + checksumBits];
            for (int i = 0; i < entropy.Length * 8; i++) bits[i] = (entropy[i / 8] & (0x80 >> (i % 8))) != 0;
            for (int i = 0; i < checksumBits; i++) bits[entropy.Length * 8 + i] = (hash[i / 8] & (0x80 >> (i % 8))) != 0;

            var words = new string[bits.Length / 11];
            for (int w = 0; w < words.Length; w++)
            {
                int index = 0;
                for (int b = 0; b < 11; b++) index = (index << 1) | (bits[w * 11 + b] ? 1 : 0);
                words[w] = WordList.Value[index];
            }
            return string.Join(" ", words);
        }

        public static string Normalize(string phrase)
        {
            return string.Join(" ", phrase.Trim().ToLowerInvariant().Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        }

        public static string Validate(string phrase)
        {
            var words = Normalize(phrase).Split(' ');
            if (words.Length != 12 && words.Length != 15 && words.Length != 18 && words.Length != 21 && words.Length != 24)
                return "The phrase must have 12, 15, 18, 21 or 24 words";
            var bits = new bool[words.Length * 11];
            for (int w = 0; w < words.Length; w++)
            {
                int index;
                if (!WordIndex.Value.TryGetValue(words[w], out index)) return "The word \"" + words[w] + "\" is not in the BIP39 list";
                for (int b = 0; b < 11; b++) bits[w * 11 + b] = (index & (1 << (10 - b))) != 0;
            }
            int checksumBits = bits.Length / 33;
            var entropy = new byte[(bits.Length - checksumBits) / 8];
            for (int i = 0; i < entropy.Length * 8; i++)
                if (bits[i]) entropy[i / 8] |= (byte)(0x80 >> (i % 8));
            var hash = Hashes.Sha256(entropy);
            for (int i = 0; i < checksumBits; i++)
            {
                bool expected = (hash[i / 8] & (0x80 >> (i % 8))) != 0;
                if (bits[entropy.Length * 8 + i] != expected) return "The phrase has an error (invalid checksum). Check the words";
            }
            return null;
        }

        public static bool IsValid(string phrase)
        {
            return Validate(phrase) == null;
        }

        public static byte[] ToSeed(string phrase, string passphrase = "")
        {
            var password = System.Text.Encoding.UTF8.GetBytes(Normalize(phrase).Normalize(NormalizationForm.FormKD));
            var salt = System.Text.Encoding.UTF8.GetBytes(("mnemonic" + passphrase).Normalize(NormalizationForm.FormKD));
            try
            {
                using (var kdf = new Rfc2898DeriveBytes(password, salt, 2048, HashAlgorithmName.SHA512))
                    return kdf.GetBytes(64);
            }
            finally
            {
                Array.Clear(password, 0, password.Length);
            }
        }

        private static string[] LoadWords()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Sillet.bip39-english.txt"))
            {
                if (stream == null) throw new InvalidOperationException("The BIP39 word list is missing");
                using (var reader = new StreamReader(stream))
                {
                    var words = reader.ReadToEnd().Split('\n').Select(w => w.Trim()).Where(w => w.Length > 0).ToArray();
                    if (words.Length != 2048) throw new InvalidOperationException("The BIP39 list must have 2048 words");
                    return words;
                }
            }
        }

        private static Dictionary<string, int> BuildIndex()
        {
            var index = new Dictionary<string, int>();
            for (int i = 0; i < WordList.Value.Length; i++) index[WordList.Value[i]] = i;
            return index;
        }
    }
}
