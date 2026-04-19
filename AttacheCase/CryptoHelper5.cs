//----------------------------------------------------------------------
// SPDX-License-Identifier: GPL-3.0-or-later OR LicenseRef-AttacheCase-Commercial
// Copyright (c) 2016-2026 HiBARA Software, LLC 
// Dual-licensed (GPLv3+ / Commercial). Third-party components remain under their own licenses (see THIRD_PARTY_NOTICES.md).
// デュアルライセンス: GPLv3+ または商用ライセンス（詳細: DUAL-LICENSING.md / COMMERCIAL-LICENSE.md）
//----------------------------------------------------------------------
using System;
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

namespace AttacheCase
{
  /// <summary>
  /// アタッシェケース5用暗号化ヘルパークラス。
  /// Bouncy Castle を使用して、AES-256-GCM / Argon2id / RSA-OAEP / SHA-256 等の
  /// 暗号プリミティブを提供する。
  /// 
  /// FileEncrypt5 / FileDecrypt5 はこのクラスを介して暗号処理を行い、
  /// ファイルフォーマットの読み書きに集中できるようにする。
  /// </summary>
  internal static class CryptoHelper5
  {
    //======================================================================
    // 定数
    //======================================================================

    /// <summary>AES-256-GCM の鍵サイズ（バイト）</summary>
    public const int AES_KEY_SIZE = 32;

    /// <summary>AES-256-GCM の Nonce サイズ（バイト）。GCM の推奨値は 12 バイト (96 bit)</summary>
    public const int AES_GCM_NONCE_SIZE = 12;

    /// <summary>AES-256-GCM の認証タグサイズ（ビット）</summary>
    public const int AES_GCM_TAG_BITS = 128;

    /// <summary>AES-256-GCM の認証タグサイズ（バイト）</summary>
    public const int AES_GCM_TAG_SIZE = AES_GCM_TAG_BITS / 8;

    /// <summary>Argon2id のソルトサイズ（バイト）。NIST SP 800-132 推奨の 16 バイト以上</summary>
    public const int ARGON2_SALT_SIZE = 16;

    /// <summary>Argon2id のデフォルトメモリコスト（KB）。64 MB</summary>
    public const int ARGON2_DEFAULT_MEMORY_KB = 65536;

    /// <summary>Argon2id のデフォルト反復回数</summary>
    public const int ARGON2_DEFAULT_ITERATIONS = 3;

    /// <summary>Argon2id のデフォルト並列度</summary>
    public const int ARGON2_DEFAULT_PARALLELISM = 4;

    /// <summary>SHA-256 ハッシュサイズ（バイト）</summary>
    public const int SHA256_HASH_SIZE = 32;

    /// <summary>フィンガープリントサイズ（バイト）。公開鍵 SHA-256 の先頭 8 バイト</summary>
    public const int FINGERPRINT_SIZE = 8;

    /// <summary>
    /// パスワード由来鍵で共通鍵を保護する際の暗号化データサイズ。
    /// Nonce(12) + 暗号化された共通鍵(32) + GCM認証タグ(16) = 60 バイト
    /// </summary>
    public const int ENCRYPTED_COMMON_KEY_SIZE_PASSWORD = AES_GCM_NONCE_SIZE + AES_KEY_SIZE + AES_GCM_TAG_SIZE;

    /// <summary>Bouncy Castle の SecureRandom インスタンス（スレッドセーフ）</summary>
    private static readonly SecureRandom Random = new SecureRandom();

    //======================================================================
    // 乱数生成
    //======================================================================

    /// <summary>
    /// 暗号学的に安全な乱数バイト列を生成する。
    /// </summary>
    /// <param name="size">生成するバイト数</param>
    /// <returns>ランダムなバイト配列</returns>
    public static byte[] GenerateRandomBytes(int size)
    {
      var bytes = new byte[size];
      Random.NextBytes(bytes);
      return bytes;
    }

    /// <summary>
    /// AES-256 用のランダム共通鍵（32 バイト）を生成する。
    /// この共通鍵はファイル本体の暗号化に使用され、パスワード方式でも公開鍵方式でも
    /// 常にランダム生成される。
    /// </summary>
    /// <returns>32 バイトのランダム共通鍵</returns>
    public static byte[] GenerateCommonKey()
    {
      return GenerateRandomBytes(AES_KEY_SIZE);
    }

