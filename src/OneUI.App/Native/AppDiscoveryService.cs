using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OneUI.App.Native;

public sealed record DiscoveredApp(string Name, string LaunchPath, string TargetPath);

/// <summary>
/// Discovers all installed Windows 11 applications — both classic Win32 (.lnk/.exe)
/// and Microsoft Store / MSIX / UWP apps (WhatsApp, Microsoft Store, Photos, etc.) via shell:AppsFolder —
/// and extracts genuine native Windows 11 icons via IShellItemImageFactory, PrivateExtractIconsW, and SHGetFileInfo.
/// </summary>
public sealed class AppDiscoveryService
{
    private readonly ConcurrentDictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _indexLock = new();
    private List<DiscoveredApp> _apps = [];

    public IReadOnlyList<DiscoveredApp> Apps
    {
        get
        {
            if (_apps.Count == 0)
            {
                RefreshIndex();
            }
            return _apps;
        }
    }

    public void RefreshIndex()
    {
        lock (_indexLock)
        {
            var seen = new Dictionary<string, DiscoveredApp>(StringComparer.OrdinalIgnoreCase);

            // 1. Enumerate Windows Shell AppsFolder (captures ALL Microsoft Store/MSIX apps like WhatsApp + desktop apps)
            foreach (var app in EnumerateAppsFolderOnSta())
            {
                if (!seen.ContainsKey(app.Name))
                    seen[app.Name] = app;
            }

            // 2. Ensure Core Windows 11 System Utilities are always present
            var sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            AddBuiltIn(seen, "File Explorer", Path.Combine(winDir, "explorer.exe"));
            AddBuiltIn(seen, "Microsoft Edge", ResolveExecutablePath("msedge.exe") ?? "msedge.exe");
            AddBuiltIn(seen, "Windows Terminal", ResolveExecutablePath("wt.exe") ?? "wt.exe");
            AddBuiltIn(seen, "Windows Settings", "ms-settings:", "ms-settings:");
            AddBuiltIn(seen, "Task Manager", Path.Combine(sys32, "Taskmgr.exe"));
            AddBuiltIn(seen, "Notepad", Path.Combine(sys32, "notepad.exe"));
            AddBuiltIn(seen, "Calculator", Path.Combine(sys32, "calc.exe"));
            AddBuiltIn(seen, "Paint", Path.Combine(sys32, "mspaint.exe"));
            AddBuiltIn(seen, "Snipping Tool", "ms-screenclip:", Path.Combine(sys32, "SnippingTool.exe"));
            AddBuiltIn(seen, "PowerShell", Path.Combine(sys32, "WindowsPowerShell", "v1.0", "powershell.exe"));
            AddBuiltIn(seen, "Command Prompt", Path.Combine(sys32, "cmd.exe"));
            AddBuiltIn(seen, "Control Panel", Path.Combine(sys32, "control.exe"));

            // 3. Scan Start Menu & Desktop .lnk shortcuts for any remaining desktop apps
            var startMenuDirs = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\Start Menu\Programs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs")
            };

            foreach (var root in startMenuDirs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                    ScanDirectorySafe(root, seen);
            }

            _apps = seen.Values
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private static List<DiscoveredApp> EnumerateAppsFolderOnSta()
    {
        var results = new List<DiscoveredApp>();

        void Run()
        {
            try
            {
                var shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType is null) return;

                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell is null) return;

                try
                {
                    dynamic? folder = shell.NameSpace("shell:AppsFolder");
                    if (folder is null) return;

                    dynamic? items = folder.Items();
                    if (items is null) return;

                    int count = (int)items.Count;
                    for (int i = 0; i < count; i++)
                    {
                        try
                        {
                            dynamic item = items.Item(i);
                            string name = (string?)item.Name ?? string.Empty;
                            string aumid = (string?)item.Path ?? string.Empty;

                            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(aumid))
                                continue;
                            if (name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase))
                                continue;

                            string launchTarget = $"shell:AppsFolder\\{aumid}";
                            results.Add(new DiscoveredApp(name.Trim(), launchTarget, launchTarget));
                        }
                        catch { }
                    }
                }
                finally
                {
                    Marshal.FinalReleaseComObject(shell);
                }
            }
            catch { }
        }

        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            Run();
        }
        else
        {
            var thread = new Thread(Run);
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(5000);
        }

        return results;
    }

    private static void ScanDirectorySafe(string dir, Dictionary<string, DiscoveredApp> seen)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.lnk", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Readme", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Help", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!seen.ContainsKey(name))
                    seen[name] = new DiscoveredApp(name, file, file);
            }

            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                ScanDirectorySafe(sub, seen);
            }
        }
        catch { }
    }

    private static void AddBuiltIn(Dictionary<string, DiscoveredApp> seen, string name, string launchPath, string? iconTarget = null)
    {
        if (!seen.ContainsKey(name))
            seen[name] = new DiscoveredApp(name, launchPath, iconTarget ?? launchPath);
    }

    public ImageSource? GetIcon(string? pathOrExe)
    {
        if (string.IsNullOrWhiteSpace(pathOrExe)) return null;

        return _iconCache.GetOrAdd(pathOrExe, key =>
        {
            // 1. shell:AppsFolder\<AUMID> (WhatsApp, Microsoft Store, Photos, Calculator, etc.)
            if (key.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
            {
                return TryGetShellItemImage(key, 96);
            }

            // 2. Special native Windows shell icons
            if (key.Equals("special:trash", StringComparison.OrdinalIgnoreCase))
                return ExtractDllIcon(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "imageres.dll"), 50);

            if (key.Equals("ms-settings:", StringComparison.OrdinalIgnoreCase))
                return TryGetShellItemImage("shell:AppsFolder\\windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel", 96)
                       ?? ExtractDllIcon(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shell32.dll"), 314);

            var resolved = ResolveExecutablePath(key) ?? (File.Exists(key) ? key : null);
            if (string.IsNullOrEmpty(resolved))
                return null;

            try
            {
                // 3. Try native ShellItemImageFactory first (works crisply for modern MSIX/Win11 apps & .lnk & .exe)
                var shellImg = TryGetShellItemImage(resolved, 96);
                if (shellImg is not null)
                    return shellImg;

                // 4. 128x128 extraction via user32 PrivateExtractIconsW for .exe/.dll
                if (!resolved.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    var hiIcon = TryExtractLargeIcon(resolved, 128);
                    if (hiIcon != nint.Zero)
                    {
                        try
                        {
                            var bmp = Imaging.CreateBitmapSourceFromHIcon(
                                hiIcon,
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromWidthAndHeight(96, 96));
                            bmp.Freeze();
                            return bmp;
                        }
                        finally
                        {
                            Win32Api.DestroyIcon(hiIcon);
                        }
                    }
                }

                // 5. Shell SHGetFileInfo fallback
                var shIcon = TryGetShellFileIcon(resolved);
                if (shIcon != nint.Zero)
                {
                    try
                    {
                        var bmp = Imaging.CreateBitmapSourceFromHIcon(
                            shIcon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromWidthAndHeight(64, 64));
                        bmp.Freeze();
                        return bmp;
                    }
                    finally
                    {
                        Win32Api.DestroyIcon(shIcon);
                    }
                }

                using var icon = Icon.ExtractAssociatedIcon(resolved);
                if (icon is null) return null;

                var source = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(64, 64));
                source.Freeze();
                return source;
            }
            catch
            {
                return null;
            }
        });
    }

    public ImageSource? GetIconByAppName(string? appName)
    {
        if (string.IsNullOrWhiteSpace(appName)) return null;
        var match = Apps.FirstOrDefault(a => a.Name.Equals(appName, StringComparison.OrdinalIgnoreCase))
                    ?? Apps.FirstOrDefault(a => a.Name.Contains(appName, StringComparison.OrdinalIgnoreCase));
        return match is not null ? GetIcon(match.TargetPath) : null;
    }

    public static string? ResolveExecutablePath(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        if (File.Exists(input)) return input;

        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);

        foreach (var dir in new[] { sys32, winDir })
        {
            var candidate = Path.Combine(dir, input);
            if (File.Exists(candidate)) return candidate;
        }

        if (input.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase))
        {
            var edgePaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe")
            };
            foreach (var p in edgePaths)
                if (File.Exists(p)) return p;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), input);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
        }

        return null;
    }

    public static void Launch(string target, string arguments = "")
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        try
        {
            if (target.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = target,
                    UseShellExecute = true
                });
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                Arguments = arguments,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const int SIIGBF_BIGGERSIZEOK = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        nint pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(Win32Api.SIZE size, int flags, out nint phbm);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIconsW(
        string szFileName,
        int nIconIndex,
        int cxIcon,
        int cyIcon,
        [Out] nint[] phicon,
        [Out] uint[] piconid,
        uint nIcons,
        uint flags);

    private static ImageSource? TryGetShellItemImage(string parsingName, int sizePx)
    {
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            int hr = SHCreateItemFromParsingName(parsingName, nint.Zero, ref iid, out var factory);
            if (hr != 0 || factory is null) return null;

            try
            {
                hr = factory.GetImage(new Win32Api.SIZE { cx = sizePx, cy = sizePx }, SIIGBF_BIGGERSIZEOK, out nint hBitmap);
                if (hr != 0 || hBitmap == nint.Zero) return null;

                try
                {
                    var bmp = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        nint.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    bmp.Freeze();
                    return bmp;
                }
                finally
                {
                    Win32Api.DeleteObject(hBitmap);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? ExtractDllIcon(string dllPath, int iconIndex)
    {
        if (!File.Exists(dllPath)) return null;
        try
        {
            var icons = new nint[1];
            var ids = new uint[1];
            uint count = PrivateExtractIconsW(dllPath, iconIndex, 128, 128, icons, ids, 1, 0);
            if (count > 0 && icons[0] != nint.Zero)
            {
                try
                {
                    var bmp = Imaging.CreateBitmapSourceFromHIcon(
                        icons[0],
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromWidthAndHeight(96, 96));
                    bmp.Freeze();
                    return bmp;
                }
                finally
                {
                    Win32Api.DestroyIcon(icons[0]);
                }
            }
        }
        catch { }
        return null;
    }

    private static nint TryExtractLargeIcon(string filePath, int sizePx)
    {
        try
        {
            var icons = new nint[1];
            var ids = new uint[1];
            uint count = PrivateExtractIconsW(filePath, 0, sizePx, sizePx, icons, ids, 1, 0);
            if (count > 0 && icons[0] != nint.Zero)
                return icons[0];
        }
        catch { }
        return nint.Zero;
    }

    private static nint TryGetShellFileIcon(string filePath)
    {
        try
        {
            var shinfo = new SHFILEINFO();
            var res = SHGetFileInfo(filePath, 0, ref shinfo, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
            if (res != nint.Zero && shinfo.hIcon != nint.Zero)
                return shinfo.hIcon;
        }
        catch { }
        return nint.Zero;
    }
}
