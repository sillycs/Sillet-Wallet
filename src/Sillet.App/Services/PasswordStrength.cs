// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sillet.App.Services
{
    public static class PasswordStrength
    {
        private static readonly HashSet<string> Common = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "password", "password1", "password123", "12345678", "123456789", "1234567890", "qwertyuiop", "qwerty123", "11111111",
            "iloveyou", "sunshine", "princess", "football", "baseball", "welcome1", "admin123", "abc12345", "contraseña", "contrasena",
            "bitcoin1", "bitcoin123", "ethereum", "sillet123", "letmein1", "trustno1", "00000000", "87654321", "asdfghjk", "zxcvbnm1",
        };

        public static int Score(string password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < 8) return 0;
            if (Common.Contains(password) || password.Distinct().Count() <= 3) return 0;
            int classes = 0;
            if (password.Any(char.IsLower)) classes++;
            if (password.Any(char.IsUpper)) classes++;
            if (password.Any(char.IsDigit)) classes++;
            if (password.Any(c => !char.IsLetterOrDigit(c))) classes++;
            int score = 1;
            if (password.Length >= 10) score++;
            if (password.Length >= 14) score++;
            if (classes >= 2) score++;
            if (classes >= 3 && password.Length >= 12) score++;
            if (classes == 1 && password.Length < 14) score = Math.Min(score, 1);
            return Math.Min(4, score);
        }

        public static string Label(int score)
        {
            switch (score)
            {
                case 0: return Loc.T("Too weak");
                case 1: return Loc.T("Weak");
                case 2: return Loc.T("Fair");
                case 3: return Loc.T("Good");
                default: return Loc.T("Strong");
            }
        }

        public static bool IsAcceptable(string password)
        {
            return Score(password) >= 2;
        }

        public static string Advice => Loc.T("Choose a stronger password: at least 8 characters mixing letters, numbers or symbols, and not a common one");
    }
}
