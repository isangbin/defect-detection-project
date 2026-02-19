# EggClassifier — C# WPF 애플리케이션

YOLOv8s + MobileFaceNet 기반 계란 품질 실시간 분류 데스크톱 앱입니다.
로그인 시 아이디/비밀번호 + 얼굴인식 2차 인증(2FA)을 지원합니다.

| 항목 | 내용 |
|------|------|
| 플랫폼 | Windows 10/11 (WPF, .NET 8.0) |
| MVVM | CommunityToolkit.Mvvm 8.2.2 |
| 백엔드 | Supabase (PostgreSQL) |
| AI 모델 (계란) | YOLOv8s → ONNX (`egg_classifier_v2.onnx`) |
| AI 모델 (얼굴) | Haar Cascade + MobileFaceNet ONNX |
| 추론 엔진 | Microsoft.ML.OnnxRuntime 1.16.3 |
| 영상 처리 | OpenCvSharp4 4.9.0 |
| 학습 성능 | mAP50: 94.0% / mAP50-95: 92.2% |

---

## 분류 클래스 (5종)

| ID | 한글명 | 영문명 | 바운딩박스 색상 |
|----|--------|--------|----------------|
| 0 | 정상 | normal | 녹색 |
| 1 | 크랙 | crack | 빨강 |
| 2 | 이물질 | foreign_matter | 마젠타 |
| 3 | 탈색 | discoloration | 노랑 |
| 4 | 외형이상 | deformed | 주황 |

---

## 프로젝트 구조

```
EggClassifier/
├── App.xaml / App.xaml.cs          # DI 컨테이너, DataTemplate 매핑
├── MainWindow.xaml / .xaml.cs      # 셸 (사이드바 + ContentControl)
├── appsettings.json                 # Supabase URL + API Key
├── EggClassifier.csproj
├── Core/
│   ├── ViewModelBase.cs            # OnNavigatedTo / OnNavigatedFrom
│   ├── INavigationService.cs
│   └── NavigationService.cs
├── Models/
│   ├── Detection.cs                # 탐지 결과 DTO
│   ├── ClassCountItem.cs
│   ├── DetectionItem.cs
│   ├── YoloDetector.cs             # ONNX 추론 엔진 (계란)
│   ├── FaceEmbedder.cs             # ONNX 추론 엔진 (얼굴 임베딩)
│   ├── UserData.cs
│   └── Database/
│       ├── UserEntity.cs           # Supabase users 테이블 엔티티
│       └── EggEntity.cs            # Supabase egg 테이블 엔티티
├── Services/
│   ├── IWebcamService.cs / WebcamService.cs     # 웹캠 캡처 (DSHOW → MSMF 폴백)
│   ├── IDetectorService.cs / DetectorService.cs # YoloDetector 래핑
│   ├── IFaceService.cs / FaceService.cs         # 얼굴 탐지 + 임베딩
│   ├── IUserService.cs                          # 사용자 CRUD 인터페이스
│   ├── SupabaseService.cs                       # Supabase 클라이언트 관리
│   ├── SupabaseUserService.cs                   # 사용자 관리 (현재 사용)
│   ├── UserService.cs                           # (레거시, 주석처리)
│   ├── IInspectionService.cs / InspectionService.cs  # 검사 로그 저장
├── Features/
│   ├── Detection/                  # 계란 분류 (웹캠 + YOLO)
│   │   ├── DetectionView.xaml
│   │   └── DetectionViewModel.cs
│   ├── Login/                      # 로그인 2FA + 회원가입
│   │   ├── LoginView.xaml / LoginViewModel.cs
│   │   ├── SignUpView.xaml / SignUpViewModel.cs
│   └── Dashboard/                  # 통계 대시보드
│       ├── DashboardView.xaml
│       └── DashboardViewModel.cs
├── ViewModels/
│   └── MainViewModel.cs            # 네비게이션 + 로그인 상태
└── docs/
    ├── SUPABASE_BACKEND.md
    ├── PROJECT_STRUCTURE.md
    ├── GUIDELINES.md
    ├── API_SPEC.md
    └── CODE_REFERENCE.md
```

---

## 빠른 시작

### 1. 사전 요구사항

- Windows 10/11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- 웹캠 (USB 또는 내장)
- `demo/models/` 폴더에 ONNX 모델 파일 배치 (아래 참고)

### 2. 모델 파일 준비

`demo/models/` 폴더에 다음 파일이 있어야 합니다:

| 파일 | 용도 | 출처 |
|------|------|------|
| `egg_classifier_v2.onnx` | 계란 분류 (YOLOv8s) | `training/export_onnx.py` 실행 |
| `haarcascade_frontalface_default.xml` | 얼굴 탐지 | `training/download_face_models.py` 실행 |
| `mobilefacenet.onnx` | 얼굴 임베딩 | `training/download_face_models.py` 실행 |

### 3. Supabase 설정

`appsettings.json`에 Supabase 연결 정보를 입력하세요:

```json
{
  "Supabase": {
    "Url": "https://your-project-id.supabase.co",
    "Key": "your-anon-public-key"
  }
}
```

> 스키마 설정은 [docs/SUPABASE_BACKEND.md](docs/SUPABASE_BACKEND.md) 참고

### 4. 빌드 및 실행

```bash
cd demo/EggClassifier
dotnet restore
dotnet build
dotnet run
```

---

## 앱 사용 방법

### 회원가입

1. 앱 실행 → 로그인 페이지에서 **"회원가입"** 클릭
2. 아이디 / 비밀번호 / 역할(USER or ADMIN) 입력
3. **"얼굴 등록"** 클릭 → 웹캠 시작 + 얼굴 탐지 미리보기
4. **"촬영"** 클릭 → 사진 확인 → **"가입하기"**
5. 얼굴 임베딩 추출(MobileFaceNet) → Supabase에 저장 → 로그인 페이지로 이동

### 로그인 (2단계 인증)

```
[1단계] 아이디 + 비밀번호 입력 → "로그인" 클릭
  → Supabase 조회 + SHA256+Salt 검증

[2단계] 웹캠 자동 시작 → 얼굴 인증
  → Haar Cascade 얼굴 탐지
  → MobileFaceNet 임베딩 추출
  → 코사인 유사도 >= 80%, 연속 10프레임 → 로그인 성공
```

### 계란 분류

1. 로그인 성공 → "계란 분류" 페이지 자동 이동
2. **[시작]** 클릭 → 웹캠 활성화 → 실시간 분류
3. 신뢰도 슬라이더로 감도 조절
4. **[중지]** 클릭으로 종료

---

## 모델 교체 방법

계란 분류 모델을 교체하려면 `DetectionViewModel.cs`의 상수 한 줄만 바꾸면 됩니다:

```csharp
// Features/Detection/DetectionViewModel.cs
private const string ModelFileName = "egg_classifier_v2.onnx";  // ← 여기만 변경
```

모델 파일은 `demo/models/` 폴더에서 자동으로 탐색됩니다.

> 새 모델을 추가할 경우 `EggClassifier.csproj`에 별도 등록 없이 `demo/models/`에 배치하기만 하면 됩니다.

---

## 시스템 워크플로우

```
[WebcamService]  웹캠 프레임 캡처 (DSHOW → MSMF 폴백)
      ↓
[YoloDetector]   Letterbox 전처리 → ONNX 추론 → 후처리 (NMS)
      ↓
[DetectionViewModel]  바운딩박스 그리기 + UI 업데이트 (Dispatcher)
      ↓
[DetectionView]  화면 표시
```

---

## 트러블슈팅

### 웹캠이 열리지 않는 경우
- `WebcamService`는 DSHOW → MSMF 순서로 자동 폴백합니다.
- 다른 앱에서 카메라를 점유 중인지 확인하세요.
- `WebcamService.cs`의 `CameraIndex`를 `0`, `1`, `2`로 바꿔 시도하세요.

### 모델 로드 실패
- `demo/models/` 폴더에 ONNX 파일이 있는지 확인하세요.
- `DetectionViewModel.cs`의 `ModelFileName` 상수와 실제 파일명이 일치하는지 확인하세요.

### 얼굴 인증이 통과되지 않는 경우
- 조명이 밝은 환경에서 정면으로 카메라를 바라보세요.
- 유사도 임계값(80%)은 `LoginViewModel.cs`에서 조정할 수 있습니다.

### Supabase 연결 실패
- `appsettings.json`의 URL과 Key를 확인하세요.
- Supabase 프로젝트가 활성 상태인지 확인하세요.

---

## 상세 문서

| 문서 | 설명 |
|------|------|
| [docs/SUPABASE_BACKEND.md](docs/SUPABASE_BACKEND.md) | DB 스키마 + Supabase 연동 가이드 |
| [docs/PROJECT_STRUCTURE.md](docs/PROJECT_STRUCTURE.md) | 폴더 구조 상세 |
| [docs/GUIDELINES.md](docs/GUIDELINES.md) | 개발 가이드라인 |
| [docs/API_SPEC.md](docs/API_SPEC.md) | 클래스/인터페이스 명세 |
| [docs/CODE_REFERENCE.md](docs/CODE_REFERENCE.md) | 코드 라인별 해설 |
