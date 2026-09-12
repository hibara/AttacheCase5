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
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using AttacheCase.ViewModels;

namespace AttacheCase;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// </summary>
[RequiresUnreferencedCode(
  "Default implementation of ViewLocator involves reflection which may be trimmed away.",
  Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
  public Control? Build(object? param)
  {
    if (param is null)
      return null;

    var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
    var type = Type.GetType(name);

    if (type != null)
    {
      return (Control)Activator.CreateInstance(type)!;
    }

    return new TextBlock { Text = "Not Found: " + name };
  }

  public bool Match(object? data)
  {
    return data is ViewModelBase;
  }
}