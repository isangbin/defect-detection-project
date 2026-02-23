# CLAUDE.md - EggClassifier 프로젝트 컨텍스트

## 프로젝트 개요
YOLOv8 + MobileFaceNet 기반 **계란 품질 실시간 분류 시스템** (WPF 데스크톱 앱).
로그인 시 아이디/비밀번호 + 얼굴인식 2차 인증(2FA)을 지원한다.

## 기술 스택
- **런타임**: .NET 8.0, WPF (net8.0-windows)
- **MVVM**: CommunityToolkit.Mvvm 8.2.2 ([ObservableProperty], [RelayCommand], partial class)
- **DI**: Microsoft.Extensions.DependencyInjection 8.0.1
- **백엔드**: Supabase (PostgreSQL), supabase-csharp 0.16.2, Npgsql 10.0.1
- **AI 추론**: Microsoft.ML.OnnxRuntime 1.16.3
- **영상 처리**: OpenCvSharp4 4.9.0.20240103 (+ Extensions, WpfExtensions, runtime.win)
- **그래픽**: System.Drawing.Common 8.0.1
- **학습**: Python 3.13, Ultralytics YOLOv8, PyTorch (CUDA 12.4)

## 프로젝트 루트 경로
```
C:\Users\tedya\source\repos\Smart_Factory\project\project_Egg_Classification\
```

## 핵심 폴더 구조
```
demo/
├── training/                    # Python 스크립트 (학습, 모델 다운로드)
│   ├── train.py                 # YOLOv8 학습
│   ├── export_onnx.py           # ONNX 내보내기
│   └── download_face_models.py  # 얼굴인식 모델 다운로드
├── models/                      # ONNX 모델 파일
│   ├── egg_classifier.onnx      # 계란 분류
│   ├── haarcascade_frontalface_default.xml  # 얼굴 탐지
│   └── mobilefacenet.onnx       # 얼굴 임베딩
└── EggClassifier/               # C# WPF 프로젝트 (여기서 빌드)
    ├── App.xaml / App.xaml.cs    # DI 컨테이너, DataTemplate, 전역 리소스
    ├── MainWindow.xaml           # 셸 (사이드바 + ContentControl)
    ├── appsettings.json          # Supabase 연결 정보 (URL, API Key)
    ├── Core/                     # 네비게이션 인프라
    ├── Models/                   # DTO + AI 추론 엔진
    │   └── Database/             # Supabase 엔티티 (UserEntity, EggEntity)
    ├── Services/                 # 비즈니스 로직 서비스
    │   ├── SupabaseService.cs    # Supabase 클라이언트 관리
    │   ├── SupabaseUserService.cs # 사용자 관리 (현재 사용 중)
    │   ├── InspectionService.cs  # 검사 로그 저장
    │   └── UserService.cs        # (레거시, 주석처리됨)
    ├── Features/                 # 기능별 View + ViewModel
    │   ├── Detection/            # 계란 분류 (웹캠 + YOLO)
    │   ├── Login/                # 로그인(2FA) + 회원가입
    │   └── Dashboard/            # 대시보드 (스텁)
    └── ViewModels/               # MainViewModel
```

## 아키텍처 패턴

### MVVM + DI + Feature 기반 구조
- **View**: UserControl (XAML). 코드비하인드에 로직 없음 (PasswordBox 바인딩 헬퍼만 예외)
- **ViewModel**: ViewModelBase 상속. [ObservableProperty]로 바인딩, [RelayCommand]로 커맨드
- **Service**: 인터페이스(I~Service) + 구현체. DI로 생성자 주입
- **네비게이션**: NavigationService.NavigateTo<T>() → DataTemplate 매칭 → ContentControl 렌더링

### DI 등록 규칙 (App.xaml.cs)
- **Singleton**: 서비스 (IWebcamService, IDetectorService, IFaceService, IUserService, IInspectionService, SupabaseService, MainViewModel)
- **Transient**: ViewModel (DetectionViewModel, LoginViewModel, SignUpViewModel, DashboardViewModel)

