namespace BionicProAuth.Services;

public interface ITokenEncryptionService
{
    string Encrypt(string input);

    string Decrypt(string input);
}
