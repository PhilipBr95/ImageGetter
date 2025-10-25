﻿namespace ImageGetter.Models
{
    public class Settings
    {
        public string Host { get; set; } = "egypt";
        public string? ImagePassword { get; set; }
        public string Username { get; set; } = "ImageGetter";
        public IEnumerable<string> Paths { get; set; } = ["/photo/Phil's Phone", "/photo/Gill's Phone"];
        public string? GoogleApiKey { get; internal set; }
        public string? FaceApi { get; set; } = "http://192.168.1.116:8092";
    }
}