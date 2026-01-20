namespace ImageGetter.Models
{
    public class Settings
    {
        public string Host { get; set; } = "egypt";
        public string? ImagePassword { get; set; }
        public string Username { get; set; } = "ImageGetter";
        public IEnumerable<string> Paths { get; set; } = ["/photo/Phil's Phone", "/photo/Gill's Phone", "/photo/Gill's Selective Phone", "/photo/Camera Selective Photos"];
        public string? GoogleApiKey { get; internal set; }
        public string? FaceApi { get; set; } = "http://192.168.1.116:8092"; //To dev locally 5000
        public double MinConfidence { get; set; } = 0.9;
        public double MinConfidenceMultiplier { get; set; } = 50000;
        public int MinHeight { get; set; } = 100;
        public float ImageRatioTolerance { get; set; } = 0.3f;
        public double MinAvgConfidence { get; set; } = 80000;
        public string DatabasePath { get; set; } = "./DB/Images.json";
        public int MaxCachedSaves { get; set; } = 5;
    }
}