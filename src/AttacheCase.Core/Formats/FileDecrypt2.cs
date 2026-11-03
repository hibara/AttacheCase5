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
using System.Text;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Diagnostics;


namespace AttacheCase
{
  class FileDecrypt2
  {
    private struct FileListData
    {
      public string FilePath;
      public Int64 FileSize;
      public int FileAttribute;
      public DateTime LastWriteDateTime;
      public DateTime CreationDateTime;
    }

    // Status code
    private const int ENCRYPT_SUCCEEDED = 1; // Encrypt is succeeded.
    private const int DECRYPT_SUCCEEDED = 2; // Decrypt is succeeded.
    private const int DELETE_SUCCEEDED = 3; // Delete is succeeded.
    private const int READY_FOR_ENCRYPT = 4; // Getting ready for encryption or decryption.
    private const int READY_FOR_DECRYPT = 5; // Getting ready for encryption or decryption.
    private const int ENCRYPTING = 6; // Ecrypting.
    private const int DECRYPTING = 7; // Decrypting.
    private const int DELETING = 8; // Deleting.

    // Error code
    private const int USER_CANCELED = -1;   // User cancel.
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
    // 保存先に同名ファイル（上書きしない指定）。何も書き出さずに止める（終了コード 5）
    private const int OUTPUT_FILE_EXISTS = -118;

    // Overwrite Option
    //private const int USER_CANCELED = -1;
    private const int OVERWRITE = 1;
    private const int OVERWRITE_ALL = 2;
    private const int KEEP_NEWER = 3;
    private const int KEEP_NEWER_ALL = 4;
    // ---
    // Skip Option
    private const int SKIP = 5;
    private const int SKIP_ALL = 6;


    private const int BUFFER_SIZE = 4096; // compatible ver.2 data buffer

    // Header data variables
    private const char DATA_SUB_VERSION = (char)6;  //ver.2.00~ = "5", ver.2.70~ = "6"
    private const char PADDING_DATA = (char)0;
    private const string STRING_TOKEN_NORMAL = "_AttacheCaseData";
    private const string STRING_TOKEN_BROKEN = "_Atc_Broken_Data";
    private const char DATA_FILE_VERSION = (char)105;
    private const int TYPE_ALGORISM = 1;            // 1: Rijndael

    // Atc data size of self executable file
    private Int64 _ExeOutSize = 0;
    private Int64 _TotalSize = 0;
    //private Int64 _TotalFileSize = 0;

    // "U_" or "Fn_" is number of char
    int prefix;

    //----------------------------------------------------------------------
    // For thie file list after description, open associated with file or folder.
    private readonly List<string> _OutputFileList = new List<string>();
    public List<string> OutputFileList
    {
      get { return _OutputFileList; }
    }

    // Temporary option for overwriting ( 0: none, 1: Yes, 2:Overwrite all, 3: Overwrite for new date file )
    private int _TempOverWriteOption = -1;
    public int TempOverWriteOption
    {
      get { return this._TempOverWriteOption; }
      set { this._TempOverWriteOption = value; }
    }
    // Temporay option for overwriting for new date only.
    private bool _TempOverWriteForNewDate = false;
    public bool TempOverWriteForNewDate
    {
      get { return this._TempOverWriteForNewDate; }
      set { this._TempOverWriteForNewDate = value; }
    }

    // 処理した暗号化ファイルの数
    // The number of ATC files to be decrypted
    private int _NumberOfFiles = 0;
    public int NumberOfFiles
    {
      get { return this._NumberOfFiles; }
      set { this._NumberOfFiles = value; }
    }

    // 処理する暗号化ファイルの合計数
    // Total number of ATC files to be decrypted
    private int _TotalNumberOfFiles = 1;
    public int TotalNumberOfFiles
    {
      get { return this._TotalNumberOfFiles; }
      set { this._TotalNumberOfFiles = value; }
    }

    // 親フォルダーを生成しないか否か
    //Create no parent folder in decryption
    private bool _fNoParentFolder = false;
    public bool fNoParentFolder
    {
      get { return this._fNoParentFolder; }
      set { this._fNoParentFolder = value; }
    }

    // パスワード入力回数制限（読み取り専用）
    //Get limit of times to input password in encrypt files ( readonly ).
    private char _MissTypeLimits = (char)3;
    public char MissTypeLimits
    {
      get { return this._MissTypeLimits; }
    }

    //  一つずつ親フォルダーを確認、生成しながら復号する（サルベージモード）
    private bool _fSalvageToCreateParentFolderOneByOne = false;
    // Decrypt one by one while creating the parent folder ( Salvage mode ).
    public bool fSalvageToCreateParentFolderOneByOne
    {
      get { return this._fSalvageToCreateParentFolderOneByOne; }
      set { this._fSalvageToCreateParentFolderOneByOne = value; }
    }

    // すべてのファイルを同じ階層のディレクトリーに復号する（サルベージモード）
    private bool _fSalvageIntoSameDirectory = false;
    // Decrypt all files into the directory of the same hierarchy ( Salvage mode ). 
    public bool fSalvageIntoSameDirectory
    {
      get { return this._fSalvageIntoSameDirectory; }
      set { this._fSalvageIntoSameDirectory = value; }
    }

    // パスワード入力回数制限（読み取り専用）
    //Get limit of times to input password in encrypt files ( readonly ).
    private bool _fExecutableType = false;
    public bool fExecutableType
    {
      get { return this._fExecutableType; }
    }

    // ファイル、フォルダーのタイムスタンプを復号時に合わせる
    private bool _fSameTimeStamp = false;
    //Set the timestamp to decrypted files or directories
    public bool fSameTimeStamp
    {
      get { return this._fSameTimeStamp; }
      set { this._fSameTimeStamp = value; }
    }

    //----------------------------------------------------------------------
    // The return value of error ( ReadOnly)
    //----------------------------------------------------------------------
    // Input "Error code" for value
    private int _ReturnCode = 0;
    public int ReturnCode
    {
      get { return this._ReturnCode; }
    }
    // File path that caused the error
    private string _ErrorFilePath = "";
    public string ErrorFilePath
    {
      get { return this._ErrorFilePath; }
    }
    // Drive name to decrypt
    private string _DriveName = "";
    public string DriveName
    {
      get { return this._DriveName; }
    }
    // Total file size of files to be decrypted
    private Int64 _TotalFileSize = -1;
    public Int64 TotalFileSize
    {
      get { return this._TotalFileSize; }
    }
    // Free space on the drive to decrypt the file
    private Int64 _AvailableFreeSpace = -1;
    public Int64 AvailableFreeSpace
    {
      get { return this._AvailableFreeSpace; }
    }
    // Error message by the exception
    private string _ErrorMessage = "";
    public string ErrorMessage
    {
      get { return this._ErrorMessage; }
    }

