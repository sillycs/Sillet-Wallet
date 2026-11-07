// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.IO;
using System.Security.Cryptography;
using Sillet.App.Services;
using Sillet.Crypto;
using Sillet.Encoding;

namespace ReleaseSigner
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "keygen") return KeyGen(args[1]);
            if (args.Length >= 4 && args[0] == "sign") return Sign(args[1], args[2], args[3], args.Length > 4 ? args[4] : null);
            if (args.Length >= 2 && args[0] == "hash") return Hash(args);
            Console.WriteLine("Sillet release signer");
            Console.WriteLine("  keygen <private-key-file>                         create a new release key (keep the file offline)");
            Console.WriteLine("  sign <private-key-file> <exe> <version> [notes]    print the version.json fields for an update");
            Console.WriteLine("  hash <file> [file...]                              print SHA-256 checksums");
            return 1;
        }

        private static int KeyGen(string path)
        {
            if (File.Exists(path))
            {
                Console.Error.WriteLine("Refusing to overwrite " + path);
                return 1;
            }
            var seed = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(seed);
            File.WriteAllText(path, Hex.Encode(seed));
            Console.WriteLine("Private key written to " + path + ". Store it offline and never commit it");
            Console.WriteLine("Public key (paste into UpdateKey.PublicKeyHex):");
            Console.WriteLine(Hex.Encode(Ed25519Keys.PublicKey(seed)));
            return 0;
        }

        private static int Sign(string keyFile, string exe, string version, string notesFile)
        {
            var seed = Hex.Decode(File.ReadAllText(keyFile).Trim());
            string sha;
            using (var hasher = SHA256.Create())
            using (var stream = File.OpenRead(exe)) sha = Hex.Encode(hasher.ComputeHash(stream));
            var notes = notesFile != null && File.Exists(notesFile) ? File.ReadAllText(notesFile).Trim() : (notesFile ?? "");
            notes = notes.Replace("\r", "");
            if (notes.Length > 1500)
            {
                Console.Error.WriteLine("Notes are longer than 1500 characters, the app would cut them and the signature would fail");
                return 1;
            }
            var signature = Convert.ToBase64String(Ed25519Keys.Sign(seed, UpdateKey.Message(version, sha, notes)));
            var publicKey = Hex.Encode(Ed25519Keys.PublicKey(seed));
            Console.WriteLine("{");
            Console.WriteLine("  \"version\": \"" + version + "\",");
            Console.WriteLine("  \"sha256\": \"" + sha + "\",");
            Console.WriteLine("  \"notes\": " + Quote(notes) + ",");
            Console.WriteLine("  \"sig\": \"" + signature + "\"");
            Console.WriteLine("}");
            Console.Error.WriteLine("Signed with public key " + publicKey);
            Console.Error.WriteLine(UpdateKey.Verify(publicKey, version, sha, notes, signature) ? "Self-check passed" : "Self-check FAILED");
            return 0;
        }

        private static int Hash(string[] args)
        {
            for (int i = 1; i < args.Length; i++)
            {
                using (var hasher = SHA256.Create())
                using (var stream = File.OpenRead(args[i])) Console.WriteLine(Hex.Encode(hasher.ComputeHash(stream)) + "  " + Path.GetFileName(args[i]));
            }
            return 0;
        }

        private static string Quote(string text)
        {
            return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n") + "\"";
        }
    }
}
