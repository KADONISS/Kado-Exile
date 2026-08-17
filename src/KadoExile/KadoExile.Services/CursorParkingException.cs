using System;

namespace KadoExile.Services;

public sealed class CursorParkingException : TimeoutException
{
	public CursorParkingException(string message)
		: base(message)
	{
	}
}
