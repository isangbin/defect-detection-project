# 개발 가이드라인

## 1. 프로젝트 환경 설정

### 1.1 C# 개발 환경

```
필수 소프트웨어:
- Visual Studio 2022 (17.8+) 또는 VS Code + C# Dev Kit
- .NET 8.0 SDK
- Windows 10/11
```

#### NuGet 패키지 목록

| 패키지 | 버전 | 용도 |
|--------|------|------|
| CommunityToolkit.Mvvm | 8.2.2 | MVVM 소스 생성기 ([ObservableProperty], [RelayCommand]) |
| Microsoft.Extensions.DependencyInjection | 8.0.1 | DI 컨테이너 (서비스/ViewModel 자동 주입) |
| Microsoft.ML.OnnxRuntime | 1.16.3 | ONNX 모델 로드 및 추론 실행 |
| OpenCvSharp4 | 4.9.0 | OpenCV C# 래퍼 (이미지 처리) |
| OpenCvSharp4.Extensions | 4.9.0 | Mat ↔ Bitmap 변환 유틸 |
| OpenCvSharp4.WpfExtensions | 4.9.0 | Mat → BitmapSource 변환 (WPF용) |
| OpenCvSharp4.runtime.win | 4.9.0 | Windows용 네이티브 OpenCV 바이너리 |

#### 빌드 방법

```bash
# 패키지 복원
dotnet restore

# Debug 빌드
dotnet build

# Release 빌드
dotnet build -c Release

# 실행
dotnet run

# 게시 (단일 실행파일)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

### 1.2 Python 학습 환경

```
필수 소프트웨어:
- Anaconda / Miniconda
- Python 3.11+ (sf_py 환경)
- NVIDIA GPU + CUDA 12.x (학습 시)
```

#### Conda 환경 설정

```bash
# 환경 생성
conda create -n sf_py python=3.13

# 환경 활성화
conda activate sf_py

# PyTorch 설치 (CUDA 12.4)
pip install torch torchvision --index-url https://download.pytorch.org/whl/cu124

# Ultralytics 설치
pip install ultralytics

# ONNX 관련 패키지
pip install onnx onnxslim onnxruntime
```

---

## 2. 코드 컨벤션

### 2.1 C# 코드 규칙

```
네이밍:
- 클래스/메서드:     PascalCase          예) YoloDetector, LoadModel()
- private 필드:      _camelCase          예) _session, _disposed
- 프로퍼티:          PascalCase          예) IsLoaded, ClassName
- 지역 변수:         camelCase           예) detections, maxConfidence
- 상수/readonly:     PascalCase          예) ClassNames, ClassColors

파일 구조:
- 클래스당 하나의 파일 (작은 DTO는 같은 파일 허용)
- 네임스페이스 = 폴더 구조와 일치
  - EggClassifier.Core             → Core/ 폴더
  - EggClassifier.Models           → Models/ 폴더
  - EggClassifier.Services         → Services/ 폴더
  - EggClassifier.ViewModels       → ViewModels/ 폴더
  - EggClassifier.Features.Detection  → Features/Detection/ 폴더
  - EggClassifier.Features.Login      → Features/Login/ 폴더
  - EggClassifier.Features.Dashboard  → Features/Dashboard/ 폴더
```

### 2.2 MVVM 패턴 규칙

```
View (XAML):
- UI 레이아웃만 담당
- 코드비하인드에 로직 작성 금지
- 모든 데이터는 Binding으로 연결
- 각 Feature의 View는 UserControl로 작성

ViewModel:
- Feature ViewModel은 ViewModelBase를 상속
- [ObservableProperty]로 바인딩 프로퍼티 선언
- [RelayCommand]로 커맨드 선언
- View에 직접 접근 금지 (Dispatcher만 예외)
- 생성자에서 서비스 인터페이스를 주입받음 (DI)
- OnNavigatedTo(): 페이지 진입 시 이벤트 구독
- OnNavigatedFrom(): 페이지 이탈 시 리소스 정리

