# 코드 라인별 상세 해설

모든 소스 파일을 라인 단위로 해설합니다.
초보자도 각 줄이 무엇을 하는지 이해할 수 있도록 작성되었습니다.

---

## 목차

1. [YoloDetector.cs — ONNX 추론 엔진](#1-yolodetectorcs)
2. [WebcamService.cs — 웹캠 캡처 서비스](#2-webcamservicecs)
3. [Core/ — 네비게이션 인프라](#3-core--네비게이션-인프라)
4. [Models/ — DTO 클래스](#4-models--dto-클래스)
5. [Services/ — 인터페이스 + 래퍼](#5-services--인터페이스--래퍼)
6. [DetectionViewModel.cs — 계란 분류 ViewModel](#6-detectionviewmodelcs)
7. [MainViewModel.cs — 네비게이션 ViewModel](#7-mainviewmodelcs)
8. [MainWindow.xaml — 셸 UI](#8-mainwindowxaml)
9. [MainWindow.xaml.cs — 코드비하인드](#9-mainwindowxamlcs)
10. [App.xaml — 전역 리소스 + DataTemplate](#10-appxaml)
11. [App.xaml.cs — DI 컨테이너 + 앱 시작](#11-appxamlcs)
12. [EggClassifier.csproj — 프로젝트 설정](#12-eggclassifiercsproj)
13. [run_train.py — 모델 학습](#13-run_trainpy)
14. [export_onnx.py — ONNX 내보내기](#14-export_onnxpy)
15. [test_inference.py — 추론 테스트](#15-test_inferencepy)

---

## 1. YoloDetector.cs

이 파일은 프로젝트의 **핵심 AI 엔진**입니다.
ONNX 모델을 로드하고, 이미지를 전처리하고, 추론을 실행하고, 결과를 후처리합니다.

> **참고:** 이전 버전에서는 이 파일 안에 `Detection` 클래스가 함께 정의되어 있었지만,
> 모듈화 리팩토링 이후 Detection 클래스는 `Models/Detection.cs`로 분리되었습니다.

```csharp
using Microsoft.ML.OnnxRuntime;           // ONNX 모델 로드/추론 라이브러리
using Microsoft.ML.OnnxRuntime.Tensors;   // 텐서(다차원 배열) 자료구조
using OpenCvSharp;                        // OpenCV C# 래퍼 (이미지 처리)
using System;                             // 기본 시스템 타입 (Math, Exception 등)
using System.Collections.Generic;         // List<T>, Dictionary 등 컬렉션
using System.Linq;                        // LINQ 쿼리 (.First(), .Select() 등)
```

### YoloDetector 클래스 (추론 엔진 본체)

```csharp
    public class YoloDetector : IDisposable   // IDisposable: 사용 후 리소스 해제 가능
    {
        private InferenceSession? _session;   // ONNX 추론 세션 (모델이 로드된 상태)
                                              // '?'는 null 가능 타입 (아직 로드 안 됐을 수 있음)
        private readonly int _inputWidth = 640;   // 모델 입력 가로 크기 (학습 시 설정한 값)
        private readonly int _inputHeight = 640;  // 모델 입력 세로 크기 (640x640 정사각형)
        private bool _disposed;               // Dispose()가 이미 호출되었는지 추적

        // ── Letterbox 전처리 정보 (좌표 복원용) ──
        // Letterbox: 종횡비를 유지하면서 640x640에 맞추고 남는 부분을 패딩하는 기법
        private float _letterboxScale;        // 원본→640 변환 시 사용된 축소/확대 비율
        private int _letterboxPadX;           // 좌우 패딩 크기 (픽셀)
        private int _letterboxPadY;           // 상하 패딩 크기 (픽셀)
```

### 클래스/색상 상수 정의

```csharp
        // ── 5개 계란 품질 클래스 이름 ──
        public static readonly string[] ClassNames = new[]
        {
            "정상",        // 인덱스 0: 정상적인 계란
            "크랙",        // 인덱스 1: 껍데기에 금이 간 계란
            "이물질",      // 인덱스 2: 이물질이 묻은 계란
            "탈색",        // 인덱스 3: 색이 변한 계란
            "외형이상"     // 인덱스 4: 모양이 일그러진 계란
        };
        // 주의: 이 순서는 학습 시 data.yaml의 클래스 순서와 반드시 일치해야 함

        // ── 클래스별 바운딩박스 표시 색상 (BGR 형식) ──
        // BGR = Blue-Green-Red (OpenCV 기본 색상 순서, RGB와 반대)
        public static readonly Scalar[] ClassColors = new[]
        {
            new Scalar(0, 255, 0),      // 정상: 녹색     (B=0, G=255, R=0)
            new Scalar(0, 0, 255),      // 크랙: 빨강     (B=0, G=0, R=255)
            new Scalar(255, 0, 255),    // 이물질: 마젠타  (B=255, G=0, R=255)
            new Scalar(0, 255, 255),    // 탈색: 노랑     (B=0, G=255, R=255)
            new Scalar(255, 128, 0)     // 외형이상: 주황  (B=255, G=128, R=0)
        };

        // _session이 null이 아니면 모델이 로드된 상태
        public bool IsLoaded => _session != null;
```

### LoadModel — 모델 로드

```csharp
        public bool LoadModel(string modelPath)   // modelPath: ONNX 파일 경로
        {
            try
            {
                // ── 1단계: 추론 세션 옵션 설정 ──
                var options = new SessionOptions();
                options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                // ORT_ENABLE_ALL: 모든 그래프 최적화 적용
                //   - 불필요한 연산 제거
                //   - 연산 결합 (여러 연산을 하나로 합침)
                //   - 메모리 할당 최적화

                // ── 2단계: GPU 가속 시도 ──
                try
                {
                    options.AppendExecutionProvider_CUDA(0);
                    // CUDA 프로바이더를 장치 0번(첫 번째 GPU)에 등록
                    // NVIDIA GPU + CUDA 설치되어 있으면 GPU 추론 사용
                }
                catch
                {
                    // CUDA가 설치 안 되어있거나 GPU가 없으면 여기로 옴
                    // → 자동으로 CPU 모드로 폴백 (별도 설정 불필요)
                    Console.WriteLine("CUDA not available, using CPU");
                }

                // ── 3단계: 모델 로드 ──
                _session = new InferenceSession(modelPath, options);
                // 이 시점에서 ONNX 파일을 읽고 신경망 구조가 메모리에 올라감

                // 입출력 텐서 이름 확인 (디버깅용)
                Console.WriteLine("Model loaded successfully");
                Console.WriteLine($"Input: {_session.InputMetadata.First().Key}");
                // 출력 예: "Input: images"
                Console.WriteLine($"Output: {_session.OutputMetadata.First().Key}");
                // 출력 예: "Output: output0"

                return true;   // 로드 성공
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load model: {ex.Message}");
                return false;  // 로드 실패 (파일 없음, 손상 등)
            }
        }
```

### Detect — 메인 추론 메서드

```csharp
        public List<Detection> Detect(Mat image,
            float confidenceThreshold = 0.5f,   // 최소 신뢰도 (50% 미만은 무시)
            float nmsThreshold = 0.45f)          // NMS 임계값 (45% 이상 겹치면 중복 제거)
        {
            // ── 유효성 검사 ──
            if (_session == null || image.Empty())
                return new List<Detection>();    // 모델 미로드 또는 빈 이미지면 빈 리스트 반환

            var originalWidth = image.Width;     // 원본 이미지 가로 (예: 640)
            var originalHeight = image.Height;   // 원본 이미지 세로 (예: 480)

            // ── 전처리: 이미지 → 텐서 ──
            var inputTensor = Preprocess(image);
            // 결과: [1, 3, 640, 640] 형태의 float 텐서
            //        1=배치, 3=RGB채널, 640x640=크기

            // ── 추론 실행 ──
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(
                    _session.InputMetadata.First().Key,  // 입력 텐서 이름 ("images")
                    inputTensor)                         // 전처리된 텐서 데이터
            };

            using var results = _session.Run(inputs);    // ★ 여기서 실제 AI 추론이 실행됨
            var output = results.First().AsTensor<float>();
            // output shape: [1, 9, 8400]
            //   9 = 4(바운딩박스 좌표) + 5(클래스 신뢰도)
            //   8400 = 탐지 후보 수

            // ── 후처리: 텐서 → Detection 리스트 ──
            var detections = Postprocess(output, originalWidth, originalHeight,
                confidenceThreshold, nmsThreshold);

            return detections;
        }
```

### Preprocess — Letterbox 전처리

```csharp
        private DenseTensor<float> Preprocess(Mat image)
        {
            int srcW = image.Width;    // 원본 가로 (예: 640)
            int srcH = image.Height;   // 원본 세로 (예: 480)

            // ── 1단계: Letterbox 스케일 계산 ──
            // 종횡비를 유지하면서 640x640 안에 들어갈 수 있는 최대 크기를 계산
            float scale = Math.Min(
                (float)_inputWidth / srcW,    // 가로 기준 스케일
                (float)_inputHeight / srcH);  // 세로 기준 스케일
            // 둘 중 작은 값을 사용 → 이미지가 640x640을 넘지 않도록 보장
            //
            // 예시: 800x400 이미지
            //   가로 스케일 = 640/800 = 0.8
            //   세로 스케일 = 640/400 = 1.6
            //   → min(0.8, 1.6) = 0.8 사용
            //   → 새 크기: 640x320

            int newW = (int)(srcW * scale);   // 리사이즈 후 가로
            int newH = (int)(srcH * scale);   // 리사이즈 후 세로

            // ── 2단계: 패딩 크기 계산 ──
            int padX = (_inputWidth - newW) / 2;    // 좌우 균등 패딩
            int padY = (_inputHeight - newH) / 2;   // 상하 균등 패딩
            // 예시: 640x320 → padX=0, padY=(640-320)/2=160

            // 후처리에서 좌표 복원할 때 필요하므로 멤버 변수에 저장
            _letterboxScale = scale;
            _letterboxPadX = padX;
            _letterboxPadY = padY;

            // ── 3단계: 이미지 리사이즈 (종횡비 유지) ──
            var resized = new Mat();
            Cv2.Resize(image, resized, new Size(newW, newH));

            // ── 4단계: 640x640 캔버스에 배치 (회색 패딩) ──
            var letterboxed = new Mat(_inputHeight, _inputWidth, MatType.CV_8UC3,
                new Scalar(114, 114, 114));
            // 114 회색 = ultralytics 프레임워크의 기본 패딩 색상
            // 학습 시와 동일한 값을 사용해야 정확도가 유지됨

            resized.CopyTo(letterboxed[new Rect(padX, padY, newW, newH)]);
            // letterboxed 이미지의 (padX, padY) 위치에 리사이즈된 이미지를 복사
            // 나머지 영역은 114 회색으로 채워진 상태

            // ── 5단계: BGR → RGB 변환 ──
            var rgb = new Mat();
            Cv2.CvtColor(letterboxed, rgb, ColorConversionCodes.BGR2RGB);
            // OpenCV는 BGR 순서를 사용하지만, 신경망은 RGB 순서를 기대함

            // ── 6단계: 정규화 (0~255 → 0.0~1.0) ──
            var floatMat = new Mat();
            rgb.ConvertTo(floatMat, MatType.CV_32FC3, 1.0 / 255.0);
            // ConvertTo: 모든 픽셀에 (1/255)를 곱해서 0~1 범위로 변환
            // CV_32FC3 = 32비트 float, 3채널
            // 기존 픽셀별 루프 대비 ~10배 빠름 (OpenCV가 내부적으로 SIMD 최적화)

            // ── 7단계: 채널 분리 → NCHW 텐서 변환 ──
            var channels = Cv2.Split(floatMat);
            // channels[0] = R채널, channels[1] = G채널, channels[2] = B채널

            var tensor = new DenseTensor<float>(new[] { 1, 3, _inputHeight, _inputWidth });
            // NCHW 형식: [배치=1, 채널=3, 높이=640, 너비=640]
            // N=batch, C=channel, H=height, W=width

            for (int c = 0; c < 3; c++)
            {
                channels[c].GetArray(out float[] channelData);
                // 한 채널의 모든 픽셀을 1차원 float 배열로 추출

                for (int i = 0; i < channelData.Length; i++)
                {
                    int y = i / _inputWidth;    // 1차원 인덱스 → 2차원 행 좌표
                    int x = i % _inputWidth;    // 1차원 인덱스 → 2차원 열 좌표
                    tensor[0, c, y, x] = channelData[i];
                }
                channels[c].Dispose();   // 채널 Mat 해제
            }

            // ── 중간 Mat 객체 메모리 해제 ──
            resized.Dispose();
            letterboxed.Dispose();
            rgb.Dispose();
            floatMat.Dispose();

            return tensor;   // [1, 3, 640, 640] 텐서 반환
        }
```

### Postprocess — 출력 후처리

```csharp
        private List<Detection> Postprocess(Tensor<float> output,
            int originalWidth, int originalHeight,
            float confidenceThreshold, float nmsThreshold)
        {
            var detections = new List<Detection>();   // 최종 결과 리스트
            var boxes = new List<Rect>();             // 후보 바운딩박스들
            var confidences = new List<float>();      // 후보 신뢰도들
            var classIds = new List<int>();           // 후보 클래스 ID들

            // ── 출력 텐서 shape 분석 ──
            // YOLOv8은 모델에 따라 두 가지 형태로 출력할 수 있음:
            //   [1, 9, 8400] — transposed 형식 (일반적)
            //   [1, 8400, 9] — 비-transposed 형식
            var dims = output.Dimensions.ToArray();   // 예: [1, 9, 8400]
            int numClasses = ClassNames.Length;        // 5 (계란 5분류)
            int numDetections;    // 탐지 후보 수 (보통 8400)
            bool isTransposed;    // 데이터 배치 방향 플래그

            if (dims.Length == 3)
            {
                if (dims[1] == 4 + numClasses)   // dims[1]=9, 4+5=9 → 일치!
                {
                    // [1, 9, 8400] 형식: 각 열이 하나의 탐지 후보
                    numDetections = dims[2];      // 8400
                    isTransposed = true;
                }
                else if (dims[2] == 4 + numClasses)
                {
                    // [1, 8400, 9] 형식: 각 행이 하나의 탐지 후보
                    numDetections = dims[1];      // 8400
                    isTransposed = false;
                }
                else
                {
                    // 다른 클래스 수의 모델 (예: COCO 80클래스)
                    numDetections = dims[1] > dims[2] ? dims[2] : dims[1];
                    isTransposed = dims[1] < dims[2];
                }
            }
            else
            {
                return detections;   // 예상 밖의 shape → 빈 결과 반환
            }

            // ── 8400개 후보를 순회하며 유효한 탐지 필터링 ──
            for (int i = 0; i < numDetections; i++)
            {
                float maxConfidence = 0;   // 이 후보의 최대 신뢰도
                int maxClassId = 0;        // 최대 신뢰도를 가진 클래스 ID

                // 5개 클래스의 신뢰도를 비교하여 가장 높은 것을 선택
                for (int c = 0; c < numClasses; c++)
                {
                    float conf = isTransposed
                        ? output[0, 4 + c, i]    // [1, 9, 8400] 형식에서 접근
                        : output[0, i, 4 + c];   // [1, 8400, 9] 형식에서 접근
                    // 4 + c: 앞 4개는 bbox 좌표이므로 인덱스 4부터가 클래스 신뢰도

                    if (conf > maxConfidence)
                    {
                        maxConfidence = conf;
                        maxClassId = c;
                    }
                }

                // 임계값 미달이면 스킵 (의미 없는 탐지 제거)
                if (maxConfidence < confidenceThreshold)
                    continue;

                // ── 바운딩박스 추출 (cx, cy, w, h — 640x640 좌표계) ──
                float cx, cy, w, h;
                if (isTransposed)
                {
                    cx = output[0, 0, i];   // 중심 X
                    cy = output[0, 1, i];   // 중심 Y
                    w  = output[0, 2, i];   // 너비
                    h  = output[0, 3, i];   // 높이
                }
                else
                {
                    cx = output[0, i, 0];
                    cy = output[0, i, 1];
                    w  = output[0, i, 2];
                    h  = output[0, i, 3];
                }

                // ── Letterbox 좌표 → 원본 이미지 좌표로 복원 ──
                // 패딩을 빼고, 스케일을 역으로 나눔
                float x1f = (cx - w / 2 - _letterboxPadX) / _letterboxScale;
                float y1f = (cy - h / 2 - _letterboxPadY) / _letterboxScale;
                float bwf = w / _letterboxScale;
                float bhf = h / _letterboxScale;
                //
                // 예시: cx=400, w=100, padX=0, scale=0.8
                //   x1 = (400 - 50 - 0) / 0.8 = 437.5 (원본 좌표)
                //   bw = 100 / 0.8 = 125 (원본 크기)

                int x1 = (int)x1f;
                int y1 = (int)y1f;
                int bw = (int)bwf;
                int bh = (int)bhf;

                // ── 경계 클리핑 (이미지 밖으로 나가지 않도록) ──
                x1 = Math.Max(0, Math.Min(x1, originalWidth - 1));
                y1 = Math.Max(0, Math.Min(y1, originalHeight - 1));
                bw = Math.Min(bw, originalWidth - x1);
                bh = Math.Min(bh, originalHeight - y1);

                // 후보 리스트에 추가
                boxes.Add(new Rect(x1, y1, bw, bh));
                confidences.Add(maxConfidence);
                classIds.Add(maxClassId);
            }

            // ── NMS (Non-Maximum Suppression) 적용 ──
            // 같은 객체에 대한 중복 박스를 제거
            if (boxes.Count > 0)
            {
                var indices = ApplyNMS(boxes, confidences, nmsThreshold);

                foreach (int idx in indices)
                {
                    detections.Add(new Detection
                    {
                        ClassId = classIds[idx],
                        ClassName = ClassNames[classIds[idx]],
                        Confidence = confidences[idx],
                        BoundingBox = boxes[idx]
                    });
                }
            }

            return detections;
        }
```

### ApplyNMS — 중복 박스 제거

```csharp
        private static List<int> ApplyNMS(List<Rect> boxes, List<float> confidences,
            float nmsThreshold)
        {
            var indices = new List<int>();   // 살아남은 박스의 인덱스

            // 신뢰도가 높은 순으로 인덱스 정렬
            var sortedIndices = confidences
                .Select((conf, idx) => new { Confidence = conf, Index = idx })
                .OrderByDescending(x => x.Confidence)   // 95%, 92%, 87%, ...
                .Select(x => x.Index)
                .ToList();

            var suppressed = new bool[boxes.Count];   // 억제(제거)된 박스 표시

            foreach (int i in sortedIndices)
            {
                if (suppressed[i])   // 이미 제거된 박스는 스킵
                    continue;

                indices.Add(i);   // 이 박스는 살아남음 (가장 높은 신뢰도)

                // 이 박스와 나머지 모든 박스의 겹침 정도(IoU) 확인
                for (int j = 0; j < boxes.Count; j++)
                {
                    if (i == j || suppressed[j])
                        continue;

                    float iou = CalculateIoU(boxes[i], boxes[j]);
                    if (iou > nmsThreshold)   // 45% 이상 겹치면 → 같은 객체로 간주
                    {
                        suppressed[j] = true;   // 낮은 신뢰도 박스 제거
                    }
                }
            }

            return indices;
        }
```

### CalculateIoU — 겹침 비율 계산

```csharp
        private static float CalculateIoU(Rect a, Rect b)
        {
            // 두 박스의 교집합 영역 계산
            int x1 = Math.Max(a.X, b.X);                       // 교집합 왼쪽
            int y1 = Math.Max(a.Y, b.Y);                       // 교집합 위쪽
            int x2 = Math.Min(a.X + a.Width, b.X + b.Width);   // 교집합 오른쪽
            int y2 = Math.Min(a.Y + a.Height, b.Y + b.Height); // 교집합 아래쪽

            int intersectionWidth = Math.Max(0, x2 - x1);      // 교집합 가로 (음수면 0)
            int intersectionHeight = Math.Max(0, y2 - y1);     // 교집합 세로 (음수면 0)
            int intersectionArea = intersectionWidth * intersectionHeight;  // 교집합 넓이

            int areaA = a.Width * a.Height;   // A 박스 넓이
            int areaB = b.Width * b.Height;   // B 박스 넓이
            int unionArea = areaA + areaB - intersectionArea;   // 합집합 넓이

            if (unionArea <= 0)
                return 0;   // 0으로 나누기 방지

            return (float)intersectionArea / unionArea;
            // IoU = 교집합 / 합집합
            // 0.0 = 전혀 안 겹침, 1.0 = 완전히 겹침
        }
```

### DrawDetections — 바운딩박스 시각화

```csharp
        public static void DrawDetections(Mat image, List<Detection> detections)
        {
            foreach (var det in detections)
            {
                var color = ClassColors[det.ClassId % ClassColors.Length];
                // 클래스에 해당하는 색상 선택 (% 연산으로 배열 범위 초과 방지)

                var rect = det.BoundingBox;

                // 바운딩박스 직사각형 그리기 (두께 2px)
                Cv2.Rectangle(image, rect, color, 2);

                // ── 라벨 텍스트 준비 ──
                string label = $"{det.ClassName} {det.Confidence:P0}";
                // 예: "크랙 92%"

                var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex,
                    0.6, 2, out int baseline);
                // 텍스트의 픽셀 크기를 미리 측정 (라벨 배경 크기 결정용)

                // 라벨 배경 위치 (바운딩박스 위에 표시)
                var labelRect = new Rect(
                    rect.X,                            // 박스 왼쪽 정렬
                    rect.Y - textSize.Height - 10,     // 박스 위에 위치
                    textSize.Width + 10,               // 텍스트 너비 + 여백
                    textSize.Height + 10               // 텍스트 높이 + 여백
                );

                // 라벨이 이미지 상단을 벗어나면 박스 아래에 표시
                if (labelRect.Y < 0)
                {
                    labelRect.Y = rect.Y + rect.Height;
                }

                // 라벨 배경 (색으로 채운 사각형)
                Cv2.Rectangle(image, labelRect, color, -1);   // -1 = 내부 채우기

                // 라벨 텍스트 (흰색)
                Cv2.PutText(image, label,
                    new Point(labelRect.X + 5, labelRect.Y + labelRect.Height - 5),
                    HersheyFonts.HersheySimplex, 0.6, Scalar.White, 2);
            }
        }
```

### Dispose — 리소스 해제

```csharp
        public void Dispose()
        {
            if (!_disposed)               // 중복 호출 방지
            {
                _session?.Dispose();      // ONNX 세션 해제 (GPU 메모리 반환)
                _session = null;          // null로 설정하여 IsLoaded = false
                _disposed = true;         // 해제 완료 표시
            }
        }
    }
}
```

---

## 2. WebcamService.cs

이 파일은 웹캠에서 프레임을 캡처하여 이벤트로 전달하는 서비스입니다.

```csharp
using OpenCvSharp;          // VideoCapture 등 OpenCV 기능
using System;
using System.Threading;     // CancellationTokenSource, Thread.Sleep
using System.Threading.Tasks;  // Task.Run (백그라운드 실행)
```

### FrameCapturedEventArgs — 이벤트 데이터

```csharp
namespace EggClassifier.Services
{
    public class FrameCapturedEventArgs : EventArgs
    {
        public Mat Frame { get; }     // 캡처된 프레임 (BGR 이미지)
        public double Fps { get; }    // 현재 측정된 FPS

        public FrameCapturedEventArgs(Mat frame, double fps)
        {
            Frame = frame;   // 이 Mat은 Clone()된 복사본
            Fps = fps;
        }
    }
```

### WebcamService 본체

```csharp
    public class WebcamService : IDisposable
    {
        private VideoCapture? _capture;            // OpenCV 웹캠 캡처 객체
        private CancellationTokenSource? _cts;     // 취소 신호 관리자
        private Task? _captureTask;                // 캡처 루프가 실행되는 백그라운드 태스크
        private bool _disposed;
        private readonly object _lock = new();     // VideoCapture 접근 동기화용 락

        // ── 이벤트 ──
        public event EventHandler<FrameCapturedEventArgs>? FrameCaptured;
        // → 새 프레임이 준비될 때마다 발생

        public event EventHandler<string>? ErrorOccurred;
        // → 에러 발생 시 한글 메시지와 함께 발생

        // ── 프로퍼티 ──
        public bool IsRunning => _captureTask != null && !_captureTask.IsCompleted;
        // 캡처 태스크가 존재하고 아직 실행 중이면 true

        public int CameraIndex { get; set; } = 0;      // 웹캠 번호 (0=기본)
        public int FrameWidth { get; set; } = 640;      // 캡처 해상도 가로
        public int FrameHeight { get; set; } = 480;     // 캡처 해상도 세로
        public int TargetFps { get; set; } = 30;        // 목표 FPS
```

### Start — 캡처 시작

```csharp
        public bool Start()
        {
            if (IsRunning)         // 이미 실행 중이면 중복 시작 방지
                return true;

            try
            {
                // ── 1단계: VideoCapture 생성 ──
                _capture = new VideoCapture(CameraIndex, VideoCaptureAPIs.DSHOW);
                // DSHOW (DirectShow): Windows에서 가장 안정적인 웹캠 API
                // 대안: MSMF, CAP_ANY 등

                if (!_capture.IsOpened())   // 카메라 열기 실패
                {
                    ErrorOccurred?.Invoke(this, "웹캠을 열 수 없습니다. 카메라가 연결되어 있는지 확인하세요.");
                    return false;
                }

                // ── 2단계: 캡처 설정 ──
                _capture.Set(VideoCaptureProperties.FrameWidth, FrameWidth);    // 640
                _capture.Set(VideoCaptureProperties.FrameHeight, FrameHeight);  // 480
                _capture.Set(VideoCaptureProperties.Fps, TargetFps);            // 30
                _capture.Set(VideoCaptureProperties.BufferSize, 1);
                // BufferSize=1: 버퍼에 프레임 1개만 유지
                // → 오래된 프레임이 쌓이지 않아 지연(latency) 최소화

                // ── 3단계: 캡처 루프 시작 ──
                _cts = new CancellationTokenSource();
                _captureTask = Task.Run(() => CaptureLoop(_cts.Token));
                // Task.Run: ThreadPool 스레드에서 CaptureLoop 실행
                // CancellationToken: 나중에 Stop()에서 취소 신호를 보낼 수 있음

                Console.WriteLine($"Webcam started: {_capture.FrameWidth}x{_capture.FrameHeight} @ {_capture.Fps}fps");
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"웹캠 시작 실패: {ex.Message}");
                return false;
            }
        }
```

### Stop — 캡처 중지

```csharp
        public void Stop()
        {
            if (!IsRunning)   // 실행 중이 아니면 아무것도 안 함
                return;

            _cts?.Cancel();   // 취소 신호 전송 → CaptureLoop의 while 조건이 false가 됨

            try
            {
                _captureTask?.Wait(TimeSpan.FromSeconds(2));
                // 캡처 루프가 종료될 때까지 최대 2초 대기
            }
            catch (AggregateException)
            {
                // 취소로 인한 예외는 정상적인 종료이므로 무시
            }

            lock (_lock)   // VideoCapture 접근을 락으로 보호
            {
                _capture?.Release();   // 웹캠 장치 해제
                _capture?.Dispose();   // 메모리 해제
                _capture = null;
            }

            _cts?.Dispose();      // CancellationTokenSource 해제
            _cts = null;
            _captureTask = null;  // 태스크 참조 제거

            Console.WriteLine("Webcam stopped");
        }
```

### CaptureLoop — 프레임 캡처 루프

```csharp
        private void CaptureLoop(CancellationToken ct)
        {
            var frame = new Mat();   // 프레임 버퍼 (재사용)
            var sw = System.Diagnostics.Stopwatch.StartNew();  // FPS 측정용 타이머
            int frameCount = 0;      // 1초간 캡처된 프레임 수
            double fps = 0;          // 계산된 FPS

            int frameInterval = 1000 / TargetFps;  // 프레임 간격 (ms) = 1000/30 ≈ 33ms

            while (!ct.IsCancellationRequested)  // 취소 신호가 올 때까지 반복
            {
                try
                {
                    lock (_lock)   // VideoCapture 접근을 락으로 보호
                    {
                        if (_capture == null || !_capture.IsOpened())
                            break;  // 카메라가 닫혔으면 루프 종료

                        if (!_capture.Read(frame) || frame.Empty())
                        {
                            // 프레임 읽기 실패 (일시적) → 잠시 대기 후 재시도
                            Thread.Sleep(10);
                            continue;
                        }
                    }

                    frameCount++;

                    // ── FPS 계산 (1초마다 갱신) ──
                    if (sw.ElapsedMilliseconds >= 1000)
                    {
                        fps = frameCount * 1000.0 / sw.ElapsedMilliseconds;
                        // 예: 30프레임 / 1002ms = 29.9 FPS
                        frameCount = 0;
                        sw.Restart();  // 타이머 리셋
                    }

                    // ── 프레임 복사 후 이벤트 발생 ──
                    var frameCopy = frame.Clone();
                    // Clone(): 프레임의 독립적인 복사본 생성
                    // 원본 frame은 다음 Read()에서 덮어쓰이므로 복사 필수

                    FrameCaptured?.Invoke(this, new FrameCapturedEventArgs(frameCopy, fps));
                    // 구독자(DetectionViewModel)에게 프레임 전달
                    // 주의: 구독자가 frameCopy.Dispose()를 호출해야 메모리 누수 방지

                    // ── 프레임레이트 조절 ──
                    Thread.Sleep(Math.Max(1, frameInterval - 5));
                    // 33ms - 5ms = 28ms 대기
                    // -5ms: 캡처/처리 시간을 고려한 보정
                    // Math.Max(1, ...): 최소 1ms 대기 (CPU 100% 방지)
                }
                catch (OperationCanceledException)
                {
                    break;   // 정상적인 취소 → 루프 종료
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Capture error: {ex.Message}");
                    ErrorOccurred?.Invoke(this, $"캡처 오류: {ex.Message}");
                    Thread.Sleep(100);  // 에러 시 100ms 대기 후 재시도
                }
            }

            frame.Dispose();   // 프레임 버퍼 해제
        }
```

### Dispose

```csharp
        public void Dispose()
        {
            if (!_disposed)
            {
                Stop();            // 캡처 중지 + 리소스 해제
                _disposed = true;
            }
        }
    }
}
```

---

## 3. Core/ — 네비게이션 인프라

`Core/` 폴더에는 페이지 네비게이션을 위한 기반 클래스와 서비스가 정의되어 있습니다.
모든 Feature ViewModel은 `ViewModelBase`를 상속하고, `NavigationService`를 통해 전환됩니다.

### Core/ViewModelBase.cs — ViewModel 부모 클래스

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace EggClassifier.Core
{
    public class ViewModelBase : ObservableObject
    // 모든 Feature ViewModel의 부모 클래스
    // ObservableObject를 상속하여 PropertyChanged 자동 지원
    {
        public virtual void OnNavigatedTo() { }
        // 페이지 진입 시 호출 — override하여 이벤트 구독, 데이터 로드 등 수행
        // 예: DetectionViewModel에서 웹캠 이벤트 구독

        public virtual void OnNavigatedFrom() { }
        // 페이지 이탈 시 호출 — override하여 웹캠 정지, 이벤트 해제 등 수행
        // 예: DetectionViewModel에서 웹캠 Stop + 이벤트 해제
    }
}
```

> **핵심 포인트:**
> - `OnNavigatedTo` / `OnNavigatedFrom`은 생성자/Dispose 대신 사용됨
> - NavigationService가 페이지 전환 시 자동으로 호출해줌
> - Feature ViewModel은 이 두 메서드를 override하여 라이프사이클을 관리

### Core/INavigationService.cs — 네비게이션 인터페이스

```csharp
using System.ComponentModel;

namespace EggClassifier.Core
{
    public interface INavigationService : INotifyPropertyChanged
    // INotifyPropertyChanged: CurrentView 변경 시 UI에 알림 가능
    {
        ViewModelBase? CurrentView { get; }
        // 현재 활성화된 ViewModel — ContentControl에 바인딩됨
        // null이면 아무 페이지도 표시되지 않음

        void NavigateTo<T>() where T : ViewModelBase;
        // 제네릭으로 대상 ViewModel 타입 지정 → DI에서 resolve하여 전환
        // 예: NavigateTo<DetectionViewModel>()
    }
}
```

### Core/NavigationService.cs — 네비게이션 구현체

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace EggClassifier.Core
{
    public class NavigationService : ObservableObject, INavigationService
    // ObservableObject: PropertyChanged 자동 지원
    // INavigationService: 인터페이스 구현
    {
        private readonly IServiceProvider _serviceProvider;
        // DI 컨테이너 참조 — ViewModel resolve에 사용

        private ViewModelBase? _currentView;
        public ViewModelBase? CurrentView
        {
            get => _currentView;
            set => SetProperty(ref _currentView, value);
            // SetProperty: 값 변경 시 PropertyChanged 발생
            // → ContentControl이 새 ViewModel을 감지하고 DataTemplate으로 View 렌더링
        }

        public NavigationService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            // App.xaml.cs에서 DI 등록 시 IServiceProvider가 자동 주입됨
        }

        public void NavigateTo<T>() where T : ViewModelBase
        {
            var oldView = CurrentView;
            oldView?.OnNavigatedFrom();
            // 이전 페이지의 정리 작업 (예: 웹캠 정지)
            // null이면 아무것도 안 함 (앱 최초 시작 시)

            var newView = (T)_serviceProvider.GetService(typeof(T))!;
            // DI 컨테이너에서 새 ViewModel 인스턴스를 가져옴
            // Transient 등록이면 매번 새 인스턴스 생성
            // '!' = null이 아님을 단언 (DI에 등록되어 있으므로)

            newView.OnNavigatedTo();
            // 새 페이지의 초기화 작업 (예: 이벤트 구독)

            CurrentView = newView;
            // PropertyChanged 발생 → ContentControl이 DataTemplate에 따라 View 렌더링
        }
    }
}
```

> **네비게이션 흐름 요약:**
> 1. 사용자가 사이드바 버튼 클릭
> 2. MainViewModel의 NavigateToXxx() 호출
> 3. NavigationService.NavigateTo\<T\>() 실행
> 4. 이전 VM의 OnNavigatedFrom() → 새 VM의 OnNavigatedTo()
> 5. CurrentView 변경 → PropertyChanged → ContentControl이 DataTemplate으로 View 표시

---

## 4. Models/ — DTO 클래스

`Models/` 폴더에는 데이터 전송 객체(DTO)가 모여 있습니다.
이전에는 다른 파일 안에 중첩되어 있던 클래스들을 별도 파일로 분리했습니다.

### Models/Detection.cs — 탐지 결과 DTO

```csharp
namespace EggClassifier.Models
{
    public class Detection
    // 하나의 탐지 결과를 담는 데이터 클래스
    // 이전에는 YoloDetector.cs 안에 정의되어 있었음 → 별도 파일로 분리
    {
        public int ClassId { get; set; }              // 클래스 번호 (0=정상, 1=크랙, ...)
        public string ClassName { get; set; } = "";   // 클래스 한글 이름
        public float Confidence { get; set; }         // 신뢰도 (0.0 ~ 1.0, 높을수록 확실)
        public Rect BoundingBox { get; set; }         // 바운딩박스 (X, Y, Width, Height)
    }
}
```

### Models/ClassCountItem.cs — 클래스별 카운트 표시용

```csharp
namespace EggClassifier.Models
{
    public class ClassCountItem : ObservableObject
    // 이전에는 ViewModels/MainViewModel.cs 안에 정의되어 있었음 → 별도 파일로 분리
    {
        private int _count;

        public string ClassName { get; set; } = string.Empty;    // "정상", "크랙" 등
        public SolidColorBrush Color { get; set; } = Brushes.Gray;  // 색상 아이콘

        public int Count
        {
            get => _count;
            set => SetProperty(ref _count, value);
            // SetProperty: 값이 변경되면 PropertyChanged 이벤트 발생
            // → UI에서 자동으로 새 값을 반영
        }
    }
}
```

### Models/DetectionItem.cs — 현재 탐지 표시용

```csharp
namespace EggClassifier.Models
{
    public class DetectionItem : ObservableObject
    // 이전에는 ViewModels/MainViewModel.cs 안에 정의되어 있었음 → 별도 파일로 분리
    {
        public string Label { get; set; } = string.Empty;   // 클래스 한글 이름
        public float Confidence { get; set; }                // 신뢰도 (0~1)

        // 신뢰도에 따라 색상이 자동 결정되는 계산 프로퍼티
        public SolidColorBrush ConfidenceColor =>
            Confidence >= 0.8f
                ? new SolidColorBrush(Color.FromRgb(76, 175, 80))    // 80%↑: 녹색
            : Confidence >= 0.5f
                ? new SolidColorBrush(Color.FromRgb(255, 193, 7))    // 50~79%: 노랑
                : new SolidColorBrush(Color.FromRgb(244, 67, 54));   // 50%↓: 빨강
    }
}
```

> **분리 이유:**
> - 한 파일에 여러 클래스가 있으면 파일이 비대해지고 찾기 어려움
> - DTO는 독립적이므로 별도 파일로 분리하면 재사용성과 가독성 향상
> - 클래스 내용 자체는 이전과 동일 — 위치만 변경됨

---

## 5. Services/ — 인터페이스 + 래퍼

`Services/` 폴더에는 DI(Dependency Injection)를 위한 인터페이스와 래퍼 서비스가 정의되어 있습니다.
**핵심 원칙:** DI를 통해 인터페이스로 접근 → 테스트/교체 용이

### Services/IWebcamService.cs — 웹캠 서비스 인터페이스

```csharp
namespace EggClassifier.Services
{
    public interface IWebcamService
    // WebcamService의 공개 API를 인터페이스로 정의
    // → DetectionViewModel은 IWebcamService에만 의존 (구체 클래스에 의존하지 않음)
    {
        bool IsRunning { get; }
        // 캡처 중인지 여부

        event EventHandler<FrameCapturedEventArgs>? FrameCaptured;
        // 프레임 캡처 이벤트

        event EventHandler<string>? ErrorOccurred;
        // 에러 이벤트

        bool Start();
        // 캡처 시작

        void Stop();
        // 캡처 중지
    }
}
```

> **인터페이스 분리 효과:**
> - 유닛 테스트 시 Mock 웹캠 서비스로 교체 가능
> - 나중에 IP 카메라 등 다른 구현으로 교체 시 인터페이스만 구현하면 됨

### Services/IDetectorService.cs — 탐지 서비스 인터페이스

```csharp
namespace EggClassifier.Services
{
    public interface IDetectorService
    // YoloDetector를 감싸는 인터페이스
    // → DetectionViewModel은 IDetectorService에만 의존
    {
        bool IsLoaded { get; }
        // 모델 로드 여부

        bool LoadModel(string modelPath);
        // 모델 로드

        List<Detection> Detect(Mat image, float confidenceThreshold = 0.5f);
        // 추론 실행
    }
}
```

### Services/DetectorService.cs — IDetectorService 구현

```csharp
namespace EggClassifier.Services
{
    public class DetectorService : IDetectorService
    // YoloDetector에 작업을 위임하는 래퍼(Wrapper) 클래스
    {
        private readonly YoloDetector _detector = new();
        // 내부적으로 YoloDetector 인스턴스를 생성하여 사용

        public bool IsLoaded => _detector.IsLoaded;
        // YoloDetector의 IsLoaded를 그대로 전달

        public bool LoadModel(string modelPath)
            => _detector.LoadModel(modelPath);
        // YoloDetector의 LoadModel을 그대로 위임

        public List<Detection> Detect(Mat image, float confidenceThreshold = 0.5f)
            => _detector.Detect(image, confidenceThreshold);
        // YoloDetector의 Detect를 그대로 위임
    }
}
```

> **래퍼 패턴의 의미:**
> - YoloDetector 자체를 수정하지 않고 DI 시스템에 편입
> - 나중에 TensorRT, OpenVINO 등 다른 추론 엔진으로 교체 시
>   IDetectorService를 구현하는 새 클래스만 만들면 됨

---

## 6. DetectionViewModel.cs

기존 `MainViewModel`의 **웹캠/추론 로직이 이 파일로 이동**되었습니다.
MVVM 패턴의 ViewModel로서, UI 상태를 관리하고 WebcamService와 DetectorService를 조율합니다.

### 기존 MainViewModel과의 주요 차이점

| 항목 | 이전 (MainViewModel) | 현재 (DetectionViewModel) |
|------|---------------------|--------------------------|
| 상속 | `ObservableObject` | `ViewModelBase` (Core/) |
| 생성자 | `new WebcamService()`, `new YoloDetector()` | `IWebcamService`, `IDetectorService` DI 주입 |
| 초기화 | 생성자에서 이벤트 구독 | `OnNavigatedTo()`에서 이벤트 구독 |
| 정리 | `Dispose()` | `OnNavigatedFrom()`에서 웹캠 정지 + 이벤트 해제 |
| Detection 타입 | 직접 참조 | `using DetectionResult = EggClassifier.Models.Detection;` alias 사용 |

```csharp
using CommunityToolkit.Mvvm.ComponentModel;  // [ObservableProperty]
using CommunityToolkit.Mvvm.Input;           // [RelayCommand]
using EggClassifier.Core;                    // ViewModelBase
using EggClassifier.Models;                  // ClassCountItem, DetectionItem
using EggClassifier.Services;                // IWebcamService, IDetectorService
using OpenCvSharp;                           // Mat
using OpenCvSharp.WpfExtensions;             // Mat.ToBitmapSource() 확장 메서드
using System;
using System.Collections.ObjectModel;        // ObservableCollection (UI 바인딩용 컬렉션)
using System.IO;                             // File.Exists, Path.Combine
using System.Linq;
using System.Windows;                        // Application.Current, Visibility
using System.Windows.Media;                  // SolidColorBrush, Color
using System.Windows.Media.Imaging;          // BitmapSource

using DetectionResult = EggClassifier.Models.Detection;
// namespace 충돌 회피를 위한 alias
// "Detection"이라는 이름이 여러 네임스페이스에서 사용될 수 있으므로
// 명시적으로 Models.Detection을 DetectionResult로 참조
```

### DetectionViewModel 본체

```csharp
    public partial class DetectionViewModel : ViewModelBase
    // ViewModelBase 상속: OnNavigatedTo/OnNavigatedFrom 라이프사이클 지원
    // partial: CommunityToolkit 소스 생성기가 나머지 코드를 자동 생성
    {
        private readonly IWebcamService _webcamService;    // DI로 주입받은 웹캠 서비스
        private readonly IDetectorService _detector;        // DI로 주입받은 탐지 서비스

        // ── WPF용 클래스별 색상 브러시 ──
        // OpenCV의 BGR Scalar과 별개로, WPF UI에서 사용할 RGB 브러시
        private static readonly SolidColorBrush[] ClassBrushes = new[]
        {
            new SolidColorBrush(Color.FromRgb(76, 175, 80)),    // 정상: 녹색
            new SolidColorBrush(Color.FromRgb(244, 67, 54)),    // 크랙: 빨강
            new SolidColorBrush(Color.FromRgb(255, 0, 255)),    // 이물질: 마젠타
            new SolidColorBrush(Color.FromRgb(255, 235, 59)),   // 탈색: 노랑
            new SolidColorBrush(Color.FromRgb(255, 152, 0))     // 외형이상: 주황
        };
```

### [ObservableProperty] — 자동 바인딩 프로퍼티

```csharp
        // ── CommunityToolkit 소스 생성기 ──
        // [ObservableProperty]를 private 필드에 붙이면,
        // 자동으로 public 프로퍼티 + PropertyChanged 알림이 생성됨
        //
        // 예: private string _fpsText → public string FpsText { get; set; }
        //     값 변경 시 자동으로 UI에 알림

        [ObservableProperty]
        private BitmapSource? _currentFrame;           // 웹캠 영상 (Image 컨트롤에 바인딩)

        [ObservableProperty]
        private string _fpsText = "FPS: --";           // FPS 표시 텍스트

        [ObservableProperty]
        private string _statusMessage = "시작 버튼을 눌러 웹캠을 활성화하세요";  // 오버레이 메시지

        [ObservableProperty]
        private Visibility _overlayVisibility = Visibility.Visible;  // 오버레이 표시 여부

        [ObservableProperty]
        private bool _isModelLoaded;                   // 모델 로드 성공 여부

        [ObservableProperty]
        private string _modelStatusText = "로딩 중..."; // "로드됨", "로드 실패", "모델 없음"

        [ObservableProperty]
        private SolidColorBrush _modelStatusColor =    // 상태 배지 색상
            new(Color.FromRgb(255, 193, 7));            // 기본: 노랑 (로딩 중)

        [ObservableProperty]
        private string _modelPath = "";                // ONNX 모델 파일 경로

        [ObservableProperty]
        private bool _canStart = false;                // [시작] 버튼 IsEnabled

        [ObservableProperty]
        private bool _canStop = false;                 // [중지] 버튼 IsEnabled

        [ObservableProperty]
        private float _confidenceThreshold = 0.5f;     // 신뢰도 슬라이더 값

        [ObservableProperty]
        private int _totalDetections;                  // 현재 프레임 총 탐지 수

        [ObservableProperty]
        private Visibility _noDetectionVisibility = Visibility.Visible;  // "탐지 없음" 텍스트

        // ── UI 바인딩용 컬렉션 ──
        public ObservableCollection<ClassCountItem> ClassCounts { get; } = new();
        // 5개 클래스의 카운트 (ObservableCollection은 항목 추가/제거 시 UI 자동 갱신)

        public ObservableCollection<DetectionItem> CurrentDetections { get; } = new();
        // 현재 프레임의 개별 탐지 목록
```

### 생성자 — DI 주입

```csharp
        public DetectionViewModel(IWebcamService webcamService, IDetectorService detector)
        // ★ 이전과 달리 인터페이스를 DI로 주입받음
        // → new WebcamService(), new YoloDetector() 대신
        //   DI 컨테이너가 등록된 구현체를 자동으로 전달
        {
            _webcamService = webcamService;
            _detector = detector;

            // ── 클래스 카운트 UI 항목 초기화 ──
            for (int i = 0; i < YoloDetector.ClassNames.Length; i++)
            {
                ClassCounts.Add(new ClassCountItem
                {
                    ClassName = YoloDetector.ClassNames[i],  // "정상", "크랙", ...
                    Color = ClassBrushes[i],                 // 대응하는 색상
                    Count = 0                                // 초기 카운트 = 0
                });
            }

            // ── ONNX 모델 로드 ──
            LoadModel();

            // 참고: 이벤트 구독은 생성자가 아닌 OnNavigatedTo()에서 수행
        }
```

### OnNavigatedTo / OnNavigatedFrom — 라이프사이클

```csharp
        public override void OnNavigatedTo()
        // ★ 페이지 진입 시 호출 (이전에는 생성자에서 처리하던 내용)
        {
            _webcamService.FrameCaptured += OnFrameCaptured;
            // 웹캠에서 새 프레임이 캡처되면 OnFrameCaptured 메서드가 호출됨

            _webcamService.ErrorOccurred += OnWebcamError;
            // 웹캠 에러 발생 시 OnWebcamError 메서드가 호출됨
        }

        public override void OnNavigatedFrom()
        // ★ 페이지 이탈 시 호출 (이전에는 Dispose()에서 처리하던 내용)
        {
            _webcamService.Stop();
            // 다른 페이지로 이동하면 웹캠 자동 정지

            _webcamService.FrameCaptured -= OnFrameCaptured;
            _webcamService.ErrorOccurred -= OnWebcamError;
            // 이벤트 해제 → 메모리 누수 방지
            // 이 페이지로 돌아오면 OnNavigatedTo()에서 다시 구독됨
        }
```

### LoadModel — 모델 탐색 및 로드

```csharp
        private void LoadModel()
        {
            // ── 여러 경로에서 ONNX 모델 파일 탐색 ──
            string[] searchPaths = new[]
            {
                // 1순위: 빌드 출력 폴더의 Models/ (배포 시 여기에 위치)
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "egg_classifier.onnx"),

                // 2순위: 빌드 출력 폴더 루트
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "egg_classifier.onnx"),

                // 3순위: 프로젝트 소스의 Models/ (개발 시)
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Models", "egg_classifier.onnx"),

                // 4순위: 상위 프로젝트의 models/ (학습 결과 직접 참조)
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "models", "egg_classifier.onnx"),
            };

            // 존재하는 첫 번째 경로를 선택
            string? modelPath = searchPaths.FirstOrDefault(File.Exists);

            if (modelPath == null)
            {
                // ── 모델 파일을 찾지 못한 경우 ──
                ModelStatusText = "모델 없음";
                ModelStatusColor = new SolidColorBrush(Color.FromRgb(244, 67, 54));  // 빨강
                ModelPath = "models/egg_classifier.onnx 파일을 배치하세요";
                StatusMessage = "ONNX 모델 파일을 찾을 수 없습니다.\n학습 후 models 폴더에 배치하세요.";
                CanStart = true;   // 모델 없이도 웹캠은 사용 가능 (탐지만 안 됨)
                return;
            }

            ModelPath = modelPath;

            if (_detector.LoadModel(modelPath))
            {
                // ── 모델 로드 성공 ──
                IsModelLoaded = true;
                ModelStatusText = "로드됨";
                ModelStatusColor = new SolidColorBrush(Color.FromRgb(76, 175, 80));  // 녹색
                CanStart = true;
            }
            else
            {
                // ── 모델 로드 실패 (파일은 있지만 읽기 실패) ──
                ModelStatusText = "로드 실패";
                ModelStatusColor = new SolidColorBrush(Color.FromRgb(244, 67, 54));  // 빨강
                StatusMessage = "모델 로드에 실패했습니다.";
                CanStart = true;   // 웹캠만이라도 사용 가능
            }
        }
```

### Start / Stop 커맨드

```csharp
        [RelayCommand]   // → 자동으로 StartCommand 프로퍼티가 생성됨
        private void Start()
        {
            if (_webcamService.Start())   // 웹캠 시작 시도
            {
                CanStart = false;              // 시작 버튼 비활성화
                CanStop = true;                // 중지 버튼 활성화
                OverlayVisibility = Visibility.Collapsed;  // 안내 오버레이 숨김

                // 이전 세션의 카운트 초기화
                foreach (var item in ClassCounts)
                {
                    item.Count = 0;
                }
                TotalDetections = 0;
            }
            else
            {
                StatusMessage = "웹캠 시작에 실패했습니다.\n카메라가 연결되어 있는지 확인하세요.";
            }
        }

        [RelayCommand]   // → 자동으로 StopCommand 프로퍼티가 생성됨
        private void Stop()
        {
            _webcamService.Stop();             // 웹캠 캡처 중지
            CanStart = true;                   // 시작 버튼 활성화
            CanStop = false;                   // 중지 버튼 비활성화
            OverlayVisibility = Visibility.Visible;  // 안내 오버레이 표시
            StatusMessage = "중지됨. 시작 버튼을 눌러 재시작하세요.";
            FpsText = "FPS: --";
        }
```

### OnFrameCaptured — 프레임 수신 이벤트 핸들러 (핵심 처리 루프)

```csharp
        private void OnFrameCaptured(object? sender, FrameCapturedEventArgs e)
        {
            try
            {
                var frame = e.Frame;   // 캡처 스레드에서 전달된 프레임 (Mat)

                // ── AI 탐지 수행 ──
                var detections = _detector.IsLoaded
                    ? _detector.Detect(frame, ConfidenceThreshold)
                    // 모델이 로드되었으면 추론 실행
                    : new System.Collections.Generic.List<DetectionResult>();
                    // 모델이 없으면 빈 리스트 (웹캠 영상만 표시)

                // ── 바운딩박스 그리기 ──
                if (detections.Count > 0)
                {
                    YoloDetector.DrawDetections(frame, detections);
                    // frame 위에 직접 바운딩박스 + 라벨을 그림 (원본 수정)
                }

                // ── Mat → WPF BitmapSource 변환 ──
                var bitmapSource = frame.ToBitmapSource();
                // OpenCvSharp.WpfExtensions 확장 메서드로 변환

                bitmapSource.Freeze();
                // Freeze(): 이 객체를 불변(immutable)으로 만듦
                // → 다른 스레드(UI 스레드)에서도 안전하게 접근 가능
                // WPF에서 크로스스레드 접근 시 반드시 필요!

                // ── UI 스레드에 비동기 업데이트 요청 ──
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    // BeginInvoke: UI 스레드에 작업을 "예약"하고 즉시 반환
                    // (Invoke와 달리 캡처 스레드가 기다리지 않음 → 프레임 드롭 방지)

                    CurrentFrame = bitmapSource;          // 웹캠 이미지 업데이트
                    FpsText = $"FPS: {e.Fps:F1}";         // FPS 텍스트 업데이트

                    UpdateDetectionResults(detections);   // 탐지 결과 UI 업데이트
                });

                frame.Dispose();  // Mat 메모리 해제 (중요!)
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Frame processing error: {ex.Message}");
            }
        }
```

### UpdateDetectionResults — 탐지 결과를 UI에 반영

```csharp
        private void UpdateDetectionResults(System.Collections.Generic.List<DetectionResult> detections)
        {
            // ── 현재 프레임의 탐지 목록 갱신 ──
            CurrentDetections.Clear();   // 이전 프레임 결과 제거
            foreach (var det in detections.OrderByDescending(d => d.Confidence))
            {
                // 신뢰도 높은 순으로 정렬하여 추가
                CurrentDetections.Add(new DetectionItem
                {
                    Label = det.ClassName,        // "정상", "크랙" 등
                    Confidence = det.Confidence   // 0.95 등
                });
            }

            // "탐지된 객체 없음" 텍스트 표시/숨김
            NoDetectionVisibility = detections.Count == 0
                ? Visibility.Visible     // 탐지 없으면 표시
                : Visibility.Collapsed;  // 탐지 있으면 숨김

            // ── 클래스별 카운트 계산 ──
            var counts = new int[YoloDetector.ClassNames.Length];  // [0, 0, 0, 0, 0]
            foreach (var det in detections)
            {
                counts[det.ClassId]++;
                // 예: 정상 2개, 크랙 1개 → [2, 1, 0, 0, 0]
            }

            for (int i = 0; i < counts.Length; i++)
            {
                ClassCounts[i].Count = counts[i];
                // SetProperty가 호출되어 UI에 자동 반영
            }

            TotalDetections = detections.Count;   // 총 탐지 수
        }
```

### OnWebcamError

```csharp
        private void OnWebcamError(object? sender, string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // UI 스레드에서 에러 메시지 박스 표시
                MessageBox.Show(message, "웹캠 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                Stop();   // 자동으로 캡처 중지
            });
        }
    }
}
```

---

## 7. MainViewModel.cs

**새로운 MainViewModel**은 네비게이션 전용입니다. (~57줄)
기존의 웹캠/추론 로직은 모두 `DetectionViewModel`로 이동했습니다.
이 ViewModel은 사이드바 상태 관리와 페이지 전환만 담당합니다.

```csharp
using CommunityToolkit.Mvvm.ComponentModel;  // ObservableObject, [ObservableProperty]
using CommunityToolkit.Mvvm.Input;           // [RelayCommand]
using EggClassifier.Core;                    // INavigationService, ViewModelBase

namespace EggClassifier.ViewModels
{
    public partial class MainViewModel : ObservableObject
    // ObservableObject: PropertyChanged 자동 지원
    // partial: 소스 생성기용
    {
        private readonly INavigationService _navigation;
        // DI로 주입받은 네비게이션 서비스

        public INavigationService Navigation => _navigation;
        // ContentControl의 Content가 이 프로퍼티의 CurrentView에 바인딩
        // XAML: Content="{Binding Navigation.CurrentView}"

        // ── 사이드바 RadioButton 선택 상태 ──
        [ObservableProperty]
        private bool _isDetectionSelected = true;
        // 앱 시작 시 "계란 분류"가 기본 선택됨

        [ObservableProperty]
        private bool _isLoginSelected = false;

        [ObservableProperty]
        private bool _isDashboardSelected = false;

        public MainViewModel(INavigationService navigation)
        // DI에서 INavigationService(= NavigationService) 주입
        {
            _navigation = navigation;
        }

        [RelayCommand]
        private void NavigateToDetection()
        // 사이드바 "계란 분류" 버튼 클릭 시 실행
        {
            IsDetectionSelected = true;
            IsLoginSelected = false;
            IsDashboardSelected = false;
            // RadioButton 상태를 수동으로 관리
            // → 선택된 버튼 하이라이트, 나머지 해제

            _navigation.NavigateTo<DetectionViewModel>();
            // NavigationService가 DI에서 DetectionViewModel을 resolve하고
            // 이전 VM의 OnNavigatedFrom → 새 VM의 OnNavigatedTo 호출
        }

        [RelayCommand]
        private void NavigateToLogin()
        // 사이드바 "로그인" 버튼 클릭 시 실행 — 동일 패턴
        {
            IsDetectionSelected = false;
            IsLoginSelected = true;
            IsDashboardSelected = false;
            _navigation.NavigateTo<LoginViewModel>();
        }

        [RelayCommand]
        private void NavigateToDashboard()
        // 사이드바 "대시보드" 버튼 클릭 시 실행 — 동일 패턴
        {
            IsDetectionSelected = false;
            IsLoginSelected = false;
            IsDashboardSelected = true;
            _navigation.NavigateTo<DashboardViewModel>();
        }
    }
}
```

> **이전 MainViewModel과의 차이:**
> - 이전: 500줄 이상, 웹캠/추론/UI 상태 모두 관리
> - 현재: ~57줄, 네비게이션만 담당
> - 단일 책임 원칙(SRP) 적용: 한 클래스는 하나의 역할만 수행

---

## 8. MainWindow.xaml

이전에는 웹캠 영상 + 컨트롤 패널이 직접 배치된 UI였지만,
이제는 **사이드바 + ContentControl 셸** 구조로 변경되었습니다.
실제 페이지 콘텐츠는 ViewModel에 따라 DataTemplate으로 자동 렌더링됩니다.

```xml
<!-- Window 선언 -->
<Window x:Class="EggClassifier.MainWindow"
        xmlns="..."   <!-- WPF 기본 네임스페이스 -->
        xmlns:x="..." <!-- XAML 확장 네임스페이스 -->
        Title="계란 품질 분류 시스템"
        Height="720" Width="1280"
        Background="{StaticResource BackgroundBrush}"
        WindowStartupLocation="CenterScreen">
```

### 전체 레이아웃 구조 (2열: 사이드바 + 메인 콘텐츠)

```xml
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="200"/>   <!-- 사이드바 (고정 200px) -->
            <ColumnDefinition Width="*"/>      <!-- 메인 콘텐츠 (나머지 전체) -->
        </Grid.ColumnDefinitions>
```

> **이전과의 차이:**
> - 이전: `<ColumnDefinition Width="*"/>` + `<ColumnDefinition Width="320"/>`
>   (왼쪽 웹캠 + 오른쪽 컨트롤 패널)
> - 현재: `<ColumnDefinition Width="200"/>` + `<ColumnDefinition Width="*"/>`
>   (왼쪽 사이드바 + 오른쪽 ContentControl)

### 왼쪽 영역: 사이드바

```xml
        <!-- 사이드바: 어두운 배경 + 로고 + 네비게이션 버튼 -->
        <Border Grid.Column="0" Background="#252525">
            <StackPanel>
                <!-- 로고/앱 이름 영역 -->
                <TextBlock Text="EggClassifier"
                           FontSize="16" FontWeight="Bold"
                           Foreground="White" Margin="20,20,20,30"/>

                <!-- 네비게이션 RadioButton: 계란 분류 -->
                <RadioButton Style="{StaticResource NavButtonStyle}"
                             Content="📷  계란 분류"
                             IsChecked="{Binding IsDetectionSelected}"
                             Command="{Binding NavigateToDetectionCommand}"/>
                <!-- IsChecked: IsDetectionSelected 프로퍼티와 양방향 바인딩 -->
                <!--   → true면 선택 상태 스타일 적용 (파란 보더 + 흰색 텍스트) -->
                <!-- Command: 클릭 시 NavigateToDetection() 실행 -->
                <!--   → NavigationService가 DetectionViewModel로 전환 -->

                <!-- 네비게이션 RadioButton: 로그인 -->
                <RadioButton Style="{StaticResource NavButtonStyle}"
                             Content="🔐  로그인"
                             IsChecked="{Binding IsLoginSelected}"
                             Command="{Binding NavigateToLoginCommand}"/>

                <!-- 네비게이션 RadioButton: 대시보드 -->
                <RadioButton Style="{StaticResource NavButtonStyle}"
                             Content="📊  대시보드"
                             IsChecked="{Binding IsDashboardSelected}"
                             Command="{Binding NavigateToDashboardCommand}"/>
            </StackPanel>
        </Border>
```

### 오른쪽 영역: 메인 콘텐츠 (ContentControl)

```xml
        <!-- 메인 콘텐츠: ViewModel에 따라 View가 자동 교체됨 -->
        <ContentControl Grid.Column="1"
                        Content="{Binding Navigation.CurrentView}"/>
        <!-- Navigation: MainViewModel의 INavigationService 프로퍼티 -->
        <!-- CurrentView: 현재 활성화된 ViewModelBase 인스턴스 -->
        <!--
             동작 원리:
             1. Navigation.CurrentView가 DetectionViewModel이면
             2. App.xaml의 DataTemplate이 매칭됨:
                <DataTemplate DataType="{x:Type detection:DetectionViewModel}">
                    <detection:DetectionView/>
                </DataTemplate>
             3. ContentControl 안에 DetectionView(UserControl)가 렌더링됨

             CurrentView가 변경되면 → PropertyChanged 발생
             → ContentControl이 새 DataTemplate을 찾아 View를 교체
        -->
    </Grid>
</Window>
```

> **핵심 원리: ViewModel-First Navigation**
> - 코드에서는 ViewModel만 교체 (`NavigateTo<DetectionViewModel>()`)
> - View(UserControl)는 DataTemplate이 자동으로 매칭하여 렌더링
> - View와 ViewModel의 연결을 App.xaml에서 선언적으로 정의

---

## 9. MainWindow.xaml.cs

코드비하인드 — DI에서 MainViewModel을 주입받고 DataContext에 연결합니다.

```csharp
using System.Windows;
using EggClassifier.ViewModels;

namespace EggClassifier
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        // ★ DI에서 MainViewModel이 주입됨 (더 이상 new MainViewModel() 하지 않음)
        // App.xaml.cs에서 MainWindow를 DI에 등록했으므로,
        // ServiceProvider가 MainWindow를 생성할 때 MainViewModel도 함께 resolve하여 전달
        {
            InitializeComponent();          // XAML에서 정의한 UI 요소 초기화
            DataContext = viewModel;         // View에 ViewModel 연결
            // → XAML의 모든 {Binding ...}이 이 MainViewModel의 프로퍼티를 참조

            Loaded += (s, e) =>
            {
                viewModel.NavigateToDetectionCommand.Execute(null);
                // 앱 시작 시 자동으로 "계란 분류" 페이지로 이동
                // NavigateToDetection()이 실행되어 DetectionViewModel이 활성화됨
                // → ContentControl에 DetectionView가 표시됨
            };
        }
    }
}
```

> **이전과의 차이:**
> - 이전: `new MainViewModel()` → 직접 생성
> - 현재: 생성자 파라미터로 DI 주입
> - 이전: `Closing += ... _viewModel.Dispose()` → 수동 정리
> - 현재: `Loaded += ... NavigateToDetectionCommand` → 초기 페이지 설정
>   (정리는 NavigationService의 OnNavigatedFrom이 담당)

---

## 10. App.xaml

WPF 앱의 전역 리소스를 정의합니다.
다크 테마 색상, 컨트롤 스타일에 더해 **DataTemplate**과 **NavButtonStyle**이 추가되었습니다.

> **주요 변경:** `StartupUri="MainWindow.xaml"`이 제거되었습니다.
> DI 컨테이너를 통해 MainWindow를 수동으로 생성하므로 StartupUri가 불필요합니다.

```xml
<Application x:Class="EggClassifier.App"
             xmlns="..."
             xmlns:x="..."
             xmlns:detection="clr-namespace:EggClassifier.Views.Detection">
    <!-- xmlns:detection: DetectionViewModel/DetectionView의 네임스페이스 매핑 -->
    <!-- StartupUri 없음 — App.xaml.cs의 OnStartup에서 수동으로 MainWindow를 생성 -->

    <Application.Resources>
        <ResourceDictionary>
            <!-- ── 색상 팔레트 (기존과 동일) ── -->
            <Color x:Key="PrimaryColor">#2196F3</Color>        <!-- 파랑 (메인 액센트) -->
            <Color x:Key="BackgroundColor">#1E1E1E</Color>     <!-- 거의 검정 (앱 배경) -->
            <Color x:Key="SurfaceColor">#2D2D2D</Color>        <!-- 어두운 회색 (카드 배경) -->
            <!-- ... 기타 색상 ... -->

            <!-- Color → SolidColorBrush 변환 (기존과 동일) -->
            <SolidColorBrush x:Key="PrimaryBrush" Color="{StaticResource PrimaryColor}"/>
            <!-- ... -->
```

### DataTemplate — ViewModel → View 자동 매핑

```xml
            <!-- ── DataTemplate: ViewModel → View 자동 매핑 ── -->
            <DataTemplate DataType="{x:Type detection:DetectionViewModel}">
                <detection:DetectionView/>
            </DataTemplate>
            <!--
                 동작 원리:
                 ContentControl에 DetectionViewModel 인스턴스가 세팅되면
                 → WPF가 이 DataTemplate을 자동으로 찾아
                 → DetectionView(UserControl)를 렌더링함

                 새 페이지를 추가할 때:
                 1. LoginViewModel/LoginView 생성
                 2. 여기에 DataTemplate 추가:
                    <DataTemplate DataType="{x:Type login:LoginViewModel}">
                        <login:LoginView/>
                    </DataTemplate>
                 3. 끝! NavigateTo<LoginViewModel>()만 호출하면 자동으로 View가 표시됨
            -->
```

### NavButtonStyle — 사이드바 RadioButton 커스텀 스타일

```xml
            <!-- ── NavButtonStyle: 사이드바 RadioButton 커스텀 스타일 ── -->
            <Style x:Key="NavButtonStyle" TargetType="RadioButton">
                <!-- RadioButton의 기본 동그라미 모양을 완전히 재정의하여
                     사이드바 네비게이션 버튼처럼 보이게 함 -->

                <!-- 기본 상태: 투명 배경, 회색 텍스트 (#AAAAAA) -->
                <Setter Property="Background" Value="Transparent"/>
                <Setter Property="Foreground" Value="#AAAAAA"/>
                <Setter Property="Padding" Value="20,12"/>
                <Setter Property="FontSize" Value="14"/>

                <!-- ControlTemplate: 시각적 구조 재정의 -->
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="RadioButton">
                            <Border Background="{TemplateBinding Background}"
                                    BorderBrush="Transparent"
                                    BorderThickness="3,0,0,0">
                                <!-- BorderThickness="3,0,0,0": 왼쪽에만 3px 보더 -->
                                <!-- Checked 상태에서 파란색으로 변경됨 -->
                                <ContentPresenter Margin="{TemplateBinding Padding}"/>
                            </Border>

                            <ControlTemplate.Triggers>
                                <!-- Hover: #333333 배경 -->
                                <Trigger Property="IsMouseOver" Value="True">
                                    <Setter Property="Background" Value="#333333"/>
                                </Trigger>

                                <!-- Checked: #333333 배경 + 좌측 파란 보더 + 흰색 텍스트 -->
                                <Trigger Property="IsChecked" Value="True">
                                    <Setter Property="Background" Value="#333333"/>
                                    <Setter Property="BorderBrush"
                                            Value="{StaticResource PrimaryBrush}"/>
                                    <!-- PrimaryBrush = #2196F3 파란색 -->
                                    <Setter Property="Foreground" Value="White"/>
                                </Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
```

### 기존 스타일 (변경 없음)

```xml
            <!-- ── 버튼 스타일 (기존과 동일) ── -->
            <Style x:Key="PrimaryButtonStyle" TargetType="Button">
                <!-- 기본: 파란색 배경, 흰색 텍스트, 둥근 모서리 -->
                <!-- IsMouseOver 시: 진한 파란색 (#1976D2) -->
                <!-- IsEnabled=False 시: 회색 배경 (#555555) -->
            </Style>

            <!-- DangerButtonStyle: PrimaryButtonStyle을 상속하고 색상만 빨강으로 변경 -->

            <!-- ── 카드 스타일 (기존과 동일) ── -->
            <Style x:Key="CardStyle" TargetType="Border">
                <!-- 둥근 모서리(8px), 어두운 회색 배경, 15px 안쪽 여백 -->
            </Style>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

---

## 11. App.xaml.cs

앱 시작점 — **DI(Dependency Injection) 컨테이너**를 구성하고 앱을 시작합니다.
이전에는 전역 예외 처리만 있었지만, 이제 모든 서비스와 ViewModel의 등록을 담당합니다.

```csharp
using Microsoft.Extensions.DependencyInjection;  // ServiceCollection, AddSingleton 등
using System;
using System.Windows;
using EggClassifier.Core;
using EggClassifier.Services;
using EggClassifier.ViewModels;

namespace EggClassifier
{
    public partial class App : Application
    {
        private readonly IServiceProvider _serviceProvider;
        // DI 컨테이너 — 등록된 서비스/ViewModel을 resolve하는 역할

        public App()
        {
            var services = new ServiceCollection();
            // Microsoft.Extensions.DependencyInjection의 DI 컨테이너 빌더
            // ASP.NET Core와 동일한 DI 프레임워크를 WPF에서도 사용

            // ── Core — 앱 전체에서 1개만 사용 ──
            services.AddSingleton<INavigationService, NavigationService>();
            // Singleton: 앱 전체에서 NavigationService 인스턴스 1개만 생성
            // INavigationService를 요청하면 항상 같은 인스턴스 반환
            // → CurrentView 상태가 공유되어야 하므로 Singleton 필수

            // ── Services — 공유 리소스이므로 Singleton ──
            services.AddSingleton<IWebcamService, WebcamService>();
            // WebcamService: 웹캠 장치는 하나이므로 인스턴스도 하나
            // → 여러 ViewModel이 같은 웹캠을 공유

            services.AddSingleton<IDetectorService, DetectorService>();
            // DetectorService: ONNX 모델 로드는 비용이 크므로 한 번만 수행
            // → 모든 DetectionViewModel이 같은 모델 인스턴스를 공유

            // ── ViewModels ──
            services.AddSingleton<MainViewModel>();
            // MainViewModel은 1개 (셸 윈도우는 하나뿐)
            // 사이드바 상태가 유지되어야 하므로 Singleton

            services.AddTransient<DetectionViewModel>();
            services.AddTransient<LoginViewModel>();
            services.AddTransient<DashboardViewModel>();
            // Feature ViewModel은 Transient: 페이지 전환마다 새 인스턴스 생성
            // → 이전 상태가 남지 않음 (깨끗한 상태로 시작)
            //
            // Transient vs Singleton:
            //   Transient: GetService할 때마다 new → 이전 상태 초기화됨
            //   Singleton: 첫 GetService에서만 new → 이후 같은 인스턴스 재사용

            // ── Window ──
            services.AddSingleton<MainWindow>();
            // MainWindow도 하나뿐이므로 Singleton

            _serviceProvider = services.BuildServiceProvider();
            // 등록 완료 → IServiceProvider 인스턴스 생성
            // 이후 GetService<T>() / GetRequiredService<T>()로 인스턴스를 가져올 수 있음
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // ── 전역 미처리 예외 핸들러 (기존과 동일) ──
            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show(
                    $"예기치 않은 오류가 발생했습니다:\n\n{args.Exception.Message}",
                    "오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                args.Handled = true;
            };

            // ── MainWindow를 DI에서 resolve하여 표시 ──
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            // GetRequiredService: 등록된 MainWindow 인스턴스를 가져옴
            // → MainWindow(MainViewModel viewModel) 생성자가 호출됨
            // → MainViewModel도 DI에서 자동으로 resolve되어 주입됨
            //
            // DI 해결 순서 (자동):
            //   MainWindow 필요 → MainViewModel 필요 → INavigationService 필요
            //   → NavigationService 생성 → MainViewModel 생성 → MainWindow 생성

            mainWindow.Show();
            // StartupUri 대신 수동으로 윈도우를 표시
        }
    }
}
```

> **이전과의 차이:**
> - 이전: `OnStartup`에 예외 처리기만 있고, `StartupUri`로 MainWindow 자동 생성
> - 현재: DI 컨테이너 구성 + `GetRequiredService`로 MainWindow 수동 생성
> - DI 덕분에 모든 의존성이 명시적이고, 테스트/교체가 용이해짐

---

## 12. EggClassifier.csproj

프로젝트 설정 파일 — 빌드 옵션과 NuGet 패키지를 정의합니다.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>        <!-- WinExe: 콘솔 창 없이 실행 -->
    <TargetFramework>net8.0-windows</TargetFramework>  <!-- .NET 8, Windows 전용 -->
    <Nullable>enable</Nullable>            <!-- null 참조 경고 활성화 -->
    <ImplicitUsings>enable</ImplicitUsings>  <!-- 자주 쓰는 using 자동 추가 -->
    <UseWPF>true</UseWPF>                  <!-- WPF 프레임워크 사용 -->
  </PropertyGroup>

  <ItemGroup>
    <!-- NuGet 패키지 의존성 -->
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.2.2" />
    <!-- MVVM 헬퍼: [ObservableProperty], [RelayCommand] 소스 생성기 -->

    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.1" />
    <!-- ★ 신규 추가: DI 컨테이너 -->
    <!-- ServiceCollection, AddSingleton, AddTransient, BuildServiceProvider 등 제공 -->
    <!-- ASP.NET Core와 동일한 DI 프레임워크를 WPF 데스크톱 앱에서도 사용 -->

    <PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.16.3" />
    <!-- ONNX 모델 추론 엔진 (CPU + CUDA 지원) -->

    <PackageReference Include="OpenCvSharp4" Version="4.9.0.20240103" />
    <!-- OpenCV C# 래퍼 (핵심) -->

    <PackageReference Include="OpenCvSharp4.Extensions" Version="4.9.0.20240103" />
    <!-- Mat ↔ Bitmap 변환 유틸리티 -->

    <PackageReference Include="OpenCvSharp4.WpfExtensions" Version="4.9.0.20240103" />
    <!-- Mat.ToBitmapSource() WPF 전용 확장 메서드 -->

    <PackageReference Include="OpenCvSharp4.runtime.win" Version="4.9.0.20240103" />
    <!-- Windows용 네이티브 OpenCV DLL (opencv_world490.dll 등) -->
  </ItemGroup>

  <ItemGroup>
    <None Update="Models\egg_classifier.onnx">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <!-- 빌드 시 egg_classifier.onnx를 출력 폴더에 복사 -->
      <!-- PreserveNewest: 소스가 더 새로운 경우에만 복사 (빌드 속도 최적화) -->
    </None>
  </ItemGroup>
</Project>
```

---

## 13. run_train.py

YOLOv8 모델 학습 스크립트입니다.

```python
from ultralytics import YOLO    # Ultralytics YOLOv8 프레임워크
import multiprocessing          # Windows에서 멀티프로세싱 안전하게 사용하기 위함

def main():
    print("Starting YOLOv8 training...")
    print("Settings: batch=32, workers=4")

    model = YOLO('yolov8n.pt')
    # YOLOv8n (nano) 사전학습 모델 로드
    # 'n' = 가장 작은 모델 → 실시간 추론에 적합 (빠른 속도)
    # 사전학습: COCO 데이터셋에서 미리 학습된 가중치 사용
    # → 적은 데이터/에포크로도 높은 성능 달성 (전이 학습)

    results = model.train(
        data='data/data.yaml',   # 데이터셋 설정 파일 경로
                                 # (이미지/라벨 경로, 클래스 수, 클래스 이름 정의)
        epochs=50,               # 전체 학습 반복 횟수 (전체 데이터를 50번 봄)
        imgsz=640,               # 입력 이미지 크기 (640x640으로 리사이즈)
        batch=32,                # 배치 크기 (한 번에 32장씩 처리)
                                 # GPU 메모리에 따라 조절 (8GB → 16~32 적당)
        device=0,                # GPU 장치 번호 (0 = 첫 번째 NVIDIA GPU)
        project='runs/detect',   # 결과 저장 상위 폴더
        name='egg_classifier',   # 결과 저장 하위 폴더
        patience=10,             # Early stopping: 10 에포크 동안 개선 없으면 학습 중단
                                 # → 과적합 방지 + 불필요한 학습 시간 절약
        workers=4,               # 데이터 로딩 병렬 워커 수
                                 # CPU 코어 수에 맞게 조절 (너무 많으면 메모리 부족)
        optimizer='SGD',         # 옵티마이저: Stochastic Gradient Descent
                                 # 원래 기본값은 'auto' (Muon)이었으나,
                                 # RTX 3070에서 CUBLAS BF16 에러 발생하여 SGD로 변경
        verbose=True             # 학습 진행 상황 상세 출력
    )

    print('Training complete!')
    print(f'Best model saved at: runs/detect/egg_classifier/weights/best.pt')

if __name__ == '__main__':
    multiprocessing.freeze_support()
    # Windows에서 multiprocessing을 사용할 때 필요한 호출
    # 이 줄이 없으면 DataLoader의 workers > 0일 때 에러 발생
    main()
```

---

## 14. export_onnx.py

학습된 PyTorch 모델을 ONNX 형식으로 변환합니다.

```python
from ultralytics import YOLO

model = YOLO(r'D:\repos\Smart_Factory\project_CSharp\runs\detect\runs\detect\egg_classifier5\weights\best.pt')
# 학습 완료된 best.pt 모델 로드
# best.pt: 학습 중 가장 높은 mAP를 기록한 시점의 가중치
# (last.pt는 마지막 에포크의 가중치 — best.pt가 보통 더 좋음)

model.export(
    format='onnx',       # 출력 포맷: ONNX (Open Neural Network Exchange)
                         # ONNX: 다양한 프레임워크/언어에서 사용 가능한 범용 모델 포맷
                         # PyTorch → ONNX → C# OnnxRuntime에서 추론 가능
    imgsz=640,           # 입력 텐서 크기 (학습 시와 동일해야 함)
    simplify=True,       # ONNX 그래프 단순화 (onnxsim 사용)
                         # 불필요한 노드 제거 → 추론 속도 향상
    opset=12             # ONNX 연산자 세트 버전
                         # 12: 대부분의 OnnxRuntime 버전과 호환
)

print('ONNX export complete!')
# 결과: best.onnx 파일이 best.pt와 같은 폴더에 생성됨
```

---

## 15. test_inference.py

학습된 모델로 검증 이미지를 테스트합니다.

```python
from ultralytics import YOLO
import cv2               # OpenCV (여기서는 사용하지 않지만 import되어 있음)
import os                # 파일 경로 처리

model = YOLO(r'D:\repos\...\best.pt')   # 학습된 모델 로드

# ── 검증 이미지 5장 선택 ──
val_dir = r'D:\repos\...\data\images\val'
images = [os.path.join(val_dir, f) for f in os.listdir(val_dir)[:5]]
# os.listdir(): 폴더 내 파일 목록
# [:5]: 처음 5개만 선택
# os.path.join(): 폴더 + 파일명 결합 → 전체 경로

# ── 추론 실행 ──
results = model.predict(
    images,              # 5장의 이미지 경로 리스트
    conf=0.5,            # 최소 신뢰도 50%
    save=True,           # 결과 이미지를 파일로 저장
    project='runs/test', # 저장 위치
    name='sample_test'   # 하위 폴더명
)

# ── 결과 출력 ──
for r in results:
    print(f"\n{os.path.basename(r.path)}:")
    # os.path.basename: 파일명만 추출 (예: "img_001.jpg")

    for box in r.boxes:   # 각 탐지된 객체에 대해
        cls = int(box.cls[0])          # 클래스 번호 (텐서 → 정수)
        conf = float(box.conf[0])      # 신뢰도 (텐서 → 실수)
        names = ['normal', 'crack', 'foreign_matter', 'discoloration', 'deformed']
        print(f"  - {names[cls]} ({conf:.1%})")
        # 예: "  - normal (95.2%)"

print(f"\nResults saved to: runs/test/sample_test/")
```
