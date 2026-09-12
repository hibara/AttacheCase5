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
#if !EXEOUT
using Org.BouncyCastle.Pqc.Crypto.Utilities;
#endif

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

    //======================================================================
    // ヘッダーから読み取る値の上限
    //   ファイルから読んだ値をそのままバッファ確保やループ回数に使わないための歯止め。
    //   鍵導出より前に読む値は、まだ正当性を検証できていないことに注意。
    //======================================================================

    /// <summary>公開鍵方式の宛先鍵ブロック数の上限（1 人が複数デバイスを持つ場合を含む）</summary>
    private const int MAX_RECIPIENT_COUNT = 1024;

    /// <summary>暗号化された共通鍵 1 個あたりのサイズ上限（RSA-4096 + ML-KEM-768 でも 2 KB 未満）</summary>
    private const int MAX_ENCRYPTED_KEY_SIZE = 8192;

    /// <summary>暗号化ヘッダ（ファイル索引）のサイズ上限</summary>
    /// <remarks>
    /// この値はパスワードの正誤を確かめる前に読むので、そのまま
    /// <c>new byte[...]</c> へ渡すと 4 バイトの改ざんでメモリを枯渇させられる。
    /// 1 エントリあたり数百バイトとして、64 MB あれば十万件規模の索引でも足りる。
    /// </remarks>
    private const int MAX_ATC_HEADER_SIZE = 64 * 1024 * 1024;
    private const string STRING_TOKEN_NORMAL = "_AttacheCaseData";

    //======================================================================
    // 内部変数
    //======================================================================
    private readonly long _ExeOutSize;
    private long _TotalSize;
    private byte[] _keyProtectionBlockRaw;

    // 展開で新しく作ったファイル・フォルダー（失敗時に削除して巻き戻す）
    private readonly List<string> _createdFiles = new List<string>();
    private readonly List<string> _createdDirs = new List<string>();

    // 既存ファイルへの上書き分。GCM タグ検証の前に FileMode.Create で開くと
    // その時点で元の内容が失われるため、一時ファイルへ書いて検証後に置き換える
    private readonly List<KeyValuePair<string, string>> _pendingReplacements =
      new List<KeyValuePair<string, string>>(); // Key = 一時, Value = 最終

    // 開いたハンドルの実体パス検査（TOCTOU 対策）の基準になる保存先の実体パス
    private string _resolvedOutDirPath;

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

    /// <summary>
    /// パスワード入力回数が制限を超えたとき、暗号化ファイルを破壊するか。
    /// </summary>
    /// <remarks>
    /// 平文ヘッダの 4 バイト目。暗号化した人の指定がファイルと一緒に運ばれてくる。
    /// 開く側のローカル設定 (<c>AppSettings.fBroken</c>) では変わらない。
    /// このバイトは鍵導出にも認証にも使われていないため、改ざんされても復号自体には影響しない。
    /// </remarks>
    public bool BreakFileWhenLimitExceeded { get; private set; } = false;

    /// <summary>タイムスタンプを復号時に合わせる</summary>
    public bool fSameTimeStamp { get; set; } = false;

#if !EXEOUT
    /// <summary>サルベージモード: 親フォルダーを一つずつ作成</summary>
    public bool fSalvageToCreateParentFolderOneByOne { get; set; } = false;

    /// <summary>サルベージモード: 同一階層に復号</summary>
    public bool fSalvageIntoSameDirectory { get; set; } = false;
#endif

    /// <summary>自己実行形式か</summary>
    public bool fExecutableType { get; } = false;

#if !EXEOUT
    /// <summary>ハッシュ値チェックを無視する</summary>
    public bool fSalvageIgnoreHashCheck { get; set; } = false;
