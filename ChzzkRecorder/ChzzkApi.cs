using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ChzzkRecorder
{
    public static class ChzzkApi
    {
        // HttpClient는 애플리케이션 수명 동안 하나의 인스턴스를 재사용하는 것이 좋습니다.
        private static readonly HttpClient _httpClient = new HttpClient();

        static ChzzkApi()
        {
            // 치지직 서버에서 봇(Bot)으로 인식해 차단하는 것을 막기 위해 일반 브라우저의 User-Agent를 설정합니다.
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

        /// <summary>
        /// 치지직 채널 ID를 통해 스트리머 이름을 가져옵니다.
        /// </summary>
        /// <param name="channelId">치지직 채널 고유 ID (예: 1234567890abcdef)</param>
        /// <returns>스트리머 이름 (실패 시 null)</returns>
        public static async Task<string?> GetStreamerNameAsync(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId)) return null;

            try
            {
                // 치지직 채널 정보 조회 API URL
                string url = $"https://api.chzzk.naver.com/service/v1/channels/{channelId}";

                // GET 요청 보내기 전에 쿠키 헤더를 추가합니다.
                var request = new HttpRequestMessage(HttpMethod.Get, url);

                // ★ 전역 변수에서 쿠키 문자열을 가져와 추가! (로그인 안 했으면 빈 문자열이 들어감)
                request.Headers.Add("Cookie", AuthManager.GetCookieString());

                // httpClient.GetAsync 대신 SendAsync 사용
                HttpResponseMessage response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode) return null;

                // JSON 응답 읽기
                string jsonResponse = await response.Content.ReadAsStringAsync();

                // System.Text.Json을 사용하여 파싱
                using JsonDocument doc = JsonDocument.Parse(jsonResponse);
                JsonElement root = doc.RootElement;

                // 응답 코드가 200(성공)인지 확인
                if (root.TryGetProperty("code", out JsonElement codeElement) && codeElement.GetInt32() == 200)
                {
                    // content 객체 안의 channelName 가져오기
                    if (root.TryGetProperty("content", out JsonElement contentElement))
                    {
                        if (contentElement.TryGetProperty("channelName", out JsonElement nameElement))
                        {
                            return nameElement.GetString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 네트워크 오류나 파싱 오류 발생 시 (콘솔 출력 또는 로그 저장)
                System.Diagnostics.Debug.WriteLine($"API 호출 오류: {ex.Message}");
            }

            return null; // 정보를 찾지 못함
        }

        /// <summary>
        /// 채널 ID를 통해 현재 방송 상태, 제목, m3u8(HLS) 주소를 가져옵니다.
        /// </summary>
        /// <param name="channelId">치지직 채널 고유 ID</param>
        /// <returns>(방송중 여부, 방송 제목, m3u8 주소)</returns>
        public static async Task<(bool IsLive, string Title, string StreamUrl)> GetLiveStatusAsync(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId)) return (false, string.Empty, string.Empty);

            try
            {
                string url = $"https://api.chzzk.naver.com/service/v2/channels/{channelId}/live-detail";

                var request = new HttpRequestMessage(HttpMethod.Get, url);

                // ★ 19금 생방송 우회를 위한 필수 보안 헤더 및 전체 쿠키 추가
                request.Headers.Add("Cookie", AuthManager.GetCookieString());
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                request.Headers.Add("Origin", "https://chzzk.naver.com");
                request.Headers.Add("Referer", $"https://chzzk.naver.com/live/{channelId}");

                HttpResponseMessage response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return (false, string.Empty, string.Empty);

                string jsonResponse = await response.Content.ReadAsStringAsync();

                using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(jsonResponse);
                System.Text.Json.JsonElement root = doc.RootElement;

                if (root.TryGetProperty("code", out var codeElement) && codeElement.GetInt32() == 200)
                {
                    if (root.TryGetProperty("content", out var contentElement) && contentElement.ValueKind != System.Text.Json.JsonValueKind.Null)
                    {
                        // 방송 상태 가져오기
                        string status = contentElement.TryGetProperty("status", out var statusEl)
                                        ? statusEl.GetString() ?? "" : "";

                        if (status == "OPEN")
                        {
                            string title = contentElement.TryGetProperty("liveTitle", out var titleEl)
                                           ? titleEl.GetString() ?? "제목 없음" : "제목 없음";

                            string streamUrl = string.Empty;

                            // 성인 방송인 경우 쿠키와 헤더가 없으면 이 livePlaybackJson 노드 자체가 안 내려옵니다!
                            if (contentElement.TryGetProperty("livePlaybackJson", out var playbackEl))
                            {
                                string? playbackJsonStr = playbackEl.GetString();
                                if (!string.IsNullOrEmpty(playbackJsonStr))
                                {
                                    using System.Text.Json.JsonDocument playbackDoc = System.Text.Json.JsonDocument.Parse(playbackJsonStr);
                                    if (playbackDoc.RootElement.TryGetProperty("media", out var mediaArray))
                                    {
                                        foreach (var media in mediaArray.EnumerateArray())
                                        {
                                            if (media.TryGetProperty("protocol", out var protocol) && protocol.GetString() == "HLS")
                                            {
                                                streamUrl = media.TryGetProperty("path", out var pathEl)
                                                            ? pathEl.GetString() ?? string.Empty : string.Empty;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }

                            return (true, title, streamUrl);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Live Status API 오류: {ex.Message}");
            }

            return (false, string.Empty, string.Empty);
        }

        public static async Task<(bool IsSuccess, string StreamerName, string Title, string Date, string StreamUrl, string Resolution)> GetVodStreamAsync(string vodUrl)
        {
            var match = System.Text.RegularExpressions.Regex.Match(vodUrl, @"video/(\d+)");
            if (!match.Success) return (false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
            string videoNo = match.Groups[1].Value;

            try
            {
                // ★ 핵심 변경: VOD 정보 API 주소가 v2에서 v3로 변경되었습니다!
                string infoUrl = $"https://api.chzzk.naver.com/service/v3/videos/{videoNo}";
                var request1 = new HttpRequestMessage(HttpMethod.Get, infoUrl);

                request1.Headers.Add("Cookie", AuthManager.GetCookieString());
                request1.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                request1.Headers.Add("Origin", "https://chzzk.naver.com");
                request1.Headers.Add("Referer", $"https://chzzk.naver.com/video/{videoNo}");

                HttpResponseMessage response1 = await _httpClient.SendAsync(request1);
                if (!response1.IsSuccessStatusCode) return (false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

                string infoJson = await response1.Content.ReadAsStringAsync();
                using System.Text.Json.JsonDocument infoDoc = System.Text.Json.JsonDocument.Parse(infoJson);

                if (!infoDoc.RootElement.TryGetProperty("content", out var content) || content.ValueKind == System.Text.Json.JsonValueKind.Null)
                    return (false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

                string videoId = content.TryGetProperty("videoId", out var vId) ? vId.GetString() ?? "" : "";
                string inKey = content.TryGetProperty("inKey", out var iKey) ? iKey.GetString() ?? "" : "";
                string title = content.TryGetProperty("videoTitle", out var vTitle) ? vTitle.GetString() ?? "다시보기" : "다시보기";

                // 1. 스트리머 이름 추출
                string streamerName = "알 수 없음";
                if (content.TryGetProperty("channel", out var channelObj) && channelObj.ValueKind != System.Text.Json.JsonValueKind.Null)
                {
                    streamerName = channelObj.TryGetProperty("channelName", out var cName) ? cName.GetString() ?? "알 수 없음" : "알 수 없음";
                }

                // 2. 방송 일자 추출
                string rawDate = "";
                if (content.TryGetProperty("liveOpenDate", out var liveDate) && liveDate.ValueKind != System.Text.Json.JsonValueKind.Null)
                    rawDate = liveDate.GetString() ?? "";
                else if (content.TryGetProperty("publishDate", out var pubDate) && pubDate.ValueKind != System.Text.Json.JsonValueKind.Null)
                    rawDate = pubDate.GetString() ?? "";

                string formattedDate = "날짜미상";
                if (DateTime.TryParse(rawDate, out DateTime dt))
                {
                    formattedDate = dt.ToString("yyMMdd");
                }

                if (string.IsNullOrEmpty(videoId) || string.IsNullOrEmpty(inKey))
                {
                    // liveRewindPlaybackJson 항목이 있다면 임시 m3u8 스트리밍 주소가 존재함!
                    if (content.TryGetProperty("liveRewindPlaybackJson", out var rewindEl))
                    {
                        string? rewindJsonStr = rewindEl.GetString();
                        if (!string.IsNullOrEmpty(rewindJsonStr))
                        {
                            using System.Text.Json.JsonDocument rewindDoc = System.Text.Json.JsonDocument.Parse(rewindJsonStr);
                            if (rewindDoc.RootElement.TryGetProperty("media", out var mediaArray))
                            {
                                foreach (var media in mediaArray.EnumerateArray())
                                {
                                    if (media.TryGetProperty("protocol", out var protocol) && protocol.GetString() == "HLS")
                                    {
                                        string m3u8Url = media.TryGetProperty("path", out var pathEl) ? pathEl.GetString() ?? "" : "";
                                        // 임시 VOD의 HLS 주소 반환
                                        return (true, streamerName, title, formattedDate, m3u8Url, "1080p(최신)");
                                    }
                                }
                            }
                        }
                    }

                    // 이것조차 없다면 비공개거나 삭제된 영상입니다.
                    return (false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
                }                              

                // 3. 네이버 네온플레이어 API 호출 (MPEG-DASH XML 문서 수신)
                string playbackUrl = $"https://apis.naver.com/neonplayer/vodplay/v2/playback/{videoId}?key={inKey}";
                var request2 = new HttpRequestMessage(HttpMethod.Get, playbackUrl);
                request2.Headers.Add("Accept", "application/dash+xml, application/xml, */*");
                request2.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                HttpResponseMessage response2 = await _httpClient.SendAsync(request2);
                if (!response2.IsSuccessStatusCode) return (false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

                string xmlData = await response2.Content.ReadAsStringAsync();
                string mp4Url = string.Empty;
                string resolution = string.Empty;

                try
                {
                    System.Xml.Linq.XDocument xmlDoc = System.Xml.Linq.XDocument.Parse(xmlData);
                    System.Xml.Linq.XNamespace ns = "urn:mpeg:dash:schema:mpd:2011";

                    var bestRepresentation = System.Linq.Enumerable.FirstOrDefault(
                        System.Linq.Enumerable.OrderByDescending(
                            xmlDoc.Descendants(ns + "Representation"),
                            r => (int?)r.Attribute("height") ?? 0
                        )
                    );

                    if (bestRepresentation != null)
                    {
                        var baseUrlNode = bestRepresentation.Element(ns + "BaseURL");
                        if (baseUrlNode != null)
                        {
                            mp4Url = baseUrlNode.Value;
                            string heightVal = bestRepresentation.Attribute("height")?.Value ?? "알수없음";
                            resolution = heightVal != "알수없음" ? $"{heightVal}p" : "알수없음";
                        }
                    }
                }
                catch (System.Xml.XmlException)
                {
                    System.Diagnostics.Debug.WriteLine("XML 파싱 실패");
                }

                if (!string.IsNullOrEmpty(mp4Url))
                {
                    return (true, streamerName, title, formattedDate, mp4Url, resolution);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"VOD API 오류: {ex.Message}");
            }

            return (false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        }
    }
}