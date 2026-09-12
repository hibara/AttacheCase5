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
//
// Mac 版（Avalonia）への移植。アルゴリズム・パラメータ・バイト列形式は
// Windows 版 AC5 の CryptoHelper5 と完全に互換。
//----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Kems;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;

namespace AttacheCase.Crypto;

/// <summary>
/// アタッシェケース5 暗号化ヘルパー（Mac 版）。
/// Bouncy Castle を使用して AES-256-GCM / Argon2id / RSA-OAEP / ML-KEM-768
/// 等の暗号プリミティブを提供する。
/// </summary>
public static class CryptoHelper5
{
    // =========================================================
    // 定数（Windows 版 AC5 と完全互換であること）
    // =========================================================
    public const int AES_KEY_SIZE = 32;
    public const int AES_GCM_NONCE_SIZE = 12;
    public const int AES_GCM_TAG_BITS = 128;
    public const int AES_GCM_TAG_SIZE = AES_GCM_TAG_BITS / 8;

    public const int ARGON2_SALT_SIZE = 16;
    public const int ARGON2_DEFAULT_MEMORY_KB = 65536;   // 64 MB
    public const int ARGON2_DEFAULT_ITERATIONS = 3;
    public const int ARGON2_DEFAULT_PARALLELISM = 4;

    // Argon2 パラメータの許容範囲（Windows 版 CryptoHelper5 と同値）。
    // これらはファイルの「認証前」に読む値なので、検証せずに鍵導出へ渡すと、
    // 改ざんされた 12 バイトで巨大なメモリ確保や事実上のフリーズを起こされる。
    public const int ARGON2_MIN_MEMORY_KB = 1024;        // 1 MB
    public const int ARGON2_MAX_MEMORY_KB = 1048576;     // 1 GB
    public const int ARGON2_MIN_ITERATIONS = 1;
    public const int ARGON2_MAX_ITERATIONS = 64;
    public const int ARGON2_MIN_PARALLELISM = 1;
    public const int ARGON2_MAX_PARALLELISM = 64;

    /// <summary>
    /// ファイルから読んだ Argon2 パラメータが許容範囲か検証する。
    /// <b>鍵導出を呼ぶ前に必ず通すこと。</b>範囲外なら破損・改ざんとして扱う。
    /// </summary>
    public static bool IsValidArgon2Parameters(int memoryKb, int iterations, int parallelism)
    {
        if (memoryKb < ARGON2_MIN_MEMORY_KB || memoryKb > ARGON2_MAX_MEMORY_KB) return false;
        if (iterations < ARGON2_MIN_ITERATIONS || iterations > ARGON2_MAX_ITERATIONS) return false;
        if (parallelism < ARGON2_MIN_PARALLELISM || parallelism > ARGON2_MAX_PARALLELISM) return false;
        return true;
    }

    public const int SHA256_HASH_SIZE = 32;
    public const int FINGERPRINT_SIZE = 8;

    public const int ENCRYPTED_COMMON_KEY_SIZE_PASSWORD =
        AES_GCM_NONCE_SIZE + AES_KEY_SIZE + AES_GCM_TAG_SIZE; // 60

    private static readonly SecureRandom Random = new();

    // =========================================================
    // 乱数生成
    // =========================================================
    public static byte[] GenerateRandomBytes(int size)
    {
        var bytes = new byte[size];
        Random.NextBytes(bytes);
        return bytes;
    }

    public static byte[] GenerateCommonKey() => GenerateRandomBytes(AES_KEY_SIZE);
    public static byte[] GenerateNonce() => GenerateRandomBytes(AES_GCM_NONCE_SIZE);
    public static byte[] GenerateSalt() => GenerateRandomBytes(ARGON2_SALT_SIZE);

    // =========================================================
    // Argon2id 鍵導出
    // =========================================================
    public static byte[] DeriveKeyFromPassword(
        string password, byte[] salt,
        int memoryKb = ARGON2_DEFAULT_MEMORY_KB,
        int iterations = ARGON2_DEFAULT_ITERATIONS,
        int parallelism = ARGON2_DEFAULT_PARALLELISM)
    {
        var pwBytes = Encoding.UTF8.GetBytes(password);
        try { return DeriveKeyFromPasswordBinary(pwBytes, salt, memoryKb, iterations, parallelism); }
        finally { SecureClear(pwBytes); }
    }

