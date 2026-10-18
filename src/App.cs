using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

[assembly: AssemblyTitle("轻周待办")]
[assembly: AssemblyProduct("LightWeekTodo")]
[assembly: AssemblyVersion("1.0.5.0")]

namespace LightWeekTodo
{
    public static class Program
    {
        public const string Product = "LightWeekTodo";
        public static readonly string DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Product);
        public static readonly uint ShowMessage = RegisterWindowMessage("LightWeekTodo.Show.30A7C2CF");
        public static readonly uint QuitMessage = RegisterWindowMessage("LightWeekTodo.Quit.30A7C2CF");
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
        [STAThread] public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-test") return SelfTest.Run(args.Length > 1 ? args[1] : "test-output");
            if (args.Contains("--quit")) { PostMessage(new IntPtr(0xffff), QuitMessage, IntPtr.Zero, IntPtr.Zero); return 0; }
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\LightWeekTodo.30A7C2CF." + Environment.UserName, out created))
            {
                if (!created) { PostMessage(new IntPtr(0xffff), ShowMessage, IntPtr.Zero, IntPtr.Zero); return 0; }
                try
                {
                    Application app = new Application();
                    app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
                    {
                        try { Directory.CreateDirectory(DataFolder); File.AppendAllText(Path.Combine(DataFolder, "error.log"), DateTime.Now + " " + e.Exception + Environment.NewLine); } catch { }
                        MessageBox.Show("发生了错误。请重新打开轻周待办。\n已保存的清单会保留。\n\n" + e.Exception.Message, "轻周待办");
                        e.Handled = true;
                    };
                    Store store = new Store(DataFolder);
                    Widget widget = new Widget(store, store.Load(), false);
                    app.Run(widget);
                    return 0;
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "轻周待办无法启动", MessageBoxButton.OK, MessageBoxImage.Error); return 1; }
                finally { mutex.ReleaseMutex(); }
            }
        }
        public static bool StartupEnabled
        {
            get { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) return key != null && key.GetValue(Product) != null; }
        }
        public static void SetStartup(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (enabled) key.SetValue(Product, "\"" + Assembly.GetExecutingAssembly().Location + "\" --startup");
                else key.DeleteValue(Product, false);
            }
        }
    }
    public class Widget : Window
    {
        readonly Store store;
        internal readonly State state;
        readonly bool testing;
        internal DateTime week = Dates.Monday(DateTime.Today);
        DateTime todayWeek = Dates.Monday(DateTime.Today);
        readonly Border panel;
        readonly Grid header;
        readonly StackPanel rows;
        readonly TextBlock dateLabel, progressLabel, titleLabel;
        readonly Border progressFill;
        readonly DispatcherTimer saveTimer;
        DispatcherTimer dateTimer;
        Forms.NotifyIcon tray;
        bool exiting, ready;
        static readonly Brush White = Brushes.White;
        static readonly Brush Dim = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));
        static readonly Brush Line = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255));
        public Widget(Store storage, State initial, bool test)
        {
            store = storage; state = initial; testing = test;
            Title = "轻周待办"; Width = state.Width; Height = state.Height;
            MinWidth = 320; MinHeight = 340; MaxWidth = 900; MaxHeight = 1400;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = state.Pinned;
            FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 14; Foreground = White;
            UseLayoutRounding = true; TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            using (Stream iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("icon.ico"))
                if (iconStream != null) Icon = BitmapFrame.Create(iconStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Resources.Add(typeof(Button), ButtonStyle());
            Resources.Add(typeof(CheckBox), CheckStyle());
            Resources.Add(typeof(ScrollBar), ScrollbarStyle());
            panel = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(22, 17, 22, 14), Background = BackdropBrush() };
            Content = panel;
            Grid root = new Grid(); panel.Child = root;
            foreach (GridLength height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
                root.RowDefinitions.Add(new RowDefinition { Height = height });
            Grid head = new Grid { Margin = new Thickness(0, 0, 0, 17), Background = Brushes.Transparent, Cursor = state.PositionLocked ? Cursors.Arrow : Cursors.SizeAll };
            header = head;
            head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel brand = new StackPanel();
            TextBlock eyebrow = Text("轻周  /  WEEKLY", 10, Dim); eyebrow.FontWeight = FontWeights.Medium; eyebrow.Margin = new Thickness(0, 0, 0, 6); brand.Children.Add(eyebrow);
            titleLabel = Text("本周待办", 25, White); titleLabel.FontWeight = FontWeights.SemiBold; brand.Children.Add(titleLabel); head.Children.Add(brand);
            StackPanel tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            Button settings = Button("···", "设置", delegate { ShowSettings(settings: null); });
            settings.FontSize = 22;
            Button addButton = Button("+", "添加待办", delegate { CreateTaskDialog(null).ShowDialog(); }); addButton.FontSize = 22;
            tools.Children.Add(addButton); tools.Children.Add(settings); Grid.SetColumn(tools, 1); head.Children.Add(tools);
            head.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (state.PositionLocked || e.ClickCount == 2) return; try { DragMove(); } catch (InvalidOperationException) { } };
            root.Children.Add(head);
            Grid navigation = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            navigation.ColumnDefinitions.Add(new ColumnDefinition()); navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dateLabel = Text("", 12, Dim); dateLabel.VerticalAlignment = VerticalAlignment.Center; navigation.Children.Add(dateLabel);
            StackPanel nav = new StackPanel { Orientation = Orientation.Horizontal };
            nav.Children.Add(Button("‹", "上一周", delegate { SwitchWeek(week.AddDays(-7)); }));
            Button current = Button("本周", "回到本周", delegate { SwitchWeek(Dates.Monday(DateTime.Today)); }); current.FontSize = 11; current.MinWidth = 44; nav.Children.Add(current);
            nav.Children.Add(Button("›", "下一周", delegate { SwitchWeek(week.AddDays(7)); }));
            Grid.SetColumn(nav, 1); navigation.Children.Add(nav); Grid.SetRow(navigation, 1); root.Children.Add(navigation);
            StackPanel progress = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            progressLabel = Text("", 11, Dim); progressLabel.Margin = new Thickness(0, 0, 0, 9); progress.Children.Add(progressLabel);
            Border track = new Border { Height = 2, Background = Line, CornerRadius = new CornerRadius(1), HorizontalAlignment = HorizontalAlignment.Stretch };
            progressFill = new Border { Height = 2, Background = White, CornerRadius = new CornerRadius(1), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            track.Child = progressFill; track.SizeChanged += delegate { UpdateProgress(); }; progress.Children.Add(track); Grid.SetRow(progress, 2); root.Children.Add(progress);
            rows = new StackPanel();
            ScrollViewer scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 2, -8, 12), Padding = new Thickness(0, 0, 8, 0) };
            Grid.SetRow(scroll, 3); root.Children.Add(scroll);
            Thumb grip = new Thumb { Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Right, Cursor = Cursors.SizeNWSE, Opacity = 0.5, Template = GripTemplate(), ToolTip = "拖动调整大小" };
            grip.DragDelta += delegate(object sender, DragDeltaEventArgs e) { Width = Math.Max(MinWidth, Math.Min(MaxWidth, ActualWidth + e.HorizontalChange)); Height = Math.Max(MinHeight, Math.Min(MaxHeight, ActualHeight + e.VerticalChange)); };
            Grid.SetRow(grip, 4); root.Children.Add(grip);
            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            saveTimer.Tick += delegate { saveTimer.Stop(); SaveNow(); };
            LocationChanged += delegate { ScheduleGeometry(); }; SizeChanged += delegate { ScheduleGeometry(); };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (!exiting && !testing) { e.Cancel = true; HideToTray(); } };
            SourceInitialized += delegate { HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook(WindowMessages); };
            Loaded += delegate
            {
                ClampPosition(); ready = true;
                if (!testing)
                {
                    CreateTray();
                    dateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                    dateTimer.Tick += delegate { DateTime now = Dates.Monday(DateTime.Today); if (now != todayWeek) { bool following = week == todayWeek; todayWeek = now; if (following) SwitchWeek(now); else RenderRows(); } };
                    dateTimer.Start();
                    if (store.Warning != null) MessageBox.Show(this, store.Warning, "轻周待办");
                }
            };
            RenderRows();
        }
        Brush BackdropBrush() { return new SolidColorBrush(Color.FromArgb((byte)Math.Max(1, Math.Round(state.Backdrop * 255)), 28, 34, 42)); }
        static TextBlock Text(string content, double size, Brush color)
        {
            return new TextBlock { Text = content, FontSize = size, Foreground = color, Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1, Opacity = 0.65 } };
        }
        static Style ButtonStyle()
        {
            return (Style)System.Windows.Markup.XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Setter Property='Foreground' Value='White'/><Setter Property='Background' Value='Transparent'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Cursor' Value='Hand'/><Setter Property='MinWidth' Value='30'/><Setter Property='Height' Value='30'/><Setter Property='FontSize' Value='19'/><Setter Property='FocusVisualStyle' Value='{x:Null}' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='bg' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Background='{TemplateBinding Background}' CornerRadius='7'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bg' Property='Background' Value='#25FFFFFF'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='bg' Property='Background' Value='#40FFFFFF'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='bg' Property='BorderBrush' Value='#AAFFFFFF'/><Setter TargetName='bg' Property='BorderThickness' Value='1'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");
        }
        static Style CheckStyle()
        {
            return (Style)System.Windows.Markup.XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='CheckBox'><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'><Grid Width='28' Height='32' Background='Transparent'><Border x:Name='box' Width='17' Height='17' CornerRadius='4' BorderBrush='#CEFFFFFF' BorderThickness='1.3'/><Path x:Name='check' Visibility='Collapsed' Data='M 8,16 L 12,20 L 21,11' Stroke='#35404B' StrokeThickness='2' StrokeStartLineCap='Round' StrokeEndLineCap='Round'/></Grid><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='box' Property='Background' Value='#E5FFFFFF'/><Setter TargetName='check' Property='Visibility' Value='Visible'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='box' Property='BorderThickness' Value='2.5'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");
        }
        static Style ScrollbarStyle()
        {
            return (Style)System.Windows.Markup.XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollBar'><Setter Property='Width' Value='5'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'><Track x:Name='PART_Track' IsDirectionReversed='True'><Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border Background='#60FFFFFF' CornerRadius='2'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></ControlTemplate></Setter.Value></Setter></Style>");
        }
        static ControlTemplate GripTemplate()
        { return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Grid Background='Transparent'><Path Stroke='White' StrokeThickness='1' Data='M 5,15 L 15,5 M 10,15 L 15,10'/></Grid></ControlTemplate>"); }
        Button Button(string content, string tip, Action action)
        {
            Button button = new Button { Content = content, ToolTip = tip, Margin = new Thickness(1, 0, 1, 0) };
            button.Click += delegate { action(); }; button.PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { /* Button handles its own click and blocks header dragging. */ };
            return button;
        }
        internal void SwitchWeek(DateTime target)
        {
            week = Dates.Monday(target); RenderRows();
        }
        internal bool AddTodo(string text)
        {
            string value = text.Trim(); if (value.Length == 0) return false;
            List<Todo> list = Dates.Items(state, week, true); Todo item = new Todo { Text = value }; list.Add(item);
            if (!SaveNow()) { list.Remove(item); return false; }
            RenderRows(); return true;
        }
        internal void RenderRows()
        {
            rows.Children.Clear();
            titleLabel.Text = week == Dates.Monday(DateTime.Today) ? "本周待办" : "每周待办";
            dateLabel.Text = week.ToString("yyyy.MM.dd") + " — " + week.AddDays(6).ToString("MM.dd");
            List<Todo> items = Dates.Items(state, week, false);
            int sequence = 0;
            double numberWidth = Math.Max(26, items.Count.ToString().Length * 8 + 12);
            foreach (Todo todo in items)
            {
                sequence++;
                Todo item = todo;
                Grid row = new Grid { Margin = new Thickness(-5, 2, 0, 2), Background = Brushes.Transparent, MinHeight = 40 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(numberWidth) });
                row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                CheckBox check = new CheckBox { IsChecked = item.Done, VerticalAlignment = VerticalAlignment.Top, ToolTip = "标记完成 / 未完成" };
                check.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "完成：" + item.Text);
                TextBlock number = Text(sequence.ToString() + ".", 13, Dim);
                number.TextAlignment = TextAlignment.Right; number.Margin = new Thickness(0, 6, 8, 7); number.LineHeight = 23;
                Grid.SetColumn(number, 1); row.Children.Add(number);
                TextBlock label = Text(item.Text, 14, White); label.TextWrapping = TextWrapping.Wrap; label.Margin = new Thickness(1, 6, 8, 7); label.LineHeight = 23;
                if (item.Done) { label.TextDecorations = TextDecorations.Strikethrough; label.Opacity = 0.48; }
                label.ToolTip = "双击编辑 · 右键管理";
                check.Checked += delegate { item.Done = true; label.TextDecorations = TextDecorations.Strikethrough; label.Opacity = 0.48; SaveNow(); UpdateProgress(); };
                check.Unchecked += delegate { item.Done = false; label.TextDecorations = null; label.Opacity = 1; SaveNow(); UpdateProgress(); };
                label.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { if (e.ClickCount == 2) { Edit(item); e.Handled = true; } };
                Grid.SetColumn(label, 2); row.Children.Add(check); row.Children.Add(label);
                Button more = Button("···", "编辑、移动或删除", delegate { }); more.Opacity = 0; more.VerticalAlignment = VerticalAlignment.Top;
                ContextMenu menu = new ContextMenu();
                MenuItem edit = Menu("编辑事项", delegate { Edit(item); }); menu.Items.Add(edit);
                MenuItem up = Menu("上移", delegate { Move(item, -1); }); up.IsEnabled = items.IndexOf(item) > 0; menu.Items.Add(up);
                MenuItem down = Menu("下移", delegate { Move(item, 1); }); down.IsEnabled = items.IndexOf(item) < items.Count - 1; menu.Items.Add(down);
                menu.Items.Add(new Separator()); menu.Items.Add(Menu("删除事项", delegate { Remove(item); }));
                more.Click += delegate { menu.PlacementTarget = more; menu.IsOpen = true; };
                row.ContextMenu = menu; row.MouseEnter += delegate { more.Opacity = 0.7; }; row.MouseLeave += delegate { more.Opacity = 0; };
                more.GotKeyboardFocus += delegate { more.Opacity = 1; }; more.LostKeyboardFocus += delegate { more.Opacity = 0; };
                Grid.SetColumn(more, 3); row.Children.Add(more); rows.Children.Add(row);
            }
            UpdateProgress();
        }
        void UpdateProgress()
        {
            List<Todo> list = Dates.Items(state, week, false); int done = list.Count(x => x.Done);
            progressLabel.Text = list.Count == 0 ? "这一周，慢慢来" : (done + " / " + list.Count + " 已完成" + (done == list.Count ? "  ·  本周计划已完成" : ""));
            FrameworkElement parent = progressFill.Parent as FrameworkElement;
            if (parent != null) progressFill.Width = list.Count == 0 ? 0 : Math.Max(0, parent.ActualWidth * done / list.Count);
        }
        internal void Move(Todo item, int delta)
        {
            List<Todo> list = Dates.Items(state, week, false); int old = list.IndexOf(item), next = old + delta;
            if (old < 0 || next < 0 || next >= list.Count) return;
            list.RemoveAt(old); list.Insert(next, item); SaveNow(); RenderRows();
        }
        internal void Remove(Todo item)
        {
            if (!testing && MessageBox.Show(this, "删除这条待办？\n\n" + item.Text, "轻周待办", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            Dates.Items(state, week, false).Remove(item); SaveNow(); RenderRows();
        }
        void Edit(Todo item)
        {
            CreateTaskDialog(item).ShowDialog();
        }
        internal Window CreateTaskDialog(Todo item)
        {
            Window dialog = new Window { Owner = this, Title = item == null ? "添加待办" : "编辑待办", Width = 420, Height = 230, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(Color.FromRgb(248, 249, 251)), Foreground = Brushes.Black, FontFamily = FontFamily, ShowInTaskbar = false };
            StackPanel body = new StackPanel { Margin = new Thickness(22) };
            TextBox editor = new TextBox { Text = item == null ? "" : item.Text, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, Height = 84, MaxLength = 2000, Padding = new Thickness(10), FontSize = 14, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            body.Children.Add(editor);
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            Button cancel = new Button { Content = "取消", Width = 80, Height = 34, Margin = new Thickness(0, 0, 10, 0), Style = null };
            cancel.Click += delegate { dialog.Close(); }; buttons.Children.Add(cancel);
            Button save = new Button { Content = item == null ? "添加" : "保存", Width = 90, Height = 34, IsDefault = true, Style = null };
            save.Click += delegate
            {
                string value = editor.Text.Trim(); if (value.Length == 0) { editor.Focus(); return; }
                if (item == null) { if (!AddTodo(value)) return; }
                else
                {
                    string original = item.Text; item.Text = value;
                    if (!SaveNow()) { item.Text = original; return; }
                    RenderRows();
                }
                dialog.Close();
            };
            buttons.Children.Add(save); body.Children.Add(buttons); dialog.Content = body;
            dialog.Loaded += delegate { editor.Focus(); editor.SelectAll(); };
            dialog.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; } };
            return dialog;
        }
        static MenuItem Menu(string text, Action action)
        { MenuItem item = new MenuItem { Header = text }; item.Click += delegate { action(); }; return item; }
        void ShowSettings(object settings)
        {
            ContextMenu menu = CreateSettingsMenu();
            menu.PlacementTarget = this; menu.Placement = PlacementMode.MousePoint; menu.IsOpen = true;
        }
        internal void SetPositionLocked(bool locked)
        {
            state.PositionLocked = locked; header.Cursor = locked ? Cursors.Arrow : Cursors.SizeAll; SaveNow();
        }
        internal ContextMenu CreateSettingsMenu()
        {
            ContextMenu menu = new ContextMenu();
            MenuItem pinned = Menu("始终置顶", delegate { state.Pinned = !state.Pinned; Topmost = state.Pinned; SaveNow(); }); pinned.IsCheckable = true; pinned.IsChecked = state.Pinned; menu.Items.Add(pinned);
            MenuItem positionLock = Menu("锁定位置", delegate { SetPositionLocked(!state.PositionLocked); }); positionLock.IsCheckable = true; positionLock.IsChecked = state.PositionLocked; menu.Items.Add(positionLock);
            MenuItem startup = Menu("开机自动启动", delegate
            {
                try { Program.SetStartup(!Program.StartupEnabled); } catch (Exception ex) { MessageBox.Show(this, "无法修改开机自启：" + ex.Message, Title); }
            }); startup.IsCheckable = true; startup.IsChecked = Program.StartupEnabled; menu.Items.Add(startup);
            menu.Items.Add(new Separator());
            StackPanel sliderBody = new StackPanel { Margin = new Thickness(5, 3, 12, 6), Width = 205 };
            sliderBody.Children.Add(new TextBlock { Text = "背景浓度  ·  0% 为全透明", Foreground = Brushes.DimGray, FontSize = 12, Margin = new Thickness(0, 0, 0, 7) });
            Slider slider = new Slider { Minimum = 0, Maximum = 80, Value = state.Backdrop * 100, TickFrequency = 10, IsSnapToTickEnabled = true, ToolTip = "透明背景上的白字配有阴影，浅色壁纸可增加背景浓度" };
            slider.ValueChanged += delegate { state.Backdrop = slider.Value / 100; panel.Background = BackdropBrush(); saveTimer.Stop(); saveTimer.Start(); };
            sliderBody.Children.Add(slider); menu.Items.Add(new MenuItem { Header = sliderBody, StaysOpenOnClick = true });
            menu.Items.Add(new Separator());
            menu.Items.Add(Menu("备份全部清单…", Export));
            menu.Items.Add(Menu("打开数据文件夹", delegate { Process.Start("explorer.exe", "\"" + store.Folder + "\""); }));
            menu.Items.Add(Menu("使用说明", delegate { MessageBox.Show(this, "拖动顶部区域移动挂件；拖动右下角调整大小。\n\n点击右上角 +，在弹窗中输入待办后按 Enter 添加。\n勾选方框表示完成。\n双击文字编辑；右键可移动或删除事项。\n‹ / › 切换周，点击“本周”返回。\n\n右键托盘图标可隐藏待办，双击托盘图标重新打开。\n设置中可选锁定位置、置顶、开机自启和背景浓度。\n开启“锁定位置”后不能拖动，取消勾选后可移动。\n\n清单自动保存于本机，不需要联网。\n轻周待办 1.0.5", "轻周待办 · 使用说明"); }));
            menu.Items.Add(new Separator()); menu.Items.Add(Menu("退出轻周待办", ExitApp)); return menu;
        }
        void Export()
        {
            SaveFileDialog dialog = new SaveFileDialog { Title = "备份全部清单", FileName = "轻周待办-备份-" + DateTime.Now.ToString("yyyyMMdd") + ".json", Filter = "清单备份 (*.json)|*.json" };
            if (dialog.ShowDialog(this) != true) return;
            try { if (!SaveNow()) return; File.Copy(store.FilePath, dialog.FileName, true); MessageBox.Show(this, "备份已导出。", Title); }
            catch (Exception ex) { MessageBox.Show(this, "备份失败：" + ex.Message, Title); }
        }
        void ScheduleGeometry() { if (ready) { saveTimer.Stop(); saveTimer.Start(); } }
        internal bool SaveNow()
        {
            state.Left = Left; state.Top = Top; state.Width = Width; state.Height = Height;
            try { store.Save(state); return true; }
            catch (Exception ex) { if (!testing) MessageBox.Show(this, "清单尚未保存，请勿退出。\n" + ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
        }
        void ClampPosition()
        {
            Rect work = SystemParameters.WorkArea;
            Left = double.IsNaN(state.Left) ? work.Right - Width - 36 : state.Left;
            Top = double.IsNaN(state.Top) ? work.Top + 70 : state.Top;
            // A saved window must intersect a current monitor, even after unplugging a display.
            var source = PresentationSource.FromVisual(this); double sx = source == null ? 1 : source.CompositionTarget.TransformToDevice.M11; double sy = source == null ? 1 : source.CompositionTarget.TransformToDevice.M22;
            var bounds = new System.Drawing.Rectangle((int)(Left * sx), (int)(Top * sy), (int)(Math.Min(Width, 100) * sx), (int)(Math.Min(Height, 70) * sy));
            if (!Forms.Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds))) { Left = work.Right - Width - 36; Top = work.Top + 50; }
        }
        IntPtr WindowMessages(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (state.PositionLocked && msg == 0x0112 && (w.ToInt64() & 0xfff0) == 0xf010) { handled = true; return IntPtr.Zero; }
            if ((uint)msg == Program.ShowMessage) { Restore(); handled = true; }
            if ((uint)msg == Program.QuitMessage) { ExitApp(); handled = true; }
            return IntPtr.Zero;
        }
        internal void CreateTray()
        {
            tray = new Forms.NotifyIcon { Text = "轻周待办 · 双击打开", Visible = true };
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("icon.ico")) tray.Icon = stream == null ? System.Drawing.SystemIcons.Application : new System.Drawing.Icon(stream);
            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            menu.Items.Add("显示待办", null, delegate { Restore(); });
            menu.Items.Add("隐藏待办", null, delegate { HideToTray(); });
            Forms.ToolStripMenuItem pin = new Forms.ToolStripMenuItem("始终置顶"); pin.Click += delegate { state.Pinned = !state.Pinned; Topmost = state.Pinned; SaveNow(); }; menu.Items.Add(pin);
            Forms.ToolStripMenuItem positionLock = new Forms.ToolStripMenuItem("锁定位置"); positionLock.Click += delegate { SetPositionLocked(!state.PositionLocked); }; menu.Items.Add(positionLock);
            Forms.ToolStripMenuItem startup = new Forms.ToolStripMenuItem("开机自动启动"); startup.Click += delegate { try { Program.SetStartup(!Program.StartupEnabled); } catch (Exception ex) { Forms.MessageBox.Show(ex.Message, "无法修改自启"); } }; menu.Items.Add(startup);
            menu.Opening += delegate { pin.Checked = state.Pinned; positionLock.Checked = state.PositionLocked; startup.Checked = Program.StartupEnabled; };
            menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add("退出", null, delegate { ExitApp(); });
            tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { Restore(); };
        }
        internal bool TrayReady { get { return tray != null && tray.Visible && tray.ContextMenuStrip.Items.Count == 7; } }
        internal void HideToTray() { if (!SaveNow()) return; Hide(); }
        internal void Restore() { Show(); WindowState = WindowState.Normal; Activate(); }
        internal void ExitApp()
        {
            if (!SaveNow()) return;
            exiting = true; saveTimer.Stop(); if (dateTimer != null) dateTimer.Stop();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            Close(); if (!testing) Application.Current.Shutdown();
        }
        internal void SetBackdrop(double amount) { state.Backdrop = amount; panel.Background = BackdropBrush(); }
    }
}
