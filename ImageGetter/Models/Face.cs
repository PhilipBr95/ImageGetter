using System.Diagnostics;

namespace ImageGetter.Models
{
    [DebuggerDisplay("ConfidenceM = {ConfidenceMultiplyer}: Confidence = {Confidence}, Width = {Width}, Height = {Height}")]
    class Face
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double Confidence { get; set; }
        public double ConfidenceMultiplyer => Confidence * Width * Height;
    }
}