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

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string command = args.Length == 0 ? "" : args[0].ToLowerInvariant();

        if (command == "--diagnose")
        {
            return Diagnostics.Run();
        }

        if (command == "--restore")
        {
            return OneShot.Apply(false);
        }

        if (command == "--apply-game")
        {
            return OneShot.Apply(true);
        }

        bool createdNew;
        using (Mutex mutex = new Mutex(true, @"Local\ValorantColorSwitcher-8E2C45E7", out createdNew))
        {
            if (!createdNew)
            {
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                Log.Write("UI exception: " + e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Log.Write("Unhandled exception: " + e.ExceptionObject);
            };

            using (SwitcherContext context = new SwitcherContext())
            {
                Application.Run(context);
            }
        }

        return 0;
    }
}

internal static class AppPaths
{
    public static readonly string BaseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    public static readonly string SettingsFile = Path.Combine(BaseDirectory, "settings.ini");
    public static readonly string LocalDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ValorantColorSwitcher");
    public static readonly string LogFile = Path.Combine(LocalDataDirectory, "switcher.log");
    public static readonly string DiagnosticFile = Path.Combine(BaseDirectory, "diagnostic.txt");
}

internal static class Log
{
    private static readonly object Gate = new object();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.LocalDataDirectory);
                File.AppendAllText(
                    AppPaths.LogFile,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message + Environment.NewLine,
                    Encoding.UTF8);
            }
        }
        catch
        {
        }
    }
}

internal sealed class SwitcherSettings
{
    public int PollMilliseconds = 1000;
    public int ReapplySeconds = 10;
    public bool ApplyToAllNvidiaDisplays = true;
    public bool RiotClientRequiresVisibleWindow = true;
    public bool ShowNotifications = false;
    public string[] GameProcesses = new string[] { "VALORANT-Win64-Shipping", "VALORANT" };
    public string[] RiotClientProcesses = new string[] { "RiotClientUx" };

    public ColorProfile Game = new ColorProfile(65, 65, 1.10, 60, 0);
    public ColorProfile Desktop = new ColorProfile(50, 50, 1.00, 50, 0);

