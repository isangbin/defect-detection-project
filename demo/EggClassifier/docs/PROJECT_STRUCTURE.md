# EggClassifier 프로젝트 구조 가이드

## 개요

EggClassifier는 **Feature 기반 모듈 구조**로 설계되어 있습니다.
각 팀원이 담당 Feature 폴더만 수정하면 되므로 Git 충돌 없이 병렬 개발이 가능합니다.

---

## 폴더 구조

```
EggClassifier/
│
├── App.xaml / App.xaml.cs          ← 앱 진입점, DI 컨테이너, DataTemplate 매핑
├── MainWindow.xaml / .xaml.cs      ← 셸 (사이드바 + ContentControl)
├── EggClassifier.csproj            ← NuGet 패키지 관리
│
├── Core/                           ← 공유 인프라 (수정 거의 없음)
│   ├── ViewModelBase.cs              페이지 ViewModel의 부모 클래스
│   ├── INavigationService.cs         네비게이션 인터페이스
│   └── NavigationService.cs          네비게이션 구현체
│
├── Models/                         ← 데이터 모델 + AI 추론 엔진
│   ├── Detection.cs                  탐지 결과 (ClassId, ClassName, Confidence, BoundingBox)
│   ├── ClassCountItem.cs             클래스별 카운트 표시용
│   ├── DetectionItem.cs              현재 탐지 표시용
│   ├── YoloDetector.cs               YOLOv8 ONNX 추론 엔진 (계란 분류)
│   ├── FaceEmbedder.cs               MobileFaceNet ONNX 얼굴 임베딩 엔진
│   └── UserData.cs                   사용자 데이터 DTO (UserData + UserStore)
│
├── Services/                       ← 비즈니스 로직 서비스
│   ├── IWebcamService.cs             웹캠 인터페이스
│   ├── WebcamService.cs              웹캠 구현체
│   ├── IDetectorService.cs           탐지 인터페이스
│   ├── DetectorService.cs            탐지 구현체 (YoloDetector 래핑)
│   ├── IFaceService.cs               얼굴 탐지/임베딩 서비스 인터페이스
│   ├── FaceService.cs                Haar Cascade + FaceEmbedder 래핑
│   ├── IUserService.cs               사용자 CRUD 서비스 인터페이스
│   └── UserService.cs                JSON 파일 기반 사용자 저장소 (SHA256+Salt)
│
├── Features/                       ← ★ 팀원별 작업 영역 ★
│   ├── Detection/                    팀원A: 계란 분류
│   │   ├── DetectionView.xaml
│   │   ├── DetectionView.xaml.cs
│   │   └── DetectionViewModel.cs
│   ├── Login/                        팀원B: 로그인 + 회원가입
│   │   ├── LoginView.xaml              2단계 로그인 UI (자격증명 → 얼굴인증)
│   │   ├── LoginView.xaml.cs           PasswordBox 바인딩 헬퍼
│   │   ├── LoginViewModel.cs           2단계 로그인 로직 (비밀번호 + 얼굴 2FA)
│   │   ├── SignUpView.xaml             회원가입 UI (폼 + 웹캠 얼굴 촬영)
│   │   ├── SignUpView.xaml.cs          PasswordBox 바인딩 헬퍼
│   │   └── SignUpViewModel.cs          회원가입 로직 (얼굴 이미지 저장)
│   └── Dashboard/                    팀원C: DB 시각화
│       ├── DashboardView.xaml
│       ├── DashboardView.xaml.cs
│       └── DashboardViewModel.cs
│
├── ViewModels/
│   └── MainViewModel.cs            ← 네비게이션 + 로그인 상태 관리
│
└── docs/                           ← 문서
```

---

## 담당 영역 규칙

| 팀원 | 담당 폴더 | 역할 |
|------|-----------|------|
| A | `Features/Detection/` | 계란 분류 (웹캠 + YOLO 감지) |
| B | `Features/Login/` | 로그인 / 회원가입 |
| C | `Features/Dashboard/` | DB 시각화 / 통계 대시보드 |

> **핵심 규칙**: 자기 Feature 폴더 안의 파일만 수정하면 충돌이 발생하지 않습니다.

---

## 네비게이션 동작 원리

```
앱 시작 → MainWindow.Loaded → NavigateToLoginCommand 실행 → 로그인 페이지 표시
  (사이드바는 IsLoggedIn = false이므로 숨김 상태)

[로그인 성공 후]
  → MainViewModel.OnLoginSuccess() → IsLoggedIn = true → 사이드바 표시
  → NavigateToDetection() → 계란 분류 페이지로 이동

사이드바 RadioButton 클릭
  → MainViewModel.NavigateToXxxCommand 실행
  → if (!IsLoggedIn) return; (로그인 상태 확인)
  → NavigationService.NavigateTo<XxxViewModel>() 호출
    → 이전 VM.OnNavigatedFrom()  (예: 웹캠 정지)
    → DI 컨테이너에서 새 VM 생성
    → 새 VM.OnNavigatedTo()      (예: 이벤트 구독)
    → CurrentView 프로퍼티 변경
  → ContentControl이 변경 감지
  → App.xaml의 DataTemplate에 따라 해당 View(UserControl) 렌더링

[로그아웃]
  → MainViewModel.LogoutCommand → IsLoggedIn = false → 사이드바 숨김
  → NavigateToLogin() → 로그인 페이지로 이동
```

---

## 새 Feature 추가하는 법

