# API 기능명세서

## 목차

1. [EggClassifier.Models.Detection](#1-detection-클래스)
2. [EggClassifier.Models.YoloDetector](#2-yolodetector-클래스)
3. [EggClassifier.Services.FrameCapturedEventArgs](#3-framecapturedeventargs-클래스)
4. [EggClassifier.Services.WebcamService](#4-webcamservice-클래스)
5. [EggClassifier.ViewModels.ClassCountItem](#5-classcountitem-클래스)
6. [EggClassifier.ViewModels.DetectionItem](#6-detectionitem-클래스)
7. [EggClassifier.ViewModels.MainViewModel](#7-mainviewmodel-클래스)
8. [Python Scripts](#8-python-스크립트)

---

## 1. Detection 클래스

**네임스페이스:** `EggClassifier.Models`
**파일:** `Models/YoloDetector.cs`
**역할:** 단일 객체 탐지 결과를 담는 데이터 클래스 (DTO)

### 프로퍼티

| 프로퍼티 | 타입 | 설명 | 예시 값 |
|----------|------|------|---------|
| `ClassId` | `int` | 탐지된 클래스 번호 (0~4) | `0` (정상) |
| `ClassName` | `string` | 클래스 한글 이름 | `"크랙"` |
| `Confidence` | `float` | 신뢰도 (0.0~1.0) | `0.95` |
| `BoundingBox` | `Rect` | 바운딩박스 좌표 (원본 이미지 기준) | `Rect(100, 50, 200, 180)` |

### BoundingBox 좌표계

```
원본 이미지 (예: 640x480)
┌──────────────────────────┐
│  (X, Y)                  │
│   ┌──────────┐           │
│   │  계란    │ Height    │
│   │          │           │
│   └──────────┘           │
│      Width               │
└──────────────────────────┘

X, Y = 좌상단 좌표 (픽셀)
Width, Height = 박스 크기 (픽셀)
```

---

## 2. YoloDetector 클래스

**네임스페이스:** `EggClassifier.Models`
**파일:** `Models/YoloDetector.cs`
**인터페이스:** `IDisposable`
**역할:** YOLOv8 ONNX 모델을 로드하고 이미지에서 객체를 탐지하는 핵심 추론 엔진

### 정적 필드

| 필드 | 타입 | 설명 |
|------|------|------|
| `ClassNames` | `string[]` | 5개 클래스 한글 이름 배열 `["정상", "크랙", "이물질", "탈색", "외형이상"]` |
| `ClassColors` | `Scalar[]` | 클래스별 바운딩박스 색상 (BGR 형식) |

### 프로퍼티

| 프로퍼티 | 타입 | 설명 |
|----------|------|------|
| `IsLoaded` | `bool` | ONNX 모델이 성공적으로 로드되었는지 여부 |

### 메서드

#### `bool LoadModel(string modelPath)`

ONNX 모델 파일을 로드합니다.

| 항목 | 내용 |
|------|------|
| **매개변수** | `modelPath` — ONNX 파일의 절대/상대 경로 |
| **반환값** | `true` = 성공, `false` = 실패 |
| **동작** | 1) SessionOptions 생성 (최대 그래프 최적화)<br>2) CUDA 프로바이더 시도 → 실패 시 CPU 폴백<br>3) InferenceSession 생성<br>4) 입출력 메타데이터 콘솔 출력 |
| **예외** | 내부에서 catch 후 `false` 반환 (예외를 던지지 않음) |

```csharp
// 사용 예시
var detector = new YoloDetector();
bool success = detector.LoadModel("Models/egg_classifier.onnx");
```

---

#### `List<Detection> Detect(Mat image, float confidenceThreshold = 0.5f, float nmsThreshold = 0.45f)`

이미지에서 객체 탐지를 수행합니다. 핵심 추론 메서드입니다.

| 항목 | 내용 |
|------|------|
| **매개변수** | `image` — 입력 이미지 (BGR, 임의 크기)<br>`confidenceThreshold` — 최소 신뢰도 (기본 0.5)<br>`nmsThreshold` — NMS IoU 임계값 (기본 0.45) |
| **반환값** | `List<Detection>` — 탐지된 객체 목록 (비어있을 수 있음) |
| **동작** | 1) 유효성 검사 (모델 로드 여부, 이미지 비어있는지)<br>2) Preprocess() — Letterbox 전처리<br>3) OnnxRuntime 추론 실행<br>4) Postprocess() — 후처리 + NMS |
| **스레드 안전성** | 단일 스레드에서만 호출 권장 |