    /// <summary>
    /// AES-256-GCM 用の Nonce（12 バイト）を生成する。
    /// ヘッダ暗号化と本体暗号化で別々の Nonce を使用すること。
    /// </summary>
    /// <returns>12 バイトの Nonce</returns>
    public static byte[] GenerateNonce()
    {
      return GenerateRandomBytes(AES_GCM_NONCE_SIZE);
    }

    /// <summary>
    /// Argon2id 用のソルト（16 バイト）を生成する。
    /// </summary>
    /// <returns>16 バイトのソルト</returns>
    public static byte[] GenerateSalt()
    {
      return GenerateRandomBytes(ARGON2_SALT_SIZE);
    }

    //======================================================================
    // Argon2id 鍵導出
    //======================================================================

    /// <summary>
    /// Argon2id を使用してパスワードから AES-256 鍵を導出する。
    /// この鍵はファイル本体の暗号化に直接使われず、ランダム共通鍵を保護するための
    /// 「パスワード由来鍵」として使用される。
    /// </summary>
    /// <param name="password">ユーザーが入力したパスワード（文字列）</param>
    /// <param name="salt">ソルト（16 バイト）</param>
    /// <param name="memoryKb">メモリコスト（KB 単位）</param>
    /// <param name="iterations">反復回数</param>
    /// <param name="parallelism">並列度</param>
    /// <returns>32 バイトのパスワード由来鍵</returns>
    public static byte[] DeriveKeyFromPassword(
      string password, byte[] salt,
      int memoryKb = ARGON2_DEFAULT_MEMORY_KB,
      int iterations = ARGON2_DEFAULT_ITERATIONS,
      int parallelism = ARGON2_DEFAULT_PARALLELISM)
    {
      var passwordBytes = Encoding.UTF8.GetBytes(password);
      try
      {
        return DeriveKeyFromPasswordBinary(passwordBytes, salt, memoryKb, iterations, parallelism);
      }
      finally
      {
        Array.Clear(passwordBytes, 0, passwordBytes.Length);
      }
    }

    /// <summary>
    /// Argon2id を使用してバイナリパスワードから AES-256 鍵を導出する。
    /// </summary>
    /// <param name="passwordBinary">パスワード（バイナリ形式）</param>
    /// <param name="salt">ソルト（16 バイト）</param>
    /// <param name="memoryKb">メモリコスト（KB 単位）</param>
    /// <param name="iterations">反復回数</param>
    /// <param name="parallelism">並列度</param>
    /// <returns>32 バイトのパスワード由来鍵</returns>
    public static byte[] DeriveKeyFromPasswordBinary(
      byte[] passwordBinary, byte[] salt,
      int memoryKb = ARGON2_DEFAULT_MEMORY_KB,
      int iterations = ARGON2_DEFAULT_ITERATIONS,
      int parallelism = ARGON2_DEFAULT_PARALLELISM)
    {
      var builder = new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
        .WithSalt(salt)
        .WithMemoryAsKB(memoryKb)
        .WithIterations(iterations)
        .WithParallelism(parallelism)
        .Build();

      var generator = new Argon2BytesGenerator();
      generator.Init(builder);

      var derivedKey = new byte[AES_KEY_SIZE];
      generator.GenerateBytes(passwordBinary, derivedKey, 0, derivedKey.Length);
      return derivedKey;
    }

    //======================================================================
    // AES-256-GCM 暗号化・復号
    //======================================================================