    public static byte[] DeriveKeyFromPasswordBinary(
        byte[] passwordBinary, byte[] salt,
        int memoryKb = ARGON2_DEFAULT_MEMORY_KB,
        int iterations = ARGON2_DEFAULT_ITERATIONS,
        int parallelism = ARGON2_DEFAULT_PARALLELISM)
    {
        var parameters = new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
            .WithSalt(salt)
            .WithMemoryAsKB(memoryKb)
            .WithIterations(iterations)
            .WithParallelism(parallelism)
            .Build();
        var generator = new Argon2BytesGenerator();
        generator.Init(parameters);
        var derivedKey = new byte[AES_KEY_SIZE];
        generator.GenerateBytes(passwordBinary, derivedKey, 0, derivedKey.Length);
        return derivedKey;
    }

    // =========================================================
    // AES-256-GCM 暗号化・復号（バイト配列単位）
    // =========================================================
    public static byte[] AesGcmEncrypt(byte[] plaintext, byte[] key, byte[] nonce, byte[]? associatedData = null)
    {
        var cipher = new GcmBlockCipher(new AesEngine());
        cipher.Init(true, new AeadParameters(new KeyParameter(key), AES_GCM_TAG_BITS, nonce, associatedData));
        var output = new byte[cipher.GetOutputSize(plaintext.Length)];
        var offset = cipher.ProcessBytes(plaintext, 0, plaintext.Length, output, 0);
        cipher.DoFinal(output, offset);
        return output;
    }

    public static byte[] AesGcmDecrypt(byte[] ciphertextWithTag, byte[] key, byte[] nonce, byte[]? associatedData = null)
    {
        var cipher = new GcmBlockCipher(new AesEngine());
        cipher.Init(false, new AeadParameters(new KeyParameter(key), AES_GCM_TAG_BITS, nonce, associatedData));
        var output = new byte[cipher.GetOutputSize(ciphertextWithTag.Length)];
        var offset = cipher.ProcessBytes(ciphertextWithTag, 0, ciphertextWithTag.Length, output, 0);
        cipher.DoFinal(output, offset);
        return output;
    }

    // =========================================================
    // パスワード由来鍵による共通鍵の保護（60 バイト固定）
    // =========================================================
    public static byte[] ProtectCommonKeyWithPassword(byte[] commonKey, byte[] passwordDerivedKey)
    {
        var nonce = GenerateNonce();
        var encrypted = AesGcmEncrypt(commonKey, passwordDerivedKey, nonce);
        var result = new byte[AES_GCM_NONCE_SIZE + encrypted.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, AES_GCM_NONCE_SIZE);
        Buffer.BlockCopy(encrypted, 0, result, AES_GCM_NONCE_SIZE, encrypted.Length);
        return result;
    }

    public static byte[] UnprotectCommonKeyWithPassword(byte[] protectedData, byte[] passwordDerivedKey)
    {
        if (protectedData.Length < AES_GCM_NONCE_SIZE + AES_GCM_TAG_SIZE)
            throw new ArgumentException("鍵保護データが短すぎます。", nameof(protectedData));

        var nonce = new byte[AES_GCM_NONCE_SIZE];
        Buffer.BlockCopy(protectedData, 0, nonce, 0, AES_GCM_NONCE_SIZE);
        var ciphertextWithTag = new byte[protectedData.Length - AES_GCM_NONCE_SIZE];
        Buffer.BlockCopy(protectedData, AES_GCM_NONCE_SIZE, ciphertextWithTag, 0, ciphertextWithTag.Length);
        return AesGcmDecrypt(ciphertextWithTag, passwordDerivedKey, nonce);
    }

    // =========================================================
    // RSA-OAEP-SHA256 による共通鍵の保護
    // =========================================================
    public static byte[] ProtectCommonKeyWithRsa(byte[] commonKey, RsaKeyParameters rsaPublicKey)
    {
        var engine = new OaepEncoding(new RsaEngine(), new Sha256Digest(), new Sha256Digest(), null);
        engine.Init(true, rsaPublicKey);
        return engine.ProcessBlock(commonKey, 0, commonKey.Length);
    }

    public static byte[] UnprotectCommonKeyWithRsa(byte[] encryptedCommonKey, RsaKeyParameters rsaPrivateKey)
    {
        var engine = new OaepEncoding(new RsaEngine(), new Sha256Digest(), new Sha256Digest(), null);
        engine.Init(false, rsaPrivateKey);
        return engine.ProcessBlock(encryptedCommonKey, 0, encryptedCommonKey.Length);
    }