    //----------------------------------------------------------------------
    // The plain text header data of encrypted file ( ReadOnly)
    //----------------------------------------------------------------------
    // Data Sub Version ( ver.2.00~ = "5", ver.2.70~ = "6" )
    private int _DataSebVersion = 0;
    public int DataSebVersion
    {
      get { return this._DataSebVersion; }
    }
    // reserved
    private byte[] _reserved;
    public byte[] reserved
    {
      get { return this._reserved; }
    }
    // The broken status of file
    private bool _fBroken = false;
    public bool fBroken
    {
      get { return this._fBroken; }
    }
    // Internal token string ( Whether or not the file is broken )  = "_AttacheCaseData" or "_Atc_Broken_Data"
    private string _TokenStr = "";
    public string TokenStr
    {
      get { return this._TokenStr; }
    }
    // Data File Version ( DATA_FILE_VERSION = 105 )
    private int _DataFileVersion = 0;
    public int DataFileVersion
    {
      get { return this._DataFileVersion; }
    }
    // TYPE_ALGORISM = 1:Rijndael	
    private int _TypeAlgorism = 0;
    public int TypeAlgorism
    {
      get { return this._TypeAlgorism; }
    }
    // The size of encrypted header data
    private int _AtcHeaderSize = 0;
    public int AtcHeaderSize
    {
      get { return this._AtcHeaderSize; }
    }

    private string _AtcFilePath = "";
    public string AtcFilePath
    {
      get { return this._AtcFilePath; }
    }

    private string _TempFilePath = "";
    public string TempFilePath
    {
      get { return this._TempFilePath; }
    }

