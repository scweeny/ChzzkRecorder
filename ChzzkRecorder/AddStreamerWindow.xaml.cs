using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace ChzzkRecorder
{
    public partial class AddStreamerWindow : Window
    {
        // 메인 창에서 읽어갈 수 있도록 public 속성으로 선언
        public string InputChannelId { get; private set; }
        public string InputStreamerName { get; private set; }
        public string InputSaveFolder { get; private set; }

        public AddStreamerWindow(string defaultSaveFolder)
        {
            InitializeComponent();

            // 메인 창에서 넘어온 기본 저장 폴더 경로가 비어있지 않다면 텍스트박스에 세팅
            if (!string.IsNullOrWhiteSpace(defaultSaveFolder))
            {
                TxtSaveFolder.Text = defaultSaveFolder;
            }
            else
            {
                // 만약 설정된 경로가 없다면 윈도우 기본 '내 비디오' 폴더로 지정
                TxtSaveFolder.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            }
        }

        public AddStreamerWindow(DummyStreamer existingStreamer)
        {
            InitializeComponent();

            // 창 제목과 버튼 글씨를 '수정'에 맞게 변경
            this.Title = "스트리머 정보 수정";
            BtnAdd.Content = "수정하기";

            // 기존 데이터 채워넣기
            TxtChannelId.Text = existingStreamer.ChannelId;
            TxtStreamerName.Text = existingStreamer.Name;
            TxtSaveFolder.Text = existingStreamer.SavePath;
        }


        // '추가하기' 버튼 클릭 시 이벤트
        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            // 입력값 검증 (비어있는지 확인)
            if (string.IsNullOrWhiteSpace(TxtChannelId.Text) ||
                string.IsNullOrWhiteSpace(TxtStreamerName.Text) ||
                string.IsNullOrWhiteSpace(TxtSaveFolder.Text))
            {
                MessageBox.Show("모든 항목을 입력해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. 입력된 값을 public 속성에 저장
            InputChannelId = TxtChannelId.Text;
            InputStreamerName = TxtStreamerName.Text;
            InputSaveFolder = TxtSaveFolder.Text;

            // 3. 창 닫기 (성공)
            this.DialogResult = true;
            this.Close();
        }

        // '취소' 버튼 클릭 시 이벤트
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            // .NET 10 WPF의 네이티브 폴더 선택 다이얼로그 생성
            OpenFolderDialog dialog = new OpenFolderDialog
            {
                Title = "스트리머 영상을 저장할 폴더를 선택하세요",

                // 처음 열릴 때 보여줄 기본 경로 (선택 사항)
                // 현재 텍스트박스에 있는 경로가 유효하면 그곳을 열고, 아니면 '내 비디오' 폴더를 엽니다.
                InitialDirectory = System.IO.Directory.Exists(TxtSaveFolder.Text)
                                    ? TxtSaveFolder.Text
                                    : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
            };

            // 다이얼로그 띄우기 (사용자가 '폴더 선택'을 누르면 true 반환)
            if (dialog.ShowDialog() == true)
            {
                // 선택한 폴더 경로를 텍스트박스에 적용 (FolderName 속성 사용)
                TxtSaveFolder.Text = dialog.FolderName;
            }
        }

        private async void BtnCheckStreamer_Click(object sender, RoutedEventArgs e)
        {
            string channelId = TxtChannelId.Text.Trim();

            if (string.IsNullOrWhiteSpace(channelId))
            {
                MessageBox.Show("방송 ID를 먼저 입력해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 검색 중에는 버튼을 비활성화하고 텍스트를 변경
            BtnCheckStreamer.IsEnabled = false;
            BtnCheckStreamer.Content = "검색 중...";

            // 위에서 만든 치지직 API 호출 (비동기)
            string? streamerName = await ChzzkApi.GetStreamerNameAsync(channelId);

            if (!string.IsNullOrEmpty(streamerName) && streamerName != "(알 수 없음)")
            {
                TxtStreamerName.Text = streamerName;
            }
            else if (streamerName == "(알 수 없음)")
            {
                MessageBox.Show("존재하지 않거나 삭제된 채널입니다.\n올바른 ID인지 다시 확인해주세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show("채널 정보를 가져오는 데 실패했습니다.\n네트워크 상태나 ID를 확인해주세요.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // 버튼 원상 복구
            BtnCheckStreamer.Content = "이름 검색";
            BtnCheckStreamer.IsEnabled = true;
        }
    }
}