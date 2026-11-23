// ViewerForm.cs - the picture-in-picture window: captures a monitor ~30 times a second and draws it scaled. // file purpose
using System.ComponentModel;                                                                               // Win32Exception (screen not readable)
using System.Diagnostics;                                                                                  // Process.Start for Windows display settings
using System.Drawing.Drawing2D;                                                                            // InterpolationMode, PixelOffsetMode
using System.Drawing.Imaging;                                                                              // PixelFormat for the capture bitmap
using System.Runtime.InteropServices;                                                                      // Marshal.SizeOf
using Microsoft.Win32;                                                                                     // SystemEvents.DisplaySettingsChanged

namespace VirtualDisplayViewer;                                                                            // program namespace

internal sealed class ViewerForm : Form                                                                    // the viewer window
{                                                                                                          // start of ViewerForm
    private const int HotKeyId = 1;                                                                        // our id for the Ctrl+Alt+V hot key
    private readonly System.Windows.Forms.Timer _timer = new();                                            // ticks once per frame
    private readonly ContextMenuStrip _menu = new();                                                       // right-click menu
    private Bitmap? _frame;                                                                                // last captured picture of the monitor
    private string? _sourceDevice;                                                                         // \\.\DISPLAYn currently shown
    private bool _autoSource = true;                                                                       // true = follow the Virtual Display Driver monitor
    private Rectangle _source;                                                                             // bounds of the shown monitor, in screen pixels
    private Rectangle _dest;                                                                               // where the picture is drawn inside this window
    private Point _savedCursor;                                                                            // where the mouse was before we sent it to the monitor
    private bool _haveSavedCursor;                                                                         // true once _savedCursor holds a real position
    private int _retryTicks;                                                                               // counts ticks while the monitor is missing
    private string _status = "Looking for the virtual display...";                                         // message shown when there is no picture

    public ViewerForm()                                                                                    // builds the window
    {                                                                                                      // start of constructor
        Text = "Virtual Display";                                                                          // title bar
        DoubleBuffered = true;                                                                             // no flicker while redrawing
        ResizeRedraw = true;                                                                               // repaint whenever the size changes
        BackColor = Color.Black;                                                                           // black bars around the picture
        TopMost = true;                                                                                    // stays above other windows
        StartPosition = FormStartPosition.Manual;                                                          // we place it ourselves
        MinimumSize = new Size(220, 160);                                                                  // smallest allowed size
        ClientSize = new Size(640, 360);                                                                   // starting size (16:9)
        var wa = Screen.PrimaryScreen!.WorkingArea;                                                        // main screen minus the taskbar
        Location = new Point(wa.Right - Width - 16, wa.Bottom - Height - 16);                              // bottom-right corner, 16 px in

        ContextMenuStrip = _menu;                                                                          // right-click shows our menu
        _menu.Opening += (_, e) => { BuildMenu(); e.Cancel = false; };                                     // rebuild it each time so it is current

        _timer.Interval = 33;                                                                              // about 30 frames per second
        _timer.Tick += (_, _) => CaptureFrame();                                                           // grab a frame on every tick
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;                                           // follow resolution/monitor changes
        PickSource();                                                                                      // find the monitor to show
        _timer.Start();                                                                                    // start capturing
    }                                                                                                      // end of constructor

    // ---------- choosing which monitor to show ----------                                                // section header

