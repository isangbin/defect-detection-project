using EggClassifier.Models;
using OpenCvSharp;
using System.Collections.Generic;

namespace EggClassifier.Services
{
    public interface IDetectorService : IDisposable
    {
        bool IsLoaded { get; }
        string[] ClassNames { get; }
        bool LoadModel(string modelPath);
        List<Detection> Detect(Mat image, float confidenceThreshold = 0.5f);
        void DrawDetections(Mat image, List<Detection> detections);
    }
}
