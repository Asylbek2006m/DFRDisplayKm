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

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.StyleWindow(this);
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
            var button = new ModernButton { Text = text, Width = Theme.S(width), Accent = accent };
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
            _subtitle.Text = "Tap a button in the preview to try it. Drop a program onto the window to add it.";
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
        }

        void BuildTray()
        {
            _trayMenu.Items.Add("Open editor", null, (s, e) => ShowEditor());
            _trayMenu.Items.Add("Restart as administrator", null, (s, e) => RestartAsAdmin());
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Exit", null, (s, e) => ExitApp());

            _tray.Icon = SystemIcons.Application;
            _tray.Text = "Touch Bar Studio";
            _tray.ContextMenuStrip = _trayMenu;
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

        // ---------------------------------------------------------------- menus

        void ShowAddMenu(Control anchor)
        {
            var menu = new ContextMenuStrip();
            Theme.StyleMenu(menu);
            void Add(string text, ItemType type) => menu.Items.Add(text, null, (s, e) => AddItem(type));

            Add("Program or app", ItemType.App);
            Add("Media key (play, volume...)", ItemType.Media);
            Add("Keyboard shortcut", ItemType.Hotkey);
            Add("Command", ItemType.Command);
            Add("Website", ItemType.Url);
            Add("Clock", ItemType.Clock);
            Add("Text label", ItemType.Text);
            Add("Switch to page", ItemType.Page);
            Add("Lock screen", ItemType.LockScreen);
            menu.Show(anchor, new Point(0, anchor.Height));
        }

        void ShowSettingsMenu(Control anchor)
        {
            var menu = new ContextMenuStrip();
            Theme.StyleMenu(menu);

            var themeItem = new ToolStripMenuItem("Theme");
            Theme.StyleMenu(themeItem.DropDown);
            foreach (ThemeMode mode in Enum.GetValues(typeof(ThemeMode)))
            {
                var selected = mode;
                string label = selected == ThemeMode.Auto ? "Follow Windows" : selected.ToString();
                themeItem.DropDownItems.Add(new ToolStripMenuItem(label, null, (s, e) =>
                {
                    _cfg.Theme = selected;
                    _cfg.Save();
                    Theme.Apply(selected);
                })
                { Checked = _cfg.Theme == selected });
            }

            menu.Items.Add(themeItem);
            menu.Items.Add(new ToolStripMenuItem("Start with Windows", null, (s, e) =>
            {
                bool enable = !IsAutoStart();
                SetAutoStart(enable);
                _cfg.AutoStart = enable;
                _cfg.Save();
            })
            { Checked = IsAutoStart() });
            menu.Items.Add(new ToolStripMenuItem("Start minimized to tray", null, (s, e) =>
            {
                _cfg.StartMinimized = !_cfg.StartMinimized;
                _cfg.Save();
            })
            { Checked = _cfg.StartMinimized });
            menu.Items.Add("Bar background color...", null, (s, e) =>
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
            menu.Items.Add("Restart as administrator", null, (s, e) => RestartAsAdmin());
            menu.Items.Add("Open config folder", null, (s, e) =>
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
            menu.Items.Add("About", null, (s, e) => MessageBox.Show(this,
                "Touch Bar Studio " + typeof(MainForm).Assembly.GetName().Version + "\n\n" +
                "Driver: DFRDisplayKm by Bingxing Wang (MIT license).\n" +
                "This fork and app: Aibek Malikov.",
                "About", MessageBoxButtons.OK, MessageBoxIcon.Information));
            menu.Show(anchor, new Point(0, anchor.Height));
        }

        void ShowPageMenu(Control chip, BarPage page)
        {
            var menu = new ContextMenuStrip();
            Theme.StyleMenu(menu);
            menu.Items.Add("Rename...", null, (s, e) => RenamePage(page));
            menu.Items.Add("Delete", null, (s, e) => DeletePage(page));
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
                throw new InvalidOperationException("No page named \"" + name + "\"");
            }

            SelectPage(page);
        }

        void NewPage()
        {
            string name = Prompt("New page", "Page name", "Page " + (_cfg.Pages.Count + 1));
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

        void RenamePage(BarPage page)
        {
            string old = page.Name;
            string name = Prompt("Rename page", "Page name", old);
            if (string.IsNullOrEmpty(name) || name == old) return;

            if (_cfg.Pages.Any(p => p != page && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, "A page with this name already exists.");
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
                MessageBox.Show(this, "At least one page is required.");
                return;
            }

            if (MessageBox.Show(this, "Delete page \"" + page.Name + "\"?", "Touch Bar Studio",
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
                case ItemType.App: item.Label = "App"; item.Width = 190; break;
                case ItemType.Media: item.Label = "Play/Pause"; item.Width = 120; break;
                case ItemType.Hotkey: item.Label = "Shortcut"; item.Hotkey = "ctrl+c"; break;
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
                Title = "Choose programs",
                Filter = "Programs and shortcuts|*.exe;*.lnk;*.bat;*.cmd|All files|*.*",
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

        void HookDrop(Control control)
        {
            control.AllowDrop = true;
            control.DragEnter += (s, e) =>
                e.Effect = e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            control.DragDrop += (s, e) =>
            {
                if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
                {
                    AddApps(files);
                }
            };
        }

        static string Describe(BarItem item)
        {
            switch (item.Type)
            {
                case ItemType.App:
                    return string.IsNullOrWhiteSpace(item.Path) ? "Program: not set" : System.IO.Path.GetFileName(item.Path);
                case ItemType.Media: return "Media: " + item.Media;
                case ItemType.Hotkey: return "Shortcut: " + (string.IsNullOrWhiteSpace(item.Hotkey) ? "not set" : item.Hotkey);
                case ItemType.Command: return "Command: " + (string.IsNullOrWhiteSpace(item.Command) ? "not set" : item.Command);
                case ItemType.Url: return string.IsNullOrWhiteSpace(item.Url) ? "Website: not set" : item.Url;
                case ItemType.Clock: return "Clock: " + item.ClockFormat;
                case ItemType.Text: return "Text label";
                case ItemType.Page: return "Open page: " + item.TargetPage;
                case ItemType.LockScreen: return "Lock screen";
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

            _statusText.Text = "Display: " + (_device.IsOpen ? "connected" : _device.Error) +
                               "     Touch: " + _touchState +
                               (string.IsNullOrEmpty(_lastAction) ? "" : "     " + _lastAction);
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
            FinishPress(HitTest(BarX(fraction)));
        }

        void OnPreviewDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int x = _preview.ToBarX(e.Location);
            _pressed = x < 0 ? -1 : HitTest(x);
            RefreshBar();
        }

        void OnPreviewUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int x = _preview.ToBarX(e.Location);
            FinishPress(x < 0 ? -1 : HitTest(x));
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
                var ok = new ModernButton { Text = "OK", Accent = true, Left = Theme.S(190), Top = Theme.S(96), Width = Theme.S(80) };
                var cancel = new ModernButton { Text = "Cancel", Left = Theme.S(280), Top = Theme.S(96), Width = Theme.S(80) };
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
