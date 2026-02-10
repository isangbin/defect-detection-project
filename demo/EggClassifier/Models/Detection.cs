using OpenCvSharp;

namespace EggClassifier.Models
{
    public class Detection
    {
        public int ClassId { get; set; }
        public string ClassName { get; set; } = string.Empty;
        public float Confidence { get; set; }
        public Rect BoundingBox { get; set; }
    }
}
