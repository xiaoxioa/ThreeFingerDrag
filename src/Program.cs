using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ThreeFingerDrag
{
    internal sealed class MouseOutput : IDragOutput
    {
        private bool down;
        private double remainderX;
        private double remainderY;
        private bool hasAnchor;
        private int anchorContactId;
        private double anchorPadX;
        private double anchorPadY;
        private Native.Point anchorCursor;
        private long anchorTime;

        internal double Sensitivity = 1.0;
        internal double CalibratedFactor = 0.75;
        internal bool AutoCalibrate = true;
        internal bool CalibrationDirty;

        public void ButtonDown()
        {
            if (down) return;
            Send(0, 0, 0x0002);
            down = true;
        }

        public void Move(double normalizedX, double normalizedY)
        {
            int width = Math.Max(1, SystemInformation.VirtualScreen.Width);
            int height = Math.Max(1, SystemInformation.VirtualScreen.Height);
            double exactX = normalizedX * width * CalibratedFactor * Sensitivity + remainderX;
            double exactY = normalizedY * height * CalibratedFactor * Sensitivity + remainderY;
            int deltaX = (int)Math.Truncate(exactX);
            int deltaY = (int)Math.Truncate(exactY);
            remainderX = exactX - deltaX;
            remainderY = exactY - deltaY;
            if (deltaX == 0 && deltaY == 0) return;

            Native.Point cursor;
            if (!Native.GetCursorPos(out cursor)) return;
            Rectangle screen = SystemInformation.VirtualScreen;
            int targetX = Math.Max(screen.Left, Math.Min(screen.Right - 1, cursor.X + deltaX));
            int targetY = Math.Max(screen.Top, Math.Min(screen.Bottom - 1, cursor.Y + deltaY));
            if (targetX != cursor.X || targetY != cursor.Y)
                SendAbsolute(targetX, targetY, screen);
        }

        public void ButtonUp()
        {
            if (!down) return;
            Send(0, 0, 0x0004);
            down = false;
            remainderX = remainderY = 0;
        }

        internal void ObserveSingleFinger(IList<ContactPoint> contacts, long now)
        {
            if (!AutoCalibrate || contacts.Count != 1)
            {
                hasAnchor = false;
                return;
            }

            Native.Point cursor;
            if (!Native.GetCursorPos(out cursor)) return;
            ContactPoint contact = contacts[0];
            if (!hasAnchor || contact.Id != anchorContactId)
            {
                SetAnchor(contact, cursor, now);
                return;
            }

            Rectangle screen = SystemInformation.VirtualScreen;
            double padX = (contact.X - anchorPadX) * Math.Max(1, screen.Width);
            double padY = (contact.Y - anchorPadY) * Math.Max(1, screen.Height);
            double padLength = Math.Sqrt(padX * padX + padY * padY);
            if (padLength < 32)
            {
                if (now - anchorTime > 700) SetAnchor(contact, cursor, now);
                return;
            }

            double cursorX = cursor.X - anchorCursor.X;
            double cursorY = cursor.Y - anchorCursor.Y;
            double cursorLength = Math.Sqrt(cursorX * cursorX + cursorY * cursorY);
            bool nearEdge = anchorCursor.X <= screen.Left + 8 || anchorCursor.X >= screen.Right - 9 ||
                anchorCursor.Y <= screen.Top + 8 || anchorCursor.Y >= screen.Bottom - 9 ||
                cursor.X <= screen.Left + 8 || cursor.X >= screen.Right - 9 ||
                cursor.Y <= screen.Top + 8 || cursor.Y >= screen.Bottom - 9;
            double dot = padX * cursorX + padY * cursorY;
            double direction = cursorLength > 0 ? dot / (padLength * cursorLength) : 0;
            double learned = dot / (padLength * padLength);
            if (!nearEdge && direction > 0.75 && learned >= 0.15 && learned <= 3.0)
            {
                CalibratedFactor = CalibratedFactor * 0.72 + learned * 0.28;
                CalibrationDirty = true;
            }
            SetAnchor(contact, cursor, now);
        }

        internal void ResetCalibration()
        {
            CalibratedFactor = 0.75;
            CalibrationDirty = true;
            hasAnchor = false;
        }

        private void SetAnchor(ContactPoint contact, Native.Point cursor, long now)
        {
            hasAnchor = true;
            anchorContactId = contact.Id;
            anchorPadX = contact.X;
            anchorPadY = contact.Y;
            anchorCursor = cursor;
            anchorTime = now;
        }

        private static void Send(int x, int y, uint flags)
        {
            Native.Input input = new Native.Input();
            input.Type = 0;
            input.Data.Mouse.Dx = x;
            input.Data.Mouse.Dy = y;
            input.Data.Mouse.Flags = flags;
            Native.SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Native.Input)));
        }

        private static void SendAbsolute(int x, int y, Rectangle screen)
        {
            int width = Math.Max(2, screen.Width);
            int height = Math.Max(2, screen.Height);
            int normalizedX = (int)Math.Round((x - screen.Left) * 65535.0 / (width - 1));
            int normalizedY = (int)Math.Round((y - screen.Top) * 65535.0 / (height - 1));
            Send(normalizedX, normalizedY, 0x0001 | 0x4000 | 0x8000);
        }
    }

    internal sealed class AppWindow : NativeWindow, IDisposable
    {
        private readonly RawTouchpad touchpad;
        private readonly GestureEngine gesture;
        private readonly MouseOutput mouse;
        private readonly Action deviceChanged;

        internal AppWindow(RawTouchpad touchpad, GestureEngine gesture, MouseOutput mouse, Action deviceChanged)
        {
            this.touchpad = touchpad;
            this.gesture = gesture;
            this.mouse = mouse;
            this.deviceChanged = deviceChanged;
            CreateParams parameters = new CreateParams();
            parameters.Caption = "ThreeFingerDrag.HiddenWindow";
            CreateHandle(parameters);
            Native.WTSRegisterSessionNotification(Handle, Native.NOTIFY_FOR_THIS_SESSION);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == Native.WM_INPUT)
            {
                try
                {
                    long now = (long)Native.GetTickCount64();
                    IList<ContactPoint> contacts = touchpad.Parse(message.LParam);
                    mouse.ObserveSingleFinger(contacts, now);
                    gesture.Update(contacts, now);
                }
                catch { gesture.Cancel(); }
            }
            else if (message.Msg == Native.WM_DEVICECHANGE)
            {
                gesture.Cancel();
                touchpad.ClearDevices();
                if (deviceChanged != null) deviceChanged();
            }
            else if (message.Msg == Native.WM_WTSSESSION_CHANGE && message.WParam.ToInt32() == Native.WTS_SESSION_LOCK)
                gesture.Cancel();
            else if (message.Msg == Native.WM_QUERYENDSESSION || message.Msg == Native.WM_ENDSESSION)
                gesture.Cancel();
            base.WndProc(ref message);
        }

        public void Dispose()
        {
            gesture.Cancel();
            if (Handle != IntPtr.Zero)
            {
                Native.WTSUnRegisterSessionNotification(Handle);
                DestroyHandle();
            }
        }
    }

    internal sealed class DragApplication : ApplicationContext
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "ThreeFingerDrag";
        private readonly string configPath;
        private readonly NotifyIcon tray;
        private readonly RawTouchpad touchpad;
        private readonly MouseOutput mouse;
        private readonly GestureEngine gesture;
        private readonly AppWindow window;
        private readonly System.Windows.Forms.Timer timer;
        private MenuItem enabledItem;
        private MenuItem graceItem;
        private MenuItem startupItem;
        private MenuItem followSpeedItem;
        private long lastCalibrationSave;
        private bool hasGraceCursor;
        private Native.Point graceCursor;
        private readonly Native.HookProc mouseHookCallback;
        private IntPtr mouseHook;

        internal DragApplication()
        {
            configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ThreeFingerDrag.ini");
            mouse = new MouseOutput();
            gesture = new GestureEngine(mouse);
            gesture.GraceStarted += OnGraceStarted;
            LoadSettings();
            touchpad = new RawTouchpad();
            window = new AppWindow(touchpad, gesture, mouse, RefreshTooltip);
            mouseHookCallback = OnLowLevelMouse;
            mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mouseHookCallback,
                Native.GetModuleHandle(null), 0);

            tray = new NotifyIcon();
            tray.Icon = SystemIcons.Hand;
            tray.Visible = true;
            tray.ContextMenu = BuildMenu();
            tray.DoubleClick += delegate { ToggleEnabled(); };

            bool registered = touchpad.Register(window.Handle);
            bool touchpadFound = RawTouchpad.Exists();
            if (!registered)
            {
                ShowError("启动失败：无法注册触控板 Raw Input。错误码：" + Marshal.GetLastWin32Error());
            }
            else if (!touchpadFound)
            {
                tray.ShowBalloonTip(5000, "ThreeFingerDrag 启动异常",
                    "未检测到 Windows Precision Touchpad，三指拖拽暂时不可用。",
                    ToolTipIcon.Warning);
            }
            else
            {
                tray.ShowBalloonTip(3500, "ThreeFingerDrag 已启动",
                    gesture.Enabled ? "三指拖拽已启用；右击托盘图标可调整设置。" : "程序已运行，但三指拖拽当前处于暂停状态。",
                    ToolTipIcon.Info);
            }
            RefreshTooltip();

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 15;
            timer.Tick += delegate
            {
                long now = (long)Native.GetTickCount64();
                if (gesture.IsInGrace && hasGraceCursor)
                {
                    Native.Point cursor;
                    if (Native.GetCursorPos(out cursor) &&
                        (cursor.X != graceCursor.X || cursor.Y != graceCursor.Y))
                    {
                        gesture.Cancel();
                        hasGraceCursor = false;
                    }
                }
                else if (!gesture.IsInGrace)
                {
                    hasGraceCursor = false;
                }
                gesture.Tick(now);
                if (mouse.CalibrationDirty && now - lastCalibrationSave > 3000)
                {
                    SaveSettings();
                    mouse.CalibrationDirty = false;
                    lastCalibrationSave = now;
                }
            };
            timer.Start();

            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        private ContextMenu BuildMenu()
        {
            enabledItem = new MenuItem("已启用", delegate { ToggleEnabled(); });
            enabledItem.Checked = gesture.Enabled;

            followSpeedItem = new MenuItem("跟随 Windows 指针速度（自动学习）", delegate
            {
                followSpeedItem.Checked = !followSpeedItem.Checked;
                mouse.AutoCalibrate = followSpeedItem.Checked;
                SaveSettings();
            });
            followSpeedItem.Checked = mouse.AutoCalibrate;

            MenuItem sensitivity = new MenuItem("相对 Windows 速度");
            AddSensitivity(sensitivity, "慢 (0.75×)", 0.75);
            AddSensitivity(sensitivity, "一致 (1.00×)", 1.00);
            AddSensitivity(sensitivity, "稍快 (1.15×)", 1.15);
            AddSensitivity(sensitivity, "快 (1.30×)", 1.30);
            sensitivity.MenuItems.Add(new MenuItem("-"));
            sensitivity.MenuItems.Add(new MenuItem("重置自动校准", delegate
            {
                mouse.ResetCalibration();
                SaveSettings();
            }));

            graceItem = new MenuItem("抬指后短暂保持拖拽", delegate
            {
                graceItem.Checked = !graceItem.Checked;
                gesture.GraceMilliseconds = graceItem.Checked ? 350 : 0;
                SaveSettings();
            });
            graceItem.Checked = gesture.GraceMilliseconds > 0;

            startupItem = new MenuItem("开机自启（当前用户）", delegate { ToggleStartup(); });
            startupItem.Checked = IsStartupEnabled();

            MenuItem settings = new MenuItem("打开 Windows 触控板设置", delegate
            {
                try { Process.Start(new ProcessStartInfo("ms-settings:devices-touchpad") { UseShellExecute = true }); }
                catch (Exception ex) { ShowError(ex.Message); }
            });
            MenuItem about = new MenuItem("关于 / 使用提示", delegate
            {
                MessageBox.Show("ThreeFingerDrag 2.0\n\n三指放在 Precision Touchpad 上并移动，即可拖动窗口、文件或选中文本。\n程序会通过日常单指移动自动学习 Windows 指针速度。\n\n请在 Windows 触控板设置中把三指轻扫和三指点击设为‘无’。\n双击托盘图标可快速启用/停用。",
                    "ThreeFingerDrag", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
            MenuItem exit = new MenuItem("退出", delegate { ExitThread(); });
            return new ContextMenu(new[] { enabledItem, followSpeedItem, sensitivity, graceItem, startupItem,
                new MenuItem("-"), settings, about, new MenuItem("-"), exit });
        }

        private void AddSensitivity(MenuItem parent, string title, double value)
        {
            MenuItem item = new MenuItem(title);
            item.RadioCheck = true;
            item.Checked = Math.Abs(mouse.Sensitivity - value) < 0.001;
            item.Click += delegate
            {
                mouse.Sensitivity = value;
                foreach (MenuItem sibling in parent.MenuItems) sibling.Checked = false;
                item.Checked = true;
                SaveSettings();
            };
            parent.MenuItems.Add(item);
        }

        private void ToggleEnabled()
        {
            gesture.Enabled = !gesture.Enabled;
            if (!gesture.Enabled) gesture.Cancel();
            enabledItem.Checked = gesture.Enabled;
            SaveSettings();
            RefreshTooltip();
        }

        private void RefreshTooltip()
        {
            if (tray == null) return;
            string state = gesture.Enabled ? "运行中" : "已暂停";
            if (!RawTouchpad.Exists()) state = "未检测到 Precision Touchpad";
            tray.Text = "ThreeFingerDrag - " + state;
        }

        private void ToggleStartup()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (startupItem.Checked) key.DeleteValue(RunName, false);
                    else key.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                }
                startupItem.Checked = !startupItem.Checked;
            }
            catch (Exception ex) { ShowError("修改开机启动失败：" + ex.Message); }
        }

        private static bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key == null) return false;
                    string command = key.GetValue(RunName) as string;
                    if (String.IsNullOrWhiteSpace(command)) return false;
                    return String.Equals(command.Trim().Trim('"'), Application.ExecutablePath,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        private void LoadSettings()
        {
            gesture.Enabled = ReadIni("enabled", "1") != "0";
            int grace;
            gesture.GraceMilliseconds = Int32.TryParse(ReadIni("grace_ms", "350"), out grace) ? Math.Max(0, Math.Min(1000, grace)) : 350;
            double sensitivity;
            string version = ReadIni("settings_version", "1");
            mouse.Sensitivity = version == "2" && Double.TryParse(ReadIni("sensitivity", "1.00"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out sensitivity) ? Math.Max(0.5, Math.Min(2, sensitivity)) : 1.00;
            double calibration;
            mouse.CalibratedFactor = Double.TryParse(ReadIni("calibrated_factor", "0.75"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out calibration) ? Math.Max(0.15, Math.Min(3, calibration)) : 0.75;
            mouse.AutoCalibrate = ReadIni("auto_calibrate", "1") != "0";
        }

        private void SaveSettings()
        {
            try
            {
                Native.WritePrivateProfileString("general", "enabled", gesture.Enabled ? "1" : "0", configPath);
                Native.WritePrivateProfileString("general", "grace_ms", gesture.GraceMilliseconds.ToString(CultureInfo.InvariantCulture), configPath);
                Native.WritePrivateProfileString("general", "sensitivity", mouse.Sensitivity.ToString(CultureInfo.InvariantCulture), configPath);
                Native.WritePrivateProfileString("general", "calibrated_factor", mouse.CalibratedFactor.ToString(CultureInfo.InvariantCulture), configPath);
                Native.WritePrivateProfileString("general", "auto_calibrate", mouse.AutoCalibrate ? "1" : "0", configPath);
                Native.WritePrivateProfileString("general", "settings_version", "2", configPath);
            }
            catch { }
        }

        private string ReadIni(string key, string fallback)
        {
            StringBuilder result = new StringBuilder(128);
            Native.GetPrivateProfileString("general", key, fallback, result, (uint)result.Capacity, configPath);
            return result.ToString();
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend) gesture.Cancel();
        }

        private void OnGraceStarted()
        {
            hasGraceCursor = Native.GetCursorPos(out graceCursor);
        }

        private IntPtr OnLowLevelMouse(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && wParam.ToInt32() == Native.WM_MOUSEMOVE && gesture.IsInGrace)
            {
                gesture.Cancel();
                hasGraceCursor = false;
            }
            return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
        }

        private static void ShowError(string message)
        {
            MessageBox.Show(message, "ThreeFingerDrag", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        protected override void ExitThreadCore()
        {
            SaveSettings();
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            if (mouseHook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(mouseHook);
                mouseHook = IntPtr.Zero;
            }
            if (timer != null) timer.Stop();
            if (window != null) window.Dispose();
            if (touchpad != null) touchpad.Dispose();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            base.ExitThreadCore();
        }
    }

    internal sealed class TestOutput : IDragOutput
    {
        internal int Downs, Ups, Moves;
        public void ButtonDown() { Downs++; }
        public void Move(double x, double y) { Moves++; }
        public void ButtonUp() { Ups++; }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-test")
            {
                Environment.ExitCode = RunSelfTests();
                return;
            }

            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\ThreeFingerDrag-7EAF87B4-7DBD-4D75-B37F-6A8BCE58E001", out created))
            {
                if (!created)
                {
                    MessageBox.Show("ThreeFingerDrag 已经在运行，请在任务栏通知区域查找托盘图标。",
                        "ThreeFingerDrag", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new DragApplication());
            }
        }

        private static int RunSelfTests()
        {
            try
            {
                TestOutput output = new TestOutput();
                GestureEngine engine = new GestureEngine(output);
                engine.Update(Contacts(0.2), 0);
                engine.Update(Contacts(0.201), 10);
                Assert(output.Downs == 0, "阈值内不应开始拖拽");
                engine.Update(Contacts(0.21), 20);
                Assert(output.Downs == 1 && output.Moves == 1, "越过阈值应按下并移动");
                engine.Update(new ContactPoint[0], 30);
                Assert(output.Ups == 0 && engine.IsDragging, "宽限期内应保持按下");
                Assert(engine.IsInGrace, "抬指后应进入宽限状态");
                engine.Update(Contacts(0.4), 100);
                engine.Update(Contacts(0.41), 110);
                Assert(output.Downs == 1 && output.Moves == 2, "重新落指不应重复按下或跳变");
                engine.Update(new ContactPoint[0], 120);
                engine.Tick(500);
                Assert(output.Ups == 1 && !engine.IsDragging, "超时必须松开");
                engine.Update(Contacts(0.2), 600);
                engine.Update(Contacts(0.21), 610);
                engine.Update(new[] { new ContactPoint(1, 0, 0), new ContactPoint(2, 0, 0),
                    new ContactPoint(3, 0, 0), new ContactPoint(4, 0, 0) }, 620);
                Assert(output.Ups == 2, "四指接触必须立即取消拖拽");
                Assert(Marshal.SizeOf(typeof(Native.HidCaps)) == 64, "HIDP_CAPS 布局错误");
                Assert(Marshal.SizeOf(typeof(Native.HidValueCaps)) == 72, "HIDP_VALUE_CAPS 布局错误");
                Assert(Marshal.SizeOf(typeof(Native.RawInputHeader)) == (IntPtr.Size == 8 ? 24 : 16), "RAWINPUTHEADER 布局错误");
                Assert(Marshal.SizeOf(typeof(Native.Input)) == (IntPtr.Size == 8 ? 40 : 28), "INPUT 布局错误");
                Console.WriteLine("Self-test passed.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Self-test failed: " + ex.Message);
                return 1;
            }
        }

        private static IList<ContactPoint> Contacts(double x)
        {
            return new[] { new ContactPoint(1, x, 0.2), new ContactPoint(2, x + 0.1, 0.2), new ContactPoint(3, x + 0.2, 0.2) };
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
