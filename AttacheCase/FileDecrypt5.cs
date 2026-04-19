//----------------------------------------------------------------------
// SPDX-License-Identifier: GPL-3.0-or-later OR LicenseRef-AttacheCase-Commercial
// Copyright (c) 2016-2026 HiBARA Software, LLC 
// Dual-licensed (GPLv3+ / Commercial). Third-party components remain under their own licenses (see THIRD_PARTY_NOTICES.md).
// デュアルライセンス: GPLv3+ または商用ライセンス（詳細: DUAL-LICENSING.md / COMMERCIAL-LICENSE.md）
//----------------------------------------------------------------------
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Pqc.Crypto.Utilities;

namespace AttacheCase
{
  /// <summary>
  /// アタッシェケース5用復号エンジン。
  /// DATA_FILE_VERSION = 150（AES-256-GCM + Argon2id / Post-PPAP）専用。
  /// 
  /// v140 以前のファイルは FileDecrypt4 で処理する。
  /// 外側の判定ロジックで DATA_FILE_VERSION に応じて振り分けること。
  /// </summary>
  internal class FileDecrypt5
  {
    //======================================================================
    // ファイルインデックスデータ
    //======================================================================

    private struct FileData
    {
      public string FilePath;
      public long FileSize;
      public int FileAttribute;
      public DateTime LastWriteDateTime;
      public DateTime CreationDateTime;
      public byte[] Hash;   // CRC32 (4 bytes)
    }

    //======================================================================
    // ステータスコード
    //======================================================================
    private const int DECRYPT_SUCCEEDED = 2;
    private const int READY_FOR_DECRYPT = 5;
    private const int DECRYPTING = 7;

    //======================================================================
    // エラーコード
    //======================================================================
    private const int USER_CANCELED = -1;
    private const int ERROR_UNEXPECTED = -100;
    private const int NOT_ATC_DATA = -101;
    private const int ATC_BROKEN_DATA = -102;
    private const int NO_DISK_SPACE = -103;
    private const int PASSWORD_TOKEN_NOT_FOUND = -105;
    private const int NOT_CORRECT_HASH_VALUE = -106;
    private const int INVALID_FILE_PATH = -107;
    private const int OS_DENIES_ACCESS = -108;
    private const int DIRECTORY_NOT_FOUND = -110;
    private const int DRIVE_NOT_FOUND = -111;
    private const int FILE_NOT_LOADED = -112;
    private const int FILE_NOT_FOUND = -113;
    private const int PATH_TOO_LONG = -114;
    private const int CRYPTOGRAPHIC_EXCEPTION = -115;
    private const int RSA_KEY_GUID_NOT_MATCH = -116;
    private const int IO_EXCEPTION = -117;

    //======================================================================
    // 上書きオプション
    //======================================================================
    private const int OVERWRITE = 1;
    private const int OVERWRITE_ALL = 2;
    private const int KEEP_NEWER = 3;
    private const int KEEP_NEWER_ALL = 4;
    private const int SKIP = 5;
    private const int SKIP_ALL = 6;

    //======================================================================
    // 定数
    //======================================================================
    private const int BUFFER_SIZE = 4096;
    private const string STRING_TOKEN_NORMAL = "_AttacheCaseData";

    //======================================================================
    // 内部変数
    //======================================================================
    private readonly long _ExeOutSize;
    private long _TotalSize;
    private byte[] _keyProtectionBlockRaw;

    //======================================================================
    // パブリックプロパティ
    //======================================================================

    /// <summary>復号後のファイルリスト</summary>
    public List<string> OutputFileList { get; } = new List<string>();

    /// <summary>上書きオプション（一時）</summary>
    public int TempOverWriteOption { get; set; } = USER_CANCELED;

    /// <summary>SHA-256 ハッシュ不一致時の続行オプション</summary>
    public int TempHashMismatchContinueOption { get; set; } = USER_CANCELED;

    /// <summary>処理中のファイル番号</summary>
    public int NumberOfFiles { get; set; } = 0;

    /// <summary>処理する暗号化ファイルの総数</summary>
    public int TotalNumberOfFiles { get; set; } = 1;

    /// <summary>親フォルダを生成しない</summary>
    public bool fNoParentFolder { get; set; } = false;

    /// <summary>パスワード入力回数制限</summary>
    public char MissTypeLimits { get; } = (char)3;

    /// <summary>タイムスタンプを復号時に合わせる</summary>
    public bool fSameTimeStamp { get; set; } = false;

    /// <summary>サルベージモード: 親フォルダーを一つずつ作成</summary>
    public bool fSalvageToCreateParentFolderOneByOne { get; set; } = false;

    /// <summary>サルベージモード: 同一階層に復号</summary>
    public bool fSalvageIntoSameDirectory { get; set; } = false;

    /// <summary>自己実行形式か</summary>
    public bool fExecutableType { get; } = false;

    /// <summary>ハッシュ値チェックを無視する</summary>
    public bool fSalvageIgnoreHashCheck { get; set; } = false;

    //----------------------------------------------------------------------
    // ヘッダから読み取った情報
    //----------------------------------------------------------------------

    /// <summary>リターンコード</summary>
    public int ReturnCode { get; private set; } = 0;

