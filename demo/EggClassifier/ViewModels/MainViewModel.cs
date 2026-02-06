using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EggClassifier.Models;
using EggClassifier.Services;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EggClassifier.ViewModels
{
    /// <summary>
    /// 클래스별 카운트 표시용
    /// </summary>
    public class ClassCountItem : ObservableObject
    {
        private int _count;

        public string ClassName { get; set; } = string.Empty;
        public SolidColorBrush Color { get; set; } = Brushes.Gray;

        public int Count
        {
            get => _count;
            set => SetProperty(ref _count, value);
        }
    }

    /// <summary>
    /// 현재 탐지 표시용
    /// </summary>
    public class DetectionItem : ObservableObject
    {
        public string Label { get; set; } = string.Empty;
        public float Confidence { get; set; }

        public SolidColorBrush ConfidenceColor =>
            Confidence >= 0.8f ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)) :
            Confidence >= 0.5f ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 193, 7)) :
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 67, 54));
    }

    /// <summary>
    /// 메인 뷰모델
    /// </summary>
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly WebcamService _webcamService;
        private readonly YoloDetector _detector;
        private bool _disposed;

        // 클래스별 색상 (WPF용)
        private static readonly SolidColorBrush[] ClassBrushes = new[]
        {
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)),    // 정상: 녹색
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 67, 54)),    // 크랙: 빨강
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 0, 255)),    // 이물질: 마젠타
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 235, 59)),   // 탈색: 노랑
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 152, 0))     // 외형이상: 주황
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

        public MainViewModel()
        {
            _webcamService = new WebcamService();
            _detector = new YoloDetector();

            // 클래스 카운트 초기화
            for (int i = 0; i < YoloDetector.ClassNames.Length; i++)
            {
                ClassCounts.Add(new ClassCountItem
                {
                    ClassName = YoloDetector.ClassNames[i],
                    Color = ClassBrushes[i],
                    Count = 0
                });
            }

            // 이벤트 연결
            _webcamService.FrameCaptured += OnFrameCaptured;
            _webcamService.ErrorOccurred += OnWebcamError;

            // 모델 로드
            LoadModel();
        }

        private void LoadModel()
        {
            // 모델 경로 탐색
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

                // 모델 없이도 웹캠은 사용 가능
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
                CanStart = true; // 웹캠만이라도 사용 가능
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

                // 카운트 초기화
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

                // 객체 탐지 수행
                var detections = _detector.IsLoaded
                    ? _detector.Detect(frame, ConfidenceThreshold)
                    : new System.Collections.Generic.List<Detection>();

                // 탐지 결과 그리기
                if (detections.Count > 0)
                {
                    YoloDetector.DrawDetections(frame, detections);
                }

                // BitmapSource로 변환 (UI 스레드에서)
                var bitmapSource = frame.ToBitmapSource();
                bitmapSource.Freeze(); // 크로스 스레드 접근 허용

                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    CurrentFrame = bitmapSource;
                    FpsText = $"FPS: {e.Fps:F1}";

                    // 탐지 결과 업데이트
                    UpdateDetectionResults(detections);
                });

                frame.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Frame processing error: {ex.Message}");
            }
        }

        private void UpdateDetectionResults(System.Collections.Generic.List<Detection> detections)
        {
            // 현재 탐지 목록 업데이트
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

            // 클래스별 카운트 업데이트
            var counts = new int[YoloDetector.ClassNames.Length];
            foreach (var det in detections)
            {
                counts[det.ClassId]++;
            }

            for (int i = 0; i < counts.Length; i++)
            {
                ClassCounts[i].Count = counts[i];
            }

            // 총 탐지 수
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
                _webcamService.FrameCaptured -= OnFrameCaptured;
                _webcamService.ErrorOccurred -= OnWebcamError;
                _webcamService.Dispose();
                _detector.Dispose();
                _disposed = true;
            }
        }
    }
}
