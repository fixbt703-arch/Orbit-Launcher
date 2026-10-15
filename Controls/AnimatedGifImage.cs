using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SkiaSharp;

namespace OrbitLauncher.Controls;

public sealed class AnimatedGifImage : Image
{
	private const int MaximumAnimatedFrames = 180;

	private static readonly SemaphoreSlim DecodeLimiter = new SemaphoreSlim(1, 1);

	private readonly DispatcherTimer _timer = new DispatcherTimer(DispatcherPriority.Background);

	private IReadOnlyList<BitmapFrame> _frames = Array.Empty<BitmapFrame>();

	private IReadOnlyList<TimeSpan> _delays = Array.Empty<TimeSpan>();

	private int _frameIndex;

	public AnimatedGifImage()
	{
		_timer.Tick += Timer_Tick;
		base.Loaded += delegate
		{
			Start();
		};
		base.Unloaded += delegate
		{
			Release();
		};
	}

	public async Task<bool> LoadAsync(byte[] bytes, int decodePixelWidth = 760, CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			if (IsGif(bytes))
			{
				await DecodeLimiter.WaitAsync(cancellationToken);
				try
				{
					(IReadOnlyList<BitmapFrame>, IReadOnlyList<TimeSpan>) obj = await Task.Run(() => DecodeGifFrames(bytes, decodePixelWidth, cancellationToken), cancellationToken);
					cancellationToken.ThrowIfCancellationRequested();
					(IReadOnlyList<BitmapFrame>, IReadOnlyList<TimeSpan>) tuple = obj;
					_frames = tuple.Item1;
					_delays = tuple.Item2;
					_frameIndex = 0;
					base.Source = _frames.FirstOrDefault();
					Start();
					return base.Source != null;
				}
				catch (InvalidDataException)
				{
					base.Source = await Task.Run(() => DecodeStatic(bytes, decodePixelWidth), cancellationToken);
					_frames = Array.Empty<BitmapFrame>();
					_delays = Array.Empty<TimeSpan>();
					return base.Source != null;
				}
				finally
				{
					DecodeLimiter.Release();
				}
			}
			BitmapSource source = await Task.Run(() => DecodeStatic(bytes, decodePixelWidth), cancellationToken);
			_frames = Array.Empty<BitmapFrame>();
			_delays = Array.Empty<TimeSpan>();
			base.Source = source;
			return true;
		}
		catch (OperationCanceledException)
		{
			base.Source = null;
			_frames = Array.Empty<BitmapFrame>();
			_delays = Array.Empty<TimeSpan>();
			_timer.Stop();
			throw;
		}
		catch
		{
			base.Source = null;
			_frames = Array.Empty<BitmapFrame>();
			_delays = Array.Empty<TimeSpan>();
			_timer.Stop();
			return false;
		}
	}

	private static BitmapSource DecodeStatic(byte[] bytes, int decodePixelWidth)
	{
		try
		{
			using MemoryStream streamSource = new MemoryStream(bytes, writable: false);
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			if (decodePixelWidth > 0)
			{
				bitmapImage.DecodePixelWidth = decodePixelWidth;
			}
			bitmapImage.StreamSource = streamSource;
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			return bitmapImage;
		}
		catch
		{
			using SKData data = SKData.CreateCopy(bytes);
			using SKCodec sKCodec = SKCodec.Create(data) ?? throw new InvalidDataException("Unsupported image format.");
			if (sKCodec.Info.Width <= 0 || sKCodec.Info.Height <= 0)
			{
				throw new InvalidDataException("Invalid image dimensions.");
			}
			float desiredScale = ((decodePixelWidth > 0 && sKCodec.Info.Width > decodePixelWidth) ? ((float)decodePixelWidth / (float)sKCodec.Info.Width) : 1f);
			SKSizeI scaledDimensions = sKCodec.GetScaledDimensions(desiredScale);
			SKImageInfo info = new SKImageInfo(Math.Max(1, scaledDimensions.Width), Math.Max(1, scaledDimensions.Height), SKColorType.Bgra8888, SKAlphaType.Premul);
			if ((long)info.Width * (long)info.Height > 20000000)
			{
				throw new InvalidDataException("Image dimensions are too large.");
			}
			using SKBitmap sKBitmap = new SKBitmap(info);
			SKCodecResult pixels = sKCodec.GetPixels(info, sKBitmap.GetPixels(), SKCodecOptions.Default);
			if ((uint)pixels > 1u)
			{
				throw new InvalidDataException($"Image could not be decoded: {pixels}.");
			}
			using SKImage sKImage = SKImage.FromBitmap(sKBitmap);
			using SKData sKData = sKImage.Encode(SKEncodedImageFormat.Png, 90) ?? throw new InvalidDataException("Image conversion failed.");
			using MemoryStream streamSource2 = new MemoryStream(sKData.ToArray(), writable: false);
			BitmapImage bitmapImage2 = new BitmapImage();
			bitmapImage2.BeginInit();
			bitmapImage2.CacheOption = BitmapCacheOption.OnLoad;
			if (decodePixelWidth > 0)
			{
				bitmapImage2.DecodePixelWidth = decodePixelWidth;
			}
			bitmapImage2.StreamSource = streamSource2;
			bitmapImage2.EndInit();
			bitmapImage2.Freeze();
			return bitmapImage2;
		}
	}

	private void Start()
	{
		if (_frames.Count >= 2 && base.IsLoaded)
		{
			_timer.Interval = _delays[_frameIndex];
			_timer.Start();
		}
	}

	public void Release()
	{
		_timer.Stop();
		base.Source = null;
		_frames = Array.Empty<BitmapFrame>();
		_delays = Array.Empty<TimeSpan>();
		_frameIndex = 0;
	}

	private void Timer_Tick(object? sender, EventArgs e)
	{
		if (_frames.Count < 2 || !base.IsLoaded)
		{
			_timer.Stop();
			return;
		}
		_frameIndex = (_frameIndex + 1) % _frames.Count;
		base.Source = _frames[_frameIndex];
		_timer.Interval = _delays[_frameIndex];
	}

	private static bool IsGif(byte[] bytes)
	{
		if (bytes.Length >= 6 && bytes[0] == 71 && bytes[1] == 73)
		{
			return bytes[2] == 70;
		}
		return false;
	}

	private static (IReadOnlyList<BitmapFrame> Frames, IReadOnlyList<TimeSpan> Delays) DecodeGifFrames(byte[] bytes, int decodePixelWidth, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		using SKData data = SKData.CreateCopy(bytes);
		using SKCodec sKCodec = SKCodec.Create(data) ?? throw new InvalidDataException("GIF decoder could not be created.");
		if (sKCodec.FrameCount <= 0 || sKCodec.Info.Width <= 0 || sKCodec.Info.Height <= 0)
		{
			throw new InvalidDataException("GIF does not contain valid frames.");
		}
		if (sKCodec.FrameCount > 180)
		{
			throw new InvalidDataException("GIF contains too many frames for safe animation.");
		}
		double val = ((decodePixelWidth > 0 && sKCodec.Info.Width > decodePixelWidth) ? ((double)decodePixelWidth / (double)sKCodec.Info.Width) : 1.0);
		double val2 = Math.Sqrt(Math.Max(1.0, 33554432.0 / (double)Math.Max(1, sKCodec.FrameCount) / 8.0) / ((double)sKCodec.Info.Width * (double)sKCodec.Info.Height));
		float desiredScale = (float)Math.Min(1.0, Math.Min(val, val2));
		SKSizeI scaledDimensions = sKCodec.GetScaledDimensions(desiredScale);
		SKImageInfo info = new SKImageInfo(Math.Max(1, scaledDimensions.Width), Math.Max(1, scaledDimensions.Height), SKColorType.Bgra8888, SKAlphaType.Premul);
		using SKBitmap sKBitmap = new SKBitmap(info);
		List<byte[]> list = new List<byte[]>(sKCodec.FrameCount);
		List<BitmapFrame> list2 = new List<BitmapFrame>(sKCodec.FrameCount);
		List<TimeSpan> list3 = new List<TimeSpan>(sKCodec.FrameCount);
		SKCodecFrameInfo[] frameInfo = sKCodec.FrameInfo;
		for (int i = 0; i < sKCodec.FrameCount; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			int num = ((i < frameInfo.Length) ? frameInfo[i].RequiredFrame : (i - 1));
			if (num >= 0 && num < list.Count)
			{
				Marshal.Copy(list[num], 0, sKBitmap.GetPixels(), sKBitmap.ByteCount);
			}
			else
			{
				sKBitmap.Erase(SKColors.Transparent);
				num = -1;
			}
			SKCodecResult sKCodecResult = sKCodec.GetPixels(options: new SKCodecOptions(i, num), info: info, pixels: sKBitmap.GetPixels(), rowBytes: sKBitmap.RowBytes);
			if ((uint)sKCodecResult > 1u)
			{
				throw new InvalidDataException($"GIF frame {i} could not be decoded: {sKCodecResult}.");
			}
			byte[] bytes2 = sKBitmap.Bytes;
			list.Add(bytes2);
			BitmapSource bitmapSource = BitmapSource.Create(info.Width, info.Height, 96.0, 96.0, PixelFormats.Pbgra32, null, bytes2, sKBitmap.RowBytes);
			bitmapSource.Freeze();
			BitmapFrame bitmapFrame = BitmapFrame.Create(bitmapSource);
			bitmapFrame.Freeze();
			list2.Add(bitmapFrame);
			int val3 = ((i < frameInfo.Length) ? frameInfo[i].Duration : 100);
			list3.Add(TimeSpan.FromMilliseconds(Math.Max(33, val3)));
		}
		return (Frames: list2, Delays: list3);
	}
}
