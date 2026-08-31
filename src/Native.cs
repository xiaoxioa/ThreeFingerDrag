using System;
using System.Runtime.InteropServices;

namespace ThreeFingerDrag
{
    internal static class Native
    {
        internal const int WM_INPUT = 0x00FF;
        internal const int WM_MOUSEMOVE = 0x0200;
        internal const int WM_QUERYENDSESSION = 0x0011;
        internal const int WM_ENDSESSION = 0x0016;
        internal const int WM_DEVICECHANGE = 0x0219;
        internal const int WM_WTSSESSION_CHANGE = 0x02B1;
        internal const int WTS_SESSION_LOCK = 0x7;
        internal const int NOTIFY_FOR_THIS_SESSION = 0;
        internal const uint RIDEV_INPUTSINK = 0x00000100;
        internal const uint RID_INPUT = 0x10000003;
        internal const uint RIDI_PREPARSEDDATA = 0x20000005;
        internal const uint RIDI_DEVICEINFO = 0x2000000B;
        internal const uint RIM_TYPEHID = 2;
        internal const uint HIDP_STATUS_SUCCESS = 0x00110000;
        internal const int WH_MOUSE_LL = 14;

        internal delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        internal struct RawInputDevice
        {
            internal ushort UsagePage;
            internal ushort Usage;
            internal uint Flags;
            internal IntPtr Target;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RawInputHeader
        {
            internal uint Type;
            internal uint Size;
            internal IntPtr Device;
            internal IntPtr WParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RawInputDeviceList
        {
            internal IntPtr Device;
            internal uint Type;
        }

        [StructLayout(LayoutKind.Explicit, Size = 32)]
        internal struct RawDeviceInfo
        {
            [FieldOffset(0)] internal uint Size;
            [FieldOffset(4)] internal uint Type;
            [FieldOffset(8)] internal uint VendorId;
            [FieldOffset(12)] internal uint ProductId;
            [FieldOffset(16)] internal uint VersionNumber;
            [FieldOffset(20)] internal ushort UsagePage;
            [FieldOffset(22)] internal ushort Usage;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HidCaps
        {
            internal ushort Usage;
            internal ushort UsagePage;
            internal ushort InputReportByteLength;
            internal ushort OutputReportByteLength;
            internal ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] internal ushort[] Reserved;
            internal ushort NumberLinkCollectionNodes;
            internal ushort NumberInputButtonCaps;
            internal ushort NumberInputValueCaps;
            internal ushort NumberInputDataIndices;
            internal ushort NumberOutputButtonCaps;
            internal ushort NumberOutputValueCaps;
            internal ushort NumberOutputDataIndices;
            internal ushort NumberFeatureButtonCaps;
            internal ushort NumberFeatureValueCaps;
            internal ushort NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HidValueCaps
        {
            internal ushort UsagePage;
            internal byte ReportId;
            [MarshalAs(UnmanagedType.U1)] internal bool IsAlias;
            internal ushort BitField;
            internal ushort LinkCollection;
            internal ushort LinkUsage;
            internal ushort LinkUsagePage;
            [MarshalAs(UnmanagedType.U1)] internal bool IsRange;
            [MarshalAs(UnmanagedType.U1)] internal bool IsStringRange;
            [MarshalAs(UnmanagedType.U1)] internal bool IsDesignatorRange;
            [MarshalAs(UnmanagedType.U1)] internal bool IsAbsolute;
            [MarshalAs(UnmanagedType.U1)] internal bool HasNull;
            internal byte Reserved;
            internal ushort BitSize;
            internal ushort ReportCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)] internal ushort[] Reserved2;
            internal uint UnitsExp;
            internal uint Units;
            internal int LogicalMin;
            internal int LogicalMax;
            internal int PhysicalMin;
            internal int PhysicalMax;
            internal ushort UsageMin;
            internal ushort UsageMax;
            internal ushort StringMin;
            internal ushort StringMax;
            internal ushort DesignatorMin;
            internal ushort DesignatorMax;
            internal ushort DataIndexMin;
            internal ushort DataIndexMax;

            internal ushort Usage { get { return UsageMin; } }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Input
        {
            internal uint Type;
            internal InputUnion Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion
        {
            [FieldOffset(0)] internal MouseInput Mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MouseInput
        {
            internal int Dx;
            internal int Dy;
            internal uint MouseData;
            internal uint Flags;
            internal uint Time;
            internal UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            internal int X;
            internal int Y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint count, uint size);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, ref RawDeviceInfo data, ref uint size);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetRawInputDeviceList([Out] RawInputDeviceList[] devices, ref uint count, uint size);

        [DllImport("hid.dll")]
        internal static extern uint HidP_GetCaps(IntPtr preparsedData, out HidCaps capabilities);

        [DllImport("hid.dll")]
        internal static extern uint HidP_GetValueCaps(int reportType, [Out] HidValueCaps[] valueCaps, ref ushort length, IntPtr preparsedData);

        [DllImport("hid.dll")]
        internal static extern uint HidP_GetUsageValue(int reportType, ushort usagePage, ushort linkCollection,
            ushort usage, out uint value, IntPtr preparsedData, IntPtr report, uint reportLength);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint count, [In] Input[] inputs, int size);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int hookId, HookProc callback, IntPtr module, uint threadId);

        [DllImport("user32.dll")]
        internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("wtsapi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WTSRegisterSessionNotification(IntPtr window, uint flags);

        [DllImport("wtsapi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WTSUnRegisterSessionNotification(IntPtr window);

        [DllImport("kernel32.dll")]
        internal static extern ulong GetTickCount64();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint GetPrivateProfileString(string section, string key, string defaultValue,
            System.Text.StringBuilder result, uint size, string fileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WritePrivateProfileString(string section, string key, string value, string fileName);
    }
}
