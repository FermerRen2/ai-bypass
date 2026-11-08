// AiBypass — окно программы.
// WinForms без сторонних библиотек: скруглённые кнопки и карточки рисуются вручную.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AiBypass
{
    internal static class Theme
    {
        public static readonly Color Page = Color.FromArgb(245, 247, 250);
        public static readonly Color Card = Color.White;
        public static readonly Color Border = Color.FromArgb(226, 232, 240);
        public static readonly Color Header = Color.FromArgb(15, 23, 42);
        public static readonly Color HeaderSub = Color.FromArgb(148, 163, 184);
        public static readonly Color Text = Color.FromArgb(30, 41, 59);
        public static readonly Color Muted = Color.FromArgb(100, 116, 139);

        public static readonly Color Accent = Color.FromArgb(37, 99, 235);
        public static readonly Color AccentHover = Color.FromArgb(29, 78, 216);
        public static readonly Color Neutral = Color.FromArgb(241, 245, 249);
        public static readonly Color NeutralHover = Color.FromArgb(226, 232, 240);
        public static readonly Color Danger = Color.FromArgb(185, 28, 28);

        public static readonly Color Green = Color.FromArgb(21, 128, 61);
        public static readonly Color Red = Color.FromArgb(185, 28, 28);
        public static readonly Color Amber = Color.FromArgb(180, 83, 9);
    }

    // Кнопка со скруглением и подсветкой при наведении.
    internal class FlatButton : Button
    {
        public Color FillNormal = Theme.Neutral;
        public Color FillHover = Theme.NeutralHover;
        public Color TextColor = Theme.Text;
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
            // сначала заливаем весь прямоугольник цветом подложки: иначе на скруглённых
            // углах и по краям остаются белые квадраты от стандартной отрисовки кнопки
            using (SolidBrush bg = new SolidBrush(BackFill))
            {
                pe.Graphics.FillRectangle(bg, ClientRectangle);
            }

            pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill;
            if (!Enabled) { fill = Color.FromArgb(226, 232, 240); }
            else { fill = hover ? FillHover : FillNormal; }

            using (GraphicsPath path = Shapes.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 7))
            using (SolidBrush brush = new SolidBrush(fill))
            {
                pe.Graphics.FillPath(brush, path);
            }

            Color tc = Enabled ? TextColor : Color.FromArgb(148, 163, 184);
            TextRenderer.DrawText(pe.Graphics, Text, Font, ClientRectangle, tc,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // Белая карточка со скруглением, тонкой рамкой и заголовком.
    internal class CardPanel : Panel
    {
        public string Title = "";

        public CardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Shapes.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 10))
            {
                using (SolidBrush b = new SolidBrush(Theme.Card)) { pe.Graphics.FillPath(b, path); }
                using (Pen p = new Pen(Theme.Border, 1)) { pe.Graphics.DrawPath(p, path); }
            }

            if (Title.Length > 0)
            {
                using (Font f = new Font("Segoe UI", 9f, FontStyle.Bold))
                {
                    TextRenderer.DrawText(pe.Graphics, Title, f, new Point(18, 12), Theme.Muted);
                }
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
        private static Form form;
        private static Panel header;
        private static Label statusLabel;
        private static TextBox ipBox;
        private static TextBox linkBox;
        private static TextBox dnsBox;
        private static TextBox log;
        private static CheckedListBox serviceList;
        private static CheckBox restartBrowser;

        public static void Run()
        {
            form = new Form();
            form.Text = "AiBypass";
            form.ClientSize = new Size(740, 856);
            form.StartPosition = FormStartPosition.CenterScreen;
            form.FormBorderStyle = FormBorderStyle.FixedSingle;
            form.MaximizeBox = false;
            form.BackColor = Theme.Page;
            form.Font = new Font("Segoe UI", 9f);

            BuildHeader();
            BuildStatus();
            BuildConnectionCard();
            BuildServicesCard();
            BuildDnsCard();
            BuildLogCard();

            LoadSelection();
            RefreshState();

            Write("Готово. Если старый адрес перестал работать - вставь ссылку на сервис и нажми «Найти адрес».");
            Write("Программа поищет адрес в их hosts-файле, на сайте и в ответе их DNS, а потом проверит его.");

            if (!Core.IsAdmin())
            {
                Write("ВНИМАНИЕ: запущено без прав администратора - менять hosts и правила DNS не получится.");
            }

            Application.Run(form);
        }

        // ---------- сборка окна ----------

        private static void BuildHeader()
        {
            header = new Panel();
            header.SetBounds(0, 0, 740, 76);
            header.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            header.Paint += new PaintEventHandler(delegate (object s, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.None;
                using (SolidBrush b = new SolidBrush(Theme.Header))
                {
                    e.Graphics.FillRectangle(b, header.ClientRectangle);
                }
                using (Font f1 = new Font("Segoe UI", 15f, FontStyle.Bold))
                using (Font f2 = new Font("Segoe UI", 8.5f))
                {
                    TextRenderer.DrawText(e.Graphics, "AiBypass", f1, new Point(20, 14), Color.White);
                    TextRenderer.DrawText(e.Graphics, "обход геоблокировок через hosts и DNS",
                        f2, new Point(20, 46), Theme.HeaderSub);
                }
            });
            form.Controls.Add(header);
        }

        private static void BuildStatus()
        {
            statusLabel = new Label();
            statusLabel.SetBounds(18, 90, 704, 26);
            statusLabel.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            statusLabel.ForeColor = Theme.Muted;
            form.Controls.Add(statusLabel);
        }

        private static void BuildConnectionCard()
        {
            CardPanel card = new CardPanel();
            card.Title = "ПОДКЛЮЧЕНИЕ";
            card.SetBounds(12, 124, 716, 200);
            form.Controls.Add(card);

            Label l0 = new Label();
            l0.Text = "Ссылка на сервис (сайт, репозиторий с hosts или DNS-имя)";
            l0.SetBounds(18, 34, 500, 18);
            l0.ForeColor = Theme.Muted;
            card.Controls.Add(l0);

            linkBox = new TextBox();
            linkBox.SetBounds(18, 54, 420, 26);
            linkBox.BorderStyle = BorderStyle.FixedSingle;
            linkBox.Font = new Font("Consolas", 9.5f);
            card.Controls.Add(linkBox);

            FlatButton bFind = MakeButton("Найти адрес", 446, 53, 118, 28, OnFind);
            card.Controls.Add(bFind);

            FlatButton bSrv = MakeButton("commonnet", 570, 53, 122, 28, OnServiceDefault);
            card.Controls.Add(bSrv);

            Label l1 = new Label();
            l1.Text = "Адрес прокси (IPv4)";
            l1.SetBounds(18, 90, 300, 18);
            l1.ForeColor = Theme.Muted;
            card.Controls.Add(l1);

            ipBox = new TextBox();
            ipBox.SetBounds(18, 110, 210, 26);
            ipBox.BorderStyle = BorderStyle.FixedSingle;
            ipBox.Font = new Font("Consolas", 11f);
            card.Controls.Add(ipBox);

            FlatButton bCheck = MakeButton("Проверить", 236, 109, 124, 28, OnCheck);
            card.Controls.Add(bCheck);

            FlatButton bApply = MakeButton("Применить", 368, 109, 116, 28, OnApply);
            bApply.FillNormal = Theme.Accent;
            bApply.FillHover = Theme.AccentHover;
            bApply.TextColor = Color.White;
            bApply.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            card.Controls.Add(bApply);

            FlatButton bOff = MakeButton("Выключить обход", 492, 109, 200, 28, OnOff);
            bOff.TextColor = Theme.Danger;
            card.Controls.Add(bOff);

            restartBrowser = new CheckBox();
            restartBrowser.Text = "Перезапускать браузер после «Применить» (иначе он держит старые адреса)";
            restartBrowser.SetBounds(18, 148, 460, 22);
            restartBrowser.ForeColor = Theme.Text;
            restartBrowser.Checked = true;
            card.Controls.Add(restartBrowser);

            FlatButton bFix = MakeButton("Починить Gemini", 492, 144, 200, 30, OnFixGemini);
            bFix.FillNormal = Theme.Accent;
            bFix.FillHover = Theme.AccentHover;
            bFix.TextColor = Color.White;
            bFix.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            card.Controls.Add(bFix);
        }

        private static void BuildServicesCard()
        {
            CardPanel card = new CardPanel();
            card.Title = "ЧТО ЗАВОРАЧИВАТЬ НА ПРОКСИ";
            card.SetBounds(12, 336, 716, 268);
            form.Controls.Add(card);

            serviceList = new CheckedListBox();
            serviceList.SetBounds(18, 34, 680, 170);
            serviceList.CheckOnClick = true;
            serviceList.BorderStyle = BorderStyle.FixedSingle;
            serviceList.Font = new Font("Segoe UI", 9.5f);
            serviceList.HorizontalScrollbar = false;
            for (int i = 0; i < Services.Count; i++)
            {
                serviceList.Items.Add(Services.Caption(Services.All[i]));
            }
            card.Controls.Add(serviceList);

            FlatButton bAll = MakeButton("Отметить все", 18, 210, 126, 28, OnSelectAll);
            card.Controls.Add(bAll);

            FlatButton bNone = MakeButton("Снять все", 152, 210, 118, 28, OnSelectNone);
            card.Controls.Add(bNone);

            Label legend = new Label();
            legend.Text = "Серый текст справа — предупреждение. Gemini через прокси ломается: страница из Европы, запросы из РФ.";
            legend.SetBounds(18, 244, 680, 18);
            legend.ForeColor = Theme.Muted;
            legend.Font = new Font("Segoe UI", 8f);
            card.Controls.Add(legend);
        }

        private static void BuildDnsCard()
        {
            CardPanel card = new CardPanel();
            card.Title = "ЕСЛИ СЕРВИС ДАЁТ DNS-СЕРВЕРЫ, А НЕ ПРОКСИ-IP";
            card.SetBounds(12, 616, 716, 106);
            form.Controls.Add(card);

            Label l = new Label();
            l.Text = "Адреса через запятую (только числа, имена Windows не принимает)";
            l.SetBounds(18, 34, 480, 18);
            l.ForeColor = Theme.Muted;
            card.Controls.Add(l);

            dnsBox = new TextBox();
            dnsBox.SetBounds(18, 56, 360, 26);
            dnsBox.BorderStyle = BorderStyle.FixedSingle;
            dnsBox.Font = new Font("Consolas", 10f);
            card.Controls.Add(dnsBox);

            FlatButton bOn = MakeButton("Поставить правила", 390, 55, 150, 28, OnNrptSet);
            card.Controls.Add(bOn);

            FlatButton bOff = MakeButton("Снять правила", 548, 55, 144, 28, OnNrptOff);
            card.Controls.Add(bOff);
        }

        private static void BuildLogCard()
        {
            CardPanel card = new CardPanel();
            card.SetBounds(12, 734, 716, 106);
            form.Controls.Add(card);

            log = new TextBox();
            log.SetBounds(18, 16, 680, 76);
            log.Multiline = true;
            log.ScrollBars = ScrollBars.Vertical;
            log.ReadOnly = true;
            log.BorderStyle = BorderStyle.None;
            log.BackColor = Theme.Card;
            log.Font = new Font("Consolas", 8.5f);
            log.ForeColor = Theme.Text;
            card.Controls.Add(log);
        }

        private static FlatButton MakeButton(string text, int left, int top, int width, int height, EventHandler h)
        {
            FlatButton b = new FlatButton();
            b.Text = text;
            b.SetBounds(left, top, width, height);
            b.Click += h;
            return b;
        }

        // ---------- журнал и состояние ----------

        private static void Write(string s)
        {
            if (log == null) { return; }
            log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s + Environment.NewLine);
        }

        private static void RefreshState()
        {
            string ip = Core.CurrentProxyIp();
            if (ip.Length > 0)
            {
                statusLabel.Text = "Обход включён: " + Core.CurrentDomainCount() + " доменов идут на " + ip;
                statusLabel.ForeColor = Theme.Green;
                if (ipBox.Text.Trim().Length == 0) { ipBox.Text = ip; }
            }
            else
            {
                statusLabel.Text = "Обход выключен: домены резолвятся как обычно";
                statusLabel.ForeColor = Theme.Red;
            }
        }

        // ---------- выбор сервисов ----------

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

        private static void SaveSelection()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < Services.Count; i++)
                {
                    if (serviceList.GetItemChecked(i)) { sb.AppendLine(Services.All[i].Name); }
                }
                File.WriteAllText(Core.SettingsPath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

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
                // по умолчанию включено всё, кроме сервисов, которые через прокси ломаются
                for (int i = 0; i < Services.Count; i++)
                {
                    serviceList.SetItemChecked(i, Services.DefaultOn(Services.All[i]));
                }
            }
        }

        private static void OnSelectAll(object sender, EventArgs e)
        {
            for (int i = 0; i < serviceList.Items.Count; i++) { serviceList.SetItemChecked(i, true); }
            SaveSelection();
            for (int i = 0; i < Services.Count; i++)
            {
                Service s = Services.All[i];
                if (s.Status == ServiceStatus.DoNotEnable)
                {
                    Write("Обрати внимание: " + s.Name + " через прокси работать не будет — " + s.Note + ".");
                }
            }
        }

        private static void OnSelectNone(object sender, EventArgs e)
        {
            for (int i = 0; i < serviceList.Items.Count; i++) { serviceList.SetItemChecked(i, false); }
            SaveSelection();
        }

        // ---------- действия ----------

        private static void OnFind(object sender, EventArgs e)
        {
            string src = linkBox.Text.Trim();
            if (src.Length == 0)
            {
                Write("Сначала вставь ссылку на сервис в верхнее поле.");
                return;
            }
            Write("Ищу адрес по: " + src);
            Application.DoEvents();
            try
            {
                List<string> lines = new List<string>();
                string ip = Core.FindAndVerify(src, lines);
                for (int i = 0; i < lines.Count; i++) { Write("  " + lines[i]); }
                if (ip.Length == 0)
                {
                    Write("Адрес не найден. Вручную: 1) у сервиса есть файл hosts - найди строку с chatgpt.com;");
                    Write("2) если это DNS-сервер - спроси его: nslookup chatgpt.com <адрес сервера>;");
                    Write("3) ищи в их инструкции строку из четырёх чисел.");
                    return;
                }
                ipBox.Text = ip;
                Write("Подставил в поле адреса: " + ip + ". Теперь «Применить».");
            }
            catch (Exception ex) { Write("Не вышло: " + ex.Message); }
        }

        private static void OnServiceDefault(object sender, EventArgs e)
        {
            linkBox.Text = "https://github.com/ASTRACAT2022/host-DNS";
            OnFind(sender, e);
        }

        private static void OnCheck(object sender, EventArgs e)
        {
            string ip = ipBox.Text.Trim();
            if (!Core.IsValidIp(ip)) { Write("Это не IPv4-адрес: " + ip); return; }
            Write("Проверяю " + ip + " (система не меняется, только замер)...");
            Application.DoEvents();

            string[][] tests = new string[][]
            {
                new string[] { "chatgpt.com", "/robots.txt" },
                new string[] { "gemini.google.com", "/" },
                new string[] { "open.spotify.com", "/" },
                new string[] { "grok.com", "/" }
            };
            for (int i = 0; i < tests.Length; i++)
            {
                string code = Core.CheckSiteWithCert(ip, tests[i][0], tests[i][1], 9000);
                Write("  " + tests[i][0] + " -> " + code + "  (" + Core.Verdict(code) + ")");
            }
            Write("200 - страница открылась; 401 - просто нет ключа; 403 - челлендж Cloudflare, браузер пройдёт.");
        }

        private static void OnApply(object sender, EventArgs e)
        {
            string ip = ipBox.Text.Trim();
            try
            {
                // про сервисы, которые через прокси ломаются, говорим прямо перед применением
                for (int i = 0; i < Services.Count; i++)
                {
                    if (!serviceList.GetItemChecked(i)) { continue; }
                    Service s = Services.All[i];
                    if (s.Status == ServiceStatus.DoNotEnable)
                    {
                        Write("ВНИМАНИЕ: " + s.Name + " включён через прокси — " + s.Note + ".");
                        Write("Обычно его надо держать выключенным: он работает напрямую, без прокси.");
                    }
                }

                string[] domains = SelectedDomains();
                Core.ApplyHosts(ip, domains);
                SaveSelection();
                Write("Прописал " + domains.Length + " доменов на " + ip + ". Бэкап hosts сохранён.");
                RefreshState();

                if (restartBrowser.Checked)
                {
                    Write("Перезапускаю браузер: он держит свой DNS-кэш и старые соединения...");
                    Application.DoEvents();
                    List<string> logLines = new List<string>();
                    List<string> done = Core.RestartProcesses(Core.BrowserProcesses, logLines, false);
                    for (int i = 0; i < logLines.Count; i++) { Write("  " + logLines[i]); }
                    if (done.Count == 0) { Write("  запущенных браузеров не нашёл - перезапусти сам, если открыт."); }
                }
            }
            catch (Exception ex) { Write("Не вышло: " + ex.Message); }
        }

        // Одна кнопка на случай «Gemini открывается, но запросы не обрабатываются»:
        // убираем Gemini с прокси, сбрасываем кэш DNS, перезапускаем браузер и открываем сайт.
        private static void OnFixGemini(object sender, EventArgs e)
        {
            try
            {
                Write("--- Чиню Gemini ---");
                string ip = ipBox.Text.Trim();
                if (!Core.IsValidIp(ip)) { ip = Core.CurrentProxyIp(); }

                string[] domains = DomainsWithoutGemini();
                Write("Убираю Gemini с прокси, остальные сервисы оставляю (доменов: " + domains.Length + ").");
                Core.ApplyHosts(ip, domains);
                Core.FlushDns();

                for (int i = 0; i < Services.Count; i++)
                {
                    if (Services.All[i].Name.IndexOf("Gemini", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        serviceList.SetItemChecked(i, false);
                    }
                }
                SaveSelection();
                RefreshState();

                Write("Перезапускаю браузер - это и есть аналог режима полёта на телефоне.");
                Write("Если в браузере есть несохранённые формы, они пропадут.");
                Application.DoEvents();

                List<string> logLines = new List<string>();
                List<string> done = Core.RestartProcesses(Core.BrowserProcesses, logLines, true);
                for (int i = 0; i < logLines.Count; i++) { Write("  " + logLines[i]); }

                if (done.Count == 0) { Write("  запущенных браузеров не нашёл - открой gemini.google.com вручную."); }
                Write("Готово. В hosts " + Core.CurrentDomainCount() + " доменов, Gemini идёт напрямую.");
            }
            catch (Exception ex) { Write("Не вышло: " + ex.Message); }
        }

        // домены всех сервисов, кроме отмеченных как «не включать»
        private static string[] DomainsWithoutGemini()
        {
            List<string> outp = new List<string>();
            for (int i = 0; i < Services.Count; i++)
            {
                if (Services.All[i].Status == ServiceStatus.DoNotEnable) { continue; }
                for (int j = 0; j < Services.All[i].Domains.Length; j++)
                {
                    if (!outp.Contains(Services.All[i].Domains[j])) { outp.Add(Services.All[i].Domains[j]); }
                }
            }
            return outp.ToArray();
        }

        private static void OnOff(object sender, EventArgs e)
        {
            try
            {
                Core.OffHosts();
                Write("Блок из hosts убран, кэш DNS сброшен.");
                RefreshState();
            }
            catch (Exception ex) { Write("Не вышло: " + ex.Message); }
        }

        private static void OnNrptSet(object sender, EventArgs e)
        {
            try
            {
                Core.NrptSet(dnsBox.Text.Trim());
                Write("Правила DNS поставлены: " + dnsBox.Text.Trim());
                Write("Сейчас в системе: " + Core.NrptState());
            }
            catch (Exception ex) { Write("Не вышло: " + ex.Message); }
        }

        private static void OnNrptOff(object sender, EventArgs e)
        {
            try
            {
                Core.NrptOff();
                Write("Правила DNS сняты.");
            }
            catch (Exception ex) { Write("Не вышло: " + ex.Message); }
        }
    }
}
