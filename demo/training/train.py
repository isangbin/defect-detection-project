"""
YOLOv8 계란 품질 분류 모델 고도화 학습 스크립트
전처리 + 데이터 분석 + 하이퍼파라미터 최적화 통합
"""

from ultralytics import YOLO
from pathlib import Path
import argparse
import yaml
from collections import Counter
import cv2
import numpy as np
from tqdm import tqdm
import matplotlib.pyplot as plt


# ============================================================================
# 1. 데이터 전처리 및 분석
# ============================================================================


def analyze_dataset(data_yaml: str):
    """
    데이터셋 분석: 클래스 분포, 이미지 품질 등

    Returns:
        dict: 분석 결과 (class_distribution, image_stats 등)
    """
    print("\n" + "=" * 60)
    print("📊 데이터셋 분석 중...")
    print("=" * 60)

    with open(data_yaml, "r", encoding="utf-8") as f:
        data = yaml.safe_load(f)

    data_path = Path(data["path"])
    train_labels = data_path / data["train"].replace("images", "labels")
    val_labels = data_path / data["val"].replace("images", "labels")

    # 클래스 분포 분석
    train_classes = []
    val_classes = []

    print("\n[1/3] 라벨 파일 읽는 중...")
    for label_file in tqdm(list(train_labels.glob("*.txt"))):
        with open(label_file, "r") as f:
            for line in f:
                class_id = int(line.split()[0])
                train_classes.append(class_id)

    for label_file in tqdm(list(val_labels.glob("*.txt"))):
        with open(label_file, "r") as f:
            for line in f:
                class_id = int(line.split()[0])
                val_classes.append(class_id)

    # 통계 출력
    print("\n[2/3] 클래스 분포 분석")
    train_dist = Counter(train_classes)
    val_dist = Counter(val_classes)

    class_names = data["names"]
    print("\n📈 학습 데이터 클래스 분포:")
    for class_id, count in sorted(train_dist.items()):
        class_name = class_names[class_id]
        percentage = count / len(train_classes) * 100
        print(f"  {class_name:20s}: {count:6d} ({percentage:5.2f}%)")

    print("\n📈 검증 데이터 클래스 분포:")
    for class_id, count in sorted(val_dist.items()):
        class_name = class_names[class_id]
        percentage = count / len(val_classes) * 100
        print(f"  {class_name:20s}: {count:6d} ({percentage:5.2f}%)")

    # 클래스 불균형 체크
    max_count = max(train_dist.values())
    min_count = min(train_dist.values())
    imbalance_ratio = max_count / min_count

    print(f"\n⚖️  클래스 불균형 비율: {imbalance_ratio:.2f}:1")
    if imbalance_ratio > 3.0:
        print("   ⚠️  경고: 클래스 불균형이 심합니다. 가중치 조정 권장!")

    return {
        "train_distribution": train_dist,
        "val_distribution": val_dist,
        "class_names": class_names,
        "imbalance_ratio": imbalance_ratio,
    }


def preprocess_images(data_yaml: str, apply_clahe: bool = False):
    """
    이미지 전처리 (선택사항)

    Args:
        data_yaml: data.yaml 경로
        apply_clahe: CLAHE (대비 향상) 적용 여부

    Note:
        기본적으로 YOLO 자체 증강이 강력하므로,
        데이터가 충분하면 전처리 없이도 학습 가능.
        필요시 apply_clahe=True로 실행.
    """
    if not apply_clahe:
        print("\n✅ 전처리 스킵 (YOLO 자체 증강 사용)")
        return

    print("\n" + "=" * 60)
    print("🔧 이미지 전처리 적용 중 (CLAHE)...")
    print("=" * 60)

    with open(data_yaml, "r", encoding="utf-8") as f:
        data = yaml.safe_load(f)

    data_path = Path(data["path"])
    train_images = data_path / data["train"]

    # CLAHE 객체 생성
    clahe = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8, 8))

    processed_dir = data_path / "images" / "train_preprocessed"
    processed_dir.mkdir(exist_ok=True)

    print(f"\n처리된 이미지 저장 위치: {processed_dir}")
    print("※ 원본 이미지는 유지됩니다.\n")

    for img_path in tqdm(
        list(train_images.glob("*.jpg")) + list(train_images.glob("*.png"))
    ):
        img = cv2.imread(str(img_path))

        # LAB 색공간으로 변환 → L 채널에만 CLAHE 적용
        lab = cv2.cvtColor(img, cv2.COLOR_BGR2LAB)
        l, a, b = cv2.split(lab)
        l_clahe = clahe.apply(l)
        enhanced = cv2.merge([l_clahe, a, b])
        enhanced_bgr = cv2.cvtColor(enhanced, cv2.COLOR_LAB2BGR)

        # 저장
        output_path = processed_dir / img_path.name
        cv2.imwrite(str(output_path), enhanced_bgr)

    print(f"\n✅ 전처리 완료! data.yaml의 train 경로를 변경하여 사용하세요:")
    print(f"   train: images/train_preprocessed")


