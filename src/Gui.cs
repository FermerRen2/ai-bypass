// AiBypass — оформление окна.
//
// Главный экран: градиентный фон, живое свечение вокруг кнопки, одна большая кнопка.
// Никаких галочек и списков: всё работает сразу. Настройки (свой прокси, журнал)
// открываются по ссылке внизу и по умолчанию не мешают.

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
        public static readonly Color BgTop = Color.FromArgb(15, 17, 30);
        public static readonly Color BgBottom = Color.FromArgb(7, 8, 13);
        public static readonly Color Card = Color.FromArgb(22, 25, 38);
        public static readonly Color Border = Color.FromArgb(40, 46, 66);
        public static readonly Color Text = Color.FromArgb(237, 240, 248);
        public static readonly Color Muted = Color.FromArgb(132, 141, 168);

        public static readonly Color AccentA = Color.FromArgb(126, 92, 255);
        public static readonly Color AccentB = Color.FromArgb(64, 190, 255);
        public static readonly Color Green = Color.FromArgb(58, 226, 145);
    }

    internal static class Ui
    {
        public static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            if (d > r.Height) { d = r.Height; }
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // Кнопка с градиентом, свечением и плавным наведением
    internal class GlowButton : Button
    {
        public Color ColorA = Theme.AccentA;
        public Color ColorB = Theme.AccentB;
        public Color TextColor = Color.White;
        public int Radius = 26;
        public bool Glow = true;
        public float Pulse = 0f;
        private float hoverT;
        private bool hover;

        public GlowButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Theme.BgTop;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI Semibold", 12.5f, FontStyle.Bold);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        public void Step()
        {
            float target = hover ? 1f : 0f;
            hoverT += (target - hoverT) * 0.25f;
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            Graphics g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(BackColor)) { g.FillRectangle(bg, ClientRectangle); }

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            if (Glow)
            {
                float a = 0.20f + Pulse * 0.22f + hoverT * 0.12f;
                for (int i = 7; i >= 1; i--)
                {
                    int grow = i * 3;
                    Rectangle gr = new Rectangle(r.X - grow, r.Y - grow, r.Width + grow * 2, r.Height + grow * 2);
                    int alpha = (int)(a * 255f / (i * 1.6f));
                    if (alpha < 3) { continue; }
                    using (GraphicsPath gp = Ui.Round(gr, Radius + grow))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, ColorA)))
                    {
                        g.FillPath(b, gp);
                    }
                }
            }

            using (GraphicsPath path = Ui.Round(r, Radius))
            {
                using (LinearGradientBrush br = new LinearGradientBrush(
                    new Point(0, 0), new Point(Width, Height), ColorA, ColorB))
                {
                    ColorBlend blend = new ColorBlend(3);
                    blend.Colors = new Color[] { ColorA, Mix(ColorA, ColorB, 0.5f), ColorB };
                    blend.Positions = new float[] { 0f, 0.5f, 1f };
                    br.InterpolationColors = blend;
                    g.FillPath(br, path);
                }

                using (GraphicsPath hp = Ui.Round(new Rectangle(r.X + 2, r.Y + 2, r.Width - 4, r.Height / 2), Radius - 2))
                using (LinearGradientBrush hb = new LinearGradientBrush(
                    new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height / 2)),
                    Color.FromArgb(60 + (int)(hoverT * 40), 255, 255, 255), Color.FromArgb(0, 255, 255, 255),
                    LinearGradientMode.Vertical))
                {
                    g.FillPath(hb, hp);
                }
            }

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, TextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }
    }

    internal class FlatButton : Button
    {
        public Color FillHover = Color.FromArgb(40, 46, 66);
        public Color TextColor = Theme.Muted;
        public int Radius = 9;
        private bool hover;

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Theme.BgTop;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9f);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs pe)
        {
            pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            // заливаем фон целиком: иначе текст рисуется поверх прошлого кадра и «двоится»
            using (SolidBrush bg = new SolidBrush(BackColor)) { pe.Graphics.FillRectangle(bg, ClientRectangle); }
            if (hover)
            {
                using (GraphicsPath p = Ui.Round(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(150, FillHover))) { pe.Graphics.FillPath(b, p); }
            }
            TextRenderer.DrawText(pe.Graphics, Text, Font, ClientRectangle, TextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // Кнопки окна: символы рисуем сами, шрифтовые глифы на разных системах выглядят по-разному
    internal class WinButton : Button
    {
        public bool IsClose = false;
        private bool hover;

        public WinButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Theme.BgTop;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs pe)
        {
            Graphics g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(BackColor)) { g.FillRectangle(bg, ClientRectangle); }
            if (hover)
            {
                Color fill = IsClose ? Color.FromArgb(90, 40, 52) : Color.FromArgb(40, 46, 66);
                using (GraphicsPath p = Ui.Round(new Rectangle(0, 0, Width - 1, Height - 1), 8))
                using (SolidBrush b = new SolidBrush(fill)) { g.FillPath(b, p); }
            }
            Color col = hover ? Color.White : Theme.Muted;
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, col,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    internal class CardPanel : Panel
    {
        public CardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.BgTop;
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Ui.Round(new Rectangle(0, 0, Width - 1, Height - 1), 14))
            {
                using (SolidBrush b = new SolidBrush(Theme.Card)) { pe.Graphics.FillPath(b, path); }
                using (Pen p = new Pen(Theme.Border, 1)) { pe.Graphics.DrawPath(p, path); }
            }
            base.OnPaint(pe);
        }
    }

    internal static class Gui
    {
        private const string RepoUrl = "https://github.com/FermerRen2/ai-bypass";
        private const int W = 560;
        private const int HeightClosed = 400;
        private const int HeightOpen = 900;

        private static Form form;
        private static Label stateLabel;
        private static Label detailLabel;
        private static GlowButton bigButton;
        private static FlatButton settingsLink;
        private static Panel settingsPanel;
        private static Timer anim;

        private static TextBox proxyBox;
        private static TextBox sourceBox;
        private static TextBox log;

        private static float pulsePhase;
        private static NotifyIcon tray;
        private static bool reallyExit;

        public static void Run()
        {
            form = new Form();
            form.Text = "AiBypass";
            form.ClientSize = new Size(W, HeightClosed);
            form.StartPosition = FormStartPosition.CenterScreen;
            form.FormBorderStyle = FormBorderStyle.None;
            form.BackColor = Theme.BgBottom;
            form.Font = new Font("Segoe UI", 9f);
            try
            {
                typeof(Control).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(form, true, null);
            }
            catch { }
            try
            {
                using (Stream st = System.Reflection.Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("AiBypass.app.ico"))
                {
                    if (st != null) { form.Icon = new Icon(st); }
                }
            }
            catch { }

            form.Paint += new PaintEventHandler(PaintBackground);
            form.Resize += new EventHandler(delegate (object s, EventArgs e) { form.Invalidate(); });

            BuildHeader();
            BuildMain();
            BuildSettings();
            BuildLog();

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
                        tray.ShowBalloonTip(2500, "AiBypass",
                            "Свёрнуто в трей, прокси работает. Выход — правой кнопкой по значку.",
                            ToolTipIcon.Info);
                    }
                    return;
                }
                if (Relay.IsRunning) { Relay.Stop(null); }
                try { Core.SetSystemProxyPac(false); } catch { }
            });

            BuildTray();

            try
            {
                string sp = Core.SystemProxy();
                if (sp != null && sp.IndexOf("127.0.0.1", StringComparison.Ordinal) >= 0)
                {
                    Core.SetSystemProxy("");
                }
                string pac = Core.SystemPac();
                if (pac != null && pac.IndexOf("127.0.0.1", StringComparison.Ordinal) >= 0)
                {
                    Core.SetSystemProxyPac(false);
                }
                if (Core.BrowsersMarked())
                {
                    List<string> heal = new List<string>();
                    Core.RestartProcesses(Core.BrowserProcesses, heal, false, null, "");
                    Core.MarkBrowsersWithProxy(false);
                    WriteAll(heal);
                }
            }
            catch { }

            string src = Core.LoadProxySource();
            sourceBox.Text = src;
            if (src.Length > 0)
            {
                List<string> lines = new List<string>();
                string got = Core.FetchProxyFromSource(src, lines);
                WriteAll(lines);
                if (got.Length > 0) { proxyBox.Text = got; Core.SaveProxyUrl(got); }
            }
            if (proxyBox.Text.Trim().Length == 0) { proxyBox.Text = Core.LoadProxyUrl(); }

            RefreshState();

            anim = new Timer();
            anim.Interval = 40;
            anim.Tick += new EventHandler(delegate (object s, EventArgs e)
            {
                pulsePhase += 0.045f;
                if (pulsePhase > (float)(Math.PI * 2)) { pulsePhase -= (float)(Math.PI * 2); }
                bigButton.Pulse = (float)((Math.Sin(pulsePhase) + 1) / 2);
                bigButton.Step();
                bigButton.Invalidate();
                form.Invalidate();
            });
            anim.Start();

            Application.Run(form);
        }

        private static void PaintBackground(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (form == null) { return; }
            Rectangle full = new Rectangle(0, 0, form.ClientSize.Width, form.ClientSize.Height);
            using (SolidBrush bg = new SolidBrush(Theme.BgTop))
            {
                g.FillRectangle(bg, full);
            }

            float pulse = bigButton == null ? 0.5f : bigButton.Pulse;
            float k = 0.55f + pulse * 0.25f;
            int cx = full.Width / 2;
            int cy = 250;
            for (int i = 8; i >= 1; i--)
            {
                int rad = i * 46;
                int alpha = (int)(6 * k);
                if (alpha < 2) { continue; }
                using (GraphicsPath p = new GraphicsPath())
                {
                    p.AddEllipse(cx - rad, cy - rad, rad * 2, rad * 2);
                    using (PathGradientBrush pb = new PathGradientBrush(p))
                    {
                        pb.CenterColor = Color.FromArgb(alpha, Theme.AccentA);
                        pb.SurroundColors = new Color[] { Color.FromArgb(0, Theme.AccentA) };
                        g.FillPath(pb, p);
                    }
                }
            }

            using (Pen border = new Pen(Color.FromArgb(70, 255, 255, 255), 1))
            {
                g.DrawRectangle(border, 0, 0, full.Width - 1, full.Height - 1);
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private static void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) { return; }
            ReleaseCapture();
            SendMessage(form.Handle, 0xA1, 0x2, 0);
        }

        private static void BuildHeader()
        {
            Panel header = new Panel();
            header.SetBounds(0, 0, W, 92);
            header.BackColor = Theme.BgTop;
            header.MouseDown += new MouseEventHandler(DragWindow);
            header.Paint += new PaintEventHandler(delegate (object s, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush bg = new SolidBrush(Theme.BgTop)) { e.Graphics.FillRectangle(bg, header.ClientRectangle); }
                using (Font f1 = new Font("Segoe UI Semibold", 17f, FontStyle.Bold))
                using (Font f2 = new Font("Segoe UI", 8.5f))
                {
                    TextRenderer.DrawText(e.Graphics, "AiBypass", f1, new Point(24, 22), Theme.Text);
                    TextRenderer.DrawText(e.Graphics, "Gemini и другие сервисы — одной кнопкой",
                        f2, new Point(25, 56), Theme.Muted);
                }
            });

            WinButton bMin = new WinButton();
            bMin.Text = "-";
            bMin.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            bMin.SetBounds(W - 92, 22, 32, 30);
            bMin.Click += new EventHandler(delegate (object s, EventArgs e) { form.WindowState = FormWindowState.Minimized; });
            header.Controls.Add(bMin);

            WinButton bClose = new WinButton();
            bClose.IsClose = true;
            bClose.Text = "X";
            bClose.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            bClose.SetBounds(W - 54, 22, 32, 30);
            bClose.Click += new EventHandler(delegate (object s, EventArgs e) { form.Close(); });
            header.Controls.Add(bClose);

            form.Controls.Add(header);
        }

        private static void BuildMain()
        {
            stateLabel = new Label();
            stateLabel.SetBounds(24, 108, W - 48, 30);
            stateLabel.Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold);
            stateLabel.ForeColor = Theme.Text;
            stateLabel.BackColor = Theme.BgTop;
            form.Controls.Add(stateLabel);

            detailLabel = new Label();
            detailLabel.SetBounds(25, 142, W - 50, 44);
            detailLabel.Font = new Font("Segoe UI", 8.5f);
            detailLabel.ForeColor = Theme.Muted;
            detailLabel.BackColor = Theme.BgTop;
            form.Controls.Add(detailLabel);

            bigButton = new GlowButton();
            bigButton.SetBounds(28, 200, W - 56, 86);
            bigButton.Click += new EventHandler(OnBigButton);
            form.Controls.Add(bigButton);

            settingsLink = new FlatButton();
            settingsLink.SetBounds(24, 306, 140, 32);
            settingsLink.Text = "Настройки";
            settingsLink.Font = new Font("Segoe UI", 8.5f);
            settingsLink.BackColor = Color.FromArgb(26, 30, 44);
            settingsLink.FillHover = Color.FromArgb(40, 46, 66);
            settingsLink.Click += new EventHandler(OnToggleSettings);
            form.Controls.Add(settingsLink);

            FlatButton repoLink = new FlatButton();
            repoLink.SetBounds(W - 190, 306, 166, 32);
            repoLink.Text = "GitHub · v1.1.11";
            repoLink.Font = new Font("Segoe UI", 8.5f);
            repoLink.BackColor = Theme.BgTop;
            repoLink.Click += new EventHandler(OpenRepo);
            form.Controls.Add(repoLink);
        }

        private static void BuildSettings()
        {
            settingsPanel = new Panel();
            settingsPanel.SetBounds(0, 350, W, HeightOpen - 350);
            settingsPanel.BackColor = Theme.BgTop;
            settingsPanel.Visible = false;
            form.Controls.Add(settingsPanel);

            CardPanel card = new CardPanel();
            card.SetBounds(16, 0, W - 32, 210);
            settingsPanel.Controls.Add(card);

            Label l1 = new Label();
            l1.Text = "Адрес прокси: socks5://ЛОГИН:ПАРОЛЬ@АДРЕС:ПОРТ";
            l1.SetBounds(18, 16, 480, 18);
            l1.ForeColor = Theme.Muted;
            l1.BackColor = Theme.BgTop;
            card.Controls.Add(l1);

            proxyBox = NewTextBox(18, 36, 484);
            card.Controls.Add(proxyBox);

            Label l2 = new Label();
            l2.Text = "Откуда брать актуальный адрес (raw-ссылка на файл в GitHub)";
            l2.SetBounds(18, 72, 480, 18);
            l2.ForeColor = Theme.Muted;
            l2.BackColor = Theme.BgTop;
            card.Controls.Add(l2);

            sourceBox = NewTextBox(18, 92, 484);
            card.Controls.Add(sourceBox);

            FlatButton bSave = NewButton("Сохранить", 18, 130, 140, OnSaveSettings);
            card.Controls.Add(bSave);

            FlatButton bTest = NewButton("Проверить", 168, 130, 130, OnProxyTest);
            card.Controls.Add(bTest);

            FlatButton bPull = NewButton("Обновить с GitHub", 308, 130, 194, OnPullProxy);
            card.Controls.Add(bPull);

            Label hint = new Label();
            hint.Text = "Исходники и новые версии: github.com/FermerRen2/ai-bypass";
            hint.SetBounds(18, 172, 484, 18);
            hint.ForeColor = Theme.Muted;
            hint.BackColor = Theme.BgTop;
            hint.Font = new Font("Segoe UI", 8f);
            card.Controls.Add(hint);
        }

        private static void BuildLog()
        {
            CardPanel card = new CardPanel();
            card.SetBounds(16, 222, W - 32, (HeightOpen - 350) - 238);
            settingsPanel.Controls.Add(card);

            log = new TextBox();
            log.SetBounds(16, 14, W - 66, card.Height - 28);
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
            tb.BackColor = Color.FromArgb(30, 34, 50);
            tb.ForeColor = Theme.Text;
            tb.Font = new Font("Consolas", 9f);
            return tb;
        }

        private static FlatButton NewButton(string text, int left, int top, int width, EventHandler h)
        {
            FlatButton b = new FlatButton();
            b.Text = text;
            b.SetBounds(left, top, width, 32);
            b.FillHover = Color.FromArgb(46, 52, 76);
            b.TextColor = Theme.Text;
            b.Font = new Font("Segoe UI", 9f);
            b.Click += h;
            return b;
        }

        private static void BuildTray()
        {
            Bitmap bmp = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (LinearGradientBrush b = new LinearGradientBrush(
                    new Rectangle(0, 0, 16, 16), Theme.AccentA, Theme.AccentB, 45f))
                {
                    g.FillEllipse(b, 1, 1, 14, 14);
                }
            }
            tray = new NotifyIcon();
            tray.Icon = Icon.FromHandle(bmp.GetHicon());
            tray.Text = "AiBypass";
            tray.Visible = false;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Показать", null, new EventHandler(delegate (object s, EventArgs e)
            {
                form.Show(); form.WindowState = FormWindowState.Normal; form.Activate();
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
                form.Show(); form.WindowState = FormWindowState.Normal; form.Activate();
            });
        }

        private static void RefreshState()
        {
            if (Relay.IsRunning)
            {
                bigButton.Text = "Выключить";
                bigButton.ColorA = Color.FromArgb(52, 66, 96);
                bigButton.ColorB = Color.FromArgb(36, 44, 66);
                bigButton.Glow = false;

                stateLabel.Text = "Прокси включён";
                stateLabel.ForeColor = Theme.Green;
                detailLabel.Text = "Через прокси: Gemini и Grok" +
                                   "\r\nВсё остальное — напрямую, включая YouTube и GitHub";
            }
            else
            {
                bigButton.Text = "Включить";
                bigButton.ColorA = Theme.AccentA;
                bigButton.ColorB = Theme.AccentB;
                bigButton.Glow = true;

                stateLabel.Text = "Выключено";
                stateLabel.ForeColor = Theme.Text;
                string p = proxyBox == null ? "" : proxyBox.Text.Trim();
                detailLabel.Text = p.Length > 0
                    ? "Прокси: " + Core.MaskProxy(p) + "\r\nНажми «Включить» — браузеры откроют Gemini сами"
                    : "Прокси не задан — открой «Настройки» и вставь адрес";
            }
            if (form != null) { form.Invalidate(); }
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

        private static void OpenRepo(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(RepoUrl);
                Write("Открыл страницу программы: " + RepoUrl);
            }
            catch (Exception ex) { Write("Не смог открыть ссылку: " + ex.Message); }
        }

        private static void OnToggleSettings(object sender, EventArgs e)
        {
            settingsPanel.Visible = !settingsPanel.Visible;
            form.ClientSize = new Size(W, settingsPanel.Visible ? HeightOpen : HeightClosed);
            settingsLink.Text = settingsPanel.Visible ? "Свернуть" : "Настройки";
            form.Invalidate();
        }

        private static void OnBigButton(object sender, EventArgs e)
        {
            try
            {
                if (Relay.IsRunning) { TurnOff(); return; }

                string url = proxyBox.Text.Trim();
                if (url.Length == 0) { url = Core.LoadProxyUrl(); proxyBox.Text = url; }
                if (url.Length == 0)
                {
                    Write("Прокси не задан. Открой «Настройки» и вставь адрес.");
                    if (!settingsPanel.Visible) { OnToggleSettings(null, null); }
                    return;
                }

                ProxySettings ps = ProxySettings.Parse(url);
                List<string> lines = new List<string>();
                Relay.Start(ps, lines);
                WriteAll(lines);
                Core.SetSystemProxyPac(true);
                Core.SaveProxyUrl(url);
                Write("Включено. Через прокси пойдут только Gemini и Grok, остальное — напрямую.");
                RefreshState();
                Application.DoEvents();

                List<string> bl = new List<string>();
                Core.RestartProcesses(Core.BrowserProcesses, bl, false,
                    "https://gemini.google.com/app", "");
                WriteAll(bl);
            }
            catch (Exception ex)
            {
                Write("Не вышло: " + ex.Message);
                try { Relay.Stop(null); Core.SetSystemProxyPac(false); } catch { }
                RefreshState();
            }
        }

        private static void TurnOff()
        {
            Relay.Stop(null);
            Core.SetSystemProxyPac(false);
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
                Write("Выключено.");
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
                if (src.Length == 0)
                {
                    Write("Ссылка не задана, и это нормально: актуальный адрес уже вшит в программу.");
                    return;
                }
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
    }
}
