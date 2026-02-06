"""
YOLOv8 계란 품질 분류 모델 학습 스크립트
"""

from ultralytics import YOLO
from pathlib import Path
import argparse


def train_model(
    data_yaml: str,
    model_size: str = 'n',
    epochs: int = 100,
    imgsz: int = 640,
    batch: int = 16,
    device: str = '0',
    project: str = 'runs/detect',
    name: str = 'egg_classifier'
):
    """
    YOLOv8 모델 학습

    Args:
        data_yaml: data.yaml 파일 경로
        model_size: 모델 크기 (n=nano, s=small, m=medium, l=large, x=xlarge)
        epochs: 학습 에포크 수
        imgsz: 입력 이미지 크기
        batch: 배치 크기
        device: GPU 장치 번호 또는 'cpu'
        project: 출력 프로젝트 디렉토리
        name: 실험 이름
    """
    # 사전 학습된 모델 로드
    model_name = f'yolov8{model_size}.pt'
    print(f"Loading pretrained model: {model_name}")
    model = YOLO(model_name)

    # 학습 설정
    print(f"\nTraining configuration:")
    print(f"  Data: {data_yaml}")
    print(f"  Epochs: {epochs}")
    print(f"  Image size: {imgsz}")
    print(f"  Batch size: {batch}")
    print(f"  Device: {device}")

    # 학습 시작
    results = model.train(
        data=data_yaml,
        epochs=epochs,
        imgsz=imgsz,
        batch=batch,
        device=device,
        project=project,
        name=name,
        patience=20,           # Early stopping patience
        save=True,             # 체크포인트 저장
        save_period=10,        # 10 에포크마다 저장
        plots=True,            # 학습 그래프 저장
        verbose=True,
        # 데이터 증강
        augment=True,
        hsv_h=0.015,           # 색조 변화
        hsv_s=0.7,             # 채도 변화
        hsv_v=0.4,             # 명도 변화
        degrees=10.0,          # 회전
        translate=0.1,         # 이동
        scale=0.5,             # 스케일
        flipud=0.5,            # 상하 반전
        fliplr=0.5,            # 좌우 반전
        mosaic=1.0,            # 모자이크 증강
    )

    # 결과 출력
    print("\n" + "="*50)
    print("Training complete!")
    print("="*50)

    # 최종 모델 경로
    best_model = Path(project) / name / 'weights' / 'best.pt'
    print(f"\nBest model saved at: {best_model}")

    # 검증 결과 출력
    if hasattr(results, 'results_dict'):
        metrics = results.results_dict
        print(f"\nValidation metrics:")
        print(f"  mAP50: {metrics.get('metrics/mAP50(B)', 'N/A'):.4f}")
        print(f"  mAP50-95: {metrics.get('metrics/mAP50-95(B)', 'N/A'):.4f}")
        print(f"  Precision: {metrics.get('metrics/precision(B)', 'N/A'):.4f}")
        print(f"  Recall: {metrics.get('metrics/recall(B)', 'N/A'):.4f}")

    return best_model


def validate_model(model_path: str, data_yaml: str):
    """학습된 모델 검증"""
    print(f"\nValidating model: {model_path}")
    model = YOLO(model_path)

    results = model.val(data=data_yaml, verbose=True)

    return results


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Train YOLOv8 egg classifier')
    parser.add_argument('--data', type=str, default='../data/data.yaml', help='data.yaml path')
    parser.add_argument('--model', type=str, default='n', choices=['n', 's', 'm', 'l', 'x'],
                        help='Model size (n=nano, s=small, m=medium, l=large, x=xlarge)')
    parser.add_argument('--epochs', type=int, default=100, help='Number of epochs')
    parser.add_argument('--imgsz', type=int, default=640, help='Input image size')
    parser.add_argument('--batch', type=int, default=16, help='Batch size')
    parser.add_argument('--device', type=str, default='0', help='Device (0 for GPU, cpu for CPU)')
    parser.add_argument('--name', type=str, default='egg_classifier', help='Experiment name')
    parser.add_argument('--validate-only', type=str, default=None, help='Only validate this model')

    args = parser.parse_args()

    if args.validate_only:
        validate_model(args.validate_only, args.data)
    else:
        train_model(
            data_yaml=args.data,
            model_size=args.model,
            epochs=args.epochs,
            imgsz=args.imgsz,
            batch=args.batch,
            device=args.device,
            name=args.name
        )
