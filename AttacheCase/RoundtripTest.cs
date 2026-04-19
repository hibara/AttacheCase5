// 一時テスト: atc5トークン削除後の暗号化→復号 往復テスト
// ビルド後に実行: AttacheCase.exe --roundtrip-test
#if DEBUG
using System;
using System.Collections;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;

namespace AttacheCase
{
  internal static class RoundtripTest
  {
    /// <summary>
    /// --roundtrip-test 引数で呼び出される往復テスト
    /// </summary>
    public static int Run()
    {
      var testDir = Path.Combine(Path.GetTempPath(), "AttacheCase5_RoundtripTest");
      if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
      Directory.CreateDirectory(testDir);

      var plainFile = Path.Combine(testDir, "testfile.txt");
      var atcFile = Path.Combine(testDir, "testfile.txt.atc");
      var decryptDir = Path.Combine(testDir, "decrypted");

      try
      {
        // テストファイル作成
        var originalContent = "Hello AttacheCase5 - Token removal test - " + DateTime.Now;
        File.WriteAllText(plainFile, originalContent);
        Console.WriteLine("=== Original ===");
        Console.WriteLine(originalContent);

        // --- 暗号化 ---
        Console.WriteLine("\n=== Encrypting ===");
        var enc = new FileEncrypt5();
        enc.CompressionOption = 1;

        var worker = new BackgroundWorker();
        worker.WorkerReportsProgress = true;
        var doWorkArgs = new DoWorkEventArgs("");

        bool encResult = enc.Encrypt(
          worker, doWorkArgs,
          new[] { plainFile }, atcFile,
          "testpass123", null, "", CompressionLevel.Fastest);

        if (encResult)
        {
          Console.WriteLine("OK: Encryption succeeded. Size=" + new FileInfo(atcFile).Length + " bytes");
        }
        else
        {
          Console.WriteLine("FAIL: Encryption failed! ReturnCode=" + enc.ReturnCode + " Error=" + enc.ErrorMessage);
          return 1;
        }

        // --- 正しいパスワードで復号 ---
        Console.WriteLine("\n=== Decrypting (correct password) ===");
        Directory.CreateDirectory(decryptDir);
        var dec = new FileDecrypt5(atcFile);
        Console.WriteLine("DataFileVersion=" + dec.DataFileVersion + " EncryptionMode=" + dec.EncryptionMode);

        var doWorkArgs2 = new DoWorkEventArgs("");
        bool decResult = dec.Decrypt(
          worker, doWorkArgs2, atcFile, decryptDir,
          "testpass123", null,
          (code, msg, list) => { Console.WriteLine("  Dialog: code=" + code + " msg=" + msg); });

        if (decResult)
        {
          Console.WriteLine("OK: Decryption succeeded!");
          var files = Directory.GetFiles(decryptDir, "*", SearchOption.AllDirectories);
          if (files.Length > 0)
          {
            var decryptedContent = File.ReadAllText(files[0]);
            Console.WriteLine("\n=== Decrypted ===");
            Console.WriteLine(decryptedContent);
            if (originalContent.Trim() == decryptedContent.Trim())
              Console.WriteLine("\n>>> PASS: Content matches!");
            else
            {
              Console.WriteLine("\n>>> FAIL: Content mismatch!");
              return 1;
            }
          }
          else
          {
            Console.WriteLine("FAIL: No decrypted file found!");
            return 1;
          }
        }
        else
        {
          Console.WriteLine("FAIL: Decryption failed! ReturnCode=" + dec.ReturnCode + " Error=" + dec.ErrorMessage);
          return 1;
        }

        // --- 間違いパスワードで復号（GCM認証失敗を確認） ---
        Console.WriteLine("\n=== Decrypting (wrong password) ===");
        var dec2 = new FileDecrypt5(atcFile);
        var doWorkArgs3 = new DoWorkEventArgs("");
        bool decResult2 = dec2.Decrypt(
          worker, doWorkArgs3, atcFile, decryptDir,
          "wrongpassword", null,
          (code, msg, list) => { });

        if (!decResult2)
          Console.WriteLine("OK: Wrong password correctly rejected. ReturnCode=" + dec2.ReturnCode);
        else
        {
          Console.WriteLine("FAIL: Wrong password was accepted!");
          return 1;
        }

        Console.WriteLine("\n=== ALL TESTS PASSED ===");
        return 0;
      }
      finally
      {
        // クリーンアップ
        try { if (Directory.Exists(testDir)) Directory.Delete(testDir, true); } catch { }
      }
    }
  }
}
#endif
