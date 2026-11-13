// AiBypass — встроенный локальный релей.
//
// Зачем он нужен: браузеры не умеют SOCKS5 с логином и паролем, а купленный прокси
// требует именно их. Релей слушает 127.0.0.1 без пароля, сам добавляет авторизацию
// и решает, куда отправить запрос:
//   * домены Google  -> в купленный прокси (выход за границей, Gemini работает);
//   * всё остальное  -> напрямую, обычной сетью (скорость и остальные сайты не страдают).

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace AiBypass
{
    public class ProxySettings
    {
        public string Host = "";
        public int Port = 0;
        public string User = "";
        public string Password = "";

        public static ProxySettings Parse(string url)
        {
            ProxySettings s = new ProxySettings();
            if (string.IsNullOrEmpty(url)) { throw new Exception("пустой адрес прокси"); }
            string u = url.Trim();
            int schemeEnd = u.IndexOf("://");
            if (schemeEnd > 0) { u = u.Substring(schemeEnd + 3); }

            string auth = "";
            int at = u.LastIndexOf('@');
            if (at > 0)
            {
                auth = u.Substring(0, at);
                u = u.Substring(at + 1);
            }

            if (auth.Length > 0)
            {
                int colon = auth.IndexOf(':');
                if (colon < 0) { s.User = auth; }
                else
                {
                    s.User = auth.Substring(0, colon);
                    s.Password = auth.Substring(colon + 1);
                }
            }

            int slash = u.IndexOfAny(new char[] { '/', '?' });
            if (slash > 0) { u = u.Substring(0, slash); }

            int portColon = u.LastIndexOf(':');
            if (portColon <= 0) { throw new Exception("в адресе прокси нет порта: " + url); }
            s.Host = u.Substring(0, portColon);
            int port;
            if (!int.TryParse(u.Substring(portColon + 1), out port)) { throw new Exception("плохой порт в адресе прокси"); }
            s.Port = port;

            if (s.Host.Length == 0 || s.Port <= 0) { throw new Exception("не разобрал адрес прокси: " + url); }
            return s;
        }

        public string Display
        {
            get { return Host + ":" + Port + (User.Length > 0 ? "  (логин " + User + ")" : ""); }
        }
    }

    public static class Relay
    {
        public const int LocalPort = 2080;

        // Через прокси идёт ТОЛЬКО то, что без него не работает: Gemini и Grok.
        // Всё остальное - напрямую. Раньше здесь были общие окончания вроде gstatic.com
        // и ggpht.com: через прокси уезжали обложки, скрипты плеера и видео YouTube -
        // мимо zapret, и ютуб переставал работать.
        public static readonly string[] ProxyHosts = new string[]
        {
            "gemini.google.com",
            "accounts.google.com",
            "generativelanguage.googleapis.com",
            "aistudio.google.com",
            "grok.com",
            "x.ai",
            "api.x.ai",
            "accounts.x.ai",
            "assets.grok.com"
        };

        public static readonly string[] ProxySuffixes = new string[]
        {
            ".clients6.google.com",
            ".grok.com",
            ".x.ai"
        };

        private static TcpListener listener;
        private static Thread acceptThread;
        private static volatile bool running;
        private static ProxySettings settings;

        public static bool IsRunning
        {
            get { return running; }
        }

        public static string CurrentUpstream
        {
            get { return settings == null ? "" : settings.Display; }
        }

        public static void Start(ProxySettings s, List<string> log)
        {
            if (running) { Stop(log); }
            settings = s;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, LocalPort);
                listener.Start();
            }
            catch (Exception ex)
            {
                throw new Exception("не смог занять порт " + LocalPort + ": " + ex.Message);
            }
            running = true;
            acceptThread = new Thread(AcceptLoop);
            acceptThread.IsBackground = true;
            acceptThread.Name = "aibypass-relay";
            acceptThread.Start();
            log.Add("релей поднят на 127.0.0.1:" + LocalPort + ", выход через " + s.Display);
        }

        public static void Stop(List<string> log)
        {
            running = false;
            try { if (listener != null) { listener.Stop(); } }
            catch { }
            listener = null;
            settings = null;
            if (log != null) { log.Add("релей остановлен"); }
        }

        private static void AcceptLoop()
        {
            while (running)
            {
                TcpClient client = null;
                try
                {
                    client = listener.AcceptTcpClient();
                }
                catch { break; }

                TcpClient c = client;
                Thread t = new Thread(delegate () { Handle(c); });
                t.IsBackground = true;
                t.Start();
            }
        }

        private static void Handle(TcpClient client)
        {
            try
            {
                client.NoDelay = true;
                NetworkStream cs = client.GetStream();
                cs.ReadTimeout = 20000;

                int first = cs.ReadByte();
                if (first < 0) { client.Close(); return; }

                if (first == 0x05) { HandleSocks(cs, client); }
                else { HandleHttp(cs, client, (byte)first); }
            }
            catch
            {
                try { client.Close(); }
                catch { }
            }
        }

        // ---------- SOCKS5 со стороны браузера (без пароля) ----------

        private static void HandleSocks(NetworkStream cs, TcpClient client)
        {
            int nMethods = cs.ReadByte();
            byte[] methods = new byte[nMethods];
            ReadExact(cs, methods, nMethods);
            cs.Write(new byte[] { 0x05, 0x00 }, 0, 2);

            byte[] head = new byte[4];
            if (!ReadExact(cs, head, 4)) { client.Close(); return; }
            if (head[1] != 0x01) { Reply(cs, 0x07); client.Close(); return; }

            string host;
            if (head[3] == 0x01)
            {
                byte[] ip = new byte[4];
                if (!ReadExact(cs, ip, 4)) { client.Close(); return; }
                host = new IPAddress(ip).ToString();
            }
            else if (head[3] == 0x03)
            {
                int len = cs.ReadByte();
                byte[] name = new byte[len];
                if (!ReadExact(cs, name, len)) { client.Close(); return; }
                host = Encoding.ASCII.GetString(name);
            }
            else if (head[3] == 0x04)
            {
                byte[] ip6 = new byte[16];
                if (!ReadExact(cs, ip6, 16)) { client.Close(); return; }
                host = new IPAddress(ip6).ToString();
            }
            else { Reply(cs, 0x08); client.Close(); return; }

            byte[] portBytes = new byte[2];
            if (!ReadExact(cs, portBytes, 2)) { client.Close(); return; }
            int port = portBytes[0] * 256 + portBytes[1];

            // Отвечаем "соединено" сразу: дальше надо подсмотреть имя сайта
            // в первых байтах, а браузер без ответа их не пришлёт.
            Reply(cs, 0x00);

            byte[] buffered;
            string sniffed = SniffHostname(cs, out buffered, 2000);
            string target = sniffed.Length > 0 ? sniffed : host;

            TcpClient t = Connect(target, port);
            if (t == null) { client.Close(); return; }
            NetworkStream ts = t.GetStream();
            if (buffered.Length > 0) { ts.Write(buffered, 0, buffered.Length); ts.Flush(); }
            Pump(cs, ts);
        }

        private static void Reply(NetworkStream cs, byte code)
        {
            byte[] r = new byte[] { 0x05, code, 0x00, 0x01, 0, 0, 0, 0, 0, 0 };
            try { cs.Write(r, 0, r.Length); cs.Flush(); }
            catch { }
        }

        private static bool ReadExact(NetworkStream s, byte[] buf, int len)
        {
            int got = 0;
            while (got < len)
            {
                int n = s.Read(buf, got, len - got);
                if (n <= 0) { return false; }
                got += n;
            }
            return true;
        }

        // ---------- HTTP-прокси со стороны браузера ----------

        // Готовый PAC-файл: браузер спрашивает его у нас и сам решает,
        // что отправить в прокси, а что грузить напрямую. Тогда через релей
        // идут только домены из списка, остальное его вообще не касается.
        public static string BuildPac()
        {
            string local = "127.0.0.1:" + LocalPort;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("function FindProxyForURL(url, host) {\n");
            sb.Append("  host = host.toLowerCase();\n");
            sb.Append("  var exact = [");
            for (int i = 0; i < ProxyHosts.Length; i++)
            {
                if (i > 0) { sb.Append(", "); }
                sb.Append("\"" + ProxyHosts[i] + "\"");
            }
            sb.Append("];\n  var suf = [");
            for (int i = 0; i < ProxySuffixes.Length; i++)
            {
                if (i > 0) { sb.Append(", "); }
                sb.Append("\"" + ProxySuffixes[i] + "\"");
            }
            sb.Append("];\n  var i;\n");
            sb.Append("  for (i = 0; i < exact.length; i++) { if (host === exact[i]) return \"PROXY " + local + "\"; }\n");
            sb.Append("  for (i = 0; i < suf.length; i++) { var s = suf[i]; if (host.length > s.length && host.substring(host.length - s.length) === s) return \"PROXY " + local + "\"; }\n");
            sb.Append("  return \"DIRECT\";\n}\n");
            return sb.ToString();
        }

        private static bool ServePac(NetworkStream cs, string head)
        {
            string[] first = head.Split(' ');
            if (first.Length < 2) { return false; }
            if (!first[0].Equals("GET", StringComparison.OrdinalIgnoreCase)) { return false; }
            if (first[1].IndexOf("/proxy.pac", StringComparison.OrdinalIgnoreCase) < 0) { return false; }

            byte[] body = Encoding.ASCII.GetBytes(BuildPac());
            string header = "HTTP/1.1 200 OK\r\n" +
                            "Content-Type: application/x-ns-proxy-autoconfig\r\n" +
                            "Content-Length: " + body.Length + "\r\n" +
                            "Connection: close\r\n\r\n";
            byte[] hb = Encoding.ASCII.GetBytes(header);
            cs.Write(hb, 0, hb.Length);
            cs.Write(body, 0, body.Length);
            cs.Flush();
            return true;
        }

        private static void HandleHttp(NetworkStream cs, TcpClient client, byte firstByte)
        {
            string head = ((char)firstByte).ToString() + ReadHeaders(cs);
            if (ServePac(cs, head)) { client.Close(); return; }

            string[] parts = head.Split(' ');
            if (parts.Length < 2) { client.Close(); return; }

            string method = parts[0];
            string targetUrl = parts[1];

            if (method.Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
            {
                string host; int port;
                if (!SplitHostPort(targetUrl, out host, out port)) { client.Close(); return; }
                if (port == 0) { port = 443; }

                byte[] ok = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection established\r\n\r\n");
                cs.Write(ok, 0, ok.Length);
                cs.Flush();

                byte[] buffered;
                string sniffed = SniffHostname(cs, out buffered, 2000);
                string route = sniffed.Length > 0 ? sniffed : host;

                TcpClient upConn = Connect(route, port);
                if (upConn == null) { client.Close(); return; }
                NetworkStream ts = upConn.GetStream();
                if (buffered.Length > 0) { ts.Write(buffered, 0, buffered.Length); ts.Flush(); }
                Pump(cs, ts);
                return;
            }

            string hostOnly; int portOnly;
            string url = targetUrl;
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) { url = url.Substring(7); }
            int slash = url.IndexOf('/');
            string authority = slash < 0 ? url : url.Substring(0, slash);
            string path = slash < 0 ? "/" : url.Substring(slash);
            if (!SplitHostPort(authority, out hostOnly, out portOnly)) { client.Close(); return; }
            if (portOnly == 0) { portOnly = 80; }

            TcpClient up2 = Connect(hostOnly, portOnly);
            if (up2 == null)
            {
                byte[] err = Encoding.ASCII.GetBytes("HTTP/1.1 502 Bad Gateway\r\n\r\n");
                cs.Write(err, 0, err.Length);
                client.Close();
                return;
            }

            StringBuilder req = new StringBuilder();
            req.Append(method).Append(' ').Append(path).Append(" HTTP/1.1\r\n");
            string[] hlines = head.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 1; i < hlines.Length; i++)
            {
                if (hlines[i].StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase)) { continue; }
                req.Append(hlines[i]).Append("\r\n");
            }
            req.Append("\r\n");
            byte[] rb = Encoding.ASCII.GetBytes(req.ToString());
            NetworkStream us = up2.GetStream();
            us.Write(rb, 0, rb.Length);
            us.Flush();
            Pump(cs, us);
        }

        private static string ReadHeaders(NetworkStream cs)
        {
            // Читаем до пустой строки: конец заголовков - это ровно "\r\n\r\n".
            // Раньше этой проверки не было, и релей висел на каждом запросе до таймаута.
            StringBuilder sb = new StringBuilder();
            while (sb.Length <= 16384)
            {
                int b = cs.ReadByte();
                if (b < 0) { break; }
                sb.Append((char)b);
                int n = sb.Length;
                if (n >= 4 && sb[n-1] == Convert.ToChar(10) && sb[n-2] == Convert.ToChar(13) && sb[n-3] == Convert.ToChar(10) && sb[n-4] == Convert.ToChar(13)) { break; }
                if (n >= 2 && sb[n-1] == Convert.ToChar(10) && sb[n-2] == Convert.ToChar(10)) { break; }
            }
            return sb.ToString();
        }

        private static bool SplitHostPort(string s, out string host, out int port)
        {
            host = ""; port = 0;
            if (string.IsNullOrEmpty(s)) { return false; }
            int colon = s.LastIndexOf(':');
            if (colon < 0) { host = s; return true; }
            host = s.Substring(0, colon);
            int p;
            if (int.TryParse(s.Substring(colon + 1), out p)) { port = p; }
            return host.Length > 0;
        }

        // ---------- соединения ----------

        private static TcpClient Connect(string host, int port)
        {
            if (NeedsProxy(host) && settings != null)
            {
                TcpClient viaProxy = ConnectViaSocks5(host, port);
                if (viaProxy != null) { return viaProxy; }
                // прокси не ответил - идём напрямую, чтобы сайт не отвалился совсем
            }
            try
            {
                TcpClient direct = new TcpClient();
                IAsyncResult ar = direct.BeginConnect(host, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(15000))
                {
                    direct.Close();
                    return null;
                }
                direct.EndConnect(ar);
                return direct;
            }
            catch { return null; }
        }

        public static bool NeedsProxy(string host)
        {
            if (string.IsNullOrEmpty(host)) { return false; }
            string h = host.ToLowerInvariant();

            for (int i = 0; i < ProxyHosts.Length; i++)
            {
                if (h == ProxyHosts[i]) { return true; }
            }
            for (int i = 0; i < ProxySuffixes.Length; i++)
            {
                string s = ProxySuffixes[i];
                if (h.Length > s.Length && h.EndsWith(s, StringComparison.Ordinal)) { return true; }
            }
            return false;
        }
        private static TcpClient ConnectViaSocks5(string host, int port)
        {
            try
            {
                TcpClient c = new TcpClient();
                IAsyncResult ar = c.BeginConnect(settings.Host, settings.Port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(15000)) { c.Close(); return null; }
                c.EndConnect(ar);
                NetworkStream s = c.GetStream();
                s.ReadTimeout = 15000;
                s.WriteTimeout = 15000;

                byte[] hello;
                if (settings.User.Length > 0) { hello = new byte[] { 0x05, 0x02, 0x00, 0x02 }; }
                else { hello = new byte[] { 0x05, 0x01, 0x00 }; }
                s.Write(hello, 0, hello.Length);
                s.Flush();

                byte[] resp = new byte[2];
                if (!ReadExact(s, resp, 2) || resp[0] != 0x05) { c.Close(); return null; }

                if (resp[1] == 0x02)
                {
                    byte[] u = Encoding.ASCII.GetBytes(settings.User);
                    byte[] p = Encoding.ASCII.GetBytes(settings.Password);
                    List<byte> auth = new List<byte>();
                    auth.Add(0x01);
                    auth.Add((byte)u.Length);
                    auth.AddRange(u);
                    auth.Add((byte)p.Length);
                    auth.AddRange(p);
                    byte[] ab = auth.ToArray();
                    s.Write(ab, 0, ab.Length);
                    s.Flush();
                    if (!ReadExact(s, resp, 2) || resp[1] != 0x00) { c.Close(); return null; }
                }
                else if (resp[1] != 0x00) { c.Close(); return null; }

                byte[] name = Encoding.ASCII.GetBytes(host);
                List<byte> req = new List<byte>();
                req.Add(0x05); req.Add(0x01); req.Add(0x00); req.Add(0x03);
                req.Add((byte)name.Length);
                req.AddRange(name);
                req.Add((byte)(port / 256)); req.Add((byte)(port % 256));
                byte[] rb = req.ToArray();
                s.Write(rb, 0, rb.Length);
                s.Flush();

                byte[] head = new byte[4];
                if (!ReadExact(s, head, 4) || head[1] != 0x00) { c.Close(); return null; }

                int skip;
                if (head[3] == 0x01) { skip = 4 + 2; }
                else if (head[3] == 0x03) { int l = s.ReadByte(); skip = l + 2; }
                else if (head[3] == 0x04) { skip = 16 + 2; }
                else { c.Close(); return null; }

                byte[] rest = new byte[skip];
                if (!ReadExact(s, rest, skip)) { c.Close(); return null; }

                return c;
            }
            catch { return null; }
        }

        // Браузеры (Chrome, Яндекс, Opera) сами разрешают имя в адрес и присылают релею
        // уже IP - по нему не понять, Google это или нет. Поэтому смотрим в первые байты
        // запроса: в TLS-рукопожатии открытым текстом лежит имя сайта (SNI), а в обычном
        // HTTP - заголовок Host. То же самое делает Happ и любой нормальный клиент.
        private static string SniffHostname(NetworkStream cs, out byte[] buffered, int waitMs)
        {
            buffered = new byte[0];
            try
            {
                cs.ReadTimeout = waitMs;
                byte[] buf = new byte[4096];
                int n = cs.Read(buf, 0, buf.Length);
                if (n <= 0) { return ""; }
                byte[] data = new byte[n];
                Array.Copy(buf, data, n);
                buffered = data;

                if (n > 5 && data[0] == 0x16)
                {
                    string sni = ParseSni(data, n);
                    if (sni.Length > 0) { return sni; }
                }
                if (n > 4 && data[0] >= 0x41 && data[0] <= 0x5A)
                {
                    string txt = Encoding.ASCII.GetString(data, 0, Math.Min(n, 1024));
                    int hp = txt.IndexOf("Host:", StringComparison.OrdinalIgnoreCase);
                    if (hp >= 0)
                    {
                        int start = hp + 5;
                        int end = txt.IndexOf('\r', start);
                        if (end < 0) { end = Math.Min(txt.Length, start + 200); }
                        string hostValue = txt.Substring(start, end - start).Trim();
                        int colon = hostValue.IndexOf(':');
                        if (colon > 0) { hostValue = hostValue.Substring(0, colon); }
                        return hostValue;
                    }
                }
            }
            catch { }
            return "";
        }

        private static string ParseSni(byte[] d, int len)
        {
            try
            {
                int p = 5; // record header
                if (p + 4 > len) { return ""; }
                int handshakeType = d[p];
                if (handshakeType != 0x01) { return ""; } // не ClientHello
                p += 4;
                p += 2 + 32; // version + random
                int sidLen = d[p]; p += 1 + sidLen;
                int csLen = d[p] * 256 + d[p + 1]; p += 2 + csLen;
                int cmLen = d[p]; p += 1 + cmLen;
                if (p + 2 > len) { return ""; }
                int extTotal = d[p] * 256 + d[p + 1]; p += 2;
                int end = Math.Min(len, p + extTotal);
                while (p + 4 <= end)
                {
                    int type = d[p] * 256 + d[p + 1];
                    int size = d[p + 2] * 256 + d[p + 3];
                    p += 4;
                    if (type == 0x0000) // server_name
                    {
                        int q = p + 2;
                        int nameType = d[q]; q += 1;
                        if (nameType == 0)
                        {
                            int nameLen = d[q] * 256 + d[q + 1]; q += 2;
                            if (q + nameLen <= len) { return Encoding.ASCII.GetString(d, q, nameLen); }
                        }
                        return "";
                    }
                    p += size;
                }
            }
            catch { }
            return "";
        }

        private static void Pump(NetworkStream a, NetworkStream b)
        {
            TcpClient dummy = null;
            Thread up = new Thread(delegate ()
            {
                try { Copy(a, b); } catch { }
                finally { CloseQuietly(a); CloseQuietly(b); }
            });
            up.IsBackground = true;
            up.Start();
            try { Copy(b, a); } catch { }
            finally { CloseQuietly(a); CloseQuietly(b); }
            if (dummy != null) { dummy.Close(); }
        }

        private static void CloseQuietly(NetworkStream s)
        {
            try { s.Close(); }
            catch { }
        }

        private static void Copy(NetworkStream from, NetworkStream to)
        {
            byte[] buf = new byte[16384];
            while (true)
            {
                int n = from.Read(buf, 0, buf.Length);
                if (n <= 0) { return; }
                to.Write(buf, 0, n);
                to.Flush();
            }
        }

        // ---------- проверка самого прокси ----------

        // Открывает соединение через прокси к нужному хосту и делает запрос.
        // Возвращает "код ip=... loc=..." или текст ошибки.
        public static string TestProxy(ProxySettings s, string host, string path, int timeoutMs)
        {
            ProxySettings old = settings;
            settings = s;
            try
            {
                TcpClient c = ConnectViaSocks5(host, 443);
                if (c == null) { return "прокси не отвечает"; }
                try
                {
                    using (System.Net.Security.SslStream ssl = new System.Net.Security.SslStream(c.GetStream(), false,
                        new System.Net.Security.RemoteCertificateValidationCallback(delegate { return true; })))
                    {
                        ssl.ReadTimeout = timeoutMs;
                        ssl.WriteTimeout = timeoutMs;
                        ssl.AuthenticateAsClient(host, null,
                            System.Security.Authentication.SslProtocols.Tls12 |
                            System.Security.Authentication.SslProtocols.Tls13, false);
                        string req = "GET " + path + " HTTP/1.1\r\nHost: " + host +
                                     "\r\nUser-Agent: Mozilla/5.0 AiBypass\r\nAccept: */*\r\nConnection: close\r\n\r\n";
                        byte[] data = Encoding.ASCII.GetBytes(req);
                        ssl.Write(data);
                        ssl.Flush();
                        StreamReader sr = new StreamReader(ssl, Encoding.ASCII);
                        string status = sr.ReadLine();
                        if (status == null) { return "пустой ответ"; }
                        string[] sp = status.Split(' ');
                        return sp.Length >= 2 ? sp[1] : status;
                    }
                }
                finally { c.Close(); }
            }
            catch (Exception ex) { return "ошибка: " + ex.Message; }
            finally { settings = old; }
        }
    }
}
