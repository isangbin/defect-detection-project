using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace EggClassifier.Models
{
    public class DetectionItem : ObservableObject
    {
        public string Label { get; set; } = string.Empty;
        public float Confidence { get; set; }

        public SolidColorBrush ConfidenceColor =>
            Confidence >= 0.8f ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)) :
            Confidence >= 0.5f ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 193, 7)) :
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 67, 54));
    }
}