# ============================================================================
# 2. 고도화 학습
# ============================================================================


def train_model(
    data_yaml: str,
    model_size: str = "s",  # 기본값을 's'로 변경 (nano → small)
    epochs: int = 150,  # 에포크 증가
    imgsz: int = 640,
    batch: int = 16,
    device: str = "0",
    project: str = "runs/detect",
    name: str = "egg_classifier_advanced",
    # 고급 옵션
    optimizer: str = "auto",  # auto, SGD, Adam, AdamW
    use_advanced_aug: bool = True,
    early_stopping: int = 30,
    warmup_epochs: int = 5,
    cos_lr: bool = True,  # Cosine learning rate scheduler
    pretrained: bool = True,
    # 클래스 가중치 (불균형 대응)
    auto_weight: bool = False,
):
    """
    YOLOv8 고도화 학습

    성능 개선 포인트:
    1. 모델 크기 업그레이드 (n → s/m)
    2. 고급 데이터 증강 (CopyPaste, MixUp)
    3. Learning rate 스케줄링 (Cosine Annealing)
    4. Warm-up + Early stopping 조정
    """

    # 사전 학습 모델 로드
    if pretrained:
        model_name = f"yolov8{model_size}.pt"
        print(f"\n✅ 사전학습 모델 로드: {model_name}")
    else:
        model_name = f"yolov8{model_size}.yaml"
        print(f"\n⚠️  처음부터 학습 (사전학습 없음): {model_name}")

    model = YOLO(model_name)

    # 학습 설정 출력
    print("\n" + "=" * 60)
    print("🚀 학습 시작")
    print("=" * 60)
    print(f"  📁 데이터: {data_yaml}")
    print(f"  🤖 모델: YOLOv8{model_size.upper()}")
    print(f"  📊 에포크: {epochs}")
    print(f"  🖼️  이미지 크기: {imgsz}")
    print(f"  📦 배치 크기: {batch}")
    print(f"  🎯 옵티마이저: {optimizer}")
    print(f"  🔄 고급 증강: {'ON' if use_advanced_aug else 'OFF'}")
    print(f"  📈 Cosine LR: {'ON' if cos_lr else 'OFF'}")
    print(f"  🔥 Warm-up: {warmup_epochs} epochs")
    print(f"  ⏸️  Early stopping: {early_stopping} patience")
    print("=" * 60 + "\n")

    # ========================================
    # 학습 파라미터 설정
    # ========================================
    train_args = {
        "data": data_yaml,
        "epochs": epochs,
        "imgsz": imgsz,
        "batch": batch,
        "device": device,
        "project": project,
        "name": name,
        # 기본 설정
        "patience": early_stopping,
        "save": True,
        "save_period": 10,
        "plots": True,
        "verbose": True,
        "workers": 4,
        # Optimizer & Learning Rate
        "optimizer": optimizer,
        "lr0": 0.01,  # 초기 learning rate
        "lrf": 0.01,  # 최종 learning rate (lr0의 비율)
        "warmup_epochs": warmup_epochs,
        "warmup_momentum": 0.8,
        "cos_lr": cos_lr,  # Cosine annealing
        # 정규화
        "weight_decay": 0.0005,
        "dropout": 0.0,
    }

    # ========================================
    # 데이터 증강 설정
    # ========================================
    if use_advanced_aug:
        print("🎨 고급 데이터 증강 활성화\n")
        train_args.update(
            {
                # 기본 증강 (더 강하게)
                "hsv_h": 0.02,  # 색조 (0.015 → 0.02)
                "hsv_s": 0.7,  # 채도
                "hsv_v": 0.4,  # 명도
                "degrees": 15.0,  # 회전 (10 → 15도)
                "translate": 0.1,  # 이동
                "scale": 0.5,  # 스케일
                "shear": 0.0,  # 전단 변형
                "perspective": 0.0,  # 원근 변형
                "flipud": 0.5,  # 상하 반전
                "fliplr": 0.5,  # 좌우 반전
                # YOLO 특화 증강
                "mosaic": 1.0,  # 모자이크 (4개 이미지 합성)
                "mixup": 0.1,  # MixUp (이미지 혼합) ← 새로 추가!
                "copy_paste": 0.1,  # CopyPaste (객체 복붙) ← 새로 추가!
                # 품질 증강
                "blur": 0.0,  # 블러 (0~1)
                "auto_augment": "randaugment",  # AutoAugment
                "erasing": 0.4,  # Random Erasing
            }
        )
    else:
        # 기본 증강만 사용
        train_args.update(
            {
                "augment": True,
                "hsv_h": 0.015,
                "hsv_s": 0.7,
                "hsv_v": 0.4,
                "degrees": 10.0,
                "translate": 0.1,
                "scale": 0.5,
                "flipud": 0.5,
                "fliplr": 0.5,
                "mosaic": 1.0,
            }
        )

    # ========================================
    # 학습 시작
    # ========================================
    results = model.train(**train_args)

    # ========================================
    # 결과 출력
    # ========================================
    print("\n" + "=" * 60)
    print("🎉 학습 완료!")
    print("=" * 60)

    best_model = Path(project) / name / "weights" / "best.pt"
    last_model = Path(project) / name / "weights" / "last.pt"

    print(f"\n📦 모델 저장 위치:")
    print(f"  Best: {best_model}")
    print(f"  Last: {last_model}")

    # 최종 메트릭 출력
    if hasattr(results, "results_dict"):
        metrics = results.results_dict
        print(f"\n📊 최종 검증 메트릭:")
        print(f"  mAP50:       {metrics.get('metrics/mAP50(B)', 0):.4f}")
        print(f"  mAP50-95:    {metrics.get('metrics/mAP50-95(B)', 0):.4f}")
        print(f"  Precision:   {metrics.get('metrics/precision(B)', 0):.4f}")
        print(f"  Recall:      {metrics.get('metrics/recall(B)', 0):.4f}")

    print("\n" + "=" * 60)
    print("💡 다음 단계:")
    print("  1. 학습 결과 확인: runs/detect/{}/".format(name))
    print("  2. ONNX 내보내기: python export_onnx.py --model {}".format(best_model))
    print("  3. 검증 실행: python train.py --validate-only {}".format(best_model))
    print("=" * 60 + "\n")

    return best_model