    public static SwitcherSettings Load()
    {
        SwitcherSettings result = new SwitcherSettings();
        if (!File.Exists(AppPaths.SettingsFile))
        {
            return result;
        }

        foreach (string originalLine in File.ReadAllLines(AppPaths.SettingsFile, Encoding.UTF8))
        {
            string line = originalLine.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
            {
                continue;
            }

            int equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            string key = line.Substring(0, equals).Trim().ToLowerInvariant();
            string value = line.Substring(equals + 1).Trim();
            int intValue;
            double doubleValue;
            bool boolValue;

            if (key == "pollmilliseconds" && int.TryParse(value, out intValue))
                result.PollMilliseconds = Clamp(intValue, 250, 10000);
            else if (key == "reapplyseconds" && int.TryParse(value, out intValue))
                result.ReapplySeconds = Clamp(intValue, 2, 300);
            else if (key == "applytoallnvidiadisplays" && bool.TryParse(value, out boolValue))
                result.ApplyToAllNvidiaDisplays = boolValue;
            else if (key == "riotclientrequiresvisiblewindow" && bool.TryParse(value, out boolValue))
                result.RiotClientRequiresVisibleWindow = boolValue;
            else if (key == "shownotifications" && bool.TryParse(value, out boolValue))
                result.ShowNotifications = boolValue;
            else if (key == "gameprocesses")
                result.GameProcesses = ParseProcessList(value);
            else if (key == "riotclientprocesses")
                result.RiotClientProcesses = ParseProcessList(value);
            else if (key == "gamebrightness" && int.TryParse(value, out intValue))
                result.Game.Brightness = Clamp(intValue, 0, 100);
            else if (key == "gamecontrast" && int.TryParse(value, out intValue))
                result.Game.Contrast = Clamp(intValue, 0, 100);
            else if (key == "gamegamma" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValue))
                result.Game.Gamma = Clamp(doubleValue, 0.40, 2.80);
            else if (key == "gamevibrance" && int.TryParse(value, out intValue))
                result.Game.Vibrance = Clamp(intValue, 0, 100);
            else if (key == "gamehue" && int.TryParse(value, out intValue))
                result.Game.Hue = NormalizeHue(intValue);
            else if (key == "desktopbrightness" && int.TryParse(value, out intValue))
                result.Desktop.Brightness = Clamp(intValue, 0, 100);
            else if (key == "desktopcontrast" && int.TryParse(value, out intValue))
                result.Desktop.Contrast = Clamp(intValue, 0, 100);
            else if (key == "desktopgamma" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValue))
                result.Desktop.Gamma = Clamp(doubleValue, 0.40, 2.80);
            else if (key == "desktopvibrance" && int.TryParse(value, out intValue))
                result.Desktop.Vibrance = Clamp(intValue, 0, 100);
            else if (key == "desktophue" && int.TryParse(value, out intValue))
                result.Desktop.Hue = NormalizeHue(intValue);
        }

        return result;
    }

    private static string[] ParseProcessList(string value)
    {
        List<string> names = new List<string>();
        foreach (string item in value.Split(new char[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string name = Path.GetFileNameWithoutExtension(item.Trim());
            if (name.Length > 0)
            {
                names.Add(name);
            }
        }
        return names.ToArray();
    }

    private static int NormalizeHue(int value)
    {
        value %= 360;
        return value < 0 ? value + 360 : value;
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }
}

internal sealed class ColorProfile
{
    public int Brightness;
    public int Contrast;
    public double Gamma;
    public int Vibrance;
    public int Hue;

    public ColorProfile(int brightness, int contrast, double gamma, int vibrance, int hue)
    {
        Brightness = brightness;
        Contrast = contrast;
        Gamma = gamma;
        Vibrance = vibrance;
        Hue = hue;
    }

    public override string ToString()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "Brightness={0} Contrast={1} Gamma={2:0.00} Vibrance={3} Hue={4}",
            Brightness, Contrast, Gamma, Vibrance, Hue);
    }
}

internal enum ActiveMode
{
    Unknown,
    Desktop,
    Game,
    ManualDesktop,
    ManualGame
}

internal sealed class SwitcherContext : ApplicationContext
{
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer timer;
    private readonly Icon appIcon;
    private NvApiColorController controller;
    private SwitcherSettings settings;
    private DateTime settingsStamp;
    private DateTime lastApply = DateTime.MinValue;
    private ActiveMode mode = ActiveMode.Unknown;
    private bool automatic = true;
    private ToolStripMenuItem statusItem;
    private ToolStripMenuItem automaticItem;
    private bool exiting;

    public SwitcherContext()
    {
        settings = LoadSettings();
        controller = new NvApiColorController();
        controller.Initialize();

        ContextMenuStrip menu = new ContextMenuStrip();
        statusItem = new ToolStripMenuItem("状态：正在启动");
        statusItem.Enabled = false;
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());

        automaticItem = new ToolStripMenuItem("自动切换");
        automaticItem.Checked = true;
        automaticItem.Click += delegate
        {
            automatic = true;
            automaticItem.Checked = true;
            mode = ActiveMode.Unknown;
            TickNow();
        };
        menu.Items.Add(automaticItem);

        ToolStripMenuItem gameItem = new ToolStripMenuItem("立即应用游戏颜色");
        gameItem.Click += delegate { ApplyManual(true); };
        menu.Items.Add(gameItem);

        ToolStripMenuItem desktopItem = new ToolStripMenuItem("立即恢复桌面颜色");
        desktopItem.Click += delegate { ApplyManual(false); };
        menu.Items.Add(desktopItem);
        menu.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem openSettings = new ToolStripMenuItem("打开 settings.ini");
        openSettings.Click += delegate { OpenFile(AppPaths.SettingsFile); };
        menu.Items.Add(openSettings);

        ToolStripMenuItem reloadSettings = new ToolStripMenuItem("重新加载配置");
        reloadSettings.Click += delegate
        {
            settings = LoadSettings();
            mode = ActiveMode.Unknown;
            timer.Interval = settings.PollMilliseconds;
            TickNow();
        };
        menu.Items.Add(reloadSettings);

