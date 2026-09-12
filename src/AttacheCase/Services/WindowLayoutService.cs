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

namespace AttacheCase.Services;

using System;
using System.IO;
using System.Text.Json;
using AttacheCase.Models;

public class WindowLayoutService : IWindowLayoutService
{
  private static readonly JsonSerializerOptions s_jsonOptions = new()
  {
    WriteIndented = true,
  };

  private readonly string _filePath;

  public WindowLayoutService(string appName = "AttacheCase")
  {
    var dir = GetSettingsDirectory(appName);
    Directory.CreateDirectory(dir);
    _filePath = Path.Combine(dir, "window-layout.json");
  }

  public WindowLayoutSettings Load()
  {
    if (!File.Exists(_filePath))
      return new WindowLayoutSettings();

    try
    {
      var json = File.ReadAllText(_filePath);
      return JsonSerializer.Deserialize<WindowLayoutSettings>(json, s_jsonOptions)
             ?? new WindowLayoutSettings();
    }
    catch
    {
      // ファイル破損時はデフォルトに戻す
      return new WindowLayoutSettings();
    }
  }

  public void Save(WindowLayoutSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);

    try
    {
      var json = JsonSerializer.Serialize(settings, s_jsonOptions);

      // 直接書くと、書き込み中に落ちたときにファイルが壊れて次回の復元に失敗する。
      // 一時ファイルに書いてから置き換える
      var tempPath = _filePath + ".tmp";
      File.WriteAllText(tempPath, json);
      File.Move(tempPath, _filePath, overwrite: true);
    }
    catch (Exception ex)
    {
      // ウィンドウ位置を保存できなかっただけで、終了処理を止める理由にはならない
      Console.Error.WriteLine($"Failed to save window layout: {ex.Message}");
    }
  }

  private static string GetSettingsDirectory(string appName)
  {
    if (OperatingSystem.IsMacOS())
    {
      var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
      return Path.Combine(home, "Library", "Application Support", appName);
    }

    // Windows: %APPDATA%\<appName>\
    // Linux:   ~/.config/<appName>/
    return Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
      appName);
  }
}