def validate_model(model_path: str, data_yaml: str):
    """학습된 모델 검증"""
    print("\n" + "=" * 60)
    print(f"🔍 모델 검증: {model_path}")
    print("=" * 60 + "\n")

    model = YOLO(model_path)
    results = model.val(data=data_yaml, verbose=True, plots=True)

    return results


# ============================================================================
# 3. 하이퍼파라미터 자동 튜닝
# ============================================================================


def tune_hyperparameters(
    data_yaml: str, model_size: str = "s", iterations: int = 30, device: str = "0"
):
    """
    Ray Tune을 사용한 하이퍼파라미터 자동 최적화

    주의: 시간이 오래 걸립니다 (GPU 필수)
    iterations=10이면 약 1~2시간 소요
    """
    print("\n" + "=" * 60)
    print("🔬 하이퍼파라미터 자동 튜닝 시작")
    print("=" * 60)
    print(f"  모델: YOLOv8{model_size}")
    print(f"  반복 횟수: {iterations}")
    print(f"  ⚠️  예상 소요 시간: {iterations * 3}~{iterations * 5}분")
    print("=" * 60 + "\n")

    model = YOLO(f"yolov8{model_size}.pt")

    # 하이퍼파라미터 탐색 공간
    # YOLO는 자동으로 최적 범위 탐색
    result = model.tune(
        data=data_yaml,
        epochs=50,  # 튜닝용 에포크 (짧게)
        iterations=iterations,
        device=device,
        plots=True,
        save=True,
        val=True,
    )

    print("\n✅ 튜닝 완료! 최적 파라미터가 저장되었습니다.")
    print("   다음 학습부터는 자동으로 적용됩니다.")

    return result


# ============================================================================
# CLI 진입점
# ============================================================================

