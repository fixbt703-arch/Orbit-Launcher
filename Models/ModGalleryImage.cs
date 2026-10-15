using System.Windows.Media;
using OrbitLauncher.Utilities;

namespace OrbitLauncher.Models;

public sealed class ModGalleryImage : ObservableObject
{
	private ImageSource? _image;

	public string Url { get; init; } = string.Empty;

	public string Title { get; init; } = string.Empty;

	public string Description { get; init; } = string.Empty;

	public ImageSource? Image
	{
		get
		{
			return _image;
		}
		set
		{
			SetProperty(ref _image, value, "Image");
		}
	}
}
