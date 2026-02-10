using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace EggClassifier.Models
{
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
}
