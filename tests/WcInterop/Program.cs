// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
// A wallet driven by tests/wc-interop.cjs: it pairs with the official JavaScript WalletConnect app and answers it
using System;
using System.IO;
using System.Threading.Tasks;
using Sillet.Net;
using Sillet.WalletConnect;

namespace Sillet.WcInterop
{
    internal sealed class Prompts : IWcPrompts
    {
        public Task<bool> ConfirmConnectAsync(WcConnectPrompt p)
        {
            Console.WriteLine("PROMPT connect " + p.Peer.Name + " | " + string.Join(",", p.Networks) + " | " + p.Account);
            return Task.FromResult(true);
        }

        public Task<bool> ConfirmMessageAsync(WcMessagePrompt p)
        {
            Console.WriteLine("PROMPT message " + (p.IsTypedData ? "typed" : "text") + " warnings=" + p.Warnings.Count);
            return Task.FromResult(!(p.IsTypedData && p.Text.StartsWith("Reject")));
        }

        public Task<bool> ConfirmTransactionAsync(WcTransactionPrompt p)
        {
            Console.WriteLine("PROMPT tx " + p.Amount + " to " + p.To + " warnings=" + p.Warnings.Count + " " + p.Network);
            return Task.FromResult(true);
        }
    }

    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            var relay = args[0];
            var uri = args[1];
            var rpc = args[2];
            var dir = Path.Combine(Path.GetTempPath(), "wc-interop-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            Endpoints.Load("ethereum=" + rpc);
            var wallet = new WalletManager(Path.Combine(dir, "vault.json"));
            wallet.Create("test test test test test test test test test test test junk", "password-123");
            wallet.Unlock("password-123");
            var service = new WalletConnectService("0123456789abcdef0123456789abcdef", Path.Combine(dir, "wc.json"), relay);
            var bridge = new WcWalletBridge(wallet, new Prompts(), service);
            service.Problem += t => Console.WriteLine("PROBLEM " + t);
            service.SessionsChanged += () => Console.WriteLine("SESSIONS " + service.Sessions.Count);
            await service.StartAsync();
            await service.PairAsync(uri);
            Console.WriteLine("READY");
            var until = DateTime.UtcNow.AddSeconds(120);
            while (DateTime.UtcNow < until) await Task.Delay(200);
            return 0;
        }
    }
}
