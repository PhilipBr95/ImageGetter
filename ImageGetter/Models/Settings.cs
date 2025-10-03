namespace ImageGetter.Models
{
    public class Settings
    {
        public string Host { get; set; } = "192.168.1.151";
        public string? Password { get; set; }
        public string Username { get; set; } = "ImageGetter";
        public IEnumerable<string> Paths { get; set; } = ["/photo/Phil's Phone", "/photo/Gill's Phone"];
    }
}