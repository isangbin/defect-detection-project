# 계란 품질 실시간 분류 시스템 사용 가이드

## 프로젝트 개요
AI Hub 계란 데이터셋을 활용한 YOLOv8 기반 계란 품질 분류 시스템입니다.

### 클래스
- **정상 (normal)**: 품질 양호한 계란
- **크랙 (crack)**: 껍질에 균열이 있는 계란
- **이물질 (foreign_matter)**: 표면에 이물질이 있는 계란
- **탈색 (discoloration)**: 색상 이상이 있는 계란
- **외형이상 (deformed)**: 형태가 불규칙한 계란

---

## Phase 0: 데이터 준비 (사용자 수행)

### AI Hub 데이터 다운로드
1. https://aihub.or.kr 접속 및 로그인
2. "계란 데이터" 검색
3. 데이터 활용 신청 (승인까지 1~3일)
4. COLOR 이미지 + XML 라벨 다운로드
5. 압축 해제 후 이미지와 라벨 폴더 경로 확인

---

## Phase 1: Python 학습 환경

### 1.1 환경 설정
```bash
cd training
pip install -r requirements.txt
```

### 1.2 데이터 변환 (XML → YOLO)

AI Hub에서 다운받은 데이터가 Training/Validation으로 이미 분리되어 있는 경우:

```bash
python convert_xml_to_yolo.py ^
    --train-images "D:\원천데이터\Training" ^
    --train-labels "D:\라벨링데이터\Training" ^
    --val-images "D:\원천데이터\Validation" ^
    --val-labels "D:\라벨링데이터\Validation" ^
    --output "../data"
```

> **참고:** 위 경로는 예시입니다. 실제 AI Hub 데이터를 다운받은 경로로 변경하세요.
> 이 스크립트는 이미지를 output 폴더로 복사하므로 디스크 용량이 충분한지 확인하세요.

### 1.3 모델 학습
```bash
python train.py \
    --data "../data/data.yaml" \
    --model n \
    --epochs 100 \
    --batch 16 \
    --device 0
```

**모델 크기 옵션:**
- `n` (nano): 빠른 추론, 낮은 정확도 (권장 - 실시간용)
- `s` (small): 균형
- `m` (medium): 높은 정확도
- `l` (large): 최고 정확도, 느린 추론

### 1.4 ONNX 내보내기
```bash
python export_onnx.py \
    --model "runs/detect/egg_classifier/weights/best.pt" \
    --output "../models" \
    --verify
```

---

## Phase 2: C# 애플리케이션

### 2.1 빌드
```bash
cd EggClassifier
dotnet restore
dotnet build --configuration Release
```

### 2.2 모델 배치
학습 완료된 `egg_classifier.onnx` 파일을 다음 위치 중 하나에 배치:
- `EggClassifier/Models/egg_classifier.onnx`
- `models/egg_classifier.onnx`

### 2.3 실행
```bash
dotnet run --configuration Release
```

또는 빌드된 exe 파일 직접 실행

---

## 애플리케이션 사용법

### UI 구성
- **웹캠 영상**: 실시간 영상 + 탐지 결과 오버레이
- **모델 상태**: ONNX 모델 로드 상태 표시
- **컨트롤**: 시작/중지 버튼, 신뢰도 임계값 조절
- **탐지 결과**: 클래스별 개수 및 현재 탐지 목록

### 조작
1. 웹캠 연결 확인
2. **시작** 버튼 클릭
3. 계란을 웹캠에 비추면 자동 탐지
4. 신뢰도 임계값 슬라이더로 민감도 조절
5. **중지** 버튼으로 종료

---

## 트러블슈팅

### 웹캠이 인식되지 않는 경우
- 카메라 드라이버 확인
- 다른 애플리케이션에서 카메라 사용 중인지 확인
- WebcamService.cs의 `CameraIndex` 값 변경 시도 (0, 1, 2...)

### 모델 로드 실패
- ONNX 파일 경로 확인
- Microsoft.ML.OnnxRuntime 패키지 버전 확인
- GPU 사용 시 CUDA 드라이버 확인

### 탐지 성능이 낮은 경우
- 더 많은 데이터로 재학습
- 모델 크기 증가 (n → s → m)
- 에포크 수 증가
- 데이터 증강 설정 조정

---

## 파일 구조

```
demo/
├── training/                    # Python 학습 환경
│   ├── convert_xml_to_yolo.py  # AI Hub XML → YOLO 변환
│   ├── train.py                # YOLOv8 학습 (SGD, argparse)
│   ├── export_onnx.py          # ONNX 내보내기 + 검증
│   └── requirements.txt        # Python 의존성
│
├── data/                        # YOLO 데이터셋 (변환 후 생성됨)
│   ├── data.yaml               # 데이터셋 설정
│   ├── images/train/           # 학습 이미지
│   ├── images/val/             # 검증 이미지
│   ├── labels/train/           # 학습 라벨 (.txt)
│   └── labels/val/             # 검증 라벨 (.txt)
│
├── models/                      # 학습된 ONNX 모델
│   └── egg_classifier.onnx     # (export 후 생성됨)
│
└── EggClassifier/              # C# WPF 프로젝트
    ├── Models/
    │   ├── YoloDetector.cs     # ONNX 추론 엔진 (Letterbox 전처리)
    │   └── egg_classifier.onnx # (models/에서 복사)
    ├── Services/
    │   └── WebcamService.cs    # 웹캠 캡처 서비스
    ├── ViewModels/
    │   └── MainViewModel.cs    # MVVM ViewModel
    └── docs/                   # 상세 문서
        ├── GUIDELINES.md       # 개발 가이드라인
        ├── API_SPEC.md         # API 기능명세서
        └── CODE_REFERENCE.md   # 코드 라인별 해설
```
