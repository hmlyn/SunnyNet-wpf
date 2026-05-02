using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace SunnyNet.Wpf.Windows;

public partial class CryptoToolWindow : Window
{
    public CryptoToolWindow()
    {
        InitializeComponent();
    }

    private void ClearAll_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        SymmetricInputTextBox.Clear();
        SymmetricOutputTextBox.Clear();
        SymmetricKeyTextBox.Clear();
        SymmetricIvTextBox.Clear();
        RsaKeyTextBox.Clear();
        RsaInputTextBox.Clear();
        RsaOutputTextBox.Clear();
        HashInputTextBox.Clear();
        HashOutputTextBox.Clear();
        HashHmacKeyTextBox.Clear();
        ToolSummaryTextBlock.Text = "支持 AES/DES、RSA 和常用哈希，输入/输出支持文本、Base64、Base64URL、HEX。";
    }

    private void SymmetricEncrypt_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        RunAction(() =>
        {
            byte[] input = DecodeData(SymmetricInputTextBox.Text, GetComboText(SymmetricInputFormatComboBox));
            byte[] output = TransformSymmetric(input, encrypt: true);
            SymmetricOutputTextBox.Text = EncodeData(output, GetComboText(SymmetricOutputFormatComboBox));
            ToolSummaryTextBlock.Text = $"对称加密完成：{input.Length:N0} → {output.Length:N0} Bytes。";
        }, "对称加密失败");
    }

    private void SymmetricDecrypt_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        RunAction(() =>
        {
            byte[] input = DecodeData(SymmetricInputTextBox.Text, GetComboText(SymmetricInputFormatComboBox));
            byte[] output = TransformSymmetric(input, encrypt: false);
            SymmetricOutputTextBox.Text = EncodeData(output, GetComboText(SymmetricOutputFormatComboBox));
            ToolSummaryTextBlock.Text = $"对称解密完成：{input.Length:N0} → {output.Length:N0} Bytes。";
        }, "对称解密失败");
    }

    private byte[] TransformSymmetric(byte[] input, bool encrypt)
    {
        string algorithm = GetComboText(SymmetricAlgorithmComboBox);
        string mode = GetComboText(SymmetricModeComboBox);
        byte[] key = DecodeData(SymmetricKeyTextBox.Text, GetComboText(SymmetricKeyFormatComboBox));
        byte[] iv = DecodeData(SymmetricIvTextBox.Text, GetComboText(SymmetricIvFormatComboBox));

        if (mode == "GCM")
        {
            if (algorithm != "AES")
            {
                throw new InvalidOperationException("GCM 仅支持 AES。用法：IV/Nonce 建议 12 字节，密文格式为 密文+16字节Tag。 ");
            }

            return TransformAesGcm(input, key, iv, encrypt);
        }

        using SymmetricAlgorithm symmetric = algorithm == "DES" ? DES.Create() : Aes.Create();
        symmetric.Key = NormalizeKey(key, algorithm == "DES" ? 8 : 32);
        symmetric.Mode = mode switch
        {
            "ECB" => CipherMode.ECB,
            _ => CipherMode.CBC
        };
        symmetric.Padding = GetPaddingMode(GetComboText(SymmetricPaddingComboBox));
        if (symmetric.Mode != CipherMode.ECB)
        {
            symmetric.IV = NormalizeKey(iv, symmetric.BlockSize / 8);
        }

        using ICryptoTransform transform = encrypt ? symmetric.CreateEncryptor() : symmetric.CreateDecryptor();
        return transform.TransformFinalBlock(input, 0, input.Length);
    }

    private static byte[] TransformAesGcm(byte[] input, byte[] key, byte[] nonce, bool encrypt)
    {
        byte[] normalizedKey = NormalizeKey(key, 32);
        byte[] normalizedNonce = NormalizeKey(nonce, 12);
        using AesGcm aesGcm = new(normalizedKey, 16);
        if (encrypt)
        {
            byte[] cipher = new byte[input.Length];
            byte[] tag = new byte[16];
            aesGcm.Encrypt(normalizedNonce, input, cipher, tag);
            return cipher.Concat(tag).ToArray();
        }

        if (input.Length < 16)
        {
            throw new InvalidOperationException("GCM 密文长度必须包含 16 字节 Tag。密文格式：密文+Tag。 ");
        }

        byte[] ciphertext = input[..^16];
        byte[] authTag = input[^16..];
        byte[] plain = new byte[ciphertext.Length];
        aesGcm.Decrypt(normalizedNonce, ciphertext, authTag, plain);
        return plain;
    }

    private void RsaEncrypt_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        RunAction(() =>
        {
            byte[] input = DecodeData(RsaInputTextBox.Text, GetComboText(RsaInputFormatComboBox));
            using RSA rsa = LoadRsaKey();
            byte[] output = rsa.Encrypt(input, GetRsaEncryptionPadding());
            RsaOutputTextBox.Text = EncodeData(output, GetComboText(RsaOutputFormatComboBox));
            ToolSummaryTextBlock.Text = $"RSA 加密完成：{input.Length:N0} → {output.Length:N0} Bytes。";
        }, "RSA 加密失败");
    }

    private void RsaDecrypt_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        RunAction(() =>
        {
            byte[] input = DecodeData(RsaInputTextBox.Text, GetComboText(RsaInputFormatComboBox));
            using RSA rsa = LoadRsaKey();
            byte[] output = rsa.Decrypt(input, GetRsaEncryptionPadding());
            RsaOutputTextBox.Text = EncodeData(output, GetComboText(RsaOutputFormatComboBox));
            ToolSummaryTextBlock.Text = $"RSA 解密完成：{input.Length:N0} → {output.Length:N0} Bytes。";
        }, "RSA 解密失败");
    }

    private RSA LoadRsaKey()
    {
        string key = RsaKeyTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("请输入 RSA 公钥或私钥。支持 PEM/XML。 ");
        }

        RSA rsa = RSA.Create();
        if (GetComboText(RsaKeyFormatComboBox) == "XML")
        {
            rsa.FromXmlString(key);
            return rsa;
        }

        if (key.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
        {
            rsa.ImportFromPem(key);
        }
        else
        {
            rsa.ImportFromPem(key);
        }

        return rsa;
    }

    private void Hash_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        RunAction(() =>
        {
            string algorithm = GetComboText(HashAlgorithmComboBox);
            byte[] input = DecodeData(HashInputTextBox.Text, GetComboText(HashInputFormatComboBox));
            string hmacKeyText = HashHmacKeyTextBox.Text ?? "";
            bool useHmac = !string.IsNullOrWhiteSpace(hmacKeyText);
            byte[] output = useHmac
                ? ComputeHmac(input, DecodeData(hmacKeyText, GetComboText(HashHmacKeyFormatComboBox)), algorithm)
                : ComputeHash(input, algorithm);
            HashOutputTextBox.Text = EncodeData(output, GetComboText(HashOutputFormatComboBox));
            ToolSummaryTextBlock.Text = useHmac
                ? $"HMAC 计算完成：HMAC-{algorithm}，{output.Length:N0} Bytes。"
                : $"哈希计算完成：{algorithm}，{output.Length:N0} Bytes。";
        }, "哈希计算失败");
    }

    private static byte[] ComputeHash(byte[] input, string algorithm)
    {
        return algorithm switch
        {
            "MD5" => MD5.HashData(input),
            "SHA1" => SHA1.HashData(input),
            "SHA384" => SHA384.HashData(input),
            "SHA512" => SHA512.HashData(input),
            "SHA3-256" when SHA3_256.IsSupported => SHA3_256.HashData(input),
            "SHA3-384" when SHA3_384.IsSupported => SHA3_384.HashData(input),
            "SHA3-512" when SHA3_512.IsSupported => SHA3_512.HashData(input),
            "SHA3-256" or "SHA3-384" or "SHA3-512" => throw new PlatformNotSupportedException($"当前 .NET/系统不支持 {algorithm}。"),
            _ => SHA256.HashData(input)
        };
    }

    private static byte[] ComputeHmac(byte[] input, byte[] key, string algorithm)
    {
        using HMAC hmac = algorithm switch
        {
            "MD5" => new HMACMD5(key),
            "SHA1" => new HMACSHA1(key),
            "SHA384" => new HMACSHA384(key),
            "SHA512" => new HMACSHA512(key),
            "SHA3-256" when HMACSHA3_256.IsSupported => new HMACSHA3_256(key),
            "SHA3-384" when HMACSHA3_384.IsSupported => new HMACSHA3_384(key),
            "SHA3-512" when HMACSHA3_512.IsSupported => new HMACSHA3_512(key),
            "SHA3-256" or "SHA3-384" or "SHA3-512" => throw new PlatformNotSupportedException($"当前 .NET/系统不支持 HMAC-{algorithm}。"),
            _ => new HMACSHA256(key)
        };

        return hmac.ComputeHash(input);
    }


    private RSAEncryptionPadding GetRsaEncryptionPadding()
    {
        return GetComboText(RsaPaddingComboBox) switch
        {
            "OAEP-SHA1" => RSAEncryptionPadding.OaepSHA1,
            "OAEP-SHA256" => RSAEncryptionPadding.OaepSHA256,
            "OAEP-SHA384" => RSAEncryptionPadding.OaepSHA384,
            "OAEP-SHA512" => RSAEncryptionPadding.OaepSHA512,
            _ => RSAEncryptionPadding.Pkcs1
        };
    }

    private static PaddingMode GetPaddingMode(string padding)
    {
        return padding switch
        {
            "Zeros" => PaddingMode.Zeros,
            "None" => PaddingMode.None,
            _ => PaddingMode.PKCS7
        };
    }

    private static byte[] NormalizeKey(byte[] input, int length)
    {
        if (input.Length == length)
        {
            return input;
        }

        byte[] output = new byte[length];
        Buffer.BlockCopy(input, 0, output, 0, Math.Min(input.Length, length));
        return output;
    }

    private static byte[] DecodeData(string? text, string format)
    {
        text ??= string.Empty;
        return format switch
        {
            "Base64" => Convert.FromBase64String(RemoveWhitespace(text)),
            "Base64URL" => FromBase64Url(text),
            "HEX" => FromHex(text),
            _ => Encoding.UTF8.GetBytes(text)
        };
    }

    private static string EncodeData(byte[] data, string format)
    {
        return format switch
        {
            "Base64" => Convert.ToBase64String(data),
            "Base64URL" => ToBase64Url(data),
            "HEX" => ToHex(data),
            _ => Encoding.UTF8.GetString(data)
        };
    }

    private static string ToHex(byte[] bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static byte[] FromHex(string text)
    {
        string hex = RemoveWhitespace(text).Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (hex.Length % 2 != 0)
        {
            hex = "0" + hex;
        }

        return Convert.FromHexString(hex);
    }

    private static string ToBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] FromBase64Url(string text)
    {
        string base64 = RemoveWhitespace(text).Replace('-', '+').Replace('_', '/');
        base64 = (base64.Length % 4) switch
        {
            0 => base64,
            2 => base64 + "==",
            3 => base64 + "=",
            _ => throw new FormatException("Base64URL 长度无效。")
        };
        return Convert.FromBase64String(base64);
    }

    private static string RemoveWhitespace(string text)
    {
        return string.Concat(text.Where(static character => !char.IsWhiteSpace(character)));
    }

    private static string GetComboText(ComboBox comboBox)
    {
        return (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
    }

    private void RunAction(Action action, string errorTitle)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            ToolSummaryTextBlock.Text = $"{errorTitle}：{exception.Message}";
        }
    }
}

