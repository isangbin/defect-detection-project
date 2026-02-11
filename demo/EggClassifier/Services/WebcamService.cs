using OpenCvSharp;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EggClassifier.Services
{
    /// <summary>
    /// 웹캠 프레임 수신 이벤트 인자
    /// </summary>
    public class FrameCapturedEventArgs : EventArgs
    {
        public Mat Frame { get; }
        public double Fps { get; }

        public FrameCapturedEventArgs(Mat frame, double fps)
        {
            Frame = frame;
            Fps = fps;
        }
    }

    /// <summary>
    /// 웹캠 캡처 서비스
    /// </summary>
    public class WebcamService : IWebcamService
    {
        private VideoCapture? _capture;
        private CancellationTokenSource? _cts;
        private Task? _captureTask;
        private bool _disposed;
        private readonly object _lock = new();

        public event EventHandler<FrameCapturedEventArgs>? FrameCaptured;
        public event EventHandler<string>? ErrorOccurred;

        public bool IsRunning => _captureTask != null && !_captureTask.IsCompleted;
        public int CameraIndex { get; set; } = 0;
        public int FrameWidth { get; set; } = 640;
        public int FrameHeight { get; set; } = 480;
        public int TargetFps { get; set; } = 30;

        /// <summary>
        /// 웹캠 캡처 시작
        /// </summary>
        public bool Start()
        {
            if (IsRunning)
                return true;

            try
            {
                _capture = new VideoCapture(CameraIndex, VideoCaptureAPIs.DSHOW);

                if (!_capture.IsOpened())
                {
                    ErrorOccurred?.Invoke(this, "웹캠을 열 수 없습니다. 카메라가 연결되어 있는지 확인하세요.");
                    return false;
                }

                // MJPG 코덱 설정 (비압축보다 전송 빠름)
                _capture.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));

                // 해상도 및 FPS 설정
                _capture.Set(VideoCaptureProperties.FrameWidth, FrameWidth);
                _capture.Set(VideoCaptureProperties.FrameHeight, FrameHeight);
                _capture.Set(VideoCaptureProperties.Fps, TargetFps);

                // 버퍼 크기 최소화 (지연 감소)
                _capture.Set(VideoCaptureProperties.BufferSize, 1);

                _cts = new CancellationTokenSource();
                _captureTask = Task.Run(() => CaptureLoop(_cts.Token));

                Console.WriteLine($"Webcam started: {_capture.FrameWidth}x{_capture.FrameHeight} @ {_capture.Fps}fps");
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"웹캠 시작 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 웹캠 캡처 중지
        /// </summary>
        public void Stop()
        {
            if (!IsRunning)
                return;

            _cts?.Cancel();

            try
            {
                _captureTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // 취소로 인한 예외 무시
            }

            lock (_lock)
            {
                _capture?.Release();
                _capture?.Dispose();
                _capture = null;
            }

            _cts?.Dispose();
            _cts = null;
            _captureTask = null;

            Console.WriteLine("Webcam stopped");
        }

        /// <summary>
        /// 프레임 캡처 루프
        /// </summary>
        private void CaptureLoop(CancellationToken ct)
        {
            var frame = new Mat();
            var fpsSw = System.Diagnostics.Stopwatch.StartNew();
            var frameSw = System.Diagnostics.Stopwatch.StartNew();
            int frameCount = 0;
            double fps = 0;

            int frameIntervalMs = 1000 / TargetFps;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    frameSw.Restart();

                    bool grabbed;
                    lock (_lock)
                    {
                        if (_capture == null || !_capture.IsOpened())
                            break;

                        // Grab만 하고 불필요한 버퍼 프레임 버리기
                        grabbed = _capture.Grab();
                    }

                    if (!grabbed)
                    {
                        Thread.Sleep(1);
                        continue;
                    }

                    lock (_lock)
                    {
                        if (_capture == null || !_capture.IsOpened())
                            break;

                        if (!_capture.Retrieve(frame) || frame.Empty())
                        {
                            Thread.Sleep(1);
                            continue;
                        }
                    }

                    frameCount++;

                    // FPS 계산 (1초마다)
                    if (fpsSw.ElapsedMilliseconds >= 1000)
                    {
                        fps = frameCount * 1000.0 / fpsSw.ElapsedMilliseconds;
                        frameCount = 0;
                        fpsSw.Restart();
                    }

                    // 프레임 복사하여 이벤트 발생 (원본 보호)
                    var frameCopy = frame.Clone();
                    FrameCaptured?.Invoke(this, new FrameCapturedEventArgs(frameCopy, fps));

                    // 처리 시간을 고려한 정밀 대기
                    int elapsed = (int)frameSw.ElapsedMilliseconds;
                    int sleepMs = frameIntervalMs - elapsed;
                    if (sleepMs > 1)
                    {
                        Thread.Sleep(sleepMs);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Capture error: {ex.Message}");
                    ErrorOccurred?.Invoke(this, $"캡처 오류: {ex.Message}");
                    Thread.Sleep(100);
                }
            }

            frame.Dispose();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Stop();
                _disposed = true;
            }
        }
    }
}
