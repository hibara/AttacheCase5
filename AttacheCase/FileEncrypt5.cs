//----------------------------------------------------------------------
// SPDX-License-Identifier: GPL-3.0-or-later OR LicenseRef-AttacheCase-Commercial
// Copyright (c) 2016-2026 HiBARA Software, LLC 
// Dual-licensed (GPLv3+ / Commercial). Third-party components remain under their own licenses (see THIRD_PARTY_NOTICES.md).
// デュアルライセンス: GPLv3+ または商用ライセンス（詳細: DUAL-LICENSING.md / COMMERCIAL-LICENSE.md）
//----------------------------------------------------------------------
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Pqc.Crypto.Utilities;
using Path = System.IO.Path;

namespace AttacheCase
{
  internal partial class FileEncrypt5
  {
    //======================================================================
    // ステータスコード
    //======================================================================
    private const int ENCRYPT_SUCCEEDED = 1;
    private const int DECRYPT_SUCCEEDED = 2;
    private const int DELETE_SUCCEEDED = 3;
    private const int READY_FOR_ENCRYPT = 4;
    private const int READY_FOR_DECRYPT = 5;
    private const int ENCRYPTING = 6;
    private const int DECRYPTING = 7;
    private const int DELETING = 8;

    //======================================================================
    // エラーコード
    //======================================================================
    private const int USER_CANCELED = -1;
    private const int ERROR_UNEXPECTED = -100;
    private const int NOT_ATC_DATA = -101;
    private const int ATC_BROKEN_DATA = -102;
    private const int NO_DISK_SPACE = -103;
    private const int FILE_INDEX_NOT_FOUND = -104;
    private const int PASSWORD_TOKEN_NOT_FOUND = -105;
    private const int NOT_CORRECT_HASH_VALUE = -106;
    private const int INVALID_FILE_PATH = -107;
    private const int OS_DENIES_ACCESS = -108;
    private const int DATA_NOT_FOUND = -109;
    private const int DIRECTORY_NOT_FOUND = -110;
    private const int DRIVE_NOT_FOUND = -111;
    private const int FILE_NOT_LOADED = -112;
    private const int FILE_NOT_FOUND = -113;
    private const int PATH_TOO_LONG = -114;
    private const int CRYPTOGRAPHIC_EXCEPTION = -115;
    private const int RSA_KEY_GUID_NOT_MATCH = -116;
    private const int IO_EXCEPTION = -117;

    //======================================================================
    // ファイルフォーマット定数
    //======================================================================
    private const int BUFFER_SIZE = 8192;

    private const string STRING_TOKEN_NORMAL = "_AttacheCaseData";
    private const int DATA_FILE_VERSION = 150;

    /// <summary>暗号化モード: パスワード方式</summary>
    public const byte ENCRYPTION_MODE_PASSWORD = 0x00;
    /// <summary>暗号化モード: 公開鍵方式（Post-PPAP）</summary>
    public const byte ENCRYPTION_MODE_PUBLIC_KEY = 0x01;

    /// <summary>対称暗号: AES-256-GCM</summary>
    public const byte SYMMETRIC_ALGORITHM_AES256GCM = 0x01;

    // 公開鍵暗号アルゴリズム識別子は PkaAlgorithmId クラスで定義
    // 後方互換エイリアス
    public const byte PUBKEY_ALGORITHM_NONE = PkaAlgorithmId.None;
    public const byte PUBKEY_ALGORITHM_RSA2048 = PkaAlgorithmId.Rsa2048;
    public const byte PUBKEY_ALGORITHM_RSA4096 = PkaAlgorithmId.Rsa4096;

    /// <summary>KDF: なし（公開鍵方式）</summary>
    public const byte KDF_ALGORITHM_NONE = 0x00;
    /// <summary>KDF: Argon2id</summary>
    public const byte KDF_ALGORITHM_ARGON2ID = 0x01;

    //======================================================================
    // 内部変数
    //======================================================================
    private byte[] buffer;
    private int _AtcHeaderSize = 0;
    private long _TotalSize = 0;
    private long _StartPos = 0;
    private long _processedSize;
    private volatile bool _isCancelled;
    private ConcurrentBag<FileSystemEntry> resultsConcurrentBag;
    private volatile int _processedFileCount;
    private readonly Stopwatch swProgress = new Stopwatch();

    //======================================================================
    // パブリックプロパティ
    //======================================================================

    /// <summary>処理中のファイル番号</summary>
    public int NumberOfFiles { get; set; } = 0;

    /// <summary>暗号化対象ファイルの総数</summary>
    public int TotalNumberOfFiles { get; set; } = 1;

    /// <summary>パスワード入力回数制限</summary>
    public char MissTypeLimits { get; set; } = (char)3;

    /// <summary>自己実行形式ファイル</summary>
    public bool fExecutable { get; set; } = false;

    /// <summary>自己実行形式の .NET バージョン</summary>
    public string ExeToolVersionString { get; set; } = "4.6.2";

    /// <summary>タイムスタンプを保持するか</summary>
    public bool fKeepTimeStamp { get; set; } = false;