    private void PickSource()                                                                              // decides which monitor is shown
    {                                                                                                      // start of PickSource
        var adapters = Native.GetAdapterNames();                                                           // device name -> adapter name
        var screens = Screen.AllScreens;                                                                   // every monitor on the desktop
        Screen? pick = null;                                                                               // the chosen monitor, if any
        if (!_autoSource && _sourceDevice != null)                                                         // the user picked a specific monitor
            pick = screens.FirstOrDefault(s => s.DeviceName.Equals(_sourceDevice, StringComparison.OrdinalIgnoreCase)); // find it again
        pick ??= screens.FirstOrDefault(s => adapters.TryGetValue(s.DeviceName, out var a)                // otherwise: a monitor whose adapter
                                             && a.Contains("Virtual Display", StringComparison.OrdinalIgnoreCase)); // is the Virtual Display Driver
        if (pick == null)                                                                                  // nothing found
        {                                                                                                  // start of if
            _sourceDevice = null;                                                                          // no monitor
            _source = Rectangle.Empty;                                                                     // no bounds
            _status = "Virtual display not found.\nRight-click to choose a monitor.";                      // tell the user
            Text = "Virtual Display - not found";                                                          // and in the title bar
            Invalidate();                                                                                  // repaint to show the message
            return;                                                                                        // done
        }                                                                                                  // end of if
        _sourceDevice = pick.DeviceName;                                                                   // remember which monitor
        _source = pick.Bounds;                                                                             // and where it is on the desktop
        Text = $"Virtual Display - {_source.Width} x {_source.Height}";                                    // show its resolution in the title
    }                                                                                                      // end of PickSource

    private void OnDisplayChanged(object? sender, EventArgs e)                                             // Windows changed a display setting
    {                                                                                                      // start of OnDisplayChanged
        if (IsHandleCreated && !IsDisposed) BeginInvoke(PickSource);                                       // re-pick on the UI thread (event comes from another thread)
    }                                                                                                      // end of OnDisplayChanged

    // ---------- capture and paint ----------                                                             // section header

    private void CaptureFrame()                                                                            // runs on every timer tick
    {                                                                                                      // start of CaptureFrame
        if (_sourceDevice == null || _source.Width <= 0)                                                   // no monitor to show
        {                                                                                                  // start of if
            if (++_retryTicks * _timer.Interval >= 2000) { _retryTicks = 0; PickSource(); }                // look again every ~2 seconds
            return;                                                                                        // nothing to capture
        }                                                                                                  // end of if
        if (WindowState == FormWindowState.Minimized) return;                                              // do not waste CPU while minimized
        try                                                                                                // capturing can fail (lock screen, admin prompt)
        {                                                                                                  // start of try
            if (_frame == null || _frame.Size != _source.Size)                                             // first frame, or the resolution changed
            {                                                                                              // start of if
                _frame?.Dispose();                                                                         // free the old bitmap
                _frame = new Bitmap(_source.Width, _source.Height, PixelFormat.Format32bppRgb);             // new bitmap the size of the monitor
            }                                                                                              // end of if
            using var g = Graphics.FromImage(_frame);                                                      // drawing surface on the bitmap
            g.CopyFromScreen(_source.Location, Point.Empty, _source.Size, CopyPixelOperation.SourceCopy);  // copy the monitor's pixels
            DrawCursor(g);                                                                                 // screen copies have no mouse pointer, so add it
            _status = "";                                                                                  // we have a picture: no message
        }                                                                                                  // end of try
        catch (Win32Exception)                                                                             // the screen could not be read
        {                                                                                                  // start of catch
            return;                                                                                        // screen not readable right now (lock screen or an administrator prompt)
        }                                                                                                  // end of catch
        Invalidate();                                                                                      // ask for a repaint with the new frame
    }                                                                                                      // end of CaptureFrame

