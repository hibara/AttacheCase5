//----------------------------------------------------------------------
// SPDX-License-Identifier: GPL-3.0-or-later OR LicenseRef-AttacheCase-Commercial
// Copyright (c) 2016-2026 HiBARA Software, LLC
// Dual-licensed (GPLv3+ / Commercial). Third-party components remain under their own licenses (see THIRD_PARTY_NOTICES.md).
// デュアルライセンス: GPLv3+ または商用ライセンス（詳細: DUAL-LICENSING.md / COMMERCIAL-LICENSE.md）
//----------------------------------------------------------------------
using System;
using System.IO;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace AttacheCase
{
  /// <summary>
  /// AES-256-GCM の暗号化を行うストリームラッパー。
  /// Write された平文をその場で逐次暗号化し、基底ストリームへ書き出す。
  /// Dispose 時に DoFinal が呼ばれ、認証タグ (16 バイト) が末尾に書き込まれる。
  ///
  /// 用途: 巨大ファイルの暗号化において、圧縮済みデータを丸ごとメモリ展開せず
  /// FileStream(出力) ← GcmEncryptingStream ← DeflateStream ← 入力 のパイプライン化を実現する。
  /// </summary>
  internal sealed class GcmEncryptingStream : Stream
  {
    private readonly Stream _baseStream;
    private readonly GcmBlockCipher _cipher;
    private readonly bool _leaveOpen;
    private readonly Func<long, bool> _progressCallback;
    private byte[] _outputBuffer;
    private long _totalProcessed;
    private bool _finalized;

    /// <summary>
    /// 新しい <see cref="GcmEncryptingStream"/> を生成する。
    /// </summary>
    /// <param name="baseStream">暗号文の書き込み先（通常は出力 FileStream）</param>
    /// <param name="key">AES-256 鍵（32 バイト）</param>
    /// <param name="nonce">Nonce（12 バイト）</param>
    /// <param name="associatedData">追加認証データ (AAD)。不要なら null</param>
    /// <param name="leaveOpen">true なら Dispose 時に基底ストリームを閉じない</param>
    /// <param name="progressCallback">
    /// 進捗コールバック。引数は処理済み平文バイト数。
    /// false を返すと <see cref="OperationCanceledException"/> がスローされる。
    /// </param>
    public GcmEncryptingStream(
      Stream baseStream, byte[] key, byte[] nonce,
      byte[] associatedData = null, bool leaveOpen = false,
      Func<long, bool> progressCallback = null)
    {
      _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
      _cipher = new GcmBlockCipher(new AesEngine());
      _cipher.Init(true, new AeadParameters(
        new KeyParameter(key), CryptoHelper5.AES_GCM_TAG_BITS, nonce, associatedData));
      _leaveOpen = leaveOpen;
      _progressCallback = progressCallback;
      _outputBuffer = new byte[8192 + CryptoHelper5.AES_GCM_TAG_SIZE];
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !_finalized;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
      get => throw new NotSupportedException();
      set => throw new NotSupportedException();
    }

    public override void Flush() => _baseStream.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
      if (_finalized) throw new InvalidOperationException("Stream has been finalized.");
      if (count <= 0) return;

      // 出力バッファサイズ確保（GCM は入力サイズ + タグ分の余裕が必要）
      var needed = count + CryptoHelper5.AES_GCM_TAG_SIZE;
      if (_outputBuffer.Length < needed)
        _outputBuffer = new byte[needed];

      var written = _cipher.ProcessBytes(buffer, offset, count, _outputBuffer, 0);
      if (written > 0)
        _baseStream.Write(_outputBuffer, 0, written);

      _totalProcessed += count;
      if (_progressCallback != null && !_progressCallback(_totalProcessed))
        throw new OperationCanceledException();
    }

    /// <summary>
    /// GCM の終端処理を実行し、認証タグ (16 バイト) を基底ストリームの末尾に書き出す。
    /// 通常は Dispose 時に自動的に呼ばれる。明示的に早期完了させたい場合のみ呼ぶ。
    /// </summary>
    public void FinalizeStream()
    {
      if (_finalized) return;
      _finalized = true;

      var finalSize = _cipher.GetOutputSize(0);
      if (_outputBuffer.Length < finalSize)
        _outputBuffer = new byte[finalSize];

      var finalLen = _cipher.DoFinal(_outputBuffer, 0);
      if (finalLen > 0)
        _baseStream.Write(_outputBuffer, 0, finalLen);
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        try
        {
          FinalizeStream();
        }
        finally
        {
          if (!_leaveOpen) _baseStream.Dispose();
          base.Dispose(disposing);
        }
      }
      else
      {
        base.Dispose(disposing);
      }
    }
  }

  /// <summary>
  /// AES-256-GCM の復号を行うストリームラッパー。
  /// 基底ストリームから暗号文を逐次読み込み、Read で平文を返す。
  /// 認証タグ (16 バイト) は基底ストリームの末尾に含まれており、自動的に検証される。
  ///
  /// 重要: BouncyCastle の GcmBlockCipher は復号モードで全入力（タグ含む）を ProcessBytes に
  /// 渡し、最後に DoFinal でタグ検証する。検証は <see cref="DrainAndVerify"/> で
  /// 明示的に呼ぶか、Read が EOF を返した時点で自動実行される。
  ///
  /// 用途: 巨大ファイルの復号において、復号済みデータを丸ごとメモリ展開せず
  /// FileStream(入力) → GcmDecryptingStream → DeflateStream → ExtractFiles のパイプライン化を実現する。
  /// </summary>
  internal sealed class GcmDecryptingStream : Stream
  {
    private readonly Stream _baseStream;
    private readonly GcmBlockCipher _cipher;
    private readonly bool _leaveOpen;
    private readonly Func<long, bool> _progressCallback;
    private readonly byte[] _inputBuffer;
    private byte[] _outputBuffer;
    private int _outputPos;
    private int _outputLen;
    private bool _inputEof;
    private bool _finalized;
    private long _totalProcessed;

    /// <summary>
    /// 新しい <see cref="GcmDecryptingStream"/> を生成する。
    /// </summary>
    /// <param name="baseStream">暗号文の読み込み元（通常は入力 FileStream、bodyNonce 直後にシーク済み）</param>
    /// <param name="key">AES-256 鍵（32 バイト）</param>
    /// <param name="nonce">Nonce（12 バイト）</param>
    /// <param name="associatedData">追加認証データ (AAD)。暗号化時と同じ値</param>
    /// <param name="leaveOpen">true なら Dispose 時に基底ストリームを閉じない</param>
    /// <param name="progressCallback">
    /// 進捗コールバック。引数は処理済み暗号文バイト数。
    /// false を返すと <see cref="OperationCanceledException"/> がスローされる。
    /// </param>
    public GcmDecryptingStream(
      Stream baseStream, byte[] key, byte[] nonce,
      byte[] associatedData = null, bool leaveOpen = false,
      Func<long, bool> progressCallback = null)
    {
      _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
      _cipher = new GcmBlockCipher(new AesEngine());
      _cipher.Init(false, new AeadParameters(
        new KeyParameter(key), CryptoHelper5.AES_GCM_TAG_BITS, nonce, associatedData));
      _leaveOpen = leaveOpen;
      _progressCallback = progressCallback;
      _inputBuffer = new byte[8192];
      _outputBuffer = new byte[8192 + CryptoHelper5.AES_GCM_TAG_SIZE];
    }

    public override bool CanRead => !_finalized || _outputPos < _outputLen;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
      get => throw new NotSupportedException();
      set => throw new NotSupportedException();
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
      if (count <= 0) return 0;

      var totalRead = 0;
      while (count > 0)
      {
        // 既に復号済みのバッファに残りがあればそれを返す
        if (_outputPos < _outputLen)
        {
          var avail = Math.Min(_outputLen - _outputPos, count);
          Buffer.BlockCopy(_outputBuffer, _outputPos, buffer, offset, avail);
          _outputPos += avail;
          offset += avail;
          count -= avail;
          totalRead += avail;
          continue;
        }

        // 完了済みならこれ以上返せない
        if (_finalized) break;

        // 入力 EOF に達していれば DoFinal で残りを取り出す
        if (_inputEof)
        {
          FinalizeStream();
          if (_outputLen > 0) continue;
          break;
        }

        // 基底ストリームから次のチャンクを読み込む
        var bytesRead = _baseStream.Read(_inputBuffer, 0, _inputBuffer.Length);
        if (bytesRead == 0)
        {
          _inputEof = true;
          continue;
        }

        // ProcessBytes 用の出力バッファサイズ確保
        var needed = bytesRead + CryptoHelper5.AES_GCM_TAG_SIZE;
        if (_outputBuffer.Length < needed)
          _outputBuffer = new byte[needed];

        _outputPos = 0;
        _outputLen = _cipher.ProcessBytes(_inputBuffer, 0, bytesRead, _outputBuffer, 0);

        _totalProcessed += bytesRead;
        if (_progressCallback != null && !_progressCallback(_totalProcessed))
          throw new OperationCanceledException();
      }
      return totalRead;
    }

    /// <summary>
    /// 基底ストリームの残りバイトをすべて読み込んで復号し、認証タグを検証する。
    ///
    /// DeflateStream は deflate 終端マーカーを検出した時点で読み取りを停止することがあり、
    /// その場合 GCM 認証タグ末尾まで読まれない。このメソッドを明示的に呼ぶことで、
    /// 必ずタグ検証が実行されることを保証する。
    /// </summary>
    /// <exception cref="InvalidCipherTextException">認証タグの検証に失敗した場合</exception>
    public void DrainAndVerify()
    {
      if (_finalized) return;

      // 残り暗号文を読み切る
      while (!_inputEof)
      {
        var bytesRead = _baseStream.Read(_inputBuffer, 0, _inputBuffer.Length);
        if (bytesRead == 0)
        {
          _inputEof = true;
          break;
        }

        var needed = bytesRead + CryptoHelper5.AES_GCM_TAG_SIZE;
        if (_outputBuffer.Length < needed)
          _outputBuffer = new byte[needed];

        // ここで出力された平文は破棄（呼び出し側が読まなかった末尾平文）
        _cipher.ProcessBytes(_inputBuffer, 0, bytesRead, _outputBuffer, 0);

        _totalProcessed += bytesRead;
        if (_progressCallback != null && !_progressCallback(_totalProcessed))
          throw new OperationCanceledException();
      }

      // DoFinal で認証タグ検証
      FinalizeStream();
    }

    /// <summary>
    /// GCM の終端処理を実行し、認証タグを検証する。
    /// 検証失敗時は <see cref="InvalidCipherTextException"/> がスローされる。
    /// </summary>
    /// <exception cref="InvalidCipherTextException">認証タグの検証に失敗した場合</exception>
    private void FinalizeStream()
    {
      if (_finalized) return;
      _finalized = true;

      var finalSize = _cipher.GetOutputSize(0);
      if (_outputBuffer.Length < finalSize)
        _outputBuffer = new byte[finalSize];

      _outputPos = 0;
      _outputLen = _cipher.DoFinal(_outputBuffer, 0);
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        // Dispose では認証検証を強制しない (呼び出し側が DrainAndVerify を呼ぶ責務)
        if (!_leaveOpen) _baseStream.Dispose();
        base.Dispose(disposing);
      }
      else
      {
        base.Dispose(disposing);
      }
    }
  }
}