Model/Service:
- 순수 로직만 담당
- UI 의존성 없음
- 인터페이스(I~Service)를 통해 접근
- IDisposable 구현 필수 (리소스 사용 시)
```

### 2.3 Python 코드 규칙

```
네이밍:
- 파일명:      snake_case          예) run_train.py, export_onnx.py
- 함수:        snake_case          예) convert_labels()
- 변수:        snake_case          예) model_path, num_classes
- 상수:        UPPER_SNAKE_CASE    예) CLASS_NAMES, INPUT_SIZE
```

---

## 3. 아키텍처 가이드

### 3.1 전체 아키텍처 다이어그램

```
┌──────────────────────────────────────────────────────────────────┐
│                      WPF App (DI Container)                       │
│                                                                   │
│  ┌──────────────┐    ┌──────────────────────────────────────┐    │
│  │ MainWindow   │    │ MainViewModel                        │    │
│  │ (셸)         │◄──►│ (네비게이션 + 로그인 상태 관리)        │    │
│  │ 사이드바      │    │ IsLoggedIn, OnLoginSuccess(), Logout │    │
│  │ + ContentCtrl│    │ NavigationService                    │    │
│  └──────────────┘    └─────────┬────────────────────────────┘    │
│                                │ NavigateTo<T>()                  │
│               ┌────────────────┼────────────────┐                │
│               ▼                ▼                ▼                │
│     ┌──────────────┐  ┌──────────────┐  ┌──────────────┐       │
│     │ Detection    │  │ Login        │  │ Dashboard    │       │
│     │ ViewModel    │  │ ViewModel    │  │ ViewModel    │       │
│     │ + View       │  │ + View       │  │ + View       │       │
│     └──────┬───────┘  │ + SignUp     │  └──────────────┘       │
│            │           │ ViewModel   │                          │
│            │           │ + View      │                          │
│            │           └──────┬──────┘                          │
│     ┌──────┼──────┐    ┌─────┼──────────────┐                  │
│     ▼             ▼    ▼     ▼              ▼                  │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐          │
│  │IWebcam  │ │IDetector │ │IFace     │ │IUser     │          │
│  │Service  │ │Service   │ │Service   │ │Service   │          │
│  │(웹캠)   │ │(YOLO)    │ │(얼굴AI)  │ │(사용자DB)│          │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘          │
└──────────────────────────────────────────────────────────────────┘
```

### 3.2 데이터 흐름

#### 계란 분류 흐름

```
1. WebcamService가 백그라운드 스레드에서 프레임 캡처
2. FrameCaptured 이벤트로 Mat 프레임을 DetectionViewModel에 전달
3. DetectionViewModel이 IDetectorService.Detect()를 호출
4. DetectorService 내부에서 YoloDetector 사용:
   a. Preprocess: Letterbox 리사이즈 → RGB 변환 → 정규화 → NCHW 텐서
   b. OnnxRuntime으로 추론 실행
   c. Postprocess: 출력 디코딩 → 좌표 복원 → NMS
5. Detection 리스트가 반환됨
6. DrawDetections()로 이미지에 바운딩박스 그리기
7. BitmapSource로 변환 → Freeze → BeginInvoke로 UI 업데이트
```

#### 로그인 얼굴 인증 흐름

```
1. LoginViewModel이 저장된 얼굴 이미지 로드 (Cv2.ImRead)
2. FaceService.GetFaceEmbedding()으로 저장된 얼굴 임베딩 추출
3. WebcamService가 실시간 프레임 캡처 시작
4. 매 프레임:
   a. FaceService.DetectFace() → Haar Cascade로 얼굴 영역 탐지
   b. 얼굴 크롭 (20% 마진) → FaceService.GetFaceEmbedding()
   c. FaceService.CompareFaces() → 코사인 유사도 계산
   d. 유사도 >= 80%: 연속 매칭 카운트 증가
   e. 연속 매칭 달성 → 로그인 성공 → MainViewModel.OnLoginSuccess()
