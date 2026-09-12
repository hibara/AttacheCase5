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
using System.Threading;
using System.Threading.Tasks;
using AttacheCase.Models;

namespace AttacheCase.Services;

public interface IDecryptionService
{
  Task<EncryptionResult> DecryptAsync(
    string inputPath,
    string outputDirectory,
    string password,
    DecryptionOptions options,
    IProgress<EncryptionProgress>? progress = null,
    CancellationToken cancellationToken = default);

  Task<FileListResult> GetFileListAsync(
    string inputPath,
    string password,
    CancellationToken cancellationToken = default);

  // RSAモード対応のオーバーロード
  Task<FileListResult> GetFileListAsync(
    string inputPath,
    string password,
    DecryptionOptions options,
    CancellationToken cancellationToken = default);
}