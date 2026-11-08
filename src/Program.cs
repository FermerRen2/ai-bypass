// AiBypass — точка входа. Без аргументов открывается окно, с аргументами работает как консоль.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace AiBypass
{
    internal static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        private static bool HasConsole()
        {
            return GetConsoleWindow() != IntPtr.Zero;
        }

        [STAThread]
        private static int Main(string[] args)
        {
            // В оконной сборке консоли нет, и установка кодировки падает с "handle is invalid".
            try
            {
                if (HasConsole()) { Console.OutputEncoding = Encoding.UTF8; }
            }
            catch { }

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
                    // чтобы падение окна не было молчаливым
                    try
                    {
                        File.WriteAllText(Path.Combine(Core.AppDir, "crash.txt"),
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
                    case "--version":
                    case "-v":
                        Console.WriteLine("AiBypass 1.1.0");
                        return 0;

                    case "--state":
                        {
                            string ip = Core.CurrentProxyIp();
                            Console.WriteLine("hosts: " + (ip.Length > 0
                                ? "включён, " + Core.CurrentDomainCount() + " доменов на " + ip
                                : "блока нет"));
                            Console.WriteLine("админ: " + (Core.IsAdmin() ? "да" : "нет"));
                            Console.WriteLine("NRPT: " + Core.NrptState());
                            return 0;
                        }

                    case "--services":
                        {
                            for (int i = 0; i < Services.Count; i++)
                            {
                                Service s = Services.All[i];
                                Console.WriteLine(s.Name + " [" + s.Status + ", " + s.Domains.Length + "]: "
                                    + string.Join(", ", s.Domains));
                            }
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
                                new string[] { "open.spotify.com", "/" },
                                new string[] { "grok.com", "/" }
                            };
                            for (int i = 0; i < tests.Length; i++)
                            {
                                Console.WriteLine(tests[i][0] + " -> " + Core.CheckSite(ip, tests[i][0], tests[i][1], 9000));
                            }
                            return 0;
                        }

                    case "--apply":
                        {
                            if (args.Length < 2) { Console.WriteLine("нужен IP"); return 2; }
                            if (args.Length >= 3)
                            {
                                // третий аргумент - какие сервисы брать (кусок имени через запятую)
                                string[] want = args[2].Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                                List<string> domains = new List<string>();
                                for (int i = 0; i < Services.Count; i++)
                                {
                                    bool hit = false;
                                    for (int k = 0; k < want.Length; k++)
                                    {
                                        if (Services.All[i].Name.IndexOf(want[k].Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            hit = true;
                                            break;
                                        }
                                    }
                                    if (!hit) { continue; }
                                    Console.WriteLine("беру сервис: " + Services.All[i].Name);
                                    for (int j = 0; j < Services.All[i].Domains.Length; j++)
                                    {
                                        if (!domains.Contains(Services.All[i].Domains[j]))
                                        {
                                            domains.Add(Services.All[i].Domains[j]);
                                        }
                                    }
                                }
                                Core.ApplyHosts(args[1], domains.ToArray());
                                Console.WriteLine("применено: " + args[1] + ", доменов " + domains.Count);
                                return 0;
                            }
                            string[] all = Services.DefaultDomains().ToArray();
                            Core.ApplyHosts(args[1], all);
                            Console.WriteLine("применено: " + args[1] + ", доменов " + all.Length);
                            return 0;
                        }

                    case "--browsers":
                        {
                            List<string> run = Core.RunningProcesses(Core.BrowserProcesses);
                            Console.WriteLine(run.Count == 0 ? "браузеры не запущены" : "запущены: " + string.Join(", ", run.ToArray()));
                            return 0;
                        }

                    case "--restart-browser":
                        {
                            List<string> lines = new List<string>();
                            List<string> done = Core.RestartProcesses(Core.BrowserProcesses, lines, false);
                            for (int i = 0; i < lines.Count; i++) { Console.WriteLine(lines[i]); }
                            Console.WriteLine("перезапущено: " + done.Count);
                            return 0;
                        }

                    case "--restart-proc":
                        {
                            if (args.Length < 2) { Console.WriteLine("нужно имя процесса"); return 2; }
                            List<string> lines = new List<string>();
                            Core.RestartProcesses(new string[] { args[1] }, lines, false);
                            for (int i = 0; i < lines.Count; i++) { Console.WriteLine(lines[i]); }
                            return 0;
                        }

                    case "--fix-gemini":
                        {
                            string ip = Core.CurrentProxyIp();
                            if (ip.Length == 0 && args.Length >= 2) { ip = args[1]; }
                            if (ip.Length == 0) { Console.WriteLine("сейчас обход выключен - укажи адрес"); return 2; }
                            List<string> domains = Services.DefaultDomains();
                            Core.ApplyHosts(ip, domains.ToArray());
                            Console.WriteLine("Gemini убран с прокси, доменов " + domains.Count + " на " + ip);
                            List<string> lines = new List<string>();
                            Core.RestartProcesses(Core.BrowserProcesses, lines, true);
                            for (int i = 0; i < lines.Count; i++) { Console.WriteLine(lines[i]); }
                            return 0;
                        }

                    case "--off":
                        Core.OffHosts();
                        Console.WriteLine("блок снят");
                        return 0;

                    case "--find":
                        {
                            if (args.Length < 2) { Console.WriteLine("нужна ссылка или домен"); return 2; }
                            List<string> lines = new List<string>();
                            string ip = Core.FindAndVerify(args[1], lines);
                            for (int i = 0; i < lines.Count; i++) { Console.WriteLine(lines[i]); }
                            if (ip.Length == 0) { Console.WriteLine("не найдено"); return 1; }
                            Console.WriteLine("ИТОГ: " + ip);
                            return 0;
                        }

                    case "--github":
                        {
                            List<string> lines = new List<string>();
                            string ip = Core.FindAndVerify("https://github.com/ASTRACAT2022/host-DNS", lines);
                            for (int i = 0; i < lines.Count; i++) { Console.WriteLine(lines[i]); }
                            if (ip.Length == 0) { Console.WriteLine("не найдено"); return 1; }
                            Console.WriteLine(ip);
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
                        Core.NrptOff();
                        Console.WriteLine("правила сняты");
                        return 0;

                    case "--help":
                    case "-h":
                    case "/?":
                        PrintHelp();
                        return 0;

                    default:
                        Console.WriteLine("неизвестная команда: " + cmd);
                        PrintHelp();
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ОШИБКА: " + ex.Message);
                return 1;
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine("AiBypass - обход геоблокировок через hosts и DNS.");
            Console.WriteLine();
            Console.WriteLine("Без аргументов открывает окно. Консольные команды:");
            Console.WriteLine("  --state                 показать состояние");
            Console.WriteLine("  --services              список сервисов и доменов");
            Console.WriteLine("  --check <ip>            проверить, пускает ли адрес к сайтам");
            Console.WriteLine("  --apply <ip> [сервисы]  прописать домены на адрес (сервисы через запятую)");
            Console.WriteLine("  --off                   снять блок из hosts");
            Console.WriteLine("  --find <ссылка|домен>   найти адрес прокси по ссылке на сервис");
            Console.WriteLine("  --github                найти адрес в репозитории commonnet");
            Console.WriteLine("  --fix-gemini            убрать Gemini с прокси, сбросить кэш DNS и перезапустить браузер");
            Console.WriteLine("  --restart-browser       перезапустить браузер (сбросить его кэш адресов)");
            Console.WriteLine("  --browsers              какие браузеры сейчас запущены");
            Console.WriteLine("  --nrpt-set <ip,ip>      поставить правила DNS");
            Console.WriteLine("  --nrpt-off              снять правила DNS");
            Console.WriteLine("  --version               версия");
        }
    }
}