    /// <summary>
    /// AES-256-GCM でデータを暗号化する。
    /// 戻り値は暗号文と認証タグを連結したバイト配列（暗号文 + タグ 16 バイト）。
    /// Nonce は呼び出し側で管理する（ファイルヘッダに平文で記録）。
    /// </summary>
    /// <param name="plaintext">平文データ</param>
    /// <param name="key">AES-256 鍵（32 バイト）</param>
    /// <param name="nonce">Nonce（12 バイト）</param>
    /// <param name="associatedData">追加認証データ（AAD）。不要なら null</param>
    /// <returns>暗号文 + 認証タグ（16 バイト）の連結バイト配列</returns>
    public static byte[] AesGcmEncrypt(byte[] plaintext, byte[] key, byte[] nonce, byte[] associatedData = null)
    {
      var cipher = new GcmBlockCipher(new AesEngine());
      var parameters = new AeadParameters(new KeyParameter(key), AES_GCM_TAG_BITS, nonce, associatedData);
      cipher.Init(true, parameters);

      var output = new byte[cipher.GetOutputSize(plaintext.Length)];
      var offset = cipher.ProcessBytes(plaintext, 0, plaintext.Length, output, 0);
      cipher.DoFinal(output, offset);

      return output; // ciphertext + tag (16 bytes)
    }

    /// <summary>
    /// AES-256-GCM でデータを復号する。
    /// 入力は暗号文と認証タグの連結バイト配列。
    /// 認証タグの検証に失敗した場合は InvalidCipherTextException がスローされる。
    /// </summary>
    /// <param name="ciphertextWithTag">暗号文 + 認証タグ（16 バイト）</param>
    /// <param name="key">AES-256 鍵（32 バイト）</param>
    /// <param name="nonce">Nonce（12 バイト）</param>
    /// <param name="associatedData">追加認証データ（AAD）。暗号化時と同じ値</param>
    /// <returns>復号された平文データ</returns>
    /// <exception cref="InvalidCipherTextException">認証タグの検証に失敗した場合</exception>
    public static byte[] AesGcmDecrypt(byte[] ciphertextWithTag, byte[] key, byte[] nonce, byte[] associatedData = null)
    {
      var cipher = new GcmBlockCipher(new AesEngine());
      var parameters = new AeadParameters(new KeyParameter(key), AES_GCM_TAG_BITS, nonce, associatedData);
      cipher.Init(false, parameters);

      var output = new byte[cipher.GetOutputSize(ciphertextWithTag.Length)];
      var offset = cipher.ProcessBytes(ciphertextWithTag, 0, ciphertextWithTag.Length, output, 0);
      cipher.DoFinal(output, offset);

      return output;
    }

    /// <summary>
    /// AES-256-GCM でストリームデータを暗号化する。
    /// 大きなファイルの暗号化に使用。データは inputStream から読み込まれ、
    /// 暗号化されたデータが outputStream に書き込まれる。
    /// 最後に認証タグ（16 バイト）が outputStream の末尾に追加される。
    /// </summary>
    /// <param name="inputStream">平文データの入力ストリーム</param>
    /// <param name="outputStream">暗号文データの出力ストリーム</param>
    /// <param name="key">AES-256 鍵（32 バイト）</param>
    /// <param name="nonce">Nonce（12 バイト）</param>
    /// <param name="associatedData">追加認証データ（AAD）。不要なら null</param>
    /// <param name="bufferSize">読み込みバッファサイズ</param>
    /// <param name="progressCallback">
    /// 進捗コールバック。引数は処理済みバイト数。
    /// false を返すとキャンセルされ OperationCanceledException がスローされる。
    /// null なら進捗報告なし。
    /// </param>
    public static void AesGcmEncryptStream(
      Stream inputStream, Stream outputStream,
      byte[] key, byte[] nonce,
      byte[] associatedData = null,
      int bufferSize = 8192,
      Func<long, bool> progressCallback = null)
    {
      var cipher = new GcmBlockCipher(new AesEngine());
      var parameters = new AeadParameters(new KeyParameter(key), AES_GCM_TAG_BITS, nonce, associatedData);
      cipher.Init(true, parameters);

      var inputBuffer = new byte[bufferSize];
      var outputBuffer = new byte[bufferSize + AES_GCM_TAG_SIZE]; // GCM は入力と同サイズ + タグ分の余裕
      long totalProcessed = 0;
      int bytesRead;

      while ((bytesRead = inputStream.Read(inputBuffer, 0, bufferSize)) > 0)
      {
        var outputLen = cipher.ProcessBytes(inputBuffer, 0, bytesRead, outputBuffer, 0);
        if (outputLen > 0)
        {
          outputStream.Write(outputBuffer, 0, outputLen);
        }
        totalProcessed += bytesRead;

        if (progressCallback != null && !progressCallback(totalProcessed))
        {
          throw new OperationCanceledException();
        }
      }

      // DoFinal で残りのデータと認証タグを出力
      var finalOutput = new byte[cipher.GetOutputSize(0)];
      var finalLen = cipher.DoFinal(finalOutput, 0);
      if (finalLen > 0)
      {
        outputStream.Write(finalOutput, 0, finalLen);
      }
    }

