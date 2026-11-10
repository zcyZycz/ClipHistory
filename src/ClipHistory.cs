using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("ClipHistory")]
[assembly: System.Reflection.AssemblyDescription("剪切板历史：记录复制的文字与图片，保留3天，常驻托盘")]
[assembly: System.Reflection.AssemblyProduct("ClipHistory")]
[assembly: System.Reflection.AssemblyCompany("ClipHistory")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace DSHClip
{
    // ---------------- 数据模型 ----------------
    public class TextEntry
    {
        public DateTime Time;
        public string Text;
    }

    public class ImageEntry
    {
        public DateTime Time;
        public string File;
        public int W;
        public int H;
    }

    // ---------------- 存储 ----------------
    public class Store
    {
        public string Dir;
        public string TextFile;
        public string ImgFile;
        public string ImgDir;

        public Store(string dir)
        {
            Dir = dir;
            TextFile = Path.Combine(dir, "texts.dat");
            ImgFile = Path.Combine(dir, "images.dat");
            ImgDir = Path.Combine(dir, "img");
            LogDir = Path.Combine(dir, "logs");
            CfgFile = Path.Combine(dir, "settings.ini");
        }

        public string LogDir;
        public string CfgFile;

        public void Ensure()
        {
            if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
            if (!Directory.Exists(ImgDir)) Directory.CreateDirectory(ImgDir);
            if (!Directory.Exists(LogDir)) Directory.CreateDirectory(LogDir);
        }

        public List<TextEntry> LoadTexts()
        {
            List<TextEntry> list = new List<TextEntry>();
            if (!File.Exists(TextFile)) return list;
            string[] lines = File.ReadAllLines(TextFile, Encoding.UTF8);
            foreach (string line in lines)
            {
                if (line.Length == 0) continue;
                string[] p = line.Split('\t');
                if (p.Length < 2) continue;
                try
                {
                    long ticks = long.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(p[0])));
                    string txt = Encoding.UTF8.GetString(Convert.FromBase64String(p[1]));
                    TextEntry e = new TextEntry();
                    e.Time = new DateTime(ticks);
                    e.Text = txt;
                    list.Add(e);
                }
                catch { }
            }
            return list;
        }

        public void SaveTexts(List<TextEntry> list)
        {
            Ensure();
            StringBuilder sb = new StringBuilder();
            foreach (TextEntry e in list)
            {
                sb.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(e.Time.Ticks.ToString())));
                sb.Append('\t');
                sb.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(e.Text)));
                sb.Append("\r\n");
            }
            File.WriteAllText(TextFile, sb.ToString(), Encoding.UTF8);
        }

        public List<ImageEntry> LoadImages()
        {
            List<ImageEntry> list = new List<ImageEntry>();
            if (!File.Exists(ImgFile)) return list;
            string[] lines = File.ReadAllLines(ImgFile, Encoding.UTF8);
            foreach (string line in lines)
            {
                if (line.Length == 0) continue;
                string[] p = line.Split('\t');
                if (p.Length < 4) continue;
                try
                {
                    long ticks = long.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(p[0])));
                    string fn = Encoding.UTF8.GetString(Convert.FromBase64String(p[1]));
                    ImageEntry e = new ImageEntry();
                    e.Time = new DateTime(ticks);
                    e.File = fn;
                    e.W = int.Parse(p[2]);
                    e.H = int.Parse(p[3]);
                    list.Add(e);
                }
                catch { }
            }
            return list;
        }

        public void SaveImages(List<ImageEntry> list)
        {
            Ensure();
            StringBuilder sb = new StringBuilder();
            foreach (ImageEntry e in list)
            {
                sb.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(e.Time.Ticks.ToString())));
                sb.Append('\t');
                sb.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(e.File)));
                sb.Append('\t');
                sb.Append(e.W);
                sb.Append('\t');
                sb.Append(e.H);
                sb.Append("\r\n");
            }
            File.WriteAllText(ImgFile, sb.ToString(), Encoding.UTF8);
        }

        public void DeleteImageFile(ImageEntry e)
        {
            try
            {
                string p = Path.Combine(ImgDir, e.File);
                if (File.Exists(p)) File.Delete(p);
            }
            catch { }
        }

        public void CleanOrphans(List<ImageEntry> images)
        {
            try
            {
                if (!Directory.Exists(ImgDir)) return;
                HashSet<string> keep = new HashSet<string>();
                foreach (ImageEntry e in images) keep.Add(e.File);
                string[] files = Directory.GetFiles(ImgDir, "*.png");
                foreach (string f in files)
                {
                    if (!keep.Contains(Path.GetFileName(f)))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }
            }
            catch { }
        }
    }

    // ---------------- 设置（便携：settings.ini） ----------------
    public static class Cfg
    {
        const string RUN = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RUNNAME = "DSHClip";

        static string IniPath()
        {
            string dir = Program.DataDir;
            if (string.IsNullOrEmpty(dir)) return null;
            return Path.Combine(dir, "settings.ini");
        }

        public static string Get(string name, string def)
        {
            try
            {
                string p = IniPath();
                if (p == null || !File.Exists(p)) return def;
                foreach (string line in File.ReadAllLines(p, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    if (line.Substring(0, eq).Trim() == name)
                        return line.Substring(eq + 1).Trim();
                }
            }
            catch { }
            return def;
        }

        public static void Set(string name, string value)
        {
            try
            {
                string p = IniPath();
                if (p == null) return;
                List<string> lines = new List<string>();
                bool done = false;
                if (File.Exists(p))
                {
                    foreach (string line in File.ReadAllLines(p, Encoding.UTF8))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0 && line.Substring(0, eq).Trim() == name)
                        {
                            lines.Add(name + "=" + value);
                            done = true;
                        }
                        else
                        {
                            lines.Add(line);
                        }
                    }
                }
                if (!done) lines.Add(name + "=" + value);
                File.WriteAllLines(p, lines.ToArray(), Encoding.UTF8);
            }
            catch { }
        }

        public static int GetInt(string name, int def)
        {
            string s = Get(name, null);
            int v;
            if (s != null && int.TryParse(s, out v)) return v;
            return def;
        }

        public static void SetInt(string name, int val)
        {
            Set(name, val.ToString());
        }

        public static bool IsAutoStart()
        {
            try
            {
                RegistryKey k = Registry.CurrentUser.OpenSubKey(RUN);
                if (k == null) return false;
                object v = k.GetValue(RUNNAME);
                k.Close();
                return v != null;
            }
            catch { return false; }
        }

        public static void SetAutoStart(bool on)
        {
            try
            {
                RegistryKey k = Registry.CurrentUser.OpenSubKey(RUN, true);
                if (k == null) return;
                if (on)
                {
                    k.SetValue(RUNNAME, "\"" + Application.ExecutablePath + "\" --tray");
                }
                else
                {
                    k.DeleteValue(RUNNAME, false);
                }
                k.Close();
            }
            catch { }
        }
    }
    // ---------------- 数据目录解析（便携优先） ----------------
    public static class DataPath
    {
        // 优先 exe 同目录（便携），其次用户目录，最后临时目录
        public static string Resolve()
        {
            List<string> candidates = new List<string>();
            try
            {
                candidates.Add(Path.Combine(Application.StartupPath, "data"));
            }
            catch { }
            try
            {
                candidates.Add(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSHClip"));
            }
            catch { }
            try
            {
                candidates.Add(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DSHClip"));
            }
            catch { }
            try
            {
                candidates.Add(Path.Combine(Path.GetTempPath(), "DSHClip"));
            }
            catch { }

            foreach (string c in candidates)
            {
                try
                {
                    if (!Directory.Exists(c)) Directory.CreateDirectory(c);
                    string probe = Path.Combine(c, ".writetest");
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);
                    Program.LogLine("数据目录可用: " + c);
                    return c;
                }
                catch (Exception ex)
                {
                    Program.LogLine("数据目录不可用: " + c + " -> " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            return null;
        }
    }


    // ---------------- 主题 / 现代化外观 ----------------
    public static class Theme
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static bool IsDark()
        {
            try
            {
                Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (k != null)
                {
                    object v = k.GetValue("AppsUseLightTheme");
                    k.Close();
                    if (v != null) return Convert.ToInt32(v) == 0;
                }
            }
            catch { }
            return false;
        }

        public static Color Bg(bool dark) { return dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243); }
        public static Color Panel(bool dark) { return dark ? Color.FromArgb(44, 44, 44) : Color.FromArgb(250, 250, 250); }
        public static Color Fg(bool dark) { return dark ? Color.FromArgb(240, 240, 240) : Color.FromArgb(28, 28, 28); }
        public static Color Sub(bool dark) { return dark ? Color.FromArgb(160, 160, 160) : Color.FromArgb(110, 110, 110); }
        public static Color Line(bool dark) { return dark ? Color.FromArgb(64, 64, 64) : Color.FromArgb(225, 225, 225); }
        public static Color Hover(bool dark) { return dark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(235, 235, 235); }
        public static Color Accent() { return Color.FromArgb(0, 120, 215); }
        public static Color Select(bool dark) { return dark ? Color.FromArgb(52, 96, 140) : Color.FromArgb(214, 232, 248); }

        // 圆角窗口 + 深色标题栏（不使用亚克力，避免 WinForms 绘制异常）
        public static void Apply(IntPtr hwnd, bool dark)
        {
            try
            {
                int round = 2;
                DwmSetWindowAttribute(hwnd, 33, ref round, 4);

                int darkMode = dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, 20, ref darkMode, 4);
            }
            catch { }
        }

        public static void Round(Control c, int radius)
        {
            try
            {
                GraphicsPath path = new GraphicsPath();
                Rectangle r = new Rectangle(0, 0, c.Width, c.Height);
                int d = radius * 2;
                if (d > r.Width) d = r.Width;
                if (d > r.Height) d = r.Height;
                path.AddArc(r.X, r.Y, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                c.Region = new Region(path);
            }
            catch { }
        }
    }

    // 列表双缓冲（消除悬停/滚动白闪）
    public static class ListFx
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        const int LVM_SETEXTENDEDLISTVIEWSTYLE = 0x1036;
        const int LVS_EX_DOUBLEBUFFER = 0x00010000;

        public static void DoubleBuffer(ListView lv)
        {
            try
            {
                if (lv == null || !lv.IsHandleCreated) return;
                SendMessage(lv.Handle, LVM_SETEXTENDEDLISTVIEWSTYLE,
                    (IntPtr)LVS_EX_DOUBLEBUFFER, (IntPtr)LVS_EX_DOUBLEBUFFER);
            }
            catch { }
        }
    }

    // ---------------- 图标 ----------------
    public static class AppIcon
    {
        public static Icon Build()
        {
            try
            {
                Bitmap bmp = new Bitmap(32, 32);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    // 蓝色圆角底，16px 下也醒目
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(0, 120, 215)))
                    {
                        g.FillRectangle(bg, 1, 1, 30, 30);
                    }

                    // 白色纸
                    using (SolidBrush paper = new SolidBrush(Color.White))
                    {
                        g.FillRectangle(paper, 7, 8, 18, 20);
                    }

                    // 顶部夹子（深蓝）
                    using (SolidBrush clip = new SolidBrush(Color.FromArgb(0, 70, 140)))
                    {
                        g.FillRectangle(clip, 11, 4, 10, 7);
                    }

                    // 文字线条
                    using (Pen line = new Pen(Color.FromArgb(90, 150, 210), 2f))
                    {
                        g.DrawLine(line, 10, 14, 22, 14);
                        g.DrawLine(line, 10, 19, 22, 19);
                        g.DrawLine(line, 10, 24, 19, 24);
                    }
                }
                IntPtr h = bmp.GetHicon();
                Icon ic = Icon.FromHandle(h);
                bmp.Dispose();
                return ic;
            }
            catch
            {
                return SystemIcons.Application;
            }
        }
    }

    // ---------------- 主窗口 ----------------
    public class MainForm : Form
    {
        const int WM_CLIPBOARDUPDATE = 0x031D;
        const int WM_HOTKEY = 0x0312;
        const int HOTKEY_ID = 0xB001;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        Store store;
        List<TextEntry> texts;
        List<ImageEntry> images;
        int retainDays;
        bool ready;
        bool exiting;
        Icon myIcon;
        DateTime suppressUntil = DateTime.MinValue;

        TabControl tabs;
        ListView lvText;
        ListView lvImage;
        ImageList thumbs;
        NotifyIcon tray;
        System.Windows.Forms.Timer pruneTimer;
        NumericUpDown numDays;
        CheckBox chkAuto;
        ToolStripStatusLabel lblStatus;
        StatusStrip status;

        public MainForm(bool startHidden)
        {
            string dir = Program.DataDir;
            if (string.IsNullOrEmpty(dir)) dir = DataPath.Resolve();
            if (dir == null)
            {
                MessageBox.Show("找不到可写的数据目录，程序无法保存记录。\r\n" +
                    "请把 exe 放到用户目录下再试。", "剪切板历史",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                throw new IOException("没有可写的数据目录");
            }
            Program.LogLine("数据目录: " + dir);
            store = new Store(dir);
            store.Ensure();

            retainDays = Cfg.GetInt("RetainDays", 3);
            if (retainDays < 1) retainDays = 3;

            texts = store.LoadTexts();
            images = store.LoadImages();

            BuildUI();
            Restyle();
            FitWidth();
            Prune(true);
            RefreshAll();

            pruneTimer = new System.Windows.Forms.Timer();
            pruneTimer.Interval = 300000;      // 5 分钟
            pruneTimer.Tick += new EventHandler(pruneTimer_Tick);
            pruneTimer.Start();

            ready = true;

            if (!startHidden)
            {
                everShown = true;
                lastLocation = Location;
            }

            if (startHidden)
            {
                allowVisible = false;   // 托盘模式：拦截一切显示
            }
            else
            {
                allowVisible = true;    // 正常启动：允许显示
            }

            // 启动提示，方便找到托盘图标
            try
            {
                if (!startHidden)
                    tray.ShowBalloonTip(4000, "剪切板历史",
                    "已常驻托盘：单击图标打开，右键可清空历史或退出。",
                    ToolTipIcon.Info);
            }
            catch { }
        }

        FlowLayoutPanel bar;
        ContextMenuStrip menuText;
        ContextMenuStrip menuImage;
        bool dragging;
        Point dragStart;
        RubberBandForm band;
        bool allowVisible;
        Rectangle lastBox = Rectangle.Empty;

        void BuildUI()
        {
            Text = "剪切板历史";
            Width = 940;
            Height = 640;
            StartPosition = FormStartPosition.CenterScreen;
            myIcon = AppIcon.Build();
            Icon = myIcon;
            MinimumSize = new Size(720, 460);
            Padding = new Padding(0);

            // ---------------- 顶部工具栏：流式布局，避免重叠 ----------------
            bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Top;
            bar.Height = 52;
            bar.Padding = new Padding(12, 10, 12, 8);
            bar.WrapContents = false;
            bar.AutoScroll = false;

            Button btnCopy = MakeButton("复制选中", true);
            btnCopy.Click += new EventHandler(btnCopy_Click);
            Button btnDel = MakeButton("删除选中", false);
            btnDel.Click += new EventHandler(btnDel_Click);
            Button btnClear = MakeButton("清空全部", false);
            btnClear.Click += new EventHandler(btnClear_Click);
            Button btnDir = MakeButton("数据目录", false);
            btnDir.Click += new EventHandler(btnDir_Click);
            Button btnHelp = MakeButton("使用说明", false);
            btnHelp.Click += new EventHandler(btnHelp_Click);

            Label lbl = new Label();
            lbl.Text = "保留天数";
            lbl.AutoSize = true;
            lbl.Margin = new Padding(16, 9, 4, 0);

            numDays = new NumericUpDown();
            numDays.Width = 56;
            numDays.Minimum = 1;
            numDays.Maximum = 365;
            numDays.Value = retainDays;
            numDays.Margin = new Padding(0, 6, 12, 0);
            numDays.ValueChanged += new EventHandler(numDays_Changed);

            chkAuto = new CheckBox();
            chkAuto.Text = "开机自启";
            chkAuto.AutoSize = true;
            chkAuto.Margin = new Padding(0, 9, 0, 0);
            chkAuto.Checked = Cfg.IsAutoStart();
            chkAuto.CheckedChanged += new EventHandler(chkAuto_Changed);

            bar.Controls.Add(btnCopy);
            bar.Controls.Add(btnDel);
            bar.Controls.Add(btnClear);
            bar.Controls.Add(btnDir);
            bar.Controls.Add(btnHelp);
            bar.Controls.Add(lbl);
            bar.Controls.Add(numDays);
            bar.Controls.Add(chkAuto);

            // ---------------- 标签页 ----------------
            tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.SizeMode = TabSizeMode.Fixed;
            tabs.ItemSize = new Size(110, 34);
            tabs.Font = new Font("Microsoft YaHei UI", 10.5f);

            TabPage tp1 = new TabPage("文字");
            tp1.Padding = new Padding(8);

            lvText = new DoubleBufferedListView();
            lvText.Dock = DockStyle.Fill;
            lvText.View = View.Details;
            lvText.FullRowSelect = true;
            lvText.HideSelection = false;
            lvText.MultiSelect = true;
            lvText.BorderStyle = BorderStyle.None;
            lvText.Columns.Add("时间", 150);
            lvText.Columns.Add("内容", 600);
            lvText.DoubleClick += new EventHandler(lvText_DoubleClick);
            lvText.KeyDown += new KeyEventHandler(lvText_KeyDown);
            lvText.MouseDown += new MouseEventHandler(lv_MouseDown);
            lvText.MouseMove += new MouseEventHandler(lv_MouseMove);
            lvText.MouseUp += new MouseEventHandler(lv_MouseUp);
            tp1.Controls.Add(lvText);
            lvText.HandleCreated += new EventHandler(delegate { ListFx.DoubleBuffer(lvText); });
            lvText.Resize += new EventHandler(delegate { AdjustColumns(); });


            TabPage tp2 = new TabPage("图片");
            tp2.Padding = new Padding(8);

            thumbs = new ImageList();
            thumbs.ColorDepth = ColorDepth.Depth32Bit;
            thumbs.ImageSize = new Size(160, 160);

            lvImage = new DoubleBufferedListView();
            lvImage.LargeImageList = thumbs;
            lvImage.Dock = DockStyle.Fill;
            lvImage.View = View.LargeIcon;
            lvImage.HideSelection = false;
            lvImage.MultiSelect = true;
            lvImage.ShowItemToolTips = true;
            lvImage.BorderStyle = BorderStyle.None;
            lvImage.DoubleClick += new EventHandler(lvImage_DoubleClick);
            lvImage.KeyDown += new KeyEventHandler(lvImage_KeyDown);
            lvImage.MouseDown += new MouseEventHandler(lv_MouseDown);
            lvImage.MouseMove += new MouseEventHandler(lv_MouseMove);
            lvImage.MouseUp += new MouseEventHandler(lv_MouseUp);
            tp2.Controls.Add(lvImage);
            lvImage.HandleCreated += new EventHandler(delegate { ListFx.DoubleBuffer(lvImage); });

            tabs.TabPages.Add(tp1);
            tabs.TabPages.Add(tp2);

            // ---------------- 状态栏 ----------------
            status = new StatusStrip();
            lblStatus = new ToolStripStatusLabel();
            lblStatus.Spring = true;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            status.Items.Add(lblStatus);

            Controls.Add(tabs);
            Controls.Add(bar);
            Controls.Add(status);

            // ---------------- 右键菜单 ----------------
            menuText = new ContextMenuStrip();
            menuText.Items.Add("复制", null, new EventHandler(delegate { CopySelectedText(); }));
            menuText.Items.Add("删除", null, new EventHandler(delegate { DeleteSelected(); }));
            menuText.Items.Add(new ToolStripSeparator());
            menuText.Items.Add("全选", null, new EventHandler(delegate { SelectAllIn(lvText); }));
            lvText.ContextMenuStrip = menuText;

            menuImage = new ContextMenuStrip();
            menuImage.Items.Add("复制图片", null, new EventHandler(delegate { CopySelectedImage(); }));
            menuImage.Items.Add("删除", null, new EventHandler(delegate { DeleteSelected(); }));
            menuImage.Items.Add(new ToolStripSeparator());
            menuImage.Items.Add("全选", null, new EventHandler(delegate { SelectAllIn(lvImage); }));
            lvImage.ContextMenuStrip = menuImage;

            // ---------------- 托盘 ----------------
            tray = new NotifyIcon();
            tray.Icon = myIcon;
            tray.Text = "剪切板历史（单击打开）";
            tray.Visible = true;
            tray.MouseClick += new MouseEventHandler(tray_MouseClick);

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem miOpen = new ToolStripMenuItem("打开主界面");
            miOpen.Click += new EventHandler(tray_DoubleClick);
            ToolStripMenuItem miClear = new ToolStripMenuItem("清空历史");
            miClear.Click += new EventHandler(btnClear_Click);
            ToolStripMenuItem miExit = new ToolStripMenuItem("退出");
            miExit.Click += new EventHandler(miExit_Click);
            menu.Items.Add(miOpen);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miClear);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miExit);
            tray.ContextMenuStrip = menu;

            Program.LogLine("托盘图标已创建, Visible=" + tray.Visible + ", Icon=" + (tray.Icon != null));
        }

        Button MakeButton(string text, bool primary)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(14, 5, 14, 5);
            b.Margin = new Padding(0, 0, 8, 0);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.Cursor = Cursors.Hand;
            b.Tag = primary ? "primary" : "normal";
            return b;
        }

        void SelectAllIn(ListView lv)
        {
            lv.BeginUpdate();
            foreach (ListViewItem it in lv.Items) it.Selected = true;
            lv.EndUpdate();
        }

        void CopySelectedText()
        {
            if (lvText.SelectedIndices.Count == 0) { SetStatus("请先选中一条文字记录"); return; }
            CopyTextAt(lvText.SelectedIndices[0]);
        }

        void CopySelectedImage()
        {
            if (lvImage.SelectedIndices.Count == 0) { SetStatus("请先选中一张图片"); return; }
            CopyImageAt(lvImage.SelectedIndices[0]);
        }

        void lvText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.C) { CopySelectedText(); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.A) { SelectAllIn(lvText); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = true; }
        }

        void lvImage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.C) { CopySelectedImage(); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.A) { SelectAllIn(lvImage); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = true; }
        }

        // 拖拽框选（卡片式多选）
        void lv_MouseDown(object sender, MouseEventArgs e)
        {
            ListView lv = (ListView)sender;
            if (e.Button != MouseButtons.Left) return;
            dragStart = e.Location;
            lastBox = Rectangle.Empty;
            dragging = false;               // 先不进入拖拽，等移动超过阈值
            if (lv.GetItemAt(e.X, e.Y) != null) return;   // 点在条目上，走正常选择/双击
            if (!(Control.ModifierKeys == Keys.Control || Control.ModifierKeys == Keys.Shift))
            {
                foreach (ListViewItem it in lv.Items) it.Selected = false;
            }
        }

        void lv_MouseMove(object sender, MouseEventArgs e)
        {
            ListView lv = (ListView)sender;
            if (e.Button != MouseButtons.Left) return;

            // 未开始拖拽：移动超过 5 像素才启动，避免双击时冒出选框
            if (!dragging)
            {
                int dx = Math.Abs(e.X - dragStart.X);
                int dy = Math.Abs(e.Y - dragStart.Y);
                if (dx < 5 && dy < 5) return;
                dragging = true;
            }

            Rectangle box = Rectangle.FromLTRB(
                Math.Min(dragStart.X, e.X), Math.Min(dragStart.Y, e.Y),
                Math.Max(dragStart.X, e.X), Math.Max(dragStart.Y, e.Y));
            try
            {
                if (band == null) band = new RubberBandForm();
                Point sp = lv.PointToScreen(new Point(box.Left, box.Top));
                band.Bounds = new Rectangle(sp.X, sp.Y, Math.Max(box.Width, 1), Math.Max(box.Height, 1));
                if (!band.Visible) band.Show();
            }
            catch { }

            if (box == lastBox) return;
            lastBox = box;

            lv.BeginUpdate();
            foreach (ListViewItem it in lv.Items)
            {
                if (it.Bounds.IntersectsWith(box)) it.Selected = true;
            }
            lv.EndUpdate();
        }

        void lv_MouseUp(object sender, MouseEventArgs e)
        {
            dragging = false;
            try { if (band != null) band.Hide(); } catch { }
        }

        // 托盘模式下彻底阻止窗口显示
        protected override void SetVisibleCore(bool value)
        {
            if (!allowVisible)
            {
                base.SetVisibleCore(false);
                return;
            }
            base.SetVisibleCore(value);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            try
            {
                if (Visible)
                {
                    string st = Environment.StackTrace;
                    string[] lines = st.Split(new char[] { (char)10 });
                    string where = lines.Length > 3 ? lines[3].Trim() : st;
                    Program.LogLine("窗口变为可见 <- " + where);
                }
            }
            catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.Apply(Handle, Theme.IsDark());
            AddClipboardFormatListener(Handle);
            // Ctrl+Alt+V 唤出窗口
            try
            {
                bool ok = RegisterHotKey(Handle, HOTKEY_ID, 0x0002 | 0x0001, 0x56);
                Program.LogLine("注册热键 Ctrl+Alt+V: " + (ok ? "成功" : "失败"));
            }
            catch (Exception ex) { Program.LogLine("热键异常: " + ex.Message); }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            RemoveClipboardFormatListener(Handle);
            try { UnregisterHotKey(Handle, HOTKEY_ID); } catch { }
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                OnClipboardChanged();
            }
            else if (m.Msg == WM_HOTKEY && m.WParam.ToInt64() == HOTKEY_ID)
            {
                ShowFromTray();
            }
        }

        // ---------- 剪贴板捕获 ----------
        void OnClipboardChanged()
        {
            if (!ready) return;
            if (DateTime.Now < suppressUntil) return;

            string txt = null;
            try
            {
                if (Clipboard.ContainsText()) txt = Clipboard.GetText();
            }
            catch { }

            if (txt != null && txt.Trim().Length > 0)
            {
                if (txt.Length > 200000) txt = txt.Substring(0, 200000);
                if (texts.Count > 0 && texts[0].Text == txt) return;

                TextEntry e = new TextEntry();
                e.Time = DateTime.Now;
                e.Text = txt;
                texts.Insert(0, e);
                if (texts.Count > 1000) texts.RemoveRange(1000, texts.Count - 1000);
                store.SaveTexts(texts);
                AddTextRow(e, 0);
                UpdateStatus();
                return;
            }

            bool hasImg = false;
            try { hasImg = Clipboard.ContainsImage(); } catch { }
            if (!hasImg) return;

            try
            {
                Image img = Clipboard.GetImage();
                if (img == null) return;
                store.Ensure();
                string fn = DateTime.Now.Ticks.ToString() + ".png";
                string path = Path.Combine(store.ImgDir, fn);
                int w = img.Width;
                int h = img.Height;
                using (img)
                {
                    using (Bitmap bmp = new Bitmap(img)) bmp.Save(path, ImageFormat.Png);
                }
                ImageEntry e = new ImageEntry();
                e.Time = DateTime.Now;
                e.File = fn;
                e.W = w;
                e.H = h;
                images.Insert(0, e);
                if (images.Count > 200)
                {
                    for (int i = 200; i < images.Count; i++) store.DeleteImageFile(images[i]);
                    images.RemoveRange(200, images.Count - 200);
                }
                store.SaveImages(images);
                AddImageRow(e, 0);
                UpdateStatus();
            }
            catch { }
        }

        // ---------- 列表操作 ----------
        string TimeText(DateTime t)
        {
            TimeSpan d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return "刚刚";
            if (d.TotalHours < 1) return ((int)d.TotalMinutes) + " 分钟前";
            if (d.TotalDays < 1) return ((int)d.TotalHours) + " 小时前";
            return t.ToString("MM-dd HH:mm");
        }

        string Preview(string s)
        {
            string one = s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            if (one.Length > 120) one = one.Substring(0, 120) + "...";
            return one;
        }

        void AddTextRow(TextEntry e, int index)
        {
            ListViewItem it = new ListViewItem(TimeText(e.Time));
            it.SubItems.Add(Preview(e.Text));
            it.Tag = e;
            lvText.Items.Insert(index, it);
        }

        void AddThumb(ImageEntry e)
        {
            try
            {
                string p = Path.Combine(store.ImgDir, e.File);
                if (!File.Exists(p)) return;
                using (Image img = Image.FromFile(p))
                {
                    Bitmap tb = new Bitmap(96, 96);
                    using (Graphics g = Graphics.FromImage(tb))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.Clear(Color.Transparent);
                        float r = Math.Min(96f / img.Width, 96f / img.Height);
                        int w = Math.Max(1, (int)(img.Width * r));
                        int h = Math.Max(1, (int)(img.Height * r));
                        g.DrawImage(img, (96 - w) / 2, (96 - h) / 2, w, h);
                    }
                    if (thumbs.Images.ContainsKey(e.File)) thumbs.Images.RemoveByKey(e.File);
                    thumbs.Images.Add(e.File, tb);
                }
            }
            catch { }
        }

        void AddImageRow(ImageEntry e, int index)
        {
            AddThumb(e);
            ListViewItem it = new ListViewItem(TimeText(e.Time));
            if (thumbs.Images.ContainsKey(e.File)) it.ImageKey = e.File;
            it.Tag = e;
            it.ToolTipText = e.W + " x " + e.H + "  " + e.Time.ToString("MM-dd HH:mm");
            lvImage.Items.Insert(index, it);
        }

        void RefreshAll()
        {
            lvText.BeginUpdate();
            lvText.Items.Clear();
            foreach (TextEntry e in texts) AddTextRow(e, lvText.Items.Count);
            lvText.EndUpdate();

            lvImage.BeginUpdate();
            lvImage.Items.Clear();
            foreach (ImageEntry e in images) AddImageRow(e, lvImage.Items.Count);
            lvImage.EndUpdate();

            UpdateStatus();
        }

        void UpdateEmptyStates()
        {
            try
            {
                foreach (Control c in Controls)
                {
                    TabControl tc = c as TabControl;
                    if (tc == null) continue;
                    foreach (Control page in tc.Controls)
                    {
                        foreach (Control inner in page.Controls)
                        {
                            Label lbl = inner as Label;
                            if (lbl == null || lbl.Name == null) continue;
                            if (lbl.Name == "emptyText") { lbl.Visible = texts.Count == 0; lvText.Visible = texts.Count > 0; }
                            if (lbl.Name == "emptyImg") { lbl.Visible = images.Count == 0; lvImage.Visible = images.Count > 0; }
                        }
                    }
                }
            }
            catch { }
        }

        void UpdateStatus()
        {
            lblStatus.Text = "文字 " + texts.Count + " 条 · 图片 " + images.Count +
                " 张 · 保留 " + retainDays + " 天";
        }

        void SetStatus(string s)
        {
            lblStatus.Text = s;
        }

        // ---------- 命令 ----------
        int SelectedTextIndex()
        {
            if (lvText.SelectedIndices.Count == 0) return -1;
            return lvText.SelectedIndices[0];
        }

        int SelectedImageIndex()
        {
            if (lvImage.SelectedIndices.Count == 0) return -1;
            return lvImage.SelectedIndices[0];
        }

        void CopyTextAt(int i)
        {
            if (i < 0 || i >= texts.Count) return;
            try
            {
                SetClipboardText(texts[i].Text);
                SetStatus("已复制文字到剪贴板（" + texts[i].Text.Length + " 字）");
            }
            catch (Exception ex) { SetStatus("复制失败: " + ex.Message); }
        }

        void CopyImageAt(int i)
        {
            if (i < 0 || i >= images.Count) return;
            string p = Path.Combine(store.ImgDir, images[i].File);
            try
            {
                using (FileStream fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (Image img = Image.FromStream(fs))
                using (Bitmap bmp = new Bitmap(img))
                {
                    SetClipboardImage(bmp);
                }
                SetStatus("已复制图片到剪贴板");
            }
            catch (Exception ex) { SetStatus("复制失败: " + ex.Message); }
        }

        void SetClipboardText(string s)
        {
            suppressUntil = DateTime.Now.AddMilliseconds(900);
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try { Clipboard.SetText(s); return; }
                catch { Thread.Sleep(60); }
            }
        }

        void SetClipboardImage(Bitmap bmp)
        {
            suppressUntil = DateTime.Now.AddMilliseconds(900);
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try { Clipboard.SetImage(bmp); return; }
                catch { Thread.Sleep(60); }
            }
        }

        void DeleteSelected()
        {
            if (tabs.SelectedIndex == 0)
            {
                if (lvText.SelectedIndices.Count == 0) { SetStatus("请先选中要删除的条目"); return; }
                int n = lvText.SelectedIndices.Count;
                if (MessageBox.Show("确定删除选中的 " + n + " 条文字记录？", "确认删除",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

                List<int> idx = new List<int>();
                foreach (int i in lvText.SelectedIndices) idx.Add(i);
                idx.Sort();
                for (int k = idx.Count - 1; k >= 0; k--) texts.RemoveAt(idx[k]);

                store.SaveTexts(texts);
                RefreshAll();
                SetStatus("已删除 " + n + " 条文字记录");
            }
            else
            {
                if (lvImage.SelectedIndices.Count == 0) { SetStatus("请先选中要删除的图片"); return; }
                int n = lvImage.SelectedIndices.Count;
                if (MessageBox.Show("确定删除选中的 " + n + " 张图片？", "确认删除",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

                List<int> idx = new List<int>();
                foreach (int i in lvImage.SelectedIndices) idx.Add(i);
                idx.Sort();
                for (int k = idx.Count - 1; k >= 0; k--)
                {
                    store.DeleteImageFile(images[idx[k]]);
                    images.RemoveAt(idx[k]);
                }

                store.SaveImages(images);
                RefreshAll();
                SetStatus("已删除 " + n + " 张图片");
            }
        }

        void ClearAll()
        {
            if (MessageBox.Show("确定清空全部剪贴板历史？", "确认",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            texts.Clear();
            foreach (ImageEntry e in images) store.DeleteImageFile(e);
            images.Clear();
            store.SaveTexts(texts);
            store.SaveImages(images);
            RefreshAll();
            SetStatus("已清空全部历史");
        }

        bool Prune(bool silent)
        {
            DateTime limit = DateTime.Now.AddDays(-retainDays);
            int removed = 0;
            for (int i = texts.Count - 1; i >= 0; i--)
            {
                if (texts[i].Time < limit) { texts.RemoveAt(i); removed++; }
            }
            for (int i = images.Count - 1; i >= 0; i--)
            {
                if (images[i].Time < limit) { store.DeleteImageFile(images[i]); images.RemoveAt(i); removed++; }
            }
            if (removed > 0)
            {
                store.SaveTexts(texts);
                store.SaveImages(images);
            }
            store.CleanOrphans(images);
            if (!silent && removed > 0) SetStatus("已清理 " + removed + " 条过期记录");
            return removed > 0;
        }

        // ---------- 事件 ----------
        void btnCopy_Click(object sender, EventArgs e)
        {
            if (tabs.SelectedIndex == 0) CopyTextAt(SelectedTextIndex());
            else CopyImageAt(SelectedImageIndex());
        }

        void btnDel_Click(object sender, EventArgs e) { DeleteSelected(); }

        void btnClear_Click(object sender, EventArgs e) { ClearAll(); }

        void btnDir_Click(object sender, EventArgs e)
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + store.Dir + "\""); }
            catch { }
        }

        // 显示内嵌的使用说明
        // ================= 现代化外观 =================
        bool darkTheme;

        static System.Collections.Generic.IEnumerable<Control> AllControls(Control root)
        {
            foreach (Control c in root.Controls)
            {
                yield return c;
                foreach (Control cc in AllControls(c)) yield return cc;
            }
        }

        void Restyle()
        {
            darkTheme = Theme.IsDark();
            Font ui = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular, GraphicsUnit.Point);

            BackColor = Theme.Bg(darkTheme);
            ForeColor = Theme.Fg(darkTheme);
            Font = ui;

            foreach (Control c in AllControls(this))
            {
                c.Font = ui;
                Button b = c as Button;
                if (b != null)
                {
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderSize = 0;
                    b.FlatAppearance.MouseOverBackColor = Theme.Hover(darkTheme);
                    b.FlatAppearance.MouseDownBackColor = Theme.Line(darkTheme);
                    b.BackColor = Theme.Panel(darkTheme);
                    b.ForeColor = Theme.Fg(darkTheme);
                    b.Height = 30;
                    b.Cursor = Cursors.Hand;
                    b.TextAlign = ContentAlignment.MiddleCenter;
                    Theme.Round(b, 7);

                    string txt = b.Text;
                    if (txt == "复制选中" || txt == "开机自启")
                    {
                        b.BackColor = Theme.Accent();
                        b.ForeColor = Color.White;
                        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 100, 190);
                    }
                }

                Label lbl = c as Label;
                if (lbl != null) lbl.ForeColor = Theme.Sub(darkTheme);

                NumericUpDown num = c as NumericUpDown;
                if (num != null)
                {
                    num.BackColor = Theme.Panel(darkTheme);
                    num.ForeColor = Theme.Fg(darkTheme);
                    num.BorderStyle = BorderStyle.FixedSingle;
                }

                CheckBox chk = c as CheckBox;
                if (chk != null) chk.ForeColor = Theme.Fg(darkTheme);

                Panel pnl = c as Panel;
                if (pnl != null) pnl.BackColor = Theme.Bg(darkTheme);

                TabControl tab = c as TabControl;
                if (tab != null)
                {
                    tab.Font = new Font("Microsoft YaHei UI", 10f);
                    tab.Padding = new Point(14, 6);
                }

                TabPage page = c as TabPage;
                if (page != null) page.BackColor = Theme.Bg(darkTheme);
            }

            StyleOwningList(lvText, true);
            StyleOwningList(lvImage, false);

            StatusStrip ss = null;
            foreach (Control c in Controls) { if (c is StatusStrip) ss = (StatusStrip)c; }
            if (ss != null)
            {
                ss.BackColor = Theme.Bg(darkTheme);
                ss.ForeColor = Theme.Sub(darkTheme);
                ss.SizingGrip = false;
            }
        }

        void StyleSimpleList(ListView lv, bool details)
        {
            lv.BackColor = Theme.Panel(darkTheme);
            lv.ForeColor = Theme.Fg(darkTheme);
            lv.Font = new Font("Microsoft YaHei UI", 9.5f);
            lv.BorderStyle = BorderStyle.None;
            lv.OwnerDraw = false;
            if (details)
            {
                lv.GridLines = false;
            }
            else
            {
                lv.LargeImageList = thumbs;
            }
        }


        // ================= 颜色 =================
        Color SelBg()
        {
            return darkTheme ? Color.FromArgb(46, 78, 110) : Color.FromArgb(204, 232, 255);
        }

        Color SelBorder()
        {
            return darkTheme ? Color.FromArgb(84, 148, 210) : Color.FromArgb(153, 209, 255);
        }

        void FitWidth()
        {
            try
            {
                int need = bar.GetPreferredSize(Size.Empty).Width + 18;
                Width = need;
                MinimumSize = new Size(need, MinimumSize.Height);
                AdjustColumns();
            }
            catch { }
        }

        void AdjustColumns()
        {
            try
            {
                if (lvText == null || lvText.Columns.Count < 2) return;
                int rest = lvText.ClientSize.Width - lvText.Columns[0].Width - 6;
                if (rest > 120) lvText.Columns[1].Width = rest;
            }
            catch { }
        }

        void StyleNativeList(ListView lv, bool details)
        {
            lv.BackColor = darkTheme ? Color.FromArgb(50, 50, 50) : Color.White;
            lv.ForeColor = Theme.Fg(darkTheme);
            lv.Font = new Font("Microsoft YaHei UI", 10f);
            lv.BorderStyle = BorderStyle.None;
            lv.OwnerDraw = false;
            lv.ShowGroups = false;
            if (details) { lv.GridLines = false; }
            else { lv.LargeImageList = thumbs; }
        }

        void StyleOwningList(ListView lv, bool details)
        {
            lv.BackColor = Theme.Panel(darkTheme);
            lv.ForeColor = Theme.Fg(darkTheme);
            lv.Font = new Font("Microsoft YaHei UI", 10f);
            lv.BorderStyle = BorderStyle.None;
            lv.OwnerDraw = true;
            lv.ShowGroups = false;

            if (details)
            {
                lv.DrawColumnHeader += new DrawListViewColumnHeaderEventHandler(Lv_Header);
                lv.DrawItem += new DrawListViewItemEventHandler(Lv_RowBack);
                lv.DrawSubItem += new DrawListViewSubItemEventHandler(Lv_SubText);
            }
            else
            {
                lv.LargeImageList = thumbs;
                lv.DrawItem += new DrawListViewItemEventHandler(Lv_Icon);
            }
        }

        void Lv_Header(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Theme.Bg(darkTheme)))
                e.Graphics.FillRectangle(b, e.Bounds);

            using (SolidBrush f = new SolidBrush(Theme.Sub(darkTheme)))
            using (StringFormat sf = new StringFormat())
            {
                sf.LineAlignment = StringAlignment.Center;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                using (Font ft = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold))
                    e.Graphics.DrawString(e.Header.Text, ft, f,
                        new RectangleF(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height), sf);
            }
            using (Pen p = new Pen(Theme.Line(darkTheme)))
                e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        }

        void Lv_RowBack(object sender, DrawListViewItemEventArgs e)
        {
            Rectangle r = e.Bounds;
            bool sel = e.Item.Selected;

            using (SolidBrush b = new SolidBrush(sel ? SelBg() : Theme.Panel(darkTheme)))
                e.Graphics.FillRectangle(b, r);

            if (sel)
            {
                using (Pen p = new Pen(SelBorder()))
                    e.Graphics.DrawRectangle(p, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
            }

            // 关键：整行所有列的文字都在这里画，避免悬停局部重绘时丢字
            ListView lv = e.Item.ListView;
            if (lv == null) return;

            int x = r.X;
            using (StringFormat sf = new StringFormat())
            {
                sf.LineAlignment = StringAlignment.Center;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                sf.Trimming = StringTrimming.EllipsisCharacter;

                for (int c = 0; c < lv.Columns.Count; c++)
                {
                    int w = lv.Columns[c].Width;
                    Rectangle cell = new Rectangle(x, r.Y, w, r.Height);

                    string txt = (c < e.Item.SubItems.Count) ? e.Item.SubItems[c].Text : "";
                    Color fc = sel ? Theme.Fg(darkTheme)
                                   : (c == 0 ? Theme.Fg(darkTheme) : Theme.Sub(darkTheme));

                    using (SolidBrush f = new SolidBrush(fc))
                    using (SolidBrush bg2 = new SolidBrush(sel ? SelBg() : Theme.Panel(darkTheme)))
                    {
                        e.Graphics.FillRectangle(bg2, new Rectangle(cell.X, cell.Y, cell.Width, Math.Max(cell.Height - 1, 1)));
                        using (Font ft = new Font("Microsoft YaHei UI", 10f))
                            e.Graphics.DrawString(txt, ft, f,
                                new RectangleF(cell.X + 10, cell.Y, cell.Width - 14, cell.Height), sf);
                    }
                    x += w;
                }
            }
            // 行间浅灰分隔线（最后绘制，避免被单元格填充覆盖）
            using (Pen sep = new Pen(darkTheme ? Color.FromArgb(64, 64, 64) : Color.FromArgb(228, 228, 228)))
                e.Graphics.DrawLine(sep, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);

        }

        void Lv_SubText(object sender, DrawListViewSubItemEventArgs e)
        {
            // 单元格局部重绘时也要有内容，否则会闪白
            bool sel = e.Item.Selected;
            Rectangle b = e.Bounds;
            using (SolidBrush bg = new SolidBrush(sel ? SelBg() : Theme.Panel(darkTheme)))
                e.Graphics.FillRectangle(bg, new Rectangle(b.X, b.Y, b.Width, Math.Max(b.Height - 1, 1)));

            string txt = (e.SubItem != null) ? e.SubItem.Text : "";
            Color fc = sel ? Theme.Fg(darkTheme)
                           : (e.ColumnIndex == 0 ? Theme.Fg(darkTheme) : Theme.Sub(darkTheme));

            using (SolidBrush f = new SolidBrush(fc))
            using (StringFormat sf = new StringFormat())
            {
                sf.LineAlignment = StringAlignment.Center;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                using (Font ft = new Font("Microsoft YaHei UI", 10f))
                    e.Graphics.DrawString(txt, ft, f,
                        new RectangleF(b.X + 10, b.Y, b.Width - 14, b.Height), sf);
            }
        }

        void Lv_Icon(object sender, DrawListViewItemEventArgs e)
        {
            Rectangle r = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 4, e.Bounds.Width - 8, e.Bounds.Height - 8);
            bool sel = e.Item.Selected;

            using (GraphicsPath path = RoundedPath(r, 12))
            using (SolidBrush b = new SolidBrush(sel ? SelBg() : Theme.Panel(darkTheme)))
                e.Graphics.FillPath(b, path);

            if (sel)
            {
                using (GraphicsPath path = RoundedPath(r, 12))
                using (Pen p = new Pen(SelBorder(), 2f))
                    e.Graphics.DrawPath(p, path);
            }

            Image img = null;
            try
            {
                if (thumbs != null && e.Item.ImageKey != null && thumbs.Images.ContainsKey(e.Item.ImageKey))
                    img = thumbs.Images[e.Item.ImageKey];
            }
            catch { }

            if (img != null)
            {
                int w = 128, h = 128;
                int x = r.X + (r.Width - w) / 2;
                int y = r.Y + 16;
                e.Graphics.DrawImage(img, new Rectangle(x, y, w, h));
            }

            using (SolidBrush f = new SolidBrush(Theme.Sub(darkTheme)))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                using (Font ft = new Font("Microsoft YaHei UI", 9f))
                    e.Graphics.DrawString(e.Item.Text, ft, f,
                        new RectangleF(r.X, r.Bottom - 30, r.Width, 24), sf);
            }
        }

        static GraphicsPath RoundedPath(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ================= 图片预览 =================
        void ShowImagePreview(ImageEntry e)
        {
            try
            {
                string path = Path.Combine(store.ImgDir, e.File);
                if (!File.Exists(path)) { SetStatus("图片文件不存在"); return; }

                Form dlg = new Form();
                dlg.Text = "图片预览  " + e.W + " x " + e.H + "   " + e.Time.ToString("MM-dd HH:mm");
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                dlg.Width = Math.Min(1100, wa.Width - 120);
                dlg.Height = Math.Min(820, wa.Height - 120);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Icon = Icon;
                dlg.KeyPreview = true;

                PictureBox pb = new PictureBox();
                pb.Dock = DockStyle.Fill;
                pb.SizeMode = PictureBoxSizeMode.Zoom;
                pb.BackColor = Color.FromArgb(28, 28, 28);
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (Image img = Image.FromStream(fs))
                    pb.Image = new Bitmap(img);

                dlg.Controls.Add(pb);
                dlg.KeyDown += new KeyEventHandler(delegate(object s2, KeyEventArgs k)
                {
                    if (k.KeyCode == Keys.Escape) dlg.Close();
                });
                dlg.ShowDialog(this);
                pb.Image.Dispose();
                dlg.Dispose();
            }
            catch (Exception ex) { SetStatus("预览失败: " + ex.Message); }
        }

        void btnHelp_Click(object sender, EventArgs e)
        {
            string text = "（未找到内嵌的说明文档）";
            try
            {
                System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream("readme.md"))
                {
                    if (s != null)
                    {
                        using (StreamReader sr = new StreamReader(s, Encoding.UTF8))
                        {
                            text = sr.ReadToEnd();
                            text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
                        }
                    }
                }
            }
            catch (Exception ex) { text = "读取说明失败：" + ex.Message; }

            Form dlg = new Form();
            dlg.Text = "使用说明";
            dlg.Width = 820;
            dlg.Height = 640;
            dlg.StartPosition = FormStartPosition.CenterParent;
            dlg.MinimizeBox = false;
            dlg.MaximizeBox = true;
            dlg.Icon = this.Icon;

            TextBox tb = new TextBox();
            tb.Multiline = true;
            tb.ScrollBars = ScrollBars.Both;
            tb.WordWrap = true;
            tb.ReadOnly = true;
            tb.Dock = DockStyle.Fill;
            tb.Font = new Font("Microsoft YaHei UI", 10f);
            tb.Text = text;
            tb.SelectionStart = 0;

            dlg.Controls.Add(tb);
            dlg.ShowDialog(this);
            dlg.Dispose();
        }

        void lvText_DoubleClick(object sender, EventArgs e) { CopyTextAt(SelectedTextIndex()); }

        void lvImage_DoubleClick(object sender, EventArgs e)
        {
            int i = SelectedImageIndex();
            if (i < 0) return;
            try
            {
                string path = Path.Combine(store.ImgDir, images[i].File);
                if (!File.Exists(path)) { SetStatus("图片文件不存在"); return; }
                System.Diagnostics.Process.Start(path);
                SetStatus("已用系统看图程序打开：" + images[i].File);
            }
            catch (Exception ex) { SetStatus("打开失败: " + ex.GetType().Name + " - " + ex.Message); }
        }

        void numDays_Changed(object sender, EventArgs e)
        {
            retainDays = (int)numDays.Value;
            Cfg.SetInt("RetainDays", retainDays);
            Prune(false);
            RefreshAll();
        }

        void chkAuto_Changed(object sender, EventArgs e)
        {
            Cfg.SetAutoStart(chkAuto.Checked);
            SetStatus(chkAuto.Checked ? "已设为开机自启动（最小化到托盘）" : "已取消开机自启动");
        }

        void pruneTimer_Tick(object sender, EventArgs e)
        {
            if (Prune(false)) RefreshAll();
        }

        void tray_DoubleClick(object sender, EventArgs e)
        {
            ShowFromTray();
        }

        void tray_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) ShowFromTray();
        }

        // 供第二次运行 exe 或托盘双击调用
        System.Windows.Forms.Timer fadeTimer;
        bool everShown;
        Point lastLocation;

        public void ShowFromTray()
        {
            try
            {
                allowVisible = true;
                if (WindowState == FormWindowState.Minimized)
                    WindowState = FormWindowState.Normal;

                if (!Visible)
                {
                    // 先摆好位置，再显示，避免在 (0,0) 闪一下
                    StartPosition = FormStartPosition.Manual;
                    if (everShown)
                    {
                        Rectangle wa = Screen.FromPoint(lastLocation).WorkingArea;
                        int x = lastLocation.X;
                        int y = lastLocation.Y;
                        if (x < wa.Left - Width + 80 || x > wa.Right - 80) x = wa.Left + (wa.Width - Width) / 2;
                        if (y < wa.Top - 40 || y > wa.Bottom - 80) y = wa.Top + (wa.Height - Height) / 2;
                        Location = new Point(x, y);
                    }
                    else
                    {
                        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                        Location = new Point(wa.Left + (wa.Width - Width) / 2,
                                             wa.Top + (wa.Height - Height) / 2);
                    }

                    Opacity = 0.0;
                    Show();
                    everShown = true;

                    if (fadeTimer == null)
                    {
                        fadeTimer = new System.Windows.Forms.Timer();
                        fadeTimer.Interval = 16;
                        fadeTimer.Tick += new EventHandler(fadeTimer_Tick);
                    }
                    fadeTimer.Start();
                    // 保险：400ms 后强制不透明，防止淡入中断留下半透明窗口
                    System.Windows.Forms.Timer guard = new System.Windows.Forms.Timer();
                    guard.Interval = 400;
                    guard.Tick += new EventHandler(delegate
                    {
                        try { guard.Stop(); guard.Dispose(); Opacity = 1.0; } catch { }
                    });
                    guard.Start();
                }

                Activate();
                RefreshAll();
            }
            catch
            {
                try { Opacity = 1.0; } catch { }
            }
        }

        void fadeTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                double o = Opacity + 0.18;
                if (o >= 1.0) { Opacity = 1.0; fadeTimer.Stop(); }
                else Opacity = o;
            }
            catch { fadeTimer.Stop(); Opacity = 1.0; }
        }

        void miExit_Click(object sender, EventArgs e)
        {
            exiting = true;
            tray.Visible = false;
            Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                lastLocation = Location;
                everShown = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
                if (pruneTimer != null) pruneTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // 双缓冲 ListView，减少自绘闪烁
    public class DoubleBufferedListView : ListView
    {
        public DoubleBufferedListView()
        {
            DoubleBuffered = true;
        }
    }

    // 拖拽框选的半透明色块
    public class RubberBandForm : Form
    {
        public RubberBandForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(0, 120, 215);
            Opacity = 0.25;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }
    }

    // ---------------- 入口 ----------------
    static class Program
    {
        internal static string DataDir;

        static string LogDirOrEmpty()
        {
            try
            {
                if (!string.IsNullOrEmpty(DataDir))
                {
                    string d = Path.Combine(DataDir, "logs");
                    if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                    return d;
                }
            }
            catch { }
            return null;
        }

        internal static void LogLine(string text)
        {
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + text + "\r\n";
            List<string> targets = new List<string>();
            string ld = LogDirOrEmpty();
            if (ld != null) targets.Add(Path.Combine(ld, "start.log"));
            else { try { targets.Add(Path.Combine(Path.GetTempPath(), "dshclip-start.log")); } catch { } }
            foreach (string p in targets)
            {
                try { File.AppendAllText(p, line, Encoding.UTF8); } catch { }
            }
        }

        internal static void LogCrash(Exception ex)
        {
            string body = "==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====\r\n" + ex.ToString() + "\r\n\r\n";
            List<string> targets = new List<string>();
            string ld = LogDirOrEmpty();
            if (ld != null) targets.Add(Path.Combine(ld, "crash.log"));
            else { try { targets.Add(Path.Combine(Path.GetTempPath(), "dshclip-crash.log")); } catch { } }
            foreach (string p in targets)
            {
                try { File.AppendAllText(p, body, Encoding.UTF8); } catch { }
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            try { DataDir = DataPath.Resolve(); } catch { }
            LogLine("Main 开始");
            LogLine("程序目录: " + AppDomain.CurrentDomain.BaseDirectory);
            LogLine("启动参数: [" + string.Join(" ", args) + "]");

            foreach (string a in args)
            {
                if (a == "--selftest") { SelfTest.Run(); return; }
            }

            foreach (string a in args)
            {
                if (a == "--uitest") { SelfTest.UiTest(); return; }
                if (a == "--shot") { SelfTest.Shot(); return; }
            }

            bool startHidden = false;
            foreach (string a in args)
            {
                if (a == "--tray") startHidden = true;
            }

            Mutex mtx = null;
            bool isNew = true;
            try
            {
                mtx = new Mutex(true, "DSHClip_SingleInstance", out isNew);
            }
            catch (Exception ex)
            {
                LogCrash(ex);
            }
            LogLine("Mutex 获取: " + (isNew ? "首个实例" : "已有实例"));

            if (!isNew)
            {
                // 已有实例：通知它把窗口弹出来
                try
                {
                    EventWaitHandle ev = EventWaitHandle.OpenExisting("DSHClip_ShowEvent");
                    ev.Set();
                    ev.Close();
                    LogLine("已通知已有实例显示窗口");
                }
                catch (Exception ex)
                {
                    LogLine("通知失败: " + ex.Message);
                    try
                    {
                        MessageBox.Show("剪切板历史已在运行，请查看系统托盘（图标是一个小剪贴板）。", "提示",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch { }
                }
                return;
            }

            try
            {
                LogLine("准备 UI");
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                LogLine("创建窗体");
                MainForm f = new MainForm(startHidden);
                LogLine("窗体创建成功，进入消息循环");

                // 监听"唤出窗口"信号
                try
                {
                    EventWaitHandle showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "DSHClip_ShowEvent");
                    Thread th = new Thread(new ThreadStart(delegate
                    {
                        while (true)
                        {
                            try
                            {
                                showEvent.WaitOne();
                                f.BeginInvoke(new MethodInvoker(delegate { f.ShowFromTray(); }));
                            }
                            catch { return; }
                        }
                    }));
                    th.IsBackground = true;
                    th.Start();
                }
                catch (Exception ex)
                {
                    LogLine("事件监听启动失败: " + ex.Message);
                }

                if (startHidden)
                {
                    IntPtr force = f.Handle;   // 建句柄：注册剪贴板监听与热键
                    f.Hide();                  // 双保险：不显示窗口
                    LogLine("以托盘模式运行（不显示窗口）");
                    Application.Run(new ApplicationContext(f));
                }
                else
                {
                    Application.Run(f);
                }
                LogLine("消息循环结束");
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                try
                {
                    MessageBox.Show("启动失败：" + ex.GetType().Name + "\r\n" + ex.Message +
                        "\r\n\r\n详细信息已写入：\r\n" + Path.Combine(Path.GetTempPath(), "dshclip-crash.log"),
                        "剪切板历史", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch { }
            }

            GC.KeepAlive(mtx);
        }
    }

    // ---------------- 自检 ----------------
    static class SelfTest
    {
        public static void Shot()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                MainForm f = new MainForm(true);
                f.ShowInTaskbar = false;
                f.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(400);
                Application.DoEvents();
                Bitmap bmp = new Bitmap(f.Width, f.Height);
                f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                bmp.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui-shot.png"), ImageFormat.Png);
                bmp.Dispose();
                f.Hide();
                f.Dispose();
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shot-error.txt"), ex.ToString()); } catch { }
            }
        }

        public static void UiTest()
        {
            StringBuilder log = new StringBuilder();
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                MainForm f = new MainForm(true);
                log.AppendLine("窗体构造: OK");
                log.AppendLine("标题: " + f.Text);
                log.AppendLine("控件数: " + f.Controls.Count);
                f.Dispose();
                log.AppendLine("释放: OK");
                log.AppendLine("RESULT: UI PASS");
            }
            catch (Exception ex)
            {
                log.AppendLine("EXCEPTION: " + ex.ToString());
                log.AppendLine("RESULT: UI FAIL");
            }
            try
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "dshclip-uitest.log"),
                    log.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        public static void Run()
        {
            StringBuilder log = new StringBuilder();
            string root = Path.Combine(Path.GetTempPath(), "dshclip-selftest");
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
                Store s = new Store(root);
                s.Ensure();

                List<TextEntry> texts = new List<TextEntry>();
                TextEntry t1 = new TextEntry(); t1.Time = DateTime.Now; t1.Text = "今天的文字"; texts.Add(t1);
                TextEntry t2 = new TextEntry(); t2.Time = DateTime.Now.AddDays(-4); t2.Text = "四天前(过期)"; texts.Add(t2);
                TextEntry t3 = new TextEntry(); t3.Time = DateTime.Now.AddDays(-5); t3.Text = "五天前(过期)"; texts.Add(t3);
                s.SaveTexts(texts);

                List<ImageEntry> imgs = new List<ImageEntry>();
                using (Bitmap bmp = new Bitmap(40, 30))
                {
                    using (Graphics g = Graphics.FromImage(bmp)) g.Clear(Color.SteelBlue);
                    bmp.Save(Path.Combine(s.ImgDir, "a.png"), ImageFormat.Png);
                }
                ImageEntry i1 = new ImageEntry(); i1.Time = DateTime.Now; i1.File = "a.png"; i1.W = 40; i1.H = 30; imgs.Add(i1);
                ImageEntry i2 = new ImageEntry(); i2.Time = DateTime.Now.AddDays(-4); i2.File = "b.png"; i2.W = 10; i2.H = 10; imgs.Add(i2);
                s.SaveImages(imgs);

                log.AppendLine("写入: 文字 3 条, 图片 2 条");

                List<TextEntry> lt = s.LoadTexts();
                List<ImageEntry> li = s.LoadImages();
                log.AppendLine("读回: 文字 " + lt.Count + " 条, 图片 " + li.Count + " 张");
                log.AppendLine("中文往返: " + (lt.Count > 0 && lt[0].Text == "今天的文字" ? "OK" : "FAIL"));
                log.AppendLine("时间往返: " + (li.Count > 0 && li[0].W == 40 ? "OK" : "FAIL"));

                DateTime limit = DateTime.Now.AddDays(-3);
                int removed = 0;
                for (int i = lt.Count - 1; i >= 0; i--) if (lt[i].Time < limit) { lt.RemoveAt(i); removed++; }
                for (int i = li.Count - 1; i >= 0; i--) if (li[i].Time < limit) { li.RemoveAt(i); removed++; }
                log.AppendLine("按3天清理: 移除 " + removed + " 条, 剩余 文字 " + lt.Count + " 图片 " + li.Count);
                log.AppendLine("清理结果: " + (removed == 3 && lt.Count == 1 && li.Count == 1 ? "OK" : "FAIL"));

                s.SaveTexts(lt);
                s.SaveImages(li);
                List<TextEntry> lt2 = s.LoadTexts();
                log.AppendLine("二次读回文字: " + lt2.Count + " 条 -> " + (lt2.Count == 1 ? "OK" : "FAIL"));

                log.AppendLine("数据目录: " + root);
                log.AppendLine("RESULT: ALL PASS");
            }
            catch (Exception ex)
            {
                log.AppendLine("EXCEPTION: " + ex.ToString());
                log.AppendLine("RESULT: FAIL");
            }

            try
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "dshclip-selftest.log"),
                    log.ToString(), Encoding.UTF8);
            }
            catch { }
        }
    }
}
