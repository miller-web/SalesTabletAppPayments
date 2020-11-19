using System;
using System.Configuration;
using System.IO;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Security.Cryptography;
using System.Text;

namespace SalesTabletAppPayments.Helper
{
    public static class AesCrypto
    {
        private const int AesKeySize = 16;

        // Storing the key in hex because it's neat
        static byte[] _key = GetStringToBytes(ConfigurationManager.AppSettings["MillerAesKey"]);

        static string AesEncrypt(string data, byte[] key)
        {
            return Convert.ToBase64String(AesEncrypt(Encoding.Default.GetBytes(data), key)).Replace('+', '.').Replace('/', '_').Replace('=', '!');
        }

        static string AesDecrypt(string data, byte[] key)
        {
            return Encoding.Default.GetString(AesDecrypt(Convert.FromBase64String(data.Replace('.', '+').Replace('_', '/').Replace('!', '=')), key));
        }

        static byte[] AesEncrypt(byte[] data, byte[] key)
        {
            if (data == null || data.Length <= 0)
            {
                throw new ArgumentNullException($"{nameof(data)} cannot be empty");
            }

            if (key == null || key.Length != AesKeySize)
            {
                throw new ArgumentException($"{nameof(key)} must be length of {AesKeySize}");
            }

            using (var aes = new AesCryptoServiceProvider
            {
                Key = key,
                Mode = CipherMode.CBC,
                Padding = PaddingMode.PKCS7
            })
            {
                aes.GenerateIV();
                var iv = aes.IV;
                using (var encrypter = aes.CreateEncryptor(aes.Key, iv))
                using (var cipherStream = new MemoryStream())
                {
                    using (var tCryptoStream = new CryptoStream(cipherStream, encrypter, CryptoStreamMode.Write))
                    using (var tBinaryWriter = new BinaryWriter(tCryptoStream))
                    {
                        // prepend IV to data
                        cipherStream.Write(iv, 0, AesKeySize);
                        tBinaryWriter.Write(data);
                        tCryptoStream.FlushFinalBlock();
                    }
                    var cipherBytes = cipherStream.ToArray();

                    return cipherBytes;
                }
            }
        }

        public static string EncyptString(string s)
        {
            return AesEncrypt(s, _key);
        }

        public static string DecryptString(string encyptedDetails)
        {
            return AesDecrypt(encyptedDetails, _key);
        }

        static byte[] AesDecrypt(byte[] data, byte[] key)
        {
            if (data == null || data.Length <= 0)
            {
                throw new ArgumentNullException($"{nameof(data)} cannot be empty");
            }

            if (key == null || key.Length != AesKeySize)
            {
                throw new ArgumentException($"{nameof(key)} must be length of {AesKeySize}");
            }

            using (var aes = new AesCryptoServiceProvider
            {
                Key = key,
                Mode = CipherMode.CBC,
                Padding = PaddingMode.PKCS7
            })
            {
                // get first KeySize bytes of IV and use it to decrypt
                var iv = new byte[AesKeySize];
                Array.Copy(data, 0, iv, 0, iv.Length);

                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, aes.CreateDecryptor(aes.Key, iv), CryptoStreamMode.Write))
                    using (var binaryWriter = new BinaryWriter(cs))
                    {
                        // decrypt cipher text from data, starting just past the IV
                        binaryWriter.Write(
                            data,
                            iv.Length,
                            data.Length - iv.Length
                        );
                    }

                    var dataBytes = ms.ToArray();

                    return dataBytes;
                }
            }
        }

        public static byte[] GetStringToBytes(string value)
        {
            SoapHexBinary shb = SoapHexBinary.Parse(value);
            return shb.Value;
        }


    }
}