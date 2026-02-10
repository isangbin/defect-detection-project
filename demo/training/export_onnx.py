"""
YOLOv8 모델 ONNX 내보내기 스크립트
"""

from ultralytics import YOLO
from pathlib import Path
import argparse
import shutil


def export_to_onnx(
    model_path: str,
    output_dir: str = '../models',
    imgsz: int = 640,
    simplify: bool = True,
    opset: int = 12,
    half: bool = False
):
    """
    YOLOv8 모델을 ONNX 포맷으로 내보내기

    Args:
        model_path: 학습된 .pt 모델 경로
        output_dir: ONNX 모델 저장 디렉토리
        imgsz: 입력 이미지 크기
        simplify: ONNX 모델 단순화 여부
        opset: ONNX opset 버전
        half: FP16 반정밀도 사용 여부
    """
    print(f"Loading model: {model_path}")
    model = YOLO(model_path)

    print(f"\nExporting to ONNX...")
    print(f"  Image size: {imgsz}")
    print(f"  Simplify: {simplify}")
    print(f"  Opset: {opset}")
    print(f"  Half precision: {half}")

    # ONNX 내보내기
    export_path = model.export(
        format='onnx',
        imgsz=imgsz,
        simplify=simplify,
        opset=opset,
        half=half,
        dynamic=False,  # 고정 입력 크기 (추론 속도 향상)
    )

    print(f"\nExported to: {export_path}")

    # 출력 디렉토리로 복사
    output_path = Path(output_dir)
    output_path.mkdir(parents=True, exist_ok=True)

    final_path = output_path / 'egg_classifier.onnx'
    shutil.copy2(export_path, final_path)

    print(f"Copied to: {final_path}")

    # 모델 정보 출력
    import os
    file_size = os.path.getsize(final_path) / (1024 * 1024)
    print(f"\nModel size: {file_size:.2f} MB")

    return final_path


def verify_onnx(onnx_path: str, imgsz: int = 640):
    """ONNX 모델 검증"""
    import onnxruntime as ort
    import numpy as np

    print(f"\nVerifying ONNX model: {onnx_path}")

    # ONNX Runtime 세션 생성
    providers = ['CUDAExecutionProvider', 'CPUExecutionProvider']
    session = ort.InferenceSession(onnx_path, providers=providers)

    # 입력 정보
    inputs = session.get_inputs()
    print(f"\nInput info:")
    for inp in inputs:
        print(f"  Name: {inp.name}")
        print(f"  Shape: {inp.shape}")
        print(f"  Type: {inp.type}")

    # 출력 정보
    outputs = session.get_outputs()
    print(f"\nOutput info:")
    for out in outputs:
        print(f"  Name: {out.name}")
        print(f"  Shape: {out.shape}")
        print(f"  Type: {out.type}")

    # 테스트 추론
    print(f"\nRunning test inference...")
    dummy_input = np.random.randn(1, 3, imgsz, imgsz).astype(np.float32)

    import time
    start = time.time()
    num_runs = 10

    for _ in range(num_runs):
        outputs = session.run(None, {inputs[0].name: dummy_input})

    elapsed = (time.time() - start) / num_runs * 1000
    print(f"Average inference time: {elapsed:.2f} ms")
    print(f"Estimated FPS: {1000/elapsed:.1f}")

    print(f"\nOutput shape: {outputs[0].shape}")
    print("ONNX model verification successful!")

    return True


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Export YOLOv8 model to ONNX')
    parser.add_argument('--model', type=str, default='runs/detect/egg_classifier/weights/best.pt',
                        help='Path to trained .pt model')
    parser.add_argument('--output', type=str, default='../models', help='Output directory')
    parser.add_argument('--imgsz', type=int, default=640, help='Input image size')
    parser.add_argument('--no-simplify', action='store_true', help='Disable ONNX simplification')
    parser.add_argument('--opset', type=int, default=12, help='ONNX opset version')
    parser.add_argument('--half', action='store_true', help='Use FP16 half precision')
    parser.add_argument('--verify', action='store_true', help='Verify exported model')

    args = parser.parse_args()

    onnx_path = export_to_onnx(
        model_path=args.model,
        output_dir=args.output,
        imgsz=args.imgsz,
        simplify=not args.no_simplify,
        opset=args.opset,
        half=args.half
    )

    if args.verify:
        verify_onnx(str(onnx_path), args.imgsz)
