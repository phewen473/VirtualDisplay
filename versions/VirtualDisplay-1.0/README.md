# Virtual Display

A virtual monitor that Windows treats as a real one, plus **Virtual Display Viewer**: a small
picture-in-picture window that shows that monitor on your real screen.

**Version 1.0** (2026-11-22). MIT License, Copyright (c) 2026 P. H. Ewen (KO4WSC). See [LICENSE](LICENSE).

## What it does

Windows can host monitors that have no physical screen attached. Programs dragged onto one keep
running and drawing normally, and you can give it any resolution the driver offers. Uses include:
- a scratch screen for screen-sharing, OBS or streaming
- a test monitor at a resolution your real screens can't do
- a parking spot for a program you want to keep an eye on

The **viewer** shows that monitor in a resizable, always-on-top window so you can see it without
switching anything.

## Parts

1. **Virtual Display Driver** (third-party, open source, MIT): release 25.7.23 from
   [VirtualDrivers/Virtual-Display-Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver).
   The unmodified driver-only package is in `driver\VirtualDisplayDriver\` with its license.
   Once installed it shows in Device Manager as "Virtual Display Driver" and in Display Settings
   as "Generic Monitor (VDD by MTT)". Its settings file is `\VirtualDisplayDriver\vdd_settings.xml` (on the Windows drive)
   (resolutions, refresh rates, 10-bit / HDR switches).
2. **Virtual Display Viewer** (this project, C# WinForms, .NET 10): `app\VirtualDisplayViewer.exe`.

## Requirements

- Windows 11 (or Windows 10 1903+) with a graphics driver that supports the Windows Indirect Display
  Driver model (IddCx). Windows 11 24H2 ships IddCx 1.10.
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download) to run the viewer;
  .NET 10 SDK to build it.
- Administrator rights to install the driver.

## Installing the driver

1. Open PowerShell **as Administrator** in this folder.
2. Run `powershell -ExecutionPolicy Bypass -File driver\install-vdd.ps1`

The script uses only Windows' own driver-install functions (no third-party installer):
- It checks the SHA-256 hash of each driver file and refuses anything that differs from the reviewed files.
- It checks that the driver catalog's digital signature is valid.
- It creates the `\VirtualDisplayDriver` folder on the Windows drive with the starting settings.
- It trusts the driver's signer only for the duration of the install, then removes that trust.
- It creates the virtual display device and installs the driver. If anything fails, it rolls back the device it made.

A log is written to `%TEMP%\vdd-install.log`. To use the driver files from another folder, add
`-PackagePath <folder>`.

Driver file hashes (SHA-256):

| File | SHA-256 |
|---|---|
| `MttVDD.dll` | `C9CA837F57A98FBD43BC416A7F535A95843626E7759EAF85CF0CD7CE334DBB05` |
| `mttvdd.cat` | `08A0093FC9B2E32B287A6F8A77CA4DE0A31830D29FC33D2B13A918DC859468F6` |
| `MttVDD.inf` | `550D211FE481E74DFE3F9D724ED78BE48B3A9113405965D683D9373E8D672F5D` |

### Safety review of the driver (before it was first installed)

- **What was checked:** about 5,000 lines of the driver's source, plus the import list and text of the shipped `MttVDD.dll`.
- **What the driver does not do:** no networking, no launching programs, no loading extra code. It only reads the registry, and screen frames are never copied or saved.
- **Signature:** signed by SignPath Foundation (GlobalSign chain); the signature is valid.
- **Weak point (low risk):** the driver's control pipe `\\.\pipe\MTTVirtualDisplayPipe` is open to any program on the PC. A program can only use it to change driver settings or reload the driver, and one malformed command can crash the driver until Windows restarts it.
- **Limit of the review:** the shipped binary is dated Dec 2024, while the reviewed source was a later version. The binary was checked by its imports and text, not decompiled.

### Removing the driver

Device Manager > Display adapters > Virtual Display Driver > Uninstall device (tick "Attempt to
remove the driver for this device"), then delete the `\VirtualDisplayDriver` folder on the Windows drive.

## Using the viewer

- Run `app\VirtualDisplayViewer.exe`. It finds the virtual monitor by itself and shows it in an
  always-on-top window in the bottom-right corner. The title bar shows the monitor's resolution.
- Drag the window edges to resize. The picture keeps its shape.
- **Double-click** a spot in the picture to put the mouse there on the virtual monitor.
- **Ctrl+Alt+V** sends the mouse to the virtual monitor, and brings it back.
- **Right-click** for:
  - Always on top
  - Window size (25-100% of the monitor)
  - Frame rate (10/15/30/60 per second)
  - Opacity
  - Show monitor (automatic, or any monitor)
  - Send mouse
  - Windows display settings
  - Exit
- To move a program onto the virtual monitor, click it and press **Win+Shift+Right Arrow** (or Left).

### Changing resolution or color depth

Windows Settings > System > Display > pick the virtual monitor > Display resolution. The viewer
follows the change by itself. The default settings offer 800x600, 1366x768, 1920x1080, 2560x1440 and
3840x2160. Add more in `\VirtualDisplayDriver\vdd_settings.xml` (on the Windows drive).

## Building

Run `build.bat`. It runs `dotnet publish -c Release -r win-x64 --self-contained false
-p:PublishSingleFile=true` in `src\VirtualDisplayViewer` and puts `VirtualDisplayViewer.exe` in `app\`.
Every line of code carries a comment explaining what it does.

## Files

| Path | What it is |
|---|---|
| `app\VirtualDisplayViewer.exe` | the built viewer (1.0) |
| `src\VirtualDisplayViewer\Program.cs` | startup and the Windows API declarations |
| `src\VirtualDisplayViewer\ViewerForm.cs` | the viewer window: capture, drawing, mouse, menu |
| `src\VirtualDisplayViewer\VirtualDisplayViewer.csproj` | project settings |
| `driver\install-vdd.ps1` | driver install script |
| `driver\VirtualDisplayDriver\` | the third-party driver package and its license |
| `build.bat` | builds the viewer |
| `versions\` | a snapshot of every released version (V1R0, 1.0); older ones are never overwritten |

## Known limits and ideas

- **Known limits:**
  - The viewer only shows the monitor. You can't click or type through it; double-click and Ctrl+Alt+V move the real mouse.
  - It uses about 60% of one CPU core at 1920x1080 and 30 frames per second, because it copies the screen with the CPU.
  - It doesn't remember its position or settings, and doesn't start with Windows.
  - Once, the virtual monitor dropped back from 1920x1080 to 800x600 a few minutes after the resolution was set. Setting it again held; the cause wasn't found.
  - The driver's 10-bit color and HDR switches are untested.
- **Ideas:**
  - Capture on the GPU to cut CPU use.
  - Pass the mouse and keyboard through the viewer window.
  - Remember the window's position and settings.
  - Optionally start with Windows.
