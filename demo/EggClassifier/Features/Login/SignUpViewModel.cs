using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EggClassifier.Core;
using EggClassifier.Services;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace EggClassifier.Features.Login
{
    public partial class SignUpViewModel : ViewModelBase, IDisposable
    {
        private readonly INavigationService _navigation;
        private readonly IWebcamService _webcamService;
        private readonly IUserService _userService;
        private bool _disposed;
        private Mat? _capturedFaceMat;

        [ObservableProperty]
        private string _username = string.Empty;

        [ObservableProperty]
        private string _password = string.Empty;

        [ObservableProperty]
        private string _passwordConfirm = string.Empty;

        [ObservableProperty]
        private string _passwordMatchMessage = string.Empty;

        [ObservableProperty]
        private bool _isPasswordMatch;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private bool _isStatusError;

        [ObservableProperty]
        private BitmapSource? _currentFrame;

        [ObservableProperty]
        private BitmapSource? _faceThumbnail;

        [ObservableProperty]
        private bool _isWebcamActive;

        [ObservableProperty]
        private bool _isFaceCaptured;

        [ObservableProperty]
        private bool _showStartButton = true;

        [ObservableProperty]
        private string _faceStatusMessage = string.Empty;

        partial void OnPasswordChanged(string value) => CheckPasswordMatch();
        partial void OnPasswordConfirmChanged(string value) => CheckPasswordMatch();

        private void CheckPasswordMatch()
        {
            if (string.IsNullOrEmpty(Password) || string.IsNullOrEmpty(PasswordConfirm))
            {
                PasswordMatchMessage = string.Empty;
                IsPasswordMatch = false;
                return;
            }

            if (Password == PasswordConfirm)
            {
                PasswordMatchMessage = "비밀번호가 일치합니다.";
                IsPasswordMatch = true;
            }
            else
            {
                PasswordMatchMessage = "비밀번호가 일치하지 않습니다.";
                IsPasswordMatch = false;
            }
        }

        public SignUpViewModel(
            INavigationService navigation,
            IWebcamService webcamService,
            IUserService userService)
        {
            _navigation = navigation;
            _webcamService = webcamService;
            _userService = userService;
        }

        public override void OnNavigatedFrom()
        {
            StopWebcam();
        }

        [RelayCommand]
        private void StartFaceCapture()
        {
            IsWebcamActive = true;
            IsFaceCaptured = false;
            ShowStartButton = false;
            _capturedFaceMat?.Dispose();
            _capturedFaceMat = null;
            FaceThumbnail = null;
            FaceStatusMessage = "카메라를 바라봐주세요...";

            _webcamService.FrameCaptured += OnFrameCaptured;
            _webcamService.ErrorOccurred += OnWebcamError;

            if (!_webcamService.Start())
            {
                FaceStatusMessage = "웹캠을 시작할 수 없습니다.";
                IsWebcamActive = false;
                ShowStartButton = true;
            }
        }

        [RelayCommand]
        private void CaptureFace()
        {
            if (!IsWebcamActive)
                return;

            _captureRequested = true;
        }

        private volatile bool _captureRequested;

        private void OnFrameCaptured(object? sender, FrameCapturedEventArgs e)
        {
            try
            {
                var frame = e.Frame;

                if (_captureRequested)
                {
                    _captureRequested = false;

                    // 현재 프레임을 그대로 캡처
                    _capturedFaceMat?.Dispose();
                    _capturedFaceMat = frame.Clone();

                    var thumbnail = frame.ToBitmapSource();
                    thumbnail.Freeze();

                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        FaceThumbnail = thumbnail;
                        IsFaceCaptured = true;
                        IsWebcamActive = false;
                        FaceStatusMessage = "사진이 마음에 드시면 '확인'을 눌러주세요.";
                    });

                    StopWebcam();
                    frame.Dispose();
                    return;
                }

                var bitmap = frame.ToBitmapSource();
                bitmap.Freeze();

                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    CurrentFrame = bitmap;
                });

                frame.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SignUp frame error: {ex.Message}");
            }
        }

        private void OnWebcamError(object? sender, string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                FaceStatusMessage = $"웹캠 오류: {message}";
                IsWebcamActive = false;
                ShowStartButton = true;
            });
        }

        [RelayCommand]
        private void RetakeFace()
        {
            _capturedFaceMat?.Dispose();
            _capturedFaceMat = null;
            FaceThumbnail = null;
            IsFaceCaptured = false;
            StartFaceCapture();
        }

        [RelayCommand]
        private void ConfirmFace()
        {
            if (_capturedFaceMat == null)
                return;

            FaceStatusMessage = "얼굴 사진이 등록되었습니다!";
        }

        [RelayCommand]
        private void Register()
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                StatusMessage = "아이디를 입력하세요.";
                IsStatusError = true;
                return;
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                StatusMessage = "비밀번호를 입력하세요.";
                IsStatusError = true;
                return;
            }

            if (Password != PasswordConfirm)
            {
                StatusMessage = "비밀번호가 일치하지 않습니다.";
                IsStatusError = true;
                return;
            }

            if (_capturedFaceMat == null)
            {
                StatusMessage = "얼굴 사진을 등록해주세요.";
                IsStatusError = true;
                return;
            }

            if (_userService.UserExists(Username))
            {
                StatusMessage = "이미 존재하는 아이디입니다.";
                IsStatusError = true;
                return;
            }

            // 얼굴 이미지 파일 저장
            string faceDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "userdata", "faces");
            Directory.CreateDirectory(faceDir);
            string fileName = $"{Username}_{DateTime.Now:yyyyMMddHHmmss}.png";
            string faceImagePath = Path.Combine(faceDir, fileName);
            Cv2.ImWrite(faceImagePath, _capturedFaceMat);

            if (_userService.RegisterUser(Username, Password, faceImagePath))
            {
                StatusMessage = "회원가입이 완료되었습니다!";
                IsStatusError = false;
                _capturedFaceMat?.Dispose();
                _capturedFaceMat = null;
                _navigation.NavigateTo<LoginViewModel>();
            }
            else
            {
                StatusMessage = "회원가입에 실패했습니다.";
                IsStatusError = true;
            }
        }

        [RelayCommand]
        private void NavigateToLogin()
        {
            _navigation.NavigateTo<LoginViewModel>();
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

        public void Dispose()
        {
            if (!_disposed)
            {
                OnNavigatedFrom();
                _capturedFaceMat?.Dispose();
                _capturedFaceMat = null;
                _disposed = true;
            }
        }
    }
}
