param(
    [Parameter(Mandatory = $true)]
    [string]$SourceSvg,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [string]$BaseName = "cqepc-logo"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

Add-Type -ReferencedAssemblies @(
    "PresentationCore",
    "WindowsBase",
    "System.Xaml",
    "System.Xml",
    "System.Xml.Linq"
) -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BrandAssetTools
{
public static class SvgBrandRenderer
{
    public static void Render(string sourceSvg, string outputDirectory, string baseName)
    {
        XDocument document = XDocument.Load(sourceSvg);
        XElement root = document.Root;
        if (root == null)
        {
            throw new InvalidOperationException("SVG root not found.");
        }

        XAttribute viewBoxAttribute = root.Attribute("viewBox");
        if (viewBoxAttribute == null)
        {
            throw new InvalidOperationException("SVG viewBox is required.");
        }

        string[] viewBox = viewBoxAttribute.Value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (viewBox.Length != 4)
        {
            throw new InvalidOperationException("SVG viewBox must contain four values.");
        }

        double width = double.Parse(viewBox[2], CultureInfo.InvariantCulture);
        double height = double.Parse(viewBox[3], CultureInfo.InvariantCulture);

        string pngPath = Path.Combine(outputDirectory, baseName + ".png");
        string icoPath = Path.Combine(outputDirectory, baseName + ".ico");
        string svgPath = Path.Combine(outputDirectory, baseName + ".svg");
        File.Copy(sourceSvg, svgPath, overwrite: true);

        SavePng(root, width, height, pngPath, 512);
        SaveIcon(root, width, height, icoPath, new[] { 16, 24, 32, 48, 64, 128, 256 });
    }

    private static void SavePng(XElement root, double viewWidth, double viewHeight, string path, int size)
    {
        using (FileStream stream = File.Create(path))
        {
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(RenderBitmap(root, viewWidth, viewHeight, size)));
            encoder.Save(stream);
        }
    }

    private static void SaveIcon(XElement root, double viewWidth, double viewHeight, string path, int[] sizes)
    {
        using (FileStream stream = File.Create(path))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            List<byte[]> frames = new List<byte[]>();
            foreach (int size in sizes)
            {
                frames.Add(EncodePng(RenderBitmap(root, viewWidth, viewHeight, size)));
            }

            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)frames.Count);

            int offset = 6 + (frames.Count * 16);
            for (int index = 0; index < sizes.Length; index++)
            {
                int size = sizes[index];
                byte dimension = size >= 256 ? (byte)0 : (byte)size;
                byte[] frame = frames[index];
                writer.Write(dimension);
                writer.Write(dimension);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(frame.Length);
                writer.Write(offset);
                offset += frame.Length;
            }

            foreach (byte[] frame in frames)
            {
                writer.Write(frame);
            }
        }
    }

    private static RenderTargetBitmap RenderBitmap(XElement root, double viewWidth, double viewHeight, int size)
    {
        DrawingVisual visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(size / viewWidth, size / viewHeight));
            foreach (XElement element in root.Elements())
            {
                DrawElement(context, element, null);
            }

            context.Pop();
        }

        RenderTargetBitmap bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] EncodePng(RenderTargetBitmap bitmap)
    {
        PngBitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (MemoryStream stream = new MemoryStream())
        {
            encoder.Save(stream);
            return stream.ToArray();
        }
    }

    private static void DrawElement(DrawingContext context, XElement element, Brush inheritedFill)
    {
        if (element.Name.LocalName == "g")
        {
            Brush fill = ParseBrush(GetAttributeValue(element, "fill")) ?? inheritedFill;
            foreach (XElement child in element.Elements())
            {
                DrawElement(context, child, fill);
            }

            return;
        }

        if (element.Name.LocalName != "path")
        {
            return;
        }

        string data = GetAttributeValue(element, "d");
        if (string.IsNullOrWhiteSpace(data))
        {
            return;
        }

        Geometry geometry = Geometry.Parse(data);
        geometry.Freeze();
        Brush fillBrush = ParseBrush(GetAttributeValue(element, "fill")) ?? inheritedFill ?? Brushes.Transparent;
        context.DrawGeometry(fillBrush, null, geometry);
    }

    private static Brush ParseBrush(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return (Brush)new BrushConverter().ConvertFromString(value);
    }

    private static string GetAttributeValue(XElement element, string name)
    {
        XAttribute attribute = element.Attribute(name);
        return attribute == null ? null : attribute.Value;
    }
}
}
"@

[BrandAssetTools.SvgBrandRenderer]::Render($SourceSvg, $OutputDirectory, $BaseName)
