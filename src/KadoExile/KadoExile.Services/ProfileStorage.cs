using System;
using System.IO;

namespace KadoExile.Services;

internal static class ProfileStorage
{
	public static void WriteAtomic(string path, string content, string backupPath)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		string text = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			File.WriteAllText(text, content);
			if (File.Exists(path))
			{
				File.Replace(text, path, backupPath, ignoreMetadataErrors: true);
				return;
			}
			File.Move(text, path);
			File.Copy(path, backupPath, overwrite: true);
		}
		finally
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
	}
}
