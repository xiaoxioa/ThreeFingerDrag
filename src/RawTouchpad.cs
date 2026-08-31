using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ThreeFingerDrag
{
    internal sealed class RawTouchpad : IDisposable
    {
        private sealed class DeviceData : IDisposable
        {
            internal byte[] Preparsed;
            internal GCHandle Pin;
            internal Native.HidValueCaps[] Caps;
            internal ushort[] Collections;
            internal double XMin;
            internal double XRange = 1;
            internal double YMin;
            internal double YRange = 1;

            public void Dispose()
            {
                if (Pin.IsAllocated) Pin.Free();
            }
        }

        private readonly Dictionary<IntPtr, DeviceData> devices = new Dictionary<IntPtr, DeviceData>();

        internal bool Register(IntPtr window)
        {
            Native.RawInputDevice input = new Native.RawInputDevice();
            input.UsagePage = 0x000D;
            input.Usage = 0x0005;
            input.Flags = Native.RIDEV_INPUTSINK;
            input.Target = window;
            return Native.RegisterRawInputDevices(new[] { input }, 1,
                (uint)Marshal.SizeOf(typeof(Native.RawInputDevice)));
        }

        internal static bool Exists()
        {
            uint count = 0;
            uint itemSize = (uint)Marshal.SizeOf(typeof(Native.RawInputDeviceList));
            if (Native.GetRawInputDeviceList(null, ref count, itemSize) != 0 || count == 0)
                return false;
            Native.RawInputDeviceList[] list = new Native.RawInputDeviceList[count];
            if (Native.GetRawInputDeviceList(list, ref count, itemSize) == UInt32.MaxValue)
                return false;
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i].Type != Native.RIM_TYPEHID) continue;
                Native.RawDeviceInfo info = new Native.RawDeviceInfo();
                info.Size = (uint)Marshal.SizeOf(typeof(Native.RawDeviceInfo));
                uint size = info.Size;
                if (Native.GetRawInputDeviceInfo(list[i].Device, Native.RIDI_DEVICEINFO, ref info, ref size) != UInt32.MaxValue &&
                    info.UsagePage == 0x000D && info.Usage == 0x0005)
                    return true;
            }
            return false;
        }

        internal IList<ContactPoint> Parse(IntPtr rawInputHandle)
        {
            uint size = 0;
            uint headerSize = (uint)Marshal.SizeOf(typeof(Native.RawInputHeader));
            if (Native.GetRawInputData(rawInputHandle, Native.RID_INPUT, IntPtr.Zero, ref size, headerSize) != 0 || size == 0)
                return Empty();

            byte[] bytes = new byte[size];
            GCHandle pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                IntPtr pointer = pin.AddrOfPinnedObject();
                if (Native.GetRawInputData(rawInputHandle, Native.RID_INPUT, pointer, ref size, headerSize) != size)
                    return Empty();
                Native.RawInputHeader header = (Native.RawInputHeader)Marshal.PtrToStructure(pointer, typeof(Native.RawInputHeader));
                if (header.Type != Native.RIM_TYPEHID)
                    return Empty();

                DeviceData device = GetDevice(header.Device);
                if (device == null)
                    return Empty();

                int hidOffset = (int)headerSize;
                uint reportSize = (uint)Marshal.ReadInt32(pointer, hidOffset);
                uint reportCount = (uint)Marshal.ReadInt32(pointer, hidOffset + 4);
                int dataOffset = hidOffset + 8;
                if (reportSize == 0 || reportCount == 0 || dataOffset + (long)reportSize * reportCount > bytes.Length)
                    return Empty();

                List<ContactPoint> contacts = new List<ContactPoint>(5);
                int expected = -1;
                HashSet<int> ids = new HashSet<int>();
                for (uint reportIndex = 0; reportIndex < reportCount; reportIndex++)
                {
                    IntPtr report = IntPtr.Add(pointer, dataOffset + (int)(reportIndex * reportSize));
                    int reportExpected;
                    if (TryValue(device, report, reportSize, 0x0D, 0, 0x54, out reportExpected))
                    {
                        if (reportExpected > 0 || expected < 0)
                            expected = reportExpected;
                    }
                    if (expected == 0)
                        continue;

                    foreach (ushort collection in device.Collections)
                    {
                        int id, x, y;
                        if (!TryValue(device, report, reportSize, 0x0D, collection, 0x51, out id) ||
                            !TryValue(device, report, reportSize, 0x01, collection, 0x30, out x) ||
                            !TryValue(device, report, reportSize, 0x01, collection, 0x31, out y))
                            continue;
                        if (ids.Add(id))
                        {
                            double normalizedX = Clamp((x - device.XMin) / device.XRange);
                            double normalizedY = Clamp((y - device.YMin) / device.YRange);
                            contacts.Add(new ContactPoint(id, normalizedX, normalizedY));
                        }
                        if (expected > 0 && contacts.Count >= expected)
                            break;
                    }
                    if (expected > 0 && contacts.Count >= expected)
                        break;
                }
                if (expected == 0)
                    contacts.Clear();
                else if (expected > 0 && contacts.Count > expected)
                    contacts.RemoveRange(expected, contacts.Count - expected);
                return contacts;
            }
            finally
            {
                pin.Free();
            }
        }

        internal void ClearDevices()
        {
            foreach (DeviceData device in devices.Values) device.Dispose();
            devices.Clear();
        }

        public void Dispose()
        {
            ClearDevices();
        }

        private DeviceData GetDevice(IntPtr handle)
        {
            DeviceData existing;
            if (devices.TryGetValue(handle, out existing)) return existing;

            uint size = 0;
            if (Native.GetRawInputDeviceInfo(handle, Native.RIDI_PREPARSEDDATA, IntPtr.Zero, ref size) != 0 || size == 0)
                return null;
            DeviceData result = new DeviceData();
            result.Preparsed = new byte[size];
            result.Pin = GCHandle.Alloc(result.Preparsed, GCHandleType.Pinned);
            IntPtr prep = result.Pin.AddrOfPinnedObject();
            if (Native.GetRawInputDeviceInfo(handle, Native.RIDI_PREPARSEDDATA, prep, ref size) != size)
            {
                result.Dispose();
                return null;
            }
            Native.HidCaps hidCaps;
            if (Native.HidP_GetCaps(prep, out hidCaps) != Native.HIDP_STATUS_SUCCESS)
            {
                result.Dispose();
                return null;
            }
            ushort valueCount = hidCaps.NumberInputValueCaps;
            result.Caps = new Native.HidValueCaps[valueCount];
            if (Native.HidP_GetValueCaps(0, result.Caps, ref valueCount, prep) != Native.HIDP_STATUS_SUCCESS)
            {
                result.Dispose();
                return null;
            }
            SortedSet<ushort> collections = new SortedSet<ushort>();
            for (int i = 0; i < result.Caps.Length; i++)
                if (result.Caps[i].LinkCollection != 0)
                    collections.Add(result.Caps[i].LinkCollection);
            result.Collections = new ushort[collections.Count];
            collections.CopyTo(result.Collections);
            FindAxis(result, 0x30, true);
            FindAxis(result, 0x31, false);
            devices.Add(handle, result);
            return result;
        }

        private static void FindAxis(DeviceData device, ushort usage, bool xAxis)
        {
            for (int i = 0; i < device.Caps.Length; i++)
            {
                Native.HidValueCaps cap = device.Caps[i];
                if (cap.UsagePage == 0x01 && cap.Usage == usage && cap.LinkCollection != 0)
                {
                    double range = Math.Max(1, (double)cap.LogicalMax - cap.LogicalMin);
                    if (xAxis) { device.XMin = cap.LogicalMin; device.XRange = range; }
                    else { device.YMin = cap.LogicalMin; device.YRange = range; }
                    return;
                }
            }
        }

        private static bool TryValue(DeviceData device, IntPtr report, uint reportLength,
            ushort page, ushort collection, ushort usage, out int result)
        {
            result = 0;
            for (int i = 0; i < device.Caps.Length; i++)
            {
                Native.HidValueCaps cap = device.Caps[i];
                if (cap.UsagePage != page || cap.LinkCollection != collection || cap.Usage != usage)
                    continue;
                uint value;
                if (Native.HidP_GetUsageValue(0, page, collection, usage, out value,
                    device.Pin.AddrOfPinnedObject(), report, reportLength) == Native.HIDP_STATUS_SUCCESS)
                {
                    result = unchecked((int)value);
                    return true;
                }
            }
            return false;
        }

        private static double Clamp(double value)
        {
            return value < 0 ? 0 : (value > 1 ? 1 : value);
        }

        private static IList<ContactPoint> Empty()
        {
            return new ContactPoint[0];
        }
    }
}
