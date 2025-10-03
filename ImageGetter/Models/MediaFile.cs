namespace ImageGetter.Models
{
    public class MediaFile
    {
        public string Filename { get; internal set; }
        public byte[] Data { get; internal set; }
        public int Width { get; internal set; }
        public int Height { get; internal set; }
        public DateTime CreatedDate { get; internal set; }
    }
}