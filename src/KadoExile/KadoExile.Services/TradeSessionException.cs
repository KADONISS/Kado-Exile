using System;

namespace KadoExile.Services;

public sealed class TradeSessionException : Exception
{
	public TradeSessionException(string message)
		: base(message)
	{
	}
}
