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

using System.Collections.Generic;

namespace AttacheCase.Models;

public class FileListResult
{
  public bool Success { get; set; }
  public List<string> FilePaths { get; set; } = new();
  public string? ErrorMessage { get; set; }
  public int ErrorCode { get; set; }

  public static FileListResult CreateSuccess(List<string> filePaths)
  {
    return new FileListResult { Success = true, FilePaths = filePaths };
  }

  public static FileListResult CreateError(int errorCode, string message)
  {
    return new FileListResult { Success = false, ErrorCode = errorCode, ErrorMessage = message };
  }
}