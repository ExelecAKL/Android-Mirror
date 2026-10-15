using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;

namespace AndroidMirror
{
    public static class SvgLogo
    {
        public static DrawingImage Load(Stream stream)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            XDocument document;
            using (var reader = XmlReader.Create(stream, settings)) document = XDocument.Load(reader);
            var fills = new Dictionary<string, string>();
            foreach (var style in document.Descendants().Where(e => e.Name.LocalName == "style"))
                foreach (Match match in Regex.Matches(style.Value, @"\.([\w-]+)\s*\{\s*fill\s*:\s*([^;}]+)")) fills[match.Groups[1].Value] = match.Groups[2].Value.Trim();
            var drawing = new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 1080, 1080))));
            foreach (var path in document.Descendants().Where(e => e.Name.LocalName == "path"))
            {
                string fill = (string)path.Attribute("fill");
                if (String.IsNullOrEmpty(fill)) fills.TryGetValue((string)path.Attribute("class") ?? "", out fill);
                if (fill == "none") continue;
                if (String.IsNullOrEmpty(fill)) fill = "#000000";
                if (path.Attribute("transform") != null || path.Ancestors().Any(e => e.Attribute("transform") != null)) throw new InvalidDataException("This logo includes an unsupported transform.");
                drawing.Children.Add(new GeometryDrawing((Brush)new BrushConverter().ConvertFromString(fill), null, Geometry.Parse("F1 " + (string)path.Attribute("d"))));
            }
            var image = new DrawingImage(drawing); image.Freeze(); return image;
        }
        public static DrawingImage LoadEmbedded() { using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("brand.svg")) return Load(stream); }
        public static byte[] Png(DrawingImage image, int size)
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen()) { context.PushTransform(new ScaleTransform(size / 1080.0, size / 1080.0)); context.DrawDrawing(image.Drawing); context.Pop(); }
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = new MemoryStream()) { encoder.Save(output); return output.ToArray(); }
        }
    }
#if ASSET_TOOL
    static class LogoAssets
    {
        [STAThread]
        static void Main(string[] args)
        {
            DrawingImage image; using (var source = File.OpenRead(args[0])) image = SvgLogo.Load(source);
            File.WriteAllBytes(Path.Combine(args[1], "brand.png"), SvgLogo.Png(image, 256));
            int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
            byte[][] frames = sizes.Select(size => SvgLogo.Png(image, size)).ToArray();
            using (var output = new BinaryWriter(File.Create(Path.Combine(args[1], "app.ico"))))
            {
                output.Write((ushort)0); output.Write((ushort)1); output.Write((ushort)sizes.Length);
                int offset = 6 + sizes.Length * 16;
                for (int i = 0; i < sizes.Length; i++)
                {
                    output.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); output.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); output.Write((byte)0); output.Write((byte)0);
                    output.Write((ushort)1); output.Write((ushort)32); output.Write(frames[i].Length); output.Write(offset); offset += frames[i].Length;
                }
                foreach (var frame in frames) output.Write(frame);
            }
        }
    }
#endif
}
