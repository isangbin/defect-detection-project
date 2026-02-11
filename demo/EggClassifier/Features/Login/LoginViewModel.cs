using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EggClassifier.Core;
using EggClassifier.Models;
using EggClassifier.Services;
using EggClassifier.ViewModels;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Windows;
using System.IO;
using System.Windows.Media.Imaging;

namespace EggClassifier.Features.Login
{
    public partial class LoginViewModel : ViewModelBase, IDisposable
    {
        private readonly INavigationService _navigation;
        private readonly IWebcamService _webcamService;
        private readonly IFaceService _faceService;
        private readonly IUserService _userService;
        private readonly MainViewModel _mainViewModel;
        private bool _disposed;
        private UserData? _authenticatedUser;
        private float[]? _savedFaceEmbedding;
        private const float SIMILARITY_THRESHOLD = 0.8f;
        private const int REQUIRED_CONSECUTIVE_FRAMES = 10;
        private int _consecutiveMatchCount;

        // Phase 1: 자격증명 입력
        [ObservableProperty]
        private bool _isCredentialPhase = true;

        // Phase 2: 얼굴 인증
        [ObservableProperty]
        private bool _isFaceVerifyPhase;

        [ObservableProperty]
        private string _username = string.Empty;

        [ObservableProperty]
        private string _password = string.Empty;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private string _faceStatusMessage = "얼굴을 카메라에 맞춰주세요...";

        [ObservableProperty]
        private BitmapSource? _currentFrame;

        [ObservableProperty]
        private bool _isStatusError;

        public LoginViewModel(
            INavigationService navigation,
            IWebcamService webcamService,
            IFaceService faceService,
            IUserService userService,
            MainViewModel mainViewModel)
        {
            _navigation = navigation;
            _webcamService = webcamService;
            _faceService = faceService;
            _userService = userService;
            _mainViewModel = mainViewModel;
        }

        public override void OnNavigatedTo()
        {
            // 얼굴 모델 로드 (아직 안 된 경우)
            if (!_faceService.IsLoaded)
            {
                _faceService.LoadModels();
            }
        }

        public override void OnNavigatedFrom()
        {
            StopWebcam();
        }

        [RelayCommand]
        private async Task Login()
        {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                StatusMessage = "아이디와 비밀번호를 입력하세요.";
                IsStatusError = true;
                return;
            }

            // UI 업데이트
            StatusMessage = "로그인 중...";
            IsStatusError = false;

            // 백그라운드 스레드에서 실행
            var username = Username;
            var password = Password;

            var user = await Task.Run(() => _userService.ValidateCredentials(username, password));

            if (user == null)
            {
                StatusMessage = "아이디 또는 비밀번호가 올바르지 않습니다.";
                IsStatusError = true;
                return;
            }

            _authenticatedUser = user;

            if (!_faceService.IsLoaded)
            {
                StatusMessage = "얼굴인식 모델이 없어 바로 로그인합니다.";
                IsStatusError = false;
                _mainViewModel.OnLoginSuccess();
                return;
            }

            // 저장된 얼굴 임베딩 가져오기
            // 1. DB에서 직접 임베딩을 가져온 경우 (Supabase)
            if (user.FaceEmbedding != null && user.FaceEmbedding.Length > 0)
            {
                _savedFaceEmbedding = user.FaceEmbedding;
            }
            // 2. 파일 경로에서 이미지를 읽어서 임베딩 추출 (JSON 파일 방식)
            else if (!string.IsNullOrEmpty(user.FaceImagePath) && File.Exists(user.FaceImagePath))
            {
                using var savedFaceImage = Cv2.ImRead(user.FaceImagePath);
                if (savedFaceImage.Empty())
                {
                    StatusMessage = "얼굴 이미지를 읽을 수 없습니다.";
                    IsStatusError = true;
                    return;
                }

                _savedFaceEmbedding = _faceService.GetFaceEmbedding(savedFaceImage);
                if (_savedFaceEmbedding == null)
                {
                    StatusMessage = "저장된 얼굴에서 특징을 추출할 수 없습니다.";
                    IsStatusError = true;
                    return;
                }
            }
            else
            {
                StatusMessage = "등록된 얼굴 정보를 찾을 수 없습니다.";
                IsStatusError = true;
                return;
            }

