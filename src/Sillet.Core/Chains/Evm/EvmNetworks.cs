// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System.Collections.Generic;
using System.Numerics;
using Sillet.Core;

namespace Sillet.Chains.Evm
{
    public sealed class EthereumChain : EvmChain
    {
        public EthereumChain()
        {
            BuiltIn = new[]
            {
                Native,
                Token("USDT", "Tether USD", 6, "0xdAC17F958D2ee523a2206206994597C13D831ec7"),
                Token("USDC", "USD Coin", 6, "0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48"),
                Token("WLD", "Worldcoin", 18, "0x163f8C2467924be0ae7B5347228CABF260318753"),
                Token("FDUSD", "First Digital USD", 18, "0xc5f0f7b66764F6ec8C8Dff7BA683102295E16409"),
                Token("LINK", "Chainlink", 18, "0x514910771AF9Ca656af840dff83E8264EcF986CA"),
            };
        }

        public override string Id { get { return "ethereum"; } }
        public override string Name { get { return "Ethereum"; } }
        public override long ChainId { get { return 1; } }
        public override Asset Native { get; } = new Asset("ETH", "Ether", 18);
        protected override IReadOnlyList<Asset> BuiltIn { get; }
        protected override string ExplorerBase { get { return "https://etherscan.io"; } }
        protected override string[] RpcUrls { get { return new[] { "https://ethereum-rpc.publicnode.com", "https://eth.drpc.org", "https://eth.llamarpc.com" }; } }
    }

    public sealed class ArbitrumChain : EvmChain
    {
        public ArbitrumChain()
        {
            BuiltIn = new[]
            {
                Native,
                Token("USDT", "Tether USD", 6, "0xFd086bC7CD5C481DCC9C85ebE478A1C0b69FCbb9"),
                Token("USDC", "USD Coin", 6, "0xaf88d065e77c8cC2239327C5EDb3A432268e5831"),
                Token("LINK", "Chainlink", 18, "0xf97f4df75117a78c1A5a0DBb814Af92458539FB4"),
            };
        }

        public override string Id { get { return "arbitrum"; } }
        public override string Name { get { return "Arbitrum One"; } }
        public override long ChainId { get { return 42161; } }
        public override Asset Native { get; } = new Asset("ETH", "Ether (Arbitrum)", 18);
        protected override IReadOnlyList<Asset> BuiltIn { get; }
        protected override string ExplorerBase { get { return "https://arbiscan.io"; } }
        protected override string[] RpcUrls { get { return new[] { "https://arbitrum-one-rpc.publicnode.com", "https://arb1.arbitrum.io/rpc", "https://arbitrum.drpc.org" }; } }
    }

    public sealed class OptimismChain : EvmChain
    {
        public OptimismChain()
        {
            BuiltIn = new[]
            {
                Native,
                Token("USDT", "Tether USD", 6, "0x94b008aA00579c1307B0EF2c499aD98a8ce58e58"),
                Token("USDC", "USD Coin", 6, "0x0b2C639c533813f4Aa9D7837CAf62653d097Ff85"),
                Token("WLD", "Worldcoin", 18, "0xdC6fF44d5d932Cbd77B52E5612Ba0529DC6226F1"),
                Token("LINK", "Chainlink", 18, "0x350a791Bfc2C21F9Ed5d10980Dad2e2638ffa7f6"),
            };
        }

        public override string Id { get { return "optimism"; } }
        public override string Name { get { return "Optimism"; } }
        public override long ChainId { get { return 10; } }
        public override Asset Native { get; } = new Asset("ETH", "Ether (Optimism)", 18);
        protected override IReadOnlyList<Asset> BuiltIn { get; }
        protected override bool IsOpStack { get { return true; } }
        protected override string ExplorerBase { get { return "https://optimistic.etherscan.io"; } }
        protected override string[] RpcUrls { get { return new[] { "https://optimism-rpc.publicnode.com", "https://mainnet.optimism.io", "https://optimism.drpc.org" }; } }
    }

    public sealed class BaseChain : EvmChain
    {
        public BaseChain()
        {
            BuiltIn = new[]
            {
                Native,
                Token("USDC", "USD Coin", 6, "0x833589fCD6eDb6E08f4c7C32D4f71b54bdA02913"),
                Token("LINK", "Chainlink", 18, "0x88Fb150BDc53A65fe94Dea0c9BA0a6dAf8C6e196"),
            };
        }

        public override string Id { get { return "base"; } }
        public override string Name { get { return "Base"; } }
        public override long ChainId { get { return 8453; } }
        public override Asset Native { get; } = new Asset("ETH", "Ether (Base)", 18);
        protected override IReadOnlyList<Asset> BuiltIn { get; }
        protected override bool IsOpStack { get { return true; } }
        protected override string ExplorerBase { get { return "https://basescan.org"; } }
        protected override string[] RpcUrls { get { return new[] { "https://base-rpc.publicnode.com", "https://mainnet.base.org", "https://base.drpc.org" }; } }
    }

    public sealed class PolygonChain : EvmChain
    {
        public PolygonChain()
        {
            BuiltIn = new[]
            {
                Native,
                Token("USDT", "Tether USD", 6, "0xc2132D05D31c914a87C6611C10748AEb04B58e8F"),
                Token("USDC", "USD Coin", 6, "0x3c499c542cEF5E3811e1192ce70d8cC03d5c3359"),
                Token("LINK", "Chainlink", 18, "0xb0897686c545045aFc77CF20eC7A532E3120E0F1"),
            };
        }