        ToolStripMenuItem openLog = new ToolStripMenuItem("打开日志");
        openLog.Click += delegate { OpenFile(AppPaths.LogFile); };
        menu.Items.Add(openLog);
        menu.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem exitItem = new ToolStripMenuItem("退出并恢复桌面颜色");
        exitItem.Click += delegate { ExitThread(); };
        menu.Items.Add(exitItem);

        tray = new NotifyIcon();
        appIcon = LoadApplicationIcon();
        tray.Icon = appIcon ?? SystemIcons.Application;
        tray.Text = "VALORANT NVIDIA 颜色自动切换";
        tray.ContextMenuStrip = menu;
        tray.Visible = true;
        tray.DoubleClick += delegate
        {
            automatic = true;
            automaticItem.Checked = true;
            mode = ActiveMode.Unknown;
            TickNow();
        };

        timer = new System.Windows.Forms.Timer();
        timer.Interval = settings.PollMilliseconds;
        timer.Tick += delegate { TickNow(); };
        timer.Start();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        Log.Write("Switcher started. NVIDIA displays=" + controller.TargetCount);
        TickNow();
    }

    private SwitcherSettings LoadSettings()
    {
        try
        {
            settingsStamp = File.Exists(AppPaths.SettingsFile)
                ? File.GetLastWriteTimeUtc(AppPaths.SettingsFile)
                : DateTime.MinValue;
            return SwitcherSettings.Load();
        }
        catch (Exception ex)
        {
            Log.Write("Could not load settings.ini; defaults are used. " + ex.Message);
            return new SwitcherSettings();
        }
    }

    private void ReloadSettingsIfChanged()
    {
        try
        {
            DateTime currentStamp = File.Exists(AppPaths.SettingsFile)
                ? File.GetLastWriteTimeUtc(AppPaths.SettingsFile)
                : DateTime.MinValue;
            if (currentStamp != settingsStamp)
            {
                settings = LoadSettings();
                timer.Interval = settings.PollMilliseconds;
                mode = ActiveMode.Unknown;
                Log.Write("settings.ini reloaded.");
            }
        }
        catch (Exception ex)
        {
            Log.Write("Settings reload failed: " + ex.Message);
        }
    }

    private void TickNow()
    {
        if (exiting)
        {
            return;
        }

        ReloadSettingsIfChanged();
        if (!automatic)
        {
            return;
        }

        bool gameRunning = ProcessDetector.AnyRunning(settings.GameProcesses, false);
        bool riotRunning = ProcessDetector.AnyRunning(
            settings.RiotClientProcesses,
            settings.RiotClientRequiresVisibleWindow);
        ActiveMode desired = (gameRunning || riotRunning) ? ActiveMode.Game : ActiveMode.Desktop;
        bool periodicReapply = (DateTime.UtcNow - lastApply).TotalSeconds >= settings.ReapplySeconds;

        if (mode != desired || periodicReapply)
        {
            Apply(desired, mode != desired);
        }
    }

    private void ApplyManual(bool game)
    {
        automatic = false;
        automaticItem.Checked = false;
        Apply(game ? ActiveMode.ManualGame : ActiveMode.ManualDesktop, true);
    }

    private void Apply(ActiveMode desired, bool announce)
    {
        ColorProfile profile = (desired == ActiveMode.Game || desired == ActiveMode.ManualGame)
            ? settings.Game
            : settings.Desktop;

        try
        {
            controller.Apply(profile, settings.ApplyToAllNvidiaDisplays);
            mode = desired;
            lastApply = DateTime.UtcNow;
            string profileValues = string.Format(
                CultureInfo.InvariantCulture,
                "{0}/{1}/{2:0.00}/{3}",
                profile.Brightness, profile.Contrast, profile.Gamma, profile.Vibrance);
            string text = (desired == ActiveMode.Game || desired == ActiveMode.ManualGame)
                ? "游戏颜色（" + profileValues + "）"
                : "桌面颜色（" + profileValues + "）";
            statusItem.Text = "状态：" + text;
            tray.Text = "NVIDIA 颜色：" + text;

            if (announce)
            {
                Log.Write("Applied " + desired + ": " + profile);
                if (settings.ShowNotifications)
                {
                    tray.BalloonTipTitle = "NVIDIA 颜色已切换";
                    tray.BalloonTipText = text;
                    tray.ShowBalloonTip(1200);
                }
            }
        }
        catch (Exception ex)
        {
            statusItem.Text = "状态：应用失败（请查看日志）";
            tray.Text = "NVIDIA 颜色切换失败";
            Log.Write("Apply failed: " + ex);
        }
    }

    private void OnDisplaySettingsChanged(object sender, EventArgs e)
    {
        try
        {
            controller.RefreshDisplays();
            mode = ActiveMode.Unknown;
            Log.Write("Display topology changed; NVIDIA targets refreshed.");
        }
        catch (Exception ex)
        {
            Log.Write("Display refresh failed: " + ex.Message);
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            mode = ActiveMode.Unknown;
        }
    }

    private static void OpenFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                File.WriteAllText(path, "", Encoding.UTF8);
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write("Open file failed: " + ex.Message);
        }
    }

    private static Icon LoadApplicationIcon()
    {
        try
        {
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch (Exception ex)
        {
            Log.Write("Could not load application icon: " + ex.Message);
            return null;
        }
    }

    protected override void ExitThreadCore()
    {
        if (exiting)
        {
            return;
        }
        exiting = true;
        timer.Stop();

        try
        {
            controller.Apply(settings.Desktop, settings.ApplyToAllNvidiaDisplays);
            Log.Write("Desktop profile restored on exit.");
        }
        catch (Exception ex)
        {
            Log.Write("Restore on exit failed: " + ex.Message);
        }

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        tray.Visible = false;
        tray.Dispose();
        if (appIcon != null) appIcon.Dispose();
        timer.Dispose();
        controller.Dispose();
        base.ExitThreadCore();
    }
}

