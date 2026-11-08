// AiBypass - быстрая смена адреса обхода для ChatGPT / Gemini / Spotify.
// Собран под .NET Framework 4.x, компилятор csc.exe (C# 5): без интерполяции строк,
// без ?., без nameof, без auto-property initializer.
//
// Два способа обхода, оба внутри:
//   1) HOSTS - домены указывают на прокси-IP (так работает commonnet.work / xbox-dns / GeoFree).
//   2) NRPT  - домены резолвятся через указанный DNS-сервер (так работал GeoHide).
//
// Режимы запуска:
//   без аргументов        - окно
//   --state               - показать состояние
//   --check <ip>          - проверить, пускает ли этот IP к сайтам (без применения)
//   --apply <ip>          - прописать домены на этот IP в hosts
//   --off                 - снять блок из hosts
//   --github              - взять текущий IP из репозитория сервиса
//   --nrpt-set <ip1,ip2>  - поставить DNS-правила с этими серверами
//   --nrpt-off            - снять DNS-правила

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

// Без этого атрибута рантайм считает сборку старой и включает TLS 1.0.
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace AiBypass
{
    internal static class Core
    {
        static Core()
        {
            // Без явного TLS 1.2/1.3 .NET Framework берёт древние умолчания (SSL3/TLS1.0),
            // и https падает с "Не удалось создать защищенный канал SSL/TLS".
            // 3072 = TLS 1.2, 12288 = TLS 1.3.
            try
            {
                ServicePointManager.SecurityProtocol =
                    (SecurityProtocolType)3072 | (SecurityProtocolType)12288 | SecurityProtocolType.Tls;
            }
            catch { }
        }

        public const string MarkBegin = "# ai-bypass BEGIN";
        public const string MarkEnd   = "# ai-bypass END";

        // маркеры прежних схем, которые тоже надо вычищать при применении
        private static readonly string[] OldBeginMarks = new string[] { "# commonnet-bypass BEGIN" };
        private static readonly string[] OldEndMarks   = new string[] { "# commonnet-bypass END" };

        public static readonly string[] Domains = new string[]
        {
            // OpenAI / ChatGPT
            "chatgpt.com", "ab.chatgpt.com", "operator.chatgpt.com", "webrtc.chatgpt.com", "sora.chatgpt.com",
            "api.openai.com", "auth.openai.com", "auth0.openai.com", "openai.com", "platform.openai.com",
            "developers.openai.com", "cdn.platform.openai.com", "videos.openai.com", "android.chat.openai.com",
            "ios.chat.openai.com", "tcr9i.chat.openai.com", "cdn.oaistatic.com", "files.oaiusercontent.com",
            // Gemini / Google AI
            "gemini.google.com", "aistudio.google.com", "notebooklm.google.com", "generativelanguage.googleapis.com",
            // Spotify
            "spotify.com", "open.spotify.com", "accounts.spotify.com", "www.spotify.com"
        };

        // домены для DNS-правил (с точкой = все поддомены), как было у GeoHide
        public static readonly string[] NrptNamespaces = new string[]
        {
            "openai.com", ".chatgpt.com", "api.openai.com", "auth.openai.com",
            "auth0.openai.com", ".sora.com", "spotify.com", ".spotify.com"
        };

        public const string NrptComment = "ai-bypass";

        public static string ExeDir
        {
            get { return Path.GetDirectoryName(Application.ExecutablePath); }
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

        // убирает из набора строк любые известные блоки (вместе со строками-маркерами)
        public static List<string> StripBlocks(string[] all)
        {
            List<string> begins = new List<string>();
            List<string> ends = new List<string>();
            begins.Add(MarkBegin);
            ends.Add(MarkEnd);
            begins.AddRange(OldBeginMarks);
            ends.AddRange(OldEndMarks);

            List<string> outp = new List<string>();
            int skipUntilEnd = 0; // 0 = не внутри блока
            for (int i = 0; i < all.Length; i++)
            {
                string line = all[i];
                string trimmed = line.Trim();

                if (skipUntilEnd == 0)
                {
                    int begIdx = IndexOfStart(begins, trimmed);
                    if (begIdx >= 0)
                    {
                        // ищем закрывающий маркер, соответствующий этому блоку
                        skipUntilEnd = begIdx;
                        continue;
                    }
                    outp.Add(line);
                }
                else
                {
                    int endIdx = IndexOfStart(ends, trimmed);
                    if (endIdx == skipUntilEnd) { skipUntilEnd = 0; }
                    continue;
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
            if (!IsValidIp(ip)) { throw new Exception("это не похоже на IPv4-адрес: " + ip); }

            string[] all = ReadHosts();
            List<string> lines = StripBlocks(all);
            while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0) { lines.RemoveAt(lines.Count - 1); }

            lines.Add("");
            lines.Add(MarkBegin + " (proxy " + ip + ")");
            lines.Add("# added " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " by AiBypass");
            for (int i = 0; i < Domains.Length; i++)
            {
                lines.Add(ip + "\t" + Domains[i]);
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
                string dir = Path.Combine(ExeDir, "backup");
                if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); }
                string dst = Path.Combine(dir, "hosts-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                File.Copy(HostsPath, dst, true);

                // держим не больше 10 снимков
                string[] files = Directory.GetFiles(dir, "hosts-*.txt");
                Array.Sort(files);
                for (int i = 0; i < files.Length - 10; i++) { try { File.Delete(files[i]); } catch { } }
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

        public static bool IsValidIp(string ip)
        {
            if (string.IsNullOrEmpty(ip)) { return false; }
            IPAddress a;
            if (!IPAddress.TryParse(ip.Trim(), out a)) { return false; }
            return a.AddressFamily == AddressFamily.InterNetwork;
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

        // ---------- проверка ----------

        // Сертификат последней проверки не совпал с именем сайта.
        // Значит, между нами и сайтом стоит кто-то, кто расшифровывает трафик —
        // такой адрес по умолчанию не применяем.
        public static bool LastCertInvalid = false;

        // Открывает TCP+TLS прямо на указанный IP, но с именем сайта в SNI -
        // то есть проверяет именно "пустит ли этот прокси к сайту", ничего не записывая в hosts.
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
                if (!ar.AsyncWaitHandle.WaitOne(timeoutMs))
                {
                    return "таймаут соединения";
                }
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
                        if (sp.Length >= 2) { result = sp[1]; } else { result = status; }
                    }
                }
            }
            catch (Exception ex)
            {
                result = "ошибка: " + Short(ex.Message);
            }
            finally
            {
                if (tcp != null) { try { tcp.Close(); } catch { } }
                LastCertInvalid = !certOk;
            }
            return result;
        }

        // Код ответа с пометкой о сертификате — для показа человеку
        public static string CheckSiteWithCert(string ip, string host, string path, int timeoutMs)
        {
            string code = CheckSite(ip, host, path, timeoutMs);
            if (LastCertInvalid) { return code + "  [сертификат не совпал: возможна расшифровка трафика]"; }
            return code;
        }

        private static string Short(string s)
        {
            if (s == null) { return ""; }
            if (s.Length > 60) { return s.Substring(0, 60); }
            return s;
        }

        // ---------- поиск адреса прокси по ссылке / домену ----------

        public static string HttpGet(string url, int timeoutMs)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = "Mozilla/5.0 AiBypass";
            req.Proxy = null;
            req.AllowAutoRedirect = true;
            // ВАЖНО: сертификат проверяется по-настоящему.
            // Раньше здесь стоял callback, принимавший любой сертификат — это позволяло
            // подсунуть подменённый ответ GitHub, а из него берётся IP прокси, который
            // потом прописывается в hosts. То есть перехватчик мог увести весь трафик
            // ChatGPT на свой сервер. Проверка снята сознательно не будет.
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                return sr.ReadToEnd();
            }
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

        private static void AddUnique(List<string> list, string ip)
        {
            if (!IsPublicIp(ip)) { return; }
            for (int i = 0; i < list.Count; i++) { if (list[i] == ip) { return; } }
            list.Add(ip);
        }

        private static string GithubRaw(string url, string fileName)
        {
            // https://github.com/ПОЛЬЗОВАТЕЛЬ/РЕПО  ->  https://raw.githubusercontent.com/ПОЛЬЗОВАТЕЛЬ/РЕПО/main/ФАЙЛ
            Match m = Regex.Match(url, @"^https?://(?:www\.)?github\.com/([^/]+)/([^/#?]+)", RegexOptions.IgnoreCase);
            if (!m.Success) { return ""; }
            string user = m.Groups[1].Value;
            string repo = m.Groups[2].Value;
            if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) { repo = repo.Substring(0, repo.Length - 4); }
            return "https://raw.githubusercontent.com/" + user + "/" + repo + "/main/" + fileName;
        }

        // Ищет адрес прокси четырьмя способами подряд: файл hosts, страница сервиса,
        // ответ их обычного DNS, ответ их DoH. Возвращает список кандидатов.
        // вытаскивает адреса-кандидаты из скачанного текста: файл hosts, JSON DoH, обычная страница
        private static void ExtractFromText(string text, List<string> found, List<string> logg)
        {
            // 1) формат hosts: "IP домен"
            MatchCollection mc = Regex.Matches(text, @"(?<![\d.])(\d{1,3}(?:\.\d{1,3}){3})[ \t]+(?:www\.)?chatgpt\.com\b");
            for (int i = 0; i < mc.Count; i++) { AddUnique(found, mc[i].Groups[1].Value); }
            if (found.Count > 0) { logg.Add("  нашёл строку hosts: " + string.Join(", ", found.ToArray())); return; }

            mc = Regex.Matches(text, @"(?<![\d.])(\d{1,3}(?:\.\d{1,3}){3})[ \t]+(?:api\.)?openai\.com\b");
            for (int i = 0; i < mc.Count; i++) { AddUnique(found, mc[i].Groups[1].Value); }
            if (found.Count > 0) { logg.Add("  нашёл строку hosts (по openai.com): " + string.Join(", ", found.ToArray())); return; }

            // 2) ответ DoH в JSON
            if (text.IndexOf("\"Answer\"", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                mc = Regex.Matches(text, @"""data""\s*:\s*""(\d{1,3}(?:\.\d{1,3}){3})""");
                for (int i = 0; i < mc.Count; i++) { AddUnique(found, mc[i].Groups[1].Value); }
                if (found.Count > 0) { logg.Add("  нашёл в ответе DoH: " + string.Join(", ", found.ToArray())); return; }
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
            if (found.Count > 0) { logg.Add("  нашёл на странице рядом с chatgpt/openai/hosts: " + string.Join(", ", found.ToArray())); }
        }

        // Ищет адрес прокси по ссылке или домену: файл hosts сервиса, страница, ответ их DNS, ответ их DoH.
        // Скачивание идёт по очереди по всем вариантам ссылки: страница, затем raw-файлы на GitHub.
        // Из каждого скачанного текста пробуем вытащить адрес и останавливаемся, как только нашли.
        public static List<string> FindProxyCandidates(string input, List<string> logg)
        {
            List<string> found = new List<string>();
            string raw = (input == null ? "" : input.Trim());
            if (raw.Length == 0) { logg.Add("пустая ссылка"); return found; }

            string url = raw;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            string urlHost = "";
            try { urlHost = new Uri(url).Host; } catch { }

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
                    logg.Add("качаю: " + u);
                    string t = HttpGet(u, 20000);
                    if (t == null || t.Length < 20) { logg.Add("  пусто"); continue; }
                    logg.Add("  получил " + t.Length + " символов");
                    ExtractFromText(t, found, logg);
                }
                catch (Exception ex) { logg.Add("  не вышло: " + Short(ex.Message)); }
            }

            // если дали домен самого DNS-сервера - спрашиваем его самого
            bool isGithub = urlHost.IndexOf("github", StringComparison.OrdinalIgnoreCase) >= 0;
            if (found.Count == 0 && urlHost.Length > 0 && !isGithub)
            {
                try
                {
                    string res = RunPowerShell(
                        "$a=@(); try { $a=@(Resolve-DnsName 'chatgpt.com' -Type A -Server '" + urlHost + "' -DnsOnly -QuickTimeout -EA Stop | Where-Object {$_.IPAddress} | ForEach-Object {$_.IPAddress}) } catch {}; $a -join ','");
                    if (res.IndexOf('.') > 0)
                    {
                        string[] parts = res.Split(new char[] { ',', ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 0; i < parts.Length; i++) { AddUnique(found, parts[i]); }
                        if (found.Count > 0) { logg.Add("  спросил обычный DNS " + urlHost + ": " + string.Join(", ", found.ToArray())); }
                    }
                    else { logg.Add("  обычный DNS " + urlHost + " не ответил"); }
                }
                catch { }

                if (found.Count == 0)
                {
                    try
                    {
                        string j = HttpGet("https://" + urlHost + "/dns-query?name=chatgpt.com&type=A", 15000);
                        MatchCollection mc = Regex.Matches(j, @"""data""\s*:\s*""(\d{1,3}(?:\.\d{1,3}){3})""");
                        for (int i = 0; i < mc.Count; i++) { AddUnique(found, mc[i].Groups[1].Value); }
                        if (found.Count > 0) { logg.Add("  спросил DoH " + urlHost + ": " + string.Join(", ", found.ToArray())); }
                    }
                    catch (Exception ex) { logg.Add("  DoH " + urlHost + " не ответил: " + Short(ex.Message)); }
                }
            }

            if (found.Count == 0) { logg.Add("адрес не найден."); }
            return found;
        }
        // находит и сразу проверяет - пускает ли найденный адрес к чату
        public static string FindAndVerify(string input, List<string> logg)
        {
            List<string> cands = FindProxyCandidates(input, logg);
            if (cands.Count == 0) { return ""; }
            logg.Add("проверяю найденное (chatgpt.com через каждый адрес):");
            string risky = "";
            for (int i = 0; i < cands.Count; i++)
            {
                string code = CheckSite(cands[i], "chatgpt.com", "/robots.txt", 9000);
                bool badCert = LastCertInvalid;
                logg.Add("  " + cands[i] + " -> " + code + (badCert ? "  [сертификат не совпал]" : ""));
                if (badCert)
                {
                    // адрес, который расшифровывает трафик, применяем только если больше нечего взять
                    if (risky.Length == 0) { risky = cands[i]; }
                    continue;
                }
                if (code == "200" || code == "301" || code == "302" || code == "307")
                {
                    logg.Add("  этот пускает: " + cands[i]);
                    return cands[i];
                }
            }
            if (risky.Length > 0)
            {
                logg.Add("ВНИМАНИЕ: единственные рабочие адреса подменяют сертификат — их трафик может");
                logg.Add("расшифровываться на стороне прокси. Подставляю " + risky + " только потому, что");
                logg.Add("рабочих без подмены нет. Используй на свой риск и не вводи там ничего важного.");
                return risky;
            }
            logg.Add("ни один не дал 200, подставляю первый на пробу: " + cands[0]);
            return cands[0];
        }

        public static string GithubProxyIp()
        {
            List<string> logg = new List<string>();
            return FindAndVerify("https://github.com/ASTRACAT2022/host-DNS", logg);
        }

        // ---------- NRPT (DNS-режим) ----------

        public static string RunPowerShell(string script)
        {
            ProcessStartInfo psi = new ProcessStartInfo("powershell",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script.Replace("\"", "\\\"") + "\"");
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
            for (int i = 0; i < list.Length; i++)
            {
                if (!IsValidIp(list[i])) { throw new Exception("плохой адрес DNS-сервера: " + list[i]); }
            }
            if (list.Length == 0) { throw new Exception("не указан ни один DNS-сервер"); }

            string srvList = "'" + string.Join("','", list) + "'";
            string nsList = "'" + string.Join("','", NrptNamespaces) + "'";
            // Сначала ОДИН раз вычищаем всё наше и совпадающее по namespace, и только потом
            // добавляем правила в цикле. Если чистить внутри цикла, второй проход удаляет
            // правило, добавленное первым, и остаётся одно вместо восьми (проверено).
            string script =
                "$srv=@(" + srvList + "); $ns=@(" + nsList + "); " +
                "Get-DnsClientNrptRule -EA SilentlyContinue | Where-Object { ($ns -contains ($_.Namespace -join ',')) -or $_.Comment -eq '" + NrptComment + "' } | Remove-DnsClientNrptRule -Force -EA SilentlyContinue; " +
                "foreach($n in $ns){ " +
                "  Add-DnsClientNrptRule -Namespace $n -NameServers $srv -Comment '" + NrptComment + "' -EA Stop | Out-Null " +
                "}; " +
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
                "Clear-DnsClientCache; " +
                "'снято правил: ' + $r.Count";
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

    internal static class Gui
    {
        private static TextBox ipBox;
        private static TextBox linkBox;
        private static TextBox dnsBox;
        private static TextBox log;
        private static Label stateLabel;
        private static Form form;

        public static void Run()
        {
            form = new Form();
            form.Text = "AiBypass - обход для ChatGPT / Gemini / Spotify";
            form.Width = 740;
            form.Height = 640;
            form.StartPosition = FormStartPosition.CenterScreen;
            form.FormBorderStyle = FormBorderStyle.FixedSingle;
            form.MaximizeBox = false;

            stateLabel = new Label();
            stateLabel.Left = 12; stateLabel.Top = 12; stateLabel.Width = 700; stateLabel.Height = 40;
            stateLabel.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            form.Controls.Add(stateLabel);

            // --- поиск адреса по ссылке на сервис ---
            Label l0 = new Label();
            l0.Text = "Ссылка на сервис (сайт, репозиторий с hosts или DNS-имя) - по ней найти адрес:";
            l0.Left = 12; l0.Top = 56; l0.Width = 700;
            form.Controls.Add(l0);

            linkBox = new TextBox();
            linkBox.Left = 12; linkBox.Top = 78; linkBox.Width = 430;
            linkBox.Font = new Font("Consolas", 9.5f);
            form.Controls.Add(linkBox);

            Button bFind = NewButton("Найти адрес", 452, 76, 120, OnFind);
            Button bGh = NewButton("commonnet (по умолчанию)", 578, 76, 130, OnGithub);

            // --- адрес прокси ---
            Label l1 = new Label();
            l1.Text = "Адрес прокси (только IPv4, вида 217.60.179.6):";
            l1.Left = 12; l1.Top = 112; l1.Width = 400;
            form.Controls.Add(l1);

            ipBox = new TextBox();
            ipBox.Left = 12; ipBox.Top = 134; ipBox.Width = 220;
            ipBox.Font = new Font("Consolas", 11f);
            form.Controls.Add(ipBox);

            Button bCheck = NewButton("Проверить адрес", 250, 132, 140, OnCheck);
            Button bApply = NewButton("Применить", 400, 132, 110, OnApply);
            Button bOff = NewButton("Выключить обход", 520, 132, 188, OnOff);

            // --- DNS-режим ---
            GroupBox gb = new GroupBox();
            gb.Text = "Вариант через DNS (если новый сервис даёт не прокси-IP, а DNS-серверы)";
            gb.Left = 12; gb.Top = 172; gb.Width = 700; gb.Height = 100;
            form.Controls.Add(gb);

            Label l2 = new Label();
            l2.Text = "DNS-серверы через запятую (только адреса, не имена):";
            l2.Left = 12; l2.Top = 24; l2.Width = 400;
            gb.Controls.Add(l2);

            dnsBox = new TextBox();
            dnsBox.Left = 12; dnsBox.Top = 46; dnsBox.Width = 380;
            dnsBox.Font = new Font("Consolas", 10f);
            gb.Controls.Add(dnsBox);

            Button bNrptOn = new Button();
            bNrptOn.Text = "Поставить правила";
            bNrptOn.Left = 404; bNrptOn.Top = 44; bNrptOn.Width = 140;
            bNrptOn.Click += new EventHandler(OnNrptSet);
            gb.Controls.Add(bNrptOn);

            Button bNrptOff = new Button();
            bNrptOff.Text = "Снять правила";
            bNrptOff.Left = 554; bNrptOff.Top = 44; bNrptOff.Width = 134;
            bNrptOff.Click += new EventHandler(OnNrptOff);
            gb.Controls.Add(bNrptOff);

            log = new TextBox();
            log.Left = 12; log.Top = 284; log.Width = 700; log.Height = 310;
            log.Multiline = true;
            log.ScrollBars = ScrollBars.Vertical;
            log.ReadOnly = true;
            log.Font = new Font("Consolas", 9f);
            form.Controls.Add(log);

            RefreshState();

            if (!Core.IsAdmin())
            {
                Write("ВНИМАНИЕ: запущено без прав администратора - менять hosts и правила DNS не получится.");
            }

            Write("Если старый адрес перестал работать: вставь ссылку на сервис сверху и нажми «Найти адрес».");
            Write("Прога сама поищет адрес в их hosts-файле, на сайте и в ответе их DNS, потом проверит его.");

            Application.Run(form);
        }
        private static Button NewButton(string text, int left, int top, int width, EventHandler h)
        {
            Button b = new Button();
            b.Text = text;
            b.Left = left; b.Top = top; b.Width = width; b.Height = 26;
            b.Click += h;
            form.Controls.Add(b);
            return b;
        }

        private static void Write(string s)
        {
            log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s + Environment.NewLine);
        }

        private static void RefreshState()
        {
            string ip = Core.CurrentProxyIp();
            if (ip.Length > 0)
            {
                stateLabel.Text = "Обход включён: домены идут на " + ip;
                stateLabel.ForeColor = Color.DarkGreen;
                if (ipBox.Text.Trim().Length == 0) { ipBox.Text = ip; }
            }
            else
            {
                stateLabel.Text = "Обход выключен: домены резолвятся как обычно";
                stateLabel.ForeColor = Color.DarkRed;
            }
        }

        private static void OnCheck(object sender, EventArgs e)
        {
            string ip = ipBox.Text.Trim();
            if (!Core.IsValidIp(ip)) { Write("Это не IPv4-адрес: " + ip); return; }
            Write("Проверяю " + ip + " (трафик не меняется, только замер)...");
            Application.DoEvents();
            string[][] tests = new string[][]
            {
                new string[] { "chatgpt.com", "/robots.txt" },
                new string[] { "gemini.google.com", "/" },
                new string[] { "open.spotify.com", "/" }
            };
            for (int i = 0; i < tests.Length; i++)
            {
                string code = Core.CheckSiteWithCert(ip, tests[i][0], tests[i][1], 9000);
                string verdict = code == "200" || code == "301" || code == "302" || code == "307" || code == "401" ? "пускает" : "НЕ пускает";
                Write("  " + tests[i][0] + " -> " + code + "  (" + verdict + ")");
            }
            Write("Готово. 200 - страница открылась; 401 - просто нет ключа; 403 у openai.com - челлендж Cloudflare, в браузере пройдёт.");
            Write("Если написано «сертификат не совпал» - этот адрес расшифровывает трафик, ставить его не стоит.");
        }

        private static void OnApply(object sender, EventArgs e)
        {
            string ip = ipBox.Text.Trim();
            try
            {
                Core.ApplyHosts(ip);
                Write("Прописал " + Core.Domains.Length + " доменов на " + ip + ". Бэкап hosts сохранён.");
                RefreshState();
            }
            catch (Exception ex) { Write("Не вышло: " + ex.Message); }
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

        private static void OnGithub(object sender, EventArgs e)
        {
            linkBox.Text = "https://github.com/ASTRACAT2022/host-DNS";
            OnFind(sender, e);
        }

        // ищет адрес по ссылке из верхнего поля и сразу проверяет его
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
                List<string> logg = new List<string>();
                string ip = Core.FindAndVerify(src, logg);
                for (int i = 0; i < logg.Count; i++) { Write("  " + logg[i]); }
                if (ip.Length == 0)
                {
                    Write("Адрес не найден. Способы вручную: (1) у сервиса есть файл hosts - открой его и найди строку «адрес chatgpt.com»;");
                    Write("(2) если это DNS-сервер - спроси его: nslookup chatgpt.com <его-адрес>; (3) ищи в их инструкции строку из четырёх чисел.");
                    return;
                }
                ipBox.Text = ip;
                Write("Подставил в поле адреса: " + ip + ". Нажми «Применить».");
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

    internal static class Program
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        private static bool GetConsoleWindowExists()
        {
            return GetConsoleWindow() != IntPtr.Zero;
        }

        [STAThread]
        private static int Main(string[] args)
        {
            // В оконной сборке консоли нет, и установка кодировки падает с "handle is invalid".
            try { if (GetConsoleWindowExists()) { Console.OutputEncoding = Encoding.UTF8; } } catch { }

            if (args.Length == 0)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try
                {
                    Gui.Run();
                }
                catch (Exception ex)
                {
                    // чтобы падение окна не было молчаливым: пишем рядом с exe
                    try
                    {
                        File.WriteAllText(Path.Combine(Core.ExeDir, "crash.txt"),
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + ex.ToString());
                    }
                    catch { }
                    return 1;
                }
                return 0;
            }

            string cmd = args[0].ToLowerInvariant();
            try
            {
                switch (cmd)
                {
                    case "--state":
                        {
                            string ip = Core.CurrentProxyIp();
                            Console.WriteLine("hosts: " + (ip.Length > 0 ? "включён, прокси " + ip : "блока нет"));
                            Console.WriteLine("админ: " + (Core.IsAdmin() ? "да" : "нет"));
                            Console.WriteLine("NRPT: " + Core.NrptState());
                            return 0;
                        }
                    case "--check":
                        {
                            if (args.Length < 2) { Console.WriteLine("нужен IP"); return 2; }
                            string ip = args[1];
                            if (!Core.IsValidIp(ip)) { Console.WriteLine("плохой IP: " + ip); return 2; }
                            string[][] tests = new string[][]
                            {
                                new string[] { "chatgpt.com", "/robots.txt" },
                                new string[] { "gemini.google.com", "/" },
                                new string[] { "open.spotify.com", "/" }
                            };
                            for (int i = 0; i < tests.Length; i++)
                            {
                                Console.WriteLine(tests[i][0] + " -> " + Core.CheckSiteWithCert(ip, tests[i][0], tests[i][1], 9000));
                            }
                            return 0;
                        }
                    case "--apply":
                        {
                            if (args.Length < 2) { Console.WriteLine("нужен IP"); return 2; }
                            Core.ApplyHosts(args[1]);
                            Console.WriteLine("применено: " + args[1]);
                            return 0;
                        }
                    case "--off":
                        {
                            Core.OffHosts();
                            Console.WriteLine("блок снят");
                            return 0;
                        }
                    case "--github":
                        {
                            List<string> logg = new List<string>();
                            string ip = Core.FindAndVerify("https://github.com/ASTRACAT2022/host-DNS", logg);
                            for (int i = 0; i < logg.Count; i++) { Console.WriteLine(logg[i]); }
                            if (ip.Length == 0) { Console.WriteLine("не найдено"); return 1; }
                            Console.WriteLine(ip);
                            return 0;
                        }
                    case "--find":
                        {
                            if (args.Length < 2) { Console.WriteLine("нужна ссылка или домен"); return 2; }
                            List<string> logg = new List<string>();
                            string ip = Core.FindAndVerify(args[1], logg);
                            for (int i = 0; i < logg.Count; i++) { Console.WriteLine(logg[i]); }
                            if (ip.Length == 0) { Console.WriteLine("не найдено"); return 1; }
                            Console.WriteLine("ИТОГ: " + ip);
                            return 0;
                        }
                    case "--nrpt-set":
                        {
                            if (args.Length < 2) { Console.WriteLine("нужны DNS-серверы"); return 2; }
                            Core.NrptSet(args[1]);
                            Console.WriteLine("правила поставлены: " + args[1]);
                            return 0;
                        }
                    case "--nrpt-off":
                        {
                            Core.NrptOff();
                            Console.WriteLine("правила сняты");
                            return 0;
                        }
                    default:
                        Console.WriteLine("неизвестная команда: " + cmd);
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ОШИБКА: " + ex.Message);
                return 1;
            }
        }
    }
}