        public override string Id { get { return "polygon"; } }
        public override string Name { get { return "Polygon"; } }
        public override long ChainId { get { return 137; } }
        public override Asset Native { get; } = new Asset("POL", "Polygon", 18);
        protected override IReadOnlyList<Asset> BuiltIn { get; }
        protected override BigInteger MinPriorityFee { get { return new BigInteger(30000000000L); } }
        protected override string ExplorerBase { get { return "https://polygonscan.com"; } }
        protected override string[] RpcUrls { get { return new[] { "https://polygon-bor-rpc.publicnode.com", "https://polygon-rpc.com", "https://polygon.drpc.org" }; } }
    }

    public sealed class BnbChain : EvmChain
    {
        public BnbChain()
        {
            BuiltIn = new[]
            {
                Native,
                Token("USDT", "Tether USD (BEP-20)", 18, "0x55d398326f99059fF775485246999027B3197955"),
                Token("FDUSD", "First Digital USD", 18, "0xc5f0f7b66764F6ec8C8Dff7BA683102295E16409"),
                Token("LINK", "Chainlink", 18, "0xF8A0BF9cF54Bb92F17374d9e9A321E6a111a51bD"),
            };
        }

        public override string Id { get { return "bsc"; } }
        public override string Name { get { return "BNB Smart Chain"; } }
        public override long ChainId { get { return 56; } }
        public override Asset Native { get; } = new Asset("BNB", "BNB", 18);
        protected override IReadOnlyList<Asset> BuiltIn { get; }
        protected override bool SupportsEip1559 { get { return false; } }
        protected override string ExplorerBase { get { return "https://bscscan.com"; } }
        protected override string[] RpcUrls { get { return new[] { "https://bsc-rpc.publicnode.com", "https://bsc-dataseed.bnbchain.org", "https://bsc.drpc.org" }; } }
    }

    public sealed class AvalancheChain : EvmChain
    {
        public AvalancheChain()
        {
            BuiltIn = new[]
            {
                Native,
                Token("USDT", "Tether USD", 6, "0x9702230A8Ea53601f5cD2dc00fDBc13d4dF4A8c7"),
                Token("USDC", "USD Coin", 6, "0xB97EF9Ef8734C71904D8002F8b6Bc66Dd9c48a6E"),
            };
        }

        public override string Id { get { return "avalanche"; } }
        public override string Name { get { return "Avalanche C-Chain"; } }
        public override long ChainId { get { return 43114; } }
        public override Asset Native { get; } = new Asset("AVAX", "Avalanche", 18);
        protected override IReadOnlyList<Asset> BuiltIn { get; }
        protected override string ExplorerBase { get { return "https://snowtrace.io"; } }
        protected override string[] RpcUrls { get { return new[] { "https://avalanche-c-chain-rpc.publicnode.com", "https://api.avax.network/ext/bc/C/rpc", "https://avalanche.drpc.org" }; } }
    }

    public sealed class KaiaChain : EvmChain
    {
        public override string Id { get { return "kaia"; } }
        public override string Name { get { return "Kaia"; } }
        public override long ChainId { get { return 8217; } }
        public override Asset Native { get; } = new Asset("KAIA", "Kaia", 18);
        protected override bool SupportsEip1559 { get { return false; } }
        protected override string ExplorerBase { get { return "https://kaiascan.io"; } }
        protected override string[] RpcUrls { get { return new[] { "https://public-en.node.kaia.io", "https://kaia.blockpi.network/v1/rpc/public" }; } }
    }

    public sealed class LineaChain : EvmChain
    {
        public override string Id { get { return "linea"; } }
        public override string Name { get { return "Linea"; } }
        public override long ChainId { get { return 59144; } }
        public override Asset Native { get; } = new Asset("ETH", "Ether", 18);
        protected override string ExplorerBase { get { return "https://lineascan.build"; } }
        protected override string[] RpcUrls { get { return new[] { "https://rpc.linea.build", "https://linea-rpc.publicnode.com" }; } }
    }

    public sealed class BlastChain : EvmChain
    {
        public override string Id { get { return "blast"; } }
        public override string Name { get { return "Blast"; } }
        public override long ChainId { get { return 81457; } }
        public override Asset Native { get; } = new Asset("ETH", "Ether", 18);
        protected override bool IsOpStack { get { return true; } }
        protected override string ExplorerBase { get { return "https://blastscan.io"; } }
        protected override string[] RpcUrls { get { return new[] { "https://rpc.blast.io", "https://blast-rpc.publicnode.com" }; } }
    }

    public sealed class GnosisChain : EvmChain
    {
        public override string Id { get { return "gnosis"; } }
        public override string Name { get { return "Gnosis"; } }
        public override long ChainId { get { return 100; } }
        public override Asset Native { get; } = new Asset("xDAI", "xDAI", 18);
        protected override string ExplorerBase { get { return "https://gnosisscan.io"; } }
        protected override string[] RpcUrls { get { return new[] { "https://rpc.gnosischain.com", "https://gnosis-rpc.publicnode.com" }; } }
    }
}
