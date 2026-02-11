using OpenCvSharp;
using System;

namespace EggClassifier.Services
{
    public interface IFaceService : IDisposable
    {
        bool IsLoaded { get; }

        /// <summary>
        /// 얼굴 탐지 + 임베딩 모델 로드
        /// </summary>
        bool LoadModels();

        /// <summary>
        /// 이미지에서 얼굴 영역 탐지
        /// </summary>
        Rect? DetectFace(Mat image);

        /// <summary>
        /// 얼굴 이미지에서 임베딩 벡터 추출
        /// </summary>
        float[]? GetFaceEmbedding(Mat faceImage);

        /// <summary>
        /// 두 임베딩 벡터 비교 (코사인 유사도)
        /// </summary>
        float CompareFaces(float[] embedding1, float[] embedding2);
    }
}
