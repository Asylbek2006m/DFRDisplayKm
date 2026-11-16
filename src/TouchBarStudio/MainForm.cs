using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TouchBarStudio
{
    /// <summary>Editor window (Windows 11 style), live preview, tray icon, and the glue between touches, drawing and actions.</summary>
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
        int _drag = -1;
        string _lastForeground = "";
        BarPage _homePage;
        string _lastClock = "";
        string _lastAction = "";
        string _touchState = "not started";
        bool _suppress;
        bool _exiting;
        bool _showAllowed;
        int _tick;

        // controls
        readonly List<Control> _containers = new List<Control>();
        readonly Label _title = new Label();
        readonly Label _subtitle = new Label();
        readonly Label _statusText = new Label();
        readonly Label _statusDot = new Label();
        readonly FlowLayoutPanel _pageBar = new FlowLayoutPanel();
        readonly PreviewSurface _preview = new PreviewSurface();
        readonly ListBox _list = new ListBox();
        readonly PropertyGrid _grid = new PropertyGrid();
        readonly Card _previewCard = new Card();
        readonly Card _listCard = new Card();
        readonly Card _gridCard = new Card();
        readonly NotifyIcon _tray = new NotifyIcon();
        readonly ContextMenuStrip _trayMenu = new ContextMenuStrip();
        readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        Font _titleFont;
        Font _subFont;
        Font _itemTitleFont;
        Font _itemSubFont;

        public MainForm(bool startHidden)
        {
            Loc.Set(_cfg.Language);
            Theme.Init();
            Theme.Apply(_cfg.Theme);

            _showAllowed = !(startHidden || _cfg.StartMinimized);

            Text = "Touch Bar Studio";
            ClientSize = new Size(Theme.S(1120), Theme.S(740));
            MinimumSize = new Size(Theme.S(960), Theme.S(640));
            StartPosition = FormStartPosition.CenterScreen;
            Icon = SystemIcons.Application;
            Font = Theme.UiFont;

            _titleFont = Theme.MakeFont(17f, FontStyle.Bold);
            _subFont = Theme.MakeFont(9f);
            _itemTitleFont = Theme.MakeFont(10f);
            _itemSubFont = Theme.MakeFont(8.5f);

            BuildUi();
            BuildTray();

            _page = _cfg.Pages[0];
            RebuildPageBar();
            FillList(_page.Items.Count > 0 ? 0 : -1);

            ApplyTheme();
            Theme.Changed += OnThemeChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            // The window may stay hidden at startup, but BeginInvoke from the touch thread needs a handle.
            if (Handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Window handle was not created");
            }

            StartSystemServices();

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

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        const int WM_CLIPBOARDUPDATE = 0x031D;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.StyleWindow(this);
            try { AddClipboardFormatListener(Handle); } catch { }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            try { RemoveClipboardFormatListener(Handle); } catch { }
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_CLIPBOARDUPDATE && ClipboardHistory.Capture() && _page != null &&
                _page.Items.Any(i => i.Type == ItemType.Clipboard))
            {
                RefreshBar();
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible)
            {
                _preview.Frame = _renderer.Frame;
                _preview.Invalidate();
                Theme.StyleScrollbars(_list);
                Theme.StyleScrollbars(_grid);
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
            Theme.Changed -= OnThemeChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _timer.Stop();
            SystemState.Media?.Dispose();
            _touch?.Dispose();
            _device.Clear();
            _device.Close();
            _renderer.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            base.OnFormClosed(e);
        }

        // ---------------------------------------------------------------- theme

        void OnThemeChanged()
        {
            ApplyTheme();
        }

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General && _cfg.Theme == ThemeMode.Auto)
            {
                OnUi(() => Theme.Apply(ThemeMode.Auto));
            }
        }

        void ApplyTheme()
        {
            var p = Theme.P;
            BackColor = p.Window;
            ForeColor = p.Text;

            foreach (var c in _containers)
            {
                c.BackColor = p.Window;
            }

            _title.ForeColor = p.Text;
            _subtitle.ForeColor = p.SubText;
            _statusText.ForeColor = p.SubText;

            _list.BackColor = p.Surface;
            _list.ForeColor = p.Text;

            _grid.BackColor = p.Surface;
            _grid.ViewBackColor = p.Surface;
            _grid.ViewForeColor = p.Text;
            _grid.LineColor = p.Border;
            _grid.CategoryForeColor = p.SubText;
            _grid.HelpBackColor = p.SurfaceAlt;
            _grid.HelpForeColor = p.Text;
            _grid.HelpBorderColor = p.Border;
            _grid.ViewBorderColor = p.Border;
            _grid.DisabledItemForeColor = p.SubText;

            Theme.StyleMenu(_trayMenu);
            Theme.StyleWindow(this);
            Theme.StyleScrollbars(_list);
            Theme.StyleScrollbars(_grid);

            if (_page != null)
            {
                RebuildPageBar();
            }

            UpdateStatus();
            Invalidate(true);
        }

        // ---------------------------------------------------------------- UI construction

        T Track<T>(T control) where T : Control
        {
            _containers.Add(control);
            return control;
        }

        static ModernButton MakeButton(string text, int width, EventHandler onClick, bool accent = false)
        {
            var button = new ModernButton { Text = Loc.T(text), Tag = text, Accent = accent };
            button.Width = Math.Max(Theme.S(width), TextRenderer.MeasureText(button.Text, Theme.UiFont).Width + Theme.S(28));
            button.Margin = new Padding(0, 0, Theme.S(8), Theme.S(8));
            button.Click += onClick;
            return button;
        }

        void BuildUi()
        {
            // ---- header: title on the left, actions on the right
            var header = Track(new Panel { Dock = DockStyle.Top, Height = Theme.S(68), Padding = new Padding(Theme.S(24), Theme.S(12), Theme.S(24), Theme.S(4)) });

            var titles = Track(new Panel { Dock = DockStyle.Fill });
            _subtitle.Dock = DockStyle.Fill;
            _subtitle.Font = _subFont;
            _subtitle.Text = Loc.T("Tap a button in the preview to try it. Drop a program onto the window to add it.");
            _subtitle.TextAlign = ContentAlignment.TopLeft;
            _title.Dock = DockStyle.Top;
            _title.Height = Theme.S(34);
            _title.Font = _titleFont;
            _title.Text = "Touch Bar Studio";
            _title.TextAlign = ContentAlignment.BottomLeft;
            titles.Controls.Add(_subtitle);
            titles.Controls.Add(_title);
            _containers.Add(_title);
            _containers.Add(_subtitle);

            var actions = Track(new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0, Theme.S(4), 0, 0)
            });
            var settingsButton = MakeButton("Settings", 100, null);
            settingsButton.Click += (s, e) => ShowSettingsMenu(settingsButton);
            actions.Controls.Add(MakeButton("Send to Touch Bar", 150, (s, e) => { TryConnect(); _device.Invalidate(); RefreshBar(); }, accent: true));
            actions.Controls.Add(MakeButton("Clear", 80, (s, e) => _device.Clear()));
            actions.Controls.Add(settingsButton);

            header.Controls.Add(titles);
            header.Controls.Add(actions);

            // ---- page chips
            var pagesRow = Track(new Panel { Dock = DockStyle.Top, Height = Theme.S(48), Padding = new Padding(Theme.S(24), Theme.S(4), Theme.S(24), 0) });
            _pageBar.Dock = DockStyle.Fill;
            _pageBar.WrapContents = false;
            _pageBar.FlowDirection = FlowDirection.LeftToRight;
            _containers.Add(_pageBar);
            pagesRow.Controls.Add(_pageBar);

            // ---- preview card
            var previewHost = Track(new Panel { Dock = DockStyle.Top, Height = Theme.S(122), Padding = new Padding(Theme.S(24), Theme.S(2), Theme.S(24), Theme.S(12)) });
            _previewCard.Dock = DockStyle.Fill;
            _containers.Add(_previewCard);
            _preview.Dock = DockStyle.Fill;
            _preview.MouseDown += OnPreviewDown;
            _preview.MouseUp += OnPreviewUp;
            _preview.MouseMove += OnPreviewMove;
            _previewCard.Controls.Add(_preview);
            previewHost.Controls.Add(_previewCard);

            // ---- list and properties
            var mainArea = Track(new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(24), 0, Theme.S(24), Theme.S(8)) });
            var table = Track(new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 });
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var listHost = Track(new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, Theme.S(6), 0) });
            var gridHost = Track(new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(6), 0, 0, 0) });

            var listTools = Track(new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Padding = new Padding(0, 0, 0, Theme.S(2))
            });
            var addButton = MakeButton("Add item", 92, null, accent: true);
            addButton.Click += (s, e) => ShowAddMenu(addButton);
            listTools.Controls.Add(addButton);
            listTools.Controls.Add(MakeButton("App...", 70, (s, e) => AddAppDialog()));
            listTools.Controls.Add(MakeButton("Up", 52, (s, e) => MoveItem(-1)));
            listTools.Controls.Add(MakeButton("Down", 60, (s, e) => MoveItem(1)));
            listTools.Controls.Add(MakeButton("Remove", 80, (s, e) => RemoveItem()));

            _list.Dock = DockStyle.Fill;
            _list.BorderStyle = BorderStyle.None;
            _list.IntegralHeight = false;
            _list.DrawMode = DrawMode.OwnerDrawFixed;
            _list.ItemHeight = Theme.S(48);
            _list.DrawItem += OnDrawListItem;
            _list.SelectedIndexChanged += (s, e) =>
            {
                if (!_suppress)
                {
                    _grid.SelectedObject = _list.SelectedItem;
                }
            };
            _list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete)
                {
                    RemoveItem();
                    e.Handled = true;
                }
            };
            _list.DoubleClick += (s, e) =>
            {
                if (_list.SelectedItem is BarItem item)
                {
                    Activate(item);
                }
            };

            _listCard.Dock = DockStyle.Fill;
            _containers.Add(_listCard);
            _listCard.Controls.Add(_list);
            _listCard.Controls.Add(listTools);
            listHost.Controls.Add(_listCard);

            _grid.Dock = DockStyle.Fill;
            _grid.Font = Theme.MakeFont(9f);
            _grid.PropertySort = PropertySort.Categorized;
            _grid.ToolbarVisible = false;
            _grid.PropertyValueChanged += (s, e) =>
            {
                int i = _list.SelectedIndex;
                if (i >= 0)
                {
                    _suppress = true;
                    _list.Items[i] = _list.Items[i]; // redraw the row
                    _suppress = false;
                }

                _renderer.ClearIconCache();
                _cfg.Save();
                RefreshBar();
            };
            _gridCard.Dock = DockStyle.Fill;
            _containers.Add(_gridCard);
            _gridCard.Controls.Add(_grid);
            gridHost.Controls.Add(_gridCard);

            table.Controls.Add(listHost, 0, 0);
            table.Controls.Add(gridHost, 1, 0);
            mainArea.Controls.Add(table);

            // ---- status bar
            var statusBar = Track(new Panel { Dock = DockStyle.Bottom, Height = Theme.S(34), Padding = new Padding(Theme.S(24), 0, Theme.S(24), 0) });
            _statusText.Dock = DockStyle.Fill;
            _statusText.Font = _subFont;
            _statusText.TextAlign = ContentAlignment.MiddleLeft;
            _statusDot.Dock = DockStyle.Left;
            _statusDot.Width = Theme.S(22);
            _statusDot.Font = Theme.MakeFont(10f);
            _statusDot.Text = "●";
            _statusDot.TextAlign = ContentAlignment.MiddleLeft;
            _containers.Add(_statusText);
            _containers.Add(_statusDot);
            statusBar.Controls.Add(_statusText);
            statusBar.Controls.Add(_statusDot);

            // Docking order: the control added last is docked first.
            Controls.Add(mainArea);
            Controls.Add(pagesRow);
            Controls.Add(previewHost);
            Controls.Add(header);
            Controls.Add(statusBar);

            foreach (var c in new Control[] { this, _list, _preview, mainArea, pagesRow, header })
            {
                HookDrop(c);
            }

            HookListReorder();
        }

        void BuildTray()
        {
            _trayMenu.Items.Clear();
            _trayMenu.Items.Add(Loc.T("Open editor"), null, (s, e) => ShowEditor());
            _trayMenu.Items.Add(Loc.T("Restart as administrator"), null, (s, e) => RestartAsAdmin());
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(Loc.T("Exit"), null, (s, e) => ExitApp());

            Theme.StyleMenu(_trayMenu);
            if (_tray.ContextMenuStrip == null)
            {
                _tray.Icon = SystemIcons.Application;
                _tray.Text = "Touch Bar Studio";
                _tray.ContextMenuStrip = _trayMenu;
                _tray.Visible = true;
                _tray.DoubleClick += (s, e) => ShowEditor();
            }
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
                _lastAction = Loc.T("Administrator restart was cancelled");
                UpdateStatus();
            }
        }

        // ---------------------------------------------------------------- menus

        void ShowAddMenu(Control anchor)
        {
            var menu = new ContextMenuStrip();
            Theme.StyleMenu(menu);
            void Add(string text, ItemType type) => menu.Items.Add(text, null, (s, e) => AddItem(type));

            Add(Loc.T("Program or app"), ItemType.App);
            Add(Loc.T("Media key (play, volume...)"), ItemType.Media);
            Add(Loc.T("Keyboard shortcut"), ItemType.Hotkey);
            Add(Loc.T("Command"), ItemType.Command);
            Add(Loc.T("Website"), ItemType.Url);
            Add(Loc.T("Clock"), ItemType.Clock);
            Add(Loc.T("Text label"), ItemType.Text);
            Add(Loc.T("Switch to page"), ItemType.Page);
            Add(Loc.T("Lock screen"), ItemType.LockScreen);
            menu.Items.Add(new ToolStripSeparator());
            Add(Loc.T("Brightness slider"), ItemType.Brightness);
            Add(Loc.T("Volume slider"), ItemType.Volume);
            Add(Loc.T("Media timeline (scrubber)"), ItemType.Scrubber);
            Add(Loc.T("Microphone mute"), ItemType.MicMute);
            Add(Loc.T("Battery level"), ItemType.Battery);
            Add(Loc.T("Clipboard entry"), ItemType.Clipboard);
            Add(Loc.T("Calculator key"), ItemType.Calc);
            Add(Loc.T("Calculator display (tap to copy)"), ItemType.CalcDisplay);
            Add("Wi-Fi", ItemType.Wifi);
            Add("Bluetooth", ItemType.Bluetooth);

            var quick = new ToolStripMenuItem(Loc.T("Quick actions"));
            Theme.StyleMenu(quick.DropDown);
            void Quick(string label, string combo) => quick.DropDownItems.Add(Loc.T(label), null, (s, e) => AddQuickAction(label, combo));
            Quick("Copy", "ctrl+c");
            Quick("Paste", "ctrl+v");
            Quick("Cut", "ctrl+x");
            Quick("Undo", "ctrl+z");
            Quick("Redo", "ctrl+y");
            Quick("Select all", "ctrl+a");
            Quick("Clipboard history", "win+v");
            Quick("Screenshot", "win+shift+s");
            Quick("Task view", "win+tab");
            Quick("Show desktop", "win+d");
            void QuickItem(string label, ItemType type, string url, int width) => quick.DropDownItems.Add(Loc.T(label), null, (s, e) =>
            {
                _page.Items.Add(new BarItem { Type = type, Label = Loc.T(label), Url = url, Width = width });
                FillList(_page.Items.Count - 1);
                _cfg.Save();
                RefreshBar();
            });
            QuickItem("Search selection (Google)", ItemType.Url, "https://www.google.com/search?q={selection}", 330);
            QuickItem("Search clipboard (Bing)", ItemType.Url, "https://www.bing.com/search?q={clipboard}", 330);
            quick.DropDownItems.Add(Loc.T("Clear clipboard history"), null, (s, e) => { ClipboardHistory.Clear(); RefreshBar(); });
            quick.DropDownItems.Add(Loc.T("Notifications settings"), null, (s, e) =>
            {
                _page.Items.Add(new BarItem { Type = ItemType.Url, Label = Loc.T("Notifications settings"), Url = "ms-settings:notifications", Width = 230 });
                FillList(_page.Items.Count - 1);
                _cfg.Save();
                RefreshBar();
            });
            menu.Items.Add(quick);
            menu.Show(anchor, new Point(0, anchor.Height));
        }

        void ShowSettingsMenu(Control anchor)
        {
            var menu = new ContextMenuStrip();
            Theme.StyleMenu(menu);

            var themeItem = new ToolStripMenuItem(Loc.T("Theme"));
            Theme.StyleMenu(themeItem.DropDown);
            foreach (ThemeMode mode in Enum.GetValues(typeof(ThemeMode)))
            {
                var selected = mode;
                string label = selected == ThemeMode.Auto ? Loc.T("Follow Windows") : (selected == ThemeMode.Light ? Loc.T("Light") : Loc.T("Dark"));
                themeItem.DropDownItems.Add(new ToolStripMenuItem(label, null, (s, e) =>
                {
                    _cfg.Theme = selected;
                    _cfg.Save();
                    Theme.Apply(selected);
                })
                { Checked = _cfg.Theme == selected });
            }

            menu.Items.Add(themeItem);

            var languageItem = new ToolStripMenuItem(Loc.T("Language"));
            Theme.StyleMenu(languageItem.DropDown);
            foreach (var entry in new[] { ("auto", Loc.T("Automatic")), ("en", "English"), ("ru", "Русский"), ("ky", "Кыргызча") })
            {
                var code = entry.Item1;
                languageItem.DropDownItems.Add(new ToolStripMenuItem(entry.Item2, null, (s, e) =>
                {
                    _cfg.Language = code;
                    _cfg.Save();
                    Loc.Set(code);
                    ApplyLanguage();
                })
                { Checked = _cfg.Language == code });
            }

            menu.Items.Add(languageItem);
            menu.Items.Add(new ToolStripMenuItem(Loc.T("Start with Windows"), null, (s, e) =>
            {
                bool enable = !IsAutoStart();
                SetAutoStart(enable);
                _cfg.AutoStart = enable;
                _cfg.Save();
            })
            { Checked = IsAutoStart() });
            menu.Items.Add(new ToolStripMenuItem(Loc.T("Start minimized to tray"), null, (s, e) =>
            {
                _cfg.StartMinimized = !_cfg.StartMinimized;
                _cfg.Save();
            })
            { Checked = _cfg.StartMinimized });
            menu.Items.Add(new ToolStripMenuItem(Loc.T("Switch pages by active program"), null, (s, e) =>
            {
                _cfg.AutoSwitchPages = !_cfg.AutoSwitchPages;
                _cfg.Save();
            })
            { Checked = _cfg.AutoSwitchPages });
            menu.Items.Add(Loc.T("Add preset pages"), null, (s, e) =>
            {
                if (_cfg.AddPresetPages() > 0)
                {
                    _cfg.Save();
                    RebuildPageBar();
                }
            });
            menu.Items.Add(Loc.T("Bar background color..."), null, (s, e) =>
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
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Loc.T("Restart as administrator"), null, (s, e) => RestartAsAdmin());
            menu.Items.Add(Loc.T("Open config folder"), null, (s, e) =>
            {
                try
                {
                    Directory.CreateDirectory(AppConfig.Dir);
                    Process.Start(new ProcessStartInfo(AppConfig.Dir) { UseShellExecute = true });
                }
                catch
                {
                }
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Loc.T("About"), null, (s, e) => MessageBox.Show(this,
                "Touch Bar Studio " + typeof(MainForm).Assembly.GetName().Version + "\n\n" +
                Loc.T("Driver: DFRDisplayKm by Bingxing Wang (MIT license).") + "\n" +
                Loc.T("This fork and app: Aibek Malikov."),
                Loc.T("About"), MessageBoxButtons.OK, MessageBoxIcon.Information));
            menu.Show(anchor, new Point(0, anchor.Height));
        }

        void ShowPageMenu(Control chip, BarPage page)
        {
            var menu = new ContextMenuStrip();
            Theme.StyleMenu(menu);
            menu.Items.Add(Loc.T("Rename..."), null, (s, e) => RenamePage(page));
            menu.Items.Add(Loc.T("Auto-switch for programs..."), null, (s, e) => EditPageProcesses(page));
            menu.Items.Add(Loc.T("Delete"), null, (s, e) => DeletePage(page));
            menu.Show(chip, new Point(0, chip.Height));
        }

        // ---------------------------------------------------------------- pages and items

        ModernButton MakeChip(string text, bool selected, int fixedWidth = 0)
        {
            int width = fixedWidth > 0 ? Theme.S(fixedWidth) : TextRenderer.MeasureText(text, Theme.UiFont).Width + Theme.S(32);
            return new ModernButton
            {
                Text = text,
                Accent = selected,
                Width = width,
                Height = Theme.S(32),
                Margin = new Padding(0, Theme.S(2), Theme.S(8), Theme.S(2))
            };
        }

        void RebuildPageBar()
        {
            _pageBar.SuspendLayout();
            foreach (var old in _pageBar.Controls.Cast<Control>().ToList())
            {
                _pageBar.Controls.Remove(old);
                old.Dispose();
            }

            foreach (var page in _cfg.Pages)
            {
                var target = page;
                var chip = MakeChip(target.Name, target == _page);
                chip.Click += (s, e) =>
                {
                    if (target != _page) SelectPage(target);
                };
                chip.MouseUp += (s, e) =>
                {
                    if (e.Button == MouseButtons.Right) ShowPageMenu(chip, target);
                };
                _pageBar.Controls.Add(chip);
            }

            var add = MakeChip("+", false, 44);
            add.Click += (s, e) => NewPage();
            _pageBar.Controls.Add(add);
            _pageBar.ResumeLayout();
        }

        void RebuildPageBarDeferred()
        {
            // The click that triggered this comes from a chip that is about to be replaced.
            if (IsHandleCreated)
            {
                BeginInvoke(new Action(RebuildPageBar));
            }
            else
            {
                RebuildPageBar();
            }
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
            FillList(_page.Items.Count > 0 ? 0 : -1);
            RebuildPageBarDeferred();
            RefreshBar();
        }

        void GoToPage(string name)
        {
            var page = _cfg.Pages.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (page == null)
            {
                throw new InvalidOperationException(Loc.F("No page named \"{0}\"", name));
            }

            SelectPage(page);
        }

        void NewPage()
        {
            string name = Prompt(Loc.T("New page"), Loc.T("Page name"), Loc.T("Page") + " " + (_cfg.Pages.Count + 1));
            if (string.IsNullOrEmpty(name)) return;

            if (_cfg.Pages.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, Loc.T("A page with this name already exists."));
                return;
            }

            var page = new BarPage { Name = name };
            _cfg.Pages.Add(page);
            _cfg.Save();
            SelectPage(page);
        }

        void RenamePage(BarPage page)
        {
            string old = page.Name;
            string name = Prompt(Loc.T("Rename page"), Loc.T("Page name"), old);
            if (string.IsNullOrEmpty(name) || name == old) return;

            if (_cfg.Pages.Any(p => p != page && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, Loc.T("A page with this name already exists."));
                return;
            }

            page.Name = name;
            foreach (var item in _cfg.Pages.SelectMany(p => p.Items))
            {
                if (item.Type == ItemType.Page && string.Equals(item.TargetPage, old, StringComparison.OrdinalIgnoreCase))
                {
                    item.TargetPage = name;
                }
            }

            _cfg.Save();
            RebuildPageBarDeferred();
            RefreshBar();
        }

        void DeletePage(BarPage page)
        {
            if (_cfg.Pages.Count <= 1)
            {
                MessageBox.Show(this, Loc.T("At least one page is required."));
                return;
            }

            if (MessageBox.Show(this, Loc.F("Delete page \"{0}\"?", page.Name), "Touch Bar Studio",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            _cfg.Pages.Remove(page);
            _cfg.Save();
            if (page == _page)
            {
                SelectPage(_cfg.Pages[0]);
            }
            else
            {
                RebuildPageBarDeferred();
            }
        }

        void AddItem(ItemType type)
        {
            var item = new BarItem { Type = type, Label = type.ToString() };
            switch (type)
            {
                case ItemType.App: item.Label = Loc.T("App"); item.Width = 190; break;
                case ItemType.Media: item.Label = Loc.T("Play/Pause"); item.Width = 120; break;
                case ItemType.Hotkey: item.Label = Loc.T("Shortcut"); item.Hotkey = "ctrl+c"; break;
                case ItemType.Command: item.Label = Loc.T("Command"); break;
                case ItemType.Url: item.Label = Loc.T("Web"); item.Url = "https://"; break;
                case ItemType.Clock: item.Label = ""; item.Width = 220; item.Align = ItemAlign.Right; break;
                case ItemType.Text: item.Label = Loc.T("Text"); break;
                case ItemType.Page: item.Label = Loc.T("Page"); item.TargetPage = _page.Name; break;
                case ItemType.LockScreen: item.Label = Loc.T("Lock"); item.Width = 110; break;
                case ItemType.Brightness: item.Label = ""; item.Width = 380; break;
                case ItemType.Volume: item.Label = ""; item.Width = 380; break;
                case ItemType.Scrubber: item.Label = ""; item.Width = 1000; break;
                case ItemType.MicMute: item.Label = Loc.T("Mic"); item.Width = 150; break;
                case ItemType.Battery: item.Label = ""; item.Width = 260; break;
                case ItemType.Clipboard: item.Label = ""; item.Width = 300; break;
                case ItemType.Calc: item.Label = "7"; item.Width = 72; break;
                case ItemType.CalcDisplay: item.Label = ""; item.Width = 300; break;
                case ItemType.Wifi: item.Label = ""; item.Width = 200; break;
                case ItemType.Bluetooth: item.Label = ""; item.Width = 230; break;
            }

            _page.Items.Add(item);
            FillList(_page.Items.Count - 1);
            _cfg.Save();
            RefreshBar();
        }

        void AddQuickAction(string label, string combo)
        {
            _page.Items.Add(new BarItem { Type = ItemType.Hotkey, Label = Loc.T(label), Hotkey = combo, Width = Math.Max(120, TextRenderer.MeasureText(Loc.T(label), Theme.UiFont).Width + 60) });
            FillList(_page.Items.Count - 1);
            _cfg.Save();
            RefreshBar();
        }

        void AddApps(IEnumerable<string> files)
        {
            int added = 0;
            foreach (var file in files)
            {
                if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) continue;

                _page.Items.Add(new BarItem
                {
                    Type = ItemType.App,
                    Label = System.IO.Path.GetFileNameWithoutExtension(file),
                    Path = file,
                    Width = 190
                });
                added++;
            }

            if (added > 0)
            {
                FillList(_page.Items.Count - 1);
                _cfg.Save();
                RefreshBar();
            }
        }

        void AddAppDialog()
        {
            using (var dialog = new OpenFileDialog
            {
                Title = Loc.T("Choose programs"),
                Filter = Loc.T("Programs and shortcuts|*.exe;*.lnk;*.bat;*.cmd|All files|*.*"),
                Multiselect = true
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    AddApps(dialog.FileNames);
                }
            }
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

        const string ReorderFormat = "TouchBarStudio.ItemIndex";
        Point _listDragStart;
        int _listDragIndex = -1;

        void MoveItemTo(int from, int to)
        {
            if (from < 0 || from >= _page.Items.Count || to < 0 || to >= _page.Items.Count || from == to) return;

            var item = _page.Items[from];
            _page.Items.RemoveAt(from);
            _page.Items.Insert(to, item);
            FillList(to);
            _cfg.Save();
            RefreshBar();
        }

        void HookListReorder()
        {
            _list.MouseDown += (s, e) =>
            {
                _listDragStart = e.Location;
                _listDragIndex = e.Button == MouseButtons.Left ? _list.IndexFromPoint(e.Location) : -1;
            };
            _list.MouseUp += (s, e) => _listDragIndex = -1;
            _list.MouseMove += (s, e) =>
            {
                if (e.Button == MouseButtons.Left && _listDragIndex >= 0 &&
                    (Math.Abs(e.X - _listDragStart.X) > 8 || Math.Abs(e.Y - _listDragStart.Y) > 8))
                {
                    int index = _listDragIndex;
                    _listDragIndex = -1;
                    _list.DoDragDrop(new DataObject(ReorderFormat, index), DragDropEffects.Move);
                }
            };
        }

        void HookDrop(Control control)
        {
            control.AllowDrop = true;
            control.DragEnter += (s, e) =>
            {
                if (e.Data != null && e.Data.GetDataPresent(ReorderFormat))
                    e.Effect = control == _list ? DragDropEffects.Move : DragDropEffects.None;
                else
                    e.Effect = e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            };
            control.DragOver += (s, e) =>
            {
                if (e.Data != null && e.Data.GetDataPresent(ReorderFormat))
                    e.Effect = control == _list ? DragDropEffects.Move : DragDropEffects.None;
            };
            control.DragDrop += (s, e) =>
            {
                if (e.Data != null && e.Data.GetDataPresent(ReorderFormat))
                {
                    if (control == _list && e.Data.GetData(ReorderFormat) is int from)
                    {
                        int to = _list.IndexFromPoint(_list.PointToClient(new Point(e.X, e.Y)));
                        if (to < 0) to = _list.Items.Count - 1;
                        MoveItemTo(from, to);
                    }

                    return;
                }

                if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
                {
                    AddApps(files);
                }
            };
        }

        static string Describe(BarItem item)
        {
            string notSet = Loc.T("not set");
            switch (item.Type)
            {
                case ItemType.App:
                    return string.IsNullOrWhiteSpace(item.Path) ? Loc.T("Program: not set") : System.IO.Path.GetFileName(item.Path);
                case ItemType.Media: return Loc.F("Media: {0}", item.Media);
                case ItemType.Hotkey: return Loc.F("Shortcut: {0}", string.IsNullOrWhiteSpace(item.Hotkey) ? notSet : item.Hotkey);
                case ItemType.Command: return Loc.F("Command: {0}", string.IsNullOrWhiteSpace(item.Command) ? notSet : item.Command);
                case ItemType.Url: return string.IsNullOrWhiteSpace(item.Url) ? Loc.T("Website: not set") : item.Url;
                case ItemType.Clock: return Loc.F("Clock: {0}", item.ClockFormat);
                case ItemType.Text: return Loc.T("Text label");
                case ItemType.Page: return Loc.F("Open page: {0}", item.TargetPage);
                case ItemType.LockScreen: return Loc.T("Lock screen");
                case ItemType.Brightness: return Loc.T("Brightness slider");
                case ItemType.Volume: return Loc.T("Volume slider");
                case ItemType.Scrubber: return Loc.T("Media timeline (scrubber)");
                case ItemType.MicMute: return Loc.T("Microphone mute");
                case ItemType.Battery: return Loc.T("Battery level");
                case ItemType.Clipboard: return Loc.T("Clipboard entry") + " " + item.Slot;
                case ItemType.Calc: return Loc.T("Calculator key") + " " + item.Label;
                case ItemType.CalcDisplay: return Loc.T("Calculator display (tap to copy)");
                case ItemType.Wifi: return "Wi-Fi";
                case ItemType.Bluetooth: return "Bluetooth";
                default: return item.Type.ToString();
            }
        }

        void OnDrawListItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _list.Items.Count) return;

            var item = (BarItem)_list.Items[e.Index];
            var g = e.Graphics;
            var p = Theme.P;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var back = new SolidBrush(p.Surface))
            {
                g.FillRectangle(back, e.Bounds);
            }

            var row = Rectangle.Inflate(e.Bounds, -Theme.S(4), -Theme.S(2));
            bool selected = (e.State & DrawItemState.Selected) != 0;
            if (selected)
            {
                using (var path = Theme.RoundedPath(row, Theme.S(6)))
                using (var brush = new SolidBrush(p.Hover))
                using (var accent = new SolidBrush(p.Accent))
                {
                    g.FillPath(brush, path);
                    var bar = new RectangleF(row.X + Theme.S(2), row.Y + (row.Height - Theme.S(18)) / 2f, Theme.S(3), Theme.S(18));
                    using (var barPath = Theme.RoundedPath(bar, 1.5f))
                    {
                        g.FillPath(accent, barPath);
                    }
                }
            }

            int tile = Theme.S(30);
            var tileRect = new Rectangle(row.X + Theme.S(12), row.Y + (row.Height - tile) / 2, tile, tile);
            var icon = _renderer.IconFor(item);
            if (icon != null)
            {
                g.DrawImage(icon, tileRect);
            }
            else
            {
                using (var path = Theme.RoundedPath(tileRect, Theme.S(6)))
                using (var brush = new SolidBrush(item.Background))
                {
                    g.FillPath(brush, path);
                }

                string letter = string.IsNullOrEmpty(item.Label) ? item.Type.ToString().Substring(0, 1) : item.Label.Substring(0, 1).ToUpperInvariant();
                TextRenderer.DrawText(g, letter, _itemTitleFont, tileRect, item.ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            int textX = tileRect.Right + Theme.S(12);
            int textWidth = row.Right - textX - Theme.S(8);
            var titleRect = new Rectangle(textX, row.Y + Theme.S(5), textWidth, Theme.S(20));
            var subRect = new Rectangle(textX, row.Y + Theme.S(24), textWidth, Theme.S(18));
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                                          TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

            string title = string.IsNullOrEmpty(item.Label) ? item.Type.ToString() : item.Label;
            TextRenderer.DrawText(g, title, _itemTitleFont, titleRect, p.Text, flags);
            TextRenderer.DrawText(g, Describe(item), _itemSubFont, subRect, p.SubText, flags);
        }

        // ---------------------------------------------------------------- drawing

        string ClockSignature()
        {
            return string.Join("|", _page.Items.Where(i => i.Type == ItemType.Clock).Select(Renderer.ClockText));
        }

        void RefreshBar()
        {
            _tiles = _renderer.Layout(_page);
            var frame = _renderer.Render(_cfg, _tiles, _pressed);

            // While the window is hidden in the tray there is nobody to show the preview to.
            if (Visible)
            {
                _preview.Frame = frame;
                _preview.Invalidate();
            }

            if (_device.IsOpen && !_device.SendFrame(frame))
            {
                // Probably unplugged or the driver was reloaded: reopen on the next retry.
                _device.Close();
            }

            _lastClock = ClockSignature();
            UpdateStatus();
        }

        void UpdateStatus()
        {
            var p = Theme.P;
            _statusDot.ForeColor = _device.IsOpen
                ? (p.Dark ? Color.FromArgb(0x6C, 0xCB, 0x5F) : Color.FromArgb(0x10, 0x7C, 0x10))
                : (p.Dark ? Color.FromArgb(0xFF, 0x99, 0xA4) : Color.FromArgb(0xC4, 0x2B, 0x1C));

            _statusText.Text = Loc.F("Display: {0}", _device.IsOpen ? Loc.T("connected") : _device.Error) +
                               "     " + Loc.F("Touch: {0}", Loc.T(_touchState)) +
                               (string.IsNullOrEmpty(_lastAction) ? "" : "     " + _lastAction);
        }

        void OnTick(object sender, EventArgs e)
        {
            _tick++;
            bool redraw = ClockSignature() != _lastClock;

            bool hasAudio = _page.Items.Any(i => i.Type == ItemType.Volume || i.Type == ItemType.MicMute);
            if (SystemState.PollAudio() && hasAudio && _drag < 0)
            {
                redraw = true;
            }

            if (_tick % 5 == 0 && _page.Items.Any(i => i.Type == ItemType.Brightness) && _drag < 0)
            {
                _ = SystemControls.RefreshBrightnessAsync();
            }

            if (_tick % 10 == 0 && _page.Items.Any(i => i.Type == ItemType.Wifi || i.Type == ItemType.Bluetooth))
            {
                _ = RadioControl.RefreshAsync().ContinueWith(t => OnUi(RefreshBar));
            }

            if (_tick % 30 == 0 && _page.Items.Any(i => i.Type == ItemType.Battery))
            {
                redraw = true;
            }

            var media = SystemState.Media;
            if (media != null && media.HasSession && media.Playing && _page.Items.Any(i => i.Type == ItemType.Scrubber) && _drag < 0)
            {
                redraw = true; // timeline moves once per second
            }

            if (redraw)
            {
                RefreshBar();
            }

            if (_cfg.AutoSwitchPages)
            {
                AutoSwitchPage();
            }

            if (_tick % 5 == 0)
            {
                TryConnect();
            }
        }

        void StartSystemServices()
        {
            try
            {
                var media = new MediaService();
                media.Changed += () => OnUi(() =>
                {
                    if (_drag < 0 && _page != null && _page.Items.Any(i => i.Type == ItemType.Scrubber))
                    {
                        RefreshBar();
                    }
                });
                SystemState.Media = media;
                _ = media.StartAsync();
            }
            catch
            {
                // Media session API unavailable: the timeline tile will say that nothing is playing.
            }

            SystemState.PollAudio();
            _ = SystemControls.RefreshBrightnessAsync();
            _ = RadioControl.RefreshAsync();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        static string ForegroundProcessName()
        {
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return "";
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0) return "";
                using (var process = Process.GetProcessById((int)pid))
                {
                    return process.ProcessName ?? "";
                }
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Context presets: shows the page whose program list contains the active window's program,
        /// and returns to the previous page when no page matches. TouchBarStudio itself is ignored.
        /// </summary>
        void AutoSwitchPage()
        {
            string name = ForegroundProcessName();
            if (name.Length == 0 || name == _lastForeground) return;
            _lastForeground = name;

            if (string.Equals(name, System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? ""), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var match = _cfg.Pages.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Processes) &&
                p.Processes.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(x => string.Equals(x.Trim().Replace(".exe", ""), name, StringComparison.OrdinalIgnoreCase)));

            if (match != null)
            {
                if (match != _page)
                {
                    if (_homePage == null) _homePage = _page;
                    SelectPage(match);
                }
            }
            else if (_homePage != null)
            {
                var home = _homePage;
                _homePage = null;
                if (_cfg.Pages.Contains(home)) SelectPage(home);
            }
        }

        void EditPageProcesses(BarPage page)
        {
            string value = Prompt(Loc.T("Programs for this page"),
                Loc.T("Program names separated by commas, for example: chrome, spotify. Leave empty to turn off."),
                page.Processes ?? "");
            if (value == null) return;

            page.Processes = value;
            _cfg.Save();
        }

        void ApplyLanguage()
        {
            _subtitle.Text = Loc.T("Tap a button in the preview to try it. Drop a program onto the window to add it.");
            foreach (var button in AllButtons(this))
            {
                if (button.Tag is string key)
                {
                    button.Text = Loc.T(key);
                    button.Width = Math.Max(button.Width, TextRenderer.MeasureText(button.Text, Theme.UiFont).Width + Theme.S(28));
                }
            }

            BuildTray();
            TypeDescriptor.Refresh(typeof(BarItem));
            var selected = _grid.SelectedObject;
            _grid.SelectedObject = null;
            _grid.SelectedObject = selected;
            _list.Invalidate();
            RebuildPageBar();
            UpdateStatus();
        }

        static IEnumerable<ModernButton> AllButtons(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is ModernButton b) yield return b;
                foreach (var inner in AllButtons(c)) yield return inner;
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

        static bool IsDraggable(BarItem item)
        {
            return item.Type == ItemType.Brightness || item.Type == ItemType.Volume || item.Type == ItemType.Scrubber;
        }

        void TouchDown(double fraction) => PressAt(BarX(fraction));

        void TouchMove(double fraction) => MoveTo(BarX(fraction));

        void TouchUp(double fraction) => ReleaseAt(BarX(fraction));

        void PressAt(int x)
        {
            int hit = x < 0 ? -1 : HitTest(x);
            if (hit >= 0 && IsDraggable(_tiles[hit].Item))
            {
                _drag = hit;
                _pressed = -1;
                ApplyDrag(x, false);
                return;
            }

            _pressed = hit;
            RefreshBar();
        }

        void MoveTo(int x)
        {
            if (_drag >= 0)
            {
                ApplyDrag(x, false);
                return;
            }

            if (_pressed >= 0 && HitTest(x) != _pressed)
            {
                _pressed = -1;
                RefreshBar();
            }
        }

        void ReleaseAt(int x)
        {
            if (_drag >= 0)
            {
                ApplyDrag(x, true);
                _drag = -1;
                return;
            }

            FinishPress(x < 0 ? -1 : HitTest(x));
        }

        /// <summary>Sets the value of the slider or timeline under the finger. The finger may leave the tile; the value is clamped.</summary>
        void ApplyDrag(int x, bool final)
        {
            if (_drag < 0 || _drag >= _tiles.Count) return;

            var tile = _tiles[_drag];
            double f = Math.Max(0.0, Math.Min(1.0, (x - tile.Rect.X) / (double)tile.Rect.Width));
            try
            {
                switch (tile.Item.Type)
                {
                    case ItemType.Volume:
                        SystemControls.SetVolume(f);
                        break;
                    case ItemType.Brightness:
                        if (SystemState.BrightnessSupported)
                        {
                            SystemControls.SetBrightness((int)Math.Round(f * 100));
                        }
                        else
                        {
                            _lastAction = Loc.T("Brightness control is not available on this display");
                        }

                        break;
                    case ItemType.Scrubber:
                        var media = SystemState.Media;
                        if (media != null && media.CanSeek)
                        {
                            SystemState.ScrubFraction = f;
                            if (final)
                            {
                                double target = f * media.Duration;
                                SystemState.ScrubFraction = -1;
                                _ = media.SeekAsync(target);
                            }
                        }

                        break;
                }
            }
            catch (Exception ex)
            {
                _lastAction = Loc.F("Error: {0}", ex.Message);
            }

            if (final && tile.Item.Type != ItemType.Scrubber)
            {
                SystemState.PollAudio();
            }

            RefreshBar();
        }

        void OnPreviewDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            PressAt(_preview.ToBarX(e.Location));
        }

        void OnPreviewMove(object sender, MouseEventArgs e)
        {
            if (_drag >= 0 && e.Button == MouseButtons.Left)
            {
                MoveTo(_preview.ToBarX(e.Location, true));
            }
        }

        void OnPreviewUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseAt(_drag >= 0 ? _preview.ToBarX(e.Location, true) : _preview.ToBarX(e.Location));
        }

        void FinishPress(int hit)
        {
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
                    _lastAction = Loc.F("Page: {0}", item.TargetPage);
                }
                else
                {
                    Actions.Run(item);
                    _lastAction = Loc.F("Ran: {0}", item);
                }
            }
            catch (Exception ex)
            {
                _lastAction = Loc.F("Error: {0}", ex.Message);
            }

            RefreshBar();
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
            var p = Theme.P;
            using (var form = new Form())
            {
                form.Text = title;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.ShowInTaskbar = false;
                form.Font = Theme.UiFont;
                form.BackColor = p.Window;
                form.ForeColor = p.Text;
                form.ClientSize = new Size(Theme.S(380), Theme.S(150));
                form.HandleCreated += (s, e) => Theme.StyleWindow(form);

                var text = new Label { Text = label, Left = Theme.S(20), Top = Theme.S(18), Width = Theme.S(340), BackColor = p.Window, ForeColor = p.SubText };
                var box = new TextBox
                {
                    Text = initial,
                    Left = Theme.S(20),
                    Top = Theme.S(46),
                    Width = Theme.S(340),
                    BackColor = p.Surface,
                    ForeColor = p.Text,
                    BorderStyle = BorderStyle.FixedSingle
                };
                var ok = new ModernButton { Text = Loc.T("OK"), Accent = true, Left = Theme.S(190), Top = Theme.S(96), Width = Theme.S(80) };
                var cancel = new ModernButton { Text = Loc.T("Cancel"), Left = Theme.S(280), Top = Theme.S(96), Width = Theme.S(80) };
                ok.Click += (s, e) => form.DialogResult = DialogResult.OK;
                cancel.Click += (s, e) => form.DialogResult = DialogResult.Cancel;
                box.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter) { form.DialogResult = DialogResult.OK; e.SuppressKeyPress = true; }
                    else if (e.KeyCode == Keys.Escape) { form.DialogResult = DialogResult.Cancel; e.SuppressKeyPress = true; }
                };
                form.Controls.AddRange(new Control[] { text, box, ok, cancel });
                form.Shown += (s, e) => { box.Focus(); box.SelectAll(); };

                return form.ShowDialog(this) == DialogResult.OK ? box.Text.Trim() : null;
            }
        }
    }
}