internal static class ProcessDetector
{
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    public static bool AnyRunning(string[] processNames, bool requireVisibleWindow)
    {
        foreach (string processName in processNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch
            {
                continue;
            }

            foreach (Process process in processes)
            {
                using (process)
                {
                    if (!requireVisibleWindow)
                    {
                        return true;
                    }

                    try
                    {
                        IntPtr window = process.MainWindowHandle;
                        if (window != IntPtr.Zero && IsWindowVisible(window) && !IsIconic(window))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }
        return false;
    }
}

internal static class OneShot
{
    public static int Apply(bool game)
    {
        try
        {
            SwitcherSettings settings = SwitcherSettings.Load();
            using (NvApiColorController controller = new NvApiColorController())
            {
                controller.Initialize();
                controller.Apply(game ? settings.Game : settings.Desktop, settings.ApplyToAllNvidiaDisplays);
            }
            Log.Write("One-shot profile applied: " + (game ? "Game" : "Desktop"));
            return 0;
        }
        catch (Exception ex)
        {
            Log.Write("One-shot apply failed: " + ex);
            return 1;
        }
    }
}

internal static class Diagnostics
{
    public static int Run()
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine("VALORANT NVIDIA Color Switcher diagnostics");
        report.AppendLine("Time: " + DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        report.AppendLine("OS: " + Environment.OSVersion);
        report.AppendLine("64-bit process: " + Environment.Is64BitProcess);
        report.AppendLine("Settings: " + AppPaths.SettingsFile);

        try
        {
            SwitcherSettings settings = SwitcherSettings.Load();
            report.AppendLine("Game profile: " + settings.Game);
            report.AppendLine("Desktop profile: " + settings.Desktop);

            using (NvApiColorController controller = new NvApiColorController())
            {
                controller.Initialize();
                report.AppendLine("NVAPI: OK");
                report.AppendLine("NVIDIA display targets: " + controller.TargetCount);
                foreach (string description in controller.TargetDescriptions)
                {
                    report.AppendLine("  " + description);
                }
            }

            File.WriteAllText(AppPaths.DiagnosticFile, report.ToString(), Encoding.UTF8);
            return 0;
        }
        catch (Exception ex)
        {
            report.AppendLine("NVAPI: FAILED");
            report.AppendLine(ex.ToString());
            File.WriteAllText(AppPaths.DiagnosticFile, report.ToString(), Encoding.UTF8);
            return 1;
        }
    }
}

internal sealed class DisplayTarget
{
    public string Name;
    public IntPtr Handle;
    public uint DisplayId;
    public bool Primary;

    public override string ToString()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}, DisplayId=0x{1:X8}, Handle=0x{2}, Primary={3}",
            Name, DisplayId, Handle.ToInt64().ToString("X", CultureInfo.InvariantCulture), Primary);
    }
}