    /// <summary>
    /// AES-256-GCM でストリームデータを復号する。
    /// 入力ストリームの末尾 16 バイトは認証タグ。
    /// 認証タグの検証に失敗した場合は InvalidCipherTextException がスローされる。
    /// </summary>
    /// <param name="inputStream">暗号文データの入力ストリーム（末尾に認証タグ 16 バイト含む）</param>
    /// <param name="outputStream">復号された平文データの出力ストリーム</param>
    /// <param name="key">AES-256 鍵（32 バイト）</param>
    /// <param name="nonce">Nonce（12 バイト）</param>
    /// <param name="associatedData">追加認証データ（AAD）。暗号化時と同じ値</param>
    /// <param name="bufferSize">読み込みバッファサイズ</param>
    /// <param name="progressCallback">
    /// 進捗コールバック。引数は処理済みバイト数。
    /// false を返すとキャンセルされ OperationCanceledException がスローされる。
    /// null なら進捗報告なし。
    /// </param>
    /// <exception cref="InvalidCipherTextException">認証タグの検証に失敗した場合</exception>
    public static void AesGcmDecryptStream(
      Stream inputStream, Stream outputStream,
      byte[] key, byte[] nonce,
      byte[] associatedData = null,
      int bufferSize = 8192,
      Func<long, bool> progressCallback = null)
    {
      var cipher = new GcmBlockCipher(new AesEngine());
      var parameters = new AeadParameters(new KeyParameter(key), AES_GCM_TAG_BITS, nonce, associatedData);
      cipher.Init(false, parameters);

      var inputBuffer = new byte[bufferSize];
      var outputBuffer = new byte[bufferSize + AES_GCM_TAG_SIZE];
      long totalProcessed = 0;
      int bytesRead;

      while ((bytesRead = inputStream.Read(inputBuffer, 0, bufferSize)) > 0)
      {
        var outputLen = cipher.ProcessBytes(inputBuffer, 0, bytesRead, outputBuffer, 0);
        if (outputLen > 0)
        {
          outputStream.Write(outputBuffer, 0, outputLen);
        }
        totalProcessed += bytesRead;

        if (progressCallback != null && !progressCallback(totalProcessed))
        {
          throw new OperationCanceledException();
        }
      }

      // DoFinal で認証タグの検証と残りデータの出力
      // 認証失敗時は InvalidCipherTextException がスローされる
      var finalOutput = new byte[cipher.GetOutputSize(0)];
      var finalLen = cipher.DoFinal(finalOutput, 0);
      if (finalLen > 0)
      {
        outputStream.Write(finalOutput, 0, finalLen);
      }
    }

    //======================================================================
    // パスワード由来鍵による共通鍵の保護
    //======================================================================

    /// <summary>
    /// パスワード由来鍵を使用して、ランダム共通鍵を AES-256-GCM で暗号化する。
    /// 戻り値は Nonce(12) + 暗号文(32) + 認証タグ(16) = 60 バイトのバイト配列。
    /// 
    /// パスワード方式の鍵保護ブロックに格納するデータを生成する。
    /// パスワードの正誤判定は、この GCM の認証タグ検証で行える。
    /// </summary>
    /// <param name="commonKey">保護対象のランダム共通鍵（32 バイト）</param>
    /// <param name="passwordDerivedKey">パスワードから Argon2id で導出した鍵（32 バイト）</param>
    /// <returns>Nonce + 暗号文 + 認証タグ（計 60 バイト）</returns>
    public static byte[] ProtectCommonKeyWithPassword(byte[] commonKey, byte[] passwordDerivedKey)
    {
      var nonce = GenerateNonce();
      var encrypted = AesGcmEncrypt(commonKey, passwordDerivedKey, nonce);

      // Nonce(12) + Encrypted(32 + 16) = 60 bytes
      var result = new byte[AES_GCM_NONCE_SIZE + encrypted.Length];
      Buffer.BlockCopy(nonce, 0, result, 0, AES_GCM_NONCE_SIZE);
      Buffer.BlockCopy(encrypted, 0, result, AES_GCM_NONCE_SIZE, encrypted.Length);
      return result;
    }