### ViewModel 생명주기
- `OnNavigatedTo()`: 페이지 진입 시 (이벤트 구독, 모델 로드)
- `OnNavigatedFrom()`: 페이지 이탈 시 (웹캠 정지, 이벤트 해제)

## 주요 서비스 인터페이스

### IUserService — 사용자 관리
```
UserExists(username) → bool
RegisterUser(username, password, faceImagePath, role = "USER") → bool
ValidateCredentials(username, password) → UserData?
```
- **현재 구현**: SupabaseUserService (Supabase PostgreSQL), SHA256+Salt 해싱
- **얼굴 임베딩**: DB에 128차원 float[] 배열로 저장 (user_face 컬럼)
- **역할 관리**: 회원가입 시 USER 또는 ADMIN 선택 가능 (기본값: USER)
- **레거시**: UserService.cs (JSON 파일 방식, 주석처리됨)

### IInspectionService — 검사 로그
```
SaveInspectionAsync(userId, eggClass, accuracy, eggImage) → Task<bool>
GetInspectionCountAsync(userId) → Task<int>
GetInspectionStatsAsync(userId) → Task<(int normal, int defect)>
```
- Supabase egg 테이블에 검사 결과 + 이미지 저장
- 사용자별 통계 조회 (정상/불량 개수)

### IFaceService — 얼굴 탐지 + 임베딩
```
LoadModels() → bool
DetectFace(Mat) → Rect?
GetFaceEmbedding(Mat) → float[]?
CompareFaces(float[], float[]) → float
```
- 얼굴 탐지: Haar Cascade (grayscale → equalizeHist → detectMultiScale)
- 얼굴 임베딩: MobileFaceNet ONNX (112x112 → 128차원 벡터)
- 유사도: 코사인 유사도 (0~1)

### IWebcamService — 웹캠 캡처
- Singleton. 여러 ViewModel이 공유
- FrameCaptured 이벤트로 Mat 프레임 전달
- 사용 후 반드시 Stop() + 이벤트 해제

### IDetectorService — 계란 분류
- YoloDetector 래핑 (YOLOv8 ONNX)
- 5클래스: 정상, 크랙, 이물질, 탈색, 외형이상

## 앱 흐름

### 시작
앱 실행 → MainWindow.Loaded → NavigateToLoginCommand → 로그인 페이지 (사이드바 숨김)

### 회원가입
아이디/비밀번호 입력 → 역할 선택 (USER/ADMIN) → 얼굴 촬영
→ 얼굴 임베딩 추출 (MobileFaceNet) → UserService.RegisterUser(role 포함)
→ Supabase users 테이블에 저장 (임베딩 + 역할)

### 로그인 (2단계)
1단계: 아이디+비밀번호 검증 (Task.Run으로 백그라운드 처리)
2단계: DB에서 얼굴 임베딩 로드 → 웹캠 실시간 비교
→ 유사도 80% 이상, 연속 10프레임 매칭 → MainViewModel.OnLoginSuccess()
→ IsLoggedIn=true, 사이드바 표시, 계란 분류 페이지로 이동

### 로그아웃
MainViewModel.LogoutCommand → IsLoggedIn=false → 사이드바 숨김 → 로그인 페이지

## 크로스스레드 UI 업데이트 패턴
```csharp
var bitmap = frame.ToBitmapSource();
bitmap.Freeze();  // 필수! 크로스스레드 접근 허용
Application.Current.Dispatcher.BeginInvoke(() => {
    CurrentFrame = bitmap;
});
frame.Dispose();  // Mat 사용 후 반드시 해제
```

## 빌드 및 실행
```bash
cd demo/EggClassifier
dotnet restore
dotnet build
dotnet run
```
- 빌드 출력: `bin/Debug/net8.0-windows/`
- 사용자 데이터: `bin/Debug/net8.0-windows/userdata/`