    // Decryption time
    private string _DecryptionTimeString;
    public string DecryptionTimeString
    {
      get { return this._DecryptionTimeString; }
    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="FilePath"></param>
    public FileDecrypt2(string FilePath)
    {
      _AtcFilePath = FilePath;

      // _AttacheCaseData
      //byte[] AtcTokenByte = { 0x5F, 0x41, 0x74, 0x74, 0x61, 0x63, 0x68, 0x65, 0x43, 0x61, 0x73, 0x65, 0x44, 0x61, 0x74, 0x61};
      int[] AtcTokenByte = { 95, 65, 116, 116, 97, 99, 104, 101, 67, 97, 115, 101, 68, 97, 116, 97 };

      // _Atc_Broken_Data
      //byte[] AtcBrokenTokenByte = { 0x5F, 0x41, 0x74, 0x63, 0x5F, 0x42, 0x72, 0x6F, 0x6B, 0x65, 0x6E, 0x5F, 0x44, 0x61, 0x74, 0x61 };
      int[] AtcBrokenTokenByte = { 95, 65, 116, 99, 95, 66, 114, 111, 107, 101, 110, 95, 68, 97, 116, 97 };

      using (FileStream fs = new FileStream(_AtcFilePath, FileMode.Open, FileAccess.Read))
      {
        bool fToken = false;
        int b;
        while ((b = fs.ReadByte()) > -1)
        {
          //-----------------------------------
          // Check the token "_AttacheCaseData"
          if (b == AtcTokenByte[0])
          {
            fToken = true;
            for (int i = 1; i < AtcTokenByte.Length; i++)
            {
              if (fs.ReadByte() != AtcTokenByte[i])
              {
                fToken = false;
                break;
              }
            }
            if (fToken == true)
            {
              if (fs.Position > 20)
              { // Self executabel file
                _fExecutableType = true;
                _ExeOutSize = fs.Position - 20;
              }
              break;
            }
          }

          //-----------------------------------
          // Check the token "_AttacheCaseData"
          if (fToken == false && b == AtcBrokenTokenByte[0])
          {
            fToken = true;
            for (int i = 1; i < AtcBrokenTokenByte.Length; i++)
            {
              if (fs.ReadByte() != AtcBrokenTokenByte[i])
              {
                fToken = false;
                break;
              }
            }

            if (fToken == true)
            {
              if (fs.Position > 20)
              { // Self executabel file
                _fExecutableType = true;
                _fBroken = true;
                _ExeOutSize = fs.Position - 20;
              }
              break;
            }
          }

          //-----------------------------------
          if (fToken == true)
          {
            break;
          }
          //-----------------------------------

        }// end while();

#if (DEBUG)
        //string msg = fToken == true ? "fTokne: true" : "fTokne: false";
        //System.Windows.Forms.MessageBox.Show(msg);
#endif

        byte[] byteArray;

        if (fs.Length < 32)
        {
          return;
        }

        if (_fExecutableType == true)
        {
          fs.Seek(_ExeOutSize, SeekOrigin.Begin);
        }
        else
        {
          fs.Seek(0, SeekOrigin.Begin);
        }

        // Plain text header
        byteArray = new byte[1];
        fs.Read(byteArray, 0, 1);
        _DataSebVersion = (int)byteArray[0];                         // DataSubVersion
        byteArray = new byte[1];
        fs.Read(byteArray, 0, 1);                                    // reserved = NULL(0)
        byteArray = new byte[1];
        _reserved = byteArray;
        fs.Read(byteArray, 0, 1);
        _MissTypeLimits = (char)byteArray[0];                        // MissTypeLimits
        byteArray = new byte[1];
        fs.Read(byteArray, 0, 1);
        _fBroken = BitConverter.ToBoolean(byteArray, 0);             // fBroken
        byteArray = new byte[16];
        fs.Read(byteArray, 0, 16);
        _TokenStr = Encoding.ASCII.GetString(byteArray); // TokenStr（"_AttacheCaseData" or "_Atc_Broken_Data"）
        byteArray = new byte[4];
        fs.Read(byteArray, 0, 4);
        _DataFileVersion = BitConverter.ToInt32(byteArray, 0);       // DATA_FILE_VERSION = 105
        byteArray = new byte[4];
        fs.Read(byteArray, 0, 4);
        _TypeAlgorism = BitConverter.ToInt32(byteArray, 0);          // TYPE_ALGORISM = 1:Rijndael
        byteArray = new byte[4];
        fs.Read(byteArray, 0, 4);
        _AtcHeaderSize = BitConverter.ToInt32(byteArray, 0);         // AtcHeaderSize ( encrypted header data size )

#if (DEBUG)
        //System.Windows.Forms.MessageBox.Show("_TokenStr: " + _TokenStr);
#endif

      } // end using (FileStream fs = new FileStream(_AtcFilePath, FileMode.Open, FileAccess.Read));

    } // end public FileDecrypt2(string FilePath);

    /// <summary>
    /// The encrypted file by AES (exactly Rijndael) to the original file or folder by user's password.
    /// ユーザーが設定したパスワードによって、AES（正確にはRijndael）によって暗号化されたファイルを
    /// 元のファイル、またはフォルダーに復号して戻す。
    /// </summary>
    /// <param name="FilePath">File path or directory path is encrypted</param>
    /// <param name="OutFileDir">The directory of outputing encryption file.</param>
    /// <param name="Password">Encription password string</param>
    /// <param name="PasswordBinary">Encription password binary</param>
    /// <param name="dialog">int MessageCode, string Message text</param>
    /// <returns>Encryption success(true) or failed(false)</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:オブジェクトを複数回破棄しない")]
    public bool Decrypt(
      object sender, DoWorkEventArgs e,
      string FilePath, string OutDirPath, string Password, byte[] PasswordBinary,
      Action<int, string, IReadOnlyList<string>> dialogOverWrite, Action<string> dialogInvalidChar)
    {
      BackgroundWorker worker = sender as BackgroundWorker;
      worker.WorkerSupportsCancellation = true;

      //-----------------------------------
      // ストップウォッチ
      Stopwatch swDecrypt = new Stopwatch();
      swDecrypt.Start();

      //-----------------------------------
      // Header data is starting.
      // Progress event handler
      ArrayList MessageList = new ArrayList();
      MessageList.Add(READY_FOR_DECRYPT);
      MessageList.Add(Path.GetFileName(FilePath));
      worker.ReportProgress(0, MessageList);

      int len = 0;
      byte[] byteArray;

      byte[] bufferPassword;
      byte[] bufferKey = new byte[32];

      List<string> FileList = new List<string>();
      Dictionary<int, FileListData> dic = new Dictionary<int, FileListData>();

      bool fPasswordValid = false;

      if (_TokenStr.Trim() == "_AttacheCaseData")
      {
        // Atc data
      }
      else if (_TokenStr.Trim() == "_Atc_Broken_Data")
      {
        // Atc file is broken
        _ReturnCode = ATC_BROKEN_DATA;
        _ErrorFilePath = FilePath;
        return (false);
      }
      else
      {
        // not AttacheCase data
        _ReturnCode = NOT_ATC_DATA;
        _ErrorFilePath = FilePath;
        return (false);
      }

      try
      {
        //----------------------------------------------------------------------
        // ヘッダーサイズの検証
        // ファイル由来の値を無検証で new byte[] やオフセット計算に使わない。
        // 負値は例外、巨大値はメモリ確保の失敗になり、どちらも本来の
        // 「壊れたデータ」ではなく ERROR_UNEXPECTED として報告されてしまう
        //----------------------------------------------------------------------
        if (_AtcHeaderSize < 0 || _AtcHeaderSize > new FileInfo(FilePath).Length)
        {
          _ReturnCode = ATC_BROKEN_DATA;
          _ErrorFilePath = FilePath;
          return (false);
        }

        //----------------------------------------------------------------------
        // Check password
        //----------------------------------------------------------------------
        for (int i = 0; i < 2; i++)
        {
          using (FileStream fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
          {
            if (fs.Length < 32)
            {
              // not AttacheCase data
              _ReturnCode = NOT_ATC_DATA;
              _ErrorFilePath = FilePath;
              return (false);
            }

            if (_ExeOutSize > 0)
            {
              //自己実行可能形式
              fs.Seek(_ExeOutSize + 32, SeekOrigin.Begin);
            }
            else
            {
              fs.Seek(32, SeekOrigin.Begin);
            }

            // The Header of MemoryStream is encrypted
            using (LegacyRijndael aes = new LegacyRijndael())
            {
              aes.BlockSize = 256;             // BlockSize = 32 bytes
              aes.KeySize = 256;               // KeySize = 32 bytes
              aes.Mode = CipherMode.CBC;       // CBC mode
              aes.Padding = PaddingMode.Zeros; // Padding mode is "ZEROS".

              // Password
              if (PasswordBinary != null)
              { // Binary
                bufferPassword = PasswordBinary;
              }
              else
              { // Text
                if (i == 0)
                {
                  bufferPassword = Encoding.UTF8.GetBytes(Password);
                }
                else
                {
                  bufferPassword = Encoding.GetEncoding(932).GetBytes(Password);  // Shift-JIS
                }
              }

              // Password is 256 bit, so truncated up to 32 bytes or fill up the data size. 
              for (int c = 0; c < bufferKey.Length; c++)
              {
                if (c < bufferPassword.Length)
                {
                  // Cut down to 32 bytes characters.
                  bufferKey[c] = bufferPassword[c];
                }
                else
                {
                  bufferKey[c] = 0; // Zero filled
                }
              }
              aes.Key = bufferKey;

              // Initilization Vector
              byte[] iv = new byte[32];
              fs.Read(iv, 0, 32);
              aes.IV = iv;

              // Decryption interface.
              ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
              using (CryptoStream cse = new CryptoStream(fs, decryptor, CryptoStreamMode.Read))
              {
                using (MemoryStream ms = new MemoryStream())
                {
                  byteArray = new byte[_AtcHeaderSize];
                  // CryptoStream.Read は要求したバイト数より少なく返すことがある。1 回で全部
                  // 読めた前提にすると byteArray の末尾がゼロのままトークン判定とヘッダ解析に
                  // 渡り、静かに壊れる。全部読めるまでループする（FileDecrypt3 と同じ扱い）。
                  len = 0;
                  while (len < _AtcHeaderSize)
                  {
                    int ReadSize = cse.Read(byteArray, len, _AtcHeaderSize - len);
                    if (ReadSize == 0)
                    {
                      // 宣言されたヘッダ長ぶんのデータが存在しない
                      _ReturnCode = ATC_BROKEN_DATA;
                      _ErrorFilePath = FilePath;
                      return (false);
                    }
                    len += ReadSize;
                  }
                  ms.Write(byteArray, 0, len);

                  // Check Password Token
                  if (Encoding.UTF8.GetString(byteArray).IndexOf("Passcode:AttacheCase") > -1)
                  {
                    // Decryption is succeeded.
                    fPasswordValid = true;
                    break;
                  }
                }
              }

            }// end using (LegacyRijndael aes = new LegacyRijndael());

          }// end using (FileStream fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read));        

        }// end for (int i = 0; i < 2; i++);

        if (fPasswordValid == false)
        {
          _ReturnCode = PASSWORD_TOKEN_NOT_FOUND;
          _ErrorFilePath = FilePath;
          return (false);
        }

        //----------------------------------------------------------------------
        // Decrypt
        //----------------------------------------------------------------------
        using (FileStream fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
          if (_ExeOutSize > 0)
          {
            //自己実行可能形式
            fs.Seek(_ExeOutSize + 32, SeekOrigin.Begin);
          }
          else
          {
            fs.Seek(32, SeekOrigin.Begin);
          }

          // The Header of MemoryStream is encrypted
          using (LegacyRijndael aes = new LegacyRijndael())
          {
            aes.BlockSize = 256;             // BlockSize = 32 bytes
            aes.KeySize = 256;               // KeySize = 32 bytes
            aes.Mode = CipherMode.CBC;       // CBC mode
            aes.Padding = PaddingMode.Zeros; // Padding mode is "ZEROS".
            aes.Key = bufferKey;

            // Initilization Vector
            byte[] iv = new byte[32];
            fs.Read(iv, 0, 32);
            aes.IV = iv;

            // Decryption interface.
            ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using (CryptoStream cse = new CryptoStream(fs, decryptor, CryptoStreamMode.Read))
            {
              using (MemoryStream ms = new MemoryStream())
              {
                ms.Position = 0;

                byteArray = new byte[_AtcHeaderSize];
                // ここも 1 回の Read で全部読めた前提にしない（上と同じ理由）。
                len = 0;
                while (len < _AtcHeaderSize)
                {
                  int ReadSize = cse.Read(byteArray, len, _AtcHeaderSize - len);
                  if (ReadSize == 0)
                  {
                    // 宣言されたヘッダ長ぶんのデータが存在しない
                    _ReturnCode = ATC_BROKEN_DATA;
                    _ErrorFilePath = FilePath;
                    return (false);
                  }
                  len += ReadSize;
                }
                ms.Write(byteArray, 0, len);

                ms.Position = 0;
                var sr = new StreamReader(ms, Encoding.UTF8);
                string line;
                while ((line = sr.ReadLine()) != null)
                { //ver. 2.8.0～
                  if (Regex.IsMatch(line, @"^U_"))
                  {
                    FileList.Add(line);
                    prefix = 2;
                  }
                }

                // Old version ( Shift-JIS )
                if (FileList.Count == 0)
                {
                  ms.Position = 0;
                  sr = new StreamReader(ms, Encoding.GetEncoding("shift_jis"));
                  while ((line = sr.ReadLine()) != null)
                  {
                    if (Regex.IsMatch(line, @"^Fn_"))
                    {
                      FileList.Add(line);
                      prefix = 3;
                    }
                  }
                }

              }//end using (MemoryStream ms = new MemoryStream())

            }//end using (CryptoStream cse = new CryptoStream(fs, decryptor, CryptoStreamMode.Read));

          }//end using (LegacyRijndael aes = new LegacyRijndael());

        }//end using (FileStream fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read));


        //----------------------------------------------------------------------
        // Make a list array of the information for each file
        //----------------------------------------------------------------------
        _TotalFileSize = 0;
        string ParentFolder = "";
        foreach (var OutputLine in FileList)
        {
          Double LastWriteDate, LastWriteTime;
          Double CreateDate, CreateTime;
          DateTime LastWriteDateTime = DateTime.Parse("0001/01/01");
          DateTime CreationDateTime = DateTime.Parse("0001/01/01");

          FileListData fd = new FileListData();
          string[] OutputFileData = OutputLine.Split('\t');

          //-----------------------------------
          // U_0:sample.txt[\t]49657[\t]32[\t]734924[\t]38976000[\t]735756[\t]40300683
          // or
          // Fn_0:dummy.bin[\t]52426752[\t]32[\t]736214[\t]74714078[\t]736214[\t]74714078
          //
          // 行の形の検証。復号したヘッダーは（パスワードが合っていても）壊れて
          // いることがある。要素が足りない行をそのまま添字参照すると
          // IndexOutOfRangeException になり、ERROR_UNEXPECTED として報告されて
          // しまう。壊れたデータとして報告する
          string[] FilePathSplits = OutputFileData[0].Split(':');
          if (OutputFileData.Length < 7 || FilePathSplits.Length < 2)
          {
            _ReturnCode = ATC_BROKEN_DATA;
            _ErrorFilePath = FilePath;
            return (false);
          }

          int FileNum = 0;
          if (Int32.TryParse(FilePathSplits[0].Split(':')[0].Remove(0, prefix), out FileNum) == false)
          {
            FileNum = -1;
          }
          //-----------------------------------
          // File path
          if (_fNoParentFolder == true)
          {
            // root directory
            if (FileNum == 0)
            {
              ParentFolder = cleanPath(FilePathSplits[1], dialogInvalidChar);
            }
            // Other files or directries
            else
            {
              // 先頭の親フォルダー部分だけを取り除く。
              // String.Replace は全出現を置換するため、親フォルダー名が短い
              // （例: "a"）と、パスの途中に現れる同じ文字列まで消えて
              // 出力ファイル名が壊れる（FileDecrypt3 と同じプレフィックス限定に揃えた）
              if (ParentFolder.Length > 0 &&
                  FilePathSplits[1].StartsWith(ParentFolder, StringComparison.Ordinal))
              {
                FilePathSplits[1] = FilePathSplits[1].Substring(ParentFolder.Length);
              }
              FilePathSplits[1] = cleanPath(FilePathSplits[1], dialogInvalidChar);
            }
          }

          string OutFilePath = "";
          if (_fSalvageIntoSameDirectory == true) // Salvage mode
          {
            OutFilePath = Path.Combine(OutDirPath, Path.GetFileName(FilePathSplits[1]));
          }
          else
          {
            OutFilePath = Path.Combine(OutDirPath, FilePathSplits[1]);
          }

          //-----------------------------------
          // ディレクトリ・トラバーサル対策
          // Directory traversal countermeasures

          // 余計な ":" が含まれている
          // Extra ":" is included
          if (FilePathSplits.Length > 2)
          {
            _ReturnCode = INVALID_FILE_PATH;
            _ErrorFilePath = OutFilePath;
            return (false);
          }

          try
          {
            // ファイルパスを正規化
            // Canonicalize file path.
            OutFilePath = Path.GetFullPath(OutFilePath);
          }
          catch
          {
            _ReturnCode = INVALID_FILE_PATH;
            _ErrorFilePath = OutFilePath;
            return (false);
          }

          // 正規化したパスが保存先の**内側**かどうか
          // Whether the canonicalized path is inside the save destination
          //
          // 旧実装は OutFilePath.StartsWith(OutDirPath) だった。OutDirPath に末尾の区切り文字が
          // 無いため、保存先が "...\out" のときエントリ名 "..\outX\evil.txt" が
          // "...\outX\evil.txt" に解決され、前方一致を通過して**兄弟フォルダー**へ書けてしまう。
          // 末尾区切り・正規化・OrdinalIgnoreCase を PathSafety に集約した。
          if (PathSafety.IsInsideDirectory(OutFilePath, OutDirPath))
          {
            fd.FilePath = OutFilePath;
          }
          else
          {
            _ReturnCode = INVALID_FILE_PATH;
            _ErrorFilePath = OutFilePath;
            return (false);
          }

          //-----------------------------------
          // File size
          if (Int64.TryParse(OutputFileData[1], out fd.FileSize) == false)
          {
            fd.FileSize = -1;
          }
          else
          {
            // 負のサイズ・合計のあふれは壊れたデータとして拒否する
            //（負の rest は書き込みで例外になり ERROR_UNEXPECTED に化ける。
            // あふれた合計は容量チェックをすり抜ける）
            if (fd.FileSize < 0 || _TotalFileSize > Int64.MaxValue - fd.FileSize)
            {
              _ReturnCode = ATC_BROKEN_DATA;
              _ErrorFilePath = FilePath;
              return (false);
            }
            _TotalFileSize += fd.FileSize;
          }
          //-----------------------------------
          // File attribute
          if (Int32.TryParse(OutputFileData[2], out fd.FileAttribute) == false)
          {
            fd.FileAttribute = -1;
          }

          /*
					  * TTimeStamp = record
					  *  Time: Integer;      { Number of milliseconds since midnight }
					  *  Date: Integer;      { One plus number of days since 1/1/0001 }
					  * end;
				  */
          //-----------------------------------
          // Last update timestamp
          try
          {
            if (_fSameTimeStamp == false && Double.TryParse(OutputFileData[3], out LastWriteDate) == true)
            {
              LastWriteDateTime = LastWriteDateTime.AddDays(LastWriteDate - 1); // Add days
            }
            else
            {
              LastWriteDateTime = DateTime.Now;
            }

            if (_fSameTimeStamp == false && Double.TryParse(OutputFileData[4], out LastWriteTime) == true)
            {
              LastWriteDateTime = LastWriteDateTime.AddMilliseconds(LastWriteTime).AddHours(9);
            }
            else
            {
              LastWriteDateTime = DateTime.Now;
            }
          }
          catch
          {
            LastWriteDateTime = DateTime.Now;
          }

          fd.LastWriteDateTime = LastWriteDateTime;

          //-----------------------------------
          // Create datetime
          try
          {
            if (_fSameTimeStamp == false && Double.TryParse(OutputFileData[5], out CreateDate) == true)
            {
              CreationDateTime = CreationDateTime.AddDays(CreateDate - 1);
            }
            else
            {
              CreationDateTime = DateTime.Now;
            }
            if (_fSameTimeStamp == false && Double.TryParse(OutputFileData[6], out CreateTime) == true)
            {
              CreationDateTime = CreationDateTime.AddMilliseconds(CreateTime).AddHours(9);
            }
            else
            {
              CreationDateTime = DateTime.Now;
            }
          }
          catch
          {
            CreationDateTime = DateTime.Now;
          }

          fd.CreationDateTime = CreationDateTime;

          //-----------------------------------
          // Insert to 'Key-Value' type array data.
          // 負の番号・重複番号は壊れたヘッダー。dic.Add の ArgumentException が
          // ERROR_UNEXPECTED に化ける前に、壊れたデータとして報告する
          if (FileNum < 0 || dic.ContainsKey(FileNum))
          {
            _ReturnCode = ATC_BROKEN_DATA;
            _ErrorFilePath = FilePath;
            return (false);
          }
          dic.Add(FileNum, fd);

        } // end foreach (var OutputLine in FileList);

        //----------------------------------------------------------------------
        // Generate a temporary file in the case of self-executable file.
        if (_fExecutableType == true)
        {
          // 自己実行形式の場合はテンポラリファイルを生成する
          string TempFileName = Path.ChangeExtension(Path.GetRandomFileName(), ".atc");
          _TempFilePath = Path.Combine(Path.GetTempPath(), TempFileName);

          File.Copy(FilePath, _TempFilePath);
          FilePath = _TempFilePath;

          //すべての属性を解除
          File.SetAttributes(FilePath, FileAttributes.Normal);

          using (FileStream fs = new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
          {
            // Delete the storage size at the end of the file
            fs.SetLength(fs.Length - sizeof(Int64));
          }
        }

        //----------------------------------------------------------------------
        // 上書きしない指定（SKIP_ALL ＝ 確認ダイアログを出せない実行での
        // 「上書きを確認する」）のとき、保存先に同名ファイルが 1 つでもあれば、
        // 何も書き出さずに止める。黙って飛ばすと「復号成功」に見えてしまう
        // （暗号化側と同じ終了コード 5。v4 / v5 の DecryptionService と同じ規則）
        //----------------------------------------------------------------------
        if (_TempOverWriteOption == SKIP_ALL && _fSalvageIntoSameDirectory == false)
        {
          foreach (FileListData item in dic.Values)
          {
            if (item.FilePath.EndsWith("\\") == true) continue;
            string existingPath = Path.Combine(OutDirPath, item.FilePath);
            if (File.Exists(existingPath) == true)
            {
              _ReturnCode = OUTPUT_FILE_EXISTS;
              _ErrorFilePath = existingPath;
              return (false);
            }
          }
        }

        //----------------------------------------------------------------------
        // Check the disk space
        //----------------------------------------------------------------------
        string RootDriveLetter = Path.GetPathRoot(OutDirPath).Substring(0, 1);

        if (RootDriveLetter == "\\")
        {
          // Network
        }
        else
        {
          DriveInfo drive = new DriveInfo(RootDriveLetter);

          DriveType driveType = drive.DriveType;
          switch (driveType)
          {
            case DriveType.CDRom:
            case DriveType.NoRootDirectory:
            case DriveType.Unknown:
              break;
            case DriveType.Fixed:     // Local Drive
            case DriveType.Network:   // Mapped Drive
            case DriveType.Ram:       // Ram Drive
            case DriveType.Removable: // Usually a USB Drive

              // The drive is not available, or not enough free space.
              if (drive.IsReady == false || drive.AvailableFreeSpace < _TotalFileSize)
              {
                _ReturnCode = NO_DISK_SPACE;
                _DriveName = drive.ToString();
                //_TotalFileSize = _TotalFileSize;
                _AvailableFreeSpace = drive.AvailableFreeSpace;
                return (false);
              }
              break;
          }
        }

        //-----------------------------------
        // Decrypt file main data.
        //-----------------------------------
        using (FileStream fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
          int mod = _AtcHeaderSize % 32;

          if (_fExecutableType == true)
          {
            // ExeOutSize + Header info data + IV of header + Atc header size
            // int で足すと AtcHeaderSize が大きいときにあふれる
            long val = 32L + 32 + AtcHeaderSize + mod;
            fs.Seek(_ExeOutSize + val, SeekOrigin.Begin);
          }
          else
          {
            // Header info data + IV of header + Atc Header data
            long val = 32L + 32 + AtcHeaderSize + mod;
            fs.Seek(val, SeekOrigin.Begin);
          }

          using (LegacyRijndael aes = new LegacyRijndael())
          {
            aes.BlockSize = 256;             // BlockSize = 32bytes
            aes.KeySize = 256;               // KeySize = 32bytes
            aes.Mode = CipherMode.CBC;       // CBC mode
            aes.Padding = PaddingMode.Zeros; // Padding mode
            aes.Key = bufferKey;
            byte[] iv = new byte[32];        // Initilization Vector
            fs.Read(iv, 0, 32);
            aes.IV = iv;

            //Decryption interface.
            ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using (CryptoStream cse = new CryptoStream(fs, decryptor, CryptoStreamMode.Read))
            {
              // zlib ヘッダー先頭 2 バイト（CMF, FLG）を読み飛ばす。
              // EOF（-1）なら、そもそも本体が無い壊れたデータ
              if (cse.ReadByte() < 0 || cse.ReadByte() < 0)
              {
                _ReturnCode = ATC_BROKEN_DATA;
                _ErrorFilePath = FilePath;
                return (false);
              }

              using (Ionic.Zlib.DeflateStream ds = new Ionic.Zlib.DeflateStream(cse, Ionic.Zlib.CompressionMode.Decompress, Ionic.Zlib.CompressionLevel.BestCompression))
              {
                /*
                public struct FileListData
                {
                  public string FilePath;
                  public Int64 FileSize;
                  public int FileAttribute;
                  public DateTime LastWriteDateTime;
                  public DateTime CreationDateTime;
                }
                */
                FileStream outfs = null;
                // 実際に書き込んだ出力先。サルベージ時は連番が付くため
                // dic[FileIndex].FilePath とは一致しない（FileDecrypt3 と同じ）。
                string currentFilePath = null;
                Int64 FileSize = 0;
                int FileIndex = 0;

                bool fSkip = false;

                if (_fNoParentFolder == true)
                {
                  // 壊れたヘッダーで番号 0 が無いと dic[0] が KeyNotFoundException になる
                  if (dic.ContainsKey(0) == false)
                  {
                    _ReturnCode = FILE_INDEX_NOT_FOUND;
                    return (false);
                  }
                  if (dic[0].FilePath.EndsWith("\\") == true)
                  {
                    FileIndex = 1;  // Ignore parent folder.
                  }
                }

                //----------------------------------------------------------------------
                bool fDataFound = false;
                byteArray = new byte[BUFFER_SIZE];
                while ((len = ds.Read(byteArray, 0, BUFFER_SIZE)) > 0)
                {
                  int buffer_size = len;
                  while (len > 0)
                  {
                    fDataFound = true;
                    //----------------------------------------------------------------------
                    // If there is no file or folder under writing, make it
                    //----------------------------------------------------------------------
                    if (outfs == null)
                    {
                      //-----------------------------------
                      // Create file or dirctories.
                      if (dic.ContainsKey(FileIndex) == false)
                      {
                        if (FileIndex > dic.Count - 1)
                        {
                          _ReturnCode = DECRYPT_SUCCEEDED;
                          return (true);
                        }
                        else
                        {
                          // FileIndex
                          _ReturnCode = FILE_INDEX_NOT_FOUND;
                          return (false);
                        }
                      }
                      else
                      {
                        //-----------------------------------
                        // Create directory
                        //-----------------------------------
                        if (dic[FileIndex].FilePath.EndsWith("\\") == true)
                        {
                          string path = Path.Combine(OutDirPath, dic[FileIndex].FilePath);
                          //path = cleanPath(path, dialogInvalidChar);

                          // 保存先の配下に置かれたシンボリックリンク／ジャンクションを
                          // 経由すると、文字列上は内側でも実体は外側に作られる。
                          // 作る直前に検査する（FileDecrypt5 と同じ防御）
                          if (PathSafety.HasReparsePointBelow(OutDirPath, path))
                          {
                            _ReturnCode = INVALID_FILE_PATH;
                            _ErrorFilePath = path;
                            return (false);
                          }

                          DirectoryInfo di = new DirectoryInfo(path);

                          // File already exists.
                          if (Directory.Exists(path) == true)
                          {
                            // Temporary option for overwriting
                            // private const int USER_CANCELED  = -1;
                            // private const int OVERWRITE      = 1;
                            // private const int OVERWRITE_ALL  = 2;
                            // private const int KEEP_NEWER     = 3;
                            // private const int KEEP_NEWER_ALL = 4;
                            // private const int SKIP           = 5;
                            // private const int SKIP_ALL       = 6;

                            if (_TempOverWriteOption == OVERWRITE_ALL)
                            {
                              // Overwrite ( New create )
                            }
                            else if (_TempOverWriteOption == SKIP_ALL)
                            {
                              fSkip = true;
                            }
                            else if (_TempOverWriteOption == KEEP_NEWER_ALL)
                            {
                              if (di.LastWriteTime > dic[FileIndex].LastWriteDateTime)
                              {
                                fSkip = true; // old directory
                              }
                            }
                            else
                            {
                              // Show dialog of comfirming to overwrite. 
                              dialogOverWrite(0, path, null);

                              // Cancel
                              if (_TempOverWriteOption == USER_CANCELED)
                              {
                                _ReturnCode = USER_CANCELED;
                                return (false);
                              }
                              else if (_TempOverWriteOption == OVERWRITE || _TempOverWriteOption == OVERWRITE_ALL)
                              {
                                // Overwrite ( New create )
                              }
                              // Skip, or Skip All
                              else if (_TempOverWriteOption == SKIP || _TempOverWriteOption == SKIP_ALL)
                              {
                                fSkip = true;
                              }
                              else if (_TempOverWriteOption == KEEP_NEWER || _TempOverWriteOption == KEEP_NEWER_ALL)
                              { // New file?
                                if (di.LastWriteTime > dic[FileIndex].LastWriteDateTime)
                                {
                                  fSkip = true;
                                }
                              }
                            }

                            if (fSkip == false)
                            {
                              //隠し属性を削除する
                              di.Attributes &= ~FileAttributes.Hidden;
                              //読み取り専用を削除
                              di.Attributes &= ~FileAttributes.ReadOnly;
                            }

                          } // end if (TempOverWriteOption == OVERWRITE_ALL);

                          Directory.CreateDirectory(dic[FileIndex].FilePath);
                          _OutputFileList.Add(dic[FileIndex].FilePath);
                          FileSize = 0;
                          FileIndex++;
                          continue;

                        }
                        //-----------------------------------
                        // Create file
                        //-----------------------------------
                        else
                        {
                          string path = Path.Combine(OutDirPath, dic[FileIndex].FilePath);
                          //path = cleanPath(path, dialogInvalidChar);

                          // 書き込む直前の再解析ポイント検査（上のフォルダー生成と同じ）
                          if (PathSafety.HasReparsePointBelow(OutDirPath, path))
                          {
                            _ReturnCode = INVALID_FILE_PATH;
                            _ErrorFilePath = path;
                            return (false);
                          }

                          FileInfo fi = new FileInfo(path);

                          // File already exists.
                          if (File.Exists(path) == true)
                          {
                            // Salvage Data Mode
                            if (_fSalvageIntoSameDirectory == true)
                            {
                              string BasePath = path;
                              int SerialNum = 0;
                              while (File.Exists(path) == true)
                              {
                                path = getFileNameWithSerialNumber(BasePath, SerialNum);
                                SerialNum++;
                              }

                            }
                            else
                            {
                              // Temporary option for overwriting
                              // private const int USER_CANCELED  = -1;
                              // private const int OVERWRITE      = 1;
                              // private const int OVERWRITE_ALL  = 2;
                              // private const int KEEP_NEWER     = 3;
                              // private const int KEEP_NEWER_ALL = 4;
                              // private const int SKIP           = 5;
                              // private const int SKIP_ALL       = 6;
                              if (_TempOverWriteOption == OVERWRITE_ALL)
                              {
                                // Overwrite ( New create )
                              }
                              else if (_TempOverWriteOption == SKIP_ALL)
                              {
                                fSkip = true;
                              }
                              else if (_TempOverWriteOption == KEEP_NEWER_ALL)
                              {
                                if (fi.LastWriteTime > dic[FileIndex].LastWriteDateTime)
                                {
                                  fSkip = true;
                                }
                              }
                              else
                              {
                                // Show dialog of comfirming to overwrite. 
                                dialogOverWrite(0, path, null);

                                // Cancel
                                if (_TempOverWriteOption == USER_CANCELED)
                                {
                                  _ReturnCode = USER_CANCELED;
                                  return (false);
                                }
                                else if (_TempOverWriteOption == OVERWRITE || _TempOverWriteOption == OVERWRITE_ALL)
                                {
                                  // Overwrite ( New create )
                                }
                                // Skip, or Skip All
                                else if (_TempOverWriteOption == SKIP || _TempOverWriteOption == SKIP_ALL)
                                {
                                  fSkip = true;
                                }
                                else if (_TempOverWriteOption == KEEP_NEWER || _TempOverWriteOption == KEEP_NEWER_ALL)
                                { // New file?
                                  if (fi.LastWriteTime > dic[FileIndex].LastWriteDateTime)
                                  {
                                    fSkip = true; // old directory
                                  }
                                }
                              }

                              //隠し属性を削除する
                              //fi.Attributes &= ~FileAttributes.Hidden;
                              //読み取り専用を削除
                              //fi.Attributes &= ~FileAttributes.ReadOnly;

                              //上書きする場合だけ、すべての属性を解除する
                              //（スキップ・新しい方を保持と決めたファイルの属性を変えない）
                              if (fSkip == false)
                              {
                                File.SetAttributes(path, FileAttributes.Normal);
                              }

                            }

                          } // end if ( File.Exists );

                          // Salvage data mode
                          // サルベージ・モード
                          if (_fSalvageToCreateParentFolderOneByOne == true)
                          {
                            // Decrypt one by one while creating the parent folder.
                            Directory.CreateDirectory(Path.GetDirectoryName(path));
                          }

                          if (fSkip == false)
                          { // ここで、"OpenOrCreate"を使用するとなぜか同じファイルと復号されない。
                            // Use the "OpenOrCreate" here, then they are not decrypted to the same file.
                            outfs = new FileStream(path, FileMode.Create, FileAccess.Write);
                          }
                          else
                          {
                            outfs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write);
                          }

                          currentFilePath = path;
                          _OutputFileList.Add(path);
                          FileSize = 0;

                        }
                      }

                    }// end if (outfs == null);

                    //----------------------------------------------------------------------
                    //　Write data
                    //----------------------------------------------------------------------
                    if (FileSize + len < (Int64)dic[FileIndex].FileSize)
                    {
                      if (outfs != null || fSkip == true)
                      {
                        //まだまだ書き込める
                        if (fSkip == false)
                        {
                          outfs.Write(byteArray, buffer_size - len, len);
                        }
                        FileSize += len;
                        _TotalSize += len;
                        len = 0;
                      }
                    }
                    else
                    {
                      // ファイルの境界を超えて読み込んでいる
                      int rest = (int)(dic[FileIndex].FileSize - FileSize);

                      if (fSkip == false)
                      {
                        //書き込み完了
                        outfs.Write(byteArray, buffer_size - len, rest);
                      }

                      _TotalSize += rest;

                      len -= rest;

                      if (outfs != null)
                      {
                        //生成したファイルを閉じる
                        outfs.Close();
                        outfs = null;
                      }

                      if (fSkip == false && currentFilePath != null)
                      {
                        // 実際に書き込んだパスへ復元する。サルベージの連番リネーム時、
                        // dic[FileIndex].FilePath は書き込んでいない元の名前を指す
                        //（存在すれば無関係なファイルを書き換え、無ければ例外になる）
                        FileInfo fi = new FileInfo(currentFilePath);
                        try
                        {
                          // タイムスタンプなどの復元
                          fi.CreationTime = (DateTime)dic[FileIndex].CreationDateTime;
                          fi.LastWriteTime = (DateTime)dic[FileIndex].LastWriteDateTime;
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                          // 書庫の日時が OS の範囲外。タイムスタンプは復元しない
                        }
                        // ファイル属性の復元（解析に失敗した -1 は全ビットが立った
                        // 値になり ArgumentException で復号全体が止まるため飛ばす）
                        if (dic[FileIndex].FileAttribute >= 0)
                        {
                          fi.Attributes = (FileAttributes)dic[FileIndex].FileAttribute;
                        }
                      }

                      FileSize = 0;
                      FileIndex++;

                      fSkip = false;

                    }

                    //----------------------------------------------------------------------
                    //進捗の表示
                    string MessageText = "";
                    if (_TotalNumberOfFiles > 1)
                    {
                      MessageText = _AtcFilePath + " ( " + _NumberOfFiles.ToString() + "/" + _TotalNumberOfFiles.ToString() + " files" + " )";
                    }
                    else
                    {
                      MessageText = _AtcFilePath;
                    }

                    MessageList = new ArrayList();
                    MessageList.Add(DECRYPTING);
                    MessageList.Add(MessageText);
                    float percent = ((float)_TotalSize / _TotalFileSize);

                    System.Random r = new System.Random();
                    if (r.Next(0, 20) == 4)
                    {
                      worker.ReportProgress((int)(percent * 10000), MessageList);
                    }

                    // User cancel
                    if (worker.CancellationPending == true)
                    {
                      if (outfs != null)
                      {
                        outfs.Close();
                      }
                      _ReturnCode = USER_CANCELED;
                      return (false);
                    }

                  }// end while(len > 0);

                }// end while ((len = ds.Read(byteArray, 0, BUFFER_SIZE)) > 0);

                // 書き込み途中のファイルが残ったままストリームが尽きた =
                // 宣言サイズ分のデータが無い切り詰められた書庫。
                // 部分的なファイルを閉じただけで成功にしない
                bool fIncompleteEntry = (outfs != null);

                if (outfs != null)
                {
                  outfs.Close();
                }

                // Data is not read at all.
                if (fDataFound == false)
                {
                  _ReturnCode = DATA_NOT_FOUND;
                  return (false);
                }

                if (fIncompleteEntry == true)
                {
                  _ReturnCode = ATC_BROKEN_DATA;
                  _ErrorFilePath = FilePath;
                  return (false);
                }

              }// end using (DeflateStream ds = new DeflateStream(cse, CompressionMode.Decompress));

            }// end using (CryptoStream cse = new CryptoStream(fs, decryptor, CryptoStreamMode.Read));

          }// end using (LegacyRijndael aes = new LegacyRijndael());

        }// end using (FileStream fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read));

        _ReturnCode = DECRYPT_SUCCEEDED;
        return (true);
      }
      catch (UnauthorizedAccessException)
      {
        //オペレーティング システムが I/O エラーまたは特定の種類のセキュリティエラーのためにアクセスを拒否する場合、スローされる例外
        //The exception that is thrown when the operating system denies access
        //because of an I/O error or a specific type of security error.
        _ReturnCode = OS_DENIES_ACCESS;
        _ErrorFilePath = _AtcFilePath;
        return (false);

      }
      catch (DirectoryNotFoundException ex)
      {
        //ファイルまたはディレクトリの一部が見つからない場合にスローされる例外
        //The exception that is thrown when part of a file or directory cannot be found
        _ReturnCode = DIRECTORY_NOT_FOUND;
        _ErrorMessage = ex.Message;
        return (false);
      }
      catch (DriveNotFoundException ex)
      {
        //使用できないドライブまたは共有にアクセスしようとするとスローされる例外
        //The exception that is thrown when trying to access a drive or share that is not available
        _ReturnCode = DRIVE_NOT_FOUND;
        _ErrorMessage = ex.Message;
        return (false);
      }
      catch (FileLoadException ex)
      {
        //マネージド アセンブリが見つかったが、読み込むことができない場合にスローされる例外
        //The exception that is thrown when a managed assembly is found but cannot be loaded
        _ReturnCode = FILE_NOT_LOADED;
        _ErrorFilePath = ex.FileName;
        return (false);
      }
      catch (FileNotFoundException ex)
      {
        //ディスク上に存在しないファイルにアクセスしようとして失敗したときにスローされる例外
        //The exception that is thrown when an attempt to access a file that does not exist on disk fails
        _ReturnCode = FILE_NOT_FOUND;
        _ErrorFilePath = ex.FileName;
        return (false);
      }
      catch (PathTooLongException)
      {
        //パス名または完全修飾ファイル名がシステム定義の最大長を超えている場合にスローされる例外
        //The exception that is thrown when a path or fully qualified file name is longer than the system-defined maximum length
        _ReturnCode = PATH_TOO_LONG;
        return (false);
      }
      catch (IOException ex)
      {
        //I/Oエラーが発生したときにスローされる例外。現在の例外を説明するメッセージを取得します。
        //The exception that is thrown when an I/O error occurs. Gets a message that describes the current exception.
        _ReturnCode = IO_EXCEPTION;
        _ErrorMessage = ex.Message;
        return (false);
      }
      catch (CryptographicException ex)
      {
        //復号器が拒否した（暗号文の末尾がブロック境界に揃わない等）。
        //切り詰め・改変されたデータであり、想定外エラーとは区別して報告する
        _ReturnCode = CRYPTOGRAPHIC_EXCEPTION;
        _ErrorFilePath = _AtcFilePath;
        _ErrorMessage = ex.Message;
        return (false);
      }
      catch (Exception ex)
      {
        _ReturnCode = ERROR_UNEXPECTED;
        _ErrorMessage = ex.Message;
        return (false);
      }
      finally
      {
        // Delete temporary file
        // finally からの例外は catch で選んだ戻り値と _ReturnCode を壊すため、ここで止める
        try
        {
          if (File.Exists(_TempFilePath) == true)
          {
            File.Delete(_TempFilePath);
          }
        }
        catch (Exception)
        {
          // 後始末の失敗は無視する（テンポラリは OS の掃除に任せる）
        }

        swDecrypt.Stop();
        TimeSpan ts = swDecrypt.Elapsed;
        _DecryptionTimeString =
          Convert.ToString(ts.Hours) + "h" + Convert.ToString(ts.Minutes) + "m" +
          Convert.ToString(ts.Seconds) + "s" + Convert.ToString(ts.Milliseconds) + "ms";
      }

    }// end Decrypt2();

    //======================================================================
    // パスに無効な文字列がある場合は、指定された文字列に置き換えられます。
    // When there is an invalid character string in the path, it replaces with the specified character string. 
    //======================================================================
    private string cleanPath(string toCleanPath, Action<string> dialogInvalidChar)
    {
      string[] pathParts = toCleanPath.Split(new char[] { '\\' });
      for (int i = 0; i < pathParts.Length; i++)
      {
        foreach (char badChar in Path.GetInvalidFileNameChars())
        {
          pathParts[i] = pathParts[i].Replace(badChar.ToString(), "="); // Replace string.
        }
      }

      string ResuletPath = String.Join(@"\", pathParts);

      if (ResuletPath != toCleanPath)
      {
        dialogInvalidChar(toCleanPath);
      }

      return ResuletPath;

    }

    /// <summary>
    /// ファイル名に連番を振る
    /// Put a serial number to the file name
    /// </summary>
    /// <param name="FilePath"></param>
    /// <returns></returns>
    private string getFileNameWithSerialNumber(string FilePath, int SerialNum)
    {
      string DirPath = Path.GetDirectoryName(FilePath);
      string FileName = Path.GetFileNameWithoutExtension(FilePath) + SerialNum.ToString("0000") + Path.GetExtension(FilePath);

      return Path.Combine(DirPath, FileName);

    }

  }//end class FileDecrypt2;

}//end namespace AttacheCase;
