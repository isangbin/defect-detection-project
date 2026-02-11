# Egg Quality Classification System (계란 품질 실시간 분류 시스템)

YOLOv8 + C# WPF 기반 계란 품질 실시간 분류 시스템입니다.
웹캠으로 촬영한 계란의 품질을 AI가 자동으로 분류합니다.

## 분류 클래스 (5종)

| 클래스 | 설명 | 바운딩박스 색상 |
|--------|------|----------------|
| 정상 (normal) | 품질 양호한 계란 | 녹색 |
| 크랙 (crack) | 껍질에 균열이 있는 계란 | 빨강 |
| 이물질 (foreign_matter) | 표면에 이물질이 있는 계란 | 마젠타 |
| 탈색 (discoloration) | 색상 이상이 있는 계란 | 노랑 |
| 외형이상 (deformed) | 형태가 불규칙한 계란 | 주황 |

## 학습 성능

| 메트릭 | 값 |
|--------|-----|
| mAP50-95 | 93.3% |
| Precision | 87.7% |
| Recall | 86.5% |
| 추론 속도 | ~17.5ms/이미지 (GPU) |
| 모델 크기 | 11.7MB (ONNX) |

---

## 프로젝트 구조

```
demo/
├── README.md                       ← 지금 보고 있는 파일
├── USAGE.md                        ← 상세 사용 가이드
├── .gitignore
│
├── training/                       ← Python 학습 스크립트
│   ├── convert_xml_to_yolo.py      ← AI Hub XML → YOLO 포맷 변환
│   ├── train.py                    ← YOLOv8 모델 학습
│   ├── export_onnx.py              ← ONNX 내보내기
│   └── requirements.txt            ← Python 의존성
│
├── data/                           ← YOLO 데이터셋 (변환 후 생성, Git 미포함)
│   └── .gitkeep
│
├── models/                         ← 학습된 ONNX 모델 (Git 미포함)
│   └── .gitkeep
│
└── EggClassifier/                  ← C# WPF 애플리케이션 (Feature 기반 모듈 구조)
    ├── App.xaml / App.xaml.cs      ← DI 컨테이너, DataTemplate 매핑
    ├── MainWindow.xaml / .xaml.cs  ← 셸 (사이드바 + ContentControl)
    ├── Core/                       ← 네비게이션 인프라
    │   ├── ViewModelBase.cs        ← 페이지 ViewModel 베이스
    │   ├── INavigationService.cs   ← 네비게이션 인터페이스
    │   └── NavigationService.cs    ← 네비게이션 구현
    ├── Models/                     ← 데이터 모델 + 추론 엔진
    │   ├── Detection.cs            ← 탐지 결과 DTO
    │   ├── ClassCountItem.cs       ← 클래스별 카운트 표시용
    │   ├── DetectionItem.cs        ← 현재 탐지 표시용
    │   └── YoloDetector.cs         ← ONNX 추론 엔진
    ├── Services/                   ← 서비스 인터페이스 + 구현
    │   ├── IWebcamService.cs       ← 웹캠 인터페이스
    │   ├── WebcamService.cs        ← 웹캠 캡처 구현
    │   ├── IDetectorService.cs     ← 탐지 인터페이스
    │   └── DetectorService.cs      ← YoloDetector 래핑
    ├── Features/                   ← 팀원별 독립 개발 영역
    │   ├── Detection/              ← 팀원A: 계란 분류 (웹캠+YOLO)
    │   ├── Login/                  ← 팀원B: 로그인
    │   └── Dashboard/              ← 팀원C: DB 시각화
    ├── ViewModels/
    │   └── MainViewModel.cs        ← 네비게이션 전용
    ├── docs/                       ← 상세 문서
    │   ├── PROJECT_STRUCTURE.md    ← 프로젝트 구조 가이드
    │   ├── GUIDELINES.md           ← 개발 가이드라인
    │   ├── API_SPEC.md             ← API 기능명세서
    │   └── CODE_REFERENCE.md       ← 코드 라인별 해설
    └── EggClassifier.csproj        ← 프로젝트 설정
```

---

## 빠른 시작

### 사전 요구사항

- Windows 10/11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Anaconda](https://www.anaconda.com/download) (Python 학습 시)
- 웹캠 (USB 또는 내장)
- (선택) NVIDIA GPU + CUDA 12.x

### Step 1: 모델 학습 (Python)

```bash
# 1. Python 환경 설정
conda create -n sf_py python=3.13
conda activate sf_py
cd training
pip install -r requirements.txt

# 2. AI Hub 데이터 변환 (D드라이브 원본 → data/ 폴더)
python convert_xml_to_yolo.py ^
    --train-images "D:\원천데이터\Training" ^
    --train-labels "D:\라벨링데이터\Training" ^
    --val-images "D:\원천데이터\Validation" ^
    --val-labels "D:\라벨링데이터\Validation" ^
    --output "../data"

# 3. 학습
python train.py --data ../data/data.yaml --model n --epochs 50 --batch 32

# 4. ONNX 내보내기
python export_onnx.py --model runs/detect/egg_classifier/weights/best.pt --output ../models --verify
```

### Step 2: C# 앱 실행

```bash
# 1. ONNX 모델 배치
copy ..\models\egg_classifier.onnx EggClassifier\Models\egg_classifier.onnx

# 2. 빌드 및 실행
cd EggClassifier
dotnet restore
dotnet run
```

### Step 3: 사용

1. 앱 실행 → 우측 "모델 상태"가 **로드됨(녹색)** 확인
2. **[시작]** 클릭 → 웹캠 활성화
3. 계란을 카메라에 비추면 자동 분류
4. 신뢰도 슬라이더로 민감도 조절

---

## 기술 스택

| 구분 | 기술 | 용도 |
|------|------|------|
| AI 모델 | YOLOv8n (Ultralytics) | 객체 탐지 |
| 추론 엔진 | Microsoft.ML.OnnxRuntime | ONNX 모델 실행 |
| 영상 처리 | OpenCvSharp4 | 웹캠 캡처, 이미지 전처리 |
| UI | WPF (.NET 8.0) | 데스크톱 애플리케이션 |
| MVVM | CommunityToolkit.Mvvm | 데이터 바인딩 |
| 학습 | PyTorch + Ultralytics | 모델 학습 (Python) |

---

## 상세 문서

- [사용 가이드 (USAGE.md)](USAGE.md)
- [개발 가이드라인](EggClassifier/docs/GUIDELINES.md)
- [API 기능명세서](EggClassifier/docs/API_SPEC.md)
- [코드 라인별 해설](EggClassifier/docs/CODE_REFERENCE.md)