    // =========================================================
    // ハイブリッド暗号化（RSA + ML-KEM-768）
    // =========================================================
    public static byte[] ProtectCommonKeyWithHybrid(
        byte[] commonKey,
        RsaKeyParameters rsaPublicKey,
        MLKemPublicKeyParameters mlkemPublicKey)
    {
        var encapsulator = new MLKemEncapsulator(MLKemParameters.ml_kem_768);
        encapsulator.Init(mlkemPublicKey);
        var kemCt = new byte[encapsulator.EncapsulationLength]; // 1088
        var kemSs = new byte[encapsulator.SecretLength];        // 32
        encapsulator.Encapsulate(kemCt, 0, kemCt.Length, kemSs, 0, kemSs.Length);

        var wrapped = new byte[AES_KEY_SIZE];
        for (var i = 0; i < AES_KEY_SIZE; i++)
            wrapped[i] = (byte)(commonKey[i] ^ kemSs[i]);
        SecureClear(kemSs);

        var rsaCt = ProtectCommonKeyWithRsa(wrapped, rsaPublicKey);
        SecureClear(wrapped);

        var result = new byte[rsaCt.Length + kemCt.Length];
        Buffer.BlockCopy(rsaCt, 0, result, 0, rsaCt.Length);
        Buffer.BlockCopy(kemCt, 0, result, rsaCt.Length, kemCt.Length);
        return result;
    }

    public static byte[] UnprotectCommonKeyWithHybrid(
        byte[] encData,
        RsaKeyParameters rsaPrivateKey,
        MLKemPrivateKeyParameters mlkemPrivateKey,
        int rsaCipherSize)
    {
        var decapsulator = new MLKemDecapsulator(MLKemParameters.ml_kem_768);
        decapsulator.Init(mlkemPrivateKey);

        // encData は暗号化ファイルから読んだ認証前データ
        // （ParsePublicKeyProtectionBlock は size > 0 しか見ていない）。
        // ハイブリッドの暗号化共通鍵は RSA 暗号文 + ML-KEM 暗号文(1088) の固定長。
        // 分割の前に確定検証し、負の配列長（OverflowException）のような
        // 暗号と無関係な例外型で落とさず、破損として扱う
        var expected = rsaCipherSize + decapsulator.EncapsulationLength;
        if (rsaCipherSize <= 0 || encData.Length != expected)
            throw new InvalidDataException(
                $"Encrypted common key size is invalid. expected={expected}, actual={encData.Length}");

        var rsaCt = new byte[rsaCipherSize];
        var kemCt = new byte[encData.Length - rsaCipherSize];
        Buffer.BlockCopy(encData, 0, rsaCt, 0, rsaCipherSize);
        Buffer.BlockCopy(encData, rsaCipherSize, kemCt, 0, kemCt.Length);

        var wrapped = UnprotectCommonKeyWithRsa(rsaCt, rsaPrivateKey);

        var kemSs = new byte[decapsulator.SecretLength];
        decapsulator.Decapsulate(kemCt, 0, kemCt.Length, kemSs, 0, kemSs.Length);

        var commonKey = new byte[AES_KEY_SIZE];
        for (var i = 0; i < AES_KEY_SIZE; i++)
            commonKey[i] = (byte)(wrapped[i] ^ kemSs[i]);

        SecureClear(wrapped);
        SecureClear(kemSs);
        return commonKey;
    }

    // =========================================================
    // 公開鍵方式の鍵保護ブロック（複数受信者対応）
    //
    // フォーマット:
    //   ReceiverCount (short, 2)
    //   for each receiver:
    //     Fingerprint        (8)
    //     EncCommonKeySize   (short, 2)
    //     EncCommonKey       (可変)
    //
    // RSA 単体 → EncCommonKey = RSA-OAEP(commonKey)
    // ハイブリッド → EncCommonKey = RSA-OAEP(wrapped) || ML-KEM-768 ciphertext
    //                ここで wrapped = commonKey XOR ML-KEM shared secret
    // =========================================================

    /// <summary>公開鍵方式の鍵保護ブロックを構築する際の宛先情報。</summary>
    public sealed class RecipientKeyEntry
    {
        public required byte[] Fingerprint { get; init; }   // 8 bytes
        public required RsaKeyParameters PublicKey { get; init; }
        public MLKemPublicKeyParameters? MlKemPublicKey { get; init; }
        public required byte AlgorithmId { get; init; }
    }

    /// <summary>鍵保護ブロックをパースして得られる宛先ごとの暗号化共通鍵エントリ。</summary>
    public sealed class ParsedRecipientEntry
    {
        public required byte[] Fingerprint { get; init; }       // 8 bytes
        public required byte[] EncryptedCommonKey { get; init; } // 可変
    }

