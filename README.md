# 계란 품질 실시간 분류 시스템

> YOLOv8s + MobileFaceNet 기반 계란 품질 실시간 분류 WPF 데스크톱 애플리케이션

**팀원:** 이귀현 · 임상빈 · 송용승 · 양태균

---

## 목차

1. [프로젝트 개요](#1-프로젝트-개요)
2. [기술 스택](#2-기술-스택)
3. [분류 클래스](#3-분류-클래스-5종)
4. [전체 폴더 구조](#4-전체-폴더-구조)
5. [AI 모델 상세](#5-ai-모델-상세)
6. [시스템 아키텍처](#6-시스템-아키텍처)
7. [환경 설정 및 실행 (처음 시작)](#7-환경-설정-및-실행-처음-시작)
8. [앱 사용 방법](#8-앱-사용-방법)
9. [모델 교체 방법](#9-모델-교체-방법)
10. [Supabase 백엔드 설정](#10-supabase-백엔드-설정)
11. [트러블슈팅](#11-트러블슈팅)

---

## 1. 프로젝트 개요

AI Hub에서 제공하는 계란 이미지 데이터셋으로 YOLOv8n 모델을 학습시키고, 학습된 모델을 ONNX로 변환하여 Windows WPF 데스크톱 앱에서 웹캠 영상을 통해 계란 품질을 실시간으로 분류하는 시스템입니다.

보안을 위해 로그인 시 아이디/비밀번호 1차 인증 후 얼굴인식(MobileFaceNet) 2차 인증(2FA)을 수행합니다. 검사 결과는 Supabase(PostgreSQL)에 저장되며 대시보드에서 통계를 확인할 수 있습니다.

| 항목 | 내용 |
|------|------|
| 플랫폼 | Windows 10/11 (WPF, .NET 8.0) |
| AI 모델 (계란) | YOLOv8n → ONNX (`egg_classifier.onnx`) |
| AI 모델 (얼굴) | Haar Cascade + MobileFaceNet ONNX |
| 백엔드 | Supabase (PostgreSQL) |
| 학습 데이터 | AI Hub 계란 데이터셋 (학습 43,091장 / 검증 5,387장) |
| 학습 성능 | mAP50: **94.0%** / mAP50-95: **92.2%** |

---

## 2. 기술 스택

### C# 애플리케이션

| 구분 | 기술 | 버전 |
|------|------|------|
| 런타임 | .NET / WPF | 8.0 |
| MVVM | CommunityToolkit.Mvvm | 8.2.2 |
| DI 컨테이너 | Microsoft.Extensions.DependencyInjection | 8.0.1 |
| AI 추론 | Microsoft.ML.OnnxRuntime | 1.16.3 |
| 영상 처리 | OpenCvSharp4 | 4.9.0.20240103 |
| 백엔드 SDK | supabase-csharp | 0.16.2 |
| DB 드라이버 | Npgsql | 10.0.1 |
| 그래픽 | System.Drawing.Common | 8.0.1 |

### Python 학습 환경

| 구분 | 기술 | 비고 |
|------|------|------|
| 런타임 | Python 3.13 (conda: sf_py) | |
| 모델 학습 | Ultralytics YOLOv8 | |
| 딥러닝 | PyTorch + CUDA 12.4 | GPU 가속 |
| ONNX 변환 | onnx, onnxruntime | |
| 전처리 | OpenCV, NumPy | |

---

## 3. 분류 클래스 (5종)

| ID | 한글명 | 영문명 | 바운딩박스 색상 | 설명 |
|----|--------|--------|----------------|------|
| 0 | 정상 | normal | 녹색 | 품질 양호한 계란 |
| 1 | 크랙 | crack | 빨강 | 껍질에 균열이 있는 계란 |
| 2 | 이물질 | foreign_matter | 마젠타 | 표면에 이물질이 있는 계란 |
| 3 | 탈색 | discoloration | 노랑 | 색상 이상이 있는 계란 |
| 4 | 외형이상 | deformed | 주황 | 형태가 불규칙한 계란 |

---

## 4. 전체 폴더 구조

```
project_Egg_Classification/
├── README.md                                                    
├── data/                              ← YOLO 데이터셋 
│   └── data.yaml                      ← 데이터셋 경로 및 클래스 설정
└── demo/
    ├── training/                      ← Python 학습 환경
    │   ├── README.md                  ← 학습 상세 가이드
    │   ├── train.py                   ← 데이터 분석 + YOLOv8 학습 + 하이퍼파라미터 튜닝 통합
    │   ├── export_onnx.py             ← ONNX 내보내기 + 검증
    │   ├── download_face_models.py    ← 얼굴인식 모델 다운로드
    │   ├── convert_xml_to_yolo.py     ← AI Hub XML → YOLO 포맷 변환
    │   └── requirements.txt           ← Python 의존성
    ├── models/                        ← ONNX 모델 파일 
    │   ├── egg_classifier_v2.onnx     ← 계란 분류 모델 
    │   ├── egg_classifier.onnx        ← 계란 분류 모델 (현재 사용)
    │   ├── egg_classifier_best.pt     ← 학습 완료된 PyTorch 체크포인트
    │   ├── haarcascade_frontalface_default.xml  ← 얼굴 탐지
    │   └── mobilefacenet.onnx         ← 얼굴 임베딩
    └── EggClassifier/                 ← C# WPF 애플리케이션
        ├── README.md                  ← 앱 개발/실행 가이드
        ├── App.xaml / App.xaml.cs     ← DI 컨테이너, DataTemplate, 전역 설정
        ├── MainWindow.xaml            ← 셸 (사이드바 + ContentControl)
        ├── appsettings.json           ← Supabase URL + API Key
        ├── EggClassifier.csproj
        ├── Core/                      ← 네비게이션 인프라
        │   ├── ViewModelBase.cs       ← 페이지 ViewModel 베이스
        │   ├── INavigationService.cs
        │   └── NavigationService.cs
        ├── Models/                    ← DTO + AI 추론 엔진
        │   ├── YoloDetector.cs        ← 계란 분류 ONNX 추론 엔진
        │   ├── FaceEmbedder.cs        ← 얼굴 임베딩 ONNX 추론 엔진
        │   ├── Detection.cs           ← 탐지 결과 DTO
        │   ├── UserData.cs
        │   └── Database/
        │       ├── UserEntity.cs      ← Supabase users 테이블 엔티티
        │       └── EggEntity.cs       ← Supabase egg 테이블 엔티티
        ├── Services/                  ← 비즈니스 로직 서비스
        │   ├── WebcamService.cs       ← 웹캠 캡처 (DSHOW → MSMF 자동 폴백)
        │   ├── DetectorService.cs     ← YoloDetector 래핑
        │   ├── FaceService.cs         ← 얼굴 탐지 + 임베딩 서비스
        │   ├── SupabaseService.cs     ← Supabase 클라이언트 관리
        │   ├── SupabaseUserService.cs ← 사용자 관리 (현재 사용)
        │   └── InspectionService.cs   ← 검사 로그 저장
        ├── Features/                  ← 기능별 View + ViewModel
        │   ├── Detection/             ← 계란 분류 페이지
        │   ├── Login/                 ← 로그인(2FA) + 회원가입
        │   └── Dashboard/             ← 검사 통계 대시보드
        ├── ViewModels/
        │   └── MainViewModel.cs       ← 네비게이션 + 로그인 상태
        └── docs/                      ← 상세 문서
            ├── SUPABASE_BACKEND.md
            ├── PROJECT_STRUCTURE.md
            ├── GUIDELINES.md
            ├── API_SPEC.md
            └── CODE_REFERENCE.md
```

---

## 5. AI 모델 상세

### 5-1. 계란 분류 모델 (YOLOv8s)

| 항목 | 내용 |
|------|------|
| 아키텍처 | YOLOv8n (small, ~11M 파라미터) |
| 입력 크기 | 640×640 |
| 출력 형식 | ONNX (opset 12, 고정 입력) |
| 학습 데이터 | AI Hub 계란 데이터셋 |
| 학습 에포크 | 40(Early stopping 적용) |
| 배치 크기 | 16 |
| 옵티마이저 | Auto (YOLOv8 내부 선택) |
| 증강 기법 | MixUp, CopyPaste, Mosaic, 좌우반전, Cosine LR |
| mAP50 | **94.0%** |
| mAP50-95 | **92.2%** |
| Precision | 88.0% |
| Recall | 87.5% |
| 현재 파일 | `demo/models/egg_classifier.onnx` |

**추론 파이프라인:**

```
웹캠 프레임 (BGR Mat)
  → Letterbox 리사이즈 (640×640, 패딩 유지)
  → 정규화 (0~1) + BGR→RGB + NCHW 변환
  → ONNX Runtime 추론
  → 바운딩박스 디코딩 + 좌표 역변환 + NMS
  → Detection[] 결과 반환
```

### 5-2. 얼굴인식 모델

| 용도 | 모델 | 입력 | 출력 |
|------|------|------|------|
| 얼굴 탐지 | OpenCV Haar Cascade (~930 KB) | 이미지 (Grayscale) | 얼굴 좌표 (Rect) |
| 얼굴 임베딩 | MobileFaceNet ONNX (~5 MB) | 112×112 RGB | 128차원 float 벡터 |
| 얼굴 비교 | 코사인 유사도 (코드 구현) | 두 임베딩 벡터 | 유사도 0~1 |

- 로그인 성공 조건: 유사도 **≥ 80%**, 연속 **10프레임** 매칭
- 얼굴 탐지 전처리: Grayscale 변환 → equalizeHist → detectMultiScale

---

## 6. 시스템 아키텍처

### 6-1. MVVM + DI + Feature 기반 구조

```
View (XAML UserControl)
  ↕  데이터 바인딩 ([ObservableProperty], [RelayCommand])
ViewModel (ViewModelBase 상속)
  ↕  생성자 주입 (DI)
Service (인터페이스 + 구현체)
```

- **View**: 코드비하인드 로직 없음 (PasswordBox 바인딩 헬퍼만 예외)
- **ViewModel**: `OnNavigatedTo()` → 이벤트 구독·모델 로드 / `OnNavigatedFrom()` → 웹캠 정지·이벤트 해제
- **네비게이션**: `NavigationService.NavigateTo<T>()` → DataTemplate 매칭 → ContentControl 렌더링

### 6-2. DI 등록 규칙

```
Singleton: IWebcamService, IDetectorService, IFaceService,
           IUserService, IInspectionService, SupabaseService, MainViewModel

Transient: DetectionViewModel, LoginViewModel, SignUpViewModel, DashboardViewModel
```

### 6-3. 실시간 분류 흐름

```
[WebcamService]        웹캠 프레임 캡처 (별도 스레드)
       ↓  FrameCaptured 이벤트 (Mat)
[DetectionViewModel]   OnFrameCaptured() 호출
       ↓
[YoloDetector]         전처리 → ONNX 추론 → 후처리 → Detection[]
       ↓
[DetectionViewModel]   바운딩박스 그리기 (OpenCvSharp Cv2.Rectangle)
       ↓  bitmap.Freeze() + Dispatcher.BeginInvoke()
[DetectionView]        CurrentFrame 바인딩 → 화면 렌더링
```

### 6-4. 크로스스레드 UI 업데이트 패턴

```csharp
var bitmap = frame.ToBitmapSource();
bitmap.Freeze();          // 필수: 크로스스레드 접근 허용
Application.Current.Dispatcher.BeginInvoke(() => {
    CurrentFrame = bitmap;
});
frame.Dispose();          // Mat 사용 후 반드시 해제
```

---

## 7. 환경 설정 및 실행 (처음 시작)

### 사전 요구사항

- Windows 10/11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Anaconda](https://www.anaconda.com/download)
- 웹캠 (USB 또는 내장)
- NVIDIA GPU + CUDA 12.x (권장; CPU도 가능)
- AI Hub 계란 데이터셋 (학습 시)

---

### Phase 1 — Python 환경 및 모델 학습

#### 1-1. 환경 설정

```bash
conda create -n sf_py python=3.13
conda activate sf_py
cd demo/training
pip install -r requirements.txt
```

#### 1-2. 데이터 변환 (AI Hub XML → YOLO)

```bash
python convert_xml_to_yolo.py ^
    --train-images "D:\원천데이터\Training" ^
    --train-labels "D:\라벨링데이터\Training" ^
    --val-images   "D:\원천데이터\Validation" ^
    --val-labels   "D:\라벨링데이터\Validation" ^
    --output "../data"
```

변환 후 `demo/data/data.yaml`의 `path:` 항목을 데이터 **절대 경로**로 확인·수정하세요:

```yaml
path: D:/repos/Smart_Factory/project_CSharp/data
train: images/train
val: images/val
nc: 5
names: [normal, crack, foreign_matter, discoloration, deformed]
```

#### 1-3. 데이터 분석 (선택)

```bash
python train.py --data "D:/경로/data.yaml" --analyze-only
```

#### 1-4. 모델 학습

```bash
python train.py \
    --data "D:/경로/data.yaml" \
    --model s \
    --epochs 150 \
    --batch 8 \
    --device 0
```

| 인자 | 기본값 | 설명 |
|------|--------|------|
| `--model` | `s` | 모델 크기 (`n`/`s`/`m`/`l`/`x`) |
| `--epochs` | `40` | 최대 에포크 수 |
| `--batch` | `8` | 배치 크기 (RTX 3070 기준 s=8 권장) |
| `--device` | `0` | GPU 번호 또는 `cpu` |
| `--early-stop` | `30` | Early stopping patience |
| `--analyze-only` | — | 데이터 분석만 실행 |
| `--tune` | — | 하이퍼파라미터 자동 튜닝 |

학습 완료 후 `best.pt`가 자동으로 `demo/models/egg_classifier_best.pt`에 복사됩니다.

#### 1-5. ONNX 변환

```bash
python export_onnx.py \
    --model "../models/egg_classifier_best.pt" \
    --output "../models" \
    --name "egg_classifier_v2.onnx" \
    --verify
```

동일 파일명이 있으면 `_1`, `_2`... 접미사를 붙여 자동 저장합니다.

---

### Phase 2 — 얼굴인식 모델 다운로드

```bash
cd demo/training
python download_face_models.py
```

`demo/models/`에 다음 파일이 생성됩니다:
- `haarcascade_frontalface_default.xml`
- `mobilefacenet.onnx`

---

### Phase 3 — C# 앱 설정 및 실행

#### 3-1. 모델 파일 확인

`demo/models/` 폴더에 아래 파일이 모두 있어야 합니다:

```
demo/models/
├── egg_classifier.onnx                  ← 계란 분류 (Phase 1에서 생성)
├── haarcascade_frontalface_default.xml     ← 얼굴 탐지 (Phase 2에서 다운로드)
└── mobilefacenet.onnx                      ← 얼굴 임베딩 (Phase 2에서 다운로드)
```

#### 3-2. Supabase 설정

`demo/EggClassifier/appsettings.json`에 연결 정보를 입력합니다:

```json
{
  "Supabase": {
    "Url": "https://your-project-id.supabase.co",
    "Key": "your-anon-public-key"
  }
}
```

- **Url**: Supabase 프로젝트 대시보드 → Project URL
- **Key**: Supabase → Settings → API → anon/public key

Supabase에 아래 두 테이블이 필요합니다:

```sql
-- 사용자 테이블
CREATE TABLE users (
    id         BIGSERIAL PRIMARY KEY,
    username   TEXT UNIQUE NOT NULL,
    password   TEXT NOT NULL,       -- SHA256 + Salt 해시
    salt       TEXT NOT NULL,
    user_face  FLOAT8[],            -- 128차원 얼굴 임베딩
    role       TEXT DEFAULT 'USER'  -- 'USER' 또는 'ADMIN'
);

-- 검사 로그 테이블
CREATE TABLE egg (
    id          BIGSERIAL PRIMARY KEY,
    user_id     BIGINT REFERENCES users(id),
    egg_class   TEXT NOT NULL,       -- 분류 결과 (normal, crack 등)
    accuracy    FLOAT8,              -- 신뢰도 (0~1)
    egg_image   BYTEA,               -- 캡처 이미지 (바이너리)
    created_at  TIMESTAMPTZ DEFAULT NOW()
);
```

#### 3-3. 빌드 및 실행

```bash
cd demo/EggClassifier
dotnet restore
dotnet build
dotnet run
```

---

## 8. 앱 사용 방법

### 8-1. 전체 흐름

```
앱 실행
  └─ 로그인 페이지 (사이드바 숨김)
       ├─ 회원가입
       │    아이디 / 비밀번호 / 역할 입력
       │    → 얼굴 촬영 (웹캠)
       │    → 가입하기 → Supabase 저장
       │
       └─ 로그인
            1단계: 아이디 + 비밀번호 검증 (SHA256+Salt)
            2단계: 웹캠 얼굴 인증 (MobileFaceNet, 유사도 ≥80%, 연속 10프레임)
            → 성공 → 사이드바 표시 → 계란 분류 페이지 이동
                 ├─ [시작] → 웹캠 활성화 → 실시간 계란 분류
                 ├─ 사이드바 "대시보드" → 검사 통계 확인
                 └─ 사이드바 "로그아웃" → 로그인 페이지 복귀
```

### 8-2. 회원가입 상세

1. 로그인 페이지 → **"회원가입"** 클릭
2. 아이디 / 비밀번호 / 비밀번호 확인 입력
3. 역할 선택: **USER** 또는 **ADMIN**
4. **"얼굴 등록"** 클릭 → 웹캠 시작 + 얼굴 탐지 미리보기
5. **"촬영"** → 얼굴 사진 확인 → **"확인"** 또는 **"재촬영"**
6. **"가입하기"** → 얼굴 임베딩 추출(MobileFaceNet) → Supabase 저장

### 8-3. 로그인 상세 (2단계 인증)

```
[1단계] 아이디 + 비밀번호 입력 → "로그인" 클릭
         → Supabase users 테이블 조회 → SHA256+Salt 검증

[2단계] 웹캠 자동 시작 → 얼굴을 카메라에 위치
         → Haar Cascade 얼굴 탐지
         → MobileFaceNet 임베딩 추출 (112×112 → 128차원)
         → 코사인 유사도 계산
         → 유사도 ≥ 80% & 연속 10프레임 → 로그인 성공
         → IsLoggedIn=true → 사이드바 표시 → 계란 분류 페이지 이동
```

### 8-4. 계란 분류 상세

1. **[시작]** 버튼 클릭 → 웹캠 캡처 시작
2. 매 프레임: YoloDetector 추론 → 바운딩박스 + 클래스명 + 신뢰도 표시
3. 우측 패널: 클래스별 탐지 횟수, 신뢰도 임계값 슬라이더
4. **[중지]** 버튼 → 웹캠 정지

---

## 9. 모델 교체 방법

계란 분류 모델을 교체하려면 상수 **한 줄**만 변경하면 됩니다.

`demo/EggClassifier/Features/Detection/DetectionViewModel.cs`:

```csharp
private const string ModelFileName = "egg_classifier_v2.onnx";  // ← 파일명만 변경
```

새 ONNX 파일을 `demo/models/`에 넣고 위 상수를 파일명으로 바꾼 뒤 빌드하면 됩니다.

---

## 10. Supabase 백엔드 설정

### 인증 방식

- 비밀번호: SHA256 + 랜덤 Salt 해싱 (평문 저장 없음)
- 얼굴 임베딩: 128차원 float[] 배열로 `user_face` 컬럼에 저장

### 주요 서비스

| 서비스 | 설명 |
|--------|------|
| `SupabaseUserService` | 회원가입, 로그인 검증, 얼굴 임베딩 저장/조회 |
| `InspectionService` | 검사 결과 저장, 통계 조회 (정상/불량 개수) |

### 사용자 서비스 메서드

```
UserExists(username)                          → bool
RegisterUser(username, password, embedding, role) → bool
ValidateCredentials(username, password)       → UserData?
```

### 검사 서비스 메서드

```
SaveInspectionAsync(userId, eggClass, accuracy, image) → Task<bool>
GetInspectionCountAsync(userId)                        → Task<int>
GetInspectionStatsAsync(userId)              → Task<(int normal, int defect)>
```

---

## 11. 트러블슈팅

### 웹캠이 열리지 않는 경우
- `WebcamService`는 DSHOW → MSMF 순서로 자동 폴백합니다.
- 다른 앱(Zoom, Teams 등)이 카메라를 점유 중인지 확인하세요.
- `WebcamService.cs`의 `CameraIndex`를 0, 1, 2로 바꿔 시도하세요.

### 모델 로드 실패
- `demo/models/`에 ONNX 파일이 있는지 확인하세요.
- `DetectionViewModel.cs`의 `ModelFileName` 상수와 실제 파일명이 일치하는지 확인하세요.

### 얼굴 인증이 통과되지 않는 경우
- 조명이 밝은 환경에서 정면으로 카메라를 바라보세요.
- 임계값(80%)은 `LoginViewModel.cs`에서 조정 가능합니다.

### CUDA Out of Memory (학습 시)
- 배치 크기를 줄이세요: `--batch 4`

### 학습 환경 모듈 오류 (ModuleNotFoundError)
```bash
conda activate sf_py   # sf_py 환경이 활성화되었는지 확인
```

### data.yaml 경로 오류
`data.yaml`의 `path:`를 데이터 실제 위치의 **절대 경로**로 수정하세요.

### Supabase 연결 실패
- `appsettings.json`의 URL과 Key를 확인하세요.
- Supabase 프로젝트가 활성(Active) 상태인지 확인하세요.
