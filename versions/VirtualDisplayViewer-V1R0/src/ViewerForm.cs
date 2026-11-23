using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VirtualDisplayViewer;

internal sealed class ViewerForm : Form
{
    private const int HotKeyId = 1;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly ContextMenuStrip _menu = new();
    private Bitmap? _frame;
    private string? _sourceDevice;          // \\.\DISPLAYn currently shown
    private bool _autoSource = true;        // true = follow the Virtual Display Driver monitor
    private Rectangle _source;              // bounds of the shown monitor, in screen pixels
    private Rectangle _dest;                // where the picture is drawn inside this window
    private Point _savedCursor;
    private bool _haveSavedCursor;
    private int _retryTicks;
    private string _status = "Looking for the virtual display...";

    public ViewerForm()
    {
        Text = "Virtual Display";
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.Black;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(220, 160);
        ClientSize = new Size(640, 360);
        var wa = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(wa.Right - Width - 16, wa.Bottom - Height - 16);

        ContextMenuStrip = _menu;
        _menu.Opening += (_, e) => { BuildMenu(); e.Cancel = false; };

        _timer.Interval = 33;
        _timer.Tick += (_, _) => CaptureFrame();
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        PickSource();
        _timer.Start();
    }

    // ---------- choosing which monitor to show ----------