```csharp
// 사용 예시
var frame = new Mat("test.jpg");
List<Detection> results = detector.Detect(frame, confidenceThreshold: 0.6f);

foreach (var det in results)
{
    Console.WriteLine($"{det.ClassName}: {det.Confidence:P0} at {det.BoundingBox}");
}
```

---

#### `private DenseTensor<float> Preprocess(Mat image)` [내부]

입력 이미지를 ONNX 모델이 요구하는 형식으로 변환합니다.

| 항목 | 내용 |
|------|------|
| **입력** | BGR Mat (임의 크기) |
| **출력** | `DenseTensor<float>` shape `[1, 3, 640, 640]` |
| **처리 순서** | 1) Letterbox 스케일 계산 (종횡비 유지)<br>2) 리사이즈 → 114 회색 패딩으로 640x640 캔버스 생성<br>3) BGR → RGB 변환<br>4) 0-255 → 0.0-1.0 정규화 (ConvertTo 일괄 처리)<br>5) 채널 분리 → NCHW 텐서 변환 |
| **부수효과** | `_letterboxScale`, `_letterboxPadX`, `_letterboxPadY` 업데이트 |

**Letterbox 시각화:**

```
원본 이미지 (800x400)           Letterbox 결과 (640x640)
┌────────────────────┐         ┌──────────────────┐
│                    │         │ ■■■ 패딩 (114) ■■■ │
│    계란 이미지      │   →    │ ┌──────────────┐  │
│                    │         │ │  리사이즈된    │  │
│                    │         │ │  이미지       │  │
└────────────────────┘         │ └──────────────┘  │
                               │ ■■■ 패딩 (114) ■■■ │
                               └──────────────────┘
```

---

#### `private List<Detection> Postprocess(...)` [내부]

ONNX 모델 출력을 Detection 리스트로 변환합니다.

| 항목 | 내용 |
|------|------|
| **입력** | `Tensor<float>` shape `[1, 9, 8400]` 또는 `[1, 8400, 9]` |
| **출력** | `List<Detection>` (NMS 적용 후) |
| **처리 순서** | 1) 출력 shape 분석 (transposed 여부 판별)<br>2) 8400개 앵커 순회: 최대 신뢰도 클래스 탐색<br>3) 신뢰도 임계값 필터링<br>4) cx,cy,w,h → Letterbox 좌표 → 원본 좌표 복원<br>5) 경계 클리핑<br>6) NMS 적용 |

**출력 텐서 해석:**

```
[1, 9, 8400] 형식:
- 9 = 4(bbox) + 5(classes)
- 8400 = 탐지 후보 수 (80x80 + 40x40 + 20x20 그리드)

인덱스 0: center_x
인덱스 1: center_y
인덱스 2: width
인덱스 3: height
인덱스 4: class_0 신뢰도 (정상)
인덱스 5: class_1 신뢰도 (크랙)
인덱스 6: class_2 신뢰도 (이물질)
인덱스 7: class_3 신뢰도 (탈색)
인덱스 8: class_4 신뢰도 (외형이상)
```

**좌표 복원 공식:**

```
원본_x = (모델출력_cx - 패딩_x - w/2) / letterbox_scale
원본_y = (모델출력_cy - 패딩_y - h/2) / letterbox_scale
원본_w = 모델출력_w / letterbox_scale
원본_h = 모델출력_h / letterbox_scale
```

