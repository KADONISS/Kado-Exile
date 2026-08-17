using KadoExile.Services;
using Xunit;

namespace KadoExile.Tests;

public sealed class CompletionSoundPlayerTests
{
    [Theory]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    [InlineData(70, 70)]
    [InlineData(100, 100)]
    [InlineData(150, 100)]
    public void NormalizesVolume(int input, int expected)
    {
        Assert.Equal(expected, CompletionSoundPlayer.NormalizeVolume(input));
    }

    [Fact]
    public void ResolvesDefaultAndRelativePathsAgainstSoundsDirectory()
    {
        var sounds = Path.Combine(Path.GetTempPath(), "ExileApi", "Sounds");

        Assert.Equal(
            Path.GetFullPath(Path.Combine(sounds, "treasure.wav")),
            CompletionSoundPlayer.ResolvePath(string.Empty, sounds));
        Assert.Equal(
            Path.GetFullPath(Path.Combine(sounds, "custom.mp3")),
            CompletionSoundPlayer.ResolvePath("custom.mp3", sounds));
    }

    [Fact]
    public void KeepsAbsoluteSoundPath()
    {
        var absolute = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "music.wav"));

        Assert.Equal(
            absolute,
            CompletionSoundPlayer.ResolvePath(absolute, "ignored"));
    }

    [Fact]
    public void ScalesSixteenBitPcmWavSamples()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "KadoExileSoundTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "source.wav");
        var output = Path.Combine(directory, "output.wav");
        try
        {
            WritePcmWav(source, [10000, -10000]);

            CompletionSoundPlayer.CreateVolumeAdjustedWav(source, output, 25);

            var bytes = File.ReadAllBytes(output);
            Assert.Equal((short)2500, BitConverter.ToInt16(bytes, 44));
            Assert.Equal((short)-2500, BitConverter.ToInt16(bytes, 46));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void WritePcmWav(string path, short[] samples)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + samples.Length * sizeof(short));
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(44100);
        writer.Write(44100 * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(samples.Length * sizeof(short));
        foreach (var sample in samples)
        {
            writer.Write(sample);
        }
    }
}