    public static byte[] BuildPublicKeyProtectionBlock(
        byte[] commonKey, IReadOnlyList<RecipientKeyEntry> recipients)
    {
        if (recipients == null || recipients.Count == 0)
            throw new ArgumentException("At least one recipient is required.", nameof(recipients));

        using var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes((short)recipients.Count), 0, 2);

        foreach (var r in recipients)
        {
            if (r.Fingerprint.Length != FINGERPRINT_SIZE)
                throw new ArgumentException(
                    $"Fingerprint must be {FINGERPRINT_SIZE} bytes.", nameof(recipients));

            ms.Write(r.Fingerprint, 0, FINGERPRINT_SIZE);

            byte[] enc;
            var isHybrid = r.AlgorithmId is PkaAlgorithmId.Rsa2048MlKem768
                                          or PkaAlgorithmId.Rsa4096MlKem768;

            if (isHybrid && r.MlKemPublicKey != null)
                enc = ProtectCommonKeyWithHybrid(commonKey, r.PublicKey, r.MlKemPublicKey);
            else
                enc = ProtectCommonKeyWithRsa(commonKey, r.PublicKey);

            ms.Write(BitConverter.GetBytes((short)enc.Length), 0, 2);
            ms.Write(enc, 0, enc.Length);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// 公開鍵方式の鍵保護ブロックをパースする。
    /// </summary>
    /// <param name="block">ブロックを含むバッファ</param>
    /// <param name="offset">ブロックの開始オフセット</param>
    /// <param name="bytesRead">ブロック全体のバイト数</param>
    public static IReadOnlyList<ParsedRecipientEntry> ParsePublicKeyProtectionBlock(
        byte[] block, int offset, out int bytesRead)
    {
        ArgumentNullException.ThrowIfNull(block);
        if (offset < 0 || offset > block.Length)
            throw new InvalidDataException("Public key protection block offset is out of range.");

        var start = offset;

        // block は暗号化ファイルから読んだもので、中身を信用できない。
        // 改変されたファイルで BlockCopy や ToInt16 が例外を投げないよう、
        // 読む前に必ず残りバイト数を確かめる
        void Require(int need)
        {
            if (need < 0 || block.Length - offset < need)
                throw new InvalidDataException("Public key protection block is truncated.");
        }

        Require(2);
        var count = BitConverter.ToInt16(block, offset);
        offset += 2;
        if (count < 1)
            throw new InvalidDataException("Public key protection block has no recipients.");

        var list = new List<ParsedRecipientEntry>(count);
        for (var i = 0; i < count; i++)
        {
            Require(FINGERPRINT_SIZE);
            var fp = new byte[FINGERPRINT_SIZE];
            Buffer.BlockCopy(block, offset, fp, 0, FINGERPRINT_SIZE);
            offset += FINGERPRINT_SIZE;

            Require(2);
            var size = BitConverter.ToInt16(block, offset);
            offset += 2;
            // ToInt16 は符号付きなので、負の値も届きうる
            if (size <= 0)
                throw new InvalidDataException("Encrypted common key size is invalid.");

            Require(size);
            var enc = new byte[size];
            Buffer.BlockCopy(block, offset, enc, 0, size);
            offset += size;

            list.Add(new ParsedRecipientEntry { Fingerprint = fp, EncryptedCommonKey = enc });
        }
        bytesRead = offset - start;
        return list;
    }

    /// <summary>
    /// AlgorithmId から RSA 暗号文サイズを返す。ハイブリッド時の RSA 部 + ML-KEM ciphertext の分離に使う。
    /// </summary>
    public static int GetRsaCipherSize(byte algorithmId) => algorithmId switch
    {
        PkaAlgorithmId.Rsa2048 or PkaAlgorithmId.Rsa2048MlKem768 => 256,
        PkaAlgorithmId.Rsa4096 or PkaAlgorithmId.Rsa4096MlKem768 => 512,
        _ => throw new ArgumentException(
                $"Unsupported public-key algorithm: 0x{algorithmId:X2}", nameof(algorithmId)),
    };

    // =========================================================
    // 鍵ペア生成
    // =========================================================
    public static AsymmetricCipherKeyPair GenerateRsaKeyPair(int keySize = 2048)
    {
        var generator = new RsaKeyPairGenerator();
        generator.Init(new KeyGenerationParameters(Random, keySize));
        return generator.GenerateKeyPair();
    }

    // =========================================================
    // SHA-256 ハッシュ
    // =========================================================
    public static byte[] ComputeSha256(byte[] data)
    {
        var digest = new Sha256Digest();
        digest.BlockUpdate(data, 0, data.Length);
        var hash = new byte[SHA256_HASH_SIZE];
        digest.DoFinal(hash, 0);
        return hash;
    }

    // =========================================================
    // フィンガープリント
    // =========================================================
    public static byte[] ComputeFingerprint(RsaKeyParameters rsaPublicKey)
        => ComputeFingerprintFromBytes(rsaPublicKey.Modulus.ToByteArrayUnsigned(), null);

    public static byte[] ComputeFingerprint(RsaKeyParameters rsaPublicKey, byte[]? mlkemPublicKeyBytes)
        => ComputeFingerprintFromBytes(rsaPublicKey.Modulus.ToByteArrayUnsigned(), mlkemPublicKeyBytes);

    public static byte[] ComputeFingerprintFromBytes(byte[] rsaModulusBytes, byte[]? mlkemPublicKeyBytes)
    {
        byte[] data;
        if (mlkemPublicKeyBytes != null && mlkemPublicKeyBytes.Length > 0)
        {
            data = new byte[rsaModulusBytes.Length + mlkemPublicKeyBytes.Length];
            Buffer.BlockCopy(rsaModulusBytes, 0, data, 0, rsaModulusBytes.Length);
            Buffer.BlockCopy(mlkemPublicKeyBytes, 0, data, rsaModulusBytes.Length, mlkemPublicKeyBytes.Length);
        }
        else
        {
            data = rsaModulusBytes;
        }
        var fullHash = ComputeSha256(data);
        var fingerprint = new byte[FINGERPRINT_SIZE];
        Buffer.BlockCopy(fullHash, 0, fingerprint, 0, FINGERPRINT_SIZE);
        return fingerprint;
    }

    public static bool FingerprintEquals(byte[] a, byte[] b) => ConstantTimeEquals(a, b);

    // =========================================================
    // XML 形式 RSA 鍵パース
    // =========================================================
    public static RsaKeyParameters ParseRsaPublicKeyFromBase64(string modulusBase64, string exponentBase64)
    {
        var modulus = new BigInteger(1, Convert.FromBase64String(modulusBase64));
        var exponent = new BigInteger(1, Convert.FromBase64String(exponentBase64));
        return new RsaKeyParameters(false, modulus, exponent);
    }

    public static RsaKeyParameters ParseRsaPublicKeyFromXml(string xml)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Element("RSAKeyValue") ?? throw new FormatException("RSAKeyValue 要素が見つかりません。");
        var modulus = new BigInteger(1, Convert.FromBase64String(root.Element("Modulus")!.Value));
        var exponent = new BigInteger(1, Convert.FromBase64String(root.Element("Exponent")!.Value));
        return new RsaKeyParameters(false, modulus, exponent);
    }

