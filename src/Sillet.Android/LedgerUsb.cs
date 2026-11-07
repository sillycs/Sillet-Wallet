// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Hardware.Usb;
using Android.OS;
using Sillet.Hardware;

namespace Sillet.Droid
{
    public sealed class LedgerUsb : ILedgerTransport
    {
        private const int LedgerVendor = 0x2C97;
        private const string PermissionAction = "fun.raton.sillet.USB_PERMISSION";
        private readonly UsbDeviceConnection _connection;
        private readonly UsbEndpoint _in;
        private readonly UsbEndpoint _out;

        private LedgerUsb(UsbDeviceConnection connection, UsbEndpoint input, UsbEndpoint output)
        {
            _connection = connection;
            _in = input;
            _out = output;
        }

        public static async Task<ILedgerTransport> OpenAsync(Context context)
        {
            var manager = context.GetSystemService(Context.UsbService) as UsbManager;
            if (manager == null) return null;
            var device = manager.DeviceList.Values.FirstOrDefault(d => d.VendorId == LedgerVendor);
            if (device == null) return null;
            if (!manager.HasPermission(device) && !await RequestPermissionAsync(context, manager, device)) return null;
            for (int i = 0; i < device.InterfaceCount; i++)
            {
                var face = device.GetInterface(i);
                UsbEndpoint input = null, output = null;
                for (int j = 0; j < face.EndpointCount; j++)
                {
                    var endpoint = face.GetEndpoint(j);
                    if (endpoint.Direction == UsbAddressing.In) input = endpoint;
                    else if (endpoint.Direction == UsbAddressing.Out) output = endpoint;
                }
                if (input == null || output == null) continue;
                var connection = manager.OpenDevice(device);
                if (connection == null) return null;
                if (!connection.ClaimInterface(face, true))
                {
                    connection.Close();
                    continue;
                }
                return new LedgerUsb(connection, input, output);
            }
            return null;
        }

        private static Task<bool> RequestPermissionAsync(Context context, UsbManager manager, UsbDevice device)
        {
            var source = new TaskCompletionSource<bool>();
            var receiver = new PermissionReceiver(source);
            var filter = new IntentFilter(PermissionAction);
            if (OperatingSystem.IsAndroidVersionAtLeast(33)) context.RegisterReceiver(receiver, filter, ReceiverFlags.NotExported);
            else context.RegisterReceiver(receiver, filter);
            var intent = new Intent(PermissionAction);
            intent.SetPackage(context.PackageName);
            var pending = PendingIntent.GetBroadcast(context, 0, intent, PendingIntentFlags.Mutable | PendingIntentFlags.UpdateCurrent);
            manager.RequestPermission(device, pending);
            source.Task.ContinueWith(t =>
            {
                try
                {
                    context.UnregisterReceiver(receiver);
                }
                catch (Java.Lang.Exception)
                {
                }
            });
            return source.Task;
        }

        public Task<byte[]> ExchangeAsync(byte[] apdu, CancellationToken ct)
        {
            return Task.Run(async () =>
            {
                foreach (var packet in LedgerFraming.Pack(apdu))
                {
                    if (_connection.BulkTransfer(_out, packet, packet.Length, 5000) < 0) throw new LedgerException("The Ledger was unplugged");
                }
                return await LedgerFraming.UnpackAsync(() =>
                {
                    var buffer = new byte[LedgerFraming.PacketSize];
                    var until = DateTime.UtcNow.AddMinutes(3);
                    while (true)
                    {
                        ct.ThrowIfCancellationRequested();
                        var read = _connection.BulkTransfer(_in, buffer, buffer.Length, 1000);
                        if (read >= 0) return Task.FromResult(buffer);
                        if (DateTime.UtcNow > until) throw new LedgerException("The Ledger did not answer in time");
                    }
                });
            }, ct);
        }

        public void Dispose()
        {
            _connection.Close();
        }

        private sealed class PermissionReceiver : BroadcastReceiver
        {
            private readonly TaskCompletionSource<bool> _source;

            public PermissionReceiver(TaskCompletionSource<bool> source)
            {
                _source = source;
            }

            public override void OnReceive(Context context, Intent intent)
            {
                _source.TrySetResult(intent != null && intent.GetBooleanExtra(UsbManager.ExtraPermissionGranted, false));
            }
        }
    }
}