    /// <summary>
    /// パスワード由来鍵を使用して、暗号化された共通鍵を復号する。
    /// 入力は ProtectCommonKeyWithPassword の戻り値と同じ形式
    /// （Nonce(12) + 暗号文(32) + 認証タグ(16) = 60 バイト）。
    /// 
    /// パスワードが正しくない場合、GCM の認証タグ検証に失敗し
    /// InvalidCipherTextException がスローされる。
    /// </summary>
    /// <param name="protectedData">Nonce + 暗号文 + 認証タグ（計 60 バイト）</param>
    /// <param name="passwordDerivedKey">パスワードから Argon2id で導出した鍵（32 バイト）</param>
    /// <returns>復号されたランダム共通鍵（32 バイト）</returns>
    /// <exception cref="InvalidCipherTextException">パスワードが正しくない場合（認証タグ検証失敗）</exception>
    public static byte[] UnprotectCommonKeyWithPassword(byte[] protectedData, byte[] passwordDerivedKey)
    {
      if (protectedData.Length < AES_GCM_NONCE_SIZE + AES_GCM_TAG_SIZE)
      {
        throw new ArgumentException("鍵保護データが短すぎます。", nameof(protectedData));
      }

      var nonce = new byte[AES_GCM_NONCE_SIZE];
      Buffer.BlockCopy(protectedData, 0, nonce, 0, AES_GCM_NONCE_SIZE);

      var ciphertextWithTag = new byte[protectedData.Length - AES_GCM_NONCE_SIZE];
      Buffer.BlockCopy(protectedData, AES_GCM_NONCE_SIZE, ciphertextWithTag, 0, ciphertextWithTag.Length);

      return AesGcmDecrypt(ciphertextWithTag, passwordDerivedKey, nonce);
    }

    //======================================================================
    // RSA-OAEP による共通鍵の保護
    //======================================================================

    /// <summary>
    /// 受信者の RSA 公開鍵を使用して、ランダム共通鍵を暗号化する。
    /// OAEP-SHA256 パディングを使用。
    /// 
    /// 公開鍵方式の宛先鍵ブロックに格納するデータを生成する。
    /// RSA-2048 の場合、出力は 256 バイト。RSA-4096 の場合は 512 バイト。
    /// </summary>
    /// <param name="commonKey">保護対象のランダム共通鍵（32 バイト）</param>
    /// <param name="rsaPublicKey">受信者の RSA 公開鍵パラメータ</param>
    /// <returns>RSA-OAEP で暗号化された共通鍵</returns>
    public static byte[] ProtectCommonKeyWithRsa(byte[] commonKey, RsaKeyParameters rsaPublicKey)
    {
      var engine = new OaepEncoding(new RsaEngine(), new Sha256Digest(), new Sha256Digest(), null);
      engine.Init(true, rsaPublicKey);
      return engine.ProcessBlock(commonKey, 0, commonKey.Length);
    }

    /// <summary>
    /// 自分の RSA 秘密鍵を使用して、暗号化された共通鍵を復号する。
    /// OAEP-SHA256 パディングを使用。
    /// 
    /// 復号に失敗した場合（鍵が一致しない等）は InvalidCipherTextException がスローされる。
    /// </summary>
    /// <param name="encryptedCommonKey">RSA-OAEP で暗号化された共通鍵</param>
    /// <param name="rsaPrivateKey">自分の RSA 秘密鍵パラメータ</param>
    /// <returns>復号されたランダム共通鍵（32 バイト）</returns>
    /// <exception cref="InvalidCipherTextException">秘密鍵が一致しない場合</exception>
    public static byte[] UnprotectCommonKeyWithRsa(byte[] encryptedCommonKey, RsaKeyParameters rsaPrivateKey)
    {
      var engine = new OaepEncoding(new RsaEngine(), new Sha256Digest(), new Sha256Digest(), null);
      engine.Init(false, rsaPrivateKey);
      return engine.ProcessBlock(encryptedCommonKey, 0, encryptedCommonKey.Length);
    }