    public static RsaPrivateCrtKeyParameters ParseRsaPrivateKeyFromXml(string xml)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Element("RSAKeyValue") ?? throw new FormatException("RSAKeyValue 要素が見つかりません。");
        var modulus = new BigInteger(1, Convert.FromBase64String(root.Element("Modulus")!.Value));
        var pubExp = new BigInteger(1, Convert.FromBase64String(root.Element("Exponent")!.Value));
        var privExp = new BigInteger(1, Convert.FromBase64String(root.Element("D")!.Value));
        var p = new BigInteger(1, Convert.FromBase64String(root.Element("P")!.Value));
        var q = new BigInteger(1, Convert.FromBase64String(root.Element("Q")!.Value));
        var dp = new BigInteger(1, Convert.FromBase64String(root.Element("DP")!.Value));
        var dq = new BigInteger(1, Convert.FromBase64String(root.Element("DQ")!.Value));
        var qInv = new BigInteger(1, Convert.FromBase64String(root.Element("InverseQ")!.Value));
        return new RsaPrivateCrtKeyParameters(modulus, pubExp, privExp, p, q, dp, dq, qInv);
    }

    // =========================================================
    // ユーティリティ
    // =========================================================
    public static bool ConstantTimeEquals(byte[]? a, byte[]? b)
    {
        if (a == null || b == null) return false;
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    public static void SecureClear(byte[]? data)
    {
        if (data != null) Array.Clear(data, 0, data.Length);
    }

    public static byte[] HexStringToBytes(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length % 2 != 0)
            throw new ArgumentException(
                "Fingerprint は偶数桁の hex 文字列である必要があります。", nameof(hex));
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }
}
