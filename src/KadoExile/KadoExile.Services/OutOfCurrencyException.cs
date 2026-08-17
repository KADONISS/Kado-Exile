using System;

namespace KadoExile.Services;

public sealed class OutOfCurrencyException : Exception
{
	public OutOfCurrencyException(string message)
		: base(message)
	{
	}
}
