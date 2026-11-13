// AiBypass — вся работа с системой: hosts, проверка адресов, правила DNS (NRPT).
// Собрано под .NET Framework 4.x и компилятор csc (C# 5), поэтому без интерполяции
// строк, без ?. и без nameof.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace AiBypass
{
    public static class Core
    {
        public const string MarkBegin = "# ai-bypass BEGIN";
        public const string MarkEnd = "# ai-bypass END";

        // маркеры прежних версий и чужих схем — тоже вычищаем при применении
        public static readonly string[] OldBeginMarks = new string[] { "# commonnet-bypass BEGIN" };
        public static readonly string[] OldEndMarks = new string[] { "# commonnet-bypass END" };

        // домены для правил DNS (точка в начале = все поддомены)
        public static readonly string[] NrptNamespaces = new string[]
        {
            "openai.com", ".chatgpt.com", "api.openai.com", "auth.openai.com",
            "auth0.openai.com", ".sora.com", "spotify.com", ".spotify.com",
            "grok.com", "x.ai", ".tiktok.com", ".instagram.com"
        };

        public const string NrptComment = "ai-bypass";

        // true, если при последней проверке сертификат сайта не совпал с именем:
        // значит, трафик может расшифровываться на стороне прокси.
        public static bool LastCertInvalid;

        static Core()
        {
            // Без явного TLS 1.2/1.3 .NET Framework берёт древние умолчания,
            // и https падает с "Не удалось создать защищенный канал SSL/TLS".
            // 3072 = TLS 1.2, 12288 = TLS 1.3.
            try
            {
                ServicePointManager.SecurityProtocol =
                    (SecurityProtocolType)3072 | (SecurityProtocolType)12288 | SecurityProtocolType.Tls;
            }
            catch { }
        }

        // ---------- пути ----------

        public static string AppDir
        {
            get
            {
                try { return Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath); }
                catch { return Directory.GetCurrentDirectory(); }
            }
        }

        public static string HostsPath
        {
            get
            {
                string win = Environment.GetEnvironmentVariable("SystemRoot");
                if (string.IsNullOrEmpty(win)) { win = @"C:\Windows"; }
                return Path.Combine(win, @"System32\drivers\etc\hosts");
            }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(AppDir, "selected.txt"); }
        }

        public static bool IsAdmin()
        {
            try
            {
                System.Security.Principal.WindowsIdentity id = System.Security.Principal.WindowsIdentity.GetCurrent();
                System.Security.Principal.WindowsPrincipal p = new System.Security.Principal.WindowsPrincipal(id);
                return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        // ---------- hosts ----------

        public static string[] ReadHosts()
        {
            return File.ReadAllLines(HostsPath);
        }

        public static void WriteHosts(List<string> lines)
        {
            if (lines.Count < 3) { throw new Exception("отказ: в hosts осталось меньше 3 строк - не записываю"); }
            File.WriteAllLines(HostsPath, lines.ToArray(), new ASCIIEncoding());
        }

        // убирает из набора строк все известные блоки вместе со строками-маркерами
        public static List<string> StripBlocks(string[] all)
        {
            List<string> begins = new List<string>();
            List<string> ends = new List<string>();
            begins.Add(MarkBegin);
            ends.Add(MarkEnd);
            begins.AddRange(OldBeginMarks);
            ends.AddRange(OldEndMarks);

            List<string> outp = new List<string>();
            // ВАЖНО: -1, а не 0. Ноль означал бы одновременно "не внутри блока" и
            // "внутри блока номер 0" (наш собственный маркер), и тогда свой же блок
            // не вырезался, а домены копились при каждом применении.
            int inside = -1;

            for (int i = 0; i < all.Length; i++)
            {
                string trimmed = all[i].Trim();

                if (inside < 0)
                {
                    int begIdx = IndexOfStart(begins, trimmed);
                    if (begIdx >= 0)
                    {
                        inside = begIdx;
                        continue;
                    }
                    // закрывающий маркер без открывающего — мусор, выбрасываем
                    if (IndexOfStart(ends, trimmed) >= 0) { continue; }
                    outp.Add(all[i]);
                }
                else
                {
                    int endIdx = IndexOfStart(ends, trimmed);
                    if (endIdx == inside) { inside = -1; }
                }
            }
            return outp;
        }

        private static int IndexOfStart(List<string> marks, string line)
        {
            for (int i = 0; i < marks.Count; i++)
            {
                if (line.StartsWith(marks[i], StringComparison.OrdinalIgnoreCase)) { return i; }
            }
            return -1;
        }

        public static void ApplyHosts(string ip)
        {
            ApplyHosts(ip, Services.AllDomains().ToArray());
        }

        public static void ApplyHosts(string ip, string[] domains)
        {
            if (!IsValidIp(ip)) { throw new Exception("это не похоже на IPv4-адрес: " + ip); }
            if (domains == null || domains.Length == 0)
            {
                throw new Exception("не выбран ни один сервис - нечего прописывать");
            }

            string[] all = ReadHosts();
            List<string> lines = StripBlocks(all);
            while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0) { lines.RemoveAt(lines.Count - 1); }

            lines.Add("");
            lines.Add(MarkBegin + " (proxy " + ip + ", доменов " + domains.Length + ")");
            lines.Add("# added " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " by AiBypass");
            for (int i = 0; i < domains.Length; i++)
            {
                lines.Add(ip + "\t" + domains[i]);
            }
            lines.Add(MarkEnd);

            BackupHosts();
            WriteHosts(lines);
            FlushDns();
        }

        public static void OffHosts()
        {
            string[] all = ReadHosts();
            List<string> lines = StripBlocks(all);
            while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0) { lines.RemoveAt(lines.Count - 1); }
            lines.Add("");
            BackupHosts();
            WriteHosts(lines);
            FlushDns();
        }

        private static void BackupHosts()
        {
            try
            {
                string dir = Path.Combine(AppDir, "backup");
                if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); }
                string dst = Path.Combine(dir, "hosts-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                File.Copy(HostsPath, dst, true);

                // держим не больше 10 снимков
                string[] files = Directory.GetFiles(dir, "hosts-*.txt");
                Array.Sort(files);
                for (int i = 0; i < files.Length - 10; i++)
                {
                    try { File.Delete(files[i]); }
                    catch { }
                }
            }
            catch { }
        }

        public static string CurrentProxyIp()
        {
            try
            {
                string[] all = ReadHosts();
                bool inside = false;
                for (int i = 0; i < all.Length; i++)
                {
                    string t = all[i].Trim();
                    if (t.StartsWith(MarkBegin, StringComparison.OrdinalIgnoreCase) ||
                        t.StartsWith(OldBeginMarks[0], StringComparison.OrdinalIgnoreCase)) { inside = true; continue; }
                    if (t.StartsWith(MarkEnd, StringComparison.OrdinalIgnoreCase) ||
                        t.StartsWith(OldEndMarks[0], StringComparison.OrdinalIgnoreCase)) { inside = false; continue; }
                    if (inside)
                    {
                        string[] parts = t.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && IsValidIp(parts[0])) { return parts[0]; }
                    }
                }
            }
            catch { }
            return "";
        }

        public static int CurrentDomainCount()
        {
            int n = 0;
            try
            {
                string[] all = ReadHosts();
                bool inside = false;
                for (int i = 0; i < all.Length; i++)
                {
                    string t = all[i].Trim();
                    if (t.StartsWith(MarkBegin, StringComparison.OrdinalIgnoreCase)) { inside = true; continue; }
                    if (t.StartsWith(MarkEnd, StringComparison.OrdinalIgnoreCase)) { inside = false; continue; }
                    if (inside && t.Length > 0 && t[0] != '#') { n++; }
                }
            }
            catch { }
            return n;
        }

        public static bool IsValidIp(string ip)
        {
            if (string.IsNullOrEmpty(ip)) { return false; }
            IPAddress a;
            if (!IPAddress.TryParse(ip.Trim(), out a)) { return false; }
            return a.AddressFamily == AddressFamily.InterNetwork;
        }

        public static bool IsPublicIp(string ip)
        {
            IPAddress a;
            if (!IPAddress.TryParse(ip, out a)) { return false; }
            if (a.AddressFamily != AddressFamily.InterNetwork) { return false; }
            byte[] b = a.GetAddressBytes();
            if (b[0] == 0 || b[0] == 10 || b[0] == 127) { return false; }
            if (b[0] == 192 && b[1] == 168) { return false; }
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) { return false; }
            if (b[0] == 169 && b[1] == 254) { return false; }
            if (b[0] >= 224) { return false; }
            return true;
        }

        public static void FlushDns()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("ipconfig", "/flushdns");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                Process p = Process.Start(psi);
                p.StandardOutput.ReadToEnd();
                p.WaitForExit(15000);
            }
            catch { }
        }

        // ---------- проверка адреса ----------

        // Открывает TCP+TLS прямо на указанный адрес, но с именем сайта в SNI.
        // Так проверяется именно "пустит ли этот прокси к сайту", ничего не записывая.
        // Рукопожатие продолжается даже при плохом сертификате (иначе не увидеть код ответа),
        // но факт подмены запоминается в LastCertInvalid.
        public static string CheckSite(string ip, string host, string path, int timeoutMs)
        {
            string result = "нет ответа";
            TcpClient tcp = null;
            bool certOk = true;
            LastCertInvalid = false;
            try
            {
                if (!IsValidIp(ip)) { return "плохой IP"; }
                tcp = new TcpClient();
                IAsyncResult ar = tcp.BeginConnect(ip, 443, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) { return "таймаут соединения"; }
                tcp.EndConnect(ar);

                RemoteCertificateValidationCallback cb = new RemoteCertificateValidationCallback(
                    delegate(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors errors)
                    {
                        if (errors != SslPolicyErrors.None) { certOk = false; }
                        return true;
                    });

                using (SslStream ssl = new SslStream(tcp.GetStream(), false, cb))
                {
                    ssl.ReadTimeout = timeoutMs;
                    ssl.WriteTimeout = timeoutMs;
                    ssl.AuthenticateAsClient(host, null, SslProtocols.Tls12 | SslProtocols.Tls13, false);

                    string req = "GET " + path + " HTTP/1.1\r\n" +
                                 "Host: " + host + "\r\n" +
                                 "User-Agent: Mozilla/5.0 AiBypass\r\n" +
                                 "Accept: */*\r\n" +
                                 "Connection: close\r\n\r\n";
                    byte[] data = Encoding.ASCII.GetBytes(req);
                    ssl.Write(data);
                    ssl.Flush();

                    StreamReader sr = new StreamReader(ssl, Encoding.ASCII);
                    string status = sr.ReadLine();
                    if (status == null) { result = "пустой ответ"; }
                    else
                    {
                        string[] sp = status.Split(' ');
                        result = sp.Length >= 2 ? sp[1] : status;
                    }
                }
            }
            catch (Exception ex)
            {
                result = "ошибка: " + Short(ex.Message);
            }
            finally
            {
                if (tcp != null)
                {
                    try { tcp.Close(); }
                    catch { }
                }
                LastCertInvalid = !certOk;
            }
            return result;
        }

        // Код ответа с пометкой о подмене сертификата — для показа человеку
        public static string CheckSiteWithCert(string ip, string host, string path, int timeoutMs)
        {
            string code = CheckSite(ip, host, path, timeoutMs);
            if (LastCertInvalid) { return code + "  [сертификат не совпал: возможна расшифровка трафика]"; }
            return code;
        }

        public static bool LooksLikePass(string code)
        {
            return code == "200" || code == "301" || code == "302" || code == "307"
                || code == "401" || code == "404";
        }

        public static string Verdict(string code)
        {
            if (LooksLikePass(code)) { return "пускает"; }
            return "НЕ пускает";
        }

        public static string Short(string s)
        {
            if (s == null) { return ""; }
            if (s.Length > 60) { return s.Substring(0, 60); }
            return s;
        }

        // ---------- поиск адреса по ссылке ----------

        public static string HttpGet(string url, int timeoutMs)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = "Mozilla/5.0 AiBypass";
            req.Proxy = null;
            req.AllowAutoRedirect = true;
            // ВАЖНО: сертификат проверяется по-настоящему. Этим методом скачивается список,
            // из которого берётся IP прокси для hosts: без проверки перехватчик мог бы
            // подсунуть свой ответ и увести весь трафик ChatGPT на свой сервер.
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                return sr.ReadToEnd();
            }
        }

        private static string GithubRaw(string url, string fileName)
        {
            // https://github.com/ПОЛЬЗОВАТЕЛЬ/РЕПО -> https://raw.githubusercontent.com/ПОЛЬЗОВАТЕЛЬ/РЕПО/main/ФАЙЛ
            Match m = Regex.Match(url, @"^https?://(?:www\.)?github\.com/([^/]+)/([^/#?]+)", RegexOptions.IgnoreCase);
            if (!m.Success) { return ""; }
            string user = m.Groups[1].Value;
            string repo = m.Groups[2].Value;
            if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) { repo = repo.Substring(0, repo.Length - 4); }
            return "https://raw.githubusercontent.com/" + user + "/" + repo + "/main/" + fileName;
        }

        private static void AddUnique(List<string> list, string ip)
        {
            if (!IsPublicIp(ip)) { return; }
            if (!list.Contains(ip)) { list.Add(ip); }
        }

        private static void ExtractFromText(string text, List<string> found, List<string> log)
        {
            // 1) формат hosts: "адрес домен"
            MatchCollection mc = Regex.Matches(text,
                @"(?<![\d.])(\d{1,3}(?:\.\d{1,3}){3})[ \t]+(?:www\.)?chatgpt\.com\b");
            for (int i = 0; i < mc.Count; i++) { AddUnique(found, mc[i].Groups[1].Value); }
            if (found.Count > 0) { log.Add("  нашёл строку hosts: " + string.Join(", ", found.ToArray())); return; }

            mc = Regex.Matches(text, @"(?<![\d.])(\d{1,3}(?:\.\d{1,3}){3})[ \t]+(?:api\.)?openai\.com\b");
            for (int i = 0; i < mc.Count; i++) { AddUnique(found, mc[i].Groups[1].Value); }
            if (found.Count > 0) { log.Add("  нашёл строку hosts (по openai.com): " + string.Join(", ", found.ToArray())); return; }

            // 2) ответ DoH в виде JSON
            if (text.IndexOf("\"Answer\"", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                mc = Regex.Matches(text, @"""data""\s*:\s*""(\d{1,3}(?:\.\d{1,3}){3})""");
                for (int i = 0; i < mc.Count; i++) { AddUnique(found, mc[i].Groups[1].Value); }
                if (found.Count > 0) { log.Add("  нашёл в ответе DoH: " + string.Join(", ", found.ToArray())); return; }
            }

            // 3) обычная страница: адреса рядом со словами chatgpt / openai / hosts
            mc = Regex.Matches(text, @"(?<![\d.])(\d{1,3}(?:\.\d{1,3}){3})(?![\d.])");
            for (int i = 0; i < mc.Count && found.Count < 6; i++)
            {
                string ip = mc[i].Groups[1].Value;
                if (!IsPublicIp(ip)) { continue; }
                int from = Math.Max(0, mc[i].Index - 300);
                int len = Math.Min(600, text.Length - from);
                string ctx = text.Substring(from, len).ToLowerInvariant();
                if (ctx.IndexOf("chatgpt") >= 0 || ctx.IndexOf("openai") >= 0 || ctx.IndexOf("hosts") >= 0)
                {
                    AddUnique(found, ip);
                }
            }
            if (found.Count > 0)
            {
                log.Add("  нашёл на странице рядом с chatgpt/openai/hosts: " + string.Join(", ", found.ToArray()));
            }
        }

        public static List<string> FindProxyCandidates(string input, List<string> log)
        {
            List<string> found = new List<string>();
            string raw = (input == null ? "" : input.Trim());
            if (raw.Length == 0) { log.Add("пустая ссылка"); return found; }

            string url = raw;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            string urlHost = "";
            try { urlHost = new Uri(url).Host; }
            catch { }

            string[] urls = new string[]
            {
                url,
                GithubRaw(url, "bypass"),
                GithubRaw(url, "base_hosts.txt"),
                GithubRaw(url, "hosts"),
                GithubRaw(url, "hosts.txt"),
                GithubRaw(url, "README.md")
            };

            for (int i = 0; i < urls.Length && found.Count == 0; i++)
            {
                string u = urls[i];
                if (u == null || u.Length == 0) { continue; }
                try
                {
                    log.Add("качаю: " + u);
                    string t = HttpGet(u, 20000);
                    if (t == null || t.Length < 20) { log.Add("  пусто"); continue; }
                    log.Add("  получил " + t.Length + " символов");
                    ExtractFromText(t, found, log);
                }
                catch (Exception ex)
                {
                    log.Add("  не вышло: " + Short(ex.Message));
                }
            }

            bool isGithub = urlHost.IndexOf("github", StringComparison.OrdinalIgnoreCase) >= 0;
            if (found.Count == 0 && urlHost.Length > 0 && !isGithub)
            {
                try
                {
                    string res = RunPowerShell(
                        "$a=@(); try { $a=@(Resolve-DnsName 'chatgpt.com' -Type A -Server '" + urlHost +
                        "' -DnsOnly -QuickTimeout -EA Stop | Where-Object {$_.IPAddress} | ForEach-Object {$_.IPAddress}) } catch {}; $a -join ','");
                    if (res.IndexOf('.') > 0)
                    {
                        string[] parts = res.Split(new char[] { ',', ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 0; i < parts.Length; i++) { AddUnique(found, parts[i]); }
                        if (found.Count > 0) { log.Add("  спросил обычный DNS " + urlHost + ": " + string.Join(", ", found.ToArray())); }
                    }
                    else
                    {
                        log.Add("  обычный DNS " + urlHost + " не ответил");
                    }
                }
                catch { }

                if (found.Count == 0)
                {
                    try
                    {
                        string j = HttpGet("https://" + urlHost + "/dns-query?name=chatgpt.com&type=A", 15000);
                        MatchCollection mc = Regex.Matches(j, @"""data""\s*:\s*""(\d{1,3}(?:\.\d{1,3}){3})""");
                        for (int i = 0; i < mc.Count; i++) { AddUnique(found, mc[i].Groups[1].Value); }
                        if (found.Count > 0) { log.Add("  спросил DoH " + urlHost + ": " + string.Join(", ", found.ToArray())); }
                    }
                    catch (Exception ex)
                    {
                        log.Add("  DoH " + urlHost + " не ответил: " + Short(ex.Message));
                    }
                }
            }

            if (found.Count == 0) { log.Add("адрес не найден."); }
            return found;
        }

        // находит адрес и сразу проверяет его; возвращает пустую строку, если ничего не пускает
        public static string FindAndVerify(string input, List<string> log)
        {
            List<string> cands = FindProxyCandidates(input, log);
            if (cands.Count == 0) { return ""; }

            log.Add("проверяю найденное (chatgpt.com через каждый адрес):");
            string risky = "";

            for (int i = 0; i < cands.Count; i++)
            {
                string code = CheckSite(cands[i], "chatgpt.com", "/robots.txt", 9000);
                bool badCert = LastCertInvalid;
                log.Add("  " + cands[i] + " -> " + code + (badCert ? "  [сертификат не совпал]" : ""));

                if (badCert)
                {
                    // адрес, расшифровывающий трафик, берём только если больше нечего взять
                    if (risky.Length == 0 && LooksLikePass(code)) { risky = cands[i]; }
                    continue;
                }
                if (LooksLikePass(code) && code != "404")
                {
                    log.Add("  этот пускает: " + cands[i]);
                    return cands[i];
                }
            }

            if (risky.Length > 0)
            {
                log.Add("ВНИМАНИЕ: рабочий адрес подменяет сертификат — трафик может расшифровываться");
                log.Add("на стороне прокси. Подставляю " + risky + " только потому, что рабочих без подмены нет.");
                log.Add("Не вводи там ничего важного.");
                return risky;
            }

            // не подставляем заведомо мёртвый адрес: иначе можно случайно
            // применить его и сломать работающий обход
            log.Add("адреса нашлись, но ни один не пускает - в поле НЕ подставляю.");
            return "";
        }

        // ---------- перезапуск браузера ----------

        // Браузер держит свой DNS-кэш и пул открытых соединений в памяти: после смены адресов
        // он продолжает ходить по старым. Закрыть процесс — единственный надёжный способ
        // сбросить это без перезагрузки машины (на телефоне то же делает режим полёта).
        public static readonly string[] BrowserProcesses = new string[]
        {
            "chrome", "firefox", "opera", "brave", "vivaldi", "browser", "yandex"
        };

        public static List<string> RunningProcesses(string[] names)
        {
            List<string> found = new List<string>();
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    Process[] ps = Process.GetProcessesByName(names[i]);
                    if (ps.Length > 0) { found.Add(names[i]); }
                }
                catch { }
            }
            return found;
        }

        // Возвращает имена процессов, которые были закрыты и запущены заново.
        public static List<string> RestartProcesses(string[] names, List<string> log, bool openGemini)
        {
            return RestartProcesses(names, log, openGemini, null, "");
        }

        // extraArgs - что добавить к запуску (например, --proxy-server=...):
        // Яндекс и Chrome не берут системную настройку прокси, если она меняется при работающем
        // браузере, а с явным ключом запуска - берут.
        public static List<string> RestartProcesses(string[] names, List<string> log, bool openGemini,
            string openUrl, string extraArgs)
        {
            List<string> done = new List<string>();

            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                Process[] ps;
                try { ps = Process.GetProcessesByName(name); }
                catch { continue; }
                if (ps.Length == 0) { continue; }

                string exe = "";
                try
                {
                    for (int k = 0; k < ps.Length; k++)
                    {
                        try
                        {
                            if (ps[k].MainModule != null) { exe = ps[k].MainModule.FileName; break; }
                        }
                        catch { }
                    }
                }
                catch { }

                log.Add("закрываю " + name + (ps.Length > 1 ? " (" + ps.Length + " процессов)" : ""));

                // сначала вежливо: просим окна закрыться, чтобы браузер сохранил сессию
                for (int k = 0; k < ps.Length; k++)
                {
                    try { if (ps[k].MainWindowHandle != IntPtr.Zero) { ps[k].CloseMainWindow(); } }
                    catch { }
                }

                DateTime deadline = DateTime.Now.AddSeconds(5);
                while (DateTime.Now < deadline)
                {
                    bool alive = false;
                    for (int k = 0; k < ps.Length; k++)
                    {
                        try { if (!ps[k].HasExited) { alive = true; break; } }
                        catch { }
                    }
                    if (!alive) { break; }
                    System.Threading.Thread.Sleep(300);
                }

                // что осталось — закрываем силой
                int killed = 0;
                for (int k = 0; k < ps.Length; k++)
                {
                    try { if (!ps[k].HasExited) { ps[k].Kill(); killed++; } }
                    catch { }
                }
                if (killed > 0) { log.Add("  принудительно закрыто процессов: " + killed); }

                // ждём, пока не останется ни одного процесса: иначе новый запуск
                // прицепится к ещё живущему старому и ключи запуска потеряются
                DateTime gone = DateTime.Now.AddSeconds(15);
                while (DateTime.Now < gone)
                {
                    Process[] left;
                    try { left = Process.GetProcessesByName(name); }
                    catch { left = new Process[0]; }
                    if (left.Length == 0) { break; }
                    System.Threading.Thread.Sleep(400);
                }
                System.Threading.Thread.Sleep(600);

                if (exe.Length > 0)
                {
                    try
                    {
                        string a = extraArgs == null ? "" : extraArgs;
                        if (openUrl != null && openUrl.Length > 0) { a = (a + " \"" + openUrl + "\"").Trim(); }
                        ProcessStartInfo psi2 = new ProcessStartInfo(exe, a);
                        psi2.UseShellExecute = false;
                        psi2.WorkingDirectory = Path.GetDirectoryName(exe);
                        Process.Start(psi2);
                        done.Add(name);
                        log.Add("  запустил заново: " + exe + (a.Length > 0 ? " " + a : ""));
                    }
                    catch (Exception ex) { log.Add("  не смог запустить заново: " + Short(ex.Message)); }
                }
                else
                {
                    log.Add("  путь к exe не определился - запусти " + name + " сам");
                }
            }

            if (openGemini)
            {
                try
                {
                    Process.Start("https://gemini.google.com/");
                    log.Add("открыл gemini.google.com");
                }
                catch (Exception ex) { log.Add("не смог открыть сайт: " + Short(ex.Message)); }
            }

            return done;
        }

        // Открывает сайт в приватном окне браузера: без куки и без расширений.
        // Нужно, чтобы отличить «сломан доступ» от «испорчена сессия/куки».
        public static string OpenPrivate(string url, List<string> log)
        {
            string exe = "";
            string name = "";
            for (int i = 0; i < BrowserProcesses.Length; i++)
            {
                Process[] ps;
                try { ps = Process.GetProcessesByName(BrowserProcesses[i]); }
                catch { continue; }
                if (ps.Length == 0) { continue; }
                for (int k = 0; k < ps.Length; k++)
                {
                    try
                    {
                        if (ps[k].MainModule != null) { exe = ps[k].MainModule.FileName; break; }
                    }
                    catch { }
                }
                if (exe.Length > 0) { name = BrowserProcesses[i]; break; }
            }

            if (exe.Length == 0)
            {
                try
                {
                    Process.Start(url);
                    log.Add("браузер не опознан - открыл сайт в браузере по умолчанию");
                    return "";
                }
                catch (Exception ex) { log.Add("не смог открыть сайт: " + Short(ex.Message)); return ""; }
            }

            string args;
            if (name == "firefox") { args = "-private-window \"" + url + "\""; }
            else { args = "--incognito \"" + url + "\""; }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false;
                Process.Start(psi);
                log.Add("открыл приватное окно: " + exe);
                return exe;
            }
            catch (Exception ex)
            {
                log.Add("приватное окно не открылось: " + Short(ex.Message));
                return "";
            }
        }

        // ---------- системный прокси ----------

        [System.Runtime.InteropServices.DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        private static void RefreshWinInet()
        {
            try
            {
                InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
            }
            catch { }
        }

        // server пустой - выключить прокси
        public static void SetSystemProxy(string server)
        {
            string key = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
            using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key, true))
            {
                if (k == null) { throw new Exception("нет доступа к настройкам прокси"); }
                if (string.IsNullOrEmpty(server))
                {
                    k.SetValue("ProxyEnable", 0);
                    k.DeleteValue("AutoConfigURL", false);
                }
                else
                {
                    k.SetValue("ProxyEnable", 1);
                    k.SetValue("ProxyServer", server);
                    k.SetValue("ProxyOverride", "<local>;127.*;192.168.*");
                    k.DeleteValue("AutoConfigURL", false);
                }
            }
            RefreshWinInet();
        }

        // Прокси только для нужных доменов: системе отдаём PAC-файл, который
        // раздаёт сам релей. Тогда браузер шлёт нам только Gemini и Grok,
        // а YouTube, github и прочее идут напрямую и вообще нас не касаются.
        public static void SetSystemProxyPac(bool on)
        {
            string key = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
            using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key, true))
            {
                if (k == null) { throw new Exception("нет доступа к настройкам прокси"); }
                if (on)
                {
                    k.SetValue("ProxyEnable", 0);
                    k.SetValue("AutoConfigURL", "http://127.0.0.1:" + Relay.LocalPort + "/proxy.pac");
                }
                else
                {
                    k.SetValue("ProxyEnable", 0);
                    k.DeleteValue("AutoConfigURL", false);
                }
            }
            RefreshWinInet();
        }

        public static string SystemPac()
        {
            string key = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
            using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key, false))
            {
                if (k == null) { return ""; }
                object v = k.GetValue("AutoConfigURL");
                return v == null ? "" : v.ToString();
            }
        }

        public static string SystemProxy()
        {
            string key = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
            using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key, false))
            {
                if (k == null) { return ""; }
                object en = k.GetValue("ProxyEnable");
                object srv = k.GetValue("ProxyServer");
                if (en == null || Convert.ToInt32(en) == 0 || srv == null) { return ""; }
                return srv.ToString();
            }
        }

        public static string ProxyUrlPath
        {
            get { return Path.Combine(AppDir, "proxy.txt"); }
        }

        public static string LoadProxyUrl()
        {
            try { if (File.Exists(ProxyUrlPath)) { return File.ReadAllText(ProxyUrlPath).Trim(); } }
            catch { }
            // если файла нет - берём адрес, вшитый при сборке
            try
            {
                if (Defaults.ProxyUrl.Length > 0)
                {
                    SaveProxyUrl(Defaults.ProxyUrl);
                    return Defaults.ProxyUrl;
                }
            }
            catch { }
            return "";
        }

        public static void SaveProxyUrl(string url)
        {
            try { File.WriteAllText(ProxyUrlPath, url, Encoding.UTF8); }
            catch { }
        }

        // прячет пароль в адресе прокси, чтобы он не светился в окне и в скриншотах
        public static string MaskProxy(string url)
        {
            if (string.IsNullOrEmpty(url)) { return ""; }
            int sc = url.IndexOf("://");
            int at = url.LastIndexOf('@');
            if (sc < 0 || at < 0 || at < sc) { return url; }
            string head = url.Substring(0, sc + 3);
            string auth = url.Substring(sc + 3, at - sc - 3);
            int colon = auth.IndexOf(':');
            if (colon > 0) { auth = auth.Substring(0, colon) + ":***"; }
            else if (auth.Length > 0) { auth = "***"; }
            return head + auth + url.Substring(at);
        }

        // отметка: программа запускала браузеры с нашим ключом прокси. Нужна, чтобы при
        // выходе и при следующем запуске вернуть браузеры в нормальное состояние,
        // а не дёргать их без нужды.
        public static string BrowserMarkPath
        {
            get { return Path.Combine(AppDir, "browsers-proxy.txt"); }
        }

        public static void MarkBrowsersWithProxy(bool on)
        {
            try
            {
                if (on) { File.WriteAllText(BrowserMarkPath, "1", Encoding.ASCII); }
                else if (File.Exists(BrowserMarkPath)) { File.Delete(BrowserMarkPath); }
            }
            catch { }
        }

        public static bool BrowsersMarked()
        {
            try { return File.Exists(BrowserMarkPath); }
            catch { return false; }
        }

        public static string ProxySourcePath
        {
            get { return Path.Combine(AppDir, "proxy-source.txt"); }
        }

        public static string LoadProxySource()
        {
            try { if (File.Exists(ProxySourcePath)) { return File.ReadAllText(ProxySourcePath).Trim(); } }
            catch { }
            try
            {
                if (Defaults.ProxySource.Length > 0)
                {
                    SaveProxySource(Defaults.ProxySource);
                    return Defaults.ProxySource;
                }
            }
            catch { }
            return "";
        }

        public static void SaveProxySource(string url)
        {
            try { File.WriteAllText(ProxySourcePath, url, Encoding.UTF8); }
            catch { }
        }

        // Забирает адрес прокси из файла на GitHub: первая непустая строка, которая
        // разбирается как адрес прокси. Нужна, чтобы менять прокси не пересобирая прогу.
        public static string FetchProxyFromSource(string url, List<string> log)
        {
            if (string.IsNullOrEmpty(url)) { return ""; }
            try
            {
                if (log != null) { log.Add("тяну адрес прокси: " + url); }
                string text = HttpGet(url, 20000);
                if (text == null) { return ""; }
                string[] lines = text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith("#")) { continue; }
                    if (line.IndexOf("://") < 0) { line = "socks5://" + line; }
                    try
                    {
                        ProxySettings ps = ProxySettings.Parse(line);
                        if (log != null) { log.Add("  нашёл: " + ps.Display); }
                        return line;
                    }
                    catch { }
                }
                if (log != null) { log.Add("  в файле нет строки с адресом прокси"); }
            }
            catch (Exception ex)
            {
                if (log != null) { log.Add("  не смог скачать: " + Short(ex.Message)); }
            }
            return "";
        }

        // ---------- правила DNS (NRPT) ----------

        public static string RunPowerShell(string script)
        {
            // без смены кодировки русский текст из PowerShell приходит в консоль кашей
            string prelude = "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; ";
            ProcessStartInfo psi = new ProcessStartInfo("powershell",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + prelude + script.Replace("\"", "\\\"") + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            Process p = Process.Start(psi);
            string outp = p.StandardOutput.ReadToEnd();
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit(60000);
            if (outp == null) { outp = ""; }
            if (err == null) { err = ""; }
            return (outp + err).Trim();
        }

        public static void NrptSet(string servers)
        {
            string[] list = servers.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (list.Length == 0) { throw new Exception("не указан ни один DNS-сервер"); }
            for (int i = 0; i < list.Length; i++)
            {
                if (!IsValidIp(list[i])) { throw new Exception("плохой адрес DNS-сервера: " + list[i]); }
            }

            string srvList = "'" + string.Join("','", list) + "'";
            string nsList = "'" + string.Join("','", NrptNamespaces) + "'";

            // Чистим один раз ДО цикла: если чистить внутри, второй проход удаляет
            // правило, добавленное первым, и остаётся одно вместо всех.
            string script =
                "$srv=@(" + srvList + "); $ns=@(" + nsList + "); " +
                "Get-DnsClientNrptRule -EA SilentlyContinue | Where-Object { ($ns -contains ($_.Namespace -join ',')) -or $_.Comment -eq '" +
                NrptComment + "' } | Remove-DnsClientNrptRule -Force -EA SilentlyContinue; " +
                "foreach($n in $ns){ Add-DnsClientNrptRule -Namespace $n -NameServers $srv -Comment '" +
                NrptComment + "' -EA Stop | Out-Null }; " +
                "Clear-DnsClientCache; " +
                "'поставлено правил: ' + @(Get-DnsClientNrptRule | Where-Object {$_.Comment -eq '" + NrptComment + "'}).Count";

            string res = RunPowerShell(script);
            if (res.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0 ||
                res.IndexOf("ошиб", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new Exception(res);
            }
        }

        public static void NrptOff()
        {
            string script =
                "$r=@(Get-DnsClientNrptRule -EA SilentlyContinue | Where-Object {$_.Comment -eq '" + NrptComment + "'}); " +
                "$r | Remove-DnsClientNrptRule -Force -EA SilentlyContinue; " +
                "Clear-DnsClientCache; 'снято правил: ' + $r.Count";
            RunPowerShell(script);
        }

        public static string NrptState()
        {
            string script =
                "$r=@(Get-DnsClientNrptRule -EA SilentlyContinue); " +
                "if($r.Count -eq 0){ 'нет' } else { foreach($x in $r){ ($x.Namespace -join ',') + ' -> ' + ($x.NameServers -join ',') } }";
            return RunPowerShell(script);
        }
    }
}