internal sealed class NvApiColorController : IDisposable
{
    private const uint NvApiInitializeId = 0x0150E828;
    private const uint NvApiUnloadId = 0xD22BDD7E;
    private const uint NvApiEnumDisplayHandleId = 0x9ABDD40D;
    private const uint NvApiGetDvcId = 0x0E45002D;
    private const uint NvApiSetDvcId = 0x4A82C2B1;
    private const uint NvApiSetHueId = 0xF5A0F22C;
    private const uint NvApiGetPrimaryDisplayIdId = 0x1E9D8A31;
    private const uint NvApiSetTargetGammaId = 0x7082A053;
    private const uint NvApiGetAssociatedDisplayHandleId = 0x35C29134;
    private const uint NvApiGetDisplayIdByNameId = 0xAE457190;

    private IntPtr module;
    private NvQueryInterface query;
    private NvUnload unload;
    private NvEnumDisplay enumDisplay;
    private NvGetDvc getDvc;
    private NvSetDvc setDvc;
    private NvSetHue setHue;
    private NvGetPrimaryDisplayId getPrimaryDisplayId;
    private NvSetTargetGamma setTargetGamma;
    private NvGetAssociatedDisplayHandle getAssociatedDisplayHandle;
    private NvGetDisplayIdByName getDisplayIdByName;
    private readonly List<DisplayTarget> targets = new List<DisplayTarget>();
    private bool initialized;

    public int TargetCount { get { return targets.Count; } }

    public IEnumerable<string> TargetDescriptions
    {
        get
        {
            foreach (DisplayTarget target in targets)
            {
                yield return target.ToString();
            }
        }
    }

    public void Initialize()
    {
        if (initialized)
        {
            return;
        }

        module = LoadLibrary("nvapi64.dll");
        if (module == IntPtr.Zero)
        {
            throw new InvalidOperationException("nvapi64.dll was not found. Install or repair the NVIDIA display driver.");
        }

        IntPtr queryPointer = GetProcAddress(module, "nvapi_QueryInterface");
        if (queryPointer == IntPtr.Zero)
        {
            throw new InvalidOperationException("nvapi_QueryInterface was not found.");
        }

        query = (NvQueryInterface)Marshal.GetDelegateForFunctionPointer(queryPointer, typeof(NvQueryInterface));
        NvInitialize initialize = Resolve<NvInitialize>(NvApiInitializeId);
        unload = Resolve<NvUnload>(NvApiUnloadId);
        enumDisplay = Resolve<NvEnumDisplay>(NvApiEnumDisplayHandleId);
        getDvc = Resolve<NvGetDvc>(NvApiGetDvcId);
        setDvc = Resolve<NvSetDvc>(NvApiSetDvcId);
        setHue = Resolve<NvSetHue>(NvApiSetHueId);
        getPrimaryDisplayId = Resolve<NvGetPrimaryDisplayId>(NvApiGetPrimaryDisplayIdId);
        setTargetGamma = Resolve<NvSetTargetGamma>(NvApiSetTargetGammaId);
        getAssociatedDisplayHandle = Resolve<NvGetAssociatedDisplayHandle>(NvApiGetAssociatedDisplayHandleId);
        getDisplayIdByName = Resolve<NvGetDisplayIdByName>(NvApiGetDisplayIdByNameId);

        int status = initialize();
        if (status != 0)
        {
            throw new InvalidOperationException("NvAPI_Initialize failed with status " + status + ".");
        }

        initialized = true;
        RefreshDisplays();
    }