    private void DrawCursor(Graphics g)                                                                    // draws the mouse pointer into the frame
    {                                                                                                      // start of DrawCursor
        var ci = new Native.CURSORINFO { cbSize = Marshal.SizeOf<Native.CURSORINFO>() };                  // record for the cursor info (size filled in)
        if (!Native.GetCursorInfo(ref ci) || (ci.flags & Native.CURSOR_SHOWING) == 0 || ci.hCursor == IntPtr.Zero) return; // no visible cursor
        var p = new Point(ci.ptScreenPos.X, ci.ptScreenPos.Y);                                             // where the cursor is on the desktop
        if (!_source.Contains(p)) return;                                                                  // it is on another monitor: nothing to draw
        int hx = 0, hy = 0;                                                                                // hotspot offset (the pointer's tip)
        if (Native.GetIconInfo(ci.hCursor, out var ii))                                                    // read the cursor's details
        {                                                                                                  // start of if
            hx = ii.xHotspot; hy = ii.yHotspot;                                                            // remember the hotspot
            if (ii.hbmMask != IntPtr.Zero) Native.DeleteObject(ii.hbmMask);                                // free the mask bitmap Windows gave us
            if (ii.hbmColor != IntPtr.Zero) Native.DeleteObject(ii.hbmColor);                              // free the colour bitmap Windows gave us
        }                                                                                                  // end of if
        IntPtr hdc = g.GetHdc();                                                                           // Windows device context for the frame
        try { Native.DrawIconEx(hdc, p.X - _source.X - hx, p.Y - _source.Y - hy, ci.hCursor, 0, 0, 0, IntPtr.Zero, Native.DI_NORMAL); } // draw the pointer at the right spot
        finally { g.ReleaseHdc(hdc); }                                                                     // always hand the device context back
    }                                                                                                      // end of DrawCursor

    protected override void OnPaint(PaintEventArgs e)                                                      // draws the window contents
    {                                                                                                      // start of OnPaint
        var g = e.Graphics;                                                                                // drawing surface for the window
        if (_frame == null || _status.Length > 0)                                                          // no picture yet, or a message to show
        {                                                                                                  // start of if
            TextRenderer.DrawText(g, _status, Font, ClientRectangle, Color.Gainsboro,                      // draw the message
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak); // centred and wrapped
            return;                                                                                        // nothing else to draw
        }                                                                                                  // end of if
        var cs = ClientSize;                                                                               // inside size of the window
        double scale = Math.Min((double)cs.Width / _frame.Width, (double)cs.Height / _frame.Height);       // largest scale that fits both ways (keeps the shape)
        int w = Math.Max(1, (int)Math.Round(_frame.Width * scale));                                        // scaled picture width
        int h = Math.Max(1, (int)Math.Round(_frame.Height * scale));                                       // scaled picture height
        _dest = new Rectangle((cs.Width - w) / 2, (cs.Height - h) / 2, w, h);                              // centred in the window (black bars around)
        g.InterpolationMode = scale < 1.0 ? InterpolationMode.HighQualityBilinear : InterpolationMode.NearestNeighbor; // smooth when shrinking, sharp when enlarging
        g.PixelOffsetMode = PixelOffsetMode.Half;                                                          // avoids a half-pixel shift at the edges
        g.DrawImage(_frame, _dest);                                                                        // draw the picture
    }                                                                                                      // end of OnPaint

    // ---------- mouse helpers ----------                                                                 // section header

    protected override void OnMouseDoubleClick(MouseEventArgs e)                                           // double-click in the picture
    {                                                                                                      // start of OnMouseDoubleClick
        base.OnMouseDoubleClick(e);                                                                        // normal handling first
        if (e.Button != MouseButtons.Left || _source.IsEmpty || _dest.Width <= 0 || !_dest.Contains(e.Location)) return; // only a left double-click on the picture
        _savedCursor = Cursor.Position; _haveSavedCursor = true;                                           // remember where the mouse was, for Ctrl+Alt+V
        int x = _source.X + (int)((long)(e.X - _dest.X) * _source.Width / _dest.Width);                    // map the click to the monitor's X
        int y = _source.Y + (int)((long)(e.Y - _dest.Y) * _source.Height / _dest.Height);                  // map the click to the monitor's Y
        Native.SetCursorPos(x, y);                                                                         // put the mouse there on the virtual monitor
    }                                                                                                      // end of OnMouseDoubleClick

