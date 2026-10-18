using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LightWeekTodo
{
    internal static class SelfTest
    {
        static readonly List<string> results = new List<string>();
        static void Check(bool value, string description) { if (!value) throw new Exception("FAIL: " + description); results.Add("PASS: " + description); }
        public static int Run(string output)
        {
            output = Path.GetFullPath(output); Directory.CreateDirectory(output);
            Widget widget = null;
            try
            {
                Check(Dates.Key(new DateTime(2027, 1, 3)) == "2026-12-28", "Sunday and cross-year Monday week boundary");
                Check(Dates.Key(new DateTime(2027, 1, 4)) == "2027-01-04", "New week starts on Monday");
                string folder = Path.Combine(output, "data-" + Guid.NewGuid().ToString("N"));
                Store store = new Store(folder); State state = store.Load();
                Check(state.Weeks.Count == 0 && state.Backdrop == 0 && !state.Pinned && !state.PositionLocked, "First run empty, transparent, unlocked, optional pin");
                Store legacyStore = new Store(Path.Combine(output, "legacy-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(legacyStore.Folder);
                File.WriteAllText(legacyStore.FilePath, "{\"Version\":1,\"Weeks\":[],\"Left\":30,\"Top\":30,\"Width\":390,\"Height\":540,\"Backdrop\":0,\"Pinned\":false}");
                Check(!legacyStore.Load().PositionLocked, "Old data without the position-lock field loads unlocked");
                Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                widget = new Widget(store, state, true); widget.Left = 30; widget.Top = 30; widget.Show(); widget.UpdateLayout();
                MenuItem lockOption = widget.CreateSettingsMenu().Items.OfType<MenuItem>().First(x => x.Header as string == "锁定位置");
                lockOption.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(state.PositionLocked && store.Load().PositionLocked && store.Load().Left == widget.Left && store.Load().Top == widget.Top, "Settings lock toggle immediately persists the current position and locked state");
                DateTime first = widget.week;
                AddUsingToolbar(widget, "整理每周计划", false);
                Check(Dates.Items(state, first, false).Count == 1, "Top-right plus opens add dialog and submits a task");
                AddUsingToolbar(widget, "取消后不应保存", true);
                Check(Dates.Items(state, first, false).Count == 1, "Canceling add dialog leaves the checklist unchanged");
                widget.AddTodo("完成项目复盘，梳理下周目标与需要跟进的事项。较长文字会自然换行。");
                widget.AddTodo("给自己留一点休息时间");
                Check(Dates.Items(state, first, false).Count == 3, "Add Chinese and multiline-wrapped tasks");
                Todo second = Dates.Items(state, first, false)[1];
                widget.Move(second, -1); Check(Dates.Items(state, first, false)[0] == second, "Reorder task");
                widget.Move(second, 1);
                CheckBox check = Descendants<CheckBox>(widget).First(); check.IsChecked = true;
                Check(Dates.Items(state, first, false)[0].Done && store.Load().Weeks[Dates.Key(first)][0].Done, "Checkbox event immediately persists completed state");
                Todo edited = Dates.Items(state, first, false)[0];
                AddUsingToolbar(widget, "整理这一周的计划", false, edited);
                Check(edited.Text == "整理这一周的计划" && store.Load().Weeks[Dates.Key(first)][0].Text == edited.Text && edited.Done, "Shared edit dialog saves text while preserving completed state");
                widget.SwitchWeek(first.AddDays(7));
                Check(Dates.Items(state, widget.week, false).Count == 0, "Next week starts empty");
                AddUsingToolbar(widget, "下周单独的任务", false); widget.SwitchWeek(first);
                Check(Dates.Items(state, first, false).Count == 3 && Dates.Items(state, first.AddDays(7), false).Count == 1, "Dialog adds to the selected week without changing history");
                Todo removed = new Todo { Text = "临时删除测试" }; Dates.Items(state, first, true).Add(removed); widget.Remove(removed);
                Check(!Dates.Items(state, first, false).Contains(removed), "Delete task persists");
                widget.SetBackdrop(0.35); state.Pinned = true; widget.Width = 390; widget.Height = 540; widget.UpdateLayout(); widget.SaveNow();
                State reload = new Store(folder).Load();
                Check(reload.Weeks.Count == 2 && reload.Pinned && reload.PositionLocked && reload.Backdrop == 0.35 && reload.Width == 390, "Reopen preserves all weeks and settings including position lock");
                Widget reopened = new Widget(store, reload, true);
                Check(reopened.CreateSettingsMenu().Items.OfType<MenuItem>().First(x => x.Header as string == "锁定位置").IsChecked, "Reopened widget shows position lock enabled"); reopened.Close();
                MenuItem unlockOption = widget.CreateSettingsMenu().Items.OfType<MenuItem>().First(x => x.Header as string == "锁定位置");
                unlockOption.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(!state.PositionLocked && !store.Load().PositionLocked, "The same settings toggle unlocks the widget and persists it");
                Render(widget, Path.Combine(output, "preview-readable.png"), false);
                widget.SetBackdrop(0); Render(widget, Path.Combine(output, "preview-transparent.png"), false);
                Render(widget, Path.Combine(output, "preview-on-wallpaper.png"), true);
                widget.Width = 320; widget.Height = 340;
                for (int i = 0; i < 24; i++) Dates.Items(state, first, true).Add(new Todo { Text = "长清单滚动测试 " + (i + 1) });
                widget.RenderRows(); widget.UpdateLayout();
                ScrollViewer scroll = Descendants<ScrollViewer>(widget).First(x => x.Content is StackPanel);
                Check(scroll.ScrollableHeight > 100 && scroll.ViewportHeight > 0, "24+ tasks scroll at minimum window size");
                Render(widget, Path.Combine(output, "preview-minimum.png"), true);
                widget.CreateTray(); Check(widget.TrayReady, "Real Windows tray icon and show, hide, pin, startup, exit menu created");
                widget.HideToTray(); Check(!widget.IsVisible && widget.TrayReady, "Hide closes visual surface while tray remains available");
                widget.Restore(); Check(widget.IsVisible, "Restore shows window");
                widget.ExitApp(); widget = null;
                Store recovery = new Store(folder); State before = recovery.Load(); recovery.Save(before);
                File.WriteAllText(recovery.FilePath, "{broken-json");
                State recovered = recovery.Load(); Check(recovery.Warning != null && recovered.Weeks.Count == 2 && Directory.GetFiles(folder, "*.damaged-*").Length == 1, "Corrupt file preserved and previous atomic backup restored");
                recovery.Save(recovered); Check(recovery.Load().Weeks.Count == 2, "Recovered data can be saved and read again");
                File.WriteAllLines(Path.Combine(output, "test-results.txt"), results);
                app.Shutdown(); return 0;
            }
            catch (Exception ex)
            {
                results.Add(ex.ToString()); File.WriteAllLines(Path.Combine(output, "test-results.txt"), results);
                if (widget != null) widget.Close(); return 1;
            }
        }
        static void AddUsingToolbar(Widget widget, string text, bool cancel, Todo editing = null)
        {
            Exception failure = null;
            widget.Dispatcher.BeginInvoke(new Action(delegate
            {
                Window dialog = null;
                try
                {
                    dialog = Application.Current.Windows.Cast<Window>().First(x => x.Owner == widget);
                    TextBox editor = Descendants<TextBox>(dialog).First();
                    Button submit = Descendants<Button>(dialog).First(x => (string)x.Content == (editing == null ? "添加" : "保存"));
                    editor.Clear();
                    submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (!dialog.IsVisible) throw new Exception("Empty task closed the dialog");
                    editor.Text = text;
                    Button action = cancel ? Descendants<Button>(dialog).First(x => (string)x.Content == "取消") : submit;
                    action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (dialog.IsVisible) throw new Exception("Task dialog did not close after action");
                }
                catch (Exception ex) { failure = ex; if (dialog != null) dialog.Close(); }
            }));
            if (editing == null) Descendants<Button>(widget).First(x => x.Content as string == "+").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else widget.CreateTaskDialog(editing).ShowDialog();
            if (failure != null) throw failure;
        }
        static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i); if (child is T) yield return (T)child;
                foreach (T item in Descendants<T>(child)) yield return item;
            }
        }
        static void Render(Widget widget, string path, bool wallpaper)
        {
            widget.UpdateLayout(); int width = (int)widget.ActualWidth, height = (int)widget.ActualHeight;
            RenderTargetBitmap surface = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); surface.Render(widget);
            if (wallpaper)
            {
                DrawingVisual visual = new DrawingVisual();
                using (DrawingContext context = visual.RenderOpen())
                {
                    context.DrawRectangle(new LinearGradientBrush(Color.FromRgb(87, 113, 140), Color.FromRgb(34, 47, 63), 40), null, new Rect(0, 0, width + 72, height + 72));
                    context.DrawImage(surface, new Rect(36, 36, width, height));
                }
                surface = new RenderTargetBitmap(width + 72, height + 72, 96, 96, PixelFormats.Pbgra32); surface.Render(visual);
            }
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(surface));
            using (FileStream stream = File.Create(path)) encoder.Save(stream);
        }
    }
}