    /// <summary>エラーが発生したファイルパス</summary>
    public string ErrorFilePath { get; private set; } = "";

    /// <summary>ドライブ名</summary>
    public string DriveName { get; private set; } = "";

    /// <summary>復号対象の総ファイルサイズ</summary>
    public long TotalFileSize { get; private set; } = -1;

    /// <summary>ドライブの空き容量</summary>
    public long AvailableFreeSpace { get; private set; } = -1;

    /// <summary>エラーメッセージ</summary>
    public string ErrorMessage { get; private set; } = "";

    /// <summary>メッセージリスト</summary>
    public ArrayList MessageList { get; private set; }

    /// <summary>トークン文字列</summary>
    public string TokenStr { get; } = "";

    /// <summary>データファイルバージョン</summary>
    public int DataFileVersion { get; } = 0;

    /// <summary>暗号化ヘッダサイズ</summary>
    private readonly int _AtcHeaderSize;

    /// <summary>ATC ファイルパス</summary>
    public string AtcFilePath { get; }

    /// <summary>アプリバージョン</summary>
    public short AppVersion { get; } = 0;

    /// <summary>復号時間文字列</summary>
    public string DecryptionTimeString { get; private set; }

    //----------------------------------------------------------------------
    // v150 ヘッダフィールド
    //----------------------------------------------------------------------

    /// <summary>暗号化モード (0x00=パスワード, 0x01=公開鍵)</summary>
    public byte EncryptionMode { get; } = 0;

    /// <summary>対称暗号アルゴリズム識別子</summary>
    public byte SymmetricAlgorithm { get; } = 0;

    /// <summary>公開鍵暗号アルゴリズム識別子</summary>
    public byte PublicKeyAlgorithm { get; } = 0;

    /// <summary>KDF アルゴリズム識別子</summary>
    public byte KdfAlgorithm { get; } = 0;

    /// <summary>公開鍵方式のデータかどうか</summary>
    public bool fPublicKeyEncryption => EncryptionMode == FileEncrypt5.ENCRYPTION_MODE_PUBLIC_KEY;

    //----------------------------------------------------------------------
    // 公開鍵方式（Post-PPAP）用
    //----------------------------------------------------------------------

    private RsaKeyParameters _rsaPrivateKey;
    private MLKemPrivateKeyParameters _mlkemPrivateKey;

    /// <summary>RSA 秘密鍵を設定する（Bouncy Castle パラメータ）</summary>
    public void SetPrivateKey(RsaKeyParameters privateKey)
    {
      _rsaPrivateKey = privateKey;
    }

    /// <summary>ML-KEM-768 秘密鍵を設定する（ハイブリッド復号用）</summary>
    public void SetMlKemPrivateKey(MLKemPrivateKeyParameters privateKey)
    {
      _mlkemPrivateKey = privateKey;
    }

    //======================================================================
    // コンストラクタ: v150 平文ヘッダの読み取り
    //======================================================================

    public FileDecrypt5(string FilePath)
    {
      AtcFilePath = FilePath;
      int[] AtcTokenByte = { 95, 65, 116, 116, 97, 99, 104, 101, 67, 97, 115, 101, 68, 97, 116, 97 };

      try
      {
        // パス1: トークンスキャン（自己実行形式の判定）
        using (var fs = new FileStream(AtcFilePath, FileMode.Open, FileAccess.Read))
        {
          int b;
          while ((b = fs.ReadByte()) > -1)
          {
            if (b == AtcTokenByte[0])
            {
              var fToken = true;
              for (var i = 1; i < AtcTokenByte.Length; i++)
              {
                if (fs.ReadByte() != AtcTokenByte[i]) { fToken = false; break; }
              }
              if (fToken)
              {
                if (fs.Position > 20) { fExecutableType = true; _ExeOutSize = fs.Position - 20; }
                break;
              }
            }
          }
        }

        // パス2: 平文ヘッダの読み取り
        using (var fs = new FileStream(AtcFilePath, FileMode.Open, FileAccess.Read))
        {
          if (fs.Length < 16) { ReturnCode = NOT_ATC_DATA; ErrorFilePath = AtcFilePath; return; }
          if (fExecutableType) fs.Seek(_ExeOutSize, SeekOrigin.Begin);

          byte[] byteArray;

          // アプリバージョン (short, 2 bytes)
          byteArray = new byte[2]; fs.Read(byteArray, 0, 2);
          AppVersion = BitConverter.ToInt16(byteArray, 0);

          // ミスタイプ回数制限 (1 byte)
          byteArray = new byte[1]; fs.Read(byteArray, 0, 1);
          MissTypeLimits = (char)byteArray[0];

          // 破壊フラグ (1 byte) — v5 では使用しないが読み飛ばす
          fs.ReadByte();

          // トークン文字列 (16 bytes)
          byteArray = new byte[16]; fs.Read(byteArray, 0, 16);
          TokenStr = Encoding.ASCII.GetString(byteArray);

          // DATA_FILE_VERSION (int, 4 bytes)
          byteArray = new byte[4]; fs.Read(byteArray, 0, 4);
          DataFileVersion = BitConverter.ToInt32(byteArray, 0);

          if (DataFileVersion < 150)
          {
            // v150 未満のファイルは FileDecrypt4 で処理すべき
            ReturnCode = NOT_ATC_DATA;
            ErrorMessage = "このファイルは旧バージョン形式です。FileDecrypt4 で処理してください。";
            return;
          }

          // 暗号化モード (1 byte)
          EncryptionMode = (byte)fs.ReadByte();

          // 対称暗号アルゴリズム (1 byte)
          SymmetricAlgorithm = (byte)fs.ReadByte();

          // 公開鍵暗号アルゴリズム (1 byte)
          PublicKeyAlgorithm = (byte)fs.ReadByte();

          // KDF アルゴリズム (1 byte)
          KdfAlgorithm = (byte)fs.ReadByte();

          // 予約領域 (4 bytes)
          fs.Read(new byte[4], 0, 4);

          // 暗号化ヘッダサイズ (int, 4 bytes)
          byteArray = new byte[4]; fs.Read(byteArray, 0, 4);
          _AtcHeaderSize = BitConverter.ToInt32(byteArray, 0);

          // 鍵保護ブロックの読み取り
          _keyProtectionBlockRaw = ReadKeyProtectionBlockRaw(fs);
        }

        ReturnCode = DECRYPT_SUCCEEDED;
      }
      catch (Exception ex) { HandleException(ex); }
    }

