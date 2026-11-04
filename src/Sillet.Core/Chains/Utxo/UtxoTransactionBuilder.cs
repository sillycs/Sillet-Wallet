using System;
using System.Collections.Generic;
using Sillet.Crypto;
using Sillet.Encoding;

namespace Sillet.Chains.Utxo
{
    public sealed class Utxo
    {
        public Utxo(string txId, uint vout, long value, bool confirmed = true)
        {
            TxId = txId;
            Vout = vout;
            Value = value;
            Confirmed = confirmed;
        }

        public string TxId { get; }
        public uint Vout { get; }
        public long Value { get; }
        public bool Confirmed { get; }
        public byte[] Key { get; set; }
    }

    public sealed class TxOutput
    {
        public TxOutput(long value, byte[] script)
        {
            Value = value;
            Script = script;
        }

        public long Value { get; }
        public byte[] Script { get; }
    }

    public sealed class SignedUtxoTransaction
    {
        public SignedUtxoTransaction(byte[] raw, string txId)
        {
            Raw = raw;
            TxId = txId;
        }

        public byte[] Raw { get; }
        public string TxId { get; }
    }

    public static class UtxoTransactionBuilder
    {
        private const uint SighashAll = 1;

        public static byte[] P2pkhScript(byte[] pubKeyHash)
        {
            return Bytes.Concat(new byte[] { 0x76, 0xa9, 0x14 }, pubKeyHash, new byte[] { 0x88, 0xac });
        }

        public static byte[] P2shScript(byte[] scriptHash)
        {
            return Bytes.Concat(new byte[] { 0xa9, 0x14 }, scriptHash, new byte[] { 0x87 });
        }

        public static byte[] WitnessScript(int version, byte[] program)
        {
            return Bytes.Concat(new[] { (byte)(version == 0 ? 0x00 : 0x50 + version), (byte)program.Length }, program);
        }

        public static SignedUtxoTransaction SignP2wpkh(IList<Utxo> inputs, IList<TxOutput> outputs, byte[] privateKey,
            uint version = 2, uint sequence = 0xFFFFFFFD, uint lockTime = 0)
        {
            return SignP2wpkh(inputs, outputs, u => privateKey, version, sequence, lockTime);
        }

        public static SignedUtxoTransaction SignP2wpkh(IList<Utxo> inputs, IList<TxOutput> outputs, Func<Utxo, byte[]> keyFor,
            uint version = 2, uint sequence = 0xFFFFFFFD, uint lockTime = 0)
        {

            var prevouts = new Writer();
            var sequences = new Writer();
            foreach (var input in inputs)
            {
                prevouts.Outpoint(input);
                sequences.U32(sequence);
            }
            var outs = new Writer();
            foreach (var o in outputs) outs.Output(o);
            var hashPrevouts = Hashes.Sha256d(prevouts.ToArray());
            var hashSequence = Hashes.Sha256d(sequences.ToArray());
            var hashOutputs = Hashes.Sha256d(outs.ToArray());

            var witnesses = new List<byte[][]>();
            foreach (var input in inputs)
            {
                var privateKey = keyFor(input);
                var pubKey = Secp256k1.PublicKey(privateKey, true);
                var scriptCode = P2pkhScript(Hashes.Hash160(pubKey));
                var pre = new Writer();
                pre.U32(version).Raw(hashPrevouts).Raw(hashSequence).Outpoint(input)
                   .VarBytes(scriptCode).I64(input.Value).U32(sequence).Raw(hashOutputs).U32(lockTime).U32(SighashAll);
                var sig = Secp256k1.Sign(Hashes.Sha256d(pre.ToArray()), privateKey);
                witnesses.Add(new[] { Bytes.Concat(sig.ToDer(), new[] { (byte)SighashAll }), pubKey });
            }

            var noWitness = Serialize(inputs, outputs, version, sequence, lockTime, i => Bytes.Empty, null);
            var full = Serialize(inputs, outputs, version, sequence, lockTime, i => Bytes.Empty, witnesses);
            return new SignedUtxoTransaction(full, TxIdOf(noWitness));
        }

