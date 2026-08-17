namespace KadoExile.Services;

public sealed record SoundPlaybackResult(bool Success, string Path, int VolumePercent, string Message);
