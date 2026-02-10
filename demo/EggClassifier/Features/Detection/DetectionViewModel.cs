using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EggClassifier.Core;
using EggClassifier.Services;
using DetectionResult = EggClassifier.Models.Detection;
using ClassCountItem = EggClassifier.Models.ClassCountItem;
using DetectionItem = EggClassifier.Models.DetectionItem;
using OpenCvSharp.WpfExtensions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EggClassifier.Features.Detection
{
    public partial class DetectionViewModel : ViewModelBase, IDisposable
    {
        private readonly IWebcamService _webcamService;
        private readonly IDetectorService _detector;
        private bool _disposed;

        private static readonly SolidColorBrush[] ClassBrushes = new[]
        {
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)),
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 67, 54)),
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 0, 255)),
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 235, 59)),
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 152, 0))
        };

        [ObservableProperty]
        private BitmapSource? _currentFrame;

        [ObservableProperty]
        private string _fpsText = "FPS: --";

        [ObservableProperty]
        private string _statusMessage = "시작 버튼을 눌러 웹캠을 활성화하세요";

        [ObservableProperty]
        private Visibility _overlayVisibility = Visibility.Visible;

        [ObservableProperty]
        private bool _isModelLoaded;

        [ObservableProperty]
        private string _modelStatusText = "로딩 중...";

        [ObservableProperty]
        private SolidColorBrush _modelStatusColor = new(System.Windows.Media.Color.FromRgb(255, 193, 7));

        [ObservableProperty]
        private string _modelPath = "";

        [ObservableProperty]
        private bool _canStart = false;

        [ObservableProperty]
        private bool _canStop = false;

        [ObservableProperty]
        private float _confidenceThreshold = 0.5f;

        [ObservableProperty]
        private int _totalDetections;

        [ObservableProperty]
        private Visibility _noDetectionVisibility = Visibility.Visible;

        public ObservableCollection<ClassCountItem> ClassCounts { get; } = new();
        public ObservableCollection<DetectionItem> CurrentDetections { get; } = new();

        public DetectionViewModel(IWebcamService webcamService, IDetectorService detector)
        {
            _webcamService = webcamService;
            _detector = detector;

            for (int i = 0; i < _detector.ClassNames.Length; i++)
            {
                ClassCounts.Add(new ClassCountItem
                {
                    ClassName = _detector.ClassNames[i],
                    Color = ClassBrushes[i],
                    Count = 0
                });
            }

            LoadModel();
        }

        public override void OnNavigatedTo()
        {
            _webcamService.FrameCaptured += OnFrameCaptured;
            _webcamService.ErrorOccurred += OnWebcamError;
        }

        public override void OnNavigatedFrom()
        {
            if (_webcamService.IsRunning)
            {
                Stop();
            }
            _webcamService.FrameCaptured -= OnFrameCaptured;
            _webcamService.ErrorOccurred -= OnWebcamError;
        }

        private void LoadModel()
        {
            string[] searchPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "egg_classifier.onnx"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "egg_classifier.onnx"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Models", "egg_classifier.onnx"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "models", "egg_classifier.onnx"),
            };

            string? modelPath = searchPaths.FirstOrDefault(File.Exists);

            if (modelPath == null)
            {
                ModelStatusText = "모델 없음";
                ModelStatusColor = new SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 67, 54));
                ModelPath = "models/egg_classifier.onnx 파일을 배치하세요";
                StatusMessage = "ONNX 모델 파일을 찾을 수 없습니다.\n학습 후 models 폴더에 배치하세요.";
                CanStart = true;
                return;
            }

            ModelPath = modelPath;

            if (_detector.LoadModel(modelPath))
            {
                IsModelLoaded = true;
                ModelStatusText = "로드됨";
                ModelStatusColor = new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80));
                CanStart = true;
            }
            else
            {
                ModelStatusText = "로드 실패";
                ModelStatusColor = new SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 67, 54));
                StatusMessage = "모델 로드에 실패했습니다.";
                CanStart = true;
            }
        }

        [RelayCommand]
        private void Start()
        {
            if (_webcamService.Start())
            {
                CanStart = false;
                CanStop = true;
                OverlayVisibility = Visibility.Collapsed;

                foreach (var item in ClassCounts)
                {
                    item.Count = 0;
                }
                TotalDetections = 0;
            }
            else
            {
                StatusMessage = "웹캠 시작에 실패했습니다.\n카메라가 연결되어 있는지 확인하세요.";
            }
        }

        [RelayCommand]
        private void Stop()
        {
            _webcamService.Stop();
            CanStart = true;
            CanStop = false;
            OverlayVisibility = Visibility.Visible;
            StatusMessage = "중지됨. 시작 버튼을 눌러 재시작하세요.";
            FpsText = "FPS: --";
        }

        private void OnFrameCaptured(object? sender, FrameCapturedEventArgs e)
        {
            try
            {
                var frame = e.Frame;

                var detections = _detector.IsLoaded
                    ? _detector.Detect(frame, ConfidenceThreshold)
                    : new List<DetectionResult>();

                if (detections.Count > 0)
                {
                    _detector.DrawDetections(frame, detections);
                }

                var bitmapSource = frame.ToBitmapSource();
                bitmapSource.Freeze();

                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    CurrentFrame = bitmapSource;
                    FpsText = $"FPS: {e.Fps:F1}";
                    UpdateDetectionResults(detections);
                });

                frame.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Frame processing error: {ex.Message}");
            }
        }

        private void UpdateDetectionResults(List<DetectionResult> detections)
        {
            CurrentDetections.Clear();
            foreach (var det in detections.OrderByDescending(d => d.Confidence))
            {
                CurrentDetections.Add(new DetectionItem
                {
                    Label = det.ClassName,
                    Confidence = det.Confidence
                });
            }

            NoDetectionVisibility = detections.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var counts = new int[_detector.ClassNames.Length];
            foreach (var det in detections)
            {
                counts[det.ClassId]++;
            }

            for (int i = 0; i < counts.Length; i++)
            {
                ClassCounts[i].Count = counts[i];
            }

            TotalDetections = detections.Count;
        }

        private void OnWebcamError(object? sender, string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(message, "웹캠 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                Stop();
            });
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                OnNavigatedFrom();
                _disposed = true;
            }
        }
    }
}
