using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TouchBarStudio
{
    /// <summary>Editor window, live preview, tray icon and the glue between touches, drawing and actions.</summary>
    public class MainForm : Form
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "TouchBarStudio";

        readonly AppConfig _cfg = AppConfig.Load();
        readonly Renderer _renderer = new Renderer();
        readonly DfrDevice _device = new DfrDevice();
        TouchInput _touch;

        BarPage _page;
        List<Tile> _tiles = new List<Tile>();
        int _pressed = -1;
        string _lastClock = "";
        string _lastAction = "";
        string _touchState = "not started";
        bool _suppress;
        bool _exiting;
        bool _showAllowed;
        int _tick;

        readonly SplitContainer _split = new SplitContainer();
        readonly PictureBox _preview = new PictureBox();
        readonly ListBox _list = new ListBox();
        readonly PropertyGrid _grid = new PropertyGrid();
        readonly ComboBox _pageBox = new ComboBox();
        readonly ToolStripStatusLabel _status = new ToolStripStatusLabel();
        readonly NotifyIcon _tray = new NotifyIcon();
        readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();

        public MainForm(bool startHidden)
        {
            _showAllowed = !(startHidden || _cfg.StartMinimized);

            Text = "Touch Bar Studio";
            Size = new Size(1100, 720);
            MinimumSize = new Size(900, 560);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = SystemIcons.Application;

            BuildUi();
            BuildTray();

            _page = _cfg.Pages[0];
            FillPages();
            FillList(_page.Items.Count > 0 ? 0 : -1);

            // The window may stay hidden at startup, but BeginInvoke from the touch thread needs a handle.
            var handle = Handle;
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Window handle was not created");
            }

            _timer.Interval = 1000;
            _timer.Tick += OnTick;
            _timer.Start();

            RefreshBar();
            TryConnect();
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(value && _showAllowed);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                _split.SplitterDistance = 330;
            }
            catch
            {
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _touch?.Dispose();
            _device.Clear();
            _device.Close();
            _renderer.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            base.OnFormClosed(e);
        }

        // ---------------------------------------------------------------- UI construction

        void BuildUi()
        {
            _split.Dock = DockStyle.Fill;
            _split.Panel1MinSize = 220;

            _pageBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _pageBox.Width = 130;
            _pageBox.SelectedIndexChanged += (s, e) =>
            {
                if (!_suppress && _pageBox.SelectedItem is BarPage p)
                {
                    SelectPage(p);
                }
            };

            var pageBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 38,
                WrapContents = false,
                Padding = new Padding(2)
            };
            pageBar.Controls.Add(_pageBox);
            pageBar.Controls.Add(MakeButton("New", (s, e) => NewPage()));
            pageBar.Controls.Add(MakeButton("Rename", (s, e) => RenamePage()));
            pageBar.Controls.Add(MakeButton("Delete", (s, e) => DeletePage()));

            _list.Dock = DockStyle.Fill;
            _list.IntegralHeight = false;
            _list.SelectedIndexChanged += (s, e) =>
            {
                if (!_suppress)
                {
                    _grid.SelectedObject = _list.SelectedItem;
                }
            };

            _split.Panel1.Controls.Add(_list);
            _split.Panel1.Controls.Add(pageBar);

            _grid.Dock = DockStyle.Fill;
            _grid.PropertySort = PropertySort.Categorized;
            _grid.PropertyValueChanged += (s, e) =>
            {
                int i = _list.SelectedIndex;
                if (i >= 0)
                {
                    _suppress = true;
                    _list.Items[i] = _list.Items[i]; // refresh the shown text
                    _suppress = false;
                }

                _renderer.ClearIconCache();
                _cfg.Save();
                RefreshBar();
            };
            _split.Panel2.Controls.Add(_grid);

            var previewPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 96,
                BackColor = Color.Black,
                Padding = new Padding(8)
            };
            _preview.Dock = DockStyle.Fill;
            _preview.SizeMode = PictureBoxSizeMode.Zoom;
            _preview.BackColor = Color.Black;
            _preview.MouseDown += OnPreviewDown;
            _preview.MouseUp += OnPreviewUp;
            previewPanel.Controls.Add(_preview);

            var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };

            var add = new ToolStripDropDownButton("Add item");
            foreach (ItemType t in Enum.GetValues(typeof(ItemType)))
            {
                var type = t;
                var mi = new ToolStripMenuItem(type.ToString());
                mi.Click += (s, e) => AddItem(type);
                add.DropDownItems.Add(mi);
            }

            tools.Items.Add(add);
            tools.Items.Add(new ToolStripButton("Remove", null, (s, e) => RemoveItem()));
            tools.Items.Add(new ToolStripButton("Move up", null, (s, e) => MoveItem(-1)));
            tools.Items.Add(new ToolStripButton("Move down", null, (s, e) => MoveItem(1)));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(new ToolStripButton("Send to Touch Bar", null, (s, e) => { TryConnect(); RefreshBar(); }));
            tools.Items.Add(new ToolStripButton("Clear Touch Bar", null, (s, e) => _device.Clear()));
            tools.Items.Add(new ToolStripSeparator());

            var settings = new ToolStripDropDownButton("Settings");
            var autoStart = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = IsAutoStart() };
            autoStart.CheckedChanged += (s, e) =>
            {
                SetAutoStart(autoStart.Checked);
                _cfg.AutoStart = autoStart.Checked;
                _cfg.Save();
            };
            var minimized = new ToolStripMenuItem("Start minimized to tray") { CheckOnClick = true, Checked = _cfg.StartMinimized };
            minimized.CheckedChanged += (s, e) =>
            {
                _cfg.StartMinimized = minimized.Checked;
                _cfg.Save();
            };
            var background = new ToolStripMenuItem("Bar background color...");
            background.Click += (s, e) =>
            {
                using (var dialog = new ColorDialog { Color = _cfg.BarBackground, FullOpen = true })
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        _cfg.BarBackground = dialog.Color;
                        _cfg.Save();
                        RefreshBar();
                    }
                }
            };
            settings.DropDownItems.Add(autoStart);
            settings.DropDownItems.Add(minimized);
            settings.DropDownItems.Add(background);
            settings.DropDownItems.Add(new ToolStripSeparator());
            settings.DropDownItems.Add(new ToolStripMenuItem("Restart as administrator", null, (s, e) => RestartAsAdmin()));
            settings.DropDownItems.Add(new ToolStripMenuItem("Open config folder", null, (s, e) =>
            {
                try
                {
                    System.IO.Directory.CreateDirectory(AppConfig.Dir);
                    Process.Start(new ProcessStartInfo(AppConfig.Dir) { UseShellExecute = true });
                }
                catch
                {
                }
            }));
            tools.Items.Add(settings);

            var statusStrip = new StatusStrip();
            _status.Spring = true;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            statusStrip.Items.Add(_status);

            // Docking order: the control added last is docked first.
            Controls.Add(_split);
            Controls.Add(previewPanel);
            Controls.Add(tools);
            Controls.Add(statusStrip);
        }

        static Button MakeButton(string text, EventHandler onClick)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += onClick;
            return button;
        }

        void BuildTray()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open editor", null, (s, e) => ShowEditor());
            menu.Items.Add("Restart as administrator", null, (s, e) => RestartAsAdmin());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => ExitApp());

            _tray.Icon = SystemIcons.Application;
            _tray.Text = "Touch Bar Studio";
            _tray.ContextMenuStrip = menu;
            _tray.Visible = true;
            _tray.DoubleClick += (s, e) => ShowEditor();
        }

        void ShowEditor()
        {
            _showAllowed = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        void ExitApp()
        {
            _exiting = true;
            Close();
        }

        void RestartAsAdmin()
        {
            try
            {
                var info = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = true, Verb = "runas" };
                Process.Start(info);
                ExitApp();
            }
            catch (Win32Exception)
            {
                _lastAction = "Administrator restart was cancelled";
                UpdateStatus();
            }
        }

        // ---------------------------------------------------------------- pages and items

        void FillPages()
        {
            _suppress = true;
            _pageBox.Items.Clear();
            foreach (var p in _cfg.Pages)
            {
                _pageBox.Items.Add(p);
            }

            _pageBox.SelectedItem = _page;
            _suppress = false;
        }

        void FillList(int select)
        {
            _suppress = true;
            _list.Items.Clear();
            foreach (var item in _page.Items)
            {
                _list.Items.Add(item);
            }

            if (select >= 0 && select < _list.Items.Count)
            {
                _list.SelectedIndex = select;
            }

            _suppress = false;
            _grid.SelectedObject = _list.SelectedItem;
        }

        void SelectPage(BarPage page)
        {
            _page = page;
            _pressed = -1;
            FillPages();
            FillList(_page.Items.Count > 0 ? 0 : -1);
            RefreshBar();
        }

        void GoToPage(string name)
        {
            var page = _cfg.Pages.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (page == null)
            {
                throw new InvalidOperationException("No page named \"" + name + "\"");
            }

            SelectPage(page);
        }

        void NewPage()
        {
            string name = Prompt("New page", "Page name:", "Page " + (_cfg.Pages.Count + 1));
            if (string.IsNullOrEmpty(name)) return;

            if (_cfg.Pages.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, "A page with this name already exists.");
                return;
            }

            var page = new BarPage { Name = name };
            _cfg.Pages.Add(page);
            _cfg.Save();
            SelectPage(page);
        }

        void RenamePage()
        {
            string old = _page.Name;
            string name = Prompt("Rename page", "Page name:", old);
            if (string.IsNullOrEmpty(name) || name == old) return;

            if (_cfg.Pages.Any(p => p != _page && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, "A page with this name already exists.");
                return;
            }

            _page.Name = name;
            foreach (var item in _cfg.Pages.SelectMany(p => p.Items))
            {
                if (item.Type == ItemType.Page && string.Equals(item.TargetPage, old, StringComparison.OrdinalIgnoreCase))
                {
                    item.TargetPage = name;
                }
            }

            _cfg.Save();
            FillPages();
            RefreshBar();
        }

        void DeletePage()
        {
            if (_cfg.Pages.Count <= 1)
            {
                MessageBox.Show(this, "At least one page is required.");
                return;
            }

            if (MessageBox.Show(this, "Delete page \"" + _page.Name + "\"?", "Touch Bar Studio",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            _cfg.Pages.Remove(_page);
            _cfg.Save();
            SelectPage(_cfg.Pages[0]);
        }

        void AddItem(ItemType type)
        {
            var item = new BarItem { Type = type, Label = type.ToString() };
            switch (type)
            {
                case ItemType.App: item.Label = "App"; item.Width = 190; break;
                case ItemType.Media: item.Label = "Play/Pause"; item.Width = 120; break;
                case ItemType.Hotkey: item.Label = "Hotkey"; item.Hotkey = "ctrl+c"; break;
                case ItemType.Command: item.Label = "Command"; break;
                case ItemType.Url: item.Label = "Web"; item.Url = "https://"; break;
                case ItemType.Clock: item.Label = ""; item.Width = 220; item.Align = ItemAlign.Right; break;
                case ItemType.Text: item.Label = "Text"; break;
                case ItemType.Page: item.Label = "Page"; item.TargetPage = _page.Name; break;
                case ItemType.LockScreen: item.Label = "Lock"; item.Width = 110; break;
            }

            _page.Items.Add(item);
            FillList(_page.Items.Count - 1);
            _cfg.Save();
            RefreshBar();
        }

        void RemoveItem()
        {
            int i = _list.SelectedIndex;
            if (i < 0) return;

            _page.Items.RemoveAt(i);
            FillList(Math.Min(i, _page.Items.Count - 1));
            _cfg.Save();
            RefreshBar();
        }

        void MoveItem(int direction)
        {
            int i = _list.SelectedIndex;
            int j = i + direction;
            if (i < 0 || j < 0 || j >= _page.Items.Count) return;

            var item = _page.Items[i];
            _page.Items.RemoveAt(i);
            _page.Items.Insert(j, item);
            FillList(j);
            _cfg.Save();
            RefreshBar();
        }

        // ---------------------------------------------------------------- drawing

        string ClockSignature()
        {
            return string.Join("|", _page.Items.Where(i => i.Type == ItemType.Clock).Select(Renderer.ClockText));
        }

        void RefreshBar()
        {
            _tiles = _renderer.Layout(_page);
            using (var bitmap = _renderer.Render(_cfg, _tiles, _pressed))
            {
                var old = _preview.Image;
                _preview.Image = new Bitmap(bitmap);
                old?.Dispose();

                if (_device.IsOpen && !_device.SendFrame(bitmap))
                {
                    // Probably unplugged or the driver was reloaded: reopen on the next retry.
                    _device.Close();
                }
            }

            _lastClock = ClockSignature();
            UpdateStatus();
        }

        void UpdateStatus()
        {
            _status.Text = "Display: " + (_device.IsOpen ? "connected" : _device.Error) +
                           "   |   Touch: " + _touchState +
                           (string.IsNullOrEmpty(_lastAction) ? "" : "   |   " + _lastAction);
        }

        void OnTick(object sender, EventArgs e)
        {
            _tick++;
            if (ClockSignature() != _lastClock)
            {
                RefreshBar();
            }

            if (_tick % 5 == 0)
            {
                TryConnect();
            }
        }

        void TryConnect()
        {
            if (!_device.IsOpen && _device.Open())
            {
                RefreshBar();
            }

            if (_touch == null)
            {
                var touch = new TouchInput();
                if (touch.Start())
                {
                    touch.Down += x => OnUi(() => TouchDown(x));
                    touch.Move += x => OnUi(() => TouchMove(x));
                    touch.Up += x => OnUi(() => TouchUp(x));
                    _touch = touch;
                    _touchState = "active";
                }
                else
                {
                    _touchState = touch.Error;
                    touch.Dispose();
                }
            }

            UpdateStatus();
        }

        void OnUi(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
            }
        }

        // ---------------------------------------------------------------- touches and clicks

        int HitTest(int x)
        {
            for (int i = 0; i < _tiles.Count; i++)
            {
                if (x >= _tiles[i].Rect.Left && x < _tiles[i].Rect.Right)
                {
                    return i;
                }
            }

            return -1;
        }

        static int BarX(double fraction)
        {
            return (int)Math.Max(0, Math.Min(Renderer.W - 1, fraction * Renderer.W));
        }

        void TouchDown(double fraction)
        {
            _pressed = HitTest(BarX(fraction));
            RefreshBar();
        }

        void TouchMove(double fraction)
        {
            if (_pressed >= 0 && HitTest(BarX(fraction)) != _pressed)
            {
                _pressed = -1;
                RefreshBar();
            }
        }

        void TouchUp(double fraction)
        {
            int hit = HitTest(BarX(fraction));
            int pressed = _pressed;
            _pressed = -1;
            RefreshBar();

            if (pressed >= 0 && pressed == hit && pressed < _tiles.Count)
            {
                Activate(_tiles[pressed].Item);
            }
        }

        // The preview works like the real bar, so items can be tested with the mouse.
        int PreviewBarX(Point p)
        {
            var size = _preview.ClientSize;
            float scale = Math.Min((float)size.Width / Renderer.W, (float)size.Height / Renderer.H);
            if (scale <= 0) return -1;

            float offset = (size.Width - Renderer.W * scale) / 2f;
            int x = (int)((p.X - offset) / scale);
            return x < 0 || x >= Renderer.W ? -1 : x;
        }

        void OnPreviewDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int x = PreviewBarX(e.Location);
            _pressed = x < 0 ? -1 : HitTest(x);
            RefreshBar();
        }

        void OnPreviewUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int x = PreviewBarX(e.Location);
            int hit = x < 0 ? -1 : HitTest(x);
            int pressed = _pressed;
            _pressed = -1;
            RefreshBar();

            if (pressed >= 0 && pressed == hit && pressed < _tiles.Count)
            {
                Activate(_tiles[pressed].Item);
            }
        }

        void Activate(BarItem item)
        {
            try
            {
                if (item.Type == ItemType.Page)
                {
                    GoToPage(item.TargetPage);
                    _lastAction = "Page: " + item.TargetPage;
                }
                else
                {
                    Actions.Run(item);
                    _lastAction = "Ran: " + item;
                }
            }
            catch (Exception ex)
            {
                _lastAction = "Error: " + ex.Message;
            }

            UpdateStatus();
        }

        // ---------------------------------------------------------------- helpers

        static bool IsAutoStart()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    return key?.GetValue(RunName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        static void SetAutoStart(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;
                    if (enabled)
                    {
                        key.SetValue(RunName, "\"" + Environment.ProcessPath + "\" --hidden");
                    }
                    else
                    {
                        key.DeleteValue(RunName, false);
                    }
                }
            }
            catch
            {
            }
        }

        string Prompt(string title, string label, string initial)
        {
            using (var form = new Form())
            {
                form.Text = title;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.ClientSize = new Size(340, 110);

                var text = new Label { Text = label, Left = 12, Top = 12, Width = 316 };
                var box = new TextBox { Text = initial, Left = 12, Top = 36, Width = 316 };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 172, Top = 70, Width = 75 };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 253, Top = 70, Width = 75 };
                form.Controls.AddRange(new Control[] { text, box, ok, cancel });
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                return form.ShowDialog(this) == DialogResult.OK ? box.Text.Trim() : null;
            }
        }
    }
}
