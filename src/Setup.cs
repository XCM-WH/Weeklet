using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("轻周待办安装程序")]
[assembly: AssemblyProduct("LightWeekTodo")]
[assembly: AssemblyVersion("1.0.5.0")]

namespace LightWeekSetup
{
    static class Installer
    {
        internal const string Identity = "LightWeekTodo.30A7C2CF.1";
        internal static readonly string DefaultInstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "LightWeekTodo");
        internal static string InstallDir
        {
            get
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UninstallKey))
                {
                    string saved = key == null ? null : key.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrWhiteSpace(saved)) try { return ValidateDestination(saved); } catch { }
                }
                return DefaultInstallDir;
            }
        }
        internal static readonly string StartDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "轻周待办");
        internal static readonly string DesktopLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "轻周待办.lnk");
        internal const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\LightWeekTodo";
        internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        static Icon icon;
        internal static Icon AppIcon
        {
            get
            {
                if (icon == null) { using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("icon.ico")) icon = s == null ? SystemIcons.Application : new Icon(s); }
                return icon;
            }
        }
        [STAThread] static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--self-test") return Test(args[1]);
            if (args.Length == 2 && args[0] == "--remove")
            {
                if (!SamePath(args[1], InstallDir)) { MessageBox.Show("卸载路径校验失败。", "轻周待办"); return 1; }
                return RemoveInstalled();
            }
            if (args.Length > 0 && args[0] == "--uninstall")
            {
                if (!SamePath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), InstallDir))
                { MessageBox.Show("这个卸载程序的位置与当前安装记录不一致，请从 Windows 设置中的“已安装的应用”卸载。", "轻周待办"); return 1; }
                if (MessageBox.Show("卸载轻周待办？\n\n本机保存的待办清单会保留，重新安装后可继续使用。", "卸载轻周待办", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return 0;
                try
                {
                    string runner = Path.Combine(Path.GetTempPath(), "LightWeekTodo-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
                    File.Copy(Assembly.GetExecutingAssembly().Location, runner);
                    Process.Start(new ProcessStartInfo(runner, "--remove \"" + InstallDir + "\"") { UseShellExecute = true });
                    return 0;
                }
                catch (Exception ex) { MessageBox.Show("无法卸载：" + ex.Message, "轻周待办"); return 1; }
            }
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\LightWeekTodo.Setup.30A7C2CF", out created))
            {
                if (!created) { MessageBox.Show("安装向导已打开。", "轻周待办"); return 0; }
                try { Application.Run(new SetupForm()); return 0; }
                finally { mutex.ReleaseMutex(); }
            }
        }
        internal static bool SamePath(string a, string b) { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
        internal static string ValidateDestination(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new IOException("请选择安装文件夹。");
            string input = Environment.ExpandEnvironmentVariables(value.Trim());
            string root = Path.GetPathRoot(input);
            if (!Path.IsPathRooted(input) || root == null || root.Length != 3 || root[1] != ':' || input.IndexOf('"') >= 0)
                throw new IOException("请输入本机磁盘上的完整文件夹路径，例如 D:\\软件\\LightWeekTodo。");
            string full = Path.GetFullPath(input).TrimEnd('\\');
            if (string.Equals(full, root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new IOException("请在磁盘中选择一个文件夹，不要直接安装到磁盘根目录。");
            foreach (string part in full.Substring(root.Length).Split('\\'))
                if (part.Length == 0 || part.EndsWith(".") || part.EndsWith(" ") || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new IOException("安装路径包含不支持的文件夹名称。");
            return full;
        }
        static bool IsProductFolder(string folder)
        {
            string marker = Path.Combine(folder, "product.id");
            return File.Exists(marker) && File.ReadAllText(marker, Encoding.UTF8) == Identity;
        }
        internal static bool IsStartup()
        { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey)) return key != null && key.GetValue("LightWeekTodo") != null; }
        static void WriteApp(string folder)
        {
            folder = ValidateDestination(folder);
            if (Directory.Exists(folder) && !IsProductFolder(folder))
                foreach (string name in new[] { "LightWeekTodo.exe", "Uninstall.exe", "product.id", "使用说明.txt", "LightWeekTodo.exe.new" })
                    if (File.Exists(Path.Combine(folder, name))) throw new IOException("这个文件夹已有同名文件，请另选安装文件夹，避免覆盖。");
            Directory.CreateDirectory(folder);
            string probe = Path.Combine(folder, ".lightweek-write-" + Guid.NewGuid().ToString("N") + ".tmp");
            try { using (FileStream check = File.Create(probe)) { } }
            catch (UnauthorizedAccessException) { throw new IOException("这个文件夹不可写，请选择有写入权限的文件夹。"); }
            finally { if (File.Exists(probe)) File.Delete(probe); }
            string temp = Path.Combine(folder, "LightWeekTodo.exe.new");
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("LightWeekTodo.exe"))
            {
                if (source == null) throw new InvalidDataException("安装程序缺少应用文件。");
                using (FileStream target = File.Create(temp)) source.CopyTo(target);
            }
            File.Copy(temp, Path.Combine(folder, "LightWeekTodo.exe"), true); File.Delete(temp);
            File.WriteAllText(Path.Combine(folder, "product.id"), Identity, Encoding.UTF8);
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("README.txt"))
            {
                if (source != null) using (FileStream target = File.Create(Path.Combine(folder, "使用说明.txt"))) source.CopyTo(target);
            }
        }
        internal static void CreateLink(string path, string target, string arguments)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType); object shortcut = null;
            try
            {
                shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                Type t = shortcut.GetType();
                t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { target });
                t.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { arguments });
                t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(target) });
                t.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "轻周待办 · 每周桌面清单" });
                t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { Path.Combine(Path.GetDirectoryName(target), "LightWeekTodo.exe") + ",0" });
                t.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally { if (shortcut != null) Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell); }
        }
        static void StopApp(string folder)
        {
            string exe = Path.Combine(folder, "LightWeekTodo.exe"); if (!File.Exists(exe) || !IsProductFolder(folder)) return;
            using (Process request = Process.Start(new ProcessStartInfo(exe, "--quit") { UseShellExecute = false, CreateNoWindow = true })) request.WaitForExit(3000);
            for (int i = 0; i < 30; i++)
            {
                try { using (FileStream file = File.Open(exe, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } return; }
                catch (IOException) { Thread.Sleep(100); }
            }
            throw new IOException("请先从托盘退出轻周待办，再重试。");
        }
        internal static string Install(string destination, bool desktop, bool startup)
        {
            string folder = ValidateDestination(destination);
            string previous = InstallDir;
            StopApp(previous); if (!SamePath(previous, folder)) StopApp(folder);
            WriteApp(folder);
            string uninstall = Path.Combine(folder, "Uninstall.exe");
            string own = Assembly.GetExecutingAssembly().Location;
            if (!SamePath(own, uninstall)) File.Copy(own, uninstall, true);
            Directory.CreateDirectory(StartDir);
            CreateLink(Path.Combine(StartDir, "轻周待办.lnk"), Path.Combine(folder, "LightWeekTodo.exe"), "");
            CreateLink(Path.Combine(StartDir, "卸载轻周待办.lnk"), uninstall, "--uninstall");
            if (desktop || File.Exists(DesktopLink)) CreateLink(DesktopLink, Path.Combine(folder, "LightWeekTodo.exe"), "");
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                key.SetValue("DisplayName", "轻周待办"); key.SetValue("DisplayVersion", "1.0.5"); key.SetValue("Publisher", "轻周待办");
                key.SetValue("InstallLocation", folder); key.SetValue("DisplayIcon", Path.Combine(folder, "LightWeekTodo.exe"));
                key.SetValue("UninstallString", "\"" + uninstall + "\" --uninstall"); key.SetValue("NoModify", 1); key.SetValue("NoRepair", 1);
                key.SetValue("EstimatedSize", (int)(new FileInfo(own).Length + new FileInfo(Path.Combine(folder, "LightWeekTodo.exe")).Length) / 1024, RegistryValueKind.DWord);
            }
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (startup) key.SetValue("LightWeekTodo", "\"" + Path.Combine(folder, "LightWeekTodo.exe") + "\" --startup");
                else key.DeleteValue("LightWeekTodo", false);
            }
            if (!SamePath(previous, folder) && Directory.Exists(previous) && IsProductFolder(previous))
            {
                try { RemoveFiles(previous); }
                catch (IOException) { return "安装完成。旧目录有文件占用，可稍后手动清理。"; }
                catch (UnauthorizedAccessException) { return "安装完成。旧目录无写入权限，可稍后手动清理。"; }
            }
            return "安装完成。点“完成”即可开始使用。";
        }
        static void RemoveFiles(string folder)
        {
            string full = Path.GetFullPath(folder);
            if (File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("安装目录是链接，无法自动移除。");
            string marker = Path.Combine(full, "product.id");
            if (!File.Exists(marker) || File.ReadAllText(marker, Encoding.UTF8) != Identity) throw new IOException("无法确认安装目录，已停止卸载。");
            foreach (string name in new[] { "LightWeekTodo.exe", "Uninstall.exe", "使用说明.txt", "LightWeekTodo.exe.new" })
            { string path = Path.Combine(full, name); if (File.Exists(path)) File.Delete(path); }
            File.Delete(marker);
            if (Directory.GetFileSystemEntries(full).Length == 0) Directory.Delete(full, false);
        }
        static int RemoveInstalled()
        {
            try
            {
                Thread.Sleep(500); string folder = InstallDir; StopApp(folder); RemoveFiles(folder);
                foreach (string link in new[] { Path.Combine(StartDir, "轻周待办.lnk"), Path.Combine(StartDir, "卸载轻周待办.lnk"), DesktopLink }) if (File.Exists(link)) File.Delete(link);
                if (Directory.Exists(StartDir) && Directory.GetFileSystemEntries(StartDir).Length == 0) Directory.Delete(StartDir, false);
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey)) key.DeleteValue("LightWeekTodo", false);
                Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
                MessageBox.Show("轻周待办已卸载。\n\n你的待办清单仍保存在：\n" + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LightWeekTodo"), "卸载完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                // The tiny temporary uninstaller is left for Windows' normal temp cleanup.
                return 0;
            }
            catch (Exception ex) { MessageBox.Show("卸载未完成：\n" + ex.Message + "\n\n待办数据未删除。", "轻周待办", MessageBoxButtons.OK, MessageBoxIcon.Warning); return 1; }
        }
        static int Test(string output)
        {
            string root = Path.GetFullPath(output); Directory.CreateDirectory(root); var lines = new System.Collections.Generic.List<string>();
            try
            {
                string sandbox = Path.Combine(root, "自选 安装位置-" + Guid.NewGuid().ToString("N"), "轻周 待办"); WriteApp(sandbox);
                if (!File.Exists(Path.Combine(sandbox, "LightWeekTodo.exe")) || !File.Exists(Path.Combine(sandbox, "使用说明.txt"))) throw new Exception("Payload missing");
                lines.Add("PASS: Embedded application, readme, and product marker extract correctly");
                lines.Add("PASS: Custom install destination supports Chinese characters, spaces, and nested folders");
                foreach (string invalid in new[] { Path.GetPathRoot(root), "relative\\LightWeekTodo", "", "C:relative" })
                {
                    bool rejected = false; try { ValidateDestination(invalid); } catch (IOException) { rejected = true; }
                    if (!rejected) throw new Exception("Invalid install path allowed: " + invalid);
                }
                lines.Add("PASS: Empty, relative, drive-relative, and drive-root destinations rejected");
                string conflict = Path.Combine(root, "conflict-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(conflict);
                string conflictFile = Path.Combine(conflict, "使用说明.txt"); File.WriteAllText(conflictFile, "user content");
                bool conflictRejected = false; try { WriteApp(conflict); } catch (IOException) { conflictRejected = true; }
                if (!conflictRejected || File.ReadAllText(conflictFile) != "user content") throw new Exception("Conflicting file overwritten");
                lines.Add("PASS: Unrelated same-name files in selected destination are preserved");
                AssemblyName name = AssemblyName.GetAssemblyName(Path.Combine(sandbox, "LightWeekTodo.exe"));
                if (name.Name != "LightWeekTodo") throw new Exception("Wrong assembly"); lines.Add("PASS: Embedded payload is the expected executable assembly");
                CreateLink(Path.Combine(root, "test-shortcut.lnk"), Path.Combine(sandbox, "LightWeekTodo.exe"), "");
                if (!File.Exists(Path.Combine(root, "test-shortcut.lnk"))) throw new Exception("Shortcut missing"); lines.Add("PASS: Windows shortcut creation");
                Type shellType = Type.GetTypeFromProgID("WScript.Shell"); object shell = Activator.CreateInstance(shellType); object link = null;
                try
                {
                    link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { Path.Combine(root, "test-shortcut.lnk") });
                    string target = link.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null) as string;
                    if (!SamePath(target, Path.Combine(sandbox, "LightWeekTodo.exe"))) throw new Exception("Shortcut points at wrong destination");
                }
                finally { if (link != null) Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
                lines.Add("PASS: Shortcut target resolves to the custom installation path");
                File.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(sandbox, "Uninstall.exe"));
                string keep = Path.Combine(sandbox, "user-keep.txt"); File.WriteAllText(keep, "retain"); RemoveFiles(sandbox);
                if (!File.Exists(keep) || File.Exists(Path.Combine(sandbox, "LightWeekTodo.exe"))) throw new Exception("Uninstall scope");
                lines.Add("PASS: Uninstaller removes only application files and retains unknown files");
                bool refused = false; try { RemoveFiles(sandbox); } catch (IOException) { refused = true; }
                if (!refused) throw new Exception("Missing marker allowed"); lines.Add("PASS: Uninstaller refuses a directory without the product marker");
                using (SetupForm form = new SetupForm())
                {
                    form.Show(); form.Update(); Application.DoEvents();
                    using (Graphics g = form.CreateGraphics()) lines.Add("Installer layout: " + form.Size + " client=" + form.ClientSize + " dpi=" + g.DpiX + " scale=" + form.AutoScaleDimensions + " current=" + form.CurrentAutoScaleDimensions);
                    ValidateBounds(form);
                    lines.Add("PASS: Editable destination and Browse button fit inside the window at system DPI");
                    using (Bitmap image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(0, 0, form.Width, form.Height)); image.Save(Path.Combine(root, "installer-preview.png")); }
                    form.Hide();
                }
                File.WriteAllLines(Path.Combine(root, "installer-test-results.txt"), lines); return 0;
            }
            catch (Exception ex) { lines.Add(ex.ToString()); File.WriteAllLines(Path.Combine(root, "installer-test-results.txt"), lines); return 1; }
        }
        static void ValidateBounds(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                if (control.Left < 0 || control.Top < 0 || control.Right > parent.ClientSize.Width + 1 || control.Bottom > parent.ClientSize.Height + 1)
                    throw new Exception("Control outside installer bounds: " + control.Text);
                ValidateBounds(control);
            }
        }
    }
    sealed class SetupForm : Form
    {
        readonly CheckBox desktop, startup, launch;
        readonly Button install, cancel;
        readonly Button browse;
        readonly TextBox location;
        readonly Label message;
        readonly ProgressBar progress;
        bool installed;
        internal SetupForm()
        {
            Text = "安装轻周待办"; Icon = Installer.AppIcon; ClientSize = new Size(560, 454); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Microsoft YaHei UI", 9); BackColor = Color.FromArgb(249, 250, 252);
            Panel mark = new Panel { Location = new Point(30, 31), Size = new Size(54, 54), BackColor = Color.FromArgb(72, 89, 111) };
            mark.Paint += delegate(object sender, PaintEventArgs e) { e.Graphics.ScaleTransform(mark.Width / 54F, mark.Height / 54F); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; using (Pen pen = new Pen(Color.White, 3)) { e.Graphics.DrawRectangle(pen, 14, 14, 26, 26); e.Graphics.DrawLines(pen, new[] { new Point(20, 27), new Point(25, 32), new Point(35, 21) }); } }; Controls.Add(mark);
            AddLabel("轻周待办", 103, 27, 380, 36, 21, Color.FromArgb(35, 44, 56), true);
            AddLabel("透明桌面清单，让每周计划一目了然", 105, 68, 400, 25, 10, Color.FromArgb(111, 120, 132), false);
            AddLabel("选择安装位置", 32, 119, 490, 24, 10, Color.FromArgb(52, 62, 76), false);
            location = new TextBox { Text = Installer.InstallDir, Location = new Point(33, 150), Size = new Size(392, 27), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White, ForeColor = Color.FromArgb(70, 79, 90) }; Controls.Add(location);
            browse = new Button { Text = "浏览…", Location = new Point(435, 148), Size = new Size(92, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.White }; browse.FlatAppearance.BorderColor = Color.FromArgb(218, 224, 232);
            browse.Click += delegate
            {
                using (FolderBrowserDialog picker = new FolderBrowserDialog { Description = "选择安装位置，程序会在所选位置下创建 LightWeekTodo 文件夹。也可直接在安装路径框中输入完整路径。", ShowNewFolderButton = true })
                {
                    if (Directory.Exists(location.Text)) picker.SelectedPath = location.Text;
                    if (picker.ShowDialog(this) == DialogResult.OK)
                        location.Text = string.Equals(Path.GetFileName(picker.SelectedPath.TrimEnd('\\')), "LightWeekTodo", StringComparison.OrdinalIgnoreCase) ? picker.SelectedPath : Path.Combine(picker.SelectedPath, "LightWeekTodo");
                }
            }; Controls.Add(browse);
            desktop = new CheckBox { Text = "创建桌面快捷方式", Location = new Point(34, 194), Size = new Size(300, 27), Checked = true }; Controls.Add(desktop);
            startup = new CheckBox { Text = "开机自动启动（以后也可在设置中修改）", Location = new Point(34, 228), Size = new Size(465, 27), Checked = Installer.IsStartup() }; Controls.Add(startup);
            launch = new CheckBox { Text = "安装后打开轻周待办", Location = new Point(34, 262), Size = new Size(330, 27), Checked = true }; Controls.Add(launch);
            message = AddLabel("按周保存 · 本机存储 · 托盘常驻", 33, 312, 492, 34, 9, Color.FromArgb(115, 123, 135), false);
            progress = new ProgressBar { Location = new Point(34, 354), Size = new Size(492, 4), Visible = false }; Controls.Add(progress);
            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 72, BackColor = Color.White }; Controls.Add(footer);
            Label version = new Label { Text = "版本 1.0.5", Location = new Point(32, 27), AutoSize = true, ForeColor = Color.FromArgb(139, 146, 156) }; footer.Controls.Add(version);
            cancel = new Button { Text = "取消", Location = new Point(322, 19), Size = new Size(88, 34), FlatStyle = FlatStyle.Flat, BackColor = Color.White }; cancel.FlatAppearance.BorderColor = Color.FromArgb(218, 224, 232); cancel.Click += delegate { Close(); }; footer.Controls.Add(cancel);
            install = new Button { Text = "安装", Location = new Point(424, 19), Size = new Size(104, 34), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(67, 84, 108), ForeColor = Color.White }; install.FlatAppearance.BorderSize = 0; install.Click += InstallClicked; footer.Controls.Add(install); AcceptButton = install; CancelButton = cancel;
            AutoScaleDimensions = new SizeF(96F, 96F);
            PerformAutoScale();
            using (Graphics graphics = CreateGraphics()) ClientSize = new Size((int)Math.Round(560 * graphics.DpiX / 96), (int)Math.Round(454 * graphics.DpiY / 96));
        }
        Label AddLabel(string text, int x, int y, int width, int height, float size, Color color, bool bold)
        { Label label = new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height), Font = new Font(Font.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color }; Controls.Add(label); return label; }
        void InstallClicked(object sender, EventArgs e)
        {
            if (installed)
            {
                if (launch.Checked)
                {
                    try { Process.Start(Path.Combine(Installer.InstallDir, "LightWeekTodo.exe")); }
                    catch (Exception ex) { MessageBox.Show("安装成功，但无法自动启动：" + ex.Message, "轻周待办"); }
                }
                Close(); return;
            }
            string destination;
            try { destination = Installer.ValidateDestination(location.Text); location.Text = destination; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "请选择有效的安装位置", MessageBoxButtons.OK, MessageBoxIcon.Information); location.Focus(); return; }
            install.Enabled = false; cancel.Enabled = false; desktop.Enabled = false; startup.Enabled = false; location.ReadOnly = true; browse.Enabled = false;
            progress.Visible = true; progress.Style = ProgressBarStyle.Marquee; message.Text = "正在安装…"; Update();
            try
            {
                message.Text = Installer.Install(destination, desktop.Checked, startup.Checked); installed = true;
                message.ForeColor = Color.FromArgb(52, 117, 83); install.Text = "完成";
                progress.Style = ProgressBarStyle.Continuous; progress.Value = 100; cancel.Visible = false;
            }
            catch (Exception ex)
            {
                message.Text = "安装未完成，可以重试。"; MessageBox.Show(this, "安装失败：\n" + ex.Message, "轻周待办", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                desktop.Enabled = true; startup.Enabled = true; cancel.Enabled = true; location.ReadOnly = false; browse.Enabled = true; progress.Visible = false;
            }
            install.Enabled = true;
        }
    }
}
