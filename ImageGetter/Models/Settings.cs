﻿namespace ImageGetter.Models
{
    public class Settings
    {
        public string Host { get; set; } = "egypt";
        public string? ImagePassword { get; set; }
        public string Username { get; set; } = "ImageGetter";
        public IEnumerable<string> Paths { get; set; } = ["/photo/Phil's Phone", "/photo/Gill's Phone"];
        public string? GoogleApiKey { get; internal set; }
        public string? FaceApi { get; set; } = "http://192.168.1.116:8092"; //To dev locally 5000
        public double MinConfidence { get; set; } = 2;
        public double MinAvgConfidence { get; set; } = 4;
        public int MinAvgHeight { get; set; } = 80;
        public int MinHeight { get; set; } = 80;
    }
}