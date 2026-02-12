# EggClassifier - 계란 품질 실시간 분류 시스템

## 프로젝트 개요

AI Hub 계란 데이터셋으로 학습된 YOLOv8 객체 탐지 모델을 활용하여,
웹캠으로 촬영되는 계란의 품질을 **실시간으로 분류**하는 Windows 데스크톱 애플리케이션입니다.

| 항목 | 내용 |
|------|------|
| 플랫폼 | Windows (WPF, .NET 8.0) |
| 백엔드 | Supabase (PostgreSQL) |
| AI 모델 (계란) | YOLOv8n (Ultralytics) → ONNX 변환 |
| AI 모델 (얼굴) | Haar Cascade (탐지) + MobileFaceNet ONNX (임베딩) |
| 추론 엔진 | Microsoft.ML.OnnxRuntime |
| 영상 처리 | OpenCvSharp4 |
| UI 패턴 | MVVM (CommunityToolkit.Mvvm) |
| 인증 | 아이디/비밀번호 + 얼굴인식 2차 인증 |
| 학습 성능 | mAP50-95: 93.3% (40 에포크) |

---

## 분류 클래스 (5종)

| 클래스 ID | 한글명 | 영문명 | 표시 색상 |
|-----------|--------|--------|-----------|
| 0 | 정상 | normal | 녹색 |
| 1 | 크랙 | crack | 빨강 |
| 2 | 이물질 | foreign_matter | 마젠타 |
| 3 | 탈색 | discoloration | 노랑 |
| 4 | 외형이상 | deformed | 주황 |

---

## 프로젝트 구조

```
EggClassifier/
├── App.xaml / App.xaml.cs          # DI 컨테이너, DataTemplate 매핑, 전역 예외 처리
├── MainWindow.xaml / .xaml.cs      # 셸 (사이드바 + ContentControl)
├── EggClassifier.csproj            # NuGet 패키지 (DI 추가됨)
├── Core/
│   ├── ViewModelBase.cs            # 페이지 ViewModel 베이스 (OnNavigatedTo/From)
│   ├── INavigationService.cs       # 네비게이션 인터페이스
│   └── NavigationService.cs        # 네비게이션 구현 (DI resolve + PropertyChanged)
├── Models/
│   ├── Detection.cs                # 탐지 결과 DTO
│   ├── ClassCountItem.cs           # 클래스별 카운트 표시용
│   ├── DetectionItem.cs            # 현재 탐지 표시용
│   ├── YoloDetector.cs             # ONNX 추론 엔진 (계란 분류)
│   ├── FaceEmbedder.cs             # ONNX 얼굴 임베딩 추론 엔진
│   ├── UserData.cs                 # 사용자 데이터 DTO + UserStore
│   ├── Database/                   # Supabase 엔티티
│   │   ├── UserEntity.cs           # users 테이블 엔티티
│   │   └── EggEntity.cs            # egg 테이블 엔티티
│   └── egg_classifier.onnx         # 학습된 ONNX 모델 파일
├── Services/
│   ├── IWebcamService.cs           # 웹캠 서비스 인터페이스
│   ├── WebcamService.cs            # 웹캠 캡처 구현
│   ├── IDetectorService.cs         # 탐지 서비스 인터페이스
│   ├── DetectorService.cs          # YoloDetector 래핑 서비스
│   ├── IFaceService.cs             # 얼굴 탐지/임베딩 서비스 인터페이스
│   ├── FaceService.cs              # Haar Cascade + FaceEmbedder 래핑
│   ├── IUserService.cs             # 사용자 CRUD 서비스 인터페이스
│   ├── UserService.cs              # (레거시) JSON 파일 기반 사용자 저장소
│   ├── SupabaseService.cs          # Supabase 클라이언트 관리
│   ├── SupabaseUserService.cs      # Supabase 기반 사용자 관리 (현재 사용 중)
│   ├── IInspectionService.cs       # 검사 로그 서비스 인터페이스
│   └── InspectionService.cs        # Supabase 기반 검사 로그 저장
├── Features/
│   ├── Detection/                  # 팀원A: 계란 분류
│   │   ├── DetectionView.xaml
│   │   ├── DetectionView.xaml.cs
│   │   └── DetectionViewModel.cs
│   ├── Login/                      # 팀원B: 로그인 + 회원가입
│   │   ├── LoginView.xaml          # 2단계 로그인 UI (자격증명 → 얼굴인증)
│   │   ├── LoginView.xaml.cs
│   │   ├── LoginViewModel.cs       # 2단계 로그인 로직 (비밀번호 + 얼굴 2FA)
│   │   ├── SignUpView.xaml          # 회원가입 UI (폼 + 웹캠 얼굴 촬영)
│   │   ├── SignUpView.xaml.cs
│   │   └── SignUpViewModel.cs       # 회원가입 로직 (얼굴 이미지 저장)
│   └── Dashboard/                  # 팀원C: DB 시각화
│       ├── DashboardView.xaml
│       ├── DashboardView.xaml.cs
│       └── DashboardViewModel.cs
├── ViewModels/
│   └── MainViewModel.cs            # 네비게이션 + 로그인 상태 관리
└── docs/
    ├── PROJECT_STRUCTURE.md         # 프로젝트 구조 상세 가이드
    ├── GUIDELINES.md
    ├── CODE_REFERENCE.md
    └── API_SPEC.md
```