#endif

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

    /// <summary>
    /// 直近に開こうとしている／処理中のファイル・ディレクトリパス。
    /// ファイルパスを直接持たない例外（UnauthorizedAccessException, IOException 等）で
    /// どのパスでエラーが起きたかを表示するためのフォールバック値。
    /// </summary>
    public string CurrentProcessingFilePath { get; private set; } = "";

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

    // ENCRYPTION_MODE_PUBLIC_KEY: FileEncrypt5.ENCRYPTION_MODE_PUBLIC_KEY と同値 (0x01)
    /// <summary>公開鍵方式のデータかどうか</summary>
    public bool fPublicKeyEncryption => EncryptionMode == 0x01;

#if !EXEOUT
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
#endif

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
        CurrentProcessingFilePath = AtcFilePath;
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
        CurrentProcessingFilePath = AtcFilePath;
        using (var fs = new FileStream(AtcFilePath, FileMode.Open, FileAccess.Read))
        {
          // 平文ヘッダは 36 バイト必要（2+1+1+16+4+1+1+1+1+4+4）。
          // 旧実装は 16 バイトしか確認せず、さらに fs.Read の戻り値と
          // fs.ReadByte() の -1 を捨てていた。切り詰められたファイルでは
          // ゼロ埋めのバッファや -1 をそのまま値として使ってしまい、
          // 「破損」と報告されないまま処理が進む。
          // 具体的には破壊フラグが -1 != 0 で true になり、
          // 各アルゴリズム識別子が (byte)(-1) = 255 になっていた。
          const int PLAIN_HEADER_SIZE = 36;
          if (fs.Length < (fExecutableType ? _ExeOutSize : 0) + PLAIN_HEADER_SIZE)
          {
            ReturnCode = NOT_ATC_DATA; ErrorFilePath = AtcFilePath; return;
          }
          if (fExecutableType) fs.Seek(_ExeOutSize, SeekOrigin.Begin);

          var header = new byte[PLAIN_HEADER_SIZE];
          ReadExactly(fs, header, PLAIN_HEADER_SIZE);

          var pos = 0;

          // アプリバージョン (short, 2 bytes)
          AppVersion = BitConverter.ToInt16(header, pos); pos += 2;

          // ミスタイプ回数制限 (1 byte)
          MissTypeLimits = (char)header[pos]; pos += 1;

          // 破壊フラグ (1 byte)
          // 「回数制限を超えたら破壊するか」は暗号化した人の指定であり、
          // ローカル設定ではなくこのバイトで判断する（アタッシェケース4 と同じ仕様）。
          BreakFileWhenLimitExceeded = header[pos] != 0; pos += 1;

          // トークン文字列 (16 bytes)
          TokenStr = Encoding.ASCII.GetString(header, pos, 16); pos += 16;

          // DATA_FILE_VERSION (int, 4 bytes)
          DataFileVersion = BitConverter.ToInt32(header, pos); pos += 4;

          if (DataFileVersion < 150)
          {
            // v150 未満のファイルは FileDecrypt4 で処理すべき
            ReturnCode = NOT_ATC_DATA;
            ErrorMessage = "このファイルは旧バージョン形式です。FileDecrypt4 で処理してください。";
            return;
          }

          // 暗号化モード (1 byte)
          EncryptionMode = header[pos]; pos += 1;

          // 対称暗号アルゴリズム (1 byte)
          SymmetricAlgorithm = header[pos]; pos += 1;

          // 公開鍵暗号アルゴリズム (1 byte)
          PublicKeyAlgorithm = header[pos]; pos += 1;

          // KDF アルゴリズム (1 byte)
          KdfAlgorithm = header[pos]; pos += 1;

          // 予約領域 (4 bytes)
          pos += 4;

          // 暗号化ヘッダサイズ (int, 4 bytes)
          _AtcHeaderSize = BitConverter.ToInt32(header, pos);

          // 鍵導出より前に読む値なので、まだ正当性を検証できていない。
          // そのままバッファ確保に使うと、負値で OverflowException、
          // 0x7FFFFFFF で 2 GB の確保を試みて OutOfMemoryException になる。
          // どちらもパスワードの正誤に関係なく発火する。
          if (_AtcHeaderSize <= 0 || _AtcHeaderSize > MAX_ATC_HEADER_SIZE)
          {
            ReturnCode = ATC_BROKEN_DATA;
            ErrorFilePath = AtcFilePath;
            return;
          }

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
      if (EncryptionMode == 0x00) // ENCRYPTION_MODE_PASSWORD
      {
        // Salt(16) + MemoryKB(4) + Iterations(4) + Parallelism(4) + EncKeySize(2) + EncKey(S)
        var fixedPart = new byte[16 + 4 + 4 + 4 + 2];
        ReadExactly(fs, fixedPart, fixedPart.Length);

        var encKeySize = BitConverter.ToInt16(fixedPart, fixedPart.Length - 2);
        // ファイルから読んだサイズをそのまま確保に使わない。
        // パスワード方式の暗号化共通鍵は Nonce(12) + 鍵(32) + タグ(16) = 60 バイト固定。
        if (encKeySize != CryptoHelper5.ENCRYPTED_COMMON_KEY_SIZE_PASSWORD)
        {
          throw new InvalidDataException(
            "鍵保護ブロックのサイズが不正です: " + encKeySize);
        }

        var encKey = new byte[encKeySize];
        ReadExactly(fs, encKey, encKeySize);

        var result = new byte[fixedPart.Length + encKeySize];
        Buffer.BlockCopy(fixedPart, 0, result, 0, fixedPart.Length);
        Buffer.BlockCopy(encKey, 0, result, fixedPart.Length, encKeySize);
        return result;
      }
      else
      {
        // 鍵ブロック数 N + (フィンガープリント + サイズ + 暗号化共通鍵) × N
        // Exeout では公開鍵方式は復号できないが、後続のチェックで弾くため
        // ここではブロックを読み飛ばすだけにとどめる。
        using var ms = new MemoryStream();
        var nBuf = new byte[2];
        ReadExactly(fs, nBuf, 2);
        ms.Write(nBuf, 0, 2);

        var n = BitConverter.ToInt16(nBuf, 0);
        // 宛先の数。負値と非現実的な大きさを弾く（確保とループの暴走を防ぐ）
        if (n < 0 || n > MAX_RECIPIENT_COUNT)
        {
          throw new InvalidDataException("宛先鍵ブロックの数が不正です: " + n);
        }

        for (var i = 0; i < n; i++)
        {
          var fp = new byte[CryptoHelper5.FINGERPRINT_SIZE];
          ReadExactly(fs, fp, fp.Length);
          ms.Write(fp, 0, fp.Length);

          var sizeBuf = new byte[2];
          ReadExactly(fs, sizeBuf, 2);
          ms.Write(sizeBuf, 0, 2);

          var encKeySize = BitConverter.ToInt16(sizeBuf, 0);
          // RSA-4096 + ML-KEM-768 のハイブリッドでも 2 KB 未満に収まる
          if (encKeySize <= 0 || encKeySize > MAX_ENCRYPTED_KEY_SIZE)
          {
            throw new InvalidDataException("暗号化共通鍵のサイズが不正です: " + encKeySize);
          }

          var encKey = new byte[encKeySize];
          ReadExactly(fs, encKey, encKeySize);
          ms.Write(encKey, 0, encKeySize);
        }
        return ms.ToArray();
      }
    }

    /// <summary>
    /// 指定バイト数を確実に読み取る。足りなければ壊れたファイルとして扱う。
    /// </summary>
    /// <remarks>
    /// <see cref="Stream.Read(byte[],int,int)"/> は要求より少ないバイト数を返すことがあり、
    /// 戻り値を捨てるとゼロ埋めのままのバッファで処理が進んでしまう。
    /// 切り詰められたファイルを「壊れている」と正しく報告するために必ず戻り値を見る。
    /// </remarks>
    private static void ReadExactly(FileStream fs, byte[] buffer, int count)
    {
      var offset = 0;
      while (offset < count)
      {
        var read = fs.Read(buffer, offset, count - offset);
        if (read <= 0)
        {
          throw new InvalidDataException("ファイルが途中で終わっています。");
        }
        offset += read;
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

      _createdFiles.Clear();
      _createdDirs.Clear();
      _pendingReplacements.Clear();
      // 保存先自身がリンクであることは正当なので、実体パスを基準にする
      _resolvedOutDirPath = PathSafety.GetFinalDirectoryPathOrNull(outDirPath) ?? outDirPath;

      try
      {
        //--------------------------------------------------------------
        // ① 共通鍵の復元
        //--------------------------------------------------------------
#if EXEOUT
        // Exeout は公開鍵方式の復号に対応しない（鍵管理 UI を持たない）
        if (EncryptionMode != 0x00) // ENCRYPTION_MODE_PASSWORD
        {
          ReturnCode = CRYPTOGRAPHIC_EXCEPTION;
          ErrorMessage = "この自己実行形式は公開鍵方式の復号に対応していません。";
          ErrorFilePath = filePath;
          return false;
        }
        byte[] commonKey = RecoverCommonKeyFromPassword(password, passwordBinary);
#else
        byte[] commonKey = EncryptionMode == FileEncrypt5.ENCRYPTION_MODE_PASSWORD
          ? RecoverCommonKeyFromPassword(password, passwordBinary)
          : RecoverCommonKeyFromPublicKey();
#endif

        if (commonKey == null) return false;

        //--------------------------------------------------------------
        // ② 暗号化ヘッダの復号 → ファイルリスト構築
        //--------------------------------------------------------------
        var FileDataList = new List<FileData>();

        CurrentProcessingFilePath = filePath;
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
          // 平文ヘッダ(24+8) + 暗号化ヘッダサイズ(4) + 鍵保護ブロック の後ろまでシーク
          long headerDataStart = (fExecutableType ? _ExeOutSize : 0)
                                 + 36 + _keyProtectionBlockRaw.Length;
          fs.Seek(headerDataStart, SeekOrigin.Begin);

          // ヘッダ暗号化用 Nonce (12 bytes)
          // 短い読み取りをゼロ埋めのまま AesGcmDecrypt に渡すと、切り詰められた
          // ファイルが「パスワード違い」と報告される。全量読めなければ
          // InvalidDataException → ATC_BROKEN_DATA（本体 Nonce も同様）
          var headerNonce = new byte[CryptoHelper5.AES_GCM_NONCE_SIZE];
          ReadExactly(fs, headerNonce, headerNonce.Length);

          // 暗号化ヘッダ
          var encryptedHeader = new byte[_AtcHeaderSize];
          ReadExactly(fs, encryptedHeader, _AtcHeaderSize);

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
          // ④ 本体データの復号 + Deflate 解凍 + ファイル展開（ストリームパイプライン）
          //
          //     fs → GcmDecryptingStream → DeflateStream → ExtractFiles
          //
          //     中間バッファを持たないため、TB クラスの暗号化ファイルでも
          //     定数メモリ（数十 KB）で処理できる。
          //--------------------------------------------------------------
          var bodyNonce = new byte[CryptoHelper5.AES_GCM_NONCE_SIZE];
          ReadExactly(fs, bodyNonce, bodyNonce.Length);

          try
          {
            using (var gcmStream = new GcmDecryptingStream(fs, commonKey, bodyNonce, leaveOpen: true))
            {
              try
              {
                using (var ds = new DeflateStream(gcmStream, CompressionMode.Decompress))
                {
                  if (!ExtractFiles(ds, FileDataList, outDirPath, cancelCheck, dialog, worker, swProgress, e))
                  {
                    // 中断・エラーで出力を破棄する経路。タグ検証まで到達しないので
                    // 放棄を明示する（宣言しないと Dispose で例外になる）。
                    gcmStream.AbandonWithoutVerification();
                    CryptoHelper5.SecureClear(commonKey);
                    CleanupPartialOutputs();
                    return false;
                  }
                }

                // DeflateStream は deflate 終端マーカーで読み取りを停止することがあるため、
                // 残りバイトを明示的に読み切って GCM 認証タグを検証する。
                gcmStream.DrainAndVerify();
              }
              catch
              {
                // 例外で抜ける場合も同様。ここで宣言しておかないと、
                // Dispose が投げる例外が本来の失敗理由を覆い隠す。
                gcmStream.AbandonWithoutVerification();
                throw;
              }
            }
          }
          catch (InvalidCipherTextException)
          {
            // タグ検証に失敗した = 復号した内容は信用できない。
            // 作りかけの新規ファイルと上書き用の一時ファイルを消して巻き戻す
            CryptoHelper5.SecureClear(commonKey);
            CleanupPartialOutputs();
            ReturnCode = ATC_BROKEN_DATA;
            ErrorFilePath = filePath;
            return false;
          }

          // 既存ファイルの置き換えは、本体全体のタグ検証を通ってから行う
          //（検証前に上書きすると、破損書庫で元ファイルの内容まで失う）
          foreach (var pair in _pendingReplacements)
          {
            try
            {
              File.Replace(pair.Key, pair.Value, null, true);
            }
            catch (Exception)
            {
              File.Delete(pair.Value);
              File.Move(pair.Key, pair.Value);
            }
          }
          _pendingReplacements.Clear();

          CryptoHelper5.SecureClear(commonKey);
        }

        swDecrypt.Stop();
        var ts = swDecrypt.Elapsed;
        DecryptionTimeString = $"{ts.Hours}h {ts.Minutes}m {ts.Seconds}s {ts.Milliseconds}ms";
        ReturnCode = DECRYPT_SUCCEEDED;
        return true;
      }
      catch (OperationCanceledException)
      {
        // CRC32 計算（RestoreFileAttributes）や KDF のキャンセルはここへ抜けてくる。
        // 汎用 catch に任せると、直前の ReturnCode 次第で「成功」のまま
        // false が返り、キャンセルとして扱われない
        CleanupPartialOutputs();
        ReturnCode = USER_CANCELED;
        if (e != null) e.Cancel = true;
        return false;
      }
      catch (Exception ex)
      {
        CleanupPartialOutputs();
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

      //--------------------------------------------------------------
      // 鍵導出パラメータの範囲検証
      //
      // これらの値はファイルから読んだだけで、まだ正当性を検証できていない。
      // GCM の認証タグで守られるのは鍵導出が終わったあとなので、
      // 「Argon2id を呼ぶ前」に守れるのは範囲検証だけ。
      // 検証せずに渡すと、改ざんされた 12 バイトで巨大なメモリ確保や
      // 事実上のフリーズを起こされる（パスワードが正しいかどうかに関係なく発火する）。
      //--------------------------------------------------------------
      if (!CryptoHelper5.IsValidArgon2Parameters(memoryKb, iterations, parallelism))
      {
        ReturnCode = ATC_BROKEN_DATA;
        ErrorFilePath = AtcFilePath;
        ErrorMessage = "鍵導出パラメータが不正です。ファイルが破損しているか、改ざんされている可能性があります。";
        return null;
      }

      if (encKeySize != CryptoHelper5.ENCRYPTED_COMMON_KEY_SIZE_PASSWORD ||
          offset + encKeySize > _keyProtectionBlockRaw.Length)
      {
        ReturnCode = ATC_BROKEN_DATA;
        ErrorFilePath = AtcFilePath;
        ErrorMessage = "鍵保護ブロックのサイズが不正です。ファイルが破損している可能性があります。";
        return null;
      }

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

#if !EXEOUT
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
#endif

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

    /// <summary>
    /// 現在位置から先に、まだ実データを必要とするエントリが残っているかを返す。
    /// </summary>
    /// <param name="fileIndex">これから処理するエントリの位置。</param>
    /// <param name="writtenSize">現在のエントリについて既に書き込んだバイト数。</param>
    /// <param name="fileDataList">索引。</param>
    /// <remarks>
    /// フォルダーと 0 バイトのファイルは実データを持たないので、
    /// 入力が尽きていても作成できる。それ以外が残っていれば本体が足りていない。
    /// </remarks>
    private static bool HasRemainingDataEntries(
      int fileIndex, long writtenSize, List<FileData> fileDataList)
    {
      for (var i = fileIndex; i < fileDataList.Count; i++)
      {
        var path = fileDataList[i].FilePath;
        if (path != null && (path.EndsWith("\\") || path.EndsWith("/")))
        {
          continue;   // ディレクトリ
        }

        var remaining = fileDataList[i].FileSize - (i == fileIndex ? writtenSize : 0);
        if (remaining > 0)
        {
          return true;
        }
      }

      return false;
    }

    private bool ExtractFiles(
      Stream dataStream, List<FileData> FileDataList, string OutDirPath,
      Func<bool> cancelCheck, Action<int, string, IReadOnlyList<string>> dialog,
      BackgroundWorker worker, Stopwatch swProgress, DoWorkEventArgs e)
    {
      FileStream outfs = null;
      string currentWritePath = null; // outfs の実際の書き込み先（既存ファイルなら一時ファイル）
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

        if (len == 0)
        {
          // 入力の終端。ここで区別すべきものが 2 つある。
          //
          //   (a) 末尾に 0 バイトのファイルやフォルダーだけが残っている
          //       → エントリを作るために内側ループを 1 度だけ回す（従来からの挙動）。
          //          書き込みバイト数は 0 になるので実データは出ない。
          //
          //   (b) 実データを持つエントリがまだ残っている
          //       → 本体が宣言サイズより短い。ファイルが壊れている。
          //
          // 旧実装は無条件に len = 1 としていたため、(b) でも内側ループを回し続け、
          // byteArray に残った**前回の内容**を 1 バイトずつ書き込んでいた。
          // 宣言サイズが大きいと大量のゴミをディスクへ書く。
          // 最終的には GCM の認証で失敗するが、その時点では書き込みが終わっている。
          if (HasRemainingDataEntries(FileIndex, FileSize, FileDataList))
          {
            outfs?.Close();
            ReturnCode = ATC_BROKEN_DATA;
            ErrorFilePath = string.IsNullOrEmpty(CurrentProcessingFilePath)
              ? AtcFilePath : CurrentProcessingFilePath;
            return false;
          }

          len = 1;
        }

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
              var dirExisted = Directory.Exists(dirPath);
              Directory.CreateDirectory(dirPath);
              if (!dirExisted) _createdDirs.Add(dirPath);
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

#if !EXEOUT
            if (fSalvageToCreateParentFolderOneByOne)
              Directory.CreateDirectory(Path.GetDirectoryName(processPath));
#endif

            if (!fSkip)
            {
              CurrentProcessingFilePath = processPath;

              // 既存ファイルは直接 FileMode.Create で開かない。開いた時点で元の
              // 内容が失われ、GCM タグ検証の失敗・切り詰め・キャンセルで
              // 巻き戻せなくなる。同じフォルダーの一時ファイルへ書き、
              // 本体全体のタグ検証後に置き換える
              var existedFile = File.Exists(processPath);
              currentWritePath = existedFile
                ? processPath + "." + Guid.NewGuid().ToString("N") + ".attmp"
                : processPath;

              try
              {
                outfs = new FileStream(currentWritePath, FileMode.Create, FileAccess.Write);
              }
              catch
              {
                var fi = new FileInfo(currentWritePath);
                if (!fi.Directory.Exists)
                {
                  fi.Directory.Create();
                  _createdDirs.Add(fi.Directory.FullName);
                }
                outfs = new FileStream(currentWritePath, FileMode.Create, FileAccess.Write);
              }

              // 文字列パスの事前検査（ValidateOutputPath）と作成の間に、途中の
              // ディレクトリをジャンクションへ差し替えられると、保存先の外の
              // 実体へ書いてしまう（TOCTOU）。開いたハンドルの実体パスで
              // 最終確認し、競合ごと閉じる
              if (!PathSafety.IsHandleInsideDirectory(outfs.SafeFileHandle, _resolvedOutDirPath))
              {
                outfs.Close(); outfs = null;
                try { File.Delete(currentWritePath); } catch (Exception) { }
                ReturnCode = INVALID_FILE_PATH;
                ErrorFilePath = processPath;
                return false;
              }

              if (existedFile)
              {
                _pendingReplacements.Add(new KeyValuePair<string, string>(currentWritePath, processPath));
              }
              else
              {
                _createdFiles.Add(processPath);
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
            //
            // 旧実装は戻り値を見ておらず、RestoreFileAttributes が
            // ReturnCode = NOT_CORRECT_HASH_VALUE を立てても、
            // 後続の DECRYPT_SUCCEEDED で上書きされて利用者に届かなかった。
            // CRC32 を計算する処理全体が無意味になっていた。
            if (!fSkip && !RestoreFileAttributes(FileIndex, FileDataList, currentWritePath, cancelCheck, dialog))
            {
              outfs?.Close();
              return false;
            }

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

    /// <returns>
    /// 処理を続けてよい場合は <c>true</c>。
    /// CRC32 が一致せず、利用者が中止を選んだ場合は <c>false</c>。
    /// </returns>
    /// <remarks>
    /// 以前は <c>void</c> で、<c>ReturnCode = NOT_CORRECT_HASH_VALUE</c> を立てても
    /// 呼び出し元がそれを見ておらず、後続の <c>DECRYPT_SUCCEEDED</c> に
    /// 上書きされていた。破損検出の結果が利用者に届かず、
    /// CRC32 を計算する処理全体が無意味になっていた。
    /// </remarks>
    private bool RestoreFileAttributes(
      int FileIndex, List<FileData> FileDataList, string actualPath,
      Func<bool> cancelCheck, Action<int, string, IReadOnlyList<string>> dialog)
    {
      // filePath は最終的な出力パス（表示・記録用）。実体は actualPath
      // （既存ファイルへの上書きでは検証待ちの一時ファイル）に書かれている。
      // 属性・タイムスタンプは実体側へ付ける（File.Replace が引き継ぐ）
      var filePath = FileDataList[FileIndex].FilePath;
      var fi = new FileInfo(actualPath)
      {
        CreationTime = FileDataList[FileIndex].CreationDateTime,
        LastWriteTime = FileDataList[FileIndex].LastWriteDateTime,
        Attributes = (FileAttributes)FileDataList[FileIndex].FileAttribute
      };

      if (
#if !EXEOUT
          fSalvageIgnoreHashCheck ||
#endif
          FileDataList[FileIndex].FileSize <= 0 ||
          FileDataList[FileIndex].Hash == null)
        return true;

      // ComputeFileCrc32 はキャンセル時に OperationCanceledException を投げ、
      // 成功時は必ず 4 バイトを返す（null は返らない）。
      var computed = Crc32Utility.ComputeFileCrc32(actualPath, cancelCheck);
      if (!computed.SequenceEqual(FileDataList[FileIndex].Hash))
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
          return false;
        }
      }

      return true;
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
#if !EXEOUT
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
#endif
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
    // 失敗時の巻き戻し
    //======================================================================

    /// <summary>
    /// 作りかけの出力を片付ける。上書き用の一時ファイルは削除し
    /// （既存ファイルは無傷のまま残る）、新規に作ったファイルと、
    /// 空のまま残った新規フォルダーを削除する。
    /// </summary>
    private void CleanupPartialOutputs()
    {
      foreach (var pair in _pendingReplacements)
      {
        try
        {
          if (!File.Exists(pair.Key)) continue;
          File.SetAttributes(pair.Key, FileAttributes.Normal); // ReadOnly を写していても消せるように
          File.Delete(pair.Key);
        }
        catch (Exception) { /* 一時ファイルが残るだけで実データは壊していない */ }
      }
      _pendingReplacements.Clear();

      for (var i = _createdFiles.Count - 1; i >= 0; i--)
      {
        try
        {
          if (!File.Exists(_createdFiles[i])) continue;
          File.SetAttributes(_createdFiles[i], FileAttributes.Normal);
          File.Delete(_createdFiles[i]);
        }
        catch (Exception) { /* 消せないものは残す */ }
      }
      _createdFiles.Clear();

      // 深い階層から消す（中身が空のものだけ）
      for (var i = _createdDirs.Count - 1; i >= 0; i--)
      {
        try
        {
          if (Directory.Exists(_createdDirs[i]) &&
              Directory.GetFileSystemEntries(_createdDirs[i]).Length == 0)
          {
            Directory.Delete(_createdDirs[i]);
          }
        }
        catch (Exception) { /* 消せないものは残す */ }
      }
      _createdDirs.Clear();
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
      // 正規化したパスが保存先の**内側**かどうか。
      //
      // 旧実装は outFilePath.StartsWith(outDirPath) だった。outDirPath に末尾の区切り文字が
      // 無いため、保存先が "...\out" のときエントリ名 "..\outX\evil.txt" が
      // "...\outX\evil.txt" に解決され、前方一致を通過して**兄弟フォルダー**へ書けてしまう。
      // 末尾区切り・正規化・OrdinalIgnoreCase を PathSafety に集約した。
      if (!PathSafety.IsInsideDirectory(outFilePath, outDirPath))
      {
        ReturnCode = INVALID_FILE_PATH;
        ErrorFilePath = outFilePath;
        return false;
      }

      // 文字列としては内側でも、途中にジャンクションが挟まっていれば実体は外側になる。
      // Directory.CreateDirectory も FileStream もリンクを辿って書き込むため、
      // 前方一致だけでは防げない。暗号化側 (FileEncrypt5) は既に
      // FileAttributes.ReparsePoint を見てスキップしている。
      if (PathSafety.HasReparsePointBelow(outDirPath, outFilePath))
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
      // フォールバック用：例外から直接ファイル名が取れる型はそれを優先、
      // 取れなければ CurrentProcessingFilePath（直近で開こうとしたパス）を使う。
      string pathFromEx = null;
      if (ex is FileNotFoundException fnfEx) pathFromEx = fnfEx.FileName;
      else if (ex is FileLoadException flEx) pathFromEx = flEx.FileName;

      var fallbackPath = pathFromEx ?? CurrentProcessingFilePath ?? "";

      switch (ex)
      {
        case UnauthorizedAccessException _:
          ReturnCode = OS_DENIES_ACCESS;
          break;
        case DirectoryNotFoundException _:
          ReturnCode = DIRECTORY_NOT_FOUND;
          break;
        case DriveNotFoundException _:
          ReturnCode = DRIVE_NOT_FOUND;
          break;
        case FileLoadException _:
          ReturnCode = FILE_NOT_LOADED;
          break;
        case FileNotFoundException _:
          ReturnCode = FILE_NOT_FOUND;
          break;
        case PathTooLongException _:
          ReturnCode = PATH_TOO_LONG;
          break;
        // ヘッダーの値が構造的にありえない場合（サイズ・個数の範囲外など）。
        // IOException より前に判定すること（InvalidDataException は IOException の派生ではないが、
        // 意味としては「壊れたデータ」なので専用のリターンコードを割り当てる）。
        case InvalidDataException _:
          ReturnCode = ATC_BROKEN_DATA;
          break;
        case IOException _:
          ReturnCode = IO_EXCEPTION;
          break;
        default:
          if (ReturnCode == USER_CANCELED || ReturnCode == DECRYPT_SUCCEEDED)
            return;
          ReturnCode = ERROR_UNEXPECTED;
          break;
      }

      ErrorFilePath = fallbackPath;
      ErrorMessage = ex.Message;
    }

  } // end class FileDecrypt5

} // end namespace AttacheCase