            // Phase 2: 얼굴 인증 시작
            _consecutiveMatchCount = 0;
            IsCredentialPhase = false;
            IsFaceVerifyPhase = true;
            FaceStatusMessage = "얼굴을 카메라에 맞춰주세요...";
            StartWebcam();
        }

        [RelayCommand]
        private void CancelFaceVerify()
        {
            StopWebcam();
            _authenticatedUser = null;
            _savedFaceEmbedding = null;
            IsFaceVerifyPhase = false;
            IsCredentialPhase = true;
            StatusMessage = string.Empty;
            IsStatusError = false;
        }

        [RelayCommand]
        private void NavigateToSignUp()
        {
            _navigation.NavigateTo<SignUpViewModel>();
        }

        private void StartWebcam()
        {
            _webcamService.FrameCaptured += OnFrameCaptured;
            _webcamService.ErrorOccurred += OnWebcamError;

            if (!_webcamService.Start())
            {
                FaceStatusMessage = "웹캠을 시작할 수 없습니다.";
            }
        }

        private void StopWebcam()
        {
            if (_webcamService.IsRunning)
            {
                _webcamService.Stop();
            }
            _webcamService.FrameCaptured -= OnFrameCaptured;
            _webcamService.ErrorOccurred -= OnWebcamError;
        }

        private void OnFrameCaptured(object? sender, FrameCapturedEventArgs e)
        {
            try
            {
                var frame = e.Frame;

                // 얼굴 탐지
                var faceRect = _faceService.DetectFace(frame);

                if (faceRect.HasValue)
                {
                    var rect = faceRect.Value;
                    // 얼굴 영역에 사각형 표시
                    Cv2.Rectangle(frame, rect, new Scalar(0, 255, 0), 2);

                    // 마진 20% 추가하여 얼굴 크롭
                    var croppedFace = CropFaceWithMargin(frame, rect, 0.2f);

                    if (croppedFace != null && _savedFaceEmbedding != null)
                    {
                        var embedding = _faceService.GetFaceEmbedding(croppedFace);
                        croppedFace.Dispose();

                        if (embedding != null)
                        {
                            float similarity = _faceService.CompareFaces(embedding, _savedFaceEmbedding);

                            var bitmapSource = frame.ToBitmapSource();
                            bitmapSource.Freeze();

                            Application.Current.Dispatcher.BeginInvoke(() =>
                            {
                                CurrentFrame = bitmapSource;

                                int percent = (int)(similarity * 100);
                                if (similarity >= SIMILARITY_THRESHOLD)
                                {
                                    _consecutiveMatchCount++;
                                    if (_consecutiveMatchCount >= REQUIRED_CONSECUTIVE_FRAMES)
                                    {
                                        FaceStatusMessage = $"얼굴인식에 성공하였습니다. ({percent}%)";
                                        StopWebcam();
                                        _mainViewModel.OnLoginSuccess();
                                    }
                                    else
                                    {
                                        FaceStatusMessage = $"인식 중... ({_consecutiveMatchCount}/{REQUIRED_CONSECUTIVE_FRAMES}) ({percent}%)";
                                    }
                                }
                                else
                                {
                                    _consecutiveMatchCount = 0;
                                    FaceStatusMessage = $"일치하지 않습니다. ({percent}%)";
                                }
                            });

                            frame.Dispose();
                            return;
                        }
                    }
                }

                var bitmap = frame.ToBitmapSource();
                bitmap.Freeze();

                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    CurrentFrame = bitmap;
                    if (!faceRect.HasValue)
                    {
                        FaceStatusMessage = "얼굴을 카메라에 맞춰주세요...";
                    }
                });

                frame.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Face verify frame error: {ex.Message}");
            }
        }

        private void OnWebcamError(object? sender, string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                FaceStatusMessage = $"웹캠 오류: {message}";
            });
        }

        private static Mat? CropFaceWithMargin(Mat image, OpenCvSharp.Rect faceRect, float margin)
        {
            int marginX = (int)(faceRect.Width * margin);
            int marginY = (int)(faceRect.Height * margin);

            int x = Math.Max(0, faceRect.X - marginX);
            int y = Math.Max(0, faceRect.Y - marginY);
            int w = Math.Min(image.Width - x, faceRect.Width + 2 * marginX);
            int h = Math.Min(image.Height - y, faceRect.Height + 2 * marginY);

            if (w <= 0 || h <= 0)
                return null;

            return new Mat(image, new OpenCvSharp.Rect(x, y, w, h));
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