```

#### 회원가입 얼굴 촬영 흐름

```
1. SignUpViewModel이 웹캠 시작 (StartFaceCapture)
2. 매 프레임: Haar Cascade 얼굴 탐지 → 사각형 표시 → UI 업데이트
3. "촬영" 클릭 → 현재 프레임 캡처 → 얼굴 크롭 → 썸네일 표시
4. "확인" 클릭 → Mat 이미지 확정 (모델 불필요, 순수 이미지 저장)
5. "가입하기" 클릭 → Cv2.ImWrite()로 PNG 저장 → UserService.RegisterUser()
```

### 3.3 스레딩 모델

```
[UI Thread]
  └─ MainWindow, DetectionViewModel (프로퍼티 업데이트, UI 렌더링)

[Capture Thread] (Task.Run)
  └─ WebcamService.CaptureLoop()
     ├─ VideoCapture.Read()     ← 프레임 캡처
     ├─ FrameCaptured 이벤트     ← 구독자 호출 (같은 스레드)
     │   ├─ YoloDetector.Detect() ← AI 추론 (같은 스레드)
     │   └─ Dispatcher.BeginInvoke() ← UI 업데이트 비동기 요청
     └─ Thread.Sleep()          ← 프레임레이트 조절

주의사항:
- BitmapSource.Freeze()를 호출해야 크로스스레드 접근 가능
- Mat 객체는 사용 후 반드시 Dispose() 호출
- Dispatcher.BeginInvoke (비동기) 사용 → 캡처 스레드 블로킹 방지
```

---

## 4. 모델 재학습 가이드

### 4.1 데이터 준비

```
data/
├── images/
│   ├── train/          # 학습 이미지 (~43,000장)
│   └── val/            # 검증 이미지 (~5,400장)
├── labels/
│   ├── train/          # YOLO 포맷 라벨 (.txt)
│   └── val/
└── data.yaml           # 데이터셋 설정 파일
```

#### data.yaml 형식

```yaml
path: ./data
train: images/train
val: images/val

nc: 5
names: ['normal', 'crack', 'foreign_matter', 'discoloration', 'deformed']
```

#### YOLO 라벨 형식 (.txt)

```
# 각 줄: class_id  center_x  center_y  width  height  (0~1 정규화)
0 0.512 0.489 0.320 0.410
1 0.230 0.670 0.150 0.200
```

### 4.2 학습 실행

```bash
# Anaconda 환경 활성화
conda activate sf_py

