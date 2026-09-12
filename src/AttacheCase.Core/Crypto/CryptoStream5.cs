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
// Mac 版（Avalonia）への移植。Windows 版と完全に同じバイト列を生成・受理する。
//----------------------------------------------------------------------

using System;
using System.IO;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace AttacheCase.Crypto;

/// <summary>
/// AES-256-GCM の暗号化を行うストリームラッパー。
/// Write された平文をその場で逐次暗号化し、基底ストリームへ書き出す。
/// Dispose 時に DoFinal が呼ばれ、認証タグ (16 バイト) が末尾に書き込まれる。
/// </summary>
internal sealed class GcmEncryptingStream : Stream
{
    private readonly Stream _baseStream;
    private readonly GcmBlockCipher _cipher;
    private readonly bool _leaveOpen;
    private readonly Func<long, bool>? _progressCallback;
    private byte[] _outputBuffer;
    private long _totalProcessed;
    private bool _finalized;

    public GcmEncryptingStream(
        Stream baseStream, byte[] key, byte[] nonce,
        byte[]? associatedData = null, bool leaveOpen = false,
        Func<long, bool>? progressCallback = null)
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

        var needed = count + CryptoHelper5.AES_GCM_TAG_SIZE;
        if (_outputBuffer.Length < needed) _outputBuffer = new byte[needed];

        var written = _cipher.ProcessBytes(buffer, offset, count, _outputBuffer, 0);
        if (written > 0) _baseStream.Write(_outputBuffer, 0, written);

        _totalProcessed += count;
        if (_progressCallback != null && !_progressCallback(_totalProcessed))
            throw new OperationCanceledException();
    }

    public void FinalizeStream()
    {
        if (_finalized) return;
        _finalized = true;

        var finalSize = _cipher.GetOutputSize(0);
        if (_outputBuffer.Length < finalSize) _outputBuffer = new byte[finalSize];

        var finalLen = _cipher.DoFinal(_outputBuffer, 0);
        if (finalLen > 0) _baseStream.Write(_outputBuffer, 0, finalLen);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { FinalizeStream(); }
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
/// 認証タグ (16 バイト) は基底ストリームの末尾に含まれており、
/// <see cref="DrainAndVerify"/> または EOF 到達時に自動検証される。
/// </summary>
internal sealed class GcmDecryptingStream : Stream
{
    private readonly Stream _baseStream;
    private readonly GcmBlockCipher _cipher;
    private readonly bool _leaveOpen;
    private readonly Func<long, bool>? _progressCallback;
    private readonly byte[] _inputBuffer;
    private byte[] _outputBuffer;
    private int _outputPos;
    private int _outputLen;
    private bool _inputEof;
    private bool _finalized;
    private long _totalProcessed;

    public GcmDecryptingStream(
        Stream baseStream, byte[] key, byte[] nonce,
        byte[]? associatedData = null, bool leaveOpen = false,
        Func<long, bool>? progressCallback = null)
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

            if (_finalized) break;

            if (_inputEof)
            {
                FinalizeStream();
                if (_outputLen > 0) continue;
                break;
            }

            var bytesRead = _baseStream.Read(_inputBuffer, 0, _inputBuffer.Length);
            if (bytesRead == 0)
            {
                _inputEof = true;
                continue;
            }

            var needed = bytesRead + CryptoHelper5.AES_GCM_TAG_SIZE;
            if (_outputBuffer.Length < needed) _outputBuffer = new byte[needed];

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
    /// DeflateStream は終端マーカーで読み取りを停止することがあるため、
    /// 確実にタグ検証を走らせるには明示的にこのメソッドを呼ぶ。
    /// </summary>
    public void DrainAndVerify()
    {
        if (_finalized) return;

        while (!_inputEof)
        {
            var bytesRead = _baseStream.Read(_inputBuffer, 0, _inputBuffer.Length);
            if (bytesRead == 0)
            {
                _inputEof = true;
                break;
            }
            var needed = bytesRead + CryptoHelper5.AES_GCM_TAG_SIZE;
            if (_outputBuffer.Length < needed) _outputBuffer = new byte[needed];
            _cipher.ProcessBytes(_inputBuffer, 0, bytesRead, _outputBuffer, 0);

            _totalProcessed += bytesRead;
            if (_progressCallback != null && !_progressCallback(_totalProcessed))
                throw new OperationCanceledException();
        }

        FinalizeStream();
    }

    private void FinalizeStream()
    {
        if (_finalized) return;
        _finalized = true;

        var finalSize = _cipher.GetOutputSize(0);
        if (_outputBuffer.Length < finalSize) _outputBuffer = new byte[finalSize];

        _outputPos = 0;
        _outputLen = _cipher.DoFinal(_outputBuffer, 0);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (!_leaveOpen) _baseStream.Dispose();
            base.Dispose(disposing);
        }
        else
        {
            base.Dispose(disposing);
        }
    }
}
