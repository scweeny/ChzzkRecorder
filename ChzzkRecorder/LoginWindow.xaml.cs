using System;
using System.Linq;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace ChzzkRecorder
{
    public partial class LoginWindow : Window
    {
        private bool _isLoginProcessed = false;

        public LoginWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            TxtLoading.Visibility = Visibility.Visible;

            // WebView2 초기화 (필수)
            await webView.EnsureCoreWebView2Async(null);

            // 페이지 이동이 완료될 때마다 발생하는 이벤트 연결
            webView.NavigationCompleted += WebView_NavigationCompleted;

            // 네이버 로그인 페이지로 이동
            webView.CoreWebView2.Navigate("https://nid.naver.com/nidlogin.login");

            TxtLoading.Visibility = Visibility.Collapsed;
        }

        private async void WebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (_isLoginProcessed) return;

            var cookies = await webView.CoreWebView2.CookieManager.GetCookiesAsync("https://.naver.com");

            var nidAut = cookies.FirstOrDefault(c => c.Name == "NID_AUT");
            var nidSes = cookies.FirstOrDefault(c => c.Name == "NID_SES");

            if (nidAut != null && nidSes != null &&
                !string.IsNullOrEmpty(nidAut.Value) && !string.IsNullOrEmpty(nidSes.Value))
            {
                _isLoginProcessed = true;
                webView.NavigationCompleted -= WebView_NavigationCompleted;

                // ★ 네이버 도메인의 "모든" 쿠키를 "이름=값; 이름=값;" 형태로 하나로 묶습니다.
                var cookieStrings = cookies.Select(c => $"{c.Name}={c.Value}");
                AuthManager.FullCookie = string.Join("; ", cookieStrings);

                AuthManager.SaveAuth();

                MessageBox.Show("로그인에 성공했습니다!", "성공", MessageBoxButton.OK, MessageBoxImage.Information);

                this.DialogResult = true;
            }
        }
    }
}