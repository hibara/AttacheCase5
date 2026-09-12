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
// 旧形式（v105 / v130）復号のための Rijndael 256 ビットブロック CBC。
//
// AttacheCase 2 / 3 は AES ではなく「ブロック長 256 ビットの Rijndael」を
// 使っていた。.NET Framework の RijndaelManaged は BlockSize=256 を受けたが、
// .NET (Core) 系の実装は 128 固定で、設定した時点で
// PlatformNotSupportedException を投げる（.NET 10 で実測）。
// そこで Bouncy Castle の RijndaelEngine で同じ変換を提供する。
//
// FileDecrypt2 / FileDecrypt3 の移植差分を最小にするため、
// RijndaelManaged と同じ書き味（Key / IV / CreateDecryptor）を持つ
// スタンドインとして実装している。復号専用。
//
// PaddingMode.Zeros 相当: 復号時にパディングは取り除かない
// （呼び出し側は索引に記録された正確なバイト数だけを読む）。
//----------------------------------------------------------------------

using System;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace AttacheCase
{
  /// <summary>
  /// RijndaelManaged（BlockSize=256）互換の復号器ファクトリ。
  /// </summary>
  internal sealed class LegacyRijndael : IDisposable
  {
    private int _blockSize = 256;
    private int _keySize = 256;

    /// <summary>ブロック長（ビット）。旧形式は 256 のみ</summary>
    public int BlockSize
    {
      get => _blockSize;
      set
      {
        if (value != 256) throw new CryptographicException("LegacyRijndael はブロック長 256 ビット専用です: " + value);
        _blockSize = value;
      }
    }

    /// <summary>鍵長（ビット）。旧形式は 256 のみ</summary>
    public int KeySize
    {
      get => _keySize;
      set
      {
        if (value != 256) throw new CryptographicException("LegacyRijndael は鍵長 256 ビット専用です: " + value);
        _keySize = value;
      }
    }

    /// <summary>CBC のみ（旧形式の固定仕様）</summary>
    public CipherMode Mode
    {
      get => CipherMode.CBC;
      set { if (value != CipherMode.CBC) throw new CryptographicException("LegacyRijndael は CBC 専用です"); }
    }

    /// <summary>Zeros のみ（復号時は何も取り除かない）</summary>
    public PaddingMode Padding
    {
      get => PaddingMode.Zeros;
      set { if (value != PaddingMode.Zeros) throw new CryptographicException("LegacyRijndael は Zeros パディング専用です"); }
    }

    public byte[] Key { get; set; } = Array.Empty<byte>();
    public byte[] IV { get; set; } = Array.Empty<byte>();

    public ICryptoTransform CreateDecryptor(byte[] key, byte[] iv)
      => new Rijndael256CbcDecryptTransform(key, iv);

    public void Dispose()
    {
      // 鍵材料は呼び出し側（FileDecrypt2/3）が使い回すため、ここでは消去しない
    }
  }

  /// <summary>
  /// ブロック長 256 ビットの Rijndael CBC 復号（パディング除去なし）。
  /// CryptoStream（Read モード）から使われる。
  /// </summary>
  internal sealed class Rijndael256CbcDecryptTransform : ICryptoTransform
  {
    private const int BLOCK_BYTES = 32;

    private readonly CbcBlockCipher _cipher;

    public Rijndael256CbcDecryptTransform(byte[] key, byte[] iv)
    {
      ArgumentNullException.ThrowIfNull(key);
      ArgumentNullException.ThrowIfNull(iv);
      if (key.Length != BLOCK_BYTES) throw new CryptographicException("鍵は 32 バイトであること: " + key.Length);
      if (iv.Length != BLOCK_BYTES) throw new CryptographicException("IV は 32 バイトであること: " + iv.Length);

      _cipher = new CbcBlockCipher(new RijndaelEngine(256));
      _cipher.Init(forEncryption: false, new ParametersWithIV(new KeyParameter(key), iv));
    }

    public int InputBlockSize => BLOCK_BYTES;
    public int OutputBlockSize => BLOCK_BYTES;
    public bool CanTransformMultipleBlocks => true;
    public bool CanReuseTransform => false;

    public int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount,
                              byte[] outputBuffer, int outputOffset)
    {
      if (inputCount % BLOCK_BYTES != 0)
        throw new CryptographicException("入力がブロック境界に揃っていません: " + inputCount);

      for (var done = 0; done < inputCount; done += BLOCK_BYTES)
      {
        _cipher.ProcessBlock(inputBuffer, inputOffset + done, outputBuffer, outputOffset + done);
      }

      return inputCount;
    }

    public byte[] TransformFinalBlock(byte[] inputBuffer, int inputOffset, int inputCount)
    {
      // Zeros パディングの復号は何も取り除かない。完全なブロックだけを処理する。
      // 暗号文は必ずブロック長の倍数なので、端数が来るのはデータの破損
      if (inputCount % BLOCK_BYTES != 0)
        throw new CryptographicException("暗号文の長さがブロック境界に揃っていません: " + inputCount);

      var output = new byte[inputCount];
      TransformBlock(inputBuffer, inputOffset, inputCount, output, 0);
      return output;
    }

    public void Dispose()
    {
      _cipher.Reset();
    }
  }
}