    /// <summary>
    /// 鍵保護ブロックの生データを読み取る。
    /// </summary>
    private byte[] ReadKeyProtectionBlockRaw(FileStream fs)
    {
      if (EncryptionMode == FileEncrypt5.ENCRYPTION_MODE_PASSWORD)
      {
        // Salt(16) + MemoryKB(4) + Iterations(4) + Parallelism(4) + EncKeySize(2) + EncKey(S)
        var fixedPart = new byte[16 + 4 + 4 + 4 + 2];
        fs.Read(fixedPart, 0, fixedPart.Length);
        var encKeySize = BitConverter.ToInt16(fixedPart, fixedPart.Length - 2);
        var encKey = new byte[encKeySize];
        fs.Read(encKey, 0, encKeySize);
        var result = new byte[fixedPart.Length + encKeySize];
        Buffer.BlockCopy(fixedPart, 0, result, 0, fixedPart.Length);
        Buffer.BlockCopy(encKey, 0, result, fixedPart.Length, encKeySize);
        return result;
      }
      else
      {
        // 鍵ブロック数 N + (フィンガープリント + サイズ + 暗号化共通鍵) × N
        using var ms = new MemoryStream();
        var nBuf = new byte[2]; fs.Read(nBuf, 0, 2); ms.Write(nBuf, 0, 2);
        var n = BitConverter.ToInt16(nBuf, 0);
        for (var i = 0; i < n; i++)
        {
          var fp = new byte[CryptoHelper5.FINGERPRINT_SIZE];
          fs.Read(fp, 0, fp.Length); ms.Write(fp, 0, fp.Length);
          var sizeBuf = new byte[2]; fs.Read(sizeBuf, 0, 2); ms.Write(sizeBuf, 0, 2);
          var encKeySize = BitConverter.ToInt16(sizeBuf, 0);
          var encKey = new byte[encKeySize];
          fs.Read(encKey, 0, encKeySize); ms.Write(encKey, 0, encKeySize);
        }
        return ms.ToArray();
      }
    }

    //======================================================================
    // メイン復号処理
    //======================================================================