    public void RefreshDisplays()
    {
        if (!initialized)
        {
            throw new InvalidOperationException("NVAPI is not initialized.");
        }

        targets.Clear();
        for (uint index = 0; ; index++)
        {
            DisplayDevice device = new DisplayDevice();
            device.cb = Marshal.SizeOf(typeof(DisplayDevice));
            if (!EnumDisplayDevices(null, index, ref device, 0))
            {
                break;
            }

            const uint Active = 0x00000001;
            const uint MirroringDriver = 0x00000008;
            if ((device.StateFlags & Active) == 0 || (device.StateFlags & MirroringDriver) != 0)
            {
                continue;
            }

            string gdiName = device.DeviceName;
            string nvName = gdiName.StartsWith(@"\.\", StringComparison.Ordinal)
                ? @"\\" + gdiName.Substring(4)
                : gdiName;

            IntPtr handle;
            uint displayId;
            int handleStatus = getAssociatedDisplayHandle(nvName, out handle);
            if (handleStatus != 0)
            {
                handleStatus = getAssociatedDisplayHandle(gdiName, out handle);
            }
            int idStatus = getDisplayIdByName(nvName, out displayId);
            if (idStatus != 0)
            {
                idStatus = getDisplayIdByName(gdiName, out displayId);
            }

            if (handleStatus == 0 && idStatus == 0 && handle != IntPtr.Zero && displayId != 0)
            {
                targets.Add(new DisplayTarget
                {
                    Name = gdiName,
                    Handle = handle,
                    DisplayId = displayId,
                    Primary = (device.StateFlags & 0x00000004) != 0
                });
            }
        }

        if (targets.Count == 0)
        {
            IntPtr primaryHandle;
            uint primaryId;
            int handleStatus = enumDisplay(0, out primaryHandle);
            int idStatus = getPrimaryDisplayId(out primaryId);
            if (handleStatus == 0 && idStatus == 0 && primaryHandle != IntPtr.Zero && primaryId != 0)
            {
                targets.Add(new DisplayTarget
                {
                    Name = "Primary NVIDIA display",
                    Handle = primaryHandle,
                    DisplayId = primaryId,
                    Primary = true
                });
            }
        }

        if (targets.Count == 0)
        {
            throw new InvalidOperationException("No active NVIDIA display could be found.");
        }

        targets.Sort(delegate(DisplayTarget left, DisplayTarget right)
        {
            if (left.Primary == right.Primary) return 0;
            return left.Primary ? -1 : 1;
        });
    }

    public void Apply(ColorProfile profile, bool allDisplays)
    {
        if (!initialized)
        {
            Initialize();
        }

        List<string> errors = new List<string>();
        int count = allDisplays ? targets.Count : Math.Min(1, targets.Count);
        for (int i = 0; i < count; i++)
        {
            DisplayTarget target = targets[i];

            DvcInfoEx dvc = new DvcInfoEx();
            dvc.version = MakeVersion(typeof(DvcInfoEx), 1);
            int status = getDvc(target.Handle, 0, ref dvc);
            if (status == 0)
            {
                dvc.currentLevel = DvcRawFromPercent(profile.Vibrance, dvc);
                status = setDvc(target.Handle, 0, ref dvc);
            }
            if (status != 0)
            {
                errors.Add(target.Name + " Digital Vibrance status=" + status);
            }

            status = setHue(target.Handle, 0, (uint)profile.Hue);
            if (status != 0)
            {
                errors.Add(target.Name + " Hue status=" + status);
            }

            NvGammaCorrectionEx gamma = BuildGamma(profile);
            status = setTargetGamma(target.DisplayId, ref gamma);
            if (status != 0)
            {
                errors.Add(target.Name + " Gamma LUT status=" + status);
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", errors.ToArray()));
        }
    }

    private static int DvcRawFromPercent(int percent, DvcInfoEx info)
    {
        percent = Math.Max(0, Math.Min(100, percent));
        double value;
        if (percent >= 50)
        {
            double t = (percent - 50) / 50.0;
            value = info.defaultLevel + t * (info.maxLevel - info.defaultLevel);
        }
        else
        {
            double t = (50 - percent) / 50.0;
            value = info.defaultLevel - t * (info.defaultLevel - info.minLevel);
        }
        int result = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        return Math.Max(info.minLevel, Math.Min(info.maxLevel, result));
    }

    private static NvGammaCorrectionEx BuildGamma(ColorProfile profile)
    {
        NvGammaCorrectionEx data = new NvGammaCorrectionEx();
        data.version = MakeVersion(typeof(NvGammaCorrectionEx), 1);
        data.gammaRamp = new float[1024 * 3];
        data.unknown = 1;

        double brightness = profile.Brightness / 100.0;
        double contrast = profile.Contrast / 100.0;
        double brightnessRaw = 80.0 + Math.Max(0.0, Math.Min(1.0, brightness)) * 40.0;
        double contrastRaw = 80.0 + Math.Max(0.0, Math.Min(1.0, contrast)) * 40.0;
        double gammaRaw = Math.Max(0.40, Math.Min(2.80, profile.Gamma)) * 100.0;
        double contrastNorm = (contrastRaw - 100.0) / 100.0;
        double brightnessShift = (brightnessRaw - 100.0) / 100.0;
        double gammaInverse = 1.0 / (gammaRaw / 100.0);

        for (int i = 0; i < 1024; i++)
        {
            double x = i / 1023.0;
            double value;
            if (contrastNorm <= 0.0)
                value = (contrastNorm + 1.0) * (x - 0.5);
            else
                value = (x - 0.5) / Math.Max(1.0 - contrastNorm, 0.000001);

            value += brightnessShift + 0.5;
            value = Math.Max(0.0, Math.Min(1.0, value));
            value = Math.Pow(value, gammaInverse);
            value = Math.Max(0.0, Math.Min(1.0, value));

            float channel = (float)value;
            data.gammaRamp[i * 3] = channel;
            data.gammaRamp[i * 3 + 1] = channel;
            data.gammaRamp[i * 3 + 2] = channel;
        }

        return data;
    }

    private static uint MakeVersion(Type type, int version)
    {
        return (uint)(Marshal.SizeOf(type) | (version << 16));
    }

    private T Resolve<T>(uint id) where T : class
    {
        IntPtr pointer = query(id);
        if (pointer == IntPtr.Zero)
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture,
                "NvAPI function 0x{0:X8} is unavailable.", id));
        }
        return (T)(object)Marshal.GetDelegateForFunctionPointer(pointer, typeof(T));
    }

    public void Dispose()
    {
        if (initialized && unload != null)
        {
            try { unload(); } catch { }
            initialized = false;
        }
        if (module != IntPtr.Zero)
        {
            FreeLibrary(module);
            module = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DvcInfoEx
    {
        public uint version;
        public int currentLevel;
        public int minLevel;
        public int maxLevel;
        public int defaultLevel;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct NvGammaCorrectionEx
    {
        public uint version;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3072)]
        public float[] gammaRamp;

        public uint unknown;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr NvQueryInterface(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvInitialize();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvUnload();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvEnumDisplay(int index, out IntPtr displayHandle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvGetDvc(IntPtr displayHandle, uint outputId, ref DvcInfoEx info);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvSetDvc(IntPtr displayHandle, uint outputId, ref DvcInfoEx info);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvSetHue(IntPtr displayHandle, uint outputId, uint hue);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvGetPrimaryDisplayId(out uint displayId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvSetTargetGamma(uint displayId, ref NvGammaCorrectionEx data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate int NvGetAssociatedDisplayHandle(string displayName, out IntPtr displayHandle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate int NvGetDisplayIdByName(string displayName, out uint displayId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string fileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procedureName);

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string device, uint deviceNumber, ref DisplayDevice displayDevice, uint flags);
}
