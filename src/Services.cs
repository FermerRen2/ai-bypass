// AiBypass — список сервисов и их доменов.
//
// Каждый сервис заворачивается на прокси-IP через файл hosts: домены указывают
// не на настоящий адрес сайта, а на прокси, который отдаёт страницу из другой страны.
//
// Поле Status честно показывает, что замерено на момент 1.0.0 (ноябрь 2026):
//   Works       - через прокси отвечает, напрямую нет или хуже;
//   Listed      - домены есть в списке сервиса, но сейчас прокси их не обслуживает;
//   Unsupported - сервиса в списке нет, через этот прокси не заработает.

using System.Collections.Generic;

namespace AiBypass
{
    public enum ServiceStatus
    {
        Works,        // через прокси работает, напрямую нет или хуже
        Listed,       // домены в списке сервиса есть, но сейчас прокси их не обслуживает
        Unsupported,  // сервис прокси не обслуживает вовсе
        DoNotEnable   // через прокси ЛОМАЕТСЯ: страница идёт из-за границы, а её запросы - из РФ
    }

    public class Service
    {
        public string Name;
        public string[] Domains;
        public ServiceStatus Status;
        public string Note;

        public Service(string name, ServiceStatus status, string note, string[] domains)
        {
            Name = name;
            Status = status;
            Note = note;
            Domains = domains;
        }

        public string[] DomainsOrDefault
        {
            get { return Domains; }
        }
    }

    public static class Services
    {
        public static readonly Service[] All = new Service[]
        {
            new Service(
                "ChatGPT / OpenAI",
                ServiceStatus.Works,
                "работает",
                new string[]
                {
                    "chatgpt.com", "ab.chatgpt.com", "operator.chatgpt.com", "webrtc.chatgpt.com",
                    "sora.chatgpt.com", "api.openai.com", "auth.openai.com", "auth0.openai.com",
                    "openai.com", "platform.openai.com", "developers.openai.com",
                    "cdn.platform.openai.com", "videos.openai.com", "android.chat.openai.com",
                    "ios.chat.openai.com", "tcr9i.chat.openai.com", "cdn.oaistatic.com",
                    "files.oaiusercontent.com"
                }),

            new Service(
                "Gemini / Google AI",
                ServiceStatus.DoNotEnable,
                "не включать: страница идёт с прокси, а запросы и вход - напрямую из РФ, Google видит разные сессии и отвечает «не удалось обработать запрос»",
                new string[]
                {
                    "gemini.google.com", "aistudio.google.com", "notebooklm.google.com",
                    "generativelanguage.googleapis.com"
                }),

            new Service(
                "Spotify",
                ServiceStatus.Works,
                "работает",
                new string[]
                {
                    "spotify.com", "open.spotify.com", "accounts.spotify.com", "www.spotify.com"
                }),

            new Service(
                "Grok (xAI)",
                ServiceStatus.Works,
                "работает",
                new string[]
                {
                    "grok.com", "assets.grok.com", "accounts.x.ai"
                }),

            new Service(
                "TikTok (веб)",
                ServiceStatus.Works,
                "открывается и так, прокси не обязателен",
                new string[]
                {
                    "www.tiktok.com"
                }),

            new Service(
                "Instagram",
                ServiceStatus.Listed,
                "в списке сервиса есть, но сейчас прокси не отвечает",
                new string[]
                {
                    "instagram.com", "www.instagram.com", "b.i.instagram.com",
                    "help.instagram.com", "z-p42-chat-e2ee-ig.facebook.com"
                }),

            new Service(
                "X / Twitter",
                ServiceStatus.Unsupported,
                "прокси не обслуживает — нужен туннель",
                new string[]
                {
                    "x.com", "www.x.com", "api.x.com",
                    "twitter.com", "www.twitter.com", "t.co"
                }),

            new Service(
                "Telegram",
                ServiceStatus.Unsupported,
                "прокси не обслуживает — режется соединение, нужен туннель или встроенный прокси",
                new string[]
                {
                    "telegram.org", "web.telegram.org", "t.me",
                    "telegram.me", "api.telegram.org", "core.telegram.org"
                })
        };

        public static int Count
        {
            get { return All.Length; }
        }

        // домены сервисов, которые по умолчанию включены
        public static List<string> DefaultDomains()
        {
            List<string> outp = new List<string>();
            for (int i = 0; i < All.Length; i++)
            {
                if (!DefaultOn(All[i])) { continue; }
                for (int j = 0; j < All[i].Domains.Length; j++)
                {
                    if (!outp.Contains(All[i].Domains[j])) { outp.Add(All[i].Domains[j]); }
                }
            }
            return outp;
        }

        public static List<string> AllDomains()
        {
            List<string> outp = new List<string>();
            for (int i = 0; i < All.Length; i++)
            {
                for (int j = 0; j < All[i].Domains.Length; j++)
                {
                    if (!outp.Contains(All[i].Domains[j])) { outp.Add(All[i].Domains[j]); }
                }
            }
            return outp;
        }

        // подпись для списка в окне
        public static string Caption(Service s)
        {
            string mark = "";
            if (s.Status == ServiceStatus.Listed) { mark = "  — не отвечает сейчас"; }
            else if (s.Status == ServiceStatus.Unsupported) { mark = "  — нужен туннель"; }
            else if (s.Status == ServiceStatus.DoNotEnable) { mark = "  — НЕ включать, ломает вход"; }
            return s.Name + "  ·  " + s.Domains.Length + " доменов" + mark;
        }

        // включено ли по умолчанию: всё, что не ломается через прокси
        public static bool DefaultOn(Service s)
        {
            return s.Status != ServiceStatus.DoNotEnable;
        }
    }
}
