using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public class KeyloggerLogEncryption
{
    private const string VersionTag = "RLK2";
    // Public RSA key (2048-bit) used to encrypt the session AES key+IV; private key is only in the secure decoder.
    private const string RsaPublicKeyXml = "<RSAKeyValue><Modulus>2cQOyi3Qb0WhdNtjKzyPTYnyivtcKN+0YxZRXBVsq0R7ImEhBpy0AdGEGZ7iqmde30Biu0/qgej3bCBK0zP/LPXjuOZdLGYSaYPv4TUKEuiDmEfS0wVIuDw09VQO2Qzqm7a8MUVmDAJgRXtOD+VhGBVVqvz85Jer+sT7He6PF9gS28OCOgo07OsT3yNA4lvYtkZ187QHir8Yxt2YDTiUINft3DLrzTPXXjd2jKUjZ+qJfY1EzWgLJ/NYlB8ArM17iSCgtGkBwM/LYl1SVvPhKgbN1WBu/lbYqY6dXPLL0L+mPy/pObJJjakyVplQAj9FnNmaWJ0CCoPWcKlO0lT4mQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

    private static byte[] MasterKey;
    private static byte[] MasterIV;
    private static byte[] MasterHmacKey;
    private static long LineCounter;

    public static string GenerateKeyAndIV()
    {
        using (Aes aesAlg = Aes.Create())
        {
            aesAlg.GenerateKey();
            aesAlg.GenerateIV();
            MasterKey = aesAlg.Key;
            MasterIV = aesAlg.IV;
            MasterHmacKey = DeriveHmacKey(MasterKey, MasterIV);
            LineCounter = 0;
            return BuildEncryptedMasterBlob(MasterKey, MasterIV);
        }
    }

    public static string EncryptLog(string logData)
    {
        try
        {
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.GenerateKey();
                aesAlg.GenerateIV();

                string payloadWithCounter = $"{LineCounter++}:{logData}";
                byte[] encryptedKey = EncryptWithMasterKey(aesAlg.Key, MasterKey, MasterIV);

                byte[] encryptedData = EncryptData(payloadWithCounter, aesAlg.Key, aesAlg.IV);

                string encryptedKeyBase64 = Convert.ToBase64String(encryptedKey);
                string ivBase64 = Convert.ToBase64String(aesAlg.IV);
                string encryptedDataBase64 = Convert.ToBase64String(encryptedData);
                string hmacBase64 = Convert.ToBase64String(ComputeHmac(encryptedKey, aesAlg.IV, encryptedData));

                return $"{encryptedKeyBase64}|{ivBase64}|{encryptedDataBase64}|{hmacBase64}";
            }
        }
        catch (Exception ex)
        {
            Modding.Logger.Log($"�訡�� �� ��஢���� ����: {ex.Message}");
            return null;
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

    private static byte[] ComputeHmac(byte[] encryptedKey, byte[] iv, byte[] encryptedData)
    {
        if (MasterHmacKey == null || MasterHmacKey.Length == 0)
        {
            MasterHmacKey = DeriveHmacKey(MasterKey, MasterIV);
        }

        byte[] buffer = new byte[encryptedKey.Length + iv.Length + encryptedData.Length];
        Buffer.BlockCopy(encryptedKey, 0, buffer, 0, encryptedKey.Length);
        Buffer.BlockCopy(iv, 0, buffer, encryptedKey.Length, iv.Length);
        Buffer.BlockCopy(encryptedData, 0, buffer, encryptedKey.Length + iv.Length, encryptedData.Length);

        using (HMACSHA256 hmac = new HMACSHA256(MasterHmacKey))
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

    public static string ByteArrayToHexString(byte[] bytes)
    {
        StringBuilder sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
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
            Modding.Logger.Log($"ReplayLogger: failed to encrypt session key (fallback to legacy header): {ex.Message}");
            return $"{ByteArrayToHexString(key)}|{ByteArrayToHexString(iv)}| NNtV04Zl56k8/Ye62cQ1JA==";
        }
    }
}