---

#### `private static List<int> ApplyNMS(...)` [내부]

Non-Maximum Suppression — 겹치는 바운딩박스 중 가장 신뢰도 높은 것만 남깁니다.

| 항목 | 내용 |
|------|------|
| **매개변수** | `boxes` — 바운딩박스 리스트<br>`confidences` — 신뢰도 리스트<br>`nmsThreshold` — IoU 임계값 (기본 0.45) |
| **반환값** | `List<int>` — 살아남은 박스의 인덱스 |
| **알고리즘** | 1) 신뢰도 내림차순 정렬<br>2) 가장 높은 것부터 선택<br>3) 선택된 박스와 IoU > 임계값인 박스 억제(suppressed)<br>4) 반복 |

```
NMS 예시:
  Box A (95%) ─── IoU=0.8 ──── Box B (87%)   → B 제거
  Box A (95%) ─── IoU=0.2 ──── Box C (92%)   → C 유지 (다른 계란)
```

---

#### `private static float CalculateIoU(Rect a, Rect b)` [내부]

두 바운딩박스의 IoU (Intersection over Union)를 계산합니다.

| 항목 | 내용 |
|------|------|
| **반환값** | `float` (0.0 ~ 1.0) — 0이면 겹침 없음, 1이면 완전 겹침 |

```
IoU = 교집합 넓이 / 합집합 넓이

┌───────┐
│   A   │
│   ┌───┼───┐
│   │///│   │    /// = 교집합
└───┼───┘   │
    │   B   │
    └───────┘

합집합 = A넓이 + B넓이 - 교집합넓이
```

---

#### `static void DrawDetections(Mat image, List<Detection> detections)`

탐지 결과를 이미지 위에 시각화합니다.

| 항목 | 내용 |
|------|------|
| **매개변수** | `image` — 그릴 대상 이미지 (원본 수정됨)<br>`detections` — 탐지 결과 리스트 |
| **동작** | 각 Detection에 대해:<br>1) 클래스 색상으로 바운딩박스 직사각형 그리기<br>2) 라벨 배경 (색상 채운 사각형) 그리기<br>3) 라벨 텍스트 ("클래스명 신뢰도%") 그리기<br>4) 라벨이 이미지 상단을 벗어나면 박스 아래에 표시 |

---

#### `void Dispose()`

ONNX InferenceSession을 해제합니다.

| 항목 | 내용 |
|------|------|
| **동작** | `_session?.Dispose()` 호출 후 null 설정 |
| **중복 호출** | 안전 (`_disposed` 플래그로 보호) |

---

## 3. FrameCapturedEventArgs 클래스

**네임스페이스:** `EggClassifier.Services`
**파일:** `Services/WebcamService.cs`
**부모 클래스:** `EventArgs`
**역할:** 웹캠 프레임 수신 이벤트의 데이터 전달

### 프로퍼티

| 프로퍼티 | 타입 | 설명 |
|----------|------|------|
| `Frame` | `Mat` | 캡처된 프레임 이미지 (BGR). **수신자가 반드시 Dispose() 해야 함** |
| `Fps` | `double` | 현재 측정된 FPS |

---

## 4. WebcamService 클래스

**네임스페이스:** `EggClassifier.Services`
**파일:** `Services/WebcamService.cs`
**인터페이스:** `IDisposable`
**역할:** 웹캠에서 프레임을 캡처하여 이벤트로 전달하는 서비스

### 프로퍼티

| 프로퍼티 | 타입 | 기본값 | 설명 |
|----------|------|--------|------|
| `IsRunning` | `bool` | `false` | 캡처 루프 실행 중 여부 |
| `CameraIndex` | `int` | `0` | 웹캠 장치 번호 (0 = 기본 카메라) |
| `FrameWidth` | `int` | `640` | 캡처 해상도 가로 |
| `FrameHeight` | `int` | `480` | 캡처 해상도 세로 |
| `TargetFps` | `int` | `30` | 목표 프레임레이트 |

