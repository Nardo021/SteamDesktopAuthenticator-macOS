using System;
using System.IO;
using System.Security.Cryptography;

namespace SDA.Core.Encryption
{
    /// <summary>
    /// Encrypts and decrypts *.maFile contents.
    ///
    /// Passwords are passed through 50000 rounds of PBKDF2 (RFC2898, HMAC-SHA1)
    /// with a cryptographically random salt. The derived key is used for AES-256
    /// in CBC mode with PKCS7 padding. The salt and IV are stored beside the
    /// ciphertext (manifest entry), not inside the maFile bytes.
    /// </summary>
    public static class FileEncryptor
    {
        private const int PBKDF2_ITERATIONS = 50000; //Set to 50k to make program not unbearably slow. May increase in future.
        private const int SALT_LENGTH = 8;
        private const int KEY_SIZE_BYTES = 32;
        private const int IV_LENGTH = 16;

        /// <summary>
        /// Returns an 8-byte cryptographically random salt in base64 encoding
        /// </summary>
        /// <returns></returns>
        public static string GetRandomSalt()
        {
            // RNGCryptoServiceProvider is obsolete on modern .NET. RandomNumberGenerator
            // is the same CSPRNG and still returns SALT_LENGTH raw bytes.
            byte[] salt = RandomNumberGenerator.GetBytes(SALT_LENGTH);
            return Convert.ToBase64String(salt);
        }

        /// <summary>
        /// Returns a 16-byte cryptographically random initialization vector (IV) in base64 encoding
        /// </summary>
        /// <returns></returns>
        public static string GetInitializationVector()
        {
            byte[] IV = RandomNumberGenerator.GetBytes(IV_LENGTH);
            return Convert.ToBase64String(IV);
        }

        /// <summary>
        /// Generates an encryption key derived using a password, a random salt, and specified number of rounds of PBKDF2
        ///
        /// TODO: pass in password via SecureString?
        /// </summary>
        /// <param name="password"></param>
        /// <param name="salt"></param>
        /// <returns></returns>
        private static byte[] GetEncryptionKey(string password, string salt)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentException("Password is empty");
            }
            if (string.IsNullOrEmpty(salt))
            {
                throw new ArgumentException("Salt is empty");
            }
            // The historical Rfc2898DeriveBytes constructor uses HMAC-SHA1 for 50000 iterations.
            // Pbkdf2 is the non-obsolete API for that same derivation.
            return Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(salt), PBKDF2_ITERATIONS, HashAlgorithmName.SHA1, KEY_SIZE_BYTES);
        }

        /// <summary>
        /// Tries to decrypt and return data given an encrypted base64 encoded string. Must use the same
        /// password, salt, IV, and ciphertext that was used during the original encryption of the data.
        /// </summary>
        /// <param name="password"></param>
        /// <param name="passwordSalt"></param>
        /// <param name="IV">Initialization Vector</param>
        /// <param name="encryptedData"></param>
        /// <returns></returns>
        public static string DecryptData(string password, string passwordSalt, string IV, string encryptedData)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentException("Password is empty");
            }
            if (string.IsNullOrEmpty(passwordSalt))
            {
                throw new ArgumentException("Salt is empty");
            }
            if (string.IsNullOrEmpty(IV))
            {
                throw new ArgumentException("Initialization Vector is empty");
            }
            if (string.IsNullOrEmpty(encryptedData))
            {
                throw new ArgumentException("Encrypted data is empty");
            }

            byte[] cipherText = Convert.FromBase64String(encryptedData);
            byte[] key = GetEncryptionKey(password, passwordSalt);
            string plaintext = null;

            // RijndaelManaged throws PlatformNotSupportedException off Windows.
            // A 32-byte key selects AES-256. Block size stays 128 bits, with the original CBC/PKCS7/IV inputs.
            using (Aes aes256 = Aes.Create())
            {
                aes256.IV = Convert.FromBase64String(IV);
                aes256.Key = key;
                aes256.Padding = PaddingMode.PKCS7;
                aes256.Mode = CipherMode.CBC;

                //create decryptor to perform the stream transform
                ICryptoTransform decryptor = aes256.CreateDecryptor(aes256.Key, aes256.IV);

                //wrap in a try since a bad password yields a bad key, which would throw an exception on decrypt
                try
                {
                    using (MemoryStream msDecrypt = new MemoryStream(cipherText))
                    {
                        using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                        {
                            using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                            {
                                plaintext = srDecrypt.ReadToEnd();
                            }
                        }
                    }
                }
                catch (CryptographicException)
                {
                    plaintext = null;
                }
            }
            return plaintext;
        }

        /// <summary>
        /// Encrypts a string given a password, salt, and initialization vector, then returns result in base64 encoded string.
        ///
        /// To retrieve this data, you must decrypt with the same password, salt, IV, and cyphertext that was used during encryption
        /// </summary>
        /// <param name="password"></param>
        /// <param name="passwordSalt"></param>
        /// <param name="IV"></param>
        /// <param name="plaintext"></param>
        /// <returns></returns>
        public static string EncryptData(string password, string passwordSalt, string IV, string plaintext)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentException("Password is empty");
            }
            if (string.IsNullOrEmpty(passwordSalt))
            {
                throw new ArgumentException("Salt is empty");
            }
            if (string.IsNullOrEmpty(IV))
            {
                throw new ArgumentException("Initialization Vector is empty");
            }
            if (string.IsNullOrEmpty(plaintext))
            {
                throw new ArgumentException("Plaintext data is empty");
            }
            byte[] key = GetEncryptionKey(password, passwordSalt);
            byte[] ciphertext;

            using (Aes aes256 = Aes.Create())
            {
                aes256.Key = key;
                aes256.IV = Convert.FromBase64String(IV);
                aes256.Padding = PaddingMode.PKCS7;
                aes256.Mode = CipherMode.CBC;

                ICryptoTransform encryptor = aes256.CreateEncryptor(aes256.Key, aes256.IV);

                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        using (StreamWriter swEncypt = new StreamWriter(csEncrypt))
                        {
                            swEncypt.Write(plaintext);
                        }
                        ciphertext = msEncrypt.ToArray();
                    }
                }
            }
            return Convert.ToBase64String(ciphertext);
        }
    }
}
