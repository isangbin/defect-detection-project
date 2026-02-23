"""
AI Hub 계란 데이터셋 XML → YOLO 포맷 변환 스크립트

클래스 매핑 (AI Hub state 값):
state=1 → 0: normal (정상)
state=2 → 1: crack (크랙)
state=3 → 2: foreign_matter (이물질)
state=4 → 3: discoloration (탈색)
state=5 → 4: deformed (외형이상)
"""

import os
import xml.etree.ElementTree as ET
import shutil
import random
from pathlib import Path

# AI Hub state 값 → YOLO 클래스 인덱스 매핑
STATE_TO_CLASS = {
    '1': 0,  # 정상 (normal)
    '2': 1,  # 크랙 (crack)
    '3': 2,  # 이물질 (foreign_matter)
    '4': 3,  # 탈색 (discoloration)
    '5': 4,  # 외형이상 (deformed)
}


def parse_xml_annotation(xml_path: str) -> list:
    """
    AI Hub XML 어노테이션 파싱

    Returns:
        list of (class_id, x_center, y_center, width, height) - 정규화된 좌표
    """
    tree = ET.parse(xml_path)
    root = tree.getroot()

    annotations = []

    # 이미지 크기 추출
    size = root.find('size')
    if size is None:
        print(f"Warning: No size info in {xml_path}")
        return annotations

    img_width = int(size.find('width').text)
    img_height = int(size.find('height').text)

    if img_width == 0 or img_height == 0:
        print(f"Warning: Invalid image size in {xml_path}")
        return annotations

    # 객체 정보 추출 (AI Hub 형식: bndbox가 root 바로 아래에 있음)
    for bndbox in root.findall('bndbox'):
        # state 값으로 클래스 결정
        state_elem = bndbox.find('state')
        if state_elem is None:
            continue

        state = state_elem.text.strip()

        if state not in STATE_TO_CLASS:
            print(f"Warning: Unknown state '{state}' in {xml_path}")
            continue

        class_id = STATE_TO_CLASS[state]

        # 좌표 추출 (AI Hub는 x_min, y_min, x_max, y_max 사용)
        try:
            x_min = float(bndbox.find('x_min').text)
            y_min = float(bndbox.find('y_min').text)
            x_max = float(bndbox.find('x_max').text)
            y_max = float(bndbox.find('y_max').text)
        except (AttributeError, ValueError) as e:
            print(f"Warning: Invalid bbox coordinates in {xml_path}: {e}")
            continue

        # YOLO 포맷으로 변환 (중심 좌표 + 너비/높이, 정규화)
        x_center = ((x_min + x_max) / 2) / img_width
        y_center = ((y_min + y_max) / 2) / img_height
        width = (x_max - x_min) / img_width
        height = (y_max - y_min) / img_height

        # 유효성 검사
        if 0 <= x_center <= 1 and 0 <= y_center <= 1 and 0 < width <= 1 and 0 < height <= 1:
            annotations.append((class_id, x_center, y_center, width, height))
        else:
            print(f"Warning: Invalid bbox in {xml_path}: {x_center}, {y_center}, {width}, {height}")

    return annotations


def save_yolo_label(annotations: list, output_path: str):
    """YOLO 포맷 라벨 파일 저장"""
    with open(output_path, 'w') as f:
        for ann in annotations:
            class_id, x_center, y_center, width, height = ann
            f.write(f"{class_id} {x_center:.6f} {y_center:.6f} {width:.6f} {height:.6f}\n")