예시: `Settings` 페이지를 추가하는 경우

### 1. Feature 폴더 생성

```
Features/Settings/
├── SettingsView.xaml
├── SettingsView.xaml.cs
└── SettingsViewModel.cs
```

### 2. ViewModel 작성

```csharp
// Features/Settings/SettingsViewModel.cs
using EggClassifier.Core;

namespace EggClassifier.Features.Settings
{
    public partial class SettingsViewModel : ViewModelBase
    {
        // ViewModelBase를 상속하면 OnNavigatedTo/OnNavigatedFrom 사용 가능
        public override void OnNavigatedTo()
        {
            // 페이지 진입 시 실행할 코드
        }
    }
}
```

### 3. View 작성

```xml
<!-- Features/Settings/SettingsView.xaml -->
<UserControl x:Class="EggClassifier.Features.Settings.SettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <TextBlock Text="설정 페이지" Foreground="{StaticResource TextBrush}"/>
</UserControl>
```

### 4. DI 등록 (`App.xaml.cs`에 1줄 추가)

```csharp
services.AddTransient<SettingsViewModel>();
```

### 5. DataTemplate 등록 (`App.xaml`에 2줄 추가)

```xml
xmlns:settings="clr-namespace:EggClassifier.Features.Settings"

<DataTemplate DataType="{x:Type settings:SettingsViewModel}">
    <settings:SettingsView/>
</DataTemplate>
```

### 6. 사이드바 버튼 추가 (`MainWindow.xaml`, `MainViewModel.cs`)

MainViewModel에 `NavigateToSettingsCommand` 추가, MainWindow에 RadioButton 추가.

---

## 새 Service 추가하는 법

예시: DB 서비스를 추가하는 경우

### 1. 인터페이스 정의

```csharp
// Services/IDatabaseService.cs
namespace EggClassifier.Services
{
    public interface IDatabaseService
    {
        void SaveResult(Detection detection);
        List<Detection> GetHistory(int count);
    }
}
```

### 2. 구현체 작성

```csharp
// Services/DatabaseService.cs
namespace EggClassifier.Services
{
    public class DatabaseService : IDatabaseService { ... }
}
```

### 3. DI 등록 (`App.xaml.cs`에 1줄 추가)

```csharp
services.AddSingleton<IDatabaseService, DatabaseService>();
```

### 4. Feature에서 사용 (생성자 주입)

```csharp
public class DashboardViewModel : ViewModelBase
{
    private readonly IDatabaseService _db;

    public DashboardViewModel(IDatabaseService db)
    {
        _db = db;  // DI가 자동으로 주입해줌
    }
}
```

> 신규 파일만 추가하므로 다른 팀원과 충돌할 일이 없습니다.

---

## DI (의존성 주입) 간단 설명

`App.xaml.cs`에서 모든 서비스와 ViewModel을 등록합니다.

```csharp
// Singleton: 앱 전체에서 1개만 생성 (공유 상태)
services.AddSingleton<IWebcamService, WebcamService>();

// Transient: 요청할 때마다 새로 생성 (페이지 전환 시 매번 새 인스턴스)
services.AddTransient<DetectionViewModel>();
```

ViewModel 생성자에 인터페이스를 넣으면 DI가 자동으로 구현체를 주입합니다:

```csharp
// 직접 new 하지 않아도 됨 — DI가 알아서 넣어줌
public DetectionViewModel(IWebcamService webcam, IDetectorService detector)
```

---

## ViewModelBase 생명주기

모든 Feature ViewModel은 `ViewModelBase`를 상속합니다.

| 메서드 | 호출 시점 | 용도 |
|--------|-----------|------|
| `OnNavigatedTo()` | 페이지 진입 시 | 이벤트 구독, 데이터 로드 |
| `OnNavigatedFrom()` | 페이지 떠날 때 | 이벤트 해제, 리소스 정리 (예: 웹캠 정지) |

```csharp
public override void OnNavigatedTo()
{
    _webcamService.FrameCaptured += OnFrameCaptured;
}

public override void OnNavigatedFrom()
{
    _webcamService.Stop();
    _webcamService.FrameCaptured -= OnFrameCaptured;
}
```

---

## 빌드 & 실행

```bash
cd demo/EggClassifier
dotnet build
dotnet run
```

## 검증 체크리스트

- [ ] `dotnet build` 오류 없음
- [ ] 앱 실행 → 로그인 페이지 표시 (시작 페이지)
- [ ] 사이드바 숨김 상태 확인 (로그인 전)
- [ ] "회원가입" 클릭 → 회원가입 페이지 이동
- [ ] 아이디/비밀번호 입력 → 비밀번호 확인 일치/불일치 메시지 표시
- [ ] "얼굴 등록" → 웹캠 시작 → "촬영" → 사진 확인/재촬영
- [ ] "가입하기" → 로그인 페이지로 이동
- [ ] 등록한 아이디/비밀번호로 로그인 → 얼굴 인증 단계 진입
- [ ] 얼굴 일치 시 → 사이드바 표시 + 계란 분류 페이지로 이동
- [ ] "계란 분류" 클릭 → 웹캠 + YOLO 감지 동작
- [ ] "대시보드" 클릭 → 대시보드 카드 표시
- [ ] "로그아웃" 클릭 → 사이드바 숨김 + 로그인 페이지로 이동
- [ ] 페이지 전환 시 웹캠 정상 정지/재시작
