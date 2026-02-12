using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EggClassifier.Core;
using EggClassifier.Services;
using DetectionResult = EggClassifier.Models.Detection;
using ClassCountItem = EggClassifier.Models.ClassCountItem;
using DetectionItem = EggClassifier.Models.DetectionItem;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        private readonly IInspectionService _inspectionService;
        private bool _disposed;

        // 추적 설정
        private const int MIN_FRAMES_TO_SAVE = 10;
        private const int MAX_MISSING_FRAMES = 10;  // 10프레임 연속 미탐지 시 제거
        private const float IOU_THRESHOLD = 0.5f;

        // 추적 상태
        private readonly Dictionary<int, TrackedEgg> _trackedEggs = new();
        private int _nextTrackId = 1;
        private string? _currentUserId;

        // 프레임 스킵: 이전 프레임 처리 중이면 새 프레임 건너뛰기
        private int _isProcessing = 0;

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
        private float _confidenceThreshold = 0.3f;

        [ObservableProperty]
        private int _totalDetections;

        [ObservableProperty]
        private int _selectedCameraIndex = 0;

        [ObservableProperty]
        private string _cameraStatusText = "웹캠";

        [ObservableProperty]
        private Visibility _noDetectionVisibility = Visibility.Visible;

        public ObservableCollection<ClassCountItem> ClassCounts { get; } = new();
        public ObservableCollection<DetectionItem> CurrentDetections { get; } = new();

        public DetectionViewModel(IWebcamService webcamService, IDetectorService detector, IInspectionService inspectionService)
        {
            _webcamService = webcamService;
            _detector = detector;
            _inspectionService = inspectionService;

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

        /// <summary>
        /// 현재 로그인한 사용자 ID 설정
        /// </summary>
        public void SetCurrentUser(string userId)
        {
            _currentUserId = userId;
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
        private void SelectWebcam()
        {
            SelectedCameraIndex = 0;
            CameraStatusText = "웹캠";
            _webcamService.CameraIndex = 0;
            StatusMessage = "웹캠이 선택되었습니다.";
        }

        [RelayCommand]
        private void SelectPhoneCamera()
        {
            // DroidCam은 보통 인덱스 1 또는 2
            // 먼저 1을 시도하고, 안되면 사용자가 수동으로 변경할 수 있도록
            SelectedCameraIndex = 1;
            CameraStatusText = "스마트폰 (인덱스: 1)";
            _webcamService.CameraIndex = 1;
            StatusMessage = "스마트폰 카메라(인덱스 1)가 선택되었습니다.\n안보이면 인덱스 2를 시도해보세요.";
        }

        [RelayCommand]
        private void SelectPhoneCamera2()
        {
            SelectedCameraIndex = 2;
            CameraStatusText = "스마트폰 (인덱스: 2)";
            _webcamService.CameraIndex = 2;
            StatusMessage = "스마트폰 카메라(인덱스 2)가 선택되었습니다.";
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

                // 추적 초기화
                ClearTrackedEggs();
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

            // 추적 정리
            ClearTrackedEggs();
        }

        private void OnFrameCaptured(object? sender, FrameCapturedEventArgs e)
        {
            // 이전 프레임 처리 중이면 스킵 (딜레이 방지)
            if (Interlocked.CompareExchange(ref _isProcessing, 1, 0) != 0)
            {
                e.Frame.Dispose();
                return;
            }

            try
            {
                var frame = e.Frame;

                var detections = _detector.IsLoaded
                    ? _detector.Detect(frame, ConfidenceThreshold)
                    : new List<DetectionResult>();

                // 추적 업데이트
                UpdateTracking(detections, frame);

                if (detections.Count > 0)
                {
                    _detector.DrawDetections(frame, detections);
                }

                var bitmapSource = frame.ToBitmapSource();
                bitmapSource.Freeze();

                Application.Current?.Dispatcher?.BeginInvoke(() =>
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
            finally
            {
                Interlocked.Exchange(ref _isProcessing, 0);
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
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                MessageBox.Show(message, "웹캠 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                Stop();
            });
        }

        /// <summary>
        /// 추적 업데이트 (IOU 기반 매칭)
        /// </summary>
        private void UpdateTracking(List<DetectionResult> detections, Mat frame)
        {
            var matched = new HashSet<int>();

            // 1. 기존 추적과 새 탐지 매칭
            foreach (var tracked in _trackedEggs.Values.ToList())
            {
                bool foundMatch = false;

                foreach (var (det, idx) in detections.Select((d, i) => (d, i)))
                {
                    if (matched.Contains(idx)) continue;

                    var bbox = det.BoundingBox;
                    var iou = tracked.CalculateIOU(bbox);

                    // IOU 기준 매칭 + 동일 클래스만 매칭
                    if (iou >= IOU_THRESHOLD && det.ClassId == tracked.CurrentClass)
                    {
                        tracked.Update(det.ClassId, bbox, frame, det.Confidence);
                        matched.Add(idx);
                        foundMatch = true;
                        break;
                    }
                }

                if (!foundMatch)
                {
                    tracked.IncrementMissing();
                }
            }

            // 2. 매칭되지 않은 새 탐지 → 새 추적 생성
            for (int i = 0; i < detections.Count; i++)
            {
                if (matched.Contains(i)) continue;

                var det = detections[i];
                var bbox = det.BoundingBox;

                var newTrack = new TrackedEgg
                {
                    TrackId = _nextTrackId++,
                    CurrentClass = det.ClassId,
                    LastBbox = bbox,
                    LastConfidence = det.Confidence
                };
                newTrack.Update(det.ClassId, bbox, frame, det.Confidence);
                _trackedEggs[newTrack.TrackId] = newTrack;
            }

            // 3. 추적 종료 판단 및 저장
            var toRemove = new List<int>();
            foreach (var kvp in _trackedEggs)
            {
                var tracked = kvp.Value;

                // 5프레임 이상 미탐지 → 추적 종료
                if (tracked.MissingFrames >= MAX_MISSING_FRAMES)
                {
                    // 10프레임 이상 유지되었고, 아직 저장 안 했으면 저장
                    if (tracked.ConsecutiveFrames >= MIN_FRAMES_TO_SAVE && !tracked.SavedToDb)
                    {
                        SaveTrackedEgg(tracked);
                    }

                    tracked.Dispose();
                    toRemove.Add(kvp.Key);
                }
                // 10프레임 이상 유지되었고, 아직 저장 안 했으면 저장 (실시간 저장)
                else if (tracked.ConsecutiveFrames >= MIN_FRAMES_TO_SAVE && !tracked.SavedToDb)
                {
                    SaveTrackedEgg(tracked);
                }
            }

            // 4. 제거
            foreach (var id in toRemove)
            {
                _trackedEggs.Remove(id);
            }
        }

        /// <summary>
        /// 추적된 계란 저장 (bbox + 신뢰도 표시)
        /// </summary>
        private void SaveTrackedEgg(TrackedEgg tracked)
        {
            if (string.IsNullOrEmpty(_currentUserId) || tracked.LastFrame == null)
                return;

            tracked.SavedToDb = true;

            // bbox가 그려진 프레임 생성
            var frameWithBox = tracked.LastFrame.Clone();
            DrawBoundingBox(frameWithBox, tracked);

            // 백그라운드에서 비동기 저장
            _ = Task.Run(async () =>
            {
                try
                {
                    var success = await _inspectionService.SaveInspectionAsync(
                        _currentUserId,
                        tracked.CurrentClass,
                        tracked.LastConfidence,
                        frameWithBox  // bbox가 그려진 프레임
                    ).ConfigureAwait(false);

                    if (success)
                    {
                        System.Diagnostics.Debug.WriteLine($"계란 저장 성공: TrackId={tracked.TrackId}, Class={tracked.CurrentClass}, Frames={tracked.ConsecutiveFrames}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"계란 저장 실패: TrackId={tracked.TrackId}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SaveTrackedEgg 오류: {ex.Message}");
                }
                finally
                {
                    frameWithBox?.Dispose();  // 사용 후 해제
                }
            });
        }

        /// <summary>
        /// 프레임에 Bounding Box + 클래스명 + 신뢰도 그리기 (한글 지원)
        /// </summary>
        private void DrawBoundingBox(Mat frame, TrackedEgg tracked)
        {
            // Mat → Bitmap 변환 (GDI+로 한글 텍스트 렌더링)
            using var bitmap = BitmapConverter.ToBitmap(frame);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            // 클래스별 색상 (RGB)
            System.Drawing.Color color = tracked.CurrentClass switch
            {
                0 => System.Drawing.Color.FromArgb(76, 175, 80),   // 정상 (녹색)
                1 => System.Drawing.Color.FromArgb(244, 67, 54),   // 크랙 (빨강)
                2 => System.Drawing.Color.FromArgb(255, 0, 255),   // 이물질 (마젠타)
                3 => System.Drawing.Color.FromArgb(255, 235, 59),  // 탈색 (노랑)
                4 => System.Drawing.Color.FromArgb(255, 152, 0),   // 외형이상 (주황)
                _ => System.Drawing.Color.White
            };

            // Bounding box 그리기
            using (var pen = new System.Drawing.Pen(color, 2))
            {
                graphics.DrawRectangle(pen, tracked.LastBbox.X, tracked.LastBbox.Y,
                    tracked.LastBbox.Width, tracked.LastBbox.Height);
            }

            // 한글 클래스명 + 신뢰도
            var className = _detector.ClassNames[tracked.CurrentClass];
            var text = $"{className} {tracked.LastConfidence:P0}";

            using (var font = new Font(new System.Drawing.FontFamily("맑은 고딕"), 12, System.Drawing.FontStyle.Bold))
            using (var textBrush = new SolidBrush(System.Drawing.Color.White))
            using (var backgroundBrush = new SolidBrush(System.Drawing.Color.FromArgb(180, 0, 0, 0)))
            {
                var textSize = graphics.MeasureString(text, font);
                var textX = tracked.LastBbox.X;
                var textY = tracked.LastBbox.Y - textSize.Height - 5;

                // 텍스트 배경
                graphics.FillRectangle(backgroundBrush, textX, textY, textSize.Width + 4, textSize.Height);

                // 텍스트
                graphics.DrawString(text, font, textBrush, textX + 2, textY);
            }

            // Bitmap → Mat 다시 변환
            var resultMat = BitmapConverter.ToMat(bitmap);
            resultMat.CopyTo(frame);
            resultMat.Dispose();
        }

        /// <summary>
        /// 모든 추적 정리
        /// </summary>
        private void ClearTrackedEggs()
        {
            foreach (var tracked in _trackedEggs.Values)
            {
                tracked.Dispose();
            }
            _trackedEggs.Clear();
            _nextTrackId = 1;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                OnNavigatedFrom();
                ClearTrackedEggs();
                _disposed = true;
            }
        }

        /// <summary>
        /// 추적 중인 계란 정보 (내부 클래스)
        /// </summary>
        private class TrackedEgg
        {
            public int TrackId { get; set; }
            public int CurrentClass { get; set; }
            public int ConsecutiveFrames { get; set; } = 0;
            public int MissingFrames { get; set; } = 0;
            public OpenCvSharp.Rect LastBbox { get; set; }
            public Mat? LastFrame { get; set; }
            public float LastConfidence { get; set; }
            public bool SavedToDb { get; set; } = false;

            /// <summary>
            /// IOU (Intersection over Union) 계산
            /// </summary>
            public float CalculateIOU(OpenCvSharp.Rect other)
            {
                var intersection = LastBbox.Intersect(other);
                if (intersection.Width <= 0 || intersection.Height <= 0)
                    return 0f;

                var intersectionArea = intersection.Width * intersection.Height;
                var unionArea = LastBbox.Width * LastBbox.Height + other.Width * other.Height - intersectionArea;

                return unionArea > 0 ? (float)intersectionArea / unionArea : 0f;
            }

            /// <summary>
            /// 추적 업데이트 (탐지됨)
            /// </summary>
            public void Update(int classId, OpenCvSharp.Rect bbox, Mat frame, float confidence)
            {
                // 클래스가 바뀌면 리셋
                if (ConsecutiveFrames > 0 && classId != CurrentClass)
                {
                    ConsecutiveFrames = 0;
                    SavedToDb = false;
                }

                CurrentClass = classId;
                LastBbox = bbox;
                LastConfidence = confidence;
                ConsecutiveFrames++;
                MissingFrames = 0;

                // 이전 프레임 해제 후 새 프레임 저장
                LastFrame?.Dispose();
                LastFrame = frame.Clone();
            }

            /// <summary>
            /// 추적 업데이트 (미탐지)
            /// </summary>
            public void IncrementMissing()
            {
                MissingFrames++;
            }

            /// <summary>
            /// 리소스 해제
            /// </summary>
            public void Dispose()
            {
                LastFrame?.Dispose();
                LastFrame = null;
            }
        }
    }
}
