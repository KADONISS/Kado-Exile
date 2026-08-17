using System;
using System.Globalization;
using System.IO;

namespace KadoExile.Services;

public sealed class KadoLogger
{
	private readonly object _sync = new object();

	private readonly Action<string> _exileLog;

	private readonly string _logDirectory;

	public bool Detailed { get; set; } = true;

	public KadoLogger(string pluginDirectory, Action<string> exileLog)
	{
		_exileLog = exileLog;
		_logDirectory = Path.Combine(pluginDirectory, "Logs");
	}

	public void Info(string eventName, string message)
	{
		string text = $"{DateTimeOffset.Now:O} [{eventName}] {message}";
		_exileLog("Kado-Exile [" + eventName + "] " + message);
		lock (_sync)
		{
			Directory.CreateDirectory(_logDirectory);
			File.AppendAllText(Path.Combine(_logDirectory, "Kado-Exile-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log"), text + Environment.NewLine);
		}
	}

	public void Detail(string eventName, string message)
	{
		if (Detailed)
		{
			Info(eventName, message);
		}
	}
}