    private void ToggleMouse()                                                                             // Ctrl+Alt+V: send the mouse over, or bring it back
    {                                                                                                      // start of ToggleMouse
        if (_source.IsEmpty) return;                                                                       // no monitor shown
        if (_source.Contains(Cursor.Position))                                                             // mouse is on the shown monitor now
        {                                                                                                  // start of if
            var back = _haveSavedCursor ? _savedCursor : new Point(Left + Width / 2, Top + Height / 2);    // where it came from, or the middle of this window
            Native.SetCursorPos(back.X, back.Y);                                                           // bring it back
        }                                                                                                  // end of if
        else                                                                                               // mouse is somewhere else
        {                                                                                                  // start of else
            _savedCursor = Cursor.Position; _haveSavedCursor = true;                                       // remember where it is
            Native.SetCursorPos(_source.X + _source.Width / 2, _source.Y + _source.Height / 2);            // send it to the middle of the shown monitor
        }                                                                                                  // end of else
    }                                                                                                      // end of ToggleMouse

    protected override void OnHandleCreated(EventArgs e)                                                   // the window now exists in Windows
    {                                                                                                      // start of OnHandleCreated
        base.OnHandleCreated(e);                                                                           // normal handling first
        Native.RegisterHotKey(Handle, HotKeyId, Native.MOD_CONTROL | Native.MOD_ALT, (int)Keys.V);         // claim Ctrl+Alt+V
    }                                                                                                      // end of OnHandleCreated

    protected override void OnHandleDestroyed(EventArgs e)                                                 // the window is going away
    {                                                                                                      // start of OnHandleDestroyed
        Native.UnregisterHotKey(Handle, HotKeyId);                                                         // release Ctrl+Alt+V
        base.OnHandleDestroyed(e);                                                                         // normal handling
    }                                                                                                      // end of OnHandleDestroyed

    protected override void WndProc(ref Message m)                                                         // every Windows message for this window
    {                                                                                                      // start of WndProc
        if (m.Msg == Native.WM_HOTKEY && m.WParam == (IntPtr)HotKeyId) { ToggleMouse(); return; }          // our hot key was pressed
        base.WndProc(ref m);                                                                               // everything else: normal handling
    }                                                                                                      // end of WndProc

    // ---------- right-click menu ----------                                                              // section header

