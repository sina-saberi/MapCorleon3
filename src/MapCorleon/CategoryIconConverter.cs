using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using MapCorleon.Contracts;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MapCorleon.Models;
using MapCorleon.Services;

namespace MapCorleon;

public sealed class CategoryIconConverter : IValueConverter
{
    private static readonly ImageSource DefaultIcon = MakeDefaultIcon();
    private readonly Dictionary<(string Font, string Layers), ImageSource?> icons = [];
    private readonly Dictionary<string, GlyphTypeface> fonts = [];

    private readonly Dictionary<(string Atlas, Int32Rect Bounds), ImageSource> spriteIcons = [];
    private readonly Dictionary<string, BitmapImage> atlases = [];

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Category category
            ? LoadFontIcon(category) ?? LoadSpriteIcon(category) ?? (category.IconAtlas is not null ? DefaultIcon : null)
            : null;

    private ImageSource? LoadFontIcon(Category category)
    {
        if (category is not { IconLayersJson: string layersJson, IconFont: string path }) return null;
        var key = (path, layersJson);
        if (icons.TryGetValue(key, out var cached)) return cached;
        if (BundledMapCatalog.Asset(path) is not { } uri) return null;
        try
        {
            if (!fonts.TryGetValue(path, out var font))
            {
                font = new GlyphTypeface(uri);
                fonts.Add(path, font);
            }
            var layers = JsonSerializer.Deserialize<ImportedIconLayer[]>(layersJson) ?? [];
            var drawing = new DrawingGroup();
            foreach (var layer in layers)
            {
                if (!font.CharacterToGlyphMap.TryGetValue(layer.CodePoint, out var glyph)) continue;
                var brush = layer.Color is { } color && ColorConverter.ConvertFromString(color) is Color valueColor
                    ? new SolidColorBrush(valueColor) : Brushes.LightSteelBlue;
                drawing.Children.Add(new GeometryDrawing(brush, null, font.GetGlyphOutline(glyph, 16, 16)));
            }
            if (drawing.Children.Count == 0) return null;
            var image = new DrawingImage(drawing);
            image.Freeze();
            return icons[key] = image;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or FormatException or JsonException)
        {
            icons[key] = null;
            return null;
        }
    }

    private ImageSource? LoadSpriteIcon(Category category)
    {
        if (category is not { IconAtlas: string path, IconX: int x, IconY: int y,
            IconWidth: > 0 and int width, IconHeight: > 0 and int height } || x < 0 || y < 0)
            return null;
        var bounds = new Int32Rect(x, y, width, height);
        if (spriteIcons.TryGetValue((path, bounds), out var cached)) return cached;
        if (BundledMapCatalog.Asset(path) is not { } uri) return null;
        try
        {
            if (!atlases.TryGetValue(path, out var atlas))
            {
                atlas = new BitmapImage(uri);
                atlas.Freeze();
                atlases.Add(path, atlas);
            }
            if (x + width > atlas.PixelWidth || y + height > atlas.PixelHeight) return null;
            var image = new CroppedBitmap(atlas, bounds);
            image.Freeze();
            return spriteIcons[(path, bounds)] = image;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or FormatException)
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static ImageSource MakeDefaultIcon()
    {
        var geometry = Geometry.Parse("M8,0 A5,5 0 0 1 13,5 C13,9 8,15 8,15 C8,15 3,9 3,5 A5,5 0 0 1 8,0 Z");
        var image = new DrawingImage(new GeometryDrawing(Brushes.LightSteelBlue, null, geometry));
        image.Freeze();
        return image;
    }
}
