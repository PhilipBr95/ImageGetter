namespace ImageGetter.Models
{
    public class MediaFile
    {
        public string Filename { get; internal set; }
        public byte[] Data { get; internal set; }
        public int Width { get; internal set; }
        public int Height { get; internal set; }
        public DateTime CreatedDate { get; internal set; }
        public string Location { get; internal set; }
        public ushort Orientation { get; internal set; }
        public bool IsLandscape => (Orientation == 1 || Orientation == 3);
        public string ParentFolderName => Path.GetFileName(Path.GetDirectoryName(Filename));
        public int MediaId { get; internal set; }
    }
}