    private void BuildMenu()                                                                               // fills the right-click menu
    {                                                                                                      // start of BuildMenu
        _menu.Items.Clear();                                                                               // start empty

        var top = new ToolStripMenuItem("Always on top") { Checked = TopMost };                            // on-top toggle, ticked if on
        top.Click += (_, _) => TopMost = !TopMost;                                                         // flip it
        _menu.Items.Add(top);                                                                              // add to the menu

        var size = new ToolStripMenuItem("Window size");                                                   // submenu: size as % of the monitor
        foreach (int pct in new[] { 25, 33, 50, 75, 100 })                                                 // the offered sizes
        {                                                                                                  // start of loop body
            int p = pct;                                                                                   // copy for the click handler
            var it = new ToolStripMenuItem($"{p}%") { Enabled = !_source.IsEmpty };                        // greyed out with no monitor
            it.Click += (_, _) =>                                                                          // when chosen
            {                                                                                              // start of handler
                WindowState = FormWindowState.Normal;                                                      // un-maximize first
                ClientSize = new Size(Math.Max(1, _source.Width * p / 100), Math.Max(1, _source.Height * p / 100)); // resize to that share of the monitor
            };                                                                                             // end of handler
            size.DropDownItems.Add(it);                                                                    // add to the submenu
        }                                                                                                  // end of loop body
        _menu.Items.Add(size);                                                                             // add the submenu

        var rate = new ToolStripMenuItem("Frame rate");                                                    // submenu: captures per second
        foreach (int fps in new[] { 10, 15, 30, 60 })                                                      // the offered rates
        {                                                                                                  // start of loop body
            int f = fps;                                                                                   // copy for the click handler
            var it = new ToolStripMenuItem($"{f} per second") { Checked = Math.Abs(_timer.Interval - 1000 / f) <= 1 }; // tick the current rate
            it.Click += (_, _) => _timer.Interval = Math.Max(1, 1000 / f);                                 // change the timer
            rate.DropDownItems.Add(it);                                                                    // add to the submenu
        }                                                                                                  // end of loop body
        _menu.Items.Add(rate);                                                                             // add the submenu

        var opacity = new ToolStripMenuItem("Opacity");                                                    // submenu: see-through level
        foreach (int pct in new[] { 100, 85, 70, 50 })                                                     // the offered levels
        {                                                                                                  // start of loop body
            int p = pct;                                                                                   // copy for the click handler
            var it = new ToolStripMenuItem($"{p}%") { Checked = Math.Abs(Opacity - p / 100.0) < 0.01 };    // tick the current level
            it.Click += (_, _) => Opacity = p / 100.0;                                                     // change it
            opacity.DropDownItems.Add(it);                                                                 // add to the submenu
        }                                                                                                  // end of loop body
        _menu.Items.Add(opacity);                                                                          // add the submenu

        var src = new ToolStripMenuItem("Show monitor");                                                   // submenu: which monitor to show
        var auto = new ToolStripMenuItem("Virtual display (automatic)") { Checked = _autoSource };         // follow the virtual display
        auto.Click += (_, _) => { _autoSource = true; PickSource(); };                                     // switch back to automatic
        src.DropDownItems.Add(auto);                                                                       // add it
        src.DropDownItems.Add(new ToolStripSeparator());                                                   // divider
        var adapters = Native.GetAdapterNames();                                                           // adapter names for the labels
        foreach (var s in Screen.AllScreens)                                                               // one entry per monitor
        {                                                                                                  // start of loop body
            string dev = s.DeviceName;                                                                     // e.g. \\.\DISPLAY2
            adapters.TryGetValue(dev, out var adapter);                                                    // its adapter name, if known
            string label = $"{dev.Replace(@"\\.\", "")} - {adapter ?? "display"} - {s.Bounds.Width} x {s.Bounds.Height}{(s.Primary ? " (main)" : "")}"; // e.g. "DISPLAY2 - NVIDIA ... - 1920 x 1080 (main)"
            var it = new ToolStripMenuItem(label) { Checked = !_autoSource && dev.Equals(_sourceDevice, StringComparison.OrdinalIgnoreCase) }; // tick the one shown
            it.Click += (_, _) => { _autoSource = false; _sourceDevice = dev; PickSource(); };             // show this monitor
            src.DropDownItems.Add(it);                                                                     // add it
        }                                                                                                  // end of loop body
        _menu.Items.Add(src);                                                                              // add the submenu

        _menu.Items.Add(new ToolStripSeparator());                                                         // divider
        var mouse = new ToolStripMenuItem("Send mouse to this display / bring it back") { ShortcutKeyDisplayString = "Ctrl+Alt+V", Enabled = !_source.IsEmpty }; // same as the hot key
        mouse.Click += (_, _) => ToggleMouse();                                                            // do it
        _menu.Items.Add(mouse);                                                                            // add to the menu

        var settings = new ToolStripMenuItem("Windows display settings...");                               // shortcut to Settings > Display
        settings.Click += (_, _) =>                                                                        // when chosen
        {                                                                                                  // start of handler
            try { Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true }); } catch { } // open Settings (ignore if it fails)
        };                                                                                                 // end of handler
        _menu.Items.Add(settings);                                                                         // add to the menu

        _menu.Items.Add(new ToolStripSeparator());                                                         // divider
        var exit = new ToolStripMenuItem("Exit");                                                          // quit
        exit.Click += (_, _) => Close();                                                                   // close the window (ends the program)
        _menu.Items.Add(exit);                                                                             // add to the menu
    }                                                                                                      // end of BuildMenu

    protected override void OnFormClosed(FormClosedEventArgs e)                                            // the window has closed
    {                                                                                                      // start of OnFormClosed
        _timer.Stop();                                                                                     // stop capturing
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;                                           // stop listening (static event would keep us alive)
        _frame?.Dispose();                                                                                 // free the last frame
        _frame = null;                                                                                     // and forget it
        base.OnFormClosed(e);                                                                              // normal handling
    }                                                                                                      // end of OnFormClosed
}                                                                                                          // end of ViewerForm