    //======================================================================
    // ハイブリッド暗号化（RSA + ML-KEM）
    //======================================================================

    /// <summary>
    /// ハイブリッド方式で共通鍵を保護する。
    /// 1. ML-KEM-768 Encapsulate → (kem_ct, kem_ss)
    /// 2. wrapped = commonKey XOR kem_ss
    /// 3. rsa_ct = RSA-OAEP-SHA256(wrapped)
    /// 出力: rsa_ct || kem_ct
    /// </summary>
    public static byte[] ProtectCommonKeyWithHybrid(
        byte[] commonKey,
        RsaKeyParameters rsaPublicKey,
        MLKemPublicKeyParameters mlkemPublicKey)
    {
      // ML-KEM Encapsulate
      var encapsulator = new MLKemEncapsulator(MLKemParameters.ml_kem_768);
      encapsulator.Init(mlkemPublicKey);
      var kemCt = new byte[encapsulator.EncapsulationLength]; // 1088 bytes
      var kemSs = new byte[encapsulator.SecretLength];         // 32 bytes
      encapsulator.Encapsulate(kemCt, 0, kemCt.Length, kemSs, 0, kemSs.Length);

      // XOR: commonKey ⊕ kem_ss → wrapped (32 bytes)
      var wrapped = new byte[AES_KEY_SIZE];
      for (var i = 0; i < AES_KEY_SIZE; i++)
        wrapped[i] = (byte)(commonKey[i] ^ kemSs[i]);
      SecureClear(kemSs);

      // RSA-OAEP encrypt wrapped
      var rsaCt = ProtectCommonKeyWithRsa(wrapped, rsaPublicKey);
      SecureClear(wrapped);

      // rsa_ct || kem_ct
      var result = new byte[rsaCt.Length + kemCt.Length];
      Buffer.BlockCopy(rsaCt, 0, result, 0, rsaCt.Length);
      Buffer.BlockCopy(kemCt, 0, result, rsaCt.Length, kemCt.Length);
      return result;
    }

    /// <summary>
    /// ハイブリッド方式で保護された共通鍵を復号する。
    /// 入力: rsa_ct || kem_ct
    /// 1. wrapped = RSA-OAEP-SHA256 decrypt(rsa_ct)
    /// 2. kem_ss = ML-KEM-768 Decapsulate(kem_ct)
    /// 3. commonKey = wrapped XOR kem_ss
    /// </summary>
    public static byte[] UnprotectCommonKeyWithHybrid(
        byte[] encData,
        RsaKeyParameters rsaPrivateKey,
        MLKemPrivateKeyParameters mlkemPrivateKey,
        int rsaCipherSize)
    {
      // 分割: rsa_ct | kem_ct
      var rsaCt = new byte[rsaCipherSize];
      var kemCt = new byte[encData.Length - rsaCipherSize];
      Buffer.BlockCopy(encData, 0, rsaCt, 0, rsaCipherSize);
      Buffer.BlockCopy(encData, rsaCipherSize, kemCt, 0, kemCt.Length);

      // RSA-OAEP decrypt → wrapped (32 bytes)
      var wrapped = UnprotectCommonKeyWithRsa(rsaCt, rsaPrivateKey);

      // ML-KEM Decapsulate → kem_ss (32 bytes)
      var decapsulator = new MLKemDecapsulator(MLKemParameters.ml_kem_768);
      decapsulator.Init(mlkemPrivateKey);
      var kemSs = new byte[decapsulator.SecretLength]; // 32 bytes
      decapsulator.Decapsulate(kemCt, 0, kemCt.Length, kemSs, 0, kemSs.Length);

      // commonKey = wrapped XOR kem_ss
      var commonKey = new byte[AES_KEY_SIZE];
      for (var i = 0; i < AES_KEY_SIZE; i++)
        commonKey[i] = (byte)(wrapped[i] ^ kemSs[i]);

      SecureClear(wrapped);
      SecureClear(kemSs);
      return commonKey;
    }

