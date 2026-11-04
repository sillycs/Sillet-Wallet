using System;
using System.Runtime.InteropServices;
using System.Security.AccessControl;

namespace Sillet.App.Platform
{
    public static class ProcessGuard
    {
        private const int DaclSecurityInformation = 0x4;
        private const int ProcessExtensionPointDisablePolicy = 6;
        private const int ProcessImageLoadPolicy = 10;
        private const string RestrictedDacl = "D:P(A;;0x101001;;;WD)(A;;0x101001;;;OW)(A;;0x1fffff;;;SY)";

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetKernelObjectSecurity(IntPtr handle, int securityInformation, byte[] securityDescriptor);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessMitigationPolicy(int policy, ref int buffer, IntPtr length);

        [DllImport("kernel32.dll")]
        private static extern bool IsDebuggerPresent();

        public static bool MemoryLocked { get; private set; }
        public static bool InjectionBlocked { get; private set; }

        public static void Apply()
        {
            MemoryLocked = LockMemory();
            InjectionBlocked = BlockInjection();
        }

        public static bool NativeDebuggerAttached()
        {
            try
            {
                return IsDebuggerPresent() && !System.Diagnostics.Debugger.IsAttached;
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                return false;
            }
        }

        private static bool LockMemory()
        {
            try
            {
                var descriptor = new RawSecurityDescriptor(RestrictedDacl);
                var bytes = new byte[descriptor.BinaryLength];
                descriptor.GetBinaryForm(bytes, 0);
                return SetKernelObjectSecurity(GetCurrentProcess(), DaclSecurityInformation, bytes);
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException || e is ArgumentException || e is System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }

        private static bool BlockInjection()
        {
            bool ok = true;
            ok &= Mitigate(ProcessImageLoadPolicy, 0x2 | 0x4);
            ok &= Mitigate(ProcessExtensionPointDisablePolicy, 0x1);
            return ok;
        }

        private static bool Mitigate(int policy, int flags)
        {
            try
            {
                var value = flags;
                return SetProcessMitigationPolicy(policy, ref value, (IntPtr)4);
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                return false;
            }
        }
    }
}