---

## 빠른 시작

### 1. 사전 요구사항

- Windows 10/11
- .NET 8.0 SDK ([다운로드](https://dotnet.microsoft.com/download/dotnet/8.0))
- 웹캠 (USB 또는 내장)
- (선택) NVIDIA GPU + CUDA 12.x (GPU 추론 시)

### 2. Supabase 설정

`appsettings.json` 파일에 Supabase 연결 정보를 입력하세요:

```json
{
  "Supabase": {
    "Url": "https://your-project-id.supabase.co",
    "Key": "your-anon-public-key"
  }
}
```

> Supabase 프로젝트 생성 및 스키마 설정은 [SUPABASE_BACKEND.md](docs/SUPABASE_BACKEND.md)를 참고하세요.

### 3. 빌드 및 실행

```bash
# 프로젝트 폴더로 이동
cd EggClassifier

# NuGet 패키지 복원
dotnet restore

# 빌드
dotnet build

# 실행
dotnet run
```

### 4. 얼굴인식 모델 다운로드

```bash
cd training
python download_face_models.py
```

> `models/` 폴더에 `haarcascade_frontalface_default.xml`과 `mobilefacenet.onnx`가 생성됩니다.

### 5. 사용 방법

1. 앱 실행 → **로그인 페이지** 표시 (시작 페이지)
2. 회원가입: "회원가입" 클릭 → 아이디/비밀번호 입력 + 얼굴 촬영 → "가입하기"
3. 로그인 1단계: 아이디 + 비밀번호 입력 → "로그인" 클릭
4. 로그인 2단계: 웹캠 자동 시작 → 얼굴 인증 (80% 유사도, 연속 프레임 매칭)
5. 인증 성공 → 좌측 사이드바 표시 → "계란 분류" 페이지로 자동 이동
6. **[시작]** 버튼 → 웹캠 활성화 → 실시간 분류
7. 사이드바에서 "대시보드" 전환 가능, "로그아웃" 시 로그인 페이지로 복귀

---

## 시스템 워크플로우

```
[웹캠 프레임 캡처]          WebcamService.cs
        ↓
[Letterbox 전처리]          YoloDetector.cs - Preprocess()
  (종횡비 유지 리사이즈 + 패딩 + 정규화 + NCHW 변환)
        ↓
[ONNX 모델 추론]            YoloDetector.cs - Detect()
  (OnnxRuntime으로 신경망 실행)
        ↓
[후처리]                    YoloDetector.cs - Postprocess()
  (바운딩박스 디코딩 + 좌표 복원 + NMS)
        ↓
[결과 시각화]               DetectionViewModel.cs - OnFrameCaptured()
  (바운딩박스 그리기 + UI 업데이트)
        ↓
[화면 표시]                 Features/Detection/DetectionView.xaml
  (웹캠 영상 + 탐지 결과 패널)
```

---

## 로그인 + 얼굴인식 2차 인증 워크플로우

### 회원가입

```
[회원가입 페이지]
  → 아이디/비밀번호/비밀번호확인 입력
  → 역할 선택 (USER 또는 ADMIN)
  → "얼굴 등록" 클릭 → 웹캠 시작 + 얼굴 탐지 미리보기
  → "촬영" 클릭 → 얼굴 사진 표시
  → "확인" 또는 "재촬영" 선택
  → "가입하기" 클릭
  → 얼굴 이미지에서 임베딩 추출 (MobileFaceNet)
  → Supabase users 테이블에 저장 (SHA256+Salt 해싱, 얼굴 임베딩 배열, 선택한 역할)
  → 로그인 페이지로 이동
```

### 로그인 (2단계)

```
[1단계: 자격증명]
  → 아이디 + 비밀번호 입력 → "로그인" 클릭
  → Supabase users 테이블 조회 → 자격증명 검증 (SHA256+Salt)

[2단계: 얼굴 인증]
  → DB에서 저장된 얼굴 임베딩 로드 (128차원 벡터)
  → 웹캠 자동 시작 → 실시간 얼굴 탐지 (Haar Cascade)
  → 얼굴 크롭 → 임베딩 추출 (MobileFaceNet) → 코사인 유사도 비교
  → 유사도 >= 80%, 연속 10프레임 매칭 → 로그인 성공
  → 계란 분류 페이지로 자동 이동
```

### 얼굴인식 AI 모델

| 용도 | 모델 | 크기 | 입출력 |
|------|------|------|--------|
| 얼굴 탐지 | OpenCV Haar Cascade | ~930KB | 이미지 → 얼굴 좌표(Rect) |
| 얼굴 임베딩 | MobileFaceNet ONNX | ~5MB | 112x112 RGB → 128차원 벡터 |
| 얼굴 비교 | 코사인 유사도 (코드 구현) | - | 두 임베딩 벡터 → 유사도 0~1 |

---

## 모델 학습 파이프라인 (Python)

학습은 별도의 Python 환경에서 수행되며, 결과 ONNX 모델을 C# 앱에서 사용합니다.

```
[AI Hub 계란 데이터 다운로드]
        ↓
[XML → YOLO 포맷 변환]      convert_xml_to_yolo.py
        ↓
[YOLOv8 학습]               run_train.py
  (50 에포크, SGD 옵티마이저, 640x640)
        ↓
[ONNX 내보내기]             export_onnx.py
  (best.pt → best.onnx)
        ↓
[추론 테스트]               test_inference.py
        ↓
[C# 앱에 배치]              Models/egg_classifier.onnx
```

---

## 학습 결과 요약

| 메트릭 | 값 |
|--------|-----|
| 학습 에포크 | 40/50 (Early stop) |
| mAP50 | 93.4% |
| mAP50-95 | 93.3% |
| Precision | 87.7% |
| Recall | 86.5% |
| 추론 속도 | ~17.5ms/이미지 (GPU) |
| 모델 크기 | 11.7MB (ONNX) |

---

## 주요 기술 스택

| 구분 | 기술 | 버전 | 용도 |
|------|------|------|------|
| 런타임 | .NET | 8.0 | 앱 프레임워크 |
| UI | WPF | - | 데스크톱 UI |
| MVVM | CommunityToolkit.Mvvm | 8.2.2 | 데이터 바인딩 |
| DI 컨테이너 | Microsoft.Extensions.DependencyInjection | 8.0.1 | 의존성 주입 |
| 백엔드 | Supabase (PostgreSQL) | - | 사용자 관리 + 검사 로그 저장 |
| Supabase SDK | supabase-csharp | 0.16.2 | Supabase API 클라이언트 |
| AI 추론 | Microsoft.ML.OnnxRuntime | 1.16.3 | ONNX 모델 실행 (계란 분류 + 얼굴 임베딩) |
| 얼굴 탐지 | OpenCV Haar Cascade | - | 얼굴 영역 탐지 |
| 얼굴 임베딩 | MobileFaceNet (ONNX) | - | 얼굴 특징 벡터 추출 |
| 영상처리 | OpenCvSharp4 | 4.9.0 | 웹캠, 이미지 처리 |
| 학습 | Ultralytics YOLOv8 | 8.4.11 | 모델 학습 (Python) |
| GPU | CUDA | 12.4 | GPU 가속 |

---

## 모델 교체 체크리스트

```
[ ] best.pt → ONNX 변환 완료
[ ] 클래스 수가 변경되었으면 YoloDetector.cs의 ClassNames 배열 수정
[ ] 클래스 수가 변경되었으면 ClassColors 배열도 동일하게 수정
[ ] 클래스 수가 변경되었으면 Features/Detection/DetectionViewModel.cs의 ClassBrushes 배열도 수정
[ ] egg_classifier.onnx를 Models/ 폴더에 복사
[ ] 빌드 후 테스트 이미지로 추론 확인
```

---

## 팀원별 개발 가이드

| 팀원 | 담당 폴더 | 역할 |
|------|-----------|------|
| A | `Features/Detection/` | 계란 분류 (웹캠 + YOLO 감지) |
| B | `Features/Login/` | 로그인 / 회원가입 |
| C | `Features/Dashboard/` | DB 시각화 / 통계 대시보드 |

> 자세한 구조 설명과 새 Feature/Service 추가 방법은 [프로젝트 구조 가이드](docs/PROJECT_STRUCTURE.md)를 참고하세요.

---

## 상세 문서

- [Supabase 백엔드 연동 가이드 (SUPABASE_BACKEND.md)](docs/SUPABASE_BACKEND.md)
- [프로젝트 구조 가이드 (PROJECT_STRUCTURE.md)](docs/PROJECT_STRUCTURE.md)
- [개발 가이드라인 (GUIDELINES.md)](docs/GUIDELINES.md)
- [코드 라인별 해설 (CODE_REFERENCE.md)](docs/CODE_REFERENCE.md)
- [API 기능명세서 (API_SPEC.md)](docs/API_SPEC.md)