    //======================================================================
    // RSA 鍵ペア生成
    //======================================================================

    /// <summary>
    /// RSA 鍵ペアを生成する。
    /// </summary>
    /// <param name="keySize">鍵サイズ（ビット）。2048 または 4096</param>
    /// <returns>生成された鍵ペア</returns>
    public static AsymmetricCipherKeyPair GenerateRsaKeyPair(int keySize = 2048)
    {
      var generator = new RsaKeyPairGenerator();
      generator.Init(new KeyGenerationParameters(Random, keySize));
      return generator.GenerateKeyPair();
    }

    //======================================================================
    // SHA-256 ハッシュ
    //======================================================================

    /// <summary>
    /// バイト配列の SHA-256 ハッシュを計算する。
    /// </summary>
    /// <param name="data">ハッシュ対象のデータ</param>
    /// <returns>32 バイトの SHA-256 ハッシュ値</returns>
    public static byte[] ComputeSha256(byte[] data)
    {
      var digest = new Sha256Digest();
      digest.BlockUpdate(data, 0, data.Length);
      var hash = new byte[SHA256_HASH_SIZE];
      digest.DoFinal(hash, 0);
      return hash;
    }

    //======================================================================
    // フィンガープリント
    //======================================================================

    /// <summary>
    /// RSA 単体のフィンガープリント（SHA-256(modulus) 先頭 8 バイト）を計算する。
    /// PkaAlgorithmId 0x01, 0x02 用。
    /// </summary>
    public static byte[] ComputeFingerprint(RsaKeyParameters rsaPublicKey)
    {
      var modulus = rsaPublicKey.Modulus.ToByteArrayUnsigned();
      return ComputeFingerprintFromBytes(modulus, null);
    }

    /// <summary>
    /// ハイブリッド方式のフィンガープリント（SHA-256(modulus || mlkemPub) 先頭 8 バイト）を計算する。
    /// PkaAlgorithmId 0x11, 0x12 用。
    /// mlkemPublicKeyBytes が null の場合は RSA 単体として計算する。
    /// </summary>
    public static byte[] ComputeFingerprint(RsaKeyParameters rsaPublicKey, byte[] mlkemPublicKeyBytes)
    {
      var modulus = rsaPublicKey.Modulus.ToByteArrayUnsigned();
      return ComputeFingerprintFromBytes(modulus, mlkemPublicKeyBytes);
    }

    /// <summary>
    /// バイト配列からフィンガープリントを計算する内部メソッド。
    /// RSA 単体: SHA-256(rsaModulus)[0..7]
    /// ハイブリッド: SHA-256(rsaModulus || mlkemPub)[0..7]
    /// </summary>
    public static byte[] ComputeFingerprintFromBytes(byte[] rsaModulusBytes, byte[] mlkemPublicKeyBytes)
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

    /// <summary>
    /// 2 つのフィンガープリントが一致するか比較する。
    /// タイミング攻撃を防ぐため、固定時間で比較する。
    /// </summary>
    /// <param name="a">フィンガープリント A</param>
    /// <param name="b">フィンガープリント B</param>
    /// <returns>一致する場合 true</returns>
    public static bool FingerprintEquals(byte[] a, byte[] b)
    {
      return ConstantTimeEquals(a, b);
    }

    //======================================================================
    // XML RSA鍵変換
    //======================================================================

    /// <summary>
    /// Base64 エンコードされた Modulus / Exponent から BouncyCastle の RsaKeyParameters を生成する。
    /// Post-PPAP キーサーバーから取得した公開鍵の復元に使用。
    /// </summary>
    public static RsaKeyParameters ParseRsaPublicKeyFromBase64(string modulusBase64, string exponentBase64)
    {
      var modulus = new BigInteger(1, Convert.FromBase64String(modulusBase64));
      var exponent = new BigInteger(1, Convert.FromBase64String(exponentBase64));
      return new RsaKeyParameters(false, modulus, exponent);
    }

