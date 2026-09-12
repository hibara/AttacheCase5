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
// 落としてきた配布物の検証。
//
// マニフェストの署名（Ed25519）と SHA-256 だけでは足りない。それらが保証するのは
// 「配信元が意図したファイルを、途中で変えられずに受け取った」ことまでで、
// 配信元そのものが乗っ取られた場合は通ってしまう。
// 実行するファイルについては Authenticode まで見る。
//
// ★ macOS 版の UpdatePackageVerifier に対応するもの。
//   あちらは codesign / spctl / TeamIdentifier を見る。Windows では
//   WinVerifyTrust（＝エクスプローラーの「デジタル署名」タブと同じ判定）と、
//   署名者が動作中のアプリと同じかどうかを見る。
//
//   ブラウザ経由で落とさないので SmartScreen が働かない。検証はこちらの責任になる。
//----------------------------------------------------------------------
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace AttacheCase.Platform.Windows
{
  /// <summary>検証に失敗した理由。</summary>
  public enum WindowsVerificationFailure
  {
    None,

    /// <summary>ファイルが無い、または読めない。</summary>
    FileNotFound,

    /// <summary>署名されていない。</summary>
    NotSigned,

    /// <summary>署名が壊れている、または信頼できない。</summary>
    SignatureInvalid,

    /// <summary>署名者が動作中のアプリと違う。</summary>
    SignerMismatch,

    /// <summary>動作中のアプリの署名者を読めなかった（比較の基準が取れない）。</summary>
    SignerUnavailable,
  }

  /// <summary>検証の結果。</summary>
  public sealed class WindowsVerificationResult
  {
    public bool Ok { get; }
    public WindowsVerificationFailure Failure { get; }
    public string Detail { get; }

    private WindowsVerificationResult(bool ok, WindowsVerificationFailure failure, string detail)
    {
      Ok = ok;
      Failure = failure;
      Detail = detail ?? "";
    }

    public static WindowsVerificationResult Success() =>
      new WindowsVerificationResult(true, WindowsVerificationFailure.None, "");

    public static WindowsVerificationResult Fail(WindowsVerificationFailure failure, string detail = "") =>
      new WindowsVerificationResult(false, failure, detail);
  }

  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  public class WindowsPackageVerifier
  {
    // WinVerifyTrust
    private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 =
      new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_WHOLECHAIN = 1;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;
    private const uint WTD_SAFER_FLAG = 0x00000100;

    private const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
    private const int TRUST_E_BAD_DIGEST = unchecked((int)0x80096010);
    private const int TRUST_E_EXPLICIT_DISTRUST = unchecked((int)0x800B0111);
    private const int CERT_E_UNTRUSTEDROOT = unchecked((int)0x800B0109);
    private const int CERT_E_CHAINING = unchecked((int)0x800B010A);

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, IntPtr pWVTData);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_FILE_INFO
    {
      public uint cbStruct;
      [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
      public IntPtr hFile;
      public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
      public uint cbStruct;
      public IntPtr pPolicyCallbackData;
      public IntPtr pSIPClientData;
      public uint dwUIChoice;
      public uint fdwRevocationChecks;
      public uint dwUnionChoice;
      public IntPtr pFile;
      public uint dwStateAction;
      public IntPtr hWVTStateData;
      [MarshalAs(UnmanagedType.LPWStr)] public string pwszURLReference;
      public uint dwProvFlags;
      public uint dwUIContext;
      public IntPtr pSignatureSettings;
    }

    /// <summary>
    /// 動作中のアプリの署名者名を返す。読めなければ <c>null</c>。
    /// </summary>
    /// <remarks>
    /// 期待する署名者を定数で持たず、実行中の自分から読む。
    /// こうしておくと証明書を更新しても追随でき、書き換え忘れで
    /// 更新が止まることがない（macOS 版が TeamIdentifier を実物から取るのと同じ考え）。
    /// </remarks>
    public virtual string GetRunningAppSigner()
    {
      try
      {
        // Assembly.Location は単一ファイル発行では空文字列になる。
        // ProcessPath なら常に実行中の exe（署名が付いている実体）を指す
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path)) return null;
        return GetSignerSubject(path);
      }
      catch (Exception)
      {
        return null;
      }
    }

    /// <summary>
    /// 署名者の Subject を取り出す。署名されていなければ <c>null</c>。
    /// </summary>
    /// <remarks>
    /// <c>X509Certificate.CreateFromSignedFile</c> は<b>署名の妥当性を検証しない</b>。
    /// 「誰が署名したことになっているか」を読むだけなので、
    /// 必ず <see cref="VerifyAuthenticode"/> と組みで使うこと。
    /// </remarks>
    internal static string GetSignerSubject(string filePath)
    {
      try
      {
        using (var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath)))
        {
          return cert.Subject;
        }
      }
      catch (Exception)
      {
        return null;
      }
    }

    /// <summary>
    /// Authenticode を検証する。エクスプローラーの「デジタル署名」タブと同じ判定。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>WINTRUST_DATA</c> は末尾に <c>pSignatureSettings</c> を持つ新しい定義で、
    /// <c>cbStruct</c> にもその大きさを入れている。<b>この形は Windows 8 以降の
    /// wintrust.dll が前提</b>で、KB3033929 を当てていない Windows 7 SP1 では
    /// 引数エラーで弾かれる可能性がある。
    /// </para>
    /// <para>
    /// 旧定義へ切り替える分岐は<b>あえて入れていない</b>。
    /// AttacheCase 5 は <c>app.manifest</c> で Windows 10 / 11 のみを宣言しており、
    /// さらに配信側の <c>minimumSystemVersion</c>（現在 10.0.19041）を
    /// <c>UpdateService.Evaluate</c> が先に見て、満たさない環境には更新を
    /// 出さない。つまりこの関数へ到達する時点で Windows 10 以降であり、
    /// 試せない OS 向けの経路を持つほうが害が大きい。
    /// </para>
    /// </remarks>
    /// <returns>成功なら 0。失敗なら HRESULT。</returns>
    internal static int VerifyAuthenticode(string filePath)
    {
      var fileInfo = new WINTRUST_FILE_INFO
      {
        cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)),
        pcwszFilePath = filePath,
        hFile = IntPtr.Zero,
        pgKnownSubject = IntPtr.Zero,
      };

      var pFile = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
      var pData = IntPtr.Zero;

      try
      {
        Marshal.StructureToPtr(fileInfo, pFile, false);

        var data = new WINTRUST_DATA
        {
          cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA)),
          pPolicyCallbackData = IntPtr.Zero,
          pSIPClientData = IntPtr.Zero,
          // 無人で確認する。ダイアログを出させない
          dwUIChoice = WTD_UI_NONE,
          // 失効まで見る。証明書が失効させられた版を掴ませない
          fdwRevocationChecks = WTD_REVOKE_WHOLECHAIN,
          dwUnionChoice = WTD_CHOICE_FILE,
          pFile = pFile,
          dwStateAction = WTD_STATEACTION_VERIFY,
          hWVTStateData = IntPtr.Zero,
          pwszURLReference = null,
          dwProvFlags = WTD_SAFER_FLAG,
          dwUIContext = 0,
          pSignatureSettings = IntPtr.Zero,
        };

        pData = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_DATA)));
        Marshal.StructureToPtr(data, pData, false);

        var result = WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, pData);

        // 検証で確保された状態を必ず閉じる
        var closing = (WINTRUST_DATA)Marshal.PtrToStructure(pData, typeof(WINTRUST_DATA));
        closing.dwStateAction = WTD_STATEACTION_CLOSE;
        Marshal.StructureToPtr(closing, pData, false);
        WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, pData);

        return result;
      }
      finally
      {
        if (pData != IntPtr.Zero) Marshal.FreeHGlobal(pData);
        Marshal.FreeHGlobal(pFile);
      }
    }

    /// <summary>
    /// 実行するファイルを検証する。
    /// </summary>
    /// <param name="filePath">落としてきたファイル。</param>
    /// <param name="expectedSigner">
    /// 期待する署名者の Subject。<see cref="GetRunningAppSigner"/> の値を渡す。
    /// </param>
    public virtual WindowsVerificationResult Verify(string filePath, string expectedSigner)
    {
      if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
      {
        return WindowsVerificationResult.Fail(WindowsVerificationFailure.FileNotFound, filePath ?? "");
      }

      if (string.IsNullOrEmpty(expectedSigner))
      {
        // 比較の基準が無いまま通すと、署名を見ている意味が無くなる
        return WindowsVerificationResult.Fail(WindowsVerificationFailure.SignerUnavailable);
      }

      // 先に「署名ブロブが在るか」を見る。
      //
      // HRESULT だけで「署名なし」と「改ざん」を分けようとしてはいけない。
      // 実測（2026-09-05）では、署名済みインストーラの本体を 1 ビット反転させても
      // TRUST_E_BAD_DIGEST ではなく TRUST_E_NOSIGNATURE (0x800B0100) が返った。
      // 壊れたことで署名の位置を辿れなくなるためと思われる。
      // HRESULT を素直に文言へ写すと、改ざんされたファイルに対して
      // 「デジタル署名がありません」と出て、利用者を誤解させる。
      //
      // GetSignerSubject は署名の妥当性を見ずにブロブを読むだけなので、
      // 「署名は付いているが検証に通らない」＝改ざん・失効などを切り分けられる。
      var signer = GetSignerSubject(filePath);
      var hasSignatureBlob = !string.IsNullOrEmpty(signer);

      var hr = VerifyAuthenticode(filePath);
      if (hr != 0)
      {
        var failure = hasSignatureBlob
          ? WindowsVerificationFailure.SignatureInvalid
          : WindowsVerificationFailure.NotSigned;

        return WindowsVerificationResult.Fail(failure, DescribeFailure(failure, hr));
      }

      if (!hasSignatureBlob)
      {
        // WinVerifyTrust は通ったのにブロブが読めない。想定していない状態なので通さない
        return WindowsVerificationResult.Fail(WindowsVerificationFailure.NotSigned);
      }

      if (!string.Equals(signer, expectedSigner, StringComparison.Ordinal))
      {
        return WindowsVerificationResult.Fail(
          WindowsVerificationFailure.SignerMismatch,
          string.Format("期待: {0}\n実際: {1}", expectedSigner, signer));
      }

      return WindowsVerificationResult.Success();
    }

    /// <summary>
    /// 失敗の理由を、そのまま利用者に見せられる文にする。
    /// </summary>
    /// <remarks>
    /// 署名ブロブが在るのに検証を通らない場合、HRESULT が
    /// <c>TRUST_E_NOSIGNATURE</c> でも「署名がありません」とは言わない。
    /// 実際には改ざんされているのに、そう伝えると誤解を招くため。
    /// </remarks>
    internal static string DescribeFailure(WindowsVerificationFailure failure, int hr)
    {
      if (failure == WindowsVerificationFailure.SignatureInvalid && hr == TRUST_E_NOSIGNATURE)
      {
        return "署名が壊れています。ファイルが改変された可能性があります。";
      }

      return DescribeHResult(hr);
    }

    internal static string DescribeHResult(int hr)
    {
      switch (hr)
      {
        case TRUST_E_NOSIGNATURE:
          return "デジタル署名がありません。";
        case TRUST_E_BAD_DIGEST:
          return "署名後にファイルが変更されています。";
        case TRUST_E_EXPLICIT_DISTRUST:
          return "この署名は信頼しないと明示されています。";
        case CERT_E_UNTRUSTEDROOT:
          return "証明書の発行元が信頼されていません。";
        case CERT_E_CHAINING:
          return "証明書の連鎖を検証できません。";
        default:
          return string.Format("署名を検証できません（0x{0:X8}）。", hr);
      }
    }
  }
}