    /// <summary>
    /// v150 フォーマットの暗号化ファイルを復号する。
    /// </summary>
    public bool Decrypt(
      object sender, DoWorkEventArgs e,
      string filePath, string outDirPath, string password, byte[] passwordBinary,
      Action<int, string, IReadOnlyList<string>> dialog)
    {
      var worker = sender as BackgroundWorker;
      bool cancelCheck() => worker?.CancellationPending ?? false;

      worker?.ReportProgress(0, new ArrayList { READY_FOR_DECRYPT, Path.GetFileName(filePath) });
      var swDecrypt = new Stopwatch(); swDecrypt.Start();
      var swProgress = new Stopwatch(); swProgress.Start();

      try
      {
        //--------------------------------------------------------------
        // ① 共通鍵の復元
        //--------------------------------------------------------------
        byte[] commonKey = EncryptionMode == FileEncrypt5.ENCRYPTION_MODE_PASSWORD
          ? RecoverCommonKeyFromPassword(password, passwordBinary)
          : RecoverCommonKeyFromPublicKey();

        if (commonKey == null) return false;

        //--------------------------------------------------------------
        // ② 暗号化ヘッダの復号 → ファイルリスト構築
        //--------------------------------------------------------------
        var FileDataList = new List<FileData>();

        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
          // 平文ヘッダ(24+8) + 暗号化ヘッダサイズ(4) + 鍵保護ブロック の後ろまでシーク
          long headerDataStart = (fExecutableType ? _ExeOutSize : 0)
                                 + 36 + _keyProtectionBlockRaw.Length;
          fs.Seek(headerDataStart, SeekOrigin.Begin);

          // ヘッダ暗号化用 Nonce (12 bytes)
          var headerNonce = new byte[CryptoHelper5.AES_GCM_NONCE_SIZE];
          fs.Read(headerNonce, 0, headerNonce.Length);

          // 暗号化ヘッダ
          var encryptedHeader = new byte[_AtcHeaderSize];
          fs.Read(encryptedHeader, 0, _AtcHeaderSize);

          // 復号
          byte[] decryptedHeader;
          try
          {
            decryptedHeader = CryptoHelper5.AesGcmDecrypt(encryptedHeader, commonKey, headerNonce);
          }
          catch (InvalidCipherTextException)
          {
            CryptoHelper5.SecureClear(commonKey);
            ReturnCode = PASSWORD_TOKEN_NOT_FOUND;
            ErrorFilePath = filePath;
            return false;
          }

          // ヘッダの解析
          if (!ParseDecryptedHeader(decryptedHeader, outDirPath, FileDataList))
          {
            CryptoHelper5.SecureClear(commonKey);
            return false;
          }

          //--------------------------------------------------------------
          // ③ ディスク空き容量チェック
          //--------------------------------------------------------------
          if (!CheckDiskSpace(outDirPath))
          {
            CryptoHelper5.SecureClear(commonKey);
            return false;
          }

          //--------------------------------------------------------------
          // ④ 本体データの復号
          //--------------------------------------------------------------
          var bodyNonce = new byte[CryptoHelper5.AES_GCM_NONCE_SIZE];
          fs.Read(bodyNonce, 0, bodyNonce.Length);

          var encBodyLen = (int)(fs.Length - fs.Position);
          var encryptedBody = new byte[encBodyLen];
          fs.Read(encryptedBody, 0, encBodyLen);

          byte[] compressedBody;
          try
          {
            compressedBody = CryptoHelper5.AesGcmDecrypt(encryptedBody, commonKey, bodyNonce);
          }
          catch (InvalidCipherTextException)
          {
            CryptoHelper5.SecureClear(commonKey);
            ReturnCode = ATC_BROKEN_DATA;
            ErrorFilePath = filePath;
            return false;
          }

          CryptoHelper5.SecureClear(commonKey);

          //--------------------------------------------------------------
          // ⑤ Deflate 解凍 → ファイル展開
          //--------------------------------------------------------------
          using (var compStream = new MemoryStream(compressedBody))
          using (var ds = new DeflateStream(compStream, CompressionMode.Decompress))
          {
            if (!ExtractFiles(ds, FileDataList, outDirPath, cancelCheck, dialog, worker, swProgress, e))
              return false;
          }
        }

        swDecrypt.Stop();
        var ts = swDecrypt.Elapsed;
        DecryptionTimeString = $"{ts.Hours}h {ts.Minutes}m {ts.Seconds}s {ts.Milliseconds}ms";
        ReturnCode = DECRYPT_SUCCEEDED;
        return true;
      }
      catch (Exception ex)
      {
        HandleException(ex);
        return false;
      }
      finally
      {
        swProgress.Stop();
      }
    }

    //======================================================================
    // 共通鍵の復元（パスワード方式）
    //======================================================================

    private byte[] RecoverCommonKeyFromPassword(string password, byte[] passwordBinary)
    {
      var offset = 0;
      var salt = new byte[CryptoHelper5.ARGON2_SALT_SIZE];
      Buffer.BlockCopy(_keyProtectionBlockRaw, offset, salt, 0, salt.Length); offset += salt.Length;

      var memoryKb = BitConverter.ToInt32(_keyProtectionBlockRaw, offset); offset += 4;
      var iterations = BitConverter.ToInt32(_keyProtectionBlockRaw, offset); offset += 4;
      var parallelism = BitConverter.ToInt32(_keyProtectionBlockRaw, offset); offset += 4;
      var encKeySize = BitConverter.ToInt16(_keyProtectionBlockRaw, offset); offset += 2;

      var protectedKey = new byte[encKeySize];
      Buffer.BlockCopy(_keyProtectionBlockRaw, offset, protectedKey, 0, encKeySize);

      var derivedKey = passwordBinary != null
        ? CryptoHelper5.DeriveKeyFromPasswordBinary(passwordBinary, salt, memoryKb, iterations, parallelism)
        : CryptoHelper5.DeriveKeyFromPassword(password, salt, memoryKb, iterations, parallelism);

      try
      {
        var commonKey = CryptoHelper5.UnprotectCommonKeyWithPassword(protectedKey, derivedKey);
        CryptoHelper5.SecureClear(derivedKey);
        return commonKey;
      }
      catch (InvalidCipherTextException)
      {
        CryptoHelper5.SecureClear(derivedKey);
        ReturnCode = PASSWORD_TOKEN_NOT_FOUND;
        ErrorFilePath = AtcFilePath;
        return null;
      }
    }

    //======================================================================
    // 共通鍵の復元（公開鍵方式）
    //======================================================================

