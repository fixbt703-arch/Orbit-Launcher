using OrbitLauncher.Utilities;

namespace OrbitLauncher.Models;

public sealed class DownloadItem : ObservableObject
{
	private double _progress;

	private string _status = LocalizationService.Translate("Ожидание");

	public string Title { get; init; } = string.Empty;

	public string Subtitle { get; init; } = string.Empty;

	public double Progress
	{
		get
		{
			return _progress;
		}
		set
		{
			SetProperty(ref _progress, value, "Progress");
		}
	}

	public string Status
	{
		get
		{
			return _status;
		}
		set
		{
			SetProperty(ref _status, value, "Status");
		}
	}
}