### 이벤트

| 이벤트 | 타입 | 발생 시점 |
|--------|------|-----------|
| `FrameCaptured` | `EventHandler<FrameCapturedEventArgs>` | 새 프레임이 캡처될 때마다 |
| `ErrorOccurred` | `EventHandler<string>` | 에러 발생 시 (한글 메시지) |

### 메서드

#### `bool Start()`

웹캠 캡처를 시작합니다.

| 항목 | 내용 |
|------|------|
| **반환값** | `true` = 성공, `false` = 실패 |
| **동작** | 1) VideoCapture 생성 (DSHOW API)<br>2) 해상도/FPS/버퍼 설정<br>3) CancellationTokenSource 생성<br>4) Task.Run으로 CaptureLoop 시작 |
| **실패 조건** | 카메라 미연결, 다른 앱이 점유 중 |

---

#### `void Stop()`

웹캠 캡처를 중지합니다.

| 항목 | 내용 |
|------|------|
| **동작** | 1) CancellationToken 취소 신호 전송<br>2) 캡처 태스크 종료 대기 (최대 2초)<br>3) VideoCapture 해제 (lock 보호)<br>4) 리소스 정리 |

---

#### `private void CaptureLoop(CancellationToken ct)` [내부]

백그라운드 스레드에서 실행되는 프레임 캡처 루프입니다.

| 항목 | 내용 |
|------|------|
| **실행 위치** | Task.Run에 의해 ThreadPool 스레드에서 실행 |
| **루프 주기** | `1000 / TargetFps - 5` ms (기본 ~28ms) |
| **종료 조건** | CancellationToken 취소, 카메라 연결 해제 |
| **FPS 계산** | 1초마다 프레임 수 / 경과 시간 |

---

## 5. ClassCountItem 클래스

**네임스페이스:** `EggClassifier.ViewModels`
**파일:** `ViewModels/MainViewModel.cs`
**부모 클래스:** `ObservableObject`
**역할:** UI에서 클래스별 탐지 카운트를 표시하기 위한 바인딩용 모델

### 프로퍼티

| 프로퍼티 | 타입 | 바인딩 | 설명 |
|----------|------|--------|------|
| `ClassName` | `string` | 단방향 | 클래스 한글 이름 |
| `Color` | `SolidColorBrush` | 단방향 | 색상 아이콘용 브러시 |
| `Count` | `int` | 양방향 (알림) | 현재 프레임 탐지 개수 (`SetProperty`로 변경 알림) |

---

## 6. DetectionItem 클래스

**네임스페이스:** `EggClassifier.ViewModels`
**파일:** `ViewModels/MainViewModel.cs`
**부모 클래스:** `ObservableObject`
**역할:** 현재 프레임의 개별 탐지 결과를 UI에 표시하기 위한 바인딩용 모델

### 프로퍼티

| 프로퍼티 | 타입 | 설명 |
|----------|------|------|
| `Label` | `string` | 클래스 한글 이름 |
| `Confidence` | `float` | 신뢰도 (0.0~1.0) |
| `ConfidenceColor` | `SolidColorBrush` | 신뢰도에 따른 색상 (읽기전용, 계산됨) |

### ConfidenceColor 규칙

| 범위 | 색상 | RGB |
|------|------|-----|
| 80% 이상 | 녹색 (높음) | `(76, 175, 80)` |
| 50% ~ 79% | 노랑 (보통) | `(255, 193, 7)` |
| 50% 미만 | 빨강 (낮음) | `(244, 67, 54)` |

---

## 7. MainViewModel 클래스

**네임스페이스:** `EggClassifier.ViewModels`
**파일:** `ViewModels/MainViewModel.cs`
**부모 클래스:** `ObservableObject`
**인터페이스:** `IDisposable`
**역할:** 메인 윈도우의 MVVM ViewModel. UI 상태 관리, 웹캠/추론 조율