def convert_dataset(
    train_images_dir: str,
    train_labels_dir: str,
    val_images_dir: str,
    val_labels_dir: str,
    output_base_dir: str
):
    """
    AI Hub 데이터셋 변환 (Training/Validation 분리된 데이터용)

    Args:
        train_images_dir: Training 이미지 디렉토리
        train_labels_dir: Training XML 라벨 디렉토리
        val_images_dir: Validation 이미지 디렉토리
        val_labels_dir: Validation XML 라벨 디렉토리
        output_base_dir: 출력 기본 디렉토리
    """
    output_base = Path(output_base_dir)

    # 출력 디렉토리 생성
    dirs = {
        'train_images': output_base / 'images' / 'train',
        'val_images': output_base / 'images' / 'val',
        'train_labels': output_base / 'labels' / 'train',
        'val_labels': output_base / 'labels' / 'val',
    }

    for d in dirs.values():
        d.mkdir(parents=True, exist_ok=True)

    # 클래스별 통계
    class_stats = {i: 0 for i in range(5)}

    def process_split(images_dir, labels_dir, out_img_dir, out_label_dir, split_name):
        """단일 split 처리"""
        image_extensions = {'.jpg', '.jpeg', '.png', '.bmp'}
        image_files = []

        for ext in image_extensions:
            image_files.extend(Path(images_dir).glob(f'*{ext}'))
            image_files.extend(Path(images_dir).glob(f'*{ext.upper()}'))

        print(f"\n{split_name}: Found {len(image_files)} images")

        stats = {'success': 0, 'no_label': 0, 'no_objects': 0, 'error': 0}

        for i, img_path in enumerate(image_files):
            if (i + 1) % 5000 == 0:
                print(f"  Progress: {i+1}/{len(image_files)}")

            img_name = img_path.stem

            # XML 라벨 파일 찾기
            xml_path = Path(labels_dir) / f"{img_name}.xml"

            if not xml_path.exists():
                stats['no_label'] += 1
                continue

            try:
                # XML 파싱 및 변환
                annotations = parse_xml_annotation(str(xml_path))

                if not annotations:
                    stats['no_objects'] += 1
                    continue

                # 이미지 복사
                dst_img_path = out_img_dir / img_path.name
                shutil.copy2(img_path, dst_img_path)

                # YOLO 라벨 저장
                label_path = out_label_dir / f"{img_name}.txt"
                save_yolo_label(annotations, str(label_path))

                # 클래스 통계 업데이트
                for ann in annotations:
                    class_stats[ann[0]] += 1

                stats['success'] += 1

            except Exception as e:
                print(f"Error processing {img_path}: {e}")
                stats['error'] += 1

        print(f"  Success: {stats['success']}")
        print(f"  No label: {stats['no_label']}")
        print(f"  No objects: {stats['no_objects']}")
        print(f"  Errors: {stats['error']}")

        return stats

    # Training 데이터 처리
    train_stats = process_split(
        train_images_dir, train_labels_dir,
        dirs['train_images'], dirs['train_labels'],
        'Training'
    )

    # Validation 데이터 처리
    val_stats = process_split(
        val_images_dir, val_labels_dir,
        dirs['val_images'], dirs['val_labels'],
        'Validation'
    )

    # 결과 요약
    print(f"\n{'='*50}")
    print("Conversion complete!")
    print(f"{'='*50}")
    print(f"Training: {train_stats['success']} images")
    print(f"Validation: {val_stats['success']} images")

    print(f"\nClass distribution (total objects):")
    class_names = ['normal', 'crack', 'foreign_matter', 'discoloration', 'deformed']
    for i, name in enumerate(class_names):
        print(f"  {i}: {name} = {class_stats[i]}")

    # data.yaml 생성
    data_yaml_path = output_base / 'data.yaml'
    with open(data_yaml_path, 'w', encoding='utf-8') as f:
        f.write(f"""# Egg Quality Classification Dataset
path: {output_base.absolute()}
train: images/train
val: images/val

# Classes
names:
  0: normal
  1: crack
  2: foreign_matter
  3: discoloration
  4: deformed

# Korean names (for reference)
# 0: 정상
# 1: 크랙
# 2: 이물질
# 3: 탈색
# 4: 외형이상
""")

    print(f"\nCreated data.yaml at {data_yaml_path}")


if __name__ == '__main__':
    import argparse

    parser = argparse.ArgumentParser(description='Convert AI Hub egg dataset to YOLO format')
    parser.add_argument('--train-images', type=str, required=True, help='Training images directory')
    parser.add_argument('--train-labels', type=str, required=True, help='Training XML labels directory')
    parser.add_argument('--val-images', type=str, required=True, help='Validation images directory')
    parser.add_argument('--val-labels', type=str, required=True, help='Validation XML labels directory')
    parser.add_argument('--output', type=str, default='../data', help='Output directory')

    args = parser.parse_args()

    convert_dataset(
        train_images_dir=args.train_images,
        train_labels_dir=args.train_labels,
        val_images_dir=args.val_images,
        val_labels_dir=args.val_labels,
        output_base_dir=args.output
    )
