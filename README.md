# OneUI — Liquid Glass macOS UI Suite for Windows 11

A lightweight, 100% safe and reversible Windows 11 desktop application (C# / .NET 9 WPF + native Win32/DWM APIs) that brings a macOS Tahoe–inspired **Liquid Glass** experience to Windows 11 while preserving all Windows functionality.

## Features
- **Floating Liquid Glass Dock**: Native Windows 11 Acrylic blur, specular rim lighting, smooth magnification wave on hover, launch bounce animations, running app indicators, pinning/unpinning, optional auto-hide, and Recycle Bin shortcut.
- **Top Menu Bar & Control Center**: Active application title, live Wi-Fi / Volume / Battery / Clock indicators, system actions menu, and a Liquid Glass Control Center flyout with real-time CoreAudio volume control and Dark/Light glass switching.
- **Spotlight Quick Launcher (`Alt + Space`)**: Fuzzy search across Start Menu apps, built-in math calculator, system commands, and web search fallback.
- **Traffic-Light Window Overlay**: Floating macOS-style Close (Red), Minimize (Yellow), and Maximize/Restore (Green) pill that tracks the active window with hover glyphs and automatic fullscreen/desktop hiding.
- **100% Safe & Reversible**: Zero system file patching. Exiting OneUI or pressing `Ctrl + Alt + Shift + R` immediately restores the native Windows 11 taskbar.

## Quick Start
Double-click `Launch-OneUI.cmd` or run:
```powershell
& "c:\Users\Joseph\One UI\src\OneUI.App\bin\Release\net9.0-windows\OneUI.App.exe"
```

## Build & Test
```powershell
$dotnet = "$env:USERPROFILE\.dotnet\dotnet.exe"
& $dotnet build "c:\Users\Joseph\One UI\OneUI.sln" -c Release
& $dotnet test "c:\Users\Joseph\One UI\OneUI.sln" -c Release
```
