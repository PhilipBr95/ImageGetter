namespace ImageGetter.Models
{
    public class ImageWithMeta
    {
        public SixLabors.ImageSharp.Image Image { get; set; }
        public string Filename { get; set; }
        public ImageWithMeta(SixLabors.ImageSharp.Image image, string filename)
        {
            Image = image;
            Filename = filename;
        }
    }
}
