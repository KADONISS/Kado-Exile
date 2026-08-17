using System;

namespace KadoExile.Services;

public sealed class InventoryFullException : Exception
{
	public InventoryFullException(string message)
		: base(message)
	{
	}
}
