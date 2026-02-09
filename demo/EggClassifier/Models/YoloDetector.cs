using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;

namespace EggClassifier.Models
{
    /// <summary>
    /// YOLOv8 ONNX 추론 엔진
    /// </summary>
    public class YoloDetector : IDisposable
    {
        private InferenceSession? _session;
        private readonly int _inputWidth = 640;
        private readonly int _inputHeight = 640;
        private bool _disposed;

        // Letterbox 전처리 시 사용된 스케일/패딩 정보 (후처리에서 좌표 복원용)
        private float _letterboxScale;
        private int _letterboxPadX;
        private int _letterboxPadY;

        // 클래스 정의
        public static readonly string[] ClassNames = new[]
        {
            "정상",        // normal
            "크랙",        // crack
            "이물질",      // foreign_matter
            "탈색",        // discoloration
            "외형이상"     // deformed
        };

        // 클래스별 색상 (BGR)
        public static readonly Scalar[] ClassColors = new[]
        {
            new Scalar(0, 255, 0),      // 정상: 녹색
            new Scalar(0, 0, 255),      // 크랙: 빨강
            new Scalar(255, 0, 255),    // 이물질: 마젠타
            new Scalar(0, 255, 255),    // 탈색: 노랑
            new Scalar(255, 128, 0)     // 외형이상: 주황
        };

        public bool IsLoaded => _session != null;

        /// <summary>
        /// ONNX 모델 로드
        /// </summary>
        public bool LoadModel(string modelPath)
        {
            try
            {
                var options = new SessionOptions();
                options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

                // GPU 사용 가능 시 CUDA 프로바이더 추가
                try
                {
                    options.AppendExecutionProvider_CUDA(0);
                }
                catch
                {
                    // CUDA 사용 불가 시 CPU로 폴백
                    Console.WriteLine("CUDA not available, using CPU");
                }

                _session = new InferenceSession(modelPath, options);

                // 모델 입출력 정보 출력
                Console.WriteLine("Model loaded successfully");
                Console.WriteLine($"Input: {_session.InputMetadata.First().Key}");
                Console.WriteLine($"Output: {_session.OutputMetadata.First().Key}");

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load model: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 이미지에서 객체 탐지 수행
        /// </summary>
        public List<Detection> Detect(Mat image, float confidenceThreshold = 0.5f, float nmsThreshold = 0.45f)
        {
            if (_session == null || image.Empty())
                return new List<Detection>();

            var originalWidth = image.Width;
            var originalHeight = image.Height;

            // 전처리: 리사이즈 + 정규화 + NCHW 변환
            var inputTensor = Preprocess(image);

            // 추론 실행
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_session.InputMetadata.First().Key, inputTensor)
            };

            using var results = _session.Run(inputs);
            var output = results.First().AsTensor<float>();

            // 후처리: 바운딩박스 디코딩 + NMS
            var detections = Postprocess(output, originalWidth, originalHeight, confidenceThreshold, nmsThreshold);

            return detections;
        }

