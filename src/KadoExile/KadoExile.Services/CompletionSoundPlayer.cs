using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace KadoExile.Services;

public sealed class CompletionSoundPlayer(string soundsDirectory) : IDisposable
{
	private const string Alias = "KadoExileCompletion";

	private readonly object _sync = new object();

	private string? _temporaryWav;

	public SoundPlaybackResult Play(string configuredPath, int volumePercent)
	{
		string text = ResolvePath(configuredPath, soundsDirectory);
		int num = NormalizeVolume(volumePercent);
		if (!File.Exists(text))
		{
			return new SoundPlaybackResult(Success: false, text, num, "Sound file was not found.");
		}
		string extension = Path.GetExtension(text);
		if (!extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
		{
			return new SoundPlaybackResult(Success: false, text, num, "Only WAV and MP3 files are supported.");
		}
		lock (_sync)
		{
			Send("close KadoExileCompletion");
			DeleteTemporaryWav();
			string text2 = text;
			if (extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) && num < 100)
			{
				string text3 = Path.Combine(Path.GetTempPath(), "Kado-Exile");
				Directory.CreateDirectory(text3);
				_temporaryWav = Path.Combine(text3, $"completion-{Guid.NewGuid():N}.wav");
				CreateVolumeAdjustedWav(text, _temporaryWav, num);
				text2 = _temporaryWav;
			}
			else if (extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) && num < 100)
			{
				return new SoundPlaybackResult(Success: false, text, num, "WinMM cannot apply per-file volume to MP3; use the ExileAPI fallback or a WAV file.");
			}
			uint num2 = Send("open \"" + text2.Replace("\"", "\"\"") + "\" alias KadoExileCompletion");
			if (num2 != 0)
			{
				return Failure(text, num, "open", num2);
			}
			uint num3 = Send("play KadoExileCompletion from 0");
			if (num3 != 0)
			{
				Send("close KadoExileCompletion");
				return Failure(text, num, "play", num3);
			}
		}
		return new SoundPlaybackResult(Success: true, text, num, "Played through WinMM MCI.");
	}

	public void Dispose()
	{
		lock (_sync)
		{
			Send("close KadoExileCompletion");
			DeleteTemporaryWav();
		}
	}

	public static int NormalizeVolume(int volumePercent)
	{
		return Math.Clamp(volumePercent, 0, 100);
	}

	public static string ResolvePath(string configuredPath, string soundsDirectory)
	{
		string text = (string.IsNullOrWhiteSpace(configuredPath) ? "treasure.wav" : configuredPath.Trim().Trim('"'));
		return Path.GetFullPath(Path.IsPathRooted(text) ? text : Path.Combine(soundsDirectory, text));
	}

	public static void CreateVolumeAdjustedWav(string sourcePath, string destinationPath, int volumePercent)
	{
		byte[] array = File.ReadAllBytes(sourcePath);
		if (array.Length < 44 || Encoding.ASCII.GetString(array, 0, 4) != "RIFF" || Encoding.ASCII.GetString(array, 8, 4) != "WAVE")
		{
			throw new InvalidDataException("The completion WAV file is invalid.");
		}
		ushort num = 0;
		ushort num2 = 0;
		int num3 = -1;
		int num4 = 0;
		int num5 = 12;
		while (num5 + 8 <= array.Length)
		{
			string text = Encoding.ASCII.GetString(array, num5, 4);
			int num6 = BitConverter.ToInt32(array, num5 + 4);
			int num7 = num5 + 8;
			if (num6 < 0 || num7 + num6 > array.Length)
			{
				throw new InvalidDataException("The completion WAV chunk table is invalid.");
			}
			if (text == "fmt " && num6 >= 16)
			{
				num = BitConverter.ToUInt16(array, num7);
				num2 = BitConverter.ToUInt16(array, num7 + 14);
			}
			else if (text == "data")
			{
				num3 = num7;
				num4 = num6;
				break;
			}
			num5 = num7 + num6 + (num6 & 1);
		}
		bool flag = num != 1 || num3 < 0;
		if (!flag)
		{
			bool flag2 = ((num2 == 8 || num2 == 16) ? true : false);
			flag = !flag2;
		}
		if (flag)
		{
			throw new NotSupportedException("Only 8-bit and 16-bit PCM WAV files support scaled volume.");
		}
		float num8 = (float)NormalizeVolume(volumePercent) / 100f;
		if (num2 == 8)
		{
			for (int i = num3; i < num3 + num4; i++)
			{
				array[i] = (byte)Math.Clamp(128 + (int)Math.Round((float)(array[i] - 128) * num8), 0, 255);
			}
		}
		else
		{
			for (int j = num3; j + 1 < num3 + num4; j += 2)
			{
				short num9 = (short)Math.Clamp((int)Math.Round((float)BitConverter.ToInt16(array, j) * num8), -32768, 32767);
				array[j] = (byte)(num9 & 0xFF);
				array[j + 1] = (byte)((num9 >> 8) & 0xFF);
			}
		}
		File.WriteAllBytes(destinationPath, array);
	}

	private static SoundPlaybackResult Failure(string path, int volume, string operation, uint errorCode)
	{
		return new SoundPlaybackResult(Success: false, path, volume, $"WinMM {operation} failed ({errorCode}): {GetErrorText(errorCode)}");
	}

	private static uint Send(string command)
	{
		return mciSendString(command, null, 0, IntPtr.Zero);
	}

	private void DeleteTemporaryWav()
	{
		if (_temporaryWav != null)
		{
			try
			{
				File.Delete(_temporaryWav);
			}
			catch
			{
			}
			_temporaryWav = null;
		}
	}

	private static string GetErrorText(uint errorCode)
	{
		StringBuilder stringBuilder = new StringBuilder(256);
		if (!mciGetErrorString(errorCode, stringBuilder, stringBuilder.Capacity))
		{
			return "Unknown MCI error";
		}
		return stringBuilder.ToString();
	}

	[DllImport("winmm.dll", CharSet = CharSet.Unicode)]
	private static extern uint mciSendString(string command, StringBuilder? returnValue, int returnLength, nint callback);

	[DllImport("winmm.dll", CharSet = CharSet.Unicode)]
	private static extern bool mciGetErrorString(uint errorCode, StringBuilder errorText, int errorTextSize);
}