    private byte[] RecoverCommonKeyFromPublicKey()
    {
      if (_rsaPrivateKey == null)
      {
        ReturnCode = CRYPTOGRAPHIC_EXCEPTION;
        ErrorMessage = "秘密鍵が設定されていません。";
        return null;
      }

      // フィンガープリント計算（アルゴリズムに応じて）
      bool isHybrid = PublicKeyAlgorithm == PkaAlgorithmId.Rsa2048MlKem768
                   || PublicKeyAlgorithm == PkaAlgorithmId.Rsa4096MlKem768;

      // フィンガープリント計算（アルゴリズムに応じて）
      var myPubParams = new RsaKeyParameters(false, _rsaPrivateKey.Modulus, _rsaPrivateKey.Exponent);
      byte[] myFingerprint;

      if (isHybrid && _mlkemPrivateKey != null)
      {
        // ハイブリッド: modulus || mlkem_pub
        var mlkemPubBytes = _mlkemPrivateKey.GetPublicKey().GetEncoded();
        myFingerprint = CryptoHelper5.ComputeFingerprint(myPubParams, mlkemPubBytes);
      }
      else
      {
        // RSA 単体: modulus のみ
        myFingerprint = CryptoHelper5.ComputeFingerprint(myPubParams);
      }

      var offset = 0;
      var n = BitConverter.ToInt16(_keyProtectionBlockRaw, offset); offset += 2;

      for (var i = 0; i < n; i++)
      {
        var fp = new byte[CryptoHelper5.FINGERPRINT_SIZE];
        Buffer.BlockCopy(_keyProtectionBlockRaw, offset, fp, 0, fp.Length); offset += fp.Length;

        var encKeySize = BitConverter.ToInt16(_keyProtectionBlockRaw, offset); offset += 2;
        var encKey = new byte[encKeySize];
        Buffer.BlockCopy(_keyProtectionBlockRaw, offset, encKey, 0, encKeySize); offset += encKeySize;

        if (CryptoHelper5.FingerprintEquals(myFingerprint, fp))
        {
          try
          {
            if (isHybrid && _mlkemPrivateKey != null)
            {
              // ハイブリッド復号: RSA ciphertext size はアルゴリズムに依存
              var rsaCipherSize = (PublicKeyAlgorithm == PkaAlgorithmId.Rsa4096MlKem768) ? 512 : 256;
              return CryptoHelper5.UnprotectCommonKeyWithHybrid(
                  encKey, _rsaPrivateKey, _mlkemPrivateKey, rsaCipherSize);
            }
            else
            {
              return CryptoHelper5.UnprotectCommonKeyWithRsa(encKey, _rsaPrivateKey);
            }
          }
          catch (InvalidCipherTextException)
          {
            continue;
          }
        }
      }

      ReturnCode = RSA_KEY_GUID_NOT_MATCH;
      ErrorMessage = "このファイルの宛先に、あなたの鍵が含まれていません。";
      return null;
    }

    /// <summary>
    /// 指定した RSA 秘密鍵の公開鍵成分が、このファイルの宛先フィンガープリントと
    /// 一致するかどうかを事前判定する。
    /// </summary>
    /// <summary>
    /// RSA 単体で照合する（後方互換）。
    /// </summary>
    public bool HasMatchingRecipient(RsaKeyParameters rsaPrivateKey)
    {
      return HasMatchingRecipient(rsaPrivateKey, null);
    }

    /// <summary>
    /// 指定した鍵ペアがこのファイルの宛先フィンガープリントと一致するか判定する。
    /// ハイブリッド方式の場合は mlkemPrivateKey も必要。
    /// </summary>
    public bool HasMatchingRecipient(RsaKeyParameters rsaPrivateKey, MLKemPrivateKeyParameters mlkemPrivateKey)
    {
      if (!fPublicKeyEncryption || _keyProtectionBlockRaw == null || rsaPrivateKey == null)
        return false;

      var myPubParams = new RsaKeyParameters(false, rsaPrivateKey.Modulus, rsaPrivateKey.Exponent);

      bool isHybrid = PublicKeyAlgorithm == PkaAlgorithmId.Rsa2048MlKem768
                   || PublicKeyAlgorithm == PkaAlgorithmId.Rsa4096MlKem768;

      byte[] myFingerprint;
      if (isHybrid && mlkemPrivateKey != null)
      {
        var mlkemPubBytes = mlkemPrivateKey.GetPublicKey().GetEncoded();
        myFingerprint = CryptoHelper5.ComputeFingerprint(myPubParams, mlkemPubBytes);
      }
      else
      {
        myFingerprint = CryptoHelper5.ComputeFingerprint(myPubParams);
      }

      System.Diagnostics.Debug.WriteLine(
          $"[HasMatchingRecipient] 自分のFP: {BitConverter.ToString(myFingerprint).Replace("-", "").ToLowerInvariant()}");
      System.Diagnostics.Debug.WriteLine(
          $"[HasMatchingRecipient] PublicKeyAlgorithm: 0x{PublicKeyAlgorithm:X2}, isHybrid: {isHybrid}");

      var offset = 0;
      var n = BitConverter.ToInt16(_keyProtectionBlockRaw, offset); offset += 2;

      for (var i = 0; i < n; i++)
      {
        var fp = new byte[CryptoHelper5.FINGERPRINT_SIZE];
        Buffer.BlockCopy(_keyProtectionBlockRaw, offset, fp, 0, fp.Length); offset += fp.Length;

        System.Diagnostics.Debug.WriteLine(
            $"[HasMatchingRecipient] 宛先[{i}] FP: {BitConverter.ToString(fp).Replace("-", "").ToLowerInvariant()}");

        var encKeySize = BitConverter.ToInt16(_keyProtectionBlockRaw, offset); offset += 2;
        offset += encKeySize;

        if (CryptoHelper5.FingerprintEquals(myFingerprint, fp))
          return true;
      }

      return false;
    }

