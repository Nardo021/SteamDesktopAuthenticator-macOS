using Newtonsoft.Json;
using SDA.Core.Encryption;
using System;
using Xunit;

namespace SDA.Core.Tests
{
    public class FileEncryptorTests
    {
        private sealed class EncryptedVector
        {
            [JsonProperty("password")]
            public string Password { get; set; }

            [JsonProperty("salt")]
            public string Salt { get; set; }

            [JsonProperty("iv")]
            public string IV { get; set; }

            [JsonProperty("ciphertext")]
            public string Ciphertext { get; set; }

            [JsonProperty("plaintext")]
            public string Plaintext { get; set; }
        }

        [Fact]
        public void KnownVector_DecryptsToOriginalPlaintext()
        {
            EncryptedVector vector = LoadVector();

            string plaintext = FileEncryptor.DecryptData(vector.Password, vector.Salt, vector.IV, vector.Ciphertext);

            Assert.Equal(vector.Plaintext, plaintext);
        }

        [Fact]
        public void KnownVector_EncryptMatchesLegacyCiphertext()
        {
            EncryptedVector vector = LoadVector();

            string ciphertext = FileEncryptor.EncryptData(vector.Password, vector.Salt, vector.IV, vector.Plaintext);

            Assert.Equal(vector.Ciphertext, ciphertext);
        }

        [Fact]
        public void RoundTrip_ReturnsOriginalPlaintext()
        {
            string salt = FileEncryptor.GetRandomSalt();
            string iv = FileEncryptor.GetInitializationVector();
            string plaintext = "{\"account_name\":\"fixture_user\",\"note\":\"caf\u00e9\"}";

            string ciphertext = FileEncryptor.EncryptData("fixture-pass", salt, iv, plaintext);
            string decrypted = FileEncryptor.DecryptData("fixture-pass", salt, iv, ciphertext);

            Assert.Equal(plaintext, decrypted);
        }

        [Fact]
        public void SameInputs_ProduceTheSameCiphertext()
        {
            EncryptedVector vector = LoadVector();

            string first = FileEncryptor.EncryptData(vector.Password, vector.Salt, vector.IV, vector.Plaintext);
            string second = FileEncryptor.EncryptData(vector.Password, vector.Salt, vector.IV, vector.Plaintext);

            Assert.Equal(first, second);
            Assert.Equal(vector.Ciphertext, first);
        }

        [Fact]
        public void IncorrectPassword_ReturnsNull()
        {
            EncryptedVector vector = LoadVector();

            string plaintext = FileEncryptor.DecryptData("wrong-passkey", vector.Salt, vector.IV, vector.Ciphertext);

            Assert.Null(plaintext);
        }

        [Fact]
        public void MalformedCiphertext_ReturnsNull()
        {
            EncryptedVector vector = LoadVector();
            byte[] cipher = Convert.FromBase64String(vector.Ciphertext);
            cipher[cipher.Length - 1] ^= 0xFF;
            string broken = Convert.ToBase64String(cipher);

            string plaintext = FileEncryptor.DecryptData(vector.Password, vector.Salt, vector.IV, broken);

            Assert.Null(plaintext);
        }

        [Fact]
        public void NonBase64Ciphertext_ThrowsFormatException()
        {
            EncryptedVector vector = LoadVector();

            Assert.Throws<FormatException>(() =>
                FileEncryptor.DecryptData(vector.Password, vector.Salt, vector.IV, "@@@not-base64@@@"));
        }

        [Fact]
        public void EmptyPassword_ThrowsArgumentException()
        {
            EncryptedVector vector = LoadVector();

            Assert.Throws<ArgumentException>(() =>
                FileEncryptor.DecryptData("", vector.Salt, vector.IV, vector.Ciphertext));
        }

        [Fact]
        public void RandomSaltAndIv_UseOriginalLengths()
        {
            byte[] salt = Convert.FromBase64String(FileEncryptor.GetRandomSalt());
            byte[] iv = Convert.FromBase64String(FileEncryptor.GetInitializationVector());

            Assert.Equal(8, salt.Length);
            Assert.Equal(16, iv.Length);
        }

        private static EncryptedVector LoadVector()
        {
            return JsonConvert.DeserializeObject<EncryptedVector>(FixtureFiles.Read("fixture-encrypted-vector.json"));
        }
    }
}
