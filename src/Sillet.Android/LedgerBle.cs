// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.OS;
using Java.Util;
using Sillet.Hardware;

namespace Sillet.Droid
{
    public sealed class LedgerBle : ILedgerTransport
    {
        private static readonly string[] Models = { "0004", "6004", "3004", "8004" };
        private static readonly UUID NotifyConfig = UUID.FromString("00002902-0000-1000-8000-00805f9b34fb");

        private readonly BluetoothGatt _gatt;
        private readonly BluetoothGattCharacteristic _write;
        private readonly Callback _callback;
        private int _block = 20;

        private LedgerBle(BluetoothGatt gatt, BluetoothGattCharacteristic write, Callback callback)
        {
            _gatt = gatt;
            _write = write;
            _callback = callback;
        }

        private static UUID Id(string model, string part)
        {
            return UUID.FromString("13d63400-2c97-" + model + "-" + part + "-4c6564676572");
        }

        public static async Task<ILedgerTransport> OpenAsync(Context context)
        {
            var manager = context.GetSystemService(Context.BluetoothService) as BluetoothManager;
            var adapter = manager == null ? null : manager.Adapter;
            if (adapter == null || !adapter.IsEnabled) return null;
            BluetoothDevice device = null;
            foreach (var bonded in adapter.BondedDevices ?? new List<BluetoothDevice>())
            {
                var name = bonded.Name ?? "";
                if (name.IndexOf("Nano X", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Stax", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Flex", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Ledger", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    device = bonded;
                    break;
                }
            }
            if (device == null) device = await ScanAsync(adapter);
            if (device == null) return null;

            var callback = new Callback();
            var gatt = device.ConnectGatt(context, false, callback, BluetoothTransports.Le);
            if (gatt == null) return null;
            try
            {
                if (!await callback.Connected.Task.WithTimeout(15000)) throw new LedgerException("Could not connect to the Ledger over Bluetooth");
                gatt.RequestMtu(156);
                await callback.Mtu.Task.WithTimeout(5000);
                gatt.DiscoverServices();
                if (!await callback.Discovered.Task.WithTimeout(10000)) throw new LedgerException("The Ledger did not answer over Bluetooth");
                BluetoothGattCharacteristic notify = null, write = null;
                foreach (var model in Models)
                {
                    var service = gatt.GetService(Id(model, "0000"));
                    if (service == null) continue;
                    notify = service.GetCharacteristic(Id(model, "0001"));
                    write = service.GetCharacteristic(Id(model, "0002"));
                    if (notify != null && write != null) break;
                }
                if (notify == null || write == null) throw new LedgerException("This Bluetooth device is not a supported Ledger");
                gatt.SetCharacteristicNotification(notify, true);
                var config = notify.GetDescriptor(NotifyConfig);
                if (config != null)
                {
                    config.SetValue(BluetoothGattDescriptor.EnableNotificationValue.ToArray());
                    gatt.WriteDescriptor(config);
                    await callback.DescriptorWritten.Task.WithTimeout(5000);
                }
                var transport = new LedgerBle(gatt, write, callback);
                await transport.InferBlockSizeAsync();
                return transport;
            }
            catch (Exception)
            {
                gatt.Close();
                throw;
            }
        }

        private static async Task<BluetoothDevice> ScanAsync(BluetoothAdapter adapter)
        {
            var scanner = adapter.BluetoothLeScanner;
            if (scanner == null) return null;
            var found = new TaskCompletionSource<BluetoothDevice>();
            var callback = new ScanHandler(found);
            var filters = Models.Select(m => new ScanFilter.Builder().SetServiceUuid(new ParcelUuid(Id(m, "0000"))).Build()).ToList();
            scanner.StartScan(filters, new ScanSettings.Builder().SetScanMode(Android.Bluetooth.LE.ScanMode.LowLatency).Build(), callback);
            try
            {
                var done = await Task.WhenAny(found.Task, Task.Delay(8000));
                return done == found.Task ? found.Task.Result : null;
            }
            finally
            {
                scanner.StopScan(callback);
            }
        }

        private async Task InferBlockSizeAsync()
        {
            Send(new byte[] { 0x08, 0x00, 0x00, 0x00, 0x00 });
            var answer = await _callback.NextAsync(CancellationToken.None, 5000);
            if (answer != null && answer.Length >= 6 && answer[0] == 0x08) _block = Math.Max(20, (int)answer[5]);
        }

        private void Send(byte[] packet)
        {
            _callback.WriteDone = new TaskCompletionSource<bool>();
            _write.SetValue(packet);
            _write.WriteType = GattWriteType.Default;
            if (!_gatt.WriteCharacteristic(_write)) throw new LedgerException("The Ledger was disconnected");
        }

        public async Task<byte[]> ExchangeAsync(byte[] apdu, CancellationToken ct)
        {
            foreach (var packet in LedgerFraming.PackBle(apdu, _block))
            {
                Send(packet);
                if (!await _callback.WriteDone.Task.WithTimeout(5000)) throw new LedgerException("The Ledger was disconnected");
            }
            return await LedgerFraming.UnpackBleAsync(async () =>
            {
                var packet = await _callback.NextAsync(ct, 180000);
                if (packet == null) throw new LedgerException("The Ledger did not answer in time");
                return packet;
            });
        }

        public void Dispose()
        {
            try
            {
                _gatt.Disconnect();
                _gatt.Close();
            }
            catch (Java.Lang.Exception)
            {
            }
        }

        private sealed class ScanHandler : ScanCallback
        {
            private readonly TaskCompletionSource<BluetoothDevice> _found;

            public ScanHandler(TaskCompletionSource<BluetoothDevice> found)
            {
                _found = found;
            }

            public override void OnScanResult(ScanCallbackType callbackType, ScanResult result)
            {
                if (result != null && result.Device != null) _found.TrySetResult(result.Device);
            }
        }

        private sealed class Callback : BluetoothGattCallback
        {
            private readonly ConcurrentQueue<byte[]> _packets = new ConcurrentQueue<byte[]>();
            private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);

            public TaskCompletionSource<bool> Connected = new TaskCompletionSource<bool>();
            public TaskCompletionSource<bool> Mtu = new TaskCompletionSource<bool>();
            public TaskCompletionSource<bool> Discovered = new TaskCompletionSource<bool>();
            public TaskCompletionSource<bool> DescriptorWritten = new TaskCompletionSource<bool>();
            public volatile TaskCompletionSource<bool> WriteDone = new TaskCompletionSource<bool>();

            public override void OnConnectionStateChange(BluetoothGatt gatt, GattStatus status, ProfileState newState)
            {
                if (newState == ProfileState.Connected && status == GattStatus.Success) Connected.TrySetResult(true);
                else if (newState == ProfileState.Disconnected) Connected.TrySetResult(false);
            }

            public override void OnMtuChanged(BluetoothGatt gatt, int mtu, GattStatus status)
            {
                Mtu.TrySetResult(status == GattStatus.Success);
            }

            public override void OnServicesDiscovered(BluetoothGatt gatt, GattStatus status)
            {
                Discovered.TrySetResult(status == GattStatus.Success);
            }

            public override void OnDescriptorWrite(BluetoothGatt gatt, BluetoothGattDescriptor descriptor, GattStatus status)
            {
                DescriptorWritten.TrySetResult(status == GattStatus.Success);
            }

            public override void OnCharacteristicWrite(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, GattStatus status)
            {
                WriteDone.TrySetResult(status == GattStatus.Success);
            }

            public override void OnCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic)
            {
                var value = characteristic.GetValue();
                if (value == null) return;
                _packets.Enqueue(value);
                _signal.Release();
            }

            public async Task<byte[]> NextAsync(CancellationToken ct, int timeoutMs)
            {
                if (!await _signal.WaitAsync(timeoutMs, ct)) return null;
                byte[] packet;
                return _packets.TryDequeue(out packet) ? packet : null;
            }
        }
    }

    internal static class TaskTimeout
    {
        public static async Task<bool> WithTimeout(this Task<bool> task, int milliseconds)
        {
            var done = await Task.WhenAny(task, Task.Delay(milliseconds));
            return done == task && task.Result;
        }
    }
}