# 학습 시작
python run_train.py
```

#### 주요 하이퍼파라미터

| 파라미터 | 현재값 | 설명 |
|----------|--------|------|
| epochs | 50 | 전체 학습 반복 횟수 |
| imgsz | 640 | 입력 이미지 크기 (정사각형) |
| batch | 32 | 배치 크기 (GPU 메모리에 따라 조절) |
| patience | 10 | Early stopping 인내심 (10 에포크 개선 없으면 중단) |
| optimizer | SGD | 옵티마이저 (RTX 3070은 SGD 권장) |
| device | 0 | GPU 장치 번호 (0 = 첫 번째 GPU) |
| workers | 4 | 데이터 로딩 워커 수 |

### 4.3 ONNX 내보내기

```bash
python export_onnx.py
```

내보내기 후 `best.onnx` 파일을 `EggClassifier/Models/egg_classifier.onnx`에 복사합니다.

### 4.4 모델 교체 시 체크리스트

```
[ ] best.pt → ONNX 변환 완료
[ ] 클래스 수가 변경되었으면 YoloDetector.cs의 ClassNames 배열 수정
[ ] 클래스 수가 변경되었으면 ClassColors 배열도 동일하게 수정
[ ] 클래스 수가 변경되었으면 Features/Detection/DetectionViewModel.cs의 ClassBrushes 배열도 수정
[ ] egg_classifier.onnx를 Models/ 폴더에 복사
[ ] 빌드 후 테스트 이미지로 추론 확인
```

---

## 5. 트러블슈팅

### 5.1 자주 발생하는 문제

| 증상 | 원인 | 해결법 |
|------|------|--------|
| "웹캠을 열 수 없습니다" | 카메라 미연결 또는 다른 앱이 점유 | 카메라 연결 확인, 다른 앱 종료 |
| "모델 없음" 표시 | ONNX 파일이 Models/ 폴더에 없음 | egg_classifier.onnx 파일 배치 |
| 모델 로드 실패 | ONNX 파일 손상 또는 호환성 문제 | ONNX 재내보내기 (opset=12) |
| 추론 속도 느림 | CPU 모드로 동작 중 | CUDA 설치 확인, GPU 드라이버 업데이트 |
| 메모리 증가 | Mat 객체 미해제 | Dispose() 호출 확인 |
| 학습 시 CUBLAS 에러 | Muon 옵티마이저 BF16 호환성 | optimizer='SGD'로 변경 |
| 바운딩박스 위치 부정확 | 전처리 방식 불일치 | Letterbox 전처리 사용 확인 |
| 얼굴인식 모델 없음 | haarcascade/mobilefacenet 파일 없음 | `python training/download_face_models.py` 실행 |
| 얼굴 인증 실패 | 유사도가 임계값 미달 | 조명/각도 조정, SIMILARITY_THRESHOLD 조정 |
| "등록된 얼굴 이미지를 찾을 수 없습니다" | 얼굴 이미지 파일 삭제됨 | userdata/faces/ 폴더 확인, 재등록 |

### 5.2 성능 튜닝

```
FPS 향상 방법:
1. GPU 추론 활성화 (CUDA 설치)
2. 입력 해상도 낮추기 (640 → 416)
3. 신뢰도 임계값 높이기 (후보 박스 감소)
4. 프레임 스킵 (매 2~3프레임마다 추론)
5. 비동기 추론 파이프라인 구성

메모리 최적화:
1. Mat 사용 후 즉시 Dispose()
2. BitmapSource.Freeze()로 GC 최적화
3. ObservableCollection 갱신 최소화
```

### 5.3 팀원 충돌 방지 규칙

| 팀원 | 수정 범위 | 비고 |
|------|-----------|------|
| A | `Features/Detection/` | 계란 분류 기능 |
| B | `Features/Login/` | 로그인 기능 |
| C | `Features/Dashboard/` | 대시보드 기능 |
| 공통 | `Services/`에 새 파일 추가 | 신규 파일이므로 충돌 없음 |
| 공통 | `App.xaml.cs`에 DI 등록 1줄 | append-only라 충돌 확률 낮음 |

새 Feature/Service 추가 방법은 [PROJECT_STRUCTURE.md](PROJECT_STRUCTURE.md) 참고

---

## 6. 배포 가이드

### 6.1 Release 빌드

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

### 6.2 배포 시 포함 파일

```
배포 폴더/
├── EggClassifier.exe                       # 실행 파일
├── Models/
│   └── egg_classifier.onnx                 # 계란 분류 ONNX 모델
├── haarcascade_frontalface_default.xml      # 얼굴 탐지 모델
├── mobilefacenet.onnx                       # 얼굴 임베딩 ONNX 모델
└── (기타 런타임 DLL - self-contained 시 포함됨)
```

### 6.3 사용자 PC 요구사항

```
최소 사양:
- Windows 10 이상
- CPU: 4코어 이상
- RAM: 8GB 이상
- 웹캠

권장 사양:
- Windows 10/11
- NVIDIA GPU (GTX 1060 이상)
- CUDA 12.x + cuDNN
- RAM: 16GB
- USB 3.0 웹캠
```
