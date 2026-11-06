using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Sillet.Hardware;

namespace Sillet.App.Services
{
    public sealed class LedgerHid : ILedgerTransport
    {
        private const ushort LedgerVendor = 0x2C97;
        private const ushort ApduUsagePage = 0xFFA0;
        private readonly SafeFileHandle _handle;
        private readonly FileStream _stream;
        private readonly int _inputLength;
        private readonly int _outputLength;

        private LedgerHid(SafeFileHandle handle, int inputLength, int outputLength)
        {
            _handle = handle;
            _inputLength = inputLength;
            _outputLength = outputLength;
            _stream = new FileStream(handle, FileAccess.ReadWrite, 1, false);
        }

        public static Task<ILedgerTransport> OpenAsync()
        {
            return Task.Run(() => Find());
        }

        private static ILedgerTransport Find()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return null;
            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);
            var set = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
            if (set == new IntPtr(-1)) return null;
            try
            {
                for (uint index = 0; ; index++)
                {
                    var data = new SP_DEVICE_INTERFACE_DATA();
                    data.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref data)) break;
                    var path = PathOf(set, ref data);
                    if (path == null) continue;
                    var handle = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (handle.IsInvalid) continue;
                    var attributes = new HIDD_ATTRIBUTES();
                    attributes.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
                    if (!HidD_GetAttributes(handle, ref attributes) || attributes.VendorID != LedgerVendor)
                    {
                        handle.Dispose();
                        continue;
                    }
                    IntPtr preparsed;
                    if (!HidD_GetPreparsedData(handle, out preparsed))
                    {
                        handle.Dispose();
                        continue;
                    }
                    HIDP_CAPS caps;
                    HidP_GetCaps(preparsed, out caps);
                    HidD_FreePreparsedData(preparsed);
                    if (caps.UsagePage != ApduUsagePage || caps.InputReportByteLength < 65 || caps.OutputReportByteLength < 65)
                    {
                        handle.Dispose();
                        continue;
                    }
                    return new LedgerHid(handle, caps.InputReportByteLength, caps.OutputReportByteLength);
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
            return null;
        }

        private static string PathOf(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data)
        {
            uint required;
            SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out required, IntPtr.Zero);
            if (required == 0) return null;
            var buffer = Marshal.AllocHGlobal((int)required);
            try
            {
                Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                if (!SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, required, out required, IntPtr.Zero)) return null;
                return Marshal.PtrToStringUni(new IntPtr(buffer.ToInt64() + 4));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public async Task<byte[]> ExchangeAsync(byte[] apdu, CancellationToken ct)
        {
            using (ct.Register(() => _handle.Dispose()))
            {
                try
                {
                    return await Task.Run(async () =>
                    {
                        foreach (var packet in LedgerFraming.Pack(apdu))
                        {
                            var report = new byte[_outputLength];
                            Buffer.BlockCopy(packet, 0, report, 1, LedgerFraming.PacketSize);
                            _stream.Write(report, 0, report.Length);
                            _stream.Flush();
                        }
                        return await LedgerFraming.UnpackAsync(() =>
                        {
                            var report = new byte[_inputLength];
                            int read = 0;
                            while (read < report.Length)
                            {
                                var n = _stream.Read(report, read, report.Length - read);
                                if (n <= 0) throw new LedgerException("The Ledger was unplugged");
                                read += n;
                            }
                            var packet = new byte[LedgerFraming.PacketSize];
                            Buffer.BlockCopy(report, 1, packet, 0, LedgerFraming.PacketSize);
                            return Task.FromResult(packet);
                        });
                    }, ct);
                }
                catch (Exception e) when (e is IOException || e is ObjectDisposedException || e is UnauthorizedAccessException)
                {
                    throw new LedgerException("The Ledger was unplugged or the connection was cancelled");
                }
            }
        }

        public void Dispose()
        {
            try
            {
                _stream.Dispose();
            }
            catch (IOException)
            {
            }
            _handle.Dispose();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")]
        private static extern void HidD_GetHidGuid(out Guid guid);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr preparsed);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsed);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetCaps(IntPtr preparsed, out HIDP_CAPS caps);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string enumerator, IntPtr hwnd, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid classGuid, uint index, ref SP_DEVICE_INTERFACE_DATA data);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, uint size, out uint required, IntPtr info);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    }
}
