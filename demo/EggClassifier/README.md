# EggClassifier - 계란 품질 실시간 분류 시스템

## 프로젝트 개요

AI Hub 계란 데이터셋으로 학습된 YOLOv8 객체 탐지 모델을 활용하여,
웹캠으로 촬영되는 계란의 품질을 **실시간으로 분류**하는 Windows 데스크톱 애플리케이션입니다.

| 항목 | 내용 |
|------|------|
| 플랫폼 | Windows (WPF, .NET 8.0) |
| AI 모델 | YOLOv8n (Ultralytics) → ONNX 변환 |
| 추론 엔진 | Microsoft.ML.OnnxRuntime |
| 영상 처리 | OpenCvSharp4 |
| UI 패턴 | MVVM (CommunityToolkit.Mvvm) |
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
├── EggClassifier.csproj        # 프로젝트 설정 (NuGet 패키지, 빌드 옵션)
├── App.xaml                    # 전역 리소스 (테마 색상, 버튼 스타일)
├── App.xaml.cs                 # 앱 시작점, 전역 예외 처리
├── MainWindow.xaml             # 메인 UI 레이아웃 (XAML)
├── MainWindow.xaml.cs          # 메인 윈도우 코드비하인드
├── Models/
│   ├── YoloDetector.cs         # ONNX 추론 엔진 (핵심 AI 로직)
│   └── egg_classifier.onnx     # 학습된 ONNX 모델 파일
├── Services/
│   └── WebcamService.cs        # 웹캠 캡처 서비스
├── ViewModels/
│   └── MainViewModel.cs        # MVVM ViewModel (UI ↔ 로직 연결)
└── docs/
    ├── GUIDELINES.md           # 개발 가이드라인
    ├── CODE_REFERENCE.md       # 코드 라인별 상세 해설
    └── API_SPEC.md             # API/클래스 기능명세서
```

---

## 빠른 시작

### 1. 사전 요구사항

- Windows 10/11
- .NET 8.0 SDK ([다운로드](https://dotnet.microsoft.com/download/dotnet/8.0))
- 웹캠 (USB 또는 내장)
- (선택) NVIDIA GPU + CUDA 12.x (GPU 추론 시)

### 2. 빌드 및 실행

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

### 3. 사용 방법

1. 앱 실행 → 우측 패널에서 "모델 상태"가 **로드됨(녹색)** 인지 확인
2. **[시작]** 버튼 클릭 → 웹캠 영상 활성화
3. 계란을 웹캠 앞에 배치 → 자동으로 바운딩박스 + 클래스 + 신뢰도 표시
4. **신뢰도 임계값** 슬라이더로 민감도 조절 (기본 50%)
5. **[중지]** 버튼으로 웹캠 정지

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
[결과 시각화]               MainViewModel.cs - OnFrameCaptured()
  (바운딩박스 그리기 + UI 업데이트)
        ↓
[화면 표시]                 MainWindow.xaml
  (웹캠 영상 + 탐지 결과 패널)
```

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
| AI 추론 | Microsoft.ML.OnnxRuntime | 1.16.3 | ONNX 모델 실행 |
| 영상처리 | OpenCvSharp4 | 4.9.0 | 웹캠, 이미지 처리 |
| 학습 | Ultralytics YOLOv8 | 8.4.11 | 모델 학습 (Python) |
| GPU | CUDA | 12.4 | GPU 가속 |

---

## 상세 문서

- [개발 가이드라인 (GUIDELINES.md)](docs/GUIDELINES.md)
- [코드 라인별 해설 (CODE_REFERENCE.md)](docs/CODE_REFERENCE.md)
- [API 기능명세서 (API_SPEC.md)](docs/API_SPEC.md)
