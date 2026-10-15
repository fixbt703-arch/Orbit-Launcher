using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using OrbitLauncher.Models;
using OrbitLauncher.Services;

namespace OrbitLauncher.Dialogs;

public partial class GalleryViewerWindow : Window
{
	private readonly IReadOnlyList<ModGalleryImage> _images;

	private readonly ImageCacheService _imageCache;

	private readonly CancellationTokenSource _closed = new CancellationTokenSource();

	private CancellationTokenSource? _loading;

	private int _index;

	private double _zoom = 1.0;


	public GalleryViewerWindow(IReadOnlyList<ModGalleryImage> images, int index, ImageCacheService imageCache)
	{
		InitializeComponent();
		_images = images;
		_index = Math.Clamp(index, 0, images.Count - 1);
		_imageCache = imageCache;
		base.Loaded += async delegate
		{
			await ShowImageAsync();
		};
		base.Closed += delegate
		{
			_closed.Cancel();
			_loading?.Cancel();
			_loading?.Dispose();
			_closed.Dispose();
		};
	}

	private async Task ShowImageAsync()
	{
		if (_images.Count == 0)
		{
			return;
		}
		_loading?.Cancel();
		_loading?.Dispose();
		_loading = CancellationTokenSource.CreateLinkedTokenSource(_closed.Token);
		CancellationToken token = _loading.Token;
		ModGalleryImage modGalleryImage = _images[_index];
		PositionText.Text = $"{_index + 1} / {_images.Count}";
		CaptionText.Text = (string.IsNullOrWhiteSpace(modGalleryImage.Title) ? modGalleryImage.Description : modGalleryImage.Title);
		CaptionText.Visibility = (string.IsNullOrWhiteSpace(CaptionText.Text) ? Visibility.Collapsed : Visibility.Visible);
		LargeImage.Source = modGalleryImage.Image;
		_zoom = 1.0;
		ResizeImage();
		try
		{
			BitmapSource bitmapSource = await _imageCache.GetAsync(modGalleryImage.Url, 2200, token);
			if (!token.IsCancellationRequested && bitmapSource != null)
			{
				LargeImage.Source = bitmapSource;
				ResizeImage();
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private void ResizeImage()
	{
		if (LargeImage.Source is BitmapSource { PixelWidth: >0, PixelHeight: >0 } bitmapSource)
		{
			double num = Math.Max(200.0, ImageScroll.ViewportWidth - 48.0);
			double num2 = Math.Min(val2: Math.Max(200.0, ImageScroll.ViewportHeight - 96.0) / (double)bitmapSource.PixelHeight, val1: num / (double)bitmapSource.PixelWidth);
			LargeImage.Width = (double)bitmapSource.PixelWidth * num2 * _zoom;
			LargeImage.Height = (double)bitmapSource.PixelHeight * num2 * _zoom;
			ZoomText.Text = $"{_zoom * 100.0:0}%";
		}
	}

	private async Task NavigateAsync(int delta)
	{
		_index = (_index + delta + _images.Count) % _images.Count;
		await ShowImageAsync();
	}

	private async void Previous_Click(object sender, RoutedEventArgs e)
	{
		await NavigateAsync(-1);
	}

	private async void Next_Click(object sender, RoutedEventArgs e)
	{
		await NavigateAsync(1);
	}

	private void ZoomOut_Click(object sender, RoutedEventArgs e)
	{
		_zoom = Math.Max(0.25, _zoom / 1.25);
		ResizeImage();
	}

	private void ZoomIn_Click(object sender, RoutedEventArgs e)
	{
		_zoom = Math.Min(5.0, _zoom * 1.25);
		ResizeImage();
	}

	private void Close_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		ResizeImage();
	}

	private void ImageScroll_MouseWheel(object sender, MouseWheelEventArgs e)
	{
		if (Keyboard.Modifiers == ModifierKeys.Control)
		{
			_zoom = ((e.Delta > 0) ? Math.Min(5.0, _zoom * 1.25) : Math.Max(0.25, _zoom / 1.25));
			ResizeImage();
			e.Handled = true;
		}
	}

	private async void Viewer_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			Close();
			return;
		}
		if (e.Key == Key.Left)
		{
			await NavigateAsync(-1);
			return;
		}
		if (e.Key == Key.Right)
		{
			await NavigateAsync(1);
			return;
		}
		Key key = e.Key;
		if ((key == Key.Add || key == Key.OemPlus) ? true : false)
		{
			_zoom = Math.Min(5.0, _zoom * 1.25);
			ResizeImage();
			return;
		}
		key = e.Key;
		if ((key == Key.Subtract || key == Key.OemMinus) ? true : false)
		{
			_zoom = Math.Max(0.25, _zoom / 1.25);
			ResizeImage();
		}
	}








}
