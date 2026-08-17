using System;

namespace KadoExile.Services;

public sealed class ListingUnavailableException : Exception
{
	public ListingUnavailableException(string message)
		: base(message)
	{
	}
}
