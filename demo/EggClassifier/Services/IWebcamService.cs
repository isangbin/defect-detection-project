using System;

namespace EggClassifier.Services
{
    public interface IWebcamService : IDisposable
    {
        event EventHandler<FrameCapturedEventArgs>? FrameCaptured;
        event EventHandler<string>? ErrorOccurred;

        bool IsRunning { get; }
        int CameraIndex { get; set; }
        bool Start();
        void Stop();
    }
}
