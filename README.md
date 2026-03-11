# 🎥 CHZ Recorder - 치지직 자동 녹화 및 VOD 다운로더

![Platform](https://img.shields.io/badge/Platform-Windows-blue) ![Language](https://img.shields.io/badge/Language-C%23-purple) ![Framework](https://img.shields.io/badge/Framework-.NET%2010%20WPF-blueviolet) ![AI](https://img.shields.io/badge/AI--Assisted-Gemini%203.1%20Pro%20Preview-orange)

**CHZ Recorder**는 네이버의 스트리밍 플랫폼 **'치지직(CHZZK)'**의 생방송을 실시간으로 감시하여 자동으로 녹화하고, 다시보기(VOD) 영상을 고화질로 다운로드할 수 있는 윈도우 전용 데스크톱 애플리케이션입니다.

---

## ✨ 주요 기능 (Key Features)

### 🔴 실시간 생방송 자동 녹화
- **24/7 무한 감시**: 스트리머가 방송을 켤 때까지 주기적으로 상태를 체크하고, 방송이 시작되면 즉시 녹화를 시작합니다.
- **멀티프로세스 녹화**: 여러 명의 스트리머를 동시에 독립된 FFmpeg 프로세스로 안전하게 녹화합니다.
- **하드웨어 가속 지원**: GPU(Nvidia NVENC, Intel QSV, AMD AMF)를 활용하여 CPU 점유율을 최소화합니다.
- **실시간 로그 뷰어**: 각 스트리머별 FFmpeg 진행 상황을 프로그램 내에서 실시간으로 확인할 수 있습니다.

### 📥 다시보기(VOD) 다운로드
- **고속 다운로드**: 통짜 MP4 링크 추출을 통해 안정적이고 빠른 다운로드를 지원합니다.
- **이어받기(Resume)**: 다운로드 중 중단되거나 프로그램이 종료되어도 이어서 받을 수 있습니다.
- **최신 VOD 대응**: 방송 종료 직후 아직 인코딩 중인 임시 VOD(HLS)까지 즉시 다운로드 가능합니다.

---

## 🛠 기술 스택 (Tech Stack)

이 프로젝트는 최신 AI 기술과 개발 환경을 활용하여 제작되었습니다.

- **AI Assisted Development**: **Gemini 3.1 Pro Preview**를 적극적으로 활용하여 로직 설계 및 코드 최적화를 진행하였습니다.
- **IDE**: **Visual Studio 2026 Community**
- **Language**: **C#** (.NET 10 기반)
- **UI Framework**: **WPF** (Windows Presentation Foundation)
- **Core Engine**: **FFmpeg** (Gyan.dev Build)

---

## 🚀 시작하기 (Getting Started)

1. **로그인**: [설정] 탭에서 네이버 로그인을 진행합니다. (성인/구독 방송 녹화 필수)
2. **FFmpeg 설치**: 설정 탭에서 **[자동 다운로드]** 버튼을 눌러 인코딩 엔진을 설치합니다.
3. **스트리머 추가**: [생방송 녹화] 탭에서 녹화하고 싶은 스트리머의 ID를 입력하고 추가합니다.
4. **감시 시작**: 스트리머를 선택하고 **[선택 녹화 시작]**을 누르면 무한 감시 모드가 작동합니다.

---

## ⚖️ 라이선스 (License)

프로그램에 사용된 **FFmpeg**는 **GPL/LGPL** 라이선스를 따르며, 본 프로그램은 FFmpeg의 바이너리를 직접 포함하지 않고 외부 프로세스로 호출하는 방식으로 라이선스 규정을 준수합니다.

---
*Developed with ❤️ and Gemini AI.*