    /// <summary>
    /// .NET XML形式の RSA 公開鍵文字列を BouncyCastle の RsaKeyParameters に変換する。
    /// </summary>
    public static RsaKeyParameters ParseRsaPublicKeyFromXml(string xml)
    {
      var doc = XDocument.Parse(xml);
      var root = doc.Element("RSAKeyValue");
      var modulus = new BigInteger(1, Convert.FromBase64String(root.Element("Modulus").Value));
      var exponent = new BigInteger(1, Convert.FromBase64String(root.Element("Exponent").Value));
      return new RsaKeyParameters(false, modulus, exponent);
    }

    /// <summary>
    /// .NET XML形式の RSA 秘密鍵文字列を BouncyCastle の RsaPrivateCrtKeyParameters に変換する。
    /// </summary>
    public static RsaPrivateCrtKeyParameters ParseRsaPrivateKeyFromXml(string xml)
    {
      var doc = XDocument.Parse(xml);
      var root = doc.Element("RSAKeyValue");
      var modulus = new BigInteger(1, Convert.FromBase64String(root.Element("Modulus").Value));
      var pubExp = new BigInteger(1, Convert.FromBase64String(root.Element("Exponent").Value));
      var privExp = new BigInteger(1, Convert.FromBase64String(root.Element("D").Value));
      var p = new BigInteger(1, Convert.FromBase64String(root.Element("P").Value));
      var q = new BigInteger(1, Convert.FromBase64String(root.Element("Q").Value));
      var dp = new BigInteger(1, Convert.FromBase64String(root.Element("DP").Value));
      var dq = new BigInteger(1, Convert.FromBase64String(root.Element("DQ").Value));
      var qInv = new BigInteger(1, Convert.FromBase64String(root.Element("InverseQ").Value));
      return new RsaPrivateCrtKeyParameters(modulus, pubExp, privExp, p, q, dp, dq, qInv);
    }

    /// <summary>
    /// RSACryptoServiceProvider から BouncyCastle の RsaPrivateCrtKeyParameters に変換する。
    /// PostPpapManager.LoadPrivateKey() で取得した秘密鍵を FileDecrypt5 に渡すために使用。
    /// </summary>
    public static RsaPrivateCrtKeyParameters ParseRsaPrivateKeyFromCsp(System.Security.Cryptography.RSACryptoServiceProvider rsa)
    {
      var p = rsa.ExportParameters(true);
      var modulus = new BigInteger(1, p.Modulus);
      var pubExp = new BigInteger(1, p.Exponent);
      var privExp = new BigInteger(1, p.D);
      var pParam = new BigInteger(1, p.P);
      var qParam = new BigInteger(1, p.Q);
      var dp = new BigInteger(1, p.DP);
      var dq = new BigInteger(1, p.DQ);
      var qInv = new BigInteger(1, p.InverseQ);
      return new RsaPrivateCrtKeyParameters(modulus, pubExp, privExp, pParam, qParam, dp, dq, qInv);
    }

    //======================================================================
    // ユーティリティ
    //======================================================================

    /// <summary>
    /// 2 つのバイト配列をタイミング攻撃耐性のある方法で比較する。
    /// 配列の長さが異なる場合は false を返す。
    /// </summary>
    /// <param name="a">バイト配列 A</param>
    /// <param name="b">バイト配列 B</param>
    /// <returns>完全に一致する場合 true</returns>
    public static bool ConstantTimeEquals(byte[] a, byte[] b)
    {
      if (a == null || b == null) return false;
      if (a.Length != b.Length) return false;

      var diff = 0;
      for (var i = 0; i < a.Length; i++)
      {
        diff |= a[i] ^ b[i];
      }
      return diff == 0;
    }

    /// <summary>
    /// バイト配列をゼロクリアして機密データをメモリから消去する。
    /// </summary>
    /// <param name="data">消去対象のバイト配列。null の場合は何もしない</param>
    public static void SecureClear(byte[] data)
    {
      if (data != null)
      {
        Array.Clear(data, 0, data.Length);
      }
    }
  }
}