        /// <summary>
        /// 이미지 전처리 (Letterbox 방식 — 종횡비 유지 + 패딩)
        /// </summary>
        private DenseTensor<float> Preprocess(Mat image)
        {
            int srcW = image.Width;
            int srcH = image.Height;

            // 종횡비를 유지하면서 640x640에 맞는 스케일 계산
            float scale = Math.Min((float)_inputWidth / srcW, (float)_inputHeight / srcH);
            int newW = (int)(srcW * scale);
            int newH = (int)(srcH * scale);

            // 패딩 크기 계산 (좌우/상하 균등 분배)
            int padX = (_inputWidth - newW) / 2;
            int padY = (_inputHeight - newH) / 2;

            // 후처리에서 좌표 복원용으로 저장
            _letterboxScale = scale;
            _letterboxPadX = padX;
            _letterboxPadY = padY;

            // 종횡비 유지하며 리사이즈
            var resized = new Mat();
            Cv2.Resize(image, resized, new OpenCvSharp.Size(newW, newH));

            // 114 회색 패딩으로 640x640 캔버스 생성 (ultralytics 기본값)
            var letterboxed = new Mat(_inputHeight, _inputWidth, MatType.CV_8UC3, new Scalar(114, 114, 114));
            resized.CopyTo(letterboxed[new Rect(padX, padY, newW, newH)]);

            // BGR -> RGB 변환
            var rgb = new Mat();
            Cv2.CvtColor(letterboxed, rgb, ColorConversionCodes.BGR2RGB);

            // 정규화 (0-255 → 0-1) + NCHW 텐서 변환
            // Mat → float 변환을 OpenCV로 일괄 처리 (픽셀 루프 대비 ~10배 빠름)
            var floatMat = new Mat();
            rgb.ConvertTo(floatMat, MatType.CV_32FC3, 1.0 / 255.0);

            // 채널 분리 (R, G, B)
            var channels = Cv2.Split(floatMat);

            var tensor = new DenseTensor<float>(new[] { 1, 3, _inputHeight, _inputWidth });
            for (int c = 0; c < 3; c++)
            {
                channels[c].GetArray(out float[] channelData);
                for (int i = 0; i < channelData.Length; i++)
                {
                    int y = i / _inputWidth;
                    int x = i % _inputWidth;
                    tensor[0, c, y, x] = channelData[i];
                }
                channels[c].Dispose();
            }

            resized.Dispose();
            letterboxed.Dispose();
            rgb.Dispose();
            floatMat.Dispose();

            return tensor;
        }

        /// <summary>
        /// 출력 후처리 (YOLOv8 형식)
        /// </summary>
        private List<Detection> Postprocess(Tensor<float> output, int originalWidth, int originalHeight,
            float confidenceThreshold, float nmsThreshold)
        {
            var detections = new List<Detection>();
            var boxes = new List<Rect>();
            var confidences = new List<float>();
            var classIds = new List<int>();

            // YOLOv8 출력 형식: [1, 84, 8400] (4 bbox + 80 classes) 또는 [1, 9, 8400] (4 bbox + 5 classes)
            // 또는 [1, 8400, 84/9]
            var dims = output.Dimensions.ToArray();
            int numClasses = ClassNames.Length;
            int numDetections;
            bool isTransposed;

            if (dims.Length == 3)
            {
                if (dims[1] == 4 + numClasses)
                {
                    // [1, 9, 8400] 형식
                    numDetections = dims[2];
                    isTransposed = true;
                }
                else if (dims[2] == 4 + numClasses)
                {
                    // [1, 8400, 9] 형식
                    numDetections = dims[1];
                    isTransposed = false;
                }
                else
                {
                    // 80 클래스 모델의 경우
                    numDetections = dims[1] > dims[2] ? dims[2] : dims[1];
                    isTransposed = dims[1] < dims[2];
                }
            }
            else
            {
                return detections;
            }

            for (int i = 0; i < numDetections; i++)
            {
                float maxConfidence = 0;
                int maxClassId = 0;

                // 클래스별 신뢰도 확인
                for (int c = 0; c < numClasses; c++)
                {
                    float conf = isTransposed ? output[0, 4 + c, i] : output[0, i, 4 + c];
                    if (conf > maxConfidence)
                    {
                        maxConfidence = conf;
                        maxClassId = c;
                    }
                }

                if (maxConfidence < confidenceThreshold)
                    continue;

                // 바운딩박스 추출 (cx, cy, w, h) — 640x640 letterbox 좌표계
                float cx, cy, w, h;
                if (isTransposed)
                {
                    cx = output[0, 0, i];
                    cy = output[0, 1, i];
                    w = output[0, 2, i];
                    h = output[0, 3, i];
                }
                else
                {
                    cx = output[0, i, 0];
                    cy = output[0, i, 1];
                    w = output[0, i, 2];
                    h = output[0, i, 3];
                }

                // Letterbox 좌표 → 원본 이미지 좌표로 복원
                // 1) 패딩 제거  2) 스케일 역변환
                float x1f = (cx - w / 2 - _letterboxPadX) / _letterboxScale;
                float y1f = (cy - h / 2 - _letterboxPadY) / _letterboxScale;
                float bwf = w / _letterboxScale;
                float bhf = h / _letterboxScale;

                int x1 = (int)x1f;
                int y1 = (int)y1f;
                int bw = (int)bwf;
                int bh = (int)bhf;

                // 경계 검사
                x1 = Math.Max(0, Math.Min(x1, originalWidth - 1));
                y1 = Math.Max(0, Math.Min(y1, originalHeight - 1));
                bw = Math.Min(bw, originalWidth - x1);
                bh = Math.Min(bh, originalHeight - y1);

                boxes.Add(new Rect(x1, y1, bw, bh));
                confidences.Add(maxConfidence);
                classIds.Add(maxClassId);
            }

            // NMS 적용
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

        /// <summary>
        /// Non-Maximum Suppression (NMS) 구현
        /// </summary>
        private static List<int> ApplyNMS(List<Rect> boxes, List<float> confidences, float nmsThreshold)
        {
            var indices = new List<int>();

            // 신뢰도 기준 인덱스 정렬
            var sortedIndices = confidences
                .Select((conf, idx) => new { Confidence = conf, Index = idx })
                .OrderByDescending(x => x.Confidence)
                .Select(x => x.Index)
                .ToList();

            var suppressed = new bool[boxes.Count];

            foreach (int i in sortedIndices)
            {
                if (suppressed[i])
                    continue;

                indices.Add(i);

                for (int j = 0; j < boxes.Count; j++)
                {
                    if (i == j || suppressed[j])
                        continue;

                    float iou = CalculateIoU(boxes[i], boxes[j]);
                    if (iou > nmsThreshold)
                    {
                        suppressed[j] = true;
                    }
                }
            }

            return indices;
        }

        /// <summary>
        /// IoU (Intersection over Union) 계산
        /// </summary>
        private static float CalculateIoU(Rect a, Rect b)
        {
            int x1 = Math.Max(a.X, b.X);
            int y1 = Math.Max(a.Y, b.Y);
            int x2 = Math.Min(a.X + a.Width, b.X + b.Width);
            int y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);

            int intersectionWidth = Math.Max(0, x2 - x1);
            int intersectionHeight = Math.Max(0, y2 - y1);
            int intersectionArea = intersectionWidth * intersectionHeight;

            int areaA = a.Width * a.Height;
            int areaB = b.Width * b.Height;
            int unionArea = areaA + areaB - intersectionArea;

            if (unionArea <= 0)
                return 0;

            return (float)intersectionArea / unionArea;
        }

