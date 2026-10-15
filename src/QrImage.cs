using System.Windows;
using System.Windows.Media;
using QRCoder;

namespace AndroidMirror
{
    public static class QrImage
    {
        public static DrawingImage Create(string payload)
        {
            using (var generator = new QRCodeGenerator())
            using (var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M))
            {
                var drawing = new DrawingGroup(); int size = data.ModuleMatrix.Count;
                using (var canvas = drawing.Open())
                {
                    canvas.DrawRectangle(Brushes.White, null, new Rect(0, 0, size, size));
                    var path = new GeometryGroup();
                    for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                        if (data.ModuleMatrix[y][x]) path.Children.Add(new RectangleGeometry(new Rect(x, y, 1, 1)));
                    canvas.DrawGeometry(Brushes.Black, null, path);
                }
                drawing.Freeze(); var image = new DrawingImage(drawing); image.Freeze(); return image;
            }
        }
    }
}