    //======================================================================
    // ヘッダ解析
    //======================================================================

    private bool ParseDecryptedHeader(
      byte[] decryptedHeader, string OutDirPath, List<FileData> FileDataList)
    {
      using (var ms = new MemoryStream(decryptedHeader))
      {
        TotalFileSize = 0;
        var ParentFolder = "";
        long FileNum = 0;

        while (true)
        {
          var sizeBuf = new byte[2];
          if (ms.Read(sizeBuf, 0, 2) < 2) break;

          var fd = new FileData();
          var nameSize = BitConverter.ToInt16(sizeBuf, 0);
          var nameBuf = new byte[nameSize]; ms.Read(nameBuf, 0, nameSize);
          fd.FilePath = Encoding.UTF8.GetString(nameBuf).Replace("/", "\\");

          // 親フォルダー不要オプション
          if (fNoParentFolder)
          {
            if (FileNum == 0)
              ParentFolder = fd.FilePath;
            else
              fd.FilePath = new StringBuilder(fd.FilePath)
                .Replace(ParentFolder, "", 0, ParentFolder.Length).ToString();
          }

          // 出力パスの構築
          var OutFilePath = Path.IsPathRooted(fd.FilePath)
            ? OutDirPath + fd.FilePath
            : Path.Combine(OutDirPath, fd.FilePath);

          if (!ValidateOutputPath(fd.FilePath, OutDirPath, ref OutFilePath))
            return false;

          fd.FilePath = OutFilePath;

          // FileSize (Int64, 8 bytes)
          var buf8 = new byte[8]; ms.Read(buf8, 0, 8);
          fd.FileSize = BitConverter.ToInt64(buf8, 0);
          TotalFileSize += fd.FileSize;

          // FileAttribute (int, 4 bytes)
          var buf4 = new byte[4]; ms.Read(buf4, 0, 4);
          fd.FileAttribute = BitConverter.ToInt32(buf4, 0);

          // LastWriteDateTime (UTC)
          var tzi = TimeZoneInfo.Local;
          ms.Read(buf4, 0, 4);
          var lwDate = BitConverter.ToInt32(buf4, 0).ToString("0000/00/00");
          ms.Read(buf4, 0, 4);
          lwDate += BitConverter.ToInt32(buf4, 0).ToString(" 00:00:00");
          DateTime.TryParse(lwDate, out fd.LastWriteDateTime);
          fd.LastWriteDateTime = TimeZoneInfo.ConvertTimeFromUtc(fd.LastWriteDateTime, tzi);

          // CreationDateTime (UTC)
          ms.Read(buf4, 0, 4);
          var cdDate = BitConverter.ToInt32(buf4, 0).ToString("0000/00/00");
          ms.Read(buf4, 0, 4);
          cdDate += BitConverter.ToInt32(buf4, 0).ToString(" 00:00:00");
          DateTime.TryParse(cdDate, out fd.CreationDateTime);
          fd.CreationDateTime = TimeZoneInfo.ConvertTimeFromUtc(fd.CreationDateTime, tzi);

          // CRC32 チェックサム（ファイルでサイズ > 0 の場合）
          if (fd.FileSize > 0)
          {
            fd.Hash = new byte[Crc32Utility.CRC32_SIZE];
            ms.Read(fd.Hash, 0, Crc32Utility.CRC32_SIZE);
          }

          FileDataList.Add(fd);
          FileNum++;
        }
      }
      return true;
    }

    //======================================================================
    // ファイル展開
    //======================================================================