### 바인딩 프로퍼티

| 프로퍼티 | 타입 | 기본값 | 설명 |
|----------|------|--------|------|
| `CurrentFrame` | `BitmapSource?` | `null` | 웹캠 영상 프레임 (Image 컨트롤 바인딩) |
| `FpsText` | `string` | `"FPS: --"` | FPS 표시 텍스트 |
| `StatusMessage` | `string` | `"시작 버튼을..."` | 오버레이 안내 메시지 |
| `OverlayVisibility` | `Visibility` | `Visible` | 안내 오버레이 표시 여부 |
| `IsModelLoaded` | `bool` | `false` | 모델 로드 성공 여부 |
| `ModelStatusText` | `string` | `"로딩 중..."` | 모델 상태 텍스트 ("로드됨", "로드 실패", "모델 없음") |
| `ModelStatusColor` | `SolidColorBrush` | 노랑 | 모델 상태 배지 색상 |
| `ModelPath` | `string` | `""` | 로드된 모델 파일 경로 |
| `CanStart` | `bool` | `false` | 시작 버튼 활성화 여부 |
| `CanStop` | `bool` | `false` | 중지 버튼 활성화 여부 |
| `ConfidenceThreshold` | `float` | `0.5` | 신뢰도 필터 임계값 (슬라이더 바인딩) |
| `TotalDetections` | `int` | `0` | 현재 프레임 총 탐지 수 |
| `NoDetectionVisibility` | `Visibility` | `Visible` | "탐지된 객체 없음" 텍스트 표시 여부 |

### 컬렉션

| 프로퍼티 | 타입 | 설명 |
|----------|------|------|
| `ClassCounts` | `ObservableCollection<ClassCountItem>` | 5개 클래스별 탐지 카운트 |
| `CurrentDetections` | `ObservableCollection<DetectionItem>` | 현재 프레임 탐지 목록 (신뢰도 내림차순) |

### 커맨드

#### `StartCommand` → `Start()`

| 항목 | 내용 |
|------|------|
| **바인딩** | 시작 버튼의 `Command` |
| **동작** | 1) WebcamService.Start() 호출<br>2) 성공 시: CanStart=false, CanStop=true, 오버레이 숨김, 카운트 초기화<br>3) 실패 시: 상태 메시지 업데이트 |

#### `StopCommand` → `Stop()`

| 항목 | 내용 |
|------|------|
| **바인딩** | 중지 버튼의 `Command` |
| **동작** | 1) WebcamService.Stop() 호출<br>2) CanStart=true, CanStop=false<br>3) 오버레이 표시, FPS 초기화 |

### 내부 메서드

#### `private void LoadModel()`

앱 시작 시 ONNX 모델을 탐색하고 로드합니다.

| 항목 | 내용 |
|------|------|
| **탐색 순서** | 1) `bin/Models/egg_classifier.onnx`<br>2) `bin/egg_classifier.onnx`<br>3) 프로젝트 소스 `Models/`<br>4) 상위 `models/` |
| **모델 없을 때** | StatusText="모델 없음", 빨간 배지, 웹캠만 사용 가능 |

#### `private void OnFrameCaptured(object?, FrameCapturedEventArgs)`

웹캠 프레임 수신 시 호출되는 이벤트 핸들러입니다.

| 항목 | 내용 |
|------|------|
| **실행 스레드** | 캡처 스레드 (ThreadPool) |
| **동작** | 1) YoloDetector.Detect() 호출<br>2) DrawDetections()로 바운딩박스 렌더링<br>3) Mat → BitmapSource 변환 + Freeze<br>4) **BeginInvoke**로 UI 스레드에 비동기 업데이트<br>5) Mat Dispose |

#### `private void UpdateDetectionResults(List<Detection>)`

탐지 결과를 UI 컬렉션에 반영합니다.