    private void PickSource()
    {
        var adapters = Native.GetAdapterNames();
        var screens = Screen.AllScreens;
        Screen? pick = null;
        if (!_autoSource && _sourceDevice != null)
            pick = screens.FirstOrDefault(s => s.DeviceName.Equals(_sourceDevice, StringComparison.OrdinalIgnoreCase));
        pick ??= screens.FirstOrDefault(s => adapters.TryGetValue(s.DeviceName, out var a)
                                             && a.Contains("Virtual Display", StringComparison.OrdinalIgnoreCase));
        if (pick == null)
        {
            _sourceDevice = null;
            _source = Rectangle.Empty;
            _status = "Virtual display not found.\nRight-click to choose a monitor.";
            Text = "Virtual Display - not found";
            Invalidate();
            return;
        }
        _sourceDevice = pick.DeviceName;
        _source = pick.Bounds;
        Text = $"Virtual Display - {_source.Width} x {_source.Height}";
    }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        if (IsHandleCreated && !IsDisposed) BeginInvoke(PickSource);
    }

    // ---------- capture and paint ----------

    private void CaptureFrame()
    {
        if (_sourceDevice == null || _source.Width <= 0)
        {
            if (++_retryTicks * _timer.Interval >= 2000) { _retryTicks = 0; PickSource(); }
            return;
        }
        if (WindowState == FormWindowState.Minimized) return;
        try
        {
            if (_frame == null || _frame.Size != _source.Size)
            {
                _frame?.Dispose();
                _frame = new Bitmap(_source.Width, _source.Height, PixelFormat.Format32bppRgb);
            }
            using var g = Graphics.FromImage(_frame);
            g.CopyFromScreen(_source.Location, Point.Empty, _source.Size, CopyPixelOperation.SourceCopy);
            DrawCursor(g);
            _status = "";
        }
        catch (Win32Exception)
        {
            return; // screen not readable right now (lock screen or an administrator prompt)
        }
        Invalidate();
    }

    private void DrawCursor(Graphics g)
    {
        var ci = new Native.CURSORINFO { cbSize = Marshal.SizeOf<Native.CURSORINFO>() };
        if (!Native.GetCursorInfo(ref ci) || (ci.flags & Native.CURSOR_SHOWING) == 0 || ci.hCursor == IntPtr.Zero) return;
        var p = new Point(ci.ptScreenPos.X, ci.ptScreenPos.Y);
        if (!_source.Contains(p)) return;
        int hx = 0, hy = 0;
        if (Native.GetIconInfo(ci.hCursor, out var ii))
        {
            hx = ii.xHotspot; hy = ii.yHotspot;
            if (ii.hbmMask != IntPtr.Zero) Native.DeleteObject(ii.hbmMask);
            if (ii.hbmColor != IntPtr.Zero) Native.DeleteObject(ii.hbmColor);
        }
        IntPtr hdc = g.GetHdc();
        try { Native.DrawIconEx(hdc, p.X - _source.X - hx, p.Y - _source.Y - hy, ci.hCursor, 0, 0, 0, IntPtr.Zero, Native.DI_NORMAL); }
        finally { g.ReleaseHdc(hdc); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (_frame == null || _status.Length > 0)
        {
            TextRenderer.DrawText(g, _status, Font, ClientRectangle, Color.Gainsboro,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        var cs = ClientSize;
        double scale = Math.Min((double)cs.Width / _frame.Width, (double)cs.Height / _frame.Height);
        int w = Math.Max(1, (int)Math.Round(_frame.Width * scale));
        int h = Math.Max(1, (int)Math.Round(_frame.Height * scale));
        _dest = new Rectangle((cs.Width - w) / 2, (cs.Height - h) / 2, w, h);
        g.InterpolationMode = scale < 1.0 ? InterpolationMode.HighQualityBilinear : InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_frame, _dest);
    }

    // ---------- mouse helpers ----------

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left || _source.IsEmpty || _dest.Width <= 0 || !_dest.Contains(e.Location)) return;
        _savedCursor = Cursor.Position; _haveSavedCursor = true;
        int x = _source.X + (int)((long)(e.X - _dest.X) * _source.Width / _dest.Width);
        int y = _source.Y + (int)((long)(e.Y - _dest.Y) * _source.Height / _dest.Height);
        Native.SetCursorPos(x, y);
    }

    private void ToggleMouse()
    {
        if (_source.IsEmpty) return;
        if (_source.Contains(Cursor.Position))
        {
            var back = _haveSavedCursor ? _savedCursor : new Point(Left + Width / 2, Top + Height / 2);
            Native.SetCursorPos(back.X, back.Y);
        }
        else
        {
            _savedCursor = Cursor.Position; _haveSavedCursor = true;
            Native.SetCursorPos(_source.X + _source.Width / 2, _source.Y + _source.Height / 2);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.RegisterHotKey(Handle, HotKeyId, Native.MOD_CONTROL | Native.MOD_ALT, (int)Keys.V);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        Native.UnregisterHotKey(Handle, HotKeyId);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && m.WParam == (IntPtr)HotKeyId) { ToggleMouse(); return; }
        base.WndProc(ref m);
    }

    // ---------- right-click menu ----------

    private void BuildMenu()
    {
        _menu.Items.Clear();

        var top = new ToolStripMenuItem("Always on top") { Checked = TopMost };
        top.Click += (_, _) => TopMost = !TopMost;
        _menu.Items.Add(top);

        var size = new ToolStripMenuItem("Window size");
        foreach (int pct in new[] { 25, 33, 50, 75, 100 })
        {
            int p = pct;
            var it = new ToolStripMenuItem($"{p}%") { Enabled = !_source.IsEmpty };
            it.Click += (_, _) =>
            {
                WindowState = FormWindowState.Normal;
                ClientSize = new Size(Math.Max(1, _source.Width * p / 100), Math.Max(1, _source.Height * p / 100));
            };
            size.DropDownItems.Add(it);
        }
        _menu.Items.Add(size);

        var rate = new ToolStripMenuItem("Frame rate");
        foreach (int fps in new[] { 10, 15, 30, 60 })
        {
            int f = fps;
            var it = new ToolStripMenuItem($"{f} per second") { Checked = Math.Abs(_timer.Interval - 1000 / f) <= 1 };
            it.Click += (_, _) => _timer.Interval = Math.Max(1, 1000 / f);
            rate.DropDownItems.Add(it);
        }
        _menu.Items.Add(rate);

        var opacity = new ToolStripMenuItem("Opacity");
        foreach (int pct in new[] { 100, 85, 70, 50 })
        {
            int p = pct;
            var it = new ToolStripMenuItem($"{p}%") { Checked = Math.Abs(Opacity - p / 100.0) < 0.01 };
            it.Click += (_, _) => Opacity = p / 100.0;
            opacity.DropDownItems.Add(it);
        }
        _menu.Items.Add(opacity);

        var src = new ToolStripMenuItem("Show monitor");
        var auto = new ToolStripMenuItem("Virtual display (automatic)") { Checked = _autoSource };
        auto.Click += (_, _) => { _autoSource = true; PickSource(); };
        src.DropDownItems.Add(auto);
        src.DropDownItems.Add(new ToolStripSeparator());
        var adapters = Native.GetAdapterNames();
        foreach (var s in Screen.AllScreens)
        {
            string dev = s.DeviceName;
            adapters.TryGetValue(dev, out var adapter);
            string label = $"{dev.Replace(@"\\.\", "")} - {adapter ?? "display"} - {s.Bounds.Width} x {s.Bounds.Height}{(s.Primary ? " (main)" : "")}";
            var it = new ToolStripMenuItem(label) { Checked = !_autoSource && dev.Equals(_sourceDevice, StringComparison.OrdinalIgnoreCase) };
            it.Click += (_, _) => { _autoSource = false; _sourceDevice = dev; PickSource(); };
            src.DropDownItems.Add(it);
        }
        _menu.Items.Add(src);

        _menu.Items.Add(new ToolStripSeparator());
        var mouse = new ToolStripMenuItem("Send mouse to this display / bring it back") { ShortcutKeyDisplayString = "Ctrl+Alt+V", Enabled = !_source.IsEmpty };
        mouse.Click += (_, _) => ToggleMouse();
        _menu.Items.Add(mouse);

        var settings = new ToolStripMenuItem("Windows display settings...");
        settings.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true }); } catch { }
        };
        _menu.Items.Add(settings);

        _menu.Items.Add(new ToolStripSeparator());
        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => Close();
        _menu.Items.Add(exit);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        _frame?.Dispose();
        _frame = null;
        base.OnFormClosed(e);
    }
}
