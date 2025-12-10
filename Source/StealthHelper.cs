using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Source
{
    public static class StealthHelper
    {
        // ================= WINDOWS MAGIC =================
        [DllImport("user32.dll")]
        private static extern uint SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        // ================= MAC MAGIC =================
        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_registerName")]
        private static extern IntPtr sel_registerName(string name);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern void void_objc_msgSend_long(IntPtr receiver, IntPtr selector, long arg1);

        public static void SetStealth(Window window)
        {
            try
            {
                var handle = window.TryGetPlatformHandle();
                if (handle == null) return;

                // 1. IF WINDOWS
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // 0x00000011 makes it invisible to capture
                    SetWindowDisplayAffinity(handle.Handle, WDA_EXCLUDEFROMCAPTURE);
                    Console.WriteLine("Stealth Mode: Active (Windows)");
                }
                // 2. IF MAC
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // On Mac, we assume the Handle IS the NSWindow pointer
                    IntPtr nsWindow = handle.Handle;
                    
                    // Selector: setSharingType:
                    // Value: 0 (NSWindowSharingNone) -> Makes it invisible to screenshots/sharing
                    IntPtr selector = sel_registerName("setSharingType:");
                    void_objc_msgSend_long(nsWindow, selector, 0);
                    
                    Console.WriteLine("Stealth Mode: Active (Mac)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Stealth Error: " + ex.Message);
            }
        }
    }
}