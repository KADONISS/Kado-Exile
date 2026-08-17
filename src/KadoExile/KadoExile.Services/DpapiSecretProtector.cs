using System;
using System.Security.Cryptography;
using System.Text;

namespace KadoExile.Services;

public sealed class DpapiSecretProtector : ISecretProtector
{
	private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Kado-Exile.Profile.v1");

	public string Protect(string value)
	{
		return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value ?? string.Empty), Entropy, DataProtectionScope.CurrentUser));
	}

	public string Unprotect(string protectedValue)
	{
		byte[] encryptedData = Convert.FromBase64String(protectedValue);
		return Encoding.UTF8.GetString(ProtectedData.Unprotect(encryptedData, Entropy, DataProtectionScope.CurrentUser));
	}
}
