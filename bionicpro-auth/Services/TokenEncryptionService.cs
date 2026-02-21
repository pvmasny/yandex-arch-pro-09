using BionicProAuth.Models;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace BionicProAuth.Services
{
    public class TokenEncryptionService : ITokenEncryptionService
    {
        private readonly byte[] _key;

        public static string GenerateKey()
        {
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.GenerateKey();
                return Convert.ToBase64String(aes.Key);
            }
        }

        public TokenEncryptionService(IOptions<BionicProSessionOptions> options)
        {
            var s = GenerateKey();

            if (string.IsNullOrEmpty(options.Value!.Key))
                throw new ArgumentNullException("key");

            _key = Convert.FromBase64String(options.Value!.Key);

            if (_key.Length != 32) // 256 бит = 32 байта
                throw new ArgumentException("Ключ должен быть 32 байта (256 бит) для AES-GCM");
        }

        public string Encrypt(string input)
        {
            if (string.IsNullOrEmpty(input))
                throw new ArgumentException("Refresh token не может быть пустым");

            var plainBytes = Encoding.UTF8.GetBytes(input);
            var nonce = new byte[AesGcm.NonceByteSizes.MaxSize]; // 12 байт
            var tag = new byte[AesGcm.TagByteSizes.MaxSize]; // 16 байт
            var cipherText = new byte[plainBytes.Length];

            // Генерация криптостойкого случайного nonce
            RandomNumberGenerator.Fill(nonce);

            using (var aes = new AesGcm(_key))
            {
                aes.Encrypt(nonce, plainBytes, cipherText, tag);
            }

            // Формат: [nonce (12)][tag (16)][cipherText]
            var result = new byte[nonce.Length + tag.Length + cipherText.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
            Buffer.BlockCopy(tag, 0, result, nonce.Length, tag.Length);
            Buffer.BlockCopy(cipherText, 0, result, nonce.Length + tag.Length, cipherText.Length);

            return Convert.ToBase64String(result);
        }

        public string Decrypt(string input)
        {
            if (string.IsNullOrEmpty(input))
                throw new ArgumentException("Зашифрованный токен не может быть пустым");

            var data = Convert.FromBase64String(input);

            var nonceSize = AesGcm.NonceByteSizes.MaxSize;
            var tagSize = AesGcm.TagByteSizes.MaxSize;

            if (data.Length < nonceSize + tagSize)
                throw new ArgumentException("Некорректный формат зашифрованных данных");

            var nonce = new byte[nonceSize];
            var tag = new byte[tagSize];
            var cipherBytes = new byte[data.Length - nonceSize - tagSize];

            Buffer.BlockCopy(data, 0, nonce, 0, nonceSize);
            Buffer.BlockCopy(data, nonceSize, tag, 0, tagSize);
            Buffer.BlockCopy(data, nonceSize + tagSize, cipherBytes, 0, cipherBytes.Length);

            var plainBytes = new byte[cipherBytes.Length];

            using (var aes = new AesGcm(_key))
            {
                aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
            }

            return Encoding.UTF8.GetString(plainBytes);
        }
    }
}
