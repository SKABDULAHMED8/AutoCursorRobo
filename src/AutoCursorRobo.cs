// AutoCursorRobo - Auto Cursor Mover for Windows 10 / 11
// Keeps the PC awake by moving the mouse in natural random patterns (and clicking on
// empty desktop space) while you are away. Any real keyboard / mouse action stops it.
// Written in C# 5 so it compiles with the csc.exe that ships with Windows (.NET Framework 4).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Auto Cursor Robo")]
[assembly: AssemblyProduct("Auto Cursor Robo")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace AutoCursorRobo
{
    // ------------------------------------------------------------------ native
    internal static class Native
    {
        public const int WH_KEYBOARD_LL = 13;
        public const int WH_MOUSE_LL = 14;
        public const int LLKHF_INJECTED = 0x10;
        public const int LLMHF_INJECTED = 0x01;

        public const uint ES_CONTINUOUS = 0x80000000;
        public const uint ES_SYSTEM_REQUIRED = 0x00000001;
        public const uint ES_DISPLAY_REQUIRED = 0x00000002;

        public const uint INPUT_MOUSE = 0;
        public const uint MOUSEEVENTF_MOVE = 0x0001;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
        public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        public const int SM_XVIRTUALSCREEN = 76;
        public const int SM_YVIRTUALSCREEN = 77;
        public const int SM_CXVIRTUALSCREEN = 78;
        public const int SM_CYVIRTUALSCREEN = 79;

        public const int GA_ROOT = 2;

        public const int WS_EX_TOPMOST = 0x8;
        public const int WS_EX_TRANSPARENT = 0x20;
        public const int WS_EX_TOOLWINDOW = 0x80;
        public const int WS_EX_LAYERED = 0x80000;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public const int ULW_ALPHA = 2;
        public const byte AC_SRC_OVER = 0;
        public const byte AC_SRC_ALPHA = 1;

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx; public int cy; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // INPUT = type + MOUSEINPUT. Sequential layout pads correctly on both x86 (28 bytes) and x64 (40 bytes).
        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("kernel32.dll")]
        public static extern uint SetThreadExecutionState(uint esFlags);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int nIndex);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT pt);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, int flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst,
            ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        public static string ClassOf(IntPtr hwnd)
        {
            StringBuilder sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
    }

    // ------------------------------------------------------------------ overlay
    // Small always-on-top, click-through, semi "sticker" window that shows the robot
    // while the cursor is being moved automatically.
    internal sealed class RoboOverlay : Form
    {
        private Bitmap bmp;
        private int size;

        public RoboOverlay(Bitmap source, int px)
        {
            size = px;
            bmp = new Bitmap(px, px, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                g.DrawImage(source, new Rectangle(0, 0, px, px));
            }
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Size = new Size(px, px);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT |
                              Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        public Rectangle ScreenRect { get { return new Rectangle(Left, Top, size, size); } }

        public void PlaceAndShow(float opacity)
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int x = wa.Right - size - 14;
            int y = wa.Bottom - size - 14;
            if (!IsHandleCreated) { CreateHandle(); }
            Location = new Point(x, y);
            Apply(x, y, opacity);
            if (!Visible) { Visible = true; }
            Apply(x, y, opacity);
        }

        public void Apply(int x, int y, float opacity)
        {
            if (!IsHandleCreated) { return; }
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBmp = IntPtr.Zero;
            IntPtr old = IntPtr.Zero;
            try
            {
                hBmp = bmp.GetHbitmap(Color.FromArgb(0));
                old = Native.SelectObject(memDc, hBmp);
                Native.SIZE sz; sz.cx = size; sz.cy = size;
                Native.POINT src; src.X = 0; src.Y = 0;
                Native.POINT dst; dst.X = x; dst.Y = y;
                Native.BLENDFUNCTION bf = new Native.BLENDFUNCTION();
                bf.BlendOp = Native.AC_SRC_OVER;
                bf.BlendFlags = 0;
                bf.SourceConstantAlpha = (byte)Math.Max(0, Math.Min(255, (int)(opacity * 255f)));
                bf.AlphaFormat = Native.AC_SRC_ALPHA;
                Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref sz, memDc, ref src, 0, ref bf, Native.ULW_ALPHA);
            }
            finally
            {
                if (hBmp != IntPtr.Zero)
                {
                    Native.SelectObject(memDc, old);
                    Native.DeleteObject(hBmp);
                }
                Native.DeleteDC(memDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && bmp != null) { bmp.Dispose(); bmp = null; }
            base.Dispose(disposing);
        }
    }

    // ------------------------------------------------------------------ main app
    internal enum State { Off, Armed, Moving }

    internal sealed class TrayApp : ApplicationContext
    {
        private const string AppName = "Auto Cursor Robo";
        private const string RegKey = @"Software\AutoCursorRobo";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private readonly NotifyIcon tray;
        private readonly Icon iconOn;
        private readonly Icon iconOff;
        private readonly RoboOverlay overlay;
        private readonly System.Windows.Forms.Timer tick;
        private readonly System.Windows.Forms.Timer pulse;
        private readonly Random rng = new Random();
        private readonly Control ui;          // hidden control used to marshal to the UI thread

        private ToolStripMenuItem miToggle;
        private ToolStripMenuItem miClicks;
        private ToolStripMenuItem miStartup;
        private readonly List<ToolStripMenuItem> idleItems = new List<ToolStripMenuItem>();

        private State state = State.Off;
        private int idleSeconds = 10;
        private bool clickEmpty = true;

        // low level hooks
        private readonly Native.HookProc kbProc;
        private readonly Native.HookProc msProc;
        private IntPtr kbHook = IntPtr.Zero;
        private IntPtr msHook = IntPtr.Zero;
        private long lastRealInput = Environment.TickCount;
        private long armedAt = 0;
        private const int StartGraceMs = 3000;   // ignore input right after the user turned it ON

        // movement engine
        private List<Native.POINT> path = new List<Native.POINT>();
        private int pathIndex = 0;
        private int stepsPerTick = 3;
        private long pauseUntil = 0;
        private Native.POINT lastSet;
        private bool haveLastSet = false;
        private bool pendingClick = false;
        private int clickPhase = 0;
        private long phaseUntil = 0;
        private float pulsePhase = 0f;

        public TrayApp(bool startArmed)
        {
            ui = new Control();
            ui.CreateControl();
            IntPtr h = ui.Handle; // force handle creation

            LoadSettings();

            iconOn = LoadIcon("AutoCursorRobo.ico");
            iconOff = LoadIcon("AutoCursorRobo_off.ico");

            using (Bitmap roboBmp = LoadRoboBitmap())
            {
                overlay = new RoboOverlay(roboBmp, 44);
            }

            kbProc = KeyboardHook;
            msProc = MouseHook;
            InstallHooks();

            tray = new NotifyIcon();
            tray.Icon = iconOff;
            tray.Text = AppName + " - OFF (click to turn ON)";
            tray.Visible = true;
            tray.MouseClick += OnTrayClick;
            tray.ContextMenuStrip = BuildMenu();

            tick = new System.Windows.Forms.Timer();
            tick.Interval = 15;
            tick.Tick += OnTick;
            tick.Start();

            pulse = new System.Windows.Forms.Timer();
            pulse.Interval = 60;
            pulse.Tick += OnPulse;

            StartToggleListener();

            if (startArmed) { SetState(State.Armed, true); }
            else
            {
                tray.ShowBalloonTip(2500, AppName, "Running in the tray. Click the robot icon to turn ON.", ToolTipIcon.Info);
            }
        }

        // -------------------------------------------------------------- resources
        private static Stream OpenResource(string name)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            Stream s = asm.GetManifestResourceStream(name);
            if (s != null) { return s; }
            string p = Path.Combine(Path.GetDirectoryName(asm.Location), name);
            if (File.Exists(p)) { return File.OpenRead(p); }
            return null;
        }

        private static Icon LoadIcon(string name)
        {
            try
            {
                using (Stream s = OpenResource(name))
                {
                    if (s != null) { return new Icon(s, new Size(32, 32)); }
                }
            }
            catch { }
            return SystemIcons.Application;
        }

        private static Bitmap LoadRoboBitmap()
        {
            try
            {
                using (Stream s = OpenResource("robo.png"))
                {
                    if (s != null)
                    {
                        using (Bitmap raw = new Bitmap(s))
                        {
                            return new Bitmap(raw);
                        }
                    }
                }
            }
            catch { }
            Bitmap fallback = new Bitmap(64, 64);
            using (Graphics g = Graphics.FromImage(fallback))
            {
                g.Clear(Color.Transparent);
                g.FillEllipse(Brushes.OrangeRed, 8, 8, 48, 48);
            }
            return fallback;
        }

        // -------------------------------------------------------------- settings
        private void LoadSettings()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RegKey))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("IdleSeconds");
                        if (v != null) { idleSeconds = Convert.ToInt32(v); }
                        v = k.GetValue("ClickEmpty");
                        if (v != null) { clickEmpty = Convert.ToInt32(v) != 0; }
                    }
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RegKey))
                {
                    k.SetValue("IdleSeconds", idleSeconds, RegistryValueKind.DWord);
                    k.SetValue("ClickEmpty", clickEmpty ? 1 : 0, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        private static bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    return k != null && k.GetValue("AutoCursorRobo") != null;
                }
            }
            catch { return false; }
        }

        private static void SetStartup(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) { return; }
                    if (on) { k.SetValue("AutoCursorRobo", "\"" + Application.ExecutablePath + "\""); }
                    else { k.DeleteValue("AutoCursorRobo", false); }
                }
            }
            catch { }
        }

        // -------------------------------------------------------------- menu
        private ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();

            miToggle = new ToolStripMenuItem("Turn ON");
            miToggle.Click += delegate { Toggle(); };
            m.Items.Add(miToggle);
            m.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem idle = new ToolStripMenuItem("Start moving after I am idle for");
            int[] opts = new int[] { 5, 10, 30, 60, 120, 300 };
            string[] names = new string[] { "5 seconds", "10 seconds", "30 seconds", "1 minute", "2 minutes", "5 minutes" };
            for (int i = 0; i < opts.Length; i++)
            {
                int secs = opts[i];
                ToolStripMenuItem it = new ToolStripMenuItem(names[i]);
                it.Tag = secs;
                it.Checked = (secs == idleSeconds);
                it.Click += delegate
                {
                    idleSeconds = secs;
                    foreach (ToolStripMenuItem o in idleItems) { o.Checked = ((int)o.Tag == idleSeconds); }
                    SaveSettings();
                };
                idleItems.Add(it);
                idle.DropDownItems.Add(it);
            }
            m.Items.Add(idle);

            miClicks = new ToolStripMenuItem("Click on empty desktop space");
            miClicks.CheckOnClick = true;
            miClicks.Checked = clickEmpty;
            miClicks.Click += delegate { clickEmpty = miClicks.Checked; SaveSettings(); };
            m.Items.Add(miClicks);

            miStartup = new ToolStripMenuItem("Start with Windows (stays OFF until clicked)");
            miStartup.CheckOnClick = true;
            miStartup.Checked = IsStartupEnabled();
            miStartup.Click += delegate { SetStartup(miStartup.Checked); };
            m.Items.Add(miStartup);

            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem exit = new ToolStripMenuItem("Exit");
            exit.Click += delegate { ExitApp(); };
            m.Items.Add(exit);
            return m;
        }

        private void OnTrayClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { Toggle(); }
        }

        // -------------------------------------------------------------- single instance toggle
        private void StartToggleListener()
        {
            Thread t = new Thread(delegate()
            {
                try
                {
                    while (true)
                    {
                        Program.ToggleEvent.WaitOne();
                        if (ui.IsHandleCreated && !ui.IsDisposed)
                        {
                            ui.BeginInvoke((MethodInvoker)delegate { Toggle(); });
                        }
                    }
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        // -------------------------------------------------------------- state
        private void Toggle()
        {
            if (state == State.Off) { SetState(State.Armed, true); }
            else { SetState(State.Off, true); }
        }

        private void SetState(State s, bool notify)
        {
            State prev = state;
            state = s;
            if (s == State.Off)
            {
                StopMoving();
                tray.Icon = iconOff;
                tray.Text = AppName + " - OFF (click to turn ON)";
                miToggle.Text = "Turn ON";
                if (notify && prev != State.Off)
                {
                    tray.ShowBalloonTip(1500, AppName, "OFF - welcome back! Click the robot to turn it ON again.", ToolTipIcon.Info);
                }
            }
            else if (s == State.Armed)
            {
                StopMoving();
                armedAt = Environment.TickCount;
                lastRealInput = Environment.TickCount;
                tray.Icon = iconOff;
                tray.Text = AppName + " - ON, waiting for idle (" + IdleLabel() + ")";
                miToggle.Text = "Turn OFF";
                if (notify)
                {
                    tray.ShowBalloonTip(2500, AppName,
                        "ON - the robot starts moving the cursor after " + IdleLabel() +
                        " without your keyboard/mouse. Touch anything to stop it.", ToolTipIcon.Info);
                }
            }
            else
            {
                tray.Icon = iconOn;
                tray.Text = AppName + " - MOVING (touch mouse/key to stop)";
                BeginMoving();
            }
        }

        private string IdleLabel()
        {
            if (idleSeconds < 60) { return idleSeconds + " s idle"; }
            return (idleSeconds / 60) + " min idle";
        }

        private void BeginMoving()
        {
            Native.SetThreadExecutionState(Native.ES_CONTINUOUS | Native.ES_SYSTEM_REQUIRED | Native.ES_DISPLAY_REQUIRED);
            overlay.PlaceAndShow(1f);
            pulse.Start();
            path.Clear();
            pathIndex = 0;
            pauseUntil = 0;
            pendingClick = false;
            clickPhase = 0;
            haveLastSet = false;
            Native.POINT p;
            if (Native.GetCursorPos(out p)) { lastSet = p; haveLastSet = true; }
        }

        private void StopMoving()
        {
            pulse.Stop();
            Native.SetThreadExecutionState(Native.ES_CONTINUOUS);
            if (overlay != null && overlay.Visible) { overlay.Visible = false; }
            path.Clear();
            pendingClick = false;
            haveLastSet = false;
        }

        // user touched something real
        private void UserInput()
        {
            lastRealInput = Environment.TickCount;
            if (state == State.Moving)
            {
                if (Environment.TickCount - phaseStart() < StartGraceMs) { return; }
                ui.BeginInvoke((MethodInvoker)delegate { if (state == State.Moving) { SetState(State.Off, true); } });
            }
        }

        private long movingSince = 0;
        private long phaseStart() { return movingSince; }

        // -------------------------------------------------------------- hooks
        private void InstallHooks()
        {
            IntPtr mod = Native.GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName);
            kbHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, kbProc, mod, 0);
            msHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, msProc, mod, 0);
        }

        private IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                Native.KBDLLHOOKSTRUCT k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                if ((k.flags & Native.LLKHF_INJECTED) == 0) { UserInput(); }
            }
            return Native.CallNextHookEx(kbHook, nCode, wParam, lParam);
        }

        private IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                Native.MSLLHOOKSTRUCT m = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                if ((m.flags & Native.LLMHF_INJECTED) == 0) { UserInput(); }
            }
            return Native.CallNextHookEx(msHook, nCode, wParam, lParam);
        }

        // -------------------------------------------------------------- tick
        private void OnPulse(object sender, EventArgs e)
        {
            // gentle breathing so you can see it is alive
            pulsePhase += 0.18f;
            float o = 0.78f + 0.22f * (float)Math.Sin(pulsePhase);
            Rectangle r = overlay.ScreenRect;
            overlay.Apply(r.X, r.Y, o);
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (state == State.Armed)
            {
                long idleMs = Environment.TickCount - lastRealInput;
                bool hooksOk = (kbHook != IntPtr.Zero && msHook != IntPtr.Zero);
                if (!hooksOk) { idleMs = Math.Min(idleMs, SystemIdleMs()); }
                if (idleMs >= (long)idleSeconds * 1000L)
                {
                    movingSince = Environment.TickCount;
                    SetState(State.Moving, false);
                }
                return;
            }
            if (state != State.Moving) { return; }

            // safety net 1: someone is holding a key / mouse button
            if (Environment.TickCount - movingSince > StartGraceMs && AnyKeyDown())
            {
                SetState(State.Off, true);
                return;
            }

            // safety net 2: cursor is not where we left it => a human grabbed the mouse
            Native.POINT cur;
            if (Native.GetCursorPos(out cur) && haveLastSet &&
                Environment.TickCount - movingSince > StartGraceMs)
            {
                int dx = cur.X - lastSet.X, dy = cur.Y - lastSet.Y;
                if (dx * dx + dy * dy > 40 * 40)
                {
                    SetState(State.Off, true);
                    return;
                }
            }

            RunMover();
        }

        private static bool AnyKeyDown()
        {
            // 0x01 left, 0x02 right, 0x04 middle mouse buttons; keyboard 0x08..0xFE
            // We skip our own synthetic left click (handled by clickPhase) by checking only keyboard + right/middle.
            if ((Native.GetAsyncKeyState(0x02) & 0x8000) != 0) { return true; }
            if ((Native.GetAsyncKeyState(0x04) & 0x8000) != 0) { return true; }
            for (int vk = 0x08; vk < 0xFF; vk++)
            {
                if (vk == 0x10 || vk == 0x11 || vk == 0x12) { continue; } // generic shift/ctrl/alt flicker
                if ((Native.GetAsyncKeyState(vk) & 0x8000) != 0) { return true; }
            }
            return false;
        }

        private static long SystemIdleMs()
        {
            Native.LASTINPUTINFO li = new Native.LASTINPUTINFO();
            li.cbSize = (uint)Marshal.SizeOf(typeof(Native.LASTINPUTINFO));
            if (!Native.GetLastInputInfo(ref li)) { return 0x7FFFFFFF; }
            return (uint)Environment.TickCount - li.dwTime;
        }

        // -------------------------------------------------------------- mover
        private void RunMover()
        {
            long now = Environment.TickCount;

            if (pendingClick)
            {
                DoClickSequence(now);
                return;
            }
            if (now < pauseUntil) { return; }

            if (pathIndex >= path.Count)
            {
                PlanNext();
                return;
            }

            int n = Math.Min(stepsPerTick, path.Count - pathIndex);
            Native.POINT p = path[pathIndex + n - 1];
            pathIndex += n;
            MoveTo(p.X, p.Y);

            if (pathIndex >= path.Count)
            {
                if (clickAfterPath)
                {
                    clickAfterPath = false;
                    pendingClick = true;
                    clickPhase = 0;
                    phaseUntil = now + 120;
                }
                else
                {
                    pauseUntil = now + rng.Next(250, 2200);
                }
            }
        }

        private bool clickAfterPath = false;

        private void DoClickSequence(long now)
        {
            if (now < phaseUntil) { return; }
            if (clickPhase == 0)
            {
                SendMouse(Native.MOUSEEVENTF_LEFTDOWN, 0, 0, false);
                clickPhase = 1;
                phaseUntil = now + rng.Next(50, 110);
            }
            else
            {
                SendMouse(Native.MOUSEEVENTF_LEFTUP, 0, 0, false);
                pendingClick = false;
                clickPhase = 0;
                pauseUntil = now + rng.Next(600, 2500);
            }
        }

        private Rectangle VirtualScreen()
        {
            return new Rectangle(
                Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN),
                Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN),
                Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN),
                Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN));
        }

        private Rectangle RandomWorkArea()
        {
            Screen[] all = Screen.AllScreens;
            Rectangle wa = all[rng.Next(all.Length)].WorkingArea;
            wa.Inflate(-60, -60); // stay away from edges / hot corners / taskbar
            if (wa.Width < 100 || wa.Height < 100) { wa = Screen.PrimaryScreen.WorkingArea; }
            return wa;
        }

        private Native.POINT Here()
        {
            Native.POINT p;
            if (!Native.GetCursorPos(out p)) { p = lastSet; }
            return p;
        }

        private void PlanNext()
        {
            Native.POINT from = haveLastSet ? lastSet : Here();
            path = new List<Native.POINT>();
            pathIndex = 0;
            stepsPerTick = rng.Next(2, 5);

            // 1) try to find an empty bit of desktop to click on
            if (clickEmpty && rng.Next(100) < 55)
            {
                Native.POINT target;
                if (FindEmptyDesktopSpot(out target))
                {
                    AddBezier(from, target);
                    clickAfterPath = true;
                    return;
                }
            }

            // 2) otherwise a random natural pattern
            Rectangle wa = RandomWorkArea();
            int kind = rng.Next(7);
            switch (kind)
            {
                case 0: AddBezier(from, RandPoint(wa)); break;
                case 1: AddBezier(from, RandPoint(wa)); AddBezier(LastPoint(), RandPoint(wa)); break;
                case 2: AddCircle(wa); break;
                case 3: AddFigureEight(wa); break;
                case 4: AddZigZag(wa); break;
                case 5: AddSpiral(wa); break;
                default: AddWave(wa); break;
            }
            clickAfterPath = false;
        }

        private Native.POINT RandPoint(Rectangle r)
        {
            Native.POINT p;
            p.X = rng.Next(r.Left, r.Right);
            p.Y = rng.Next(r.Top, r.Bottom);
            return p;
        }

        private Native.POINT LastPoint()
        {
            if (path.Count > 0) { return path[path.Count - 1]; }
            return haveLastSet ? lastSet : Here();
        }

        private Native.POINT Clamp(double x, double y)
        {
            Rectangle v = VirtualScreen();
            Native.POINT p;
            p.X = (int)Math.Max(v.Left + 8, Math.Min(v.Right - 9, x));
            p.Y = (int)Math.Max(v.Top + 8, Math.Min(v.Bottom - 9, y));
            return p;
        }

        private static double Ease(double t) { return t * t * (3 - 2 * t); }

        private void AddBezier(Native.POINT a, Native.POINT b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            int steps = (int)Math.Max(25, Math.Min(260, dist / 4.0));
            double bend = Math.Max(30, dist * 0.35);
            double c1x = a.X + dx * 0.3 + (rng.NextDouble() - 0.5) * bend;
            double c1y = a.Y + dy * 0.3 + (rng.NextDouble() - 0.5) * bend;
            double c2x = a.X + dx * 0.7 + (rng.NextDouble() - 0.5) * bend;
            double c2y = a.Y + dy * 0.7 + (rng.NextDouble() - 0.5) * bend;
            for (int i = 1; i <= steps; i++)
            {
                double t = Ease((double)i / steps);
                double u = 1 - t;
                double x = u * u * u * a.X + 3 * u * u * t * c1x + 3 * u * t * t * c2x + t * t * t * b.X;
                double y = u * u * u * a.Y + 3 * u * u * t * c1y + 3 * u * t * t * c2y + t * t * t * b.Y;
                path.Add(Clamp(x, y));
            }
        }

        private void AddCircle(Rectangle wa)
        {
            Native.POINT c = RandPoint(wa);
            double r = rng.Next(40, 140);
            double a0 = rng.NextDouble() * Math.PI * 2;
            double dir = rng.Next(2) == 0 ? 1 : -1;
            Native.POINT start = Clamp(c.X + Math.Cos(a0) * r, c.Y + Math.Sin(a0) * r);
            AddBezier(haveLastSet ? lastSet : Here(), start);
            int steps = 140;
            for (int i = 1; i <= steps; i++)
            {
                double a = a0 + dir * (Math.PI * 2 * i / steps);
                path.Add(Clamp(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r));
            }
        }

        private void AddFigureEight(Rectangle wa)
        {
            Native.POINT c = RandPoint(wa);
            double r = rng.Next(60, 170);
            Native.POINT start = Clamp(c.X + r, c.Y);
            AddBezier(haveLastSet ? lastSet : Here(), start);
            int steps = 220;
            for (int i = 1; i <= steps; i++)
            {
                double t = Math.PI * 2 * i / steps;
                double x = c.X + r * Math.Cos(t);
                double y = c.Y + r * Math.Sin(t) * Math.Cos(t);
                path.Add(Clamp(x, y));
            }
        }

        private void AddZigZag(Rectangle wa)
        {
            Native.POINT cur = haveLastSet ? lastSet : Here();
            int zigs = rng.Next(3, 7);
            Native.POINT p = cur;
            for (int i = 0; i < zigs; i++)
            {
                Native.POINT q = RandPoint(wa);
                int steps = rng.Next(14, 40);
                for (int s = 1; s <= steps; s++)
                {
                    double t = Ease((double)s / steps);
                    path.Add(Clamp(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t));
                }
                p = q;
            }
        }

        private void AddSpiral(Rectangle wa)
        {
            Native.POINT c = RandPoint(wa);
            double rMax = rng.Next(60, 150);
            double a0 = rng.NextDouble() * Math.PI * 2;
            AddBezier(haveLastSet ? lastSet : Here(), Clamp(c.X + Math.Cos(a0) * 8, c.Y + Math.Sin(a0) * 8));
            int steps = 260;
            bool outward = rng.Next(2) == 0;
            for (int i = 1; i <= steps; i++)
            {
                double f = (double)i / steps;
                double r = rMax * (outward ? f : 1 - f) + 8;
                double a = a0 + f * Math.PI * 6;
                path.Add(Clamp(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r));
            }
        }

        private void AddWave(Rectangle wa)
        {
            Native.POINT a = RandPoint(wa);
            Native.POINT b = RandPoint(wa);
            AddBezier(haveLastSet ? lastSet : Here(), a);
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) { return; }
            double nx = -dy / len, ny = dx / len;
            double amp = rng.Next(20, 80);
            double waves = rng.Next(2, 5);
            int steps = (int)Math.Max(60, Math.Min(300, len / 3));
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                double off = Math.Sin(t * Math.PI * 2 * waves) * amp * Math.Sin(t * Math.PI);
                path.Add(Clamp(a.X + dx * t + nx * off, a.Y + dy * t + ny * off));
            }
        }

        // Pick a random point where the thing under the pointer is the bare desktop
        // (wallpaper area; Progman / WorkerW) so a click can't hit any program window.
        private bool FindEmptyDesktopSpot(out Native.POINT result)
        {
            result.X = 0; result.Y = 0;
            Rectangle ovl = overlay.ScreenRect;
            ovl.Inflate(30, 30);
            for (int i = 0; i < 40; i++)
            {
                Rectangle wa = RandomWorkArea();
                Native.POINT p = RandPoint(wa);
                if (ovl.Contains(p.X, p.Y)) { continue; }
                IntPtr hwnd = Native.WindowFromPoint(p);
                if (hwnd == IntPtr.Zero) { continue; }
                IntPtr root = Native.GetAncestor(hwnd, Native.GA_ROOT);
                if (root == IntPtr.Zero) { root = hwnd; }
                string rootClass = Native.ClassOf(root);
                if (rootClass != "Progman" && rootClass != "WorkerW") { continue; }
                // make sure we are on the background and not on a desktop-icon list item row:
                // the icon list view window is "SysListView32"; the bare wallpaper is "SHELLDLL_DefView"/"WorkerW"/"Progman".
                string cls = Native.ClassOf(hwnd);
                if (cls == "SysListView32" || cls == "SHELLDLL_DefView" || cls == "Progman" || cls == "WorkerW")
                {
                    result = p;
                    return true;
                }
            }
            return false;
        }

        // -------------------------------------------------------------- input out
        private void MoveTo(int x, int y)
        {
            Rectangle v = VirtualScreen();
            if (v.Width < 2 || v.Height < 2) { return; }
            int nx = (int)Math.Round((x - v.Left) * 65535.0 / (v.Width - 1));
            int ny = (int)Math.Round((y - v.Top) * 65535.0 / (v.Height - 1));
            SendMouse(Native.MOUSEEVENTF_MOVE | Native.MOUSEEVENTF_ABSOLUTE | Native.MOUSEEVENTF_VIRTUALDESK, nx, ny, true);
            lastSet.X = x; lastSet.Y = y;
            haveLastSet = true;
        }

        private static void SendMouse(uint flags, int dx, int dy, bool absolute)
        {
            Native.INPUT[] inp = new Native.INPUT[1];
            inp[0].type = Native.INPUT_MOUSE;
            inp[0].mi.dx = dx;
            inp[0].mi.dy = dy;
            inp[0].mi.mouseData = 0;
            inp[0].mi.dwFlags = flags;
            inp[0].mi.time = 0;
            inp[0].mi.dwExtraInfo = IntPtr.Zero;
            Native.SendInput(1, inp, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        // -------------------------------------------------------------- exit
        private void ExitApp()
        {
            try
            {
                tick.Stop();
                pulse.Stop();
                StopMoving();
                if (kbHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(kbHook); kbHook = IntPtr.Zero; }
                if (msHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(msHook); msHook = IntPtr.Zero; }
                tray.Visible = false;
                tray.Dispose();
                overlay.Dispose();
            }
            catch { }
            ExitThread();
        }
    }

    internal static class Program
    {
        public static EventWaitHandle ToggleEvent;

        [STAThread]
        private static void Main(string[] args)
        {
            try { Native.SetProcessDPIAware(); } catch { }

            bool created;
            Mutex mutex = new Mutex(true, "AutoCursorRobo_SingleInstance", out created);
            ToggleEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "AutoCursorRobo_Toggle");

            bool startArmed = false;
            foreach (string a in args)
            {
                if (a.Equals("/on", StringComparison.OrdinalIgnoreCase) || a.Equals("-on", StringComparison.OrdinalIgnoreCase))
                {
                    startArmed = true;
                }
            }

            if (!created)
            {
                // already running: a second launch (e.g. the desktop shortcut) just toggles it
                ToggleEvent.Set();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApp(startArmed));
            GC.KeepAlive(mutex);
        }
    }
}
