using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SharpDX;

namespace KadoExile.Services;

public sealed class InputController
{
	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

	private const uint MouseLeftDown = 2u;

	private const uint MouseLeftUp = 4u;

	private const uint KeyExtended = 1u;

	private const uint KeyUp = 2u;

	public async Task CtrlClickAsync(Vector2 clientPoint, Vector2 windowOffset, int clickDelayMilliseconds, CancellationToken cancellationToken)
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		await _gate.WaitAsync(cancellationToken);
		try
		{
			Vector2 val = clientPoint + windowOffset;
			SetCursorPos((int)val.X, (int)val.Y);
			await Task.Delay(20, cancellationToken);
			keybd_event(162, 0, 1u, UIntPtr.Zero);
			await Task.Delay(10, cancellationToken);
			mouse_event(2u, 0u, 0u, 0u, UIntPtr.Zero);
			await Task.Delay(clickDelayMilliseconds, cancellationToken);
			mouse_event(4u, 0u, 0u, 0u, UIntPtr.Zero);
			await Task.Delay(5, cancellationToken);
		}
		finally
		{
			keybd_event(162, 0, 3u, UIntPtr.Zero);
			_gate.Release();
		}
	}

	public async Task PasteRegexAsync(string regex, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(regex))
		{
			throw new InvalidOperationException("Item regex is empty.");
		}
		await _gate.WaitAsync(cancellationToken);
		try
		{
			await SetClipboardTextAsync(regex, cancellationToken);
			await SendChordAsync(Keys.F, cancellationToken);
			await Task.Delay(70, cancellationToken);
			await SendChordAsync(Keys.V, cancellationToken);
			await Task.Delay(60, cancellationToken);
			TapKey(Keys.Return);
			await Task.Delay(120, cancellationToken);
		}
		finally
		{
			_gate.Release();
		}
	}

	private static async Task SendChordAsync(Keys key, CancellationToken cancellationToken)
	{
		keybd_event(162, 0, 1u, UIntPtr.Zero);
		await Task.Delay(10, cancellationToken);
		TapKey(key);
		await Task.Delay(10, cancellationToken);
		keybd_event(162, 0, 3u, UIntPtr.Zero);
	}

	private static void TapKey(Keys key)
	{
		keybd_event((byte)key, 0, 1u, UIntPtr.Zero);
		keybd_event((byte)key, 0, 3u, UIntPtr.Zero);
	}

	private static Task SetClipboardTextAsync(string text, CancellationToken cancellationToken)
	{
		TaskCompletionSource completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		Thread thread = new Thread((ThreadStart)delegate
		{
			try
			{
				Exception ex = null;
				for (int i = 1; i <= 5; i++)
				{
					try
					{
						Clipboard.SetText(text);
						completion.SetResult();
						return;
					}
					catch (Exception ex2)
					{
						ex = ex2;
						Thread.Sleep(40 * i);
					}
				}
				completion.SetException(ex ?? new InvalidOperationException("Could not write regex to clipboard."));
			}
			catch (Exception exception)
			{
				completion.SetException(exception);
			}
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.IsBackground = true;
		thread.Start();
		cancellationToken.Register(delegate
		{
			completion.TrySetCanceled(cancellationToken);
		});
		return completion.Task;
	}

	[DllImport("user32.dll")]
	private static extern bool SetCursorPos(int x, int y);

	[DllImport("user32.dll")]
	private static extern void mouse_event(uint flags, uint dx, uint dy, uint buttons, nuint extraInfo);

	[DllImport("user32.dll")]
	private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);
}