| 항목 | 내용 |
|------|------|
| **실행 스레드** | UI 스레드 (Dispatcher 경유) |
| **동작** | 1) CurrentDetections 갱신 (신뢰도 내림차순)<br>2) NoDetectionVisibility 토글<br>3) ClassCounts 클래스별 카운트 업데이트<br>4) TotalDetections 합산 |

---

## 8. Python 스크립트

### 8.1 run_train.py — 모델 학습

| 항목 | 내용 |
|------|------|
| **목적** | YOLOv8n 모델을 계란 데이터셋으로 학습 |
| **입력** | `data/data.yaml` (데이터셋 설정), `yolov8n.pt` (사전학습 가중치) |
| **출력** | `runs/detect/egg_classifier/weights/best.pt` (최적 모델) |

#### 하이퍼파라미터

| 파라미터 | 값 | 설명 |
|----------|----|------|
| `data` | `'data/data.yaml'` | 데이터셋 설정 YAML |
| `epochs` | `50` | 전체 학습 반복 횟수 |
| `imgsz` | `640` | 입력 이미지 크기 |
| `batch` | `32` | 배치 크기 |
| `device` | `0` | GPU 장치 번호 |
| `patience` | `10` | Early stopping 인내값 |
| `workers` | `4` | 데이터 로딩 병렬 워커 수 |
| `optimizer` | `'SGD'` | 옵티마이저 (Muon → SGD 변경, CUDA BF16 호환성) |

### 8.2 export_onnx.py — ONNX 내보내기

| 항목 | 내용 |
|------|------|
| **목적** | 학습된 PyTorch 모델(.pt)을 ONNX 형식으로 변환 |
| **입력** | `best.pt` (학습 완료된 가중치) |
| **출력** | `best.onnx` (C# 앱에서 사용할 모델) |

#### 내보내기 옵션

| 옵션 | 값 | 설명 |
|------|----|------|
| `format` | `'onnx'` | 출력 포맷 |
| `imgsz` | `640` | 입력 텐서 크기 |
| `simplify` | `True` | ONNX 그래프 단순화 (불필요한 노드 제거) |
| `opset` | `12` | ONNX 연산자 세트 버전 |

### 8.3 test_inference.py — 추론 테스트

| 항목 | 내용 |
|------|------|
| **목적** | 학습된 모델의 검증 이미지 탐지 결과 확인 |
| **입력** | 검증 이미지 5장 (`data/images/val/`) |
| **출력** | 콘솔에 클래스별 신뢰도 출력 + 결과 이미지 저장 (`runs/test/`) |

---

## 9. XAML 리소스 (App.xaml)

### 색상 리소스

| 키 | 색상 코드 | 용도 |
|----|-----------|------|
| `PrimaryColor` | `#2196F3` | 주요 액센트 (파란색) |
| `SecondaryColor` | `#FF9800` | 보조 액센트 (주황) |
| `SuccessColor` | `#4CAF50` | 성공 상태 (녹색) |
| `DangerColor` | `#F44336` | 위험/에러 (빨강) |
| `WarningColor` | `#FFC107` | 경고 (노랑) |
| `BackgroundColor` | `#1E1E1E` | 앱 배경 (다크) |
| `SurfaceColor` | `#2D2D2D` | 카드 배경 |
| `TextColor` | `#FFFFFF` | 주요 텍스트 (흰색) |
| `TextSecondaryColor` | `#B0B0B0` | 보조 텍스트 (회색) |

### 스타일

| 스타일 키 | 대상 | 설명 |
|-----------|------|------|
| `PrimaryButtonStyle` | `Button` | 파란색 배경, 둥근 모서리, hover 시 어두워짐, disabled 시 회색 |
| `DangerButtonStyle` | `Button` | 빨간색 배경 (PrimaryButtonStyle 상속) |
| `CardStyle` | `Border` | 카드 컨테이너 (둥근 모서리, SurfaceColor 배경, 15px 패딩) |