        /// <summary>
        /// 탐지 결과를 이미지에 그리기 (System.Drawing으로 한글 렌더링)
        /// </summary>
        public static void DrawDetections(Mat image, List<Detection> detections)
        {
            if (detections.Count == 0) return;

            using var bitmap = BitmapConverter.ToBitmap(image);
            using var g = Graphics.FromImage(bitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using var font = new Font("맑은 고딕", 13f, System.Drawing.FontStyle.Bold);

            foreach (var det in detections)
            {
                var bgr = ClassColors[det.ClassId % ClassColors.Length];
                var drawColor = Color.FromArgb((int)bgr.Val2, (int)bgr.Val1, (int)bgr.Val0);
                var rect = det.BoundingBox;

                // 바운딩박스
                using var pen = new Pen(drawColor, 2);
                g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);

                // 라벨 텍스트 크기 측정
                string label = $"{det.ClassName} {det.Confidence:P0}";
                var textSize = g.MeasureString(label, font);

                // 라벨 위치 (박스 위에, 넘치면 아래에)
                int labelX = rect.X;
                int labelY = rect.Y - (int)textSize.Height - 4;
                if (labelY < 0)
                    labelY = rect.Y + rect.Height;

                // 라벨 배경
                using var bgBrush = new SolidBrush(drawColor);
                g.FillRectangle(bgBrush, labelX, labelY,
                    textSize.Width + 6, textSize.Height + 2);

                // 라벨 텍스트 (흰색)
                g.DrawString(label, font, Brushes.White, labelX + 3, labelY + 1);
            }

            // 렌더링된 Bitmap을 원본 Mat에 복사
            using var rendered = BitmapConverter.ToMat(bitmap);
            rendered.CopyTo(image);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _session?.Dispose();
                _session = null;
                _disposed = true;
            }
        }
    }
}
