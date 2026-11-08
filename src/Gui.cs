// AiBypass — окно в духе Happ: тёмное, одна большая кнопка, всё лишнее спрятано.
//
// На главном экране только состояние и кнопка включения. Прокси, хосты и журнал
// лежат под «Настройками» и по умолчанию не мешают.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace AiBypass
{
    internal static class Theme
    {
        public static readonly Color Page = Color.FromArgb(11, 12, 16);
        public static readonly Color Card = Color.FromArgb(20, 22, 28);
        public static readonly Color Border = Color.FromArgb(35, 40, 50);
        public static readonly Color Text = Color.FromArgb(230, 233, 239);
        public static readonly Color Muted = Color.FromArgb(139, 147, 167);

        public static readonly Color Accent = Color.FromArgb(107, 91, 255);
        public static readonly Color AccentHover = Color.FromArgb(126, 112, 255);
        public static readonly Color Neutral = Color.FromArgb(30, 33, 42);
        public static readonly Color NeutralHover = Color.FromArgb(42, 46, 58);

        public static readonly Color Green = Color.FromArgb(43, 217, 124);
    }

    internal class FlatButton : Button
    {
        public Color FillNormal = Theme.Neutral;
        public Color FillHover = Theme.NeutralHover;
        public Color TextColor = Theme.Text;
        public int Radius = 8;
        public Color BackFill = Theme.Card;
        private bool hover;

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9f, FontStyle.Regular);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs pe)
        {
            using (SolidBrush bg = new SolidBrush(BackFill)) { pe.Graphics.FillRectangle(bg, ClientRectangle); }

            pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = !Enabled ? Color.FromArgb(40, 44, 54) : (hover ? FillHover : FillNormal);

            using (GraphicsPath path = Shapes.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
            using (SolidBrush brush = new SolidBrush(fill))
            {
                pe.Graphics.FillPath(brush, path);
            }

            Color tc = Enabled ? TextColor : Theme.Muted;
            TextRenderer.DrawText(pe.Graphics, Text, Font, ClientRectangle, tc,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    internal class CardPanel : Panel
    {
        public CardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Shapes.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 12))
            {
                using (SolidBrush b = new SolidBrush(Theme.Card)) { pe.Graphics.FillPath(b, path); }
                using (Pen p = new Pen(Theme.Border, 1)) { pe.Graphics.DrawPath(p, path); }
            }
            base.OnPaint(pe);
        }
    }

    internal static class Shapes
    {
        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (d > r.Height) { d = r.Height; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal static class Gui
    {
        private const int W = 560;
        private const int HeightClosed = 360;
        private const int HeightOpen = 940;

        private static Form form;
        private static Label stateLabel;
        private static Label detailLabel;
        private static FlatButton bigButton;
        private static FlatButton settingsLink;
        private static Panel settingsPanel;

        private static TextBox proxyBox;
        private static TextBox sourceBox;
        private static CheckedListBox serviceList;
        private static TextBox log;
        private static NotifyIcon tray;
        private static bool reallyExit;

        public static void Run()
        {
            form = new Form();
            form.Text = "AiBypass";
            form.ClientSize = new Size(W, HeightClosed);
            form.StartPosition = FormStartPosition.CenterScreen;
            form.FormBorderStyle = FormBorderStyle.None;
            form.MaximizeBox = false;
            form.MinimizeBox = true;
            try
            {
                using (System.IO.Stream st = System.Reflection.Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("AiBypass.app.ico"))
                {
                    if (st != null) { form.Icon = new Icon(st); }
                }
            }
            catch { }
            form.Paint += new PaintEventHandler(delegate (object s, PaintEventArgs e)
            {
                // тонкая рамка вместо стандартной белой от Windows
                using (Pen p = new Pen(Theme.Border, 1))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, form.ClientSize.Width - 1, form.ClientSize.Height - 1);
                }
            });
            form.Resize += new EventHandler(delegate (object s, EventArgs e) { form.Invalidate(); });
            form.BackColor = Theme.Page;
            form.Font = new Font("Segoe UI", 9f);

            BuildHeader();
            BuildMain();
            BuildSettings();
            BuildLog();

            // Закрытие окна больше НЕ убивает релей: иначе браузеры, запущенные с ключом
            // --proxy-server, остаются с настройкой на мёртвый порт и перестают открывать
            // вообще всё - и гит, и ютуб, и вообще любые сайты.
            // Окно прячется в трей, релей продолжает работать.
            form.FormClosing += new FormClosingEventHandler(delegate (object s, FormClosingEventArgs e)
            {

                bool stopping = e.CloseReason == CloseReason.WindowsShutDown ||
                                e.CloseReason == CloseReason.TaskManagerClosing;

                if (!reallyExit && !stopping)
                {
                    e.Cancel = true;
                    form.Hide();

                    if (tray != null)
                    {
                        tray.Visible = true;
                        tray.ShowBalloonTip(2000, "AiBypass", "Программа свёрнута. Прокси продолжает работать. Выход — через значок в трее.", ToolTipIcon.Info);
                    }
                    return;
                }
                if (Relay.IsRunning) { Relay.Stop(null); }
                try { Core.SetSystemProxy(""); } catch { }
            });

            BuildTray();

            // самолечение: если в системе остался наш прокси от прошлого запуска (программа
            // упала, компьютер перезагрузили), он ведёт на мёртвый порт и ломает вообще все сайты.
            try
            {
                string sp = Core.SystemProxy();
                if (sp != null && sp.IndexOf("127.0.0.1", StringComparison.Ordinal) >= 0)
                {
                    Core.SetSystemProxy("");
                }

                // если прошлый запуск оставил браузеры с нашим ключом прокси - вернуть их в норму
                if (Core.BrowsersMarked())
                {
                    List<string> heal = new List<string>();
                    Core.RestartProcesses(Core.BrowserProcesses, heal, false, null, "");
                    Core.MarkBrowsersWithProxy(false);
                    WriteAll(heal);
                }
            }
            catch { }

            LoadSelection();

            // при каждом запуске тянем актуальный адрес прокси из своего файла на GitHub
            string src = Core.LoadProxySource();
            sourceBox.Text = src;
            if (src.Length > 0)
            {
                List<string> lines = new List<string>();
                string got = Core.FetchProxyFromSource(src, lines);
                WriteAll(lines);
                if (got.Length > 0)
                {
                    proxyBox.Text = got;
                    Core.SaveProxyUrl(got);
                }
            }
            if (proxyBox.Text.Trim().Length == 0) { proxyBox.Text = Core.LoadProxyUrl(); }

            RefreshState();
            Application.Run(form);
        }

        private static void BuildTray()
        {
            Bitmap bmp = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (SolidBrush b = new SolidBrush(Theme.Accent)) { g.FillEllipse(b, 1, 1, 14, 14); }
            }
            Icon ic = Icon.FromHandle(bmp.GetHicon());

            tray = new NotifyIcon();
            tray.Icon = ic;
            tray.Text = "AiBypass";
            tray.Visible = false;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Показать", null, new EventHandler(delegate (object s, EventArgs e)
            {
                form.Show();
                form.WindowState = FormWindowState.Normal;
                form.Activate();
            }));
            menu.Items.Add("Выключить прокси", null, new EventHandler(delegate (object s, EventArgs e)
            {
                if (Relay.IsRunning) { TurnOff(); }
            }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Выход", null, new EventHandler(delegate (object s, EventArgs e)
            {
                reallyExit = true;
                TurnOff();
                if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
                Application.Exit();
            }));
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += new EventHandler(delegate (object s, EventArgs e)
            {
                form.Show();
                form.WindowState = FormWindowState.Normal;
                form.Activate();
            });
        }

        // ---------- сборка окна ----------

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private static void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) { return; }
            ReleaseCapture();
            SendMessage(form.Handle, 0xA1, 0x2, 0); // WM_NCLBUTTONDOWN, HTCAPTION
        }

        private static void BuildHeader()
        {
            Panel header = new Panel();
            header.SetBounds(0, 0, W, 84);
            header.MouseDown += new MouseEventHandler(DragWindow);
            header.Paint += new PaintEventHandler(delegate (object s, PaintEventArgs e)
            {
                using (SolidBrush b = new SolidBrush(Theme.Page)) { e.Graphics.FillRectangle(b, header.ClientRectangle); }
                using (Font f1 = new Font("Segoe UI", 16f, FontStyle.Bold))
                using (Font f2 = new Font("Segoe UI", 8.5f))
                {
                    TextRenderer.DrawText(e.Graphics, "AiBypass", f1, new Point(20, 20), Theme.Text);
                    TextRenderer.DrawText(e.Graphics, "обход блокировок для Gemini и других сервисов",
                        f2, new Point(21, 50), Theme.Muted);
                }
            });
            // свои кнопки вместо виндовых
            FlatButton bMin = new FlatButton();
            bMin.Text = "—";
            bMin.SetBounds(W - 84, 16, 32, 30);
            bMin.Radius = 6;
            bMin.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            bMin.TextColor = Theme.Muted;
            bMin.BackFill = Theme.Page;
            bMin.Click += new EventHandler(delegate (object s, EventArgs e) { form.WindowState = FormWindowState.Minimized; });
            header.Controls.Add(bMin);

            FlatButton bClose = new FlatButton();
            bClose.Text = "✕";
            bClose.SetBounds(W - 46, 16, 32, 30);
            bClose.Radius = 6;
            bClose.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            bClose.TextColor = Theme.Muted;
            bClose.FillNormal = Theme.Page;
            bClose.FillHover = Color.FromArgb(60, 30, 36);
            bClose.BackFill = Theme.Page;
            bClose.Click += new EventHandler(delegate (object s, EventArgs e) { form.Close(); });
            header.Controls.Add(bClose);

            form.Controls.Add(header);
        }

        private static void BuildMain()
        {
            stateLabel = new Label();
            stateLabel.SetBounds(20, 94, W - 40, 30);
            stateLabel.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
            stateLabel.ForeColor = Theme.Text;
            form.Controls.Add(stateLabel);

            detailLabel = new Label();
            detailLabel.SetBounds(21, 126, W - 42, 40);
            detailLabel.Font = new Font("Segoe UI", 8.5f);
            detailLabel.ForeColor = Theme.Muted;
            form.Controls.Add(detailLabel);

            bigButton = new FlatButton();
            bigButton.SetBounds(20, 174, W - 40, 84);
            bigButton.Radius = 24;
            bigButton.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
            bigButton.FillNormal = Theme.Accent;
            bigButton.FillHover = Theme.AccentHover;
            bigButton.TextColor = Color.White;
            bigButton.BackFill = Theme.Page;
            bigButton.Click += new EventHandler(OnBigButton);
            form.Controls.Add(bigButton);

            settingsLink = new FlatButton();
            settingsLink.SetBounds(20, 270, 130, 30);
            settingsLink.Radius = 8;
            settingsLink.Text = "Настройки";
            settingsLink.Font = new Font("Segoe UI", 8.5f);
            settingsLink.TextColor = Theme.Muted;
            settingsLink.BackFill = Theme.Page;
            settingsLink.Click += new EventHandler(OnToggleSettings);
            form.Controls.Add(settingsLink);
        }

        private static void BuildSettings()
        {
            settingsPanel = new Panel();
            settingsPanel.SetBounds(0, 308, W, HeightOpen - 308);
            settingsPanel.BackColor = Theme.Page;
            settingsPanel.Visible = false;
            form.Controls.Add(settingsPanel);

            CardPanel card = new CardPanel();
            card.SetBounds(12, 0, W - 24, 300);
            settingsPanel.Controls.Add(card);

            Label l1 = new Label();
            l1.Text = "Адрес прокси: socks5://ЛОГИН:ПАРОЛЬ@АДРЕС:ПОРТ";
            l1.SetBounds(16, 16, 500, 18);
            l1.ForeColor = Theme.Muted;
            card.Controls.Add(l1);

            proxyBox = NewTextBox(16, 36, 496);
            card.Controls.Add(proxyBox);

            Label l2 = new Label();
            l2.Text = "Откуда брать актуальный адрес (raw-ссылка на файл в GitHub)";
            l2.SetBounds(16, 70, 500, 18);
            l2.ForeColor = Theme.Muted;
            card.Controls.Add(l2);

            sourceBox = NewTextBox(16, 90, 496);
            card.Controls.Add(sourceBox);

            FlatButton bSave = NewButton("Сохранить", 16, 124, 140, OnSaveSettings);
            card.Controls.Add(bSave);

            FlatButton bTest = NewButton("Проверить", 164, 124, 130, OnProxyTest);
            card.Controls.Add(bTest);

            FlatButton bPull = NewButton("Обновить с GitHub", 302, 124, 210, OnPullProxy);
            card.Controls.Add(bPull);

            Label l3 = new Label();
            l3.Text = "Хосты: что заворачивать на адрес commonnet (отдельная схема, не прокси)";
            l3.SetBounds(16, 162, 500, 18);
            l3.ForeColor = Theme.Muted;
            card.Controls.Add(l3);

            serviceList = new CheckedListBox();
            serviceList.SetBounds(16, 182, 496, 62);
            serviceList.CheckOnClick = true;
            serviceList.BorderStyle = BorderStyle.FixedSingle;
            serviceList.BackColor = Theme.Neutral;
            serviceList.ForeColor = Theme.Text;
            serviceList.Font = new Font("Segoe UI", 8.5f);
            for (int i = 0; i < Services.Count; i++) { serviceList.Items.Add(Services.Caption(Services.All[i])); }
            card.Controls.Add(serviceList);

            FlatButton bApplyHosts = NewButton("Записать хосты", 16, 252, 160, OnApplyHosts);
            card.Controls.Add(bApplyHosts);

            FlatButton bOffHosts = NewButton("Снять хосты", 184, 252, 150, OnOffHosts);
            card.Controls.Add(bOffHosts);
        }

        private static void BuildLog()
        {
            CardPanel card = new CardPanel();
            card.SetBounds(12, 312, W - 24, HeightOpen - 330);
            settingsPanel.Controls.Add(card);

            log = new TextBox();
            log.SetBounds(14, 14, W - 52, card.Height - 28);
            log.Multiline = true;
            log.ScrollBars = ScrollBars.Vertical;
            log.ReadOnly = true;
            log.BorderStyle = BorderStyle.None;
            log.BackColor = Theme.Card;
            log.ForeColor = Theme.Muted;
            log.Font = new Font("Consolas", 8.5f);
            card.Controls.Add(log);
        }

        private static TextBox NewTextBox(int left, int top, int width)
        {
            TextBox tb = new TextBox();
            tb.SetBounds(left, top, width, 26);
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.BackColor = Theme.Neutral;
            tb.ForeColor = Theme.Text;
            tb.Font = new Font("Consolas", 9f);
            return tb;
        }

        private static FlatButton NewButton(string text, int left, int top, int width, EventHandler h)
        {
            FlatButton b = new FlatButton();
            b.Text = text;
            b.SetBounds(left, top, width, 30);
            b.BackFill = Theme.Card;
            b.Click += h;
            return b;
        }

        // ---------- состояние ----------

        private static void RefreshState()
        {
            if (Relay.IsRunning)
            {
                bigButton.Text = "Выключить";
                bigButton.FillNormal = Theme.Neutral;
                bigButton.FillHover = Theme.NeutralHover;
                bigButton.TextColor = Theme.Text;

                stateLabel.Text = "Включено";
                stateLabel.ForeColor = Theme.Green;
                detailLabel.Text = "Google идёт через " + Relay.CurrentUpstream + "\r\nОстальные сайты — напрямую, как обычно";
            }
            else
            {
                bigButton.Text = "Включить";
                bigButton.FillNormal = Theme.Accent;
                bigButton.FillHover = Theme.AccentHover;
                bigButton.TextColor = Color.White;

                stateLabel.Text = "Выключено";
                stateLabel.ForeColor = Theme.Muted;
                string p = proxyBox == null ? "" : proxyBox.Text.Trim();
                detailLabel.Text = p.Length > 0
                    ? "Прокси задан: " + Core.MaskProxy(p) + "\r\nНажми «Включить», чтобы пустить через него Google"
                    : "Прокси не задан — открой «Настройки» и вставь адрес";
            }
        }

        private static void WriteAll(List<string> lines)
        {
            for (int i = 0; i < lines.Count; i++) { Write(lines[i]); }
        }

        private static void Write(string s)
        {
            if (log == null) { return; }
            log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s + Environment.NewLine);
        }

        // ---------- действия ----------

        private static void OnToggleSettings(object sender, EventArgs e)
        {
            settingsPanel.Visible = !settingsPanel.Visible;
            form.ClientSize = new Size(W, settingsPanel.Visible ? HeightOpen : HeightClosed);
            settingsLink.Text = settingsPanel.Visible ? "Свернуть" : "Настройки";
        }

        private static void OnBigButton(object sender, EventArgs e)
        {
            try
            {
                if (Relay.IsRunning) { TurnOff(); return; }

                string url = proxyBox.Text.Trim();
                if (url.Length == 0)
                {
                    Write("Прокси не задан. Открой «Настройки» и вставь адрес.");
                    OnToggleSettings(null, null);
                    return;
                }

                ProxySettings ps = ProxySettings.Parse(url);
                List<string> lines = new List<string>();
                Relay.Start(ps, lines);
                WriteAll(lines);
                Core.SetSystemProxy("127.0.0.1:" + Relay.LocalPort);
                Core.SaveProxyUrl(url);
                Write("Включено. Перезапускаю браузеры — они сами откроют Gemini.");
                RefreshState();
                Application.DoEvents();

                List<string> bl = new List<string>();
                Core.RestartProcesses(Core.BrowserProcesses, bl, false,
                    "https://gemini.google.com/app", "--proxy-server=http://127.0.0.1:" + Relay.LocalPort);
                Core.MarkBrowsersWithProxy(true);
                WriteAll(bl);
            }
            catch (Exception ex)
            {
                Write("Не вышло: " + ex.Message);
                try { Relay.Stop(null); Core.SetSystemProxy(""); } catch { }
                RefreshState();
            }
        }

        private static void TurnOff()
        {
            Relay.Stop(null);
            Core.SetSystemProxy("");

            // браузеры, запущенные нами с ключом прокси, надо вернуть в норму:
            // иначе они останутся с настройкой на мёртвый порт
            if (Core.BrowsersMarked())
            {
                Write("Выключено. Перезапускаю браузеры без прокси.");
                RefreshState();
                Application.DoEvents();
                List<string> bl = new List<string>();
                Core.RestartProcesses(Core.BrowserProcesses, bl, false, null, "");
                Core.MarkBrowsersWithProxy(false);
                WriteAll(bl);
            }
            else
            {
                Write("Выключено. Прокси снят.");
                RefreshState();
            }
        }

        private static void OnSaveSettings(object sender, EventArgs e)
        {
            try
            {
                Core.SaveProxyUrl(proxyBox.Text.Trim());
                Core.SaveProxySource(sourceBox.Text.Trim());
                Write("Настройки сохранены.");
                RefreshState();
            }
            catch (Exception ex) { Write("Не вышло: " + ex.Message); }
        }

        private static void OnProxyTest(object sender, EventArgs e)
        {
            try
            {
                ProxySettings ps = ProxySettings.Parse(proxyBox.Text.Trim());
                Write("Проверяю " + ps.Display + " ...");
                Application.DoEvents();
                string r = Relay.TestProxy(ps, "gemini.google.com", "/app", 20000);
                Write("  gemini.google.com -> " + r);
                Write(r == "200" ? "Прокси живой." : "Ответ не 200 — проверь прокси в личном кабинете.");
            }
            catch (Exception ex) { Write("Не вышло: " + ex.Message); }
        }

        private static void OnPullProxy(object sender, EventArgs e)
        {
            try
            {
                string src = sourceBox.Text.Trim();
                if (src.Length == 0) { Write("Укажи ссылку на файл с адресом прокси."); return; }
                List<string> lines = new List<string>();
                string got = Core.FetchProxyFromSource(src, lines);
                WriteAll(lines);
                if (got.Length == 0) { Write("В файле не нашёл адрес прокси."); return; }
                proxyBox.Text = got;
                Core.SaveProxyUrl(got);
                Core.SaveProxySource(src);
                Write("Адрес обновлён: " + Core.MaskProxy(got));
                RefreshState();
            }
            catch (Exception ex) { Write("Не вышло: " + ex.Message); }
        }

        // ---------- хосты ----------

        private static void LoadSelection()
        {
            bool fromFile = false;
            try
            {
                if (File.Exists(Core.SettingsPath))
                {
                    fromFile = true;
                    string[] names = File.ReadAllLines(Core.SettingsPath);
                    for (int i = 0; i < Services.Count; i++)
                    {
                        bool on = false;
                        for (int k = 0; k < names.Length; k++)
                        {
                            if (names[k].Trim() == Services.All[i].Name) { on = true; break; }
                        }
                        serviceList.SetItemChecked(i, on);
                    }
                }
            }
            catch { }
            if (!fromFile)
            {
                for (int i = 0; i < Services.Count; i++)
                {
                    serviceList.SetItemChecked(i, Services.DefaultOn(Services.All[i]));
                }
            }
        }

        private static string[] SelectedDomains()
        {
            List<string> outp = new List<string>();
            for (int i = 0; i < Services.Count; i++)
            {
                if (!serviceList.GetItemChecked(i)) { continue; }
                for (int j = 0; j < Services.All[i].Domains.Length; j++)
                {
                    if (!outp.Contains(Services.All[i].Domains[j])) { outp.Add(Services.All[i].Domains[j]); }
                }
            }
            return outp.ToArray();
        }

        private static void OnApplyHosts(object sender, EventArgs e)
        {
            try
            {
                string ip = Core.CurrentProxyIp();
                if (ip.Length == 0) { Write("Адрес commonnet неизвестен — сначала впиши его в строку адреса."); return; }
                string[] doms = SelectedDomains();
                Core.ApplyHosts(ip, doms);
                Write("Записал " + doms.Length + " доменов на " + ip + ".");
            }
            catch (Exception ex) { Write("Не вышло: " + ex.Message); }
        }

        private static void OnOffHosts(object sender, EventArgs e)
        {
            try
            {
                Core.OffHosts();
                Write("Блок из hosts снят.");
            }
            catch (Exception ex) { Write("Не вышло: " + ex.Message); }
        }
    }
}