## 모델 파일 경로 규칙
모든 ONNX 모델 파일은 `Models/` 폴더에 배치되며, csproj 설정에 따라 빌드 출력 폴더로 자동 복사됩니다.

**현재 등록된 모델 파일:**
- `Models/egg_classifier.onnx` — 계란 분류 YOLOv8 모델
- `Models/haarcascade_frontalface_default.xml` — 얼굴 탐지 Haar Cascade
- `Models/mobilefacenet.onnx` — 얼굴 임베딩 추출 모델

**새 모델 추가 시:**
1. `demo/EggClassifier/Models/` 폴더에 파일 배치
2. `EggClassifier.csproj`에 다음 형식으로 추가:
```xml
<None Update="Models/your_model.onnx">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```
3. 런타임에는 `bin/Debug/net8.0-windows/Models/` 경로에서 접근

## 코드 컨벤션
- 클래스/메서드: PascalCase
- private 필드: _camelCase
- 네임스페이스 = 폴더 구조 (EggClassifier.Features.Login, EggClassifier.Services 등)
- Feature ViewModel은 ViewModelBase 상속
- 서비스는 인터페이스 + 구현체 쌍으로 작성
- UI 언어: 한국어

## 새 기능 추가 체크리스트
1. `Features/{Name}/` 폴더에 View.xaml + ViewModel.cs 생성
2. ViewModel은 ViewModelBase 상속
3. `App.xaml.cs`에 DI 등록 (`services.AddTransient<NewViewModel>()`)
4. `App.xaml`에 DataTemplate 추가
5. 필요 시 `MainViewModel`에 NavigateToNewCommand 추가
6. 필요 시 `MainWindow.xaml` 사이드바에 RadioButton 추가

## 새 서비스 추가 체크리스트
1. `Services/INewService.cs` 인터페이스 정의
2. `Services/NewService.cs` 구현체 작성
3. `App.xaml.cs`에 DI 등록 (`services.AddSingleton<INewService, NewService>()`)
4. ViewModel 생성자에서 인터페이스로 주입받아 사용

## 상세 문서
- `demo/EggClassifier/README.md` — 프로젝트 개요 + 구조
- `demo/EggClassifier/docs/SUPABASE_BACKEND.md` — **Supabase 백엔드 연동 가이드**
- `demo/EggClassifier/docs/PROJECT_STRUCTURE.md` — 폴더 구조 상세
- `demo/EggClassifier/docs/API_SPEC.md` — 클래스/인터페이스 명세
- `demo/EggClassifier/docs/GUIDELINES.md` — 개발 가이드라인
- `demo/EggClassifier/docs/CODE_REFERENCE.md` — 코드 라인별 해설
- `demo/USAGE.md` — 사용 가이드 (학습 → 빌드 → 실행)

## 현재 미구현/스텁 상태
- **DashboardViewModel**: 스텁 (TotalInspections, NormalCount, DefectCount 기본값 0)
  - IInspectionService를 주입받아 실제 통계 표시 가능
- **검사 로그 저장**: DetectionViewModel에서 InspectionService.SaveInspectionAsync() 호출 필요

## Supabase 설정

### appsettings.json 구조
프로젝트 루트에 `appsettings.json` 파일을 생성하고 다음 형식으로 설정:
```json
{
  "Supabase": {
    "Url": "https://your-project-id.supabase.co",
    "Key": "your-anon-public-key"
  }
}
```
- **Url**: Supabase 프로젝트 대시보드의 Project URL
- **Key**: Supabase 프로젝트 Settings > API > anon/public key

### 데이터베이스 스키마
- **users 테이블**: 사용자 인증 정보 + 얼굴 임베딩 (user_face 컬럼, float[] 배열)
- **egg 테이블**: 검사 로그 (사용자별 계란 분류 결과 + 이미지)
- 상세 스키마 정보: `demo/EggClassifier/docs/SUPABASE_BACKEND.md` 참고

### 레거시 마이그레이션
JSON 파일 방식 → Supabase 이전 방법은 SUPABASE_BACKEND.md 참고
