using EggClassifier.Models;
using OpenCvSharp;
using System;
using System.Collections.Generic;

namespace EggClassifier.Services
{
    public class DetectorService : IDetectorService
    {
        private readonly YoloDetector _detector = new();

        public bool IsLoaded => _detector.IsLoaded;
        public string[] ClassNames => YoloDetector.ClassNames;

        public bool LoadModel(string modelPath)
        {
            return _detector.LoadModel(modelPath);
        }

        public List<Detection> Detect(Mat image, float confidenceThreshold = 0.5f)
        {
            return _detector.Detect(image, confidenceThreshold);
        }

        public void DrawDetections(Mat image, List<Detection> detections)
        {
            YoloDetector.DrawDetections(image, detections);
        }
        // test
        public void Dispose()
        {
            _detector.Dispose();
        }
    }
}
