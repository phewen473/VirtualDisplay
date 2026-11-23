# Virtual Display

A virtual monitor that Windows treats as real, plus a picture-in-picture viewer for it.

## Parts
1. **Virtual Display Driver** (open source, VirtualDrivers/Virtual-Display-Driver, release 25.7.23).
   Shows in Device Manager as "Virtual Display Driver" and in Display Settings as "Generic Monitor (VDD by MTT)".
   Settings file: `\VirtualDisplayDriver\vdd_settings.xml` (on the Windows drive) (resolutions, refresh rates, 10-bit / HDR switches).
2. **VirtualDisplayViewer** (this project, C# WinForms, .NET 10) - `app\VirtualDisplayViewer.exe`.

## Using the viewer
- Run `app\VirtualDisplayViewer.exe`. It finds the virtual monitor by itself and shows it in an always-on-top window.
- Drag the window edges to resize; the picture keeps its shape.
- Double-click a spot in the picture to put the mouse there on the virtual monitor.
- Ctrl+Alt+V sends the mouse to the virtual monitor, and brings it back.
- Right-click for: always on top, window size, frame rate, opacity, which monitor to show, Windows display settings, exit.
- To move a program onto the virtual monitor: click it, then press Win+Shift+Right Arrow.

## Changing resolution or color depth
Windows Settings > System > Display > pick the virtual monitor > Display resolution.
The viewer follows the change by itself.

## Building
Run `build.bat` (needs the .NET 10 SDK). Output goes to `app\`.

## Versions
Each version is kept as its own snapshot under `versions\`. Current: V1R0.

## Removing the driver
Device Manager > Display adapters > Virtual Display Driver > Uninstall device
(tick "Attempt to remove the driver for this device"), then delete the `\VirtualDisplayDriver` folder on the Windows drive.
