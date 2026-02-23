# training — YOLOv8 모델 학습 환경

YOLOv8 계란 품질 분류 모델의 학습·검증·ONNX 변환을 담당하는 Python 스크립트 모음입니다.

---

## 스크립트 구성

| 파일 | 설명 |
|------|------|
| `train.py` | 데이터 분석 + YOLOv8 학습 + 하이퍼파라미터 튜닝 통합 스크립트 |
| `export_onnx.py` | 학습된 `.pt` 모델 → ONNX 변환 및 검증 |
| `download_face_models.py` | 얼굴인식 모델 다운로드 (Haar Cascade + MobileFaceNet) |
| `convert_xml_to_yolo.py` | AI Hub XML 라벨 → YOLO 포맷 변환 |
| `requirements.txt` | Python 의존성 목록 |

---

## 사전 요구사항

- [Anaconda](https://www.anaconda.com/download) 또는 Miniconda
- NVIDIA GPU + CUDA 12.x (권장; CPU 학습도 가능)
- AI Hub 계란 데이터셋 다운로드 완료

---

## Step 0: 환경 설정

```bash
conda create -n sf_py python=3.13
conda activate sf_py
cd demo/training
pip install -r requirements.txt
```

---

## Step 1: 데이터 준비

### AI Hub 데이터 변환 (XML → YOLO)

AI Hub에서 다운받은 COLOR 이미지와 XML 라벨을 YOLO 포맷으로 변환합니다.

```bash
python convert_xml_to_yolo.py ^
    --train-images "D:\원천데이터\Training" ^
    --train-labels "D:\라벨링데이터\Training" ^
    --val-images "D:\원천데이터\Validation" ^
    --val-labels "D:\라벨링데이터\Validation" ^
    --output "../data"
```

> 변환 후 `demo/data/data.yaml`이 생성됩니다.
> `data.yaml`의 `path:` 항목이 실제 데이터 절대 경로를 가리키는지 확인하세요.

### data.yaml 경로 확인

```yaml
# demo/data/data.yaml
path: D:/repos/Smart_Factory/project_CSharp/data   # 절대 경로
train: images/train
val: images/val
nc: 5
names: [normal, crack, foreign_matter, discoloration, deformed]
```

---

## Step 2: 데이터 분석

학습 전 클래스 분포와 이미지 통계를 확인합니다.

```bash
python train.py --data "D:/repos/.../data/data.yaml" --analyze-only
```

출력 예시:

```
학습 데이터: 43,091장 / 검증 데이터: 5,387장
클래스 불균형 비율: 7.40:1
  foreign_matter: 38.47%  (최다)
  deformed:        5.20%  (최소)
```

---

## Step 3: 모델 학습

### 기본 학습 (권장)

```bash
python train.py \
    --data "D:/repos/.../data/data.yaml" \
    --model s \
    --epochs 150 \
    --batch 8 \
    --device 0
```

### 주요 옵션

| 인자 | 기본값 | 설명 |
|------|--------|------|
| `--data` | `../data/data.yaml` | data.yaml 경로 (절대 경로 권장) |
| `--model` | `s` | 모델 크기 (`n` / `s` / `m` / `l` / `x`) |
| `--epochs` | `40` | 최대 에포크 수 |
| `--batch` | `8` | 배치 크기 (RTX 3070 기준 s=8, n=16 권장) |
| `--device` | `0` | GPU 번호 또는 `cpu` |
| `--early-stop` | `30` | Early stopping patience |
| `--warmup` | `5` | Warm-up 에포크 수 |
| `--no-advanced-aug` | — | MixUp·CopyPaste 등 고급 증강 비활성화 |
| `--preprocess-clahe` | — | CLAHE 이미지 전처리 적용 |
| `--tune` | — | 하이퍼파라미터 자동 튜닝 실행 |
| `--analyze-only` | — | 데이터 분석만 수행 |

### 모델 크기 선택 가이드

| 크기 | 파라미터 | 추천 상황 |
|------|---------|-----------|
| `n` (nano) | ~3M | 저사양 GPU, 초고속 추론 |
| `s` (small) | ~11M | 정확도·속도 균형 **(현재 사용)** |
| `m` (medium) | ~26M | 더 높은 정확도 필요 시 |

### 학습 완료 후 자동 복사

학습이 완료되면 `best.pt`가 자동으로 `demo/models/egg_classifier_best.pt`에 복사됩니다.

---

## Step 4: ONNX 내보내기

```bash
python export_onnx.py \
    --model "../models/egg_classifier_best.pt" \
    --output "../models" \
    --name "egg_classifier_v2.onnx" \
    --verify
```

### 주요 옵션

| 인자 | 기본값 | 설명 |
|------|--------|------|
| `--model` | `../models/egg_classifier_best.pt` | 학습된 `.pt` 경로 |
| `--output` | `../models` | ONNX 저장 디렉토리 |
| `--name` | `egg_classifier.onnx` | 저장 파일명 |
| `--verify` | — | 내보낸 모델 추론 속도 검증 |
| `--opset` | `12` | ONNX opset 버전 |

> 동일 파일명이 존재하면 자동으로 `_1`, `_2` 등의 접미사를 붙여 저장합니다.

---

## Step 5: 얼굴인식 모델 다운로드

로그인 2차 인증에 필요한 얼굴인식 모델을 다운로드합니다.

```bash
python download_face_models.py
```

`demo/models/` 폴더에 다음 파일이 생성됩니다:
- `haarcascade_frontalface_default.xml` — 얼굴 탐지 (OpenCV Haar Cascade)
- `mobilefacenet.onnx` — 얼굴 임베딩 추출 (128차원 벡터)

---

## 학습 결과 (현재 배포 모델)

| 메트릭 | 값 |
|--------|-----|
| 모델 | YOLOv8s |
| 에포크 | 150 (Early stop) |
| mAP50 | **94.0%** |
| mAP50-95 | **92.2%** |
| Precision | 88.0% |
| Recall | 87.5% |
| 배치 크기 | 8 |
| 출력 파일 | `demo/models/egg_classifier_v2.onnx` |

---

## 학습 결과 파일 위치

```
demo/
├── models/
│   ├── egg_classifier_best.pt       ← 학습 완료 후 자동 복사
│   ├── egg_classifier_v2.onnx       ← ONNX 변환 결과 (C# 앱에서 사용)
│   ├── haarcascade_frontalface_default.xml
│   └── mobilefacenet.onnx
└── training/
    └── runs/                        ← YOLO 학습 로그 + 체크포인트
        └── egg_classifier_advanced/
            └── weights/
                ├── best.pt
                └── last.pt
```

---

## 트러블슈팅

### CUDA Out of Memory
배치 크기를 줄이세요.
```bash
python train.py --batch 4 ...
```

### 모듈 찾기 실패 (ModuleNotFoundError)
sf_py 환경이 활성화되었는지 확인하세요.
```bash
conda activate sf_py
```

### data.yaml 경로 오류
`data.yaml`의 `path:`를 데이터 실제 위치의 **절대 경로**로 수정하세요.
