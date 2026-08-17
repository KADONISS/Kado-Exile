namespace KadoExile.Services;

public interface ISecretProtector
{
	string Protect(string value);

	string Unprotect(string protectedValue);
}