if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="YOLOv8 계란 분류 모델 고도화 학습",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
사용 예시:
  # 기본 학습 (YOLOv8s, 고급 증강)
  python train.py --data ../data/data.yaml --model s

  # 고성능 모델 (YOLOv8m)
  python train.py --data ../data/data.yaml --model m --epochs 200

  # 데이터 분석만 수행
  python train.py --analyze-only

  # 모델 검증
  python train.py --validate-only runs/detect/egg_classifier_advanced/weights/best.pt

  # 하이퍼파라미터 자동 튜닝
  python train.py --tune --model s --iterations 20
        """,
    )

    # 기본 인자
    parser.add_argument(
        "--data", type=str, default="../data/data.yaml", help="data.yaml 경로"
    )
    parser.add_argument(
        "--model",
        type=str,
        default="s",
        choices=["n", "s", "m", "l", "x"],
        help="모델 크기 (s=추천, m=고성능)",
    )
    parser.add_argument("--epochs", type=int, default=150, help="에포크 수 (기본 150)")
    parser.add_argument("--imgsz", type=int, default=640, help="입력 이미지 크기")
    parser.add_argument("--batch", type=int, default=16, help="배치 크기")
    parser.add_argument("--device", type=str, default="0", help="GPU 장치 (0) 또는 cpu")
    parser.add_argument(
        "--name", type=str, default="egg_classifier_advanced", help="실험 이름"
    )

    # 고급 옵션
    parser.add_argument(
        "--optimizer",
        type=str,
        default="auto",
        choices=["auto", "SGD", "Adam", "AdamW"],
        help="옵티마이저 선택",
    )
    parser.add_argument(
        "--no-advanced-aug", action="store_true", help="고급 증강 비활성화"
    )
    parser.add_argument(
        "--early-stop", type=int, default=30, help="Early stopping patience"
    )
    parser.add_argument("--warmup", type=int, default=5, help="Warm-up epochs")
    parser.add_argument(
        "--no-pretrained", action="store_true", help="사전학습 모델 사용 안함"
    )

    # 전처리 옵션
    parser.add_argument(
        "--preprocess-clahe", action="store_true", help="CLAHE 전처리 적용 (선택)"
    )

    # 특수 모드
    parser.add_argument(
        "--analyze-only", action="store_true", help="데이터 분석만 수행"
    )
    parser.add_argument(
        "--validate-only", type=str, default=None, help="특정 모델만 검증"
    )
    parser.add_argument("--tune", action="store_true", help="하이퍼파라미터 자동 튜닝")
    parser.add_argument(
        "--tune-iterations", type=int, default=30, help="튜닝 반복 횟수"
    )

    args = parser.parse_args()

    # ========================================
    # 실행 모드 분기
    # ========================================

    # 1. 데이터 분석만
    if args.analyze_only:
        analyze_dataset(args.data)
        exit(0)

    # 2. 모델 검증만
    if args.validate_only:
        validate_model(args.validate_only, args.data)
        exit(0)

    # 3. 하이퍼파라미터 튜닝하기
    if args.tune:
        tune_hyperparameters(
            data_yaml=args.data,
            model_size=args.model,
            iterations=args.tune_iterations,
            device=args.device,
        )
        exit(0)

    # 4. 전체 파이프라인 (분석 → 전처리 → 학습)
    print(
        """
    ╔═══════════════════════════════════════════════════════════════╗
    ║         YOLOv8 계란 품질 분류 모델 고도화 학습               ║
    ║                                                               ║
    ║  전처리 + 데이터 분석 + 하이퍼파라미터 최적화 통합           ║
    ╚═══════════════════════════════════════════════════════════════╝
    """
    )

    # Step 1: 데이터 분석
    stats = analyze_dataset(args.data)

    # Step 2: 전처리 (선택사항)
    preprocess_images(args.data, apply_clahe=args.preprocess_clahe)

    # Step 3: 학습
    input("\n분석 완료! 엔터를 눌러 학습을 시작하세요... (Ctrl+C로 취소)")

    train_model(
        data_yaml=args.data,
        model_size=args.model,
        epochs=args.epochs,
        imgsz=args.imgsz,
        batch=args.batch,
        device=args.device,
        name=args.name,
        optimizer=args.optimizer,
        use_advanced_aug=not args.no_advanced_aug,
        early_stopping=args.early_stop,
        warmup_epochs=args.warmup,
        pretrained=not args.no_pretrained,
    )