        public static SignedUtxoTransaction SignP2pkh(IList<Utxo> inputs, IList<TxOutput> outputs, byte[] privateKey,
            uint version = 1, uint sequence = 0xFFFFFFFF, uint lockTime = 0)
        {
            return SignP2pkh(inputs, outputs, u => privateKey, version, sequence, lockTime);
        }

        public static SignedUtxoTransaction SignP2pkh(IList<Utxo> inputs, IList<TxOutput> outputs, Func<Utxo, byte[]> keyFor,
            uint version = 1, uint sequence = 0xFFFFFFFF, uint lockTime = 0)
        {
            var scriptSigs = new byte[inputs.Count][];

            for (int i = 0; i < inputs.Count; i++)
            {
                int signing = i;
                var privateKey = keyFor(inputs[i]);
                var pubKey = Secp256k1.PublicKey(privateKey, true);
                var prevScript = P2pkhScript(Hashes.Hash160(pubKey));
                var unsigned = Serialize(inputs, outputs, version, sequence, lockTime, j => j == signing ? prevScript : Bytes.Empty, null);
                var sig = Secp256k1.Sign(Hashes.Sha256d(Bytes.Concat(unsigned, Bytes.UInt32LE(SighashAll))), privateKey);
                var der = Bytes.Concat(sig.ToDer(), new[] { (byte)SighashAll });
                scriptSigs[i] = Bytes.Concat(new[] { (byte)der.Length }, der, new[] { (byte)pubKey.Length }, pubKey);
            }

            var raw = Serialize(inputs, outputs, version, sequence, lockTime, j => scriptSigs[j], null);
            return new SignedUtxoTransaction(raw, TxIdOf(raw));
        }

        private static byte[] Serialize(IList<Utxo> inputs, IList<TxOutput> outputs, uint version, uint sequence,
            uint lockTime, Func<int, byte[]> scriptSig, List<byte[][]> witnesses)
        {
            var w = new Writer().U32(version);
            if (witnesses != null) w.Raw(new byte[] { 0x00, 0x01 });
            w.VarInt((ulong)inputs.Count);
            for (int i = 0; i < inputs.Count; i++) w.Outpoint(inputs[i]).VarBytes(scriptSig(i)).U32(sequence);
            w.VarInt((ulong)outputs.Count);
            foreach (var o in outputs) w.Output(o);
            if (witnesses != null)
            {
                foreach (var stack in witnesses)
                {
                    w.VarInt((ulong)stack.Length);
                    foreach (var item in stack) w.VarBytes(item);
                }
            }
            return w.U32(lockTime).ToArray();
        }

        private static string TxIdOf(byte[] noWitnessTx)
        {
            var hash = Hashes.Sha256d(noWitnessTx);
            Array.Reverse(hash);
            return Hex.Encode(hash);
        }

        private sealed class Writer
        {
            private readonly List<byte> _buffer = new List<byte>();

            public Writer Raw(byte[] bytes)
            {
                _buffer.AddRange(bytes);
                return this;
            }

            public Writer U32(uint value)
            {
                return Raw(Bytes.UInt32LE(value));
            }

            public Writer I64(long value)
            {
                return Raw(Bytes.UInt64LE((ulong)value));
            }

            public Writer VarInt(ulong value)
            {
                if (value < 0xfd)
                {
                    _buffer.Add((byte)value);
                    return this;
                }
                if (value <= 0xffff)
                {
                    _buffer.Add(0xfd);
                    return Raw(Bytes.UInt16LE((ushort)value));
                }
                _buffer.Add(0xfe);
                return U32((uint)value);
            }

            public Writer VarBytes(byte[] bytes)
            {
                return VarInt((ulong)bytes.Length).Raw(bytes);
            }

            public Writer Outpoint(Utxo utxo)
            {
                var txid = Hex.Decode(utxo.TxId);
                Array.Reverse(txid);
                return Raw(txid).U32(utxo.Vout);
            }

            public Writer Output(TxOutput output)
            {
                return I64(output.Value).VarBytes(output.Script);
            }

            public byte[] ToArray()
            {
                return _buffer.ToArray();
            }
        }
    }
}
