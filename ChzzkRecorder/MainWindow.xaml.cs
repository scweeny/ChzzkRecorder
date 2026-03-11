using Microsoft.Win32;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Path = System.IO.Path;



namespace ChzzkRecorder
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public ObservableCollection<DummyStreamer> StreamerList { get; set; } = new ObservableCollection<DummyStreamer>();
        public ObservableCollection<DummyVod> VodList { get; set; } = new ObservableCollection<DummyVod>();


        private Dictionary<string, Process> _activeProcesses = new Dictionary<string, Process>();
        private Dictionary<string, CancellationTokenSource> _monitorTokens = new Dictionary<string, CancellationTokenSource>(); // ★ 추가됨

        private readonly string _settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChzzkRecorder", "settings.json");

        public MainWindow()
        {
            InitializeComponent();

            this.StateChanged += MainWindow_StateChanged;

            StreamerList = new ObservableCollection<DummyStreamer>();
            StreamerDataGrid.ItemsSource = StreamerList;

            VodDataGrid.ItemsSource = VodList;

            LoadSettings();

            AuthManager.LoadAuth();
            UpdateLoginStatusUI();

            string[] args = Environment.GetCommandLineArgs();
            if (args.Contains("-startup"))
            {
                // -startup 인자가 있다면 윈도우가 켠 것이므로, 창을 최소화 상태로 바꿉니다.
                // (이전에 만들어둔 StateChanged 이벤트가 최소화를 감지하고 알아서 트레이로 숨겨줍니다!)
                this.WindowState = WindowState.Minimized;
            }

            if (ChkAutoStartRecording.IsChecked == true)
            {
                // 목록에 있는 모든 스트리머를 대상으로 StartRecordingAsync(감시 루프)를 실행합니다.
                foreach (var streamer in StreamerList)
                {
                    // _ = 를 붙여 경고를 없애고 백그라운드 스레드로 비동기 실행을 던져놓습니다.
                    _ = StartRecordingAsync(streamer);
                }
            }

        }

        private void LoadSettings()
        {
            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);

                    if (settings != null)
                    {
                        TxtDefaultSaveFolder.Text = settings.DefaultSaveFolder;
                        CmbFileNameFormat.SelectedIndex = settings.FileNameFormatIndex;
                        TxtFfmpegPath.Text = settings.FfmpegPath;

                        if (settings.VideoCodec == "Copy") RdoCodecCopy.IsChecked = true;
                        else if (settings.VideoCodec == "H265") RdoCodecH265.IsChecked = true;
                        else if (settings.VideoCodec == "AV1") RdoCodecAV1.IsChecked = true;
                        else RdoCodecH264.IsChecked = true;

                        if (settings.EncoderType == "NVENC") RdoEncNVENC.IsChecked = true;
                        else if (settings.EncoderType == "QSV") RdoEncQSV.IsChecked = true;
                        else if (settings.EncoderType == "AMF") RdoEncAMF.IsChecked = true;
                        else RdoEncCPU.IsChecked = true; // 기본값

                        TxtBitrate.Text = settings.Bitrate.ToString();
                        TxtCheckInterval.Text = settings.CheckInterval.ToString();
                        ChkMinimizeToTray.IsChecked = settings.MinimizeToTray;
                        ChkRunAtStartup.IsChecked = settings.RunAtStartup;
                        ChkAutoStartRecording.IsChecked = settings.AutoStartRecording;

                        // ★ 방어 코드 추가: SavedStreamers가 null이 아닐 때만 반복문 실행!
                        if (settings.SavedStreamers != null)
                        {
                            foreach (var streamer in settings.SavedStreamers)
                            {
                                streamer.State = "Wait";
                                streamer.Title = "-";
                                streamer.StartTime = "-";

                                StreamerList.Add(streamer);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 불러오기 실패 시 파일이 깨진 것일 수 있으므로 사용자에게 알림
                    System.Diagnostics.Debug.WriteLine($"설정 불러오기 오류: {ex.Message}");
                }
            }
        }

        private void SaveSettings()
        {
            try
            {
                var settings = new AppSettings
                {
                    // ★ StreamerList가 혹시라도 null이면 빈 리스트를 대신 넣도록 수정
                    SavedStreamers = StreamerList?.ToList() ?? new List<DummyStreamer>(),
                    DefaultSaveFolder = TxtDefaultSaveFolder.Text,
                    FileNameFormatIndex = CmbFileNameFormat.SelectedIndex,
                    FfmpegPath = TxtFfmpegPath.Text,
                    VideoCodec = RdoCodecCopy.IsChecked == true ? "Copy" : (RdoCodecH264.IsChecked == true ? "H264" : (RdoCodecH265.IsChecked == true ? "H265" : "AV1")),
                    EncoderType = RdoEncCPU.IsChecked == true ? "CPU" : (RdoEncNVENC.IsChecked == true ? "NVENC" : (RdoEncQSV.IsChecked == true ? "QSV" : "AMF")),
                    Bitrate = int.TryParse(TxtBitrate.Text, out int b) ? b : 8000,
                    CheckInterval = int.TryParse(TxtCheckInterval.Text, out int c) ? c : 5,
                    MinimizeToTray = ChkMinimizeToTray.IsChecked ?? true,
                    RunAtStartup = ChkRunAtStartup.IsChecked ?? false,
                    AutoStartRecording = ChkAutoStartRecording.IsChecked ?? false
                };

                string dir = Path.GetDirectoryName(_settingsFilePath)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"설정 저장 오류: {ex.Message}");
            }
        }

        private void Codec_Checked(object sender, RoutedEventArgs e)
        {
            // 윈도우 로드 전 null 방지
            if (RdoCodecAV1 == null || RdoEncCPU == null || GridEncoderSelection == null || GridBitrateSelection == null) return;

            // ★ 원본이 선택되었을 때
            if (RdoCodecCopy.IsChecked == true)
            {
                GridEncoderSelection.Visibility = Visibility.Collapsed; // 인코더 숨김
                GridBitrateSelection.Visibility = Visibility.Collapsed; // 비트레이트 숨김
                TxtBitrate.Text = "8000"; // 비트레이트 값을 8000으로 강제 초기화
            }
            else
            {
                // 인코딩을 진행할 때 다시 보이게 함
                GridEncoderSelection.Visibility = Visibility.Visible;
                GridBitrateSelection.Visibility = Visibility.Visible;

                // 기존 AV1 제어 로직
                if (RdoCodecAV1.IsChecked == true)
                {
                    RdoEncCPU.IsEnabled = false;
                    if (RdoEncCPU.IsChecked == true)
                    {
                        RdoEncNVENC.IsChecked = true;
                    }
                }
                else
                {
                    RdoEncCPU.IsEnabled = true;
                }
            }
        }

        private void TxtBitrate_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            // 정규식을 사용하여 0~9(숫자)가 아닌 값이 들어오면 입력을 취소(Handled = true)합니다.
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void BtnBitrateUp_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtBitrate.Text, out int bitrate))
            {
                // 최대 비트레이트 제한 (예: 50000 kbps)
                TxtBitrate.Text = Math.Min(50000, bitrate + 500).ToString();
            }
            else
            {
                TxtBitrate.Text = "8000"; // 숫자가 꼬여있으면 기본값 복구
            }
        }

        private void BtnBitrateDown_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtBitrate.Text, out int bitrate))
            {
                // 최소 비트레이트는 500 kbps로 제한 (너무 낮으면 화질이 완전히 뭉개짐)
                TxtBitrate.Text = Math.Max(500, bitrate - 500).ToString();
            }
            else
            {
                TxtBitrate.Text = "8000";
            }
        }

        private void TxtBitrate_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (TxtSizePerSec == null) return;

            // 사용자가 입력한 텍스트가 숫자로 변환 가능한지 확인
            if (int.TryParse(TxtBitrate.Text, out int bitrate))
            {
                // 계산 공식: 
                // 1 kbps = 1000 bits per second
                // bytes/sec = (bitrate * 1000) / 8
                // MB/sec = bytes / (1024 * 1024)
                double mbPerSec = (bitrate * 1000.0) / 8.0 / 1024.0 / 1024.0;

                // 소수점 2자리까지만 예쁘게 표시
                TxtSizePerSec.Text = $"(약 {mbPerSec:F2} MB/초)";
            }
            else
            {
                TxtSizePerSec.Text = "(올바른 숫자를 입력하세요)";
            }
        }

        private void BtnIntervalUp_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtCheckInterval.Text, out int interval))
                TxtCheckInterval.Text = Math.Min(600, interval + 5).ToString();
        }

        private void BtnIntervalDown_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtCheckInterval.Text, out int interval))
                TxtCheckInterval.Text = Math.Max(10, interval - 5).ToString();
        }




        private void UpdateLoginStatusUI()
        {
            // 설정 탭에 있는 TextBlock이나 Button의 x:Name이 지정되어 있어야 합니다.
            // (예: TxtLoginStatus, BtnLogin)
            if (AuthManager.IsLoggedIn)
            {
                // 실제 XAML에 맞춰 이름을 수정하세요.
                TxtLoginStatus.Text = "현재 상태: 로그인 완료 (세션 유지 중)";
                BtnLogin.Content = "로그아웃";
            }
            else
            {
                TxtLoginStatus.Text = "현재 상태: 로그인되어 있지 않음";
                BtnLogin.Content = "로그인 하기";
            }
        }

        private void Window_Initialized(object sender, EventArgs e)
        {

        }
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            CleanupBeforeExit();
            // 프로그램이 종료되기 직전에 무조건 설정을 저장합니다.
            SaveSettings();
            MyNotifyIcon?.Dispose(); // 아이콘 찌꺼기 제거
        }

        private void BtnBrowseDefaultFolder_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderDialog dialog = new OpenFolderDialog
            {
                Title = "녹화 파일을 저장할 기본 폴더를 선택하세요",
                // 기존 텍스트박스의 경로가 유효하면 그곳을 열고, 아니면 내 비디오 폴더 열기
                InitialDirectory = System.IO.Directory.Exists(TxtDefaultSaveFolder.Text)
                                    ? TxtDefaultSaveFolder.Text
                                    : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
            };

            if (dialog.ShowDialog() == true)
            {
                TxtDefaultSaveFolder.Text = dialog.FolderName;

                // 폴더를 바꿨으니 즉시 설정 파일에 저장하도록 SaveSettings() 호출 (선택 사항)
                SaveSettings();
            }
        }


        private void btn_Record_AddStremaer_Click(object sender, RoutedEventArgs e)
        {
            AddStreamerWindow addWindow = new AddStreamerWindow(TxtDefaultSaveFolder.Text);
            addWindow.Owner = this;

            if (addWindow.ShowDialog() == true)
            {
                DummyStreamer newStreamer = new DummyStreamer
                {
                    ChannelId = addWindow.InputChannelId, // 새로 추가된 속성!
                    State = "Wait",
                    Name = addWindow.InputStreamerName,
                    Title = "-",
                    StartTime = "-",
                    SavePath = addWindow.InputSaveFolder
                };

                StreamerList.Add(newStreamer);
            }
        }
        private void BtnEditStreamer_Click(object sender, RoutedEventArgs e)
        {
            // 1. 항목이 정확히 1개 선택되었는지 확인
            if (StreamerDataGrid.SelectedItems.Count != 1)
            {
                MessageBox.Show("수정할 스트리머를 하나만 선택해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedStreamer = (DummyStreamer)StreamerDataGrid.SelectedItem;

            // 2. 녹화 또는 감시 중일 때는 안전을 위해 수정 차단
            if (selectedStreamer.State == "Rec" || _monitorTokens.ContainsKey(selectedStreamer.ChannelId))
            {
                MessageBox.Show("녹화 또는 감시 중인 스트리머는 수정할 수 없습니다.\n먼저 해당 스트리머의 [선택 중지]를 눌러주세요.", "수정 불가", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 3. 수정 모드로 창 띄우기 (선택된 스트리머 객체를 넘겨줌)
            AddStreamerWindow editWindow = new AddStreamerWindow(selectedStreamer);
            editWindow.Owner = this;

            if (editWindow.ShowDialog() == true)
            {
                // 4. 사용자가 '수정하기'를 눌렀다면 새로운 값으로 업데이트
                selectedStreamer.ChannelId = editWindow.InputChannelId;
                selectedStreamer.Name = editWindow.InputStreamerName;
                selectedStreamer.SavePath = editWindow.InputSaveFolder;

                // 5. 변경된 설정 저장
                SaveSettings();
            }
        }

        private void BtnDeleteStreamer_Click(object sender, RoutedEventArgs e)
        {
            // 1. DataGrid에서 선택된 항목이 있는지 확인
            if (StreamerDataGrid.SelectedItems.Count == 0)
            {
                MessageBox.Show("삭제할 스트리머를 먼저 선택해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 2. 선택된 항목들을 리스트로 복사 (다중 선택 지원)
            // 주의: SelectedItems를 직접 반복문으로 돌리며 삭제하면 오류가 발생하므로 ToList()로 복사본을 만듭니다.
            var selectedStreamers = StreamerDataGrid.SelectedItems.Cast<DummyStreamer>().ToList();

            // 3. 사용자에게 삭제 확인 메시지 띄우기
            string message = selectedStreamers.Count == 1
                ? $"'{selectedStreamers[0].Name}' 스트리머를 목록에서 삭제하시겠습니까?"
                : $"선택한 {selectedStreamers.Count}명의 스트리머를 목록에서 삭제하시겠습니까?";

            MessageBoxResult result = MessageBox.Show(message, "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Question);

            // 4. 사용자가 '예(Yes)'를 눌렀을 때만 삭제 진행
            if (result == MessageBoxResult.Yes)
            {
                foreach (var streamer in selectedStreamers)
                {
                    // ObservableCollection에서 제거하면 DataGrid 화면에서도 즉시 사라집니다.
                    StreamerList.Remove(streamer);
                }
            }
        }

        private async void BtnStartSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = StreamerDataGrid.SelectedItems.Cast<DummyStreamer>().ToList();
            if (!selectedItems.Any())
            {
                MessageBox.Show("녹화를 시작할 항목을 선택해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (var streamer in selectedItems)
            {
                await StartRecordingAsync(streamer);
            }
        }

        private async void BtnStartAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var streamer in StreamerList)
            {
                await StartRecordingAsync(streamer);
            }
        }

        private void BtnStopSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = StreamerDataGrid.SelectedItems.Cast<DummyStreamer>().ToList();
            foreach (var streamer in selectedItems)
            {
                StopRecording(streamer);
            }
        }


        private void BtnStopAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var streamer in StreamerList)
            {
                StopRecording(streamer);
            }
        }

        // ====================================================================
        // 공통 동작: 감시 및 녹화 시작 (진입점)
        // ====================================================================
        private async Task StartRecordingAsync(DummyStreamer streamer)
        {
            // 이미 감시 중이거나 녹화 중이면 무시
            if (_monitorTokens.ContainsKey(streamer.ChannelId)) return;

            string ffmpegPath = TxtFfmpegPath.Text;
            if (!File.Exists(ffmpegPath))
            {
                MessageBox.Show("FFmpeg 실행 파일을 찾을 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 감시 루프를 위한 취소 토큰 생성
            var cts = new CancellationTokenSource();
            _monitorTokens[streamer.ChannelId] = cts;

            // 백그라운드 무한 감시 루프 시작 (Task.Run으로 UI 멈춤 방지)
            _ = Task.Run(() => MonitoringLoopAsync(streamer, ffmpegPath, cts.Token));
        }

        // ====================================================================
        // 백그라운드 무한 감시 루프
        // ====================================================================
        private async Task MonitoringLoopAsync(DummyStreamer streamer, string ffmpegPath, CancellationToken token)
        {
            int interval = 5; // 기본값
            Application.Current.Dispatcher.Invoke(() =>
            {
                interval = int.TryParse(TxtCheckInterval.Text, out int res) ? res : 5;
            });
            streamer.AddLog($"--- 자동 녹화 감시를 시작합니다. (주기: {interval}초) ---");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    streamer.AddLog("서버에 방송 상태를 확인하는 중...");
                    Application.Current.Dispatcher.Invoke(() => streamer.Title = "방송 상태 확인 중...");

                    // API 호출
                    var (isLive, liveTitle, streamUrl) = await ChzzkApi.GetLiveStatusAsync(streamer.ChannelId);

                    if (isLive)
                    {
                        streamer.AddLog($"방송 켜짐 감지됨! 제목: {liveTitle}");
                        // 방송이 켜졌으므로 FFmpeg 녹화 프로세스 실행
                        await RunFfmpegProcessAsync(streamer, ffmpegPath, liveTitle, streamUrl, token);

                        // RunFfmpegProcessAsync는 녹화가 끝날 때까지 대기(Block)합니다.
                        // 방송이 끝나서 FFmpeg가 종료되면, 다시 이 무한 루프로 돌아와서 다음 방송을 대기합니다!
                    }
                    else
                    {
                        streamer.AddLog("현재 오프라인 상태입니다.");
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            streamer.State = "Wait";
                            streamer.Title = $"오프라인 (다음 확인: {interval}초 뒤)";
                        });
                    }

                    // 토큰 취소 여부 확인
                    if (token.IsCancellationRequested) break;

                    // 설정한 감지 주기(초) 만큼 대기 후 다시 확인
                    await Task.Delay(interval * 1000, token);
                }
                catch (TaskCanceledException)
                {
                    // 사용자가 중지 버튼을 눌러 Delay가 취소된 경우 자연스럽게 루프 종료
                    break;
                }
                catch (Exception)
                {
                    // 일시적 네트워크 에러 시 무시하고 다음 주기에 재시도
                    Application.Current.Dispatcher.Invoke(() => streamer.Title = "오류 발생 (재시도 대기중)");
                    try { await Task.Delay(interval * 1000, token); } catch { break; }
                }
            }
        }

        // ====================================================================
        // 실제 FFmpeg 녹화 프로세스 실행 (종료 시까지 대기)
        // ====================================================================
        private async Task RunFfmpegProcessAsync(DummyStreamer streamer, string ffmpegPath, string liveTitle, string streamUrl, CancellationToken token)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                streamer.State = "Rec";
                streamer.Title = liveTitle;
                streamer.StartTime = DateTime.Now.ToString("HH:mm:ss");
            });

            string dateStr = DateTime.Now.ToString("yyMMdd_HHmmss");
            string safeTitle = GetSafeFileName(liveTitle);
            string safeStreamer = GetSafeFileName(streamer.Name);

            string fileName = "";
            string vCodec = "";
            string bitrate = "";

            Application.Current.Dispatcher.Invoke(() =>
            {
                fileName = CmbFileNameFormat.SelectedIndex == 0 ? $"{dateStr}_{safeStreamer}_{safeTitle}.mkv" : $"{safeStreamer}_{dateStr}_{safeTitle}.mkv";
                vCodec = GetFfmpegVideoCodec();
                bitrate = TxtBitrate.Text;
                if (!Directory.Exists(streamer.SavePath)) Directory.CreateDirectory(streamer.SavePath);
            });

            string outputPath = Path.Combine(streamer.SavePath, fileName);
            // ★ FFmpeg가 영상 조각(m4s)을 받을 때도 19금 인증이 풀리지 않도록 완벽하게 위장합니다.
            string userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
            string cookieHeader = string.IsNullOrEmpty(AuthManager.GetCookieString()) ? "" : $"Cookie: {AuthManager.GetCookieString()}\r\n";

            // Referer에 정확히 해당 스트리머의 채널 주소를 박아줍니다.
            string headers = $"User-Agent: {userAgent}\r\nOrigin: https://chzzk.naver.com\r\nReferer: https://chzzk.naver.com/live/{streamer.ChannelId}\r\n{cookieHeader}";

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-stats");
            psi.ArgumentList.Add("-headers"); psi.ArgumentList.Add(headers);
            psi.ArgumentList.Add("-reconnect"); psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-reconnect_streamed"); psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-reconnect_delay_max"); psi.ArgumentList.Add("5");
            psi.ArgumentList.Add("-extension_picky");
            psi.ArgumentList.Add("0");
            psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(streamUrl);
            psi.ArgumentList.Add("-c:v"); psi.ArgumentList.Add(vCodec);
            if (vCodec != "copy")
            {
                psi.ArgumentList.Add("-b:v"); psi.ArgumentList.Add($"{bitrate}k");
            }
            psi.ArgumentList.Add("-c:a"); psi.ArgumentList.Add("copy");
            psi.ArgumentList.Add(outputPath);

            Process process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    // 앞뒤 공백 제거
                    string line = e.Data.TrimStart();

                    // ★ "frame=" 또는 "size=" 로 시작하는 진행 상황 줄이거나, "error" 단어가 포함된 경우만 로그창에 띄움
                    if (line.StartsWith("frame=") || line.StartsWith("size=") || line.ToLower().Contains("error"))
                    {
                        streamer.AddLog(line);
                    }
                }
            };


            // 사용자가 '중지' 버튼을 눌렀을 때 토큰이 취소되면 이 콜백이 실행되어 안전하게 녹화 종료
            using (token.Register(() =>
            {
                if (!process.HasExited)
                {
                    try { process.StandardInput.WriteLine("q"); } catch { }
                    // 3초 뒤에도 안 꺼지면 강제 종료
                    Task.Delay(3000).ContinueWith(t => { try { if (!process.HasExited) process.Kill(); } catch { } });
                }
            }))
            {
                process.Start();
                process.BeginErrorReadLine();

                _activeProcesses[streamer.ChannelId] = process;

                // ★ FFmpeg가 끝날 때까지 여기서 멈춰서 기다립니다. (방송이 끝날 때까지 대기)
                await process.WaitForExitAsync();
                streamer.AddLog($"FFmpeg 인코딩이 종료되었습니다. (저장 완료: {fileName})");
                _activeProcesses.Remove(streamer.ChannelId);

                // ★ 버그 수정: 사용자가 [중단] 버튼을 눌러서 강제로 꺼진 게 "아닐 때만" 대기 글씨를 띄웁니다!
                if (!token.IsCancellationRequested)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        streamer.State = "Wait";
                        streamer.Title = "녹화 종료 (다음 방송 대기 중...)";
                        streamer.StartTime = "-";
                    });
                }
            }
        }

        // ====================================================================
        // 공통 동작 메서드 (중지)
        // ====================================================================
        private void StopRecording(DummyStreamer streamer)
        {
            // 무한 루프 감시망 취소 처리 (토큰 캔슬)
            if (_monitorTokens.TryGetValue(streamer.ChannelId, out var cts))
            {
                cts.Cancel();     // 무한 대기(Task.Delay)와 FFmpeg 프로세스를 즉시 안전하게 중단시킵니다.
                cts.Dispose();
                _monitorTokens.Remove(streamer.ChannelId);
            }

            // UI 복구
            Application.Current.Dispatcher.Invoke(() =>
            {
                streamer.State = "Wait";
                streamer.Title = "-";
                streamer.StartTime = "-";
            });
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            if (AuthManager.IsLoggedIn)
            {
                // 이미 로그인 되어있다면 로그아웃 처리
                if (MessageBox.Show("로그아웃 하시겠습니까?", "로그아웃", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    AuthManager.Logout();
                    UpdateLoginStatusUI();
                }
            }
            else
            {
                // 로그인 창(WebView2) 띄우기
                LoginWindow loginWindow = new LoginWindow();
                loginWindow.Owner = this;

                if (loginWindow.ShowDialog() == true)
                {
                    // 로그인 성공 시 UI 업데이트
                    UpdateLoginStatusUI();
                }
            }
        }

        private async void BtnDownloadFfmpeg_Click(object sender, RoutedEventArgs e)
        {
            BtnDownloadFfmpeg.IsEnabled = false;
            BtnDownloadFfmpeg.Content = "다운로드 중...";

            try
            {
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string appFolder = Path.Combine(appDataPath, "ChzzkRecorder");
                string ffmpegFolder = Path.Combine(appFolder, "ffmpeg");

                // 확장자를 .7z로 변경
                string archivePath = Path.Combine(appFolder, "ffmpeg.7z");

                if (!Directory.Exists(appFolder)) Directory.CreateDirectory(appFolder);

                if (Directory.Exists(ffmpegFolder))
                {
                    Directory.Delete(ffmpegFolder, true);
                }
                Directory.CreateDirectory(ffmpegFolder);

                string downloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-full.7z";

                // ★ 1. Windows 기본 내장 curl을 사용하여 다운로드
                // -L: 리다이렉트 허용, -A: User-Agent 설정(차단 방지), -o: 저장 경로 지정
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "curl",
                    Arguments = $"-L -A \"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36\" -o \"{archivePath}\" \"{downloadUrl}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true // 검은 창 숨김
                };

                // 비동기로 curl 다운로드가 끝날 때까지 대기 (.NET 8 지원)
                using (Process process = Process.Start(psi)!)
                {
                    await process.WaitForExitAsync();
                }

                // 다운로드 성공 여부 확인
                if (!File.Exists(archivePath))
                {
                    MessageBox.Show("curl을 통한 다운로드에 실패했습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                BtnDownloadFfmpeg.Content = "압축 해제 중...";

                // ★ 2. SharpCompress를 사용하여 .7z 압축 해제
                await Task.Run(() =>
                {
                    // 파일을 스트림으로 엽니다.
                    using (Stream stream = File.OpenRead(archivePath))
                    // 7z 전용 아카이브 클래스를 직접 호출합니다. (네임스페이스 전체 경로 사용)
                    using (var archive = SevenZipArchive.OpenArchive(stream))
                    {
                        // 압축 파일 안의 파일들을 하나씩 꺼내어 하드에 씁니다.
                        foreach (var entry in archive.Entries)
                        {
                            if (!entry.IsDirectory)
                            {
                                // 원본 폴더 경로(ExtractFullPath)를 유지하며 덮어쓰기(Overwrite)로 압축 해제
                                entry.WriteToDirectory(ffmpegFolder, new ExtractionOptions()
                                {
                                    ExtractFullPath = true,
                                    Overwrite = true
                                });
                            }
                        }
                    }
                });

                // 용량 확보를 위해 원본 7z 파일 삭제
                File.Delete(archivePath);

                // 하위 폴더들을 뒤져서 ffmpeg.exe 찾기
                string[] exeFiles = Directory.GetFiles(ffmpegFolder, "ffmpeg.exe", SearchOption.AllDirectories);

                if (exeFiles.Length > 0)
                {
                    TxtFfmpegPath.Text = exeFiles[0];
                    SaveSettings(); // 찾은 경로 즉시 저장
                    MessageBox.Show("Gyan 버전 FFmpeg의 다운로드 및 7z 압축 해제가 완료되었습니다!", "설치 완료", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("압축 해제는 완료되었으나 ffmpeg.exe를 찾을 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"FFmpeg 설치 중 오류가 발생했습니다.\n{ex.Message}", "다운로드 실패", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // 모든 작업 완료 후 버튼 원상 복구
                BtnDownloadFfmpeg.IsEnabled = true;
                BtnDownloadFfmpeg.Content = "자동 다운로드";
            }
        }


        /// <summary>
        /// ffmpeg Setting
        /// </summary>
        /// <returns></returns>
        /// 


        private string GetFfmpegVideoCodec()
        {
            if (RdoCodecCopy.IsChecked == true) return "copy";

            string codec = RdoCodecH264.IsChecked == true ? "H264" : (RdoCodecH265.IsChecked == true ? "H265" : "AV1");
            string encoder = RdoEncCPU.IsChecked == true ? "CPU" : (RdoEncNVENC.IsChecked == true ? "NVENC" : (RdoEncQSV.IsChecked == true ? "QSV" : "AMF"));

            if (codec == "H264")
            {
                if (encoder == "NVENC") return "h264_nvenc";
                if (encoder == "QSV") return "h264_qsv";
                if (encoder == "AMF") return "h264_amf";
                return "libx264"; // CPU
            }
            else if (codec == "H265")
            {
                if (encoder == "NVENC") return "hevc_nvenc";
                if (encoder == "QSV") return "hevc_qsv";
                if (encoder == "AMF") return "hevc_amf";
                return "libx265"; // CPU
            }
            else // AV1
            {
                if (encoder == "NVENC") return "av1_nvenc";
                if (encoder == "QSV") return "av1_qsv";
                if (encoder == "AMF") return "av1_amf";
                return "libaom-av1"; // AV1 CPU는 매우 느리지만 예비용
            }
        }
        
        private string GetSafeFileName(string name)
        {
            string invalidChars = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            Regex regex = new Regex($"[{Regex.Escape(invalidChars)}]");
            return regex.Replace(name, "_"); // 못 쓰는 글자는 _로 치환
        }

        private async void BtnAnalyzeVod_Click(object sender, RoutedEventArgs e)
        {
            string vodLink = TxtVodUrl.Text.Trim();

            if (string.IsNullOrEmpty(vodLink))
            {
                MessageBox.Show("다시보기 링크를 입력해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnAnalyzeVod.IsEnabled = false;
            BtnAnalyzeVod.Content = "분석 중...";

            // ★ API가 이제 6개의 값(날짜 date 포함)을 반환합니다.
            var (isSuccess, streamerName, title, date, mp4Url, resolution) = await ChzzkApi.GetVodStreamAsync(vodLink);

            BtnAnalyzeVod.IsEnabled = true;
            BtnAnalyzeVod.Content = "분석하기";

            if (isSuccess)
            {
                DummyVod newVod = new DummyVod
                {
                    // ★ 날짜를 맨 앞에 괄호와 함께 예쁘게 배치합니다!
                    FileName = $"[{date}] {streamerName} - {title}",

                    Resolution = resolution,
                    DownloadUrl = mp4Url,
                    ProgressPercent = 0,
                    ProgressText = "대기 중",
                    Speed = "-",
                    Status = "대기"
                };

                VodList.Add(newVod);
                TxtVodUrl.Text = string.Empty;
            }
            else
            {
                MessageBox.Show("VOD 정보를 불러올 수 없습니다.\n링크가 올바른지 확인해주세요.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ====================================================================
        // [1] VOD 다운로드 시작 버튼
        // ====================================================================
        private async void BtnStartVodDownload_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = VodDataGrid.SelectedItems.Cast<DummyVod>().ToList();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("다운로드할 항목을 선택해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (var vod in selectedItems)
            {
                // 이미 다운로드 중이거나 완료된 항목은 무시
                if (vod.IsDownloading || vod.Status == "완료") continue;

                // VOD 파일이 저장될 전체 경로 설정 (없으면 세팅)
                if (string.IsNullOrEmpty(vod.SaveFilePath))
                {
                    string safeFileName = GetSafeFileName(vod.FileName);
                    vod.SaveFilePath = Path.Combine(TxtDefaultSaveFolder.Text, $"{safeFileName}_{vod.Resolution}.mp4");
                }

                if (!Directory.Exists(TxtDefaultSaveFolder.Text))
                    Directory.CreateDirectory(TxtDefaultSaveFolder.Text);

                vod.IsDownloading = true;
                vod.Status = "다운로드 중";
                vod.Cts = new CancellationTokenSource();

                // 비동기로 다운로드 작업 던지기 (UI 멈춤 방지)
                _ = DownloadVodAsync(vod);
            }
        }

        // ====================================================================
        // [2] VOD 일시 정지 버튼
        // ====================================================================
        private void BtnPauseVodDownload_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = VodDataGrid.SelectedItems.Cast<DummyVod>().ToList();
            foreach (var vod in selectedItems)
            {
                if (vod.IsDownloading && vod.Cts != null)
                {
                    // 토큰을 캔슬하여 스트림 읽기를 즉시 중단시킵니다.
                    vod.Cts.Cancel();
                    vod.IsDownloading = false;
                    vod.Status = "일시 정지됨";
                    vod.Speed = "-";
                }
            }
        }

        // ====================================================================
        // [3] VOD 취소 및 목록 삭제 버튼
        // ====================================================================
        private void BtnDeleteVod_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = VodDataGrid.SelectedItems.Cast<DummyVod>().ToList();
            if (selectedItems.Count == 0) return;

            if (MessageBox.Show($"선택한 {selectedItems.Count}개의 항목을 목록에서 지우시겠습니까?\n(다운로드 중이던 파일도 함께 삭제됩니다)", "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                foreach (var vod in selectedItems)
                {
                    // 다운로드 중이면 먼저 중지
                    if (vod.IsDownloading && vod.Cts != null)
                    {
                        vod.Cts.Cancel();
                    }

                    // 만들어진 찌꺼기 MP4 파일이 있으면 삭제
                    if (File.Exists(vod.SaveFilePath))
                    {
                        try { File.Delete(vod.SaveFilePath); } catch { }
                    }

                    // 목록에서 제거
                    VodList.Remove(vod);
                }
            }
        }

        // ====================================================================
        // ★ 핵심: VOD 이어받기 및 실시간 속도 계산 로직
        // ====================================================================
        private async Task DownloadVodAsync(DummyVod vod)
        {
            if (vod.DownloadUrl.Contains(".m3u8"))
            {
                await DownloadVodWithFfmpegAsync(vod);
                return;
            }
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                    // 1. 이어받기를 위해 기존에 받아둔 파일 크기 확인
                    long existingLength = 0;
                    if (File.Exists(vod.SaveFilePath))
                    {
                        existingLength = new FileInfo(vod.SaveFilePath).Length;
                    }

                    // 2. HTTP 요청 생성 (이어받기 Range 헤더 추가)
                    var request = new HttpRequestMessage(HttpMethod.Get, vod.DownloadUrl);
                    if (existingLength > 0)
                    {
                        request.Headers.Range = new RangeHeaderValue(existingLength, null);
                    }

                    using (HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, vod.Cts!.Token))
                    {
                        // Range 요청을 했는데 서버가 416(Range Not Satisfiable)을 주면 이미 다 받은 것임
                        if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                vod.ProgressPercent = 100;
                                vod.Status = "완료";
                                vod.Speed = "-";
                                vod.IsDownloading = false;
                            });
                            return;
                        }

                        response.EnsureSuccessStatusCode();

                        // 3. 파일 전체 용량 계산 (기존 받아둔 용량 + 이번에 받을 용량)
                        long totalLength = response.Content.Headers.ContentLength ?? 0;
                        long totalBytes = existingLength + totalLength;
                        vod.TotalBytes = totalBytes;

                        // 4. 스트림 읽기 및 파일 쓰기 (FileMode.Append로 이어서 쓰기)
                        using (Stream contentStream = await response.Content.ReadAsStreamAsync(vod.Cts.Token))
                        using (FileStream fileStream = new FileStream(vod.SaveFilePath, FileMode.Append, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            byte[] buffer = new byte[81920]; // 80KB 버퍼
                            int bytesRead;
                            long bytesReadThisSession = 0;

                            Stopwatch stopwatch = Stopwatch.StartNew();
                            long lastUpdateBytes = 0;

                            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, vod.Cts.Token)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead, vod.Cts.Token);
                                bytesReadThisSession += bytesRead;
                                long currentTotalBytes = existingLength + bytesReadThisSession;

                                // 0.5초(500ms)마다 UI 갱신 및 속도 계산
                                if (stopwatch.ElapsedMilliseconds > 500)
                                {
                                    double speedInMBps = (bytesReadThisSession - lastUpdateBytes) / 1024.0 / 1024.0 / (stopwatch.ElapsedMilliseconds / 1000.0);

                                    Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        vod.ProgressPercent = (int)((double)currentTotalBytes / totalBytes * 100);
                                        vod.ProgressText = $"{currentTotalBytes / 1048576.0:F1}MB / {totalBytes / 1048576.0:F1}MB";
                                        vod.Speed = $"{speedInMBps:F1} MB/s";
                                    });

                                    stopwatch.Restart();
                                    lastUpdateBytes = bytesReadThisSession;
                                }
                            }
                        }
                    }

                    // 루프를 정상적으로 빠져나왔다면 다운로드 완료
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        vod.ProgressPercent = 100;
                        vod.ProgressText = $"{vod.TotalBytes / 1048576.0:F1}MB / {vod.TotalBytes / 1048576.0:F1}MB";
                        vod.Status = "완료";
                        vod.Speed = "-";
                        vod.IsDownloading = false;
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // 사용자가 '일시 정지'나 '삭제'를 눌러 토큰이 취소된 경우
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    vod.Status = "오류 발생";
                    vod.Speed = "-";
                    vod.IsDownloading = false;
                });
                System.Diagnostics.Debug.WriteLine($"다운로드 오류: {ex.Message}");
            }
        }
        private async Task DownloadVodWithFfmpegAsync(DummyVod vod)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    vod.ProgressText = "FFmpeg 병합 중...";
                    vod.Speed = "알 수 없음"; // FFmpeg는 용량 계산이 어려워 텍스트로 대체
                });

                string ffmpegPath = "";
                Application.Current.Dispatcher.Invoke(() => ffmpegPath = TxtFfmpegPath.Text);

                string userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                string cookieHeader = string.IsNullOrEmpty(AuthManager.GetCookieString()) ? "" : $"Cookie: {AuthManager.GetCookieString()}\r\n";
                string headers = $"User-Agent: {userAgent}\r\nOrigin: https://chzzk.naver.com\r\nReferer: https://chzzk.naver.com/\r\n{cookieHeader}";

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = false
                };

                // 명령어 셋팅 (인코딩 없이 원본 화질/음질 그대로 하나의 파일로 복사 합치기)
                psi.ArgumentList.Add("-headers"); psi.ArgumentList.Add(headers);
                psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(vod.DownloadUrl);
                psi.ArgumentList.Add("-c"); psi.ArgumentList.Add("copy");
                psi.ArgumentList.Add("-y"); // 덮어쓰기 허용
                psi.ArgumentList.Add(vod.SaveFilePath);

                using (Process process = new Process { StartInfo = psi })
                {
                    using (vod.Cts!.Token.Register(() =>
                    {
                        try { process.StandardInput.WriteLine("q"); } catch { }
                    }))
                    {
                        process.Start();

                        // ★ 앱 종료 시 확실하게 끌 수 있도록 추적 리스트에 등록!
                        _activeProcesses[vod.FileName] = process;

                        await process.WaitForExitAsync();

                        // 끝나면 리스트에서 제거
                        _activeProcesses.Remove(vod.FileName);
                    }
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (vod.Cts.IsCancellationRequested)
                    {
                        vod.Status = "일시 정지됨";
                        vod.IsDownloading = false;
                    }
                    else
                    {
                        // 성공적으로 끝났을 때
                        vod.ProgressPercent = 100;
                        vod.ProgressText = "다운로드 완료";
                        vod.Status = "완료";
                        vod.Speed = "-";
                        vod.IsDownloading = false;
                    }
                });
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    vod.Status = "오류 발생";
                    vod.IsDownloading = false;
                });
                System.Diagnostics.Debug.WriteLine($"FFmpeg VOD 다운로드 오류: {ex.Message}");
            }
        }
        private void LogTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                textBox.ScrollToEnd();
            }
        }

        // ====================================================================
        // 프로그램 종료 시 잔여 FFmpeg 및 토큰을 완벽하게 청소하는 로직
        // ====================================================================
        private void CleanupBeforeExit()
        {
            // 1. 대기 중인 무한 감시망 토큰 전부 취소
            foreach (var cts in _monitorTokens.Values)
            {
                try { cts.Cancel(); } catch { }
            }

            // 2. 다운로드 중인 VOD 토큰 전부 취소 (HttpClient 및 찌꺼기 파일 정리)
            foreach (var vod in VodList)
            {
                if (vod.IsDownloading && vod.Cts != null)
                {
                    try { vod.Cts.Cancel(); } catch { }
                }
            }

            // ★ 3. 뒤에서 몰래 돌아가는 FFmpeg 프로세스 안전 종료 및 확인 사살
            foreach (var process in _activeProcesses.Values.ToList())
            {
                if (!process.HasExited)
                {
                    try
                    {
                        // q를 보내서 영상 헤더가 깨지지 않게 정상 저장 유도
                        process.StandardInput.WriteLine("q");

                        // 프로그램이 꺼지는 중이므로 딱 1.5초만 기다려줌
                        if (!process.WaitForExit(1500))
                        {
                            // 그래도 안 꺼지고 버티면 강제 처형
                            process.Kill();
                        }
                    }
                    catch { }
                }
            }

            _activeProcesses.Clear();
        }

        // ====================================================================
        // 시스템 트레이 (최소화 / 복구 / 종료) 관련 로직
        // ====================================================================

        // 1. 최소화 버튼(-)을 눌렀을 때의 동작
        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            // 창이 최소화 상태로 변했고, '트레이로 숨기기' 옵션이 켜져있다면
            if (this.WindowState == WindowState.Minimized && ChkMinimizeToTray.IsChecked == true)
            {
                this.Hide(); // 작업 표시줄에서 창을 아예 숨깁니다. (백그라운드 실행)
            }
        }

        // 2. 트레이 아이콘을 더블클릭하거나 우클릭->[다시 열기]를 눌렀을 때
        private void MenuOpen_Click(object sender, RoutedEventArgs e)
        {
            this.Show(); // 숨겼던 창을 다시 표시
            this.WindowState = WindowState.Normal; // 창 크기를 원래대로 복구
            this.Activate(); // 창을 최상단으로 포커스
        }

        private void MyNotifyIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            MenuOpen_Click(sender, e);
        }

        // 3. 우클릭->[종료]를 눌렀을 때 (완전 종료)
        private async void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            // ★ [핵심] 우클릭 메뉴가 완전히 닫힐 때까지 0.2초(200ms)만 아주 잠깐 기다려 줍니다.
            // 이 한 줄이 들어가면 MessageBox가 메인 윈도우에 정상적으로 달라붙어 사라지지 않습니다!
            await Task.Delay(200);

            // 백그라운드 녹화 중이라면 강제 종료될 수 있으므로 한 번 물어보는 것이 좋습니다.
            if (_activeProcesses.Count > 0)
            {
                var result = MessageBox.Show("현재 진행 중인 녹화 또는 감시가 있습니다.\n정말 완전히 종료하시겠습니까?", "종료 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.No) return;
            }
            CleanupBeforeExit();
            Application.Current.Shutdown();
        }
        // ====================================================================
        // 부팅 시 자동 실행 (레지스트리 등록/해제) 로직
        // ====================================================================
        private void ChkRunAtStartup_Click(object sender, RoutedEventArgs e)
        {
            bool isEnabled = ChkRunAtStartup.IsChecked ?? false;
            SetStartupRegistry(isEnabled);
            SaveSettings(); // 체크하는 즉시 저장
        }

        private void SetStartupRegistry(bool enable)
        {
            try
            {
                // 윈도우 시작 프로그램 레지스트리 경로
                string runKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(runKey, true)!)
                {
                    if (enable)
                    {
                        // 현재 내 프로그램의 실행 파일(.exe) 경로를 가져옴
                        string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";

                        // ★ 핵심: 경로 뒤에 "-startup" 이라는 비밀 암호를 붙여서 윈도우에게 실행을 부탁함
                        key.SetValue("ChzzkRecorder", $"\"{exePath}\" -startup");
                    }
                    else
                    {
                        // 체크 해제 시 레지스트리에서 삭제
                        key.DeleteValue("ChzzkRecorder", false);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"시작 프로그램 등록 중 오류가 발생했습니다.\n{ex.Message}", "권한 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                ChkRunAtStartup.IsChecked = !enable; // 실패 시 체크박스 원상복구
            }
        }



    }



    // ==============================================================================
    // 디자인 확인을 위한 더미 데이터 클래스들입니다.
    // 프로젝트 생성 시 네임스페이스 오류 방지를 위해 여기에 임시로 정의합니다.
    // 실제 개발 시에는 별도의 파일로 모델을 분리하고 ViewModel을 구성하여 사용하세요.
    // ==============================================================================

    public class DummyStreamer : INotifyPropertyChanged
    {
        private string _state = string.Empty;
        private string _title = string.Empty;
        private string _startTime = string.Empty;
        private string _channelId = string.Empty;
        private string _name = string.Empty;
        private string _savePath = string.Empty;
        private string _logs = "";


        // ★ 수정된 부분: 이름, ID, 경로가 바뀌어도 화면이 즉시 갱신되도록 OnPropertyChanged 적용
        public string ChannelId { get => _channelId; set { _channelId = value; OnPropertyChanged(); } }
        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
        public string SavePath { get => _savePath; set { _savePath = value; OnPropertyChanged(); } }

        public string State { get => _state; set { _state = value; OnPropertyChanged(); } }
        public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }
        public string StartTime { get => _startTime; set { _startTime = value; OnPropertyChanged(); } }
        public string Logs
        {
            get => _logs;
            set { _logs = value; OnPropertyChanged(); }
        }

        public void AddLog(string message)
        {
            // 1. 넘어온 메시지가 null이면 빈 문자열로 처리
            message ??= "";

            // 2. 프로그램이 종료되는 시점이라 UI 스레드(Dispatcher)가 파괴되었다면 안전하게 무시
            if (Application.Current == null || Application.Current.Dispatcher == null) return;

            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // 3. JSON 불러오기 등으로 인해 기존 로그(_logs)가 null로 꼬여있다면 빈 문자열로 자동 복구
                if (_logs == null) _logs = string.Empty;

                // 메모리 폭주 방지: 로그가 너무 길어지면 앞부분 자르기 (약 3만 자 유지)
                if (_logs.Length > 30000)
                {
                    _logs = _logs.Substring(_logs.Length - 10000);
                }

                string time = DateTime.Now.ToString("HH:mm:ss");

                // Logs += 를 쓰지 않고 풀어서 대입하여 UI 갱신 이벤트(OnPropertyChanged)가 정상적으로 울리게 함
                Logs = _logs + $"[{time}] {message}\n";
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null!)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }

    public class DummyVod : INotifyPropertyChanged
    {
        private int _progressPercent;
        private string _progressText = string.Empty;
        private string _speed = string.Empty;
        private string _status = string.Empty;

        public CancellationTokenSource? Cts { get; set; }
        public bool IsDownloading { get; set; } = false;
        public string SaveFilePath { get; set; } = string.Empty; // 실제 저장될 파일 경로
        public long TotalBytes { get; set; } = 0; // 전체 용량 저장용

        public string FileName { get; set; } = string.Empty;
        public string Resolution { get; set; } = "원본(최고화질)";
        public string DownloadUrl { get; set; } = string.Empty; // 실제 MP4 링크 저장용

        public int ProgressPercent
        {
            get => _progressPercent;
            set { _progressPercent = value; OnPropertyChanged(); }
        }

        public string ProgressText
        {
            get => _progressText;
            set { _progressText = value; OnPropertyChanged(); }
        }

        public string Speed
        {
            get => _speed;
            set { _speed = value; OnPropertyChanged(); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null!)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }

    public class AppSettings
    {
        // 스트리머 목록 리스트
        public List<DummyStreamer> SavedStreamers { get; set; } = new List<DummyStreamer>();

        // 설정 탭 값들
        public string DefaultSaveFolder { get; set; } = string.Empty;
        public int FileNameFormatIndex { get; set; } = 0;
        public string FfmpegPath { get; set; } = string.Empty;
        public string Resolution { get; set; } = "Auto";
        public string VideoCodec { get; set; } = "H264";
        public string EncoderType { get; set; } = "CPU";
        public int Bitrate { get; set; } = 8000;

        public int CheckInterval { get; set; } = 5; // ★ 추가됨

        public bool MinimizeToTray { get; set; } = false;
        public bool RunAtStartup { get; set; } = false;
        public bool AutoStartRecording { get; set; } = false;
    }


}