"""
얼굴인식 모델 다운로드 스크립트
- haarcascade_frontalface_default.xml (OpenCV 얼굴 탐지)
- mobilefacenet.onnx (얼굴 임베딩)

사용법:
    python download_face_models.py
"""

import os
import urllib.request
import sys

MODELS_DIR = os.path.join(os.path.dirname(__file__), "..", "Models")

MODELS = {
    "haarcascade_frontalface_default.xml": (
        "https://raw.githubusercontent.com/opencv/opencv/master/data/haarcascades/haarcascade_frontalface_default.xml"
    ),
    "mobilefacenet.onnx": (
        "https://github.com/onnx/models/raw/main/validated/vision/body_analysis/arcface/model/arcfaceresnet100-11-int8.onnx"
    ),
}


def download(url: str, dest: str) -> None:
    if os.path.exists(dest):
        print(f"  [SKIP] {os.path.basename(dest)} already exists")
        return
    print(f"  Downloading {os.path.basename(dest)} ...")
    try:
        urllib.request.urlretrieve(url, dest)
        size_mb = os.path.getsize(dest) / (1024 * 1024)
        print(f"  [OK] {size_mb:.1f} MB")
    except Exception as e:
        print(f"  [ERROR] {e}", file=sys.stderr)
        if os.path.exists(dest):
            os.remove(dest)


def main() -> None:
    os.makedirs(MODELS_DIR, exist_ok=True)
    print(f"Models directory: {os.path.abspath(MODELS_DIR)}\n")

    for filename, url in MODELS.items():
        dest = os.path.join(MODELS_DIR, filename)
        download(url, dest)

    print("\nDone. Place mobilefacenet.onnx manually if the download URL has changed.")
    print("MobileFaceNet ONNX can also be obtained from:")
    print("  https://github.com/onnx/models (ArcFace / MobileFaceNet)")
    print("  https://github.com/deepinsight/insightface")


if __name__ == "__main__":
    main()
