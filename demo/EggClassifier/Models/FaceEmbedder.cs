using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EggClassifier.Models
{
    /// <summary>
    /// 얼굴 임베딩 ONNX 추론 엔진 (MobileFaceNet / ArcFace)
    /// </summary>
    public class FaceEmbedder : IDisposable
    {
        private InferenceSession? _session;
        private bool _disposed;
        private int _inputWidth = 112;
        private int _inputHeight = 112;
        private int _embeddingDim = 128;

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

                try
                {
                    options.AppendExecutionProvider_CUDA(0);
                }
                catch
                {
                    Console.WriteLine("CUDA not available for FaceEmbedder, using CPU");
                }

                _session = new InferenceSession(modelPath, options);

                // 모델 입출력 정보에서 차원 추출
                var inputMeta = _session.InputMetadata.First();
                var inputDims = inputMeta.Value.Dimensions;
                if (inputDims.Length == 4)
                {
                    _inputHeight = inputDims[2] > 0 ? inputDims[2] : 112;
                    _inputWidth = inputDims[3] > 0 ? inputDims[3] : 112;
                }

                var outputMeta = _session.OutputMetadata.First();
                var outputDims = outputMeta.Value.Dimensions;
                if (outputDims.Length == 2 && outputDims[1] > 0)
                {
                    _embeddingDim = outputDims[1];
                }

                Console.WriteLine($"FaceEmbedder loaded: input={_inputWidth}x{_inputHeight}, embedding={_embeddingDim}D");
                Console.WriteLine($"  Input: {inputMeta.Key} [{string.Join(",", inputDims)}]");
                Console.WriteLine($"  Output: {outputMeta.Key} [{string.Join(",", outputDims)}]");

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load FaceEmbedder: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 얼굴 이미지에서 임베딩 벡터 추출
        /// </summary>
        public float[]? GetEmbedding(Mat faceImage)
        {
            if (_session == null || faceImage.Empty())
                return null;

            var inputTensor = Preprocess(faceImage);

            var inputName = _session.InputMetadata.First().Key;
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
            };

            using var results = _session.Run(inputs);
            var output = results.First().AsTensor<float>();

            // L2 정규화된 임베딩 벡터 반환
            var embedding = new float[_embeddingDim];
            float norm = 0f;
            for (int i = 0; i < _embeddingDim; i++)
            {
                embedding[i] = output[0, i];
                norm += embedding[i] * embedding[i];
            }
            norm = MathF.Sqrt(norm);

            if (norm > 0)
            {
                for (int i = 0; i < _embeddingDim; i++)
                {
                    embedding[i] /= norm;
                }
            }

            return embedding;
        }

        /// <summary>
        /// 전처리: 112x112 리사이즈 → RGB → 정규화 → NCHW 텐서
        /// </summary>
        private DenseTensor<float> Preprocess(Mat image)
        {
            // 112x112 리사이즈
            var resized = new Mat();
            Cv2.Resize(image, resized, new OpenCvSharp.Size(_inputWidth, _inputHeight));

            // BGR → RGB
            var rgb = new Mat();
            Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);

            // 정규화 (0-255 → -1~1, InsightFace 표준)
            var floatMat = new Mat();
            rgb.ConvertTo(floatMat, MatType.CV_32FC3, 1.0 / 127.5, -1.0);

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
            rgb.Dispose();
            floatMat.Dispose();

            return tensor;
        }

        /// <summary>
        /// 코사인 유사도 계산
        /// </summary>
        public static float CosineSimilarity(float[] a, float[] b)
        {
            if (a.Length != b.Length)
                return 0f;

            float dot = 0f, normA = 0f, normB = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }

            float denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
            return denom > 0 ? dot / denom : 0f;
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
