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

//----------------------------------------------------------------------
// 複数起動の抑止と、2 つ目のプロセスから 1 つ目へ引数を渡す仕組みのテスト。
//----------------------------------------------------------------------

using AttacheCase.Services;

namespace AttacheCase.Tests;

public class SingleInstanceTests
{
    private static string NewDirectory() => Directory.CreateTempSubdirectory("atc-instance").FullName;

    [Fact]
    public void TheSecondServiceCannotTakeTheLock()
    {
        var directory = NewDirectory();
        try
        {
            using var first = new SingleInstanceService(directory);
            Assert.True(first.TryAcquire());

            using var second = new SingleInstanceService(directory);
            Assert.False(second.TryAcquire());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void TheLockIsReleasedOnDispose()
    {
        var directory = NewDirectory();
        try
        {
            var first = new SingleInstanceService(directory);
            Assert.True(first.TryAcquire());
            first.Dispose();

            using var next = new SingleInstanceService(directory);
            Assert.True(next.TryAcquire());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ArgumentsAreHandedOverToTheRunningInstance()
    {
        var directory = NewDirectory();
        try
        {
            using var running = new SingleInstanceService(directory);
            Assert.True(running.TryAcquire());

            string[]? received = null;
            using var delivered = new ManualResetEventSlim();

            running.StartListening(args =>
            {
                received = args;
                delivered.Set();
                return true;
            });

            using var second = new SingleInstanceService(directory);
            var accepted = second.TrySendArguments(["/en", "/p=secret", "/tmp/file.txt"], TimeSpan.FromSeconds(5));

            Assert.True(accepted);
            Assert.True(delivered.Wait(TimeSpan.FromSeconds(5)));
            Assert.NotNull(received);
            Assert.Equal(["/en", "/p=secret", "/tmp/file.txt"], received);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void WhenTheRunningInstanceIsBusy_TheHandoverIsRefused()
    {
        var directory = NewDirectory();
        try
        {
            using var running = new SingleInstanceService(directory);
            Assert.True(running.TryAcquire());

            // 処理中で受け付けられない状態を模す
            running.StartListening(_ => false);

            using var second = new SingleInstanceService(directory);

            Assert.False(second.TrySendArguments(["/en"], TimeSpan.FromSeconds(5)));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void WithoutARunningInstance_TheHandoverJustFails()
    {
        var directory = NewDirectory();
        try
        {
            using var lonely = new SingleInstanceService(directory);

            Assert.False(lonely.TrySendArguments(["/en"], TimeSpan.FromSeconds(1)));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void AStaleSocketFileDoesNotPreventListening()
    {
        var directory = NewDirectory();
        try
        {
            // 異常終了でソケットファイルだけが残っている状況を作る
            var socketPath = Path.Combine(directory, "instance.sock");
            File.WriteAllText(socketPath, "stale");

            using var running = new SingleInstanceService(directory);
            Assert.True(running.TryAcquire());
            running.StartListening(_ => true);

            using var second = new SingleInstanceService(directory);
            Assert.True(second.TrySendArguments(["/en"], TimeSpan.FromSeconds(5)));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