    private bool ExtractFiles(
      Stream dataStream, List<FileData> FileDataList, string OutDirPath,
      Func<bool> cancelCheck, Action<int, string, IReadOnlyList<string>> dialog,
      BackgroundWorker worker, Stopwatch swProgress, DoWorkEventArgs e)
    {
      FileStream outfs = null;
      long FileSize = 0;
      var FileIndex = 0;
      var fSkip = false;
      var byteArray = new byte[BUFFER_SIZE];

      if (fNoParentFolder && FileDataList.Count > 0 &&
          (FileDataList[0].FilePath.EndsWith("\\") || FileDataList[0].FilePath.EndsWith("/")))
      {
        FileIndex = 1;
      }

      while (true)
      {
        var len = dataStream.Read(byteArray, 0, BUFFER_SIZE);
        if (len == 0) len = 1; // 末尾の0バイトファイル/フォルダ生成対策
        var buffer_size = len;

        while (len > 0)
        {
          //--------------------------------------------------------------
          // 書き込み先がない場合は作成
          //--------------------------------------------------------------
          if (outfs == null)
          {
            if (FileIndex > FileDataList.Count - 1)
            {
              ReturnCode = DECRYPT_SUCCEEDED;
              return true;
            }

            // ディレクトリ
            if (FileDataList[FileIndex].FilePath.EndsWith("\\") ||
                FileDataList[FileIndex].FilePath.EndsWith("/"))
            {
              var dirPath = FileDataList[FileIndex].FilePath;
              if (!HandleDirectoryOverwrite(dirPath, FileIndex, FileDataList, dialog, ref fSkip))
                return false;
              Directory.CreateDirectory(dirPath);
              OutputFileList.Add(dirPath);
              FileSize = 0; FileIndex++; fSkip = false;
              if (FileIndex > FileDataList.Count - 1) { ReturnCode = DECRYPT_SUCCEEDED; return true; }
              continue;
            }

            // ファイル
            var processPath = FileDataList[FileIndex].FilePath;
            if (!HandleFileOverwrite(ref processPath, FileIndex, FileDataList, dialog, ref fSkip))
              return false;

            var updatedFd = FileDataList[FileIndex];
            updatedFd.FilePath = processPath;
            FileDataList[FileIndex] = updatedFd;

            if (fSalvageToCreateParentFolderOneByOne)
              Directory.CreateDirectory(Path.GetDirectoryName(processPath));

            if (!fSkip)
            {
              try
              {
                outfs = new FileStream(processPath, FileMode.Create, FileAccess.Write);
              }
              catch
              {
                var fi = new FileInfo(processPath);
                if (!fi.Directory.Exists) fi.Directory.Create();
                outfs = new FileStream(processPath, FileMode.Create, FileAccess.Write);
              }
            }

            OutputFileList.Add(processPath);
            FileSize = 0;
          }

          //--------------------------------------------------------------
          // データ書き込み
          //--------------------------------------------------------------
          if (FileSize + len < FileDataList[FileIndex].FileSize)
          {
            if (!fSkip && outfs != null) outfs.Write(byteArray, buffer_size - len, len);
            FileSize += len; _TotalSize += len; len = 0;
          }
          else
          {
            // ファイル境界を超えた
            var rest = (int)(FileDataList[FileIndex].FileSize - FileSize);
            if (!fSkip && outfs != null) outfs.Write(byteArray, buffer_size - len, rest);
            _TotalSize += rest; len -= rest;

            if (outfs != null) { outfs.Close(); outfs = null; }

            // ファイル属性・タイムスタンプ復元、ハッシュ検証
            if (!fSkip)
              RestoreFileAttributes(FileIndex, FileDataList, cancelCheck, dialog);

            FileSize = 0; FileIndex++; fSkip = false;
            if (FileIndex > FileDataList.Count - 1) { ReturnCode = DECRYPT_SUCCEEDED; return true; }
          }

          //--------------------------------------------------------------
          // 進捗報告
          //--------------------------------------------------------------
          if (swProgress.ElapsedMilliseconds > 100)
          {
            var idx = Math.Min(FileIndex, FileDataList.Count - 1);
            var msg = TotalNumberOfFiles > 1
              ? $"{FileDataList[idx].FilePath} ( {NumberOfFiles}/{TotalNumberOfFiles} files )"
              : FileDataList[idx].FilePath;
            this.MessageList = new ArrayList { DECRYPTING, msg };
            var pct = TotalFileSize > 0 ? (float)_TotalSize / TotalFileSize : 0;
            worker?.ReportProgress((int)(pct * 10000), this.MessageList);
            swProgress.Restart();
          }

          if (cancelCheck())
          {
            outfs?.Close();
            if (e != null) e.Cancel = true;
            return false;
          }
        }
      }
    }

    //======================================================================
    // ファイル属性復元・ハッシュ検証
    //======================================================================

    private void RestoreFileAttributes(
      int FileIndex, List<FileData> FileDataList,
      Func<bool> cancelCheck, Action<int, string, IReadOnlyList<string>> dialog)
    {
      var filePath = FileDataList[FileIndex].FilePath;
      var fi = new FileInfo(filePath)
      {
        CreationTime = FileDataList[FileIndex].CreationDateTime,
        LastWriteTime = FileDataList[FileIndex].LastWriteDateTime,
        Attributes = (FileAttributes)FileDataList[FileIndex].FileAttribute
      };

      if (fSalvageIgnoreHashCheck || FileDataList[FileIndex].FileSize <= 0 ||
          FileDataList[FileIndex].Hash == null)
        return;

      var computed = Crc32Utility.ComputeFileCrc32(filePath, cancelCheck);
      if (computed != null && !computed.SequenceEqual(FileDataList[FileIndex].Hash))
      {
        var hashList = new[]
        {
          BitConverter.ToString(FileDataList[FileIndex].Hash).Replace("-", "").ToLower(),
          BitConverter.ToString(computed).Replace("-", "").ToLower()
        };
        dialog?.Invoke(2, filePath, hashList);
        if (TempHashMismatchContinueOption == USER_CANCELED)
        {
          ReturnCode = NOT_CORRECT_HASH_VALUE;
          ErrorFilePath = filePath;
        }
      }
    }

    //======================================================================
    // 上書き処理
    //======================================================================

    private bool HandleDirectoryOverwrite(
      string dirPath, int FileIndex, List<FileData> FileDataList,
      Action<int, string, IReadOnlyList<string>> dialog, ref bool fSkip)
    {
      if (!Directory.Exists(dirPath)) return true;
      var di = new DirectoryInfo(dirPath);
      switch (TempOverWriteOption)
      {
        case OVERWRITE_ALL: break;
        case SKIP_ALL: fSkip = true; break;
        case KEEP_NEWER_ALL:
          if (di.LastWriteTime > FileDataList[FileIndex].LastWriteDateTime) fSkip = true;
          break;
        default:
          dialog?.Invoke(0, dirPath, null);
          switch (TempOverWriteOption)
          {
            case USER_CANCELED: ReturnCode = USER_CANCELED; return false;
            case OVERWRITE: case OVERWRITE_ALL: break;
            case SKIP: fSkip = true; break;
            case SKIP_ALL: fSkip = true; break;
            case KEEP_NEWER: case KEEP_NEWER_ALL:
              if (di.LastWriteTime > FileDataList[FileIndex].LastWriteDateTime) fSkip = true;
              break;
          }
          break;
      }
      if (!fSkip)
      {
        di.Attributes &= ~FileAttributes.Hidden;
        di.Attributes &= ~FileAttributes.ReadOnly;
      }
      return true;
    }

    private bool HandleFileOverwrite(
      ref string processPath, int FileIndex, List<FileData> FileDataList,
      Action<int, string, IReadOnlyList<string>> dialog, ref bool fSkip)
    {
      if (fSalvageIntoSameDirectory)
      {
        var sn = 0;
        while (File.Exists(processPath))
        {
          processPath = GetFileNameWithSerialNumber(processPath, sn);
          sn++;
        }
        return true;
      }
      if (!File.Exists(processPath)) return true;

      switch (TempOverWriteOption)
      {
        case OVERWRITE_ALL: break;
        case SKIP_ALL: fSkip = true; break;
        case KEEP_NEWER_ALL:
          if (new FileInfo(processPath).LastWriteTime > FileDataList[FileIndex].LastWriteDateTime)
            fSkip = true;
          break;
        default:
          dialog?.Invoke(1, processPath, null);
          switch (TempOverWriteOption)
          {
            case USER_CANCELED: ReturnCode = USER_CANCELED; return false;
            case OVERWRITE: case OVERWRITE_ALL: break;
            case SKIP: case SKIP_ALL: fSkip = true; break;
            case KEEP_NEWER: case KEEP_NEWER_ALL:
              if (new FileInfo(processPath).LastWriteTime > FileDataList[FileIndex].LastWriteDateTime)
                fSkip = true;
              break;
          }
          break;
      }
      if (!fSkip) File.SetAttributes(processPath, FileAttributes.Normal);
      return true;
    }

    //======================================================================
    // ディレクトリ・トラバーサル対策
    //======================================================================

    private bool ValidateOutputPath(string filePath, string outDirPath, ref string outFilePath)
    {
      if (filePath.Split(':').Length > 1)
      {
        ReturnCode = INVALID_FILE_PATH;
        ErrorFilePath = outFilePath;
        return false;
      }
      try { outFilePath = Path.GetFullPath(outFilePath); }
      catch
      {
        ReturnCode = INVALID_FILE_PATH;
        ErrorFilePath = outFilePath;
        return false;
      }
      if (!outFilePath.StartsWith(outDirPath))
      {
        ReturnCode = INVALID_FILE_PATH;
        ErrorFilePath = outFilePath;
        return false;
      }
      return true;
    }

    //======================================================================
    // ユーティリティ
    //======================================================================

    private bool CheckDiskSpace(string outDirPath)
    {
      var root = Path.GetPathRoot(outDirPath)?.Substring(0, 1);
      if (root == null || root == "\\") return true;
      var drive = new DriveInfo(root);
      if ((drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Network ||
           drive.DriveType == DriveType.Ram || drive.DriveType == DriveType.Removable) &&
          (!drive.IsReady || drive.AvailableFreeSpace < TotalFileSize))
      {
        ReturnCode = NO_DISK_SPACE;
        DriveName = drive.ToString();
        AvailableFreeSpace = drive.AvailableFreeSpace;
        return false;
      }
      return true;
    }

    private static string GetFileNameWithSerialNumber(string filePath, int serialNum)
    {
      var dir = Path.GetDirectoryName(filePath);
      var name = Path.GetFileNameWithoutExtension(filePath)
                 + serialNum.ToString("0000")
                 + Path.GetExtension(filePath);
      return dir == null ? name : Path.Combine(dir, name);
    }

    private void HandleException(Exception ex)
    {
      switch (ex)
      {
        case UnauthorizedAccessException _:
          ReturnCode = OS_DENIES_ACCESS; ErrorFilePath = AtcFilePath; break;
        case DirectoryNotFoundException _:
          ReturnCode = DIRECTORY_NOT_FOUND; ErrorMessage = ex.Message; break;
        case DriveNotFoundException _:
          ReturnCode = DRIVE_NOT_FOUND; ErrorMessage = ex.Message; break;
        case FileLoadException flEx:
          ReturnCode = FILE_NOT_LOADED; ErrorFilePath = flEx.FileName; break;
        case FileNotFoundException fnfEx:
          ReturnCode = FILE_NOT_FOUND; ErrorFilePath = fnfEx.FileName; break;
        case PathTooLongException _:
          ReturnCode = PATH_TOO_LONG; break;
        case IOException _:
          ReturnCode = IO_EXCEPTION; ErrorMessage = ex.Message; break;
        default:
          if (ReturnCode != USER_CANCELED && ReturnCode != DECRYPT_SUCCEEDED)
          { ReturnCode = ERROR_UNEXPECTED; ErrorMessage = ex.Message; }
          break;
      }
    }

  } // end class FileDecrypt5

} // end namespace AttacheCase
