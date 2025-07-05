using System.Security.Cryptography;


namespace Resource.Package.Assets.Secure
{
    internal static class AES
    {



        public static Byte[] Encrypt(Byte[] entData, Byte[] inputKey)
        {
            var key = inputKey;
            using (var aesAlg = Aes.Create())
            {
                using (var encryptor = aesAlg.CreateEncryptor(key, aesAlg.IV))
                {
                    using (var msEncrypt = new MemoryStream())
                    {
                        msEncrypt.Write(aesAlg.IV);
                        using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                        {
                            csEncrypt.Write(entData);
                        }
                        return msEncrypt.ToArray();
                    }
                }
            }
        }


        public static Byte[] Decrypt(Byte[] fullCipher, Byte[] inputKey)
        {
            var key = inputKey;
            var worldSpan = fullCipher.AsSpan();
            var iv = worldSpan.Slice(start: 0, length: 16);
            var cipher = worldSpan.Slice(start: 16);
            using (var aesAlg = Aes.Create())
            {
                using (var decryptor = aesAlg.CreateDecryptor(key, iv.ToArray()))
                {
                    using (var resultStream = new MemoryStream())
                    {
                        using (var aesStream = new CryptoStream(resultStream, decryptor, CryptoStreamMode.Write))
                        {
                            aesStream.Write(cipher);
                        }
                        return resultStream.ToArray();
                    }
                }
            }
        }

    }
}
