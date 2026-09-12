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
// 設定「アタッシェケースを複数起動しない」の実現。
//
// Windows 版は名前付き Mutex ＋ WM_COPYDATA で「2 つ目の起動で渡された引数を
// 1 つ目へ転送する」を実現している。macOS には両方とも無いので、
//   - 排他制御 … ロックファイル（.NET は Unix で FileShare.None を flock(2) で実装）
//   - 引数の転送 … Unix ドメインソケット
// で置き換えている。ソケットは ~/Library/Application Support/AttacheCase/ 配下に置くため、
// 他ユーザーから接続されることはない。
//----------------------------------------------------------------------

using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AttacheCase.Services;

public sealed class SingleInstanceService : IDisposable
{
    /// <summary>転送を受け付けたときの応答</summary>
    private const string ResponseAccepted = "ok";

    /// <summary>処理中などで受け付けられなかったときの応答</summary>
    private const string ResponseBusy = "busy";

    private const int MaxMessageBytes = 1024 * 1024;

    /// <summary>ロックを取得できたインスタンス（1 つ目のプロセス）だけが持つ</summary>
    public static SingleInstanceService? Current { get; private set; }

    private readonly string _lockPath;
    private readonly string _socketPath;

    private FileStream? _lock;
    private Socket? _listener;
    private CancellationTokenSource? _cts;

    public SingleInstanceService(string directory)
    {
        _lockPath = Path.Combine(directory, "instance.lock");
        _socketPath = Path.Combine(directory, "instance.sock");
    }

    public string SocketPath => _socketPath;

    /// <summary>
    /// 1 つ目のプロセスとしてロックを取得する。
    /// すでに別のプロセスが起動していれば false。
    /// </summary>
    public bool TryAcquire()
    {
        try
        {
            var directory = Path.GetDirectoryName(_lockPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            _lock = new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            Current = this;
            return true;
        }
        catch (IOException)
        {
            // すでに別のプロセスが握っている
            return false;
        }
        catch (Exception)
        {
            // ロックの仕組みが使えない環境では、起動を妨げない
            Current = this;
            return true;
        }
    }

    /// <summary>
    /// 2 つ目以降のプロセスから渡された引数を受け取れるようにする。
    /// </summary>
    /// <param name="onArgumentsReceived">
    /// 受け取った引数を処理する。受け付けられたら true を返すこと（false なら送信側に「処理中」と伝える）。
    /// </param>
    public void StartListening(Func<string[], bool> onArgumentsReceived)
    {
        try
        {
            // 前回異常終了した場合に残っているソケットを片付ける
            // （ロックを取れている＝他に起動していないので、消して問題ない）
            if (File.Exists(_socketPath)) File.Delete(_socketPath);

            _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
            _listener.Listen(4);

            if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(_socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            _cts = new CancellationTokenSource();
            _ = Task.Run(() => AcceptLoopAsync(onArgumentsReceived, _cts.Token));
        }
        catch (Exception ex)
        {
            // 転送できなくても、単独での動作には支障がない
            Console.Error.WriteLine("Could not listen for other instances: " + ex.Message);
            _listener?.Dispose();
            _listener = null;
        }
    }

    private async Task AcceptLoopAsync(Func<string[], bool> onArgumentsReceived, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null)
        {
            Socket client;
            try
            {
                client = await _listener.AcceptAsync(ct);
            }
            catch (Exception)
            {
                return; // 終了時
            }

            using (client)
            {
                // 送信側が Shutdown(Send) を呼ばずに居座ると、この 1 件で
                // 受信ループ全体が止まる。接続ごとに時間を区切る
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var connectionToken = timeout.Token;

                try
                {
                    var arguments = await ReadArgumentsAsync(client, connectionToken);
                    var accepted = arguments != null && onArgumentsReceived(arguments);

                    var response = Encoding.UTF8.GetBytes(accepted ? ResponseAccepted : ResponseBusy);
                    await client.SendAsync(response, SocketFlags.None, connectionToken);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Could not handle another instance: " + ex.Message);
                }
            }
        }
    }

    private static async Task<string[]?> ReadArgumentsAsync(Socket client, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];

        while (true)
        {
            var read = await client.ReceiveAsync(chunk, SocketFlags.None, ct);
            if (read <= 0) break;

            if (buffer.Length + read > MaxMessageBytes) return null;

            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0) return null;

        return JsonSerializer.Deserialize<string[]>(buffer.ToArray());
    }

    /// <summary>
    /// すでに起動しているプロセスへ引数を渡す。
    /// </summary>
    /// <returns>相手が受け取って処理を始めたら true</returns>
    public bool TrySendArguments(string[] arguments, TimeSpan timeout)
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
            {
                SendTimeout = (int)timeout.TotalMilliseconds,
                ReceiveTimeout = (int)timeout.TotalMilliseconds,
            };

            socket.Connect(new UnixDomainSocketEndPoint(_socketPath));
            socket.Send(JsonSerializer.SerializeToUtf8Bytes(arguments));
            socket.Shutdown(SocketShutdown.Send);

            var response = new byte[16];
            var read = socket.Receive(response);

            return read > 0 && Encoding.UTF8.GetString(response, 0, read) == ResponseAccepted;
        }
        catch (Exception)
        {
            // 相手が受け取れる状態にない（起動直後・終了直後など）
            return false;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _listener?.Dispose();
        _listener = null;

        _lock?.Dispose();
        _lock = null;

        try
        {
            if (File.Exists(_socketPath)) File.Delete(_socketPath);
        }
        catch (Exception)
        {
            // 消せなくても次回の起動時に消す
        }

        if (ReferenceEquals(Current, this)) Current = null;
    }
}
