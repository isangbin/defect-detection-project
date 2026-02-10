using EggClassifier.Models;
using OpenCvSharp;
using System;
using System.IO;
using System.Linq;

namespace EggClassifier.Services
{
    public class FaceService : IFaceService
    {
        private CascadeClassifier? _cascadeClassifier;
        private readonly FaceEmbedder _embedder = new();
        private bool _disposed;

        public bool IsLoaded => _cascadeClassifier != null && _embedder.IsLoaded;

        public bool LoadModels()
        {
            try
            {
                // Haar Cascade 로드
                string[] cascadePaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "haarcascade_frontalface_default.xml"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "haarcascade_frontalface_default.xml"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Models", "haarcascade_frontalface_default.xml"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "models", "haarcascade_frontalface_default.xml"),
                };

                string? cascadePath = cascadePaths.FirstOrDefault(File.Exists);
                if (cascadePath != null)
                {
                    _cascadeClassifier = new CascadeClassifier(cascadePath);
                    Console.WriteLine($"Haar Cascade loaded: {cascadePath}");
                }
                else
                {
                    Console.WriteLine("Haar Cascade XML not found");
                    return false;
                }

                // MobileFaceNet ONNX 로드
                string[] onnxPaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "mobilefacenet.onnx"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mobilefacenet.onnx"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Models", "mobilefacenet.onnx"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "models", "mobilefacenet.onnx"),
                };

                string? onnxPath = onnxPaths.FirstOrDefault(File.Exists);
                if (onnxPath != null)
                {
                    return _embedder.LoadModel(onnxPath);
                }
                else
                {
                    Console.WriteLine("MobileFaceNet ONNX not found");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load face models: {ex.Message}");
                return false;
            }
        }

        public Rect? DetectFace(Mat image)
        {
            if (_cascadeClassifier == null || image.Empty())
                return null;

            // 그레이스케일 변환 + 히스토그램 평활화
            var gray = new Mat();
            Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.EqualizeHist(gray, gray);

            // 얼굴 탐지
            var faces = _cascadeClassifier.DetectMultiScale(
                gray,
                scaleFactor: 1.1,
                minNeighbors: 5,
                flags: HaarDetectionTypes.ScaleImage,
                minSize: new OpenCvSharp.Size(80, 80)
            );

            gray.Dispose();

            if (faces.Length == 0)
                return null;

            // 가장 큰 얼굴 반환
            return faces.OrderByDescending(f => f.Width * f.Height).First();
        }

        public float[]? GetFaceEmbedding(Mat faceImage)
        {
            if (!_embedder.IsLoaded || faceImage.Empty())
                return null;

            return _embedder.GetEmbedding(faceImage);
        }

        public float CompareFaces(float[] embedding1, float[] embedding2)
        {
            return FaceEmbedder.CosineSimilarity(embedding1, embedding2);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cascadeClassifier?.Dispose();
                _cascadeClassifier = null;
                _embedder.Dispose();
                _disposed = true;
            }
        }
    }
}
