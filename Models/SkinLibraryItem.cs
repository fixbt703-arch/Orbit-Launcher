using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace OrbitLauncher.Models;

public sealed class SkinLibraryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Skin";
    public string Variant { get; set; } = "classic";
    public string FileName { get; set; } = string.Empty;
    [JsonIgnore] public string FilePath { get; set; } = string.Empty;
    [JsonIgnore] public BitmapSource? Preview { get; set; }
    [JsonIgnore] public string ModelText => Variant == "slim" ? "Slim · Alex" : "Classic · Steve";
}
