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
// 外部コマンドの実行。codesign / spctl / stapler / hdiutil を呼ぶために使う。
//
// 引数は配列で渡し、シェルを介さない。文字列に組み立ててシェルへ渡すと、
// パスに空白や引用符が含まれる場合に意図しない解釈をされる。
//----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AttacheCase.Services;

/// <summary>外部コマンドの実行結果。</summary>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;

    /// <summary>標準出力と標準エラーを合わせたもの。codesign は結果を標準エラーへ出す。</summary>
    public string CombinedOutput =>
        string.IsNullOrEmpty(StandardError) ? StandardOutput
        : string.IsNullOrEmpty(StandardOutput) ? StandardError
        : StandardOutput + "\n" + StandardError;
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = info };
        process.Start();

        // 先に読み出しを始める。待ってから読むと、出力がパイプを埋めて
        // 相手が書き込みで止まり、こちらも待ち続けることになる
        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);

            // 呼び出し側からの中止と、時間切れを取り違えない
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"コマンドが {timeout.TotalSeconds:0} 秒で終わりませんでした: {fileName}");
        }

        return new ProcessResult(
            process.ExitCode,
            await stdOutTask.ConfigureAwait(false),
            await stdErrTask.ConfigureAwait(false));
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // すでに終わっている場合など。ここで失敗しても続ける
        }
    }
}
