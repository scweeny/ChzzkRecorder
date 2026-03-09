using System;
using System.IO;
using System.Text.Json;

namespace ChzzkRecorder
{
    public static class AuthManager
    {
        // 네이버의 모든 쿠키를 하나로 묶어서 저장합니다.
        public static string FullCookie { get; set; } = string.Empty;

        // 로그인 여부 확인 (세션 쿠키가 포함되어 있는지 확인)
        public static bool IsLoggedIn => !string.IsNullOrEmpty(FullCookie) && FullCookie.Contains("NID_SES");

        // ★ 수정된 부분: 저장 경로를 %AppData%\ChzzkRecorder\naver_auth.json 으로 변경
        private static readonly string AuthFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ChzzkRecorder",
            "naver_auth.json");

        public static string GetCookieString()
        {
            return FullCookie;
        }

        public static void SaveAuth()
        {
            // ★ 폴더가 존재하지 않으면 생성하는 방어 로직 추가
            string dir = Path.GetDirectoryName(AuthFilePath)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var data = new { FullCookie };
            string json = JsonSerializer.Serialize(data);
            File.WriteAllText(AuthFilePath, json);
        }

        public static void LoadAuth()
        {
            if (File.Exists(AuthFilePath))
            {
                try
                {
                    string json = File.ReadAllText(AuthFilePath);
                    using JsonDocument doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("FullCookie", out JsonElement cookieEl))
                    {
                        FullCookie = cookieEl.GetString() ?? "";
                    }
                    else
                    {
                        // (하위 호환) 예전에 저장해둔 방식인 경우 다시 묶어줍니다.
                        if (doc.RootElement.TryGetProperty("NID_AUT", out JsonElement aut) && doc.RootElement.TryGetProperty("NID_SES", out JsonElement ses))
                        {
                            FullCookie = $"NID_AUT={aut.GetString()}; NID_SES={ses.GetString()};";
                        }
                    }
                }
                catch { }
            }
        }

        public static void Logout()
        {
            FullCookie = string.Empty;
            if (File.Exists(AuthFilePath))
            {
                File.Delete(AuthFilePath);
            }
        }
    }
}