    /// <summary>圧縮オプション (0: Optimal, 1: Fastest, 2: NoCompression)</summary>
    public int CompressionOption { get; set; } = 1;

    /// <summary>出力 ATC ファイルパス</summary>
    public string AtcFilePath { get; private set; } = "";

    /// <summary>暗号化対象ファイルリスト</summary>
    public List<string> FileList { get; private set; }

    /// <summary>暗号化処理時間文字列</summary>
    public string EncryptionTimeString { get; private set; }

    /// <summary>リターンコード</summary>
    public int ReturnCode { get; private set; } = -1;

    /// <summary>エラーが発生したファイルパス</summary>
    public string ErrorFilePath { get; private set; } = "";

    /// <summary>ドライブ名</summary>
    public string DriveName { get; private set; } = "";

    /// <summary>暗号化対象の総ファイルサイズ</summary>
    public long TotalFileSize { get; private set; } = 0;

    /// <summary>ドライブの空き容量</summary>
    public long AvailableFreeSpace { get; private set; } = -1;

    /// <summary>例外エラーメッセージ</summary>
    public string ErrorMessage { get; private set; } = "";

    //----------------------------------------------------------------------
    // 公開鍵方式（Post-PPAP）用プロパティ
    //----------------------------------------------------------------------

    /// <summary>
    /// 公開鍵方式で暗号化するかどうか。
    /// RecipientKeys にエントリが追加されると自動的に true になる。
    /// </summary>
    public bool fPublicKeyEncryption { get; private set; } = false;

    /// <summary>
    /// 公開鍵暗号アルゴリズム識別子。公開鍵方式時に使用。
    /// デフォルトは RSA-4096 + ML-KEM-768 ハイブリッド。
    /// </summary>
    public byte PublicKeyAlgorithm { get; set; } = PostPpapManager.DefaultAlgorithmId;

    /// <summary>
    /// 公開鍵方式の宛先リスト。
    /// フィンガープリント（8 バイト）と RSA 公開鍵のペア。
    /// 1 人のユーザーが複数デバイスを持つ場合、同一人物でも複数エントリになる。
    /// </summary>
    public List<RecipientKeyEntry> RecipientKeys { get; } = new List<RecipientKeyEntry>();

    /// <summary>
    /// 宛先鍵エントリ。公開鍵方式の鍵保護ブロックで使用。
    /// </summary>
    public class RecipientKeyEntry
    {
      /// <summary>フィンガープリント（公開鍵 SHA-256 の先頭 8 バイト）</summary>
      public byte[] Fingerprint { get; set; }
      /// <summary>RSA 公開鍵パラメータ</summary>
      public RsaKeyParameters PublicKey { get; set; }
      /// <summary>ML-KEM-768 公開鍵パラメータ（ハイブリッド時のみ）</summary>
      public MLKemPublicKeyParameters MlKemPublicKey { get; set; }
      /// <summary>アルゴリズム識別子</summary>
      public byte AlgorithmId { get; set; }
    }

    /// <summary>
    /// RSA 単体の宛先を追加する（従来のRSA鍵交換用）。
    /// </summary>
    public void AddRecipient(RsaKeyParameters publicKey)
    {
      RecipientKeys.Add(new RecipientKeyEntry
      {
        Fingerprint = CryptoHelper5.ComputeFingerprint(publicKey),
        PublicKey = publicKey,
        AlgorithmId = PkaAlgorithmId.Rsa4096
      });
      fPublicKeyEncryption = true;
    }

    /// <summary>
    /// ハイブリッド方式（RSA + ML-KEM）の宛先を追加する。
    /// </summary>
    public void AddRecipient(RsaKeyParameters rsaPublicKey, MLKemPublicKeyParameters mlkemPublicKey, byte algorithmId)
    {
      var mlkemPubBytes = mlkemPublicKey.GetEncoded();
      RecipientKeys.Add(new RecipientKeyEntry
      {
        Fingerprint = CryptoHelper5.ComputeFingerprint(rsaPublicKey, mlkemPubBytes),
        PublicKey = rsaPublicKey,
        MlKemPublicKey = mlkemPublicKey,
        AlgorithmId = algorithmId
      });
      PublicKeyAlgorithm = algorithmId;
      fPublicKeyEncryption = true;
    }

    //======================================================================
    // コンストラクタ
    //======================================================================

    public FileEncrypt5()
    {
    }

    public void CancelEncryption()
    {
      _isCancelled = true;
    }

    //======================================================================
    // メイン暗号化処理
    //======================================================================

    /// <summary>
    /// 指定されたファイルを v5 フォーマット（DATA_FILE_VERSION = 150）で暗号化する。
    /// パスワード方式と公開鍵方式の両方に対応。
    /// 
    /// 共通鍵は常にランダム生成され、パスワード方式ではパスワード由来鍵で、
    /// 公開鍵方式では受信者の公開鍵で保護される。
    /// ファイル本体の暗号化処理はどちらの方式でも完全に同一。
    /// </summary>
    public bool Encrypt(
      object sender, DoWorkEventArgs e,
      string[] FilePaths, string OutFilePath, string Password, byte[] PasswordBinary,
      string NewArchiveName, CompressionLevel compressionLevel)
    {
      var swEncrypt = new Stopwatch();
      swEncrypt.Start();
      var swProg = new Stopwatch();
      swProg.Start();

      AtcFilePath = OutFilePath;
      var worker = sender as BackgroundWorker;

      if (ShouldCancel(worker))
      {
        e.Cancel = true;
        return false;
      }

      // タイムスタンプ保持用
      var dtCreate = File.GetCreationTime(FilePaths[0]);
      var dtUpdate = File.GetLastWriteTime(FilePaths[0]);
      var dtAccess = File.GetLastAccessTime(FilePaths[0]);

      // 進捗報告: 準備中
      var MessageList = new ArrayList { READY_FOR_ENCRYPT, Path.GetFileName(AtcFilePath) };
      worker?.ReportProgress(0, MessageList);
      FileList = new List<string>();

      //------------------------------------------------------------------
      // ① ランダム共通鍵の生成
      //    パスワード方式でも公開鍵方式でも、この共通鍵でファイル本体を暗号化する
      //------------------------------------------------------------------
      var commonKey = CryptoHelper5.GenerateCommonKey();

      //------------------------------------------------------------------
      // ② 鍵保護ブロックのデータを準備
      //------------------------------------------------------------------
      byte encryptionMode;
      byte pubkeyAlgorithm;
      byte kdfAlgorithm;
      byte[] keyProtectionBlockData;

      if (fPublicKeyEncryption && RecipientKeys.Count > 0)
      {
        // --- 公開鍵方式 ---
        encryptionMode = ENCRYPTION_MODE_PUBLIC_KEY;
        pubkeyAlgorithm = PublicKeyAlgorithm;
        kdfAlgorithm = KDF_ALGORITHM_NONE;
        keyProtectionBlockData = BuildPublicKeyProtectionBlock(commonKey);
      }
      else
      {
        // --- パスワード方式 ---
        encryptionMode = ENCRYPTION_MODE_PASSWORD;
        pubkeyAlgorithm = PUBKEY_ALGORITHM_NONE;
        kdfAlgorithm = KDF_ALGORITHM_ARGON2ID;
        keyProtectionBlockData = BuildPasswordProtectionBlock(commonKey, Password, PasswordBinary);
      }

      try
      {
        //------------------------------------------------------------------
        // ③ ファイルリスト（暗号化ヘッダ）の構築
        //------------------------------------------------------------------
        var msHeader = new MemoryStream();

        // 「名前をつけて保存」の場合のアーカイブ名ディレクトリ
        if (!string.IsNullOrEmpty(NewArchiveName))
        {
          if (!NewArchiveName.EndsWith("\\"))
          {
            NewArchiveName += "\\";
          }
          WriteArchiveDirectoryEntry(msHeader, NewArchiveName);
        }

        // ファイル/ディレクトリのインデックスを構築
        foreach (var FilePath in FilePaths)
        {
          var ParentPath = Path.GetDirectoryName(FilePath) ?? FilePath;
          if (!ParentPath.EndsWith("\\"))
          {
            ParentPath += "\\";
          }

          if (ShouldCancel(worker))
          {
            e.Cancel = true;
            CryptoHelper5.SecureClear(commonKey);
            return false;
          }

          if (File.Exists(FilePath))
          {
            // 単一ファイル
            var entry = GetFileInfo(ParentPath, FilePath, () => ShouldCancel(worker));
            if (ShouldCancel(worker))
            {
              e.Cancel = true;
              CryptoHelper5.SecureClear(commonKey);
              return false;
            }
            TotalFileSize += FileInfoStreamWriter(ref msHeader, entry, NewArchiveName);
            FileList.Add(entry.FullPath);
          }
          else
          {
            // ディレクトリ
            var entryList = GetFileList(ParentPath, FilePath, () => ShouldCancel(worker), worker);
            foreach (var entry in entryList)
            {
              if (ShouldCancel(worker))
              {
                e.Cancel = true;
                CryptoHelper5.SecureClear(commonKey);
                return false;
              }
              TotalFileSize += FileInfoStreamWriter(ref msHeader, entry, NewArchiveName);
              FileList.Add(entry.FullPath);
            }
          }
        }

        //------------------------------------------------------------------
        // ④ ディスク空き容量チェック
        //------------------------------------------------------------------
        if (!CheckDiskSpace(AtcFilePath, TotalFileSize))
        {
          CryptoHelper5.SecureClear(commonKey);
          return false;
        }

        //------------------------------------------------------------------
        // ⑤ ファイルへの書き込み開始
        //------------------------------------------------------------------
        using (var outfs = new FileStream(AtcFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
          // --- 自己実行形式 ---
          if (fExecutable)
          {
            WriteExeData(outfs);
          }

          _StartPos = outfs.Position;

          // --- 平文ヘッダ ---
          WritePlainHeader(outfs, encryptionMode, pubkeyAlgorithm, kdfAlgorithm);

          // 暗号化ヘッダサイズのプレースホルダ位置を記録
          var headerSizePosition = outfs.Position;
          outfs.Write(BitConverter.GetBytes((int)0), 0, 4); // 後で書き戻す

          // --- 鍵保護ブロック ---
          outfs.Write(keyProtectionBlockData, 0, keyProtectionBlockData.Length);

          // --- ヘッダ暗号化用 Nonce ---
          var headerNonce = CryptoHelper5.GenerateNonce();
          outfs.Write(headerNonce, 0, CryptoHelper5.AES_GCM_NONCE_SIZE);

          // --- 暗号化ヘッダの書き込み ---
          msHeader.Position = 0;
          var headerPlainBytes = msHeader.ToArray();
          var encryptedHeader = CryptoHelper5.AesGcmEncrypt(headerPlainBytes, commonKey, headerNonce);
          _AtcHeaderSize = encryptedHeader.Length;
          outfs.Write(encryptedHeader, 0, encryptedHeader.Length);

          // 暗号化ヘッダサイズを書き戻す
          var currentPos = outfs.Position;
          outfs.Seek(headerSizePosition, SeekOrigin.Begin);
          outfs.Write(BitConverter.GetBytes(_AtcHeaderSize), 0, 4);
          outfs.Seek(currentPos, SeekOrigin.Begin);

          // --- 本体暗号化用 Nonce ---
          var bodyNonce = CryptoHelper5.GenerateNonce();
          outfs.Write(bodyNonce, 0, CryptoHelper5.AES_GCM_NONCE_SIZE);

          // --- 暗号化本体の書き込み ---
          //     DeflateStream → AES-256-GCM の順でストリーム処理
          using (var bodyPlainStream = new MemoryStream())
          {
            // まず圧縮してメモリに格納（GCM はストリーム全体を処理する必要があるため）
            using (var ds = new DeflateStream(bodyPlainStream, compressionLevel, leaveOpen: true))
            {
              foreach (var path in FileList)
              {
                if (File.Exists(path) && !Directory.Exists(path))
                {
                  buffer = new byte[BUFFER_SIZE];
                  using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                  {
                    int len;
                    while ((len = fs.Read(buffer, 0, BUFFER_SIZE)) > 0)
                    {
                      ds.Write(buffer, 0, len);
                      _TotalSize += len;
                      Interlocked.Add(ref _processedSize, len);

                      // 進捗報告
                      if (swProg.ElapsedMilliseconds > 100)
                      {
                        var MessageText = TotalNumberOfFiles > 1
                          ? $"{path} ( {NumberOfFiles} / {TotalNumberOfFiles} files )"
                          : path;

                        MessageList = new ArrayList { ENCRYPTING, MessageText };
                        var percent = (float)_processedSize / TotalFileSize;
                        worker?.ReportProgress((int)(percent * 10000), MessageList);
                        swProg.Restart();

                        if (ShouldCancel(worker))
                        {
                          e.Cancel = true;
                          CryptoHelper5.SecureClear(commonKey);
                          return false;
                        }
                      }
                    }
                  }
                }
              }
            } // DeflateStream 閉じ → 圧縮完了

            // 圧縮データを AES-256-GCM で暗号化して書き込み
            bodyPlainStream.Position = 0;
            CryptoHelper5.AesGcmEncryptStream(
              bodyPlainStream, outfs, commonKey, bodyNonce,
              progressCallback: processedBytes =>
              {
                if (ShouldCancel(worker))
                  return false;
                return true;
              });
          }
        } // outfs 閉じ

        // --- 共通鍵をメモリから消去 ---
        CryptoHelper5.SecureClear(commonKey);

        // --- タイムスタンプの設定 ---
        if (fKeepTimeStamp)
        {
          File.SetCreationTime(AtcFilePath, dtCreate);
          File.SetLastWriteTime(AtcFilePath, dtUpdate);
          File.SetLastAccessTime(AtcFilePath, dtAccess);
        }
        else
        {
          File.SetLastWriteTime(AtcFilePath, DateTime.Now);
        }

        swEncrypt.Stop();
        var ts = swEncrypt.Elapsed;
        EncryptionTimeString = $"{ts.Hours}h {ts.Minutes}m {ts.Seconds}s {ts.Milliseconds}ms";

        ReturnCode = ENCRYPT_SUCCEEDED;
        return true;
      }
      catch (OperationCanceledException)
      {
        CryptoHelper5.SecureClear(commonKey);
        e.Cancel = true;
        return false;
      }
      catch (Exception ex)
      {
        CryptoHelper5.SecureClear(commonKey);
        HandleEncryptionException(ex);
        return false;
      }
    }

    //======================================================================
    // 平文ヘッダの書き込み
    //======================================================================

    /// <summary>
    /// v5 の平文ヘッダを書き込む。
    /// 既存 v4 と先頭 28 バイトの構造を揃えることで、旧アプリでの
    /// DATA_FILE_VERSION チェックによるバージョン判定を可能にする。
    /// </summary>
    private void WritePlainHeader(
      Stream outfs, byte encryptionMode, byte pubkeyAlgorithm, byte kdfAlgorithm)
    {
      byte[] byteArray;

      // アプリバージョン（short, 2 bytes）
      var ver = AppInfo.Version;
      var vernum = short.Parse(ver.ToString().Replace(".", ""));
      byteArray = BitConverter.GetBytes(vernum);
      outfs.Write(byteArray, 0, 2);

      // パスワード入力回数制限（1 byte）
      byteArray = BitConverter.GetBytes(MissTypeLimits);
      outfs.Write(byteArray, 0, 1);

      // 破壊フラグ（1 byte）
      byteArray = BitConverter.GetBytes(false); // v5 ではデフォルト false
      outfs.Write(byteArray, 0, 1);

      // ファイル・シグニチャ（16 bytes）
      // v5 では方式によらず統一。モード識別は別フィールドで行う。
      byteArray = Encoding.ASCII.GetBytes(STRING_TOKEN_NORMAL);
      outfs.Write(byteArray, 0, 16);

      // DATA_FILE_VERSION（int, 4 bytes）
      byteArray = BitConverter.GetBytes(DATA_FILE_VERSION);
      outfs.Write(byteArray, 0, 4);

      // ※ 暗号化ヘッダサイズ（int, 4 bytes）は呼び出し元で書き込む

      // 暗号化モード識別子（1 byte）
      outfs.WriteByte(encryptionMode);

      // 対称暗号アルゴリズム識別子（1 byte）
      outfs.WriteByte(SYMMETRIC_ALGORITHM_AES256GCM);

      // 公開鍵暗号アルゴリズム識別子（1 byte）
      outfs.WriteByte(pubkeyAlgorithm);

      // KDF アルゴリズム識別子（1 byte）
      outfs.WriteByte(kdfAlgorithm);

      // 予約領域（4 bytes）
      outfs.Write(new byte[4], 0, 4);
    }

    //======================================================================
    // 鍵保護ブロックの構築
    //======================================================================

    /// <summary>
    /// パスワード方式の鍵保護ブロックを構築する。
    /// Salt + Argon2 パラメータ + パスワード由来鍵で暗号化された共通鍵。
    /// </summary>
    private byte[] BuildPasswordProtectionBlock(byte[] commonKey, string password, byte[] passwordBinary)
    {
      var salt = CryptoHelper5.GenerateSalt();

      // パスワードから鍵を導出
      byte[] passwordDerivedKey;
      if (passwordBinary != null)
      {
        passwordDerivedKey = CryptoHelper5.DeriveKeyFromPasswordBinary(
          passwordBinary, salt,
          CryptoHelper5.ARGON2_DEFAULT_MEMORY_KB,
          CryptoHelper5.ARGON2_DEFAULT_ITERATIONS,
          CryptoHelper5.ARGON2_DEFAULT_PARALLELISM);
      }
      else
      {
        passwordDerivedKey = CryptoHelper5.DeriveKeyFromPassword(
          password, salt,
          CryptoHelper5.ARGON2_DEFAULT_MEMORY_KB,
          CryptoHelper5.ARGON2_DEFAULT_ITERATIONS,
          CryptoHelper5.ARGON2_DEFAULT_PARALLELISM);
      }

      // 共通鍵をパスワード由来鍵で暗号化
      var protectedCommonKey = CryptoHelper5.ProtectCommonKeyWithPassword(commonKey, passwordDerivedKey);
      CryptoHelper5.SecureClear(passwordDerivedKey);

      // 鍵保護ブロックを組み立て
      using (var ms = new MemoryStream())
      {
        // Salt (16 bytes)
        ms.Write(salt, 0, CryptoHelper5.ARGON2_SALT_SIZE);

        // Argon2 メモリコスト (int, 4 bytes)
        ms.Write(BitConverter.GetBytes(CryptoHelper5.ARGON2_DEFAULT_MEMORY_KB), 0, 4);

        // Argon2 反復回数 (int, 4 bytes)
        ms.Write(BitConverter.GetBytes(CryptoHelper5.ARGON2_DEFAULT_ITERATIONS), 0, 4);

        // Argon2 並列度 (int, 4 bytes)
        ms.Write(BitConverter.GetBytes(CryptoHelper5.ARGON2_DEFAULT_PARALLELISM), 0, 4);

        // 暗号化された共通鍵のサイズ (short, 2 bytes)
        ms.Write(BitConverter.GetBytes((short)protectedCommonKey.Length), 0, 2);

        // 暗号化された共通鍵 (60 bytes)
        ms.Write(protectedCommonKey, 0, protectedCommonKey.Length);

        return ms.ToArray();
      }
    }

    /// <summary>
    /// 公開鍵方式の鍵保護ブロックを構築する。
    /// 宛先鍵ブロック数 + (フィンガープリント + 暗号化された共通鍵) × N。
    /// 1 人のユーザーが複数デバイスを持つ場合も N に含まれる。
    /// </summary>
    private byte[] BuildPublicKeyProtectionBlock(byte[] commonKey)
    {
      using (var ms = new MemoryStream())
      {
        // 鍵ブロック数 N (short, 2 bytes)
        ms.Write(BitConverter.GetBytes((short)RecipientKeys.Count), 0, 2);

        // 各宛先エントリ
        foreach (var recipient in RecipientKeys)
        {
          // フィンガープリント (8 bytes)
          ms.Write(recipient.Fingerprint, 0, CryptoHelper5.FINGERPRINT_SIZE);

          byte[] encryptedCommonKey;
          bool isHybrid = recipient.AlgorithmId == PkaAlgorithmId.Rsa2048MlKem768
                       || recipient.AlgorithmId == PkaAlgorithmId.Rsa4096MlKem768;

          if (isHybrid && recipient.MlKemPublicKey != null)
          {
            // ハイブリッド: RSA-OAEP(wrapped) || ML-KEM-768 ciphertext
            encryptedCommonKey = CryptoHelper5.ProtectCommonKeyWithHybrid(
                commonKey, recipient.PublicKey, recipient.MlKemPublicKey);
          }
          else
          {
            // RSA 単体
            encryptedCommonKey = CryptoHelper5.ProtectCommonKeyWithRsa(commonKey, recipient.PublicKey);
          }

          // 暗号化された共通鍵のサイズ (short, 2 bytes)
          ms.Write(BitConverter.GetBytes((short)encryptedCommonKey.Length), 0, 2);

          // 暗号化された共通鍵
          ms.Write(encryptedCommonKey, 0, encryptedCommonKey.Length);
        }

        return ms.ToArray();
      }
    }

    //======================================================================
    // アーカイブ名ディレクトリエントリの書き込み
    //======================================================================

    /// <summary>
    /// 「名前をつけて保存」時のアーカイブ名ディレクトリエントリを暗号化ヘッダに書き込む。
    /// </summary>
    private void WriteArchiveDirectoryEntry(MemoryStream ms, string archiveName)
    {
      // ファイル名長 (short, 2 bytes)
      var fileLen = Encoding.UTF8.GetByteCount(archiveName);
      ms.Write(BitConverter.GetBytes((short)fileLen), 0, 2);

      // ファイル名
      var nameBytes = Encoding.UTF8.GetBytes(archiveName);
      ms.Write(nameBytes, 0, fileLen);

      // ファイルサイズ = 0（ディレクトリ）
      ms.Write(BitConverter.GetBytes((long)0), 0, 8);

      // ファイル属性 = 16（ディレクトリ）
      ms.Write(BitConverter.GetBytes(16), 0, 4);

      // UTC タイムスタンプ
      var now = DateTime.UtcNow;
      ms.Write(BitConverter.GetBytes(GetDateInt(now)), 0, 4);
      ms.Write(BitConverter.GetBytes(GetTimeInt(now)), 0, 4);
      ms.Write(BitConverter.GetBytes(GetDateInt(now)), 0, 4);
      ms.Write(BitConverter.GetBytes(GetTimeInt(now)), 0, 4);
    }

    //======================================================================
    // 自己実行形式の書き込み
    //======================================================================

    /// <summary>
    /// 自己実行形式ファイルのデータを書き込む。
    /// ExeOut45.cs（partial class）で定義される rawData を使用。
    /// </summary>
    private void WriteExeData(Stream outfs)
    {
      // partial class で定義される ExeOutFileSize, rawData を使用
      if (ExeToolVersionString == "4.0")
      {
        ExeOutFileSize[0] = rawData[0].Length;
        outfs.Write(rawData[0], 0, ExeOutFileSize[0]);
      }
      else
      {
        ExeOutFileSize[1] = rawData[1].Length;
        outfs.Write(rawData[1], 0, ExeOutFileSize[1]);
      }
    }

    //======================================================================
    // ディスク空き容量チェック
    //======================================================================

    private bool CheckDiskSpace(string filePath, long requiredSize)
    {
      var rootDriveLetter = Path.GetPathRoot(filePath)?.Substring(0, 1);
      if (rootDriveLetter == null || rootDriveLetter == "\\")
      {
        return true; // ネットワークパス
      }

      var drive = new DriveInfo(rootDriveLetter);
      switch (drive.DriveType)
      {
        case DriveType.CDRom:
        case DriveType.NoRootDirectory:
        case DriveType.Unknown:
          return true;

        case DriveType.Fixed:
        case DriveType.Network:
        case DriveType.Ram:
        case DriveType.Removable:
          if (!drive.IsReady || drive.AvailableFreeSpace < requiredSize)
          {
            ReturnCode = NO_DISK_SPACE;
            DriveName = drive.ToString();
            AvailableFreeSpace = drive.AvailableFreeSpace;
            return false;
          }
          return true;

        default:
          return true;
      }
    }

    //======================================================================
    // キャンセル判定
    //======================================================================

    private bool ShouldCancel(BackgroundWorker worker)
    {
      return _isCancelled || (worker?.CancellationPending ?? false);
    }

    //======================================================================
    // 進捗更新
    //======================================================================

    public void UpdateProgress(long size)
    {
      Interlocked.Add(ref _processedSize, size);
    }

    //======================================================================
    // ファイルリスト構築
    //======================================================================

    /// <summary>
    /// ディレクトリ内のファイルとサブディレクトリを再帰的に列挙する。
    /// </summary>
    public IEnumerable<FileSystemEntry> GetFileList(
      string parentPath, string rootFolderPath, Func<bool> cancelCheck, BackgroundWorker worker)
    {
      resultsConcurrentBag = new ConcurrentBag<FileSystemEntry>();
      _processedFileCount = 0;
      swProgress.Restart();

      try
      {
        var pending = new Queue<string>();
        pending.Enqueue(rootFolderPath);

        while (pending.Count > 0)
        {
          if (cancelCheck()) throw new OperationCanceledException();

          var currentPath = pending.Dequeue();
          var dirInfo = GetDirectoryInfo(parentPath, currentPath);

          // 再解析ポイント（ジャンクション、シンボリックリンク）はスキップ
          if ((dirInfo.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) continue;

          if (!string.IsNullOrEmpty(dirInfo.FullPath))
          {
            resultsConcurrentBag.Add(dirInfo);
            ReportFileListProgress(currentPath, worker);
          }

          try
          {
            foreach (var dir in Directory.GetDirectories(currentPath))
            {
              if (cancelCheck()) throw new OperationCanceledException();
              pending.Enqueue(dir);
            }

            foreach (var file in Directory.GetFiles(currentPath))
            {
              if (cancelCheck()) throw new OperationCanceledException();
              var fileInfo = GetFileInfo(parentPath, file, cancelCheck);
              if (!string.IsNullOrEmpty(fileInfo.FullPath))
              {
                resultsConcurrentBag.Add(fileInfo);
                ReportFileListProgress(file, worker);
              }
            }
          }
          catch (UnauthorizedAccessException ex)
          {
            Debug.WriteLine($"Access denied to {currentPath}: {ex.Message}");
          }
          catch (DirectoryNotFoundException ex)
          {
            Debug.WriteLine($"Directory not found {currentPath}: {ex.Message}");
          }
        }

        return resultsConcurrentBag
          .Where(entry => !string.IsNullOrEmpty(entry.FullPath))
          .OrderBy(e => e.RelativePath)
          .ToList();
      }
      finally
      {
        swProgress.Restart();
      }
    }

    private void ReportFileListProgress(string currentPath, BackgroundWorker worker)
    {
      Interlocked.Increment(ref _processedFileCount);
      if (swProgress.ElapsedMilliseconds > 100)
      {
        worker?.ReportProgress(-1, new ArrayList
        {
          READY_FOR_ENCRYPT,
          $"{currentPath} ({_processedFileCount})"
        });
        swProgress.Restart();
      }
    }

    //======================================================================
    // ファイルインデックスの書き込み（暗号化ヘッダ内）
    //======================================================================

    /// <summary>
    /// 各ファイル/ディレクトリの情報を MemoryStream に書き込む。
    /// v5: CRC32(4bytes) でファイル破損検出。改ざん検知は GCM 認証タグが担う。
    /// </summary>
    private static long FileInfoStreamWriter(ref MemoryStream ms, FileSystemEntry entry, string newArchiveName)
    {
      if (ms == null) throw new ArgumentNullException(nameof(ms));
      if (string.IsNullOrEmpty(entry.RelativePath)) return 0;

      newArchiveName = newArchiveName ?? string.Empty;

      // ファイル名長 (short, 2 bytes)
      var fileLen = Encoding.UTF8.GetByteCount(newArchiveName + entry.RelativePath);
      ms.Write(BitConverter.GetBytes((short)fileLen), 0, 2);

      // ファイル名
      var nameBytes = Encoding.UTF8.GetBytes(newArchiveName + entry.RelativePath);
      ms.Write(nameBytes, 0, fileLen);

      // ファイルサイズ (Int64, 8 bytes)
      ms.Write(BitConverter.GetBytes(entry.Size), 0, 8);

      // ファイル属性 (int, 4 bytes)
      ms.Write(BitConverter.GetBytes((int)entry.Attributes), 0, 4);

      // UTC 更新日 (int, 4 bytes)
      ms.Write(BitConverter.GetBytes(entry.LastWriteDate), 0, 4);

      // UTC 更新時 (int, 4 bytes)
      ms.Write(BitConverter.GetBytes(entry.LastWriteTime), 0, 4);

      // UTC 作成日 (int, 4 bytes)
      ms.Write(BitConverter.GetBytes(entry.CreationDate), 0, 4);

      // UTC 作成時 (int, 4 bytes)
      ms.Write(BitConverter.GetBytes(entry.CreationTime), 0, 4);

      // CRC32 チェックサム（ファイルでサイズ > 0 の場合のみ）
      if (entry is { IsDirectory: false, Size: > 0 })
      {
        ms.Write(entry.Crc32, 0, Crc32Utility.CRC32_SIZE);
      }

      return entry.Size;
    }

    //======================================================================
    // FileSystemEntry
    //======================================================================

    public class FileSystemEntry
    {
      public bool IsDirectory { get; set; }
      public string FullPath { get; set; }
      public string RelativePath { get; set; }
      public long Size { get; set; }
      public FileAttributes Attributes { get; set; }
      public int LastWriteDate { get; set; }
      public int LastWriteTime { get; set; }
      public int CreationDate { get; set; }
      public int CreationTime { get; set; }
      /// <summary>CRC32 チェックサム（4 バイト）。ファイル破損検出用</summary>
      public byte[] Crc32 { get; set; }
    }

    //======================================================================
    // ファイル/ディレクトリ情報の取得
    //======================================================================

    private static FileSystemEntry GetDirectoryInfo(string parentPath, string dirPath)
    {
      try
      {
        var di = new DirectoryInfo(dirPath);
        var relativePath = CreateRelativePath(parentPath, dirPath);
        if (!relativePath.EndsWith("\\"))
        {
          relativePath += "\\";
        }

        return new FileSystemEntry
        {
          IsDirectory = true,
          FullPath = dirPath,
          RelativePath = relativePath,
          Size = 0,
          Attributes = di.Attributes,
          LastWriteDate = GetDateInt(di.LastWriteTimeUtc),
          LastWriteTime = GetTimeInt(di.LastWriteTimeUtc),
          CreationDate = GetDateInt(di.CreationTimeUtc),
          CreationTime = GetTimeInt(di.CreationTimeUtc),
          Crc32 = Array.Empty<byte>()
        };
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        throw new FileProcessingException($"Error processing directory: {dirPath}", dirPath, ex);
      }
    }

    private static FileSystemEntry GetFileInfo(string parentPath, string filePath, Func<bool> cancelCheck)
    {
      try
      {
        var fi = new FileInfo(filePath);
        return new FileSystemEntry
        {
          IsDirectory = false,
          FullPath = filePath,
          RelativePath = CreateRelativePath(parentPath, filePath),
          Size = fi.Length,
          Attributes = fi.Attributes,
          LastWriteDate = GetDateInt(fi.LastWriteTimeUtc),
          LastWriteTime = GetTimeInt(fi.LastWriteTimeUtc),
          CreationDate = GetDateInt(fi.CreationTimeUtc),
          CreationTime = GetTimeInt(fi.CreationTimeUtc),
          Crc32 = fi.Length == 0 ? null : Crc32Utility.ComputeFileCrc32(filePath, cancelCheck)
        };
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        throw new FileProcessingException($"Error processing file: {filePath}", filePath, ex);
      }
    }

    public class FileProcessingException : Exception
    {
      public string FilePath { get; }

      public FileProcessingException(string message, string filePath, Exception inner)
        : base(message, inner)
      {
        FilePath = filePath;
      }
    }

    //======================================================================
    // パスユーティリティ
    //======================================================================

    private static string CreateRelativePath(string parentPath, string fullPath)
    {
      if (string.IsNullOrEmpty(parentPath) || string.IsNullOrEmpty(fullPath))
        return string.Empty;

      parentPath = Path.GetFullPath(parentPath);
      fullPath = Path.GetFullPath(fullPath);

      if (!fullPath.StartsWith(parentPath, StringComparison.OrdinalIgnoreCase))
        return string.Empty;

      var relativeLength = fullPath.Length - parentPath.Length;
      if (relativeLength <= 0) return string.Empty;

      var relativePath = fullPath.Substring(parentPath.Length, relativeLength)
        .TrimStart(Path.DirectorySeparatorChar);

      if (Directory.Exists(fullPath) && !relativePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
      {
        relativePath += Path.DirectorySeparatorChar;
      }

      return relativePath;
    }

    //======================================================================
    // 日時ユーティリティ
    //======================================================================

    private static int GetDateInt(DateTime dt)
    {
      return (dt.Year * 10000) + (dt.Month * 100) + dt.Day;
    }

    private static int GetTimeInt(DateTime dt)
    {
      return (dt.Hour * 10000) + (dt.Minute * 100) + dt.Second;
    }

    //======================================================================
    // 例外ハンドリング
    //======================================================================

    private void HandleEncryptionException(Exception ex)
    {
      switch (ex)
      {
        case UnauthorizedAccessException _:
          ReturnCode = OS_DENIES_ACCESS;
          ErrorFilePath = AtcFilePath;
          break;
        case DirectoryNotFoundException _:
          ReturnCode = DIRECTORY_NOT_FOUND;
          ErrorMessage = ex.Message;
          break;
        case DriveNotFoundException _:
          ReturnCode = DRIVE_NOT_FOUND;
          ErrorMessage = ex.Message;
          break;
        case FileLoadException fileLoadEx:
          ReturnCode = FILE_NOT_LOADED;
          ErrorMessage = fileLoadEx.FileName;
          break;
        case FileNotFoundException fileNotFoundEx:
          ReturnCode = FILE_NOT_FOUND;
          ErrorMessage = fileNotFoundEx.FileName;
          break;
        case PathTooLongException _:
          ReturnCode = PATH_TOO_LONG;
          break;
        case IOException _:
          ReturnCode = IO_EXCEPTION;
          ErrorMessage = ex.Message;
          break;
        default:
          ReturnCode = ERROR_UNEXPECTED;
          ErrorMessage = ex.Message;
          break;
      }
    }

    //======================================================================
    // アセンブリ情報
    //======================================================================

    private static class AppInfo
    {
      private static readonly Assembly _assembly = Assembly.GetCallingAssembly();
      public static Version Version => _assembly.GetName().Version;
    }

  } // end class FileEncrypt5

} // end namespace AttacheCase
