//----------------------------------------------------------------------
// SPDX-License-Identifier: LicenseRef-AttacheCase-SourceAvailable-1.0
// Copyright (c) 2016-2026 HiBARA Software, LLC
// Released under the AttacheCase Source Available License Version 1.0 (LICENSE.md).
// This is NOT an open source license. Business use requires a separate Commercial,
// Integration or OEM License (COMMERCIAL-LICENSE.md).
// Third-party components remain under their own licenses (THIRD_PARTY_NOTICES.md).
// AttacheCase Source Available License Version 1.0 に基づき公開しています。
// オープンソースライセンスではありません。業務・商用利用には別途
// Commercial / Integration / OEM License が必要です（詳細: LICENSE.md / COMMERCIAL-LICENSE.md）。
//----------------------------------------------------------------------
// RSA 鍵ファイル（.atcpub / .atcpvt、RSAKeyValue の XML）を v150 の公開鍵方式で使う。
//
// 経緯: RSA 鍵ファイルによる暗号化は 5.0 で一度畳み（Post-PPAP へ一本化）、
// 5.5 で「いずれ廃止するにせよ、いまは使えるように」との利用者判断（2026-09-12）で
// 復活させた。方式は旧 Windows 5.0 が残していた FileEncrypt5.AddRecipient と同じ:
//   - ファイル形式は v150（Post-PPAP と同じ鍵保護ブロック）。宛先エントリは
//     RSA 単体（PkaAlgorithmId.Rsa2048 / Rsa4096）で、指紋は RSA 公開鍵の
//     Modulus だけから計算する（ML-KEM 無し）
//   - 鍵ファイルは AttacheCase 4 以来の XML（token / id / type + RSAKeyValue）。
//     暗号化に使うのは Modulus / Exponent だけで、id（GUID）は照合に使わない
//   - 旧 AC4 形式（v140、ヘッダの GUID + 256 バイトの RSA ブロック）は書かない。
//     読む側（DecryptionService の v140 経路）は従来どおり残っている
// 鍵長は 2048 / 4096 ビットだけを受け付ける（識別子が定義されているのがこの 2 つ）。
//----------------------------------------------------------------------

using System;
using System.Xml;
using System.Xml.Linq;
using AttacheCase.Models;
using Org.BouncyCastle.Crypto.Parameters;

namespace AttacheCase.Crypto;

public static class RsaKeyFile
{
    /// <summary>鍵長が 2048 / 4096 ビット以外だったときに投げる。</summary>
    public sealed class UnsupportedKeySizeException : NotSupportedException
    {
        public int Bits { get; }

        public UnsupportedKeySizeException(int bits)
            : base($"unsupported RSA key size: {bits} bits (2048 or 4096 only)")
        {
            Bits = bits;
        }
    }

    /// <summary>
    /// 公開鍵 XML（.atcpub の中身）から、暗号化の宛先（RSA 単体）を作る。
    /// </summary>
    /// <exception cref="FormatException">XML として読めない、RSAKeyValue / Modulus / Exponent が無い</exception>
    /// <exception cref="UnsupportedKeySizeException">鍵長が 2048 / 4096 ビット以外</exception>
    public static PostPpapKeyInfo CreateRecipient(string publicKeyXml)
    {
        if (string.IsNullOrWhiteSpace(publicKeyXml))
            throw new FormatException("public key XML is empty");

        string modulus;
        string exponent;
        RsaKeyParameters publicKey;
        try
        {
            var root = XDocument.Parse(publicKeyXml).Element("RSAKeyValue")
                       ?? throw new FormatException("RSAKeyValue element not found");
            modulus = root.Element("Modulus")?.Value.Trim()
                      ?? throw new FormatException("Modulus element not found");
            exponent = root.Element("Exponent")?.Value.Trim()
                       ?? throw new FormatException("Exponent element not found");
            publicKey = CryptoHelper5.ParseRsaPublicKeyFromBase64(modulus, exponent);
        }
        catch (XmlException ex)
        {
            throw new FormatException(ex.Message, ex);
        }

        var algorithmId = AlgorithmIdForKey(publicKey);
        var fingerprint = CryptoHelper5.ComputeFingerprint(publicKey);

        return new PostPpapKeyInfo
        {
            Email = string.Empty,
            Modulus = modulus,
            Exponent = exponent,
            Fingerprint = Convert.ToHexString(fingerprint),
            AlgorithmId = algorithmId,
            MlKemPublicKey = null,
        };
    }

    /// <summary>
    /// 秘密鍵 XML（.atcpvt の中身）を読む。
    /// </summary>
    /// <exception cref="FormatException">XML として読めない、必要な要素が無い</exception>
    public static RsaPrivateCrtKeyParameters ParsePrivateKey(string privateKeyXml)
    {
        if (string.IsNullOrWhiteSpace(privateKeyXml))
            throw new FormatException("private key XML is empty");
        try
        {
            return CryptoHelper5.ParseRsaPrivateKeyFromXml(privateKeyXml);
        }
        catch (Exception ex) when (ex is XmlException or NullReferenceException)
        {
            throw new FormatException(ex.Message, ex);
        }
    }

    /// <summary>
    /// 秘密鍵から、v150 の宛先エントリと突き合わせる指紋を計算する。
    /// 指紋は公開鍵部分（Modulus）だけから作るので、暗号化側の
    /// <see cref="CreateRecipient"/> と同じ値になる。
    /// </summary>
    public static byte[] ComputeFingerprint(RsaPrivateCrtKeyParameters privateKey)
    {
        var publicPart = new RsaKeyParameters(false, privateKey.Modulus, privateKey.PublicExponent);
        return CryptoHelper5.ComputeFingerprint(publicPart);
    }

    /// <summary>鍵長からアルゴリズム識別子を決める。2048 / 4096 以外は拒否。</summary>
    public static byte AlgorithmIdForKey(RsaKeyParameters publicKey)
    {
        var bits = publicKey.Modulus.BitLength;
        return bits switch
        {
            2048 => PkaAlgorithmId.Rsa2048,
            4096 => PkaAlgorithmId.Rsa4096,
            _ => throw new UnsupportedKeySizeException(bits),
        };
    }
}
