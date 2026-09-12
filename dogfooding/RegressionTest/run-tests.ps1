#Requires -Version 5.1
<#
.SYNOPSIS
  AttacheCase 5 回帰テストスイート（層 1）

.DESCRIPTION
  AttacheCase.Cli.exe の終了コードと出力を突き合わせて、仕様どおりに動くかを検証する。
  数十秒で終わることを狙っており、コードを変えるたびに回す前提。

  従来は GUI を起動して「ファイルが増えたか」で推測するしかなかったが、
  AttacheCase.Cli.exe が終了コードを返すようになったのでアサーションが書けるようになった。

  長時間の負荷試験・大容量・多数回の反復は層 2（..\Tester）が担当する。

.PARAMETER Cli
  AttacheCase.Cli.exe のパス。省略時はリポジトリの bin\Release から自動検出。

.PARAMETER Gui
  AttacheCase.exe のパス。省略時は Cli と同じフォルダー。

.PARAMETER Work
  作業フォルダー。省略時は %TEMP% 配下に作る。

.PARAMETER KeepWork
  終了後も作業フォルダーを残す（調査用）。

.PARAMETER Filter
  実行するテスト名の絞り込み（ワイルドカード可）。

.PARAMETER NoGui
  本体 GUI を起動するテストを飛ばす。
  作業中に回すとウィンドウが出たり消えたりしてフォーカスを奪われるため、
  その間は これを付けて CLI だけのテストに絞る。
  飛ばされるのは C 群（鍵ペア生成に GUI が要る）と G 群（GUI の経路そのもの）。

.PARAMETER PostPpap
  Post-PPAP のテスト（H 群）を実行する。
  **本番のキーサーバへ問い合わせる**ため既定では実行しない。
  検索 API はサーバ側で lastSeen を自動更新し、列挙対策で 1 秒の遅延が入る。
  日常の確認で毎回叩くものではないので、明示したときだけ走らせる。

.PARAMETER PostPpapEmail
  Post-PPAP テストで使うメールアドレス（鍵サーバに登録済みのもの）。
  省略時は環境変数 ATC_POSTPPAP_EMAIL。どちらも無ければ Post-PPAP のテストは SKIP。

.PARAMETER RefreshFixtures
  旧形式（v105 / v130 / v140）の検体を作り直す。
  検体は %TEMP%\atc_regress_fixtures に置いて使い回している。
  旧本体を入れ替えたときだけ指定すればよい。

.EXAMPLE
  .\run-tests.ps1
  .\run-tests.ps1 -Filter "旧形式*"
  .\run-tests.ps1 -KeepWork
  .\run-tests.ps1 -NoGui            # 作業中（GUI が出ない）
  .\run-tests.ps1 -PostPpap
  .\run-tests.ps1 -RefreshFixtures
#>
[CmdletBinding()]
param(
  [string]$Cli,
  [string]$Gui,
  [string]$Work,
  [switch]$KeepWork,
  [string]$Filter = "*",
  [switch]$RefreshFixtures,
  [switch]$PostPpap,
  [string]$PostPpapEmail = $env:ATC_POSTPPAP_EMAIL,
  [switch]$NoGui
)

$ErrorActionPreference = "Stop"
$script:Results = New-Object System.Collections.ArrayList

#----------------------------------------------------------------------
# 準備
#----------------------------------------------------------------------

$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

# 旧版のリポジトリ置き場（旧形式の検体生成に旧本体を使う）。環境変数 ATC_LEGACY_ROOT で差し替え可
$legacyRoot = if ($env:ATC_LEGACY_ROOT) { $env:ATC_LEGACY_ROOT } else { "D:\Dropbox" }
$legacyRepoRoot = Join-Path $legacyRoot "AttacheCase5"

if (-not $Cli) {
  # Release があれば Release、無ければ Debug
  $Cli = Join-Path $repoRoot "src\AttacheCase.Cli\bin\Release\net10.0\AttacheCase.Cli.exe"
  if (-not (Test-Path $Cli)) {
    $Cli = Join-Path $repoRoot "src\AttacheCase.Cli\bin\Debug\net10.0\AttacheCase.Cli.exe"
  }
}
if (-not $Gui) {
  $config = Split-Path (Split-Path (Split-Path $Cli -Parent) -Parent) -Leaf  # Debug / Release
  $Gui = Join-Path $repoRoot "src\AttacheCase\bin\$config\net10.0\AttacheCase.exe"
}
if (-not (Test-Path $Cli)) { throw "AttacheCase.Cli.exe が見つかりません: $Cli" }
if (-not (Test-Path $Gui)) { throw "AttacheCase.exe が見つかりません: $Gui" }

if (-not $Work) {
  $Work = Join-Path $env:TEMP ("atc_regress_" + (Get-Date -Format "yyyyMMdd_HHmmss"))
}
if (Test-Path $Work) { Remove-Item $Work -Recurse -Force }
New-Item -ItemType Directory -Force $Work | Out-Null

# 旧バージョンの本体（旧形式の検体を生成するために使う）。無ければ該当テストは SKIP。
$legacyExe = @{
  "v105" = Join-Path $legacyRepoRoot "_tools\Tester\AttacheCase.exe"
  "v130" = Join-Path $legacyRoot "AttacheCase3\AttacheCase\bin\Release\AttacheCase.exe"
  "v140" = Join-Path $legacyRoot "AttacheCase4\AttacheCase\bin\Release\AttacheCase.exe"
}

Write-Host ""
Write-Host "AttacheCase 5 回帰テスト" -ForegroundColor Cyan
Write-Host "  CLI : $Cli"
Write-Host "  GUI : $Gui"
Write-Host "  Work: $Work"
Write-Host ""

#----------------------------------------------------------------------
# ヘルパー
#----------------------------------------------------------------------

function New-TestDir {
  param([string]$Name)
  $d = Join-Path $Work $Name
  New-Item -ItemType Directory -Force $d | Out-Null
  return $d
}

function New-TestFile {
  param([string]$Path, [int]$SizeKb = 4)
  New-Item -ItemType Directory -Force (Split-Path $Path -Parent) | Out-Null
  $bytes = New-Object byte[] ($SizeKb * 1024)
  (New-Object Random 12345).NextBytes($bytes)
  [System.IO.File]::WriteAllBytes($Path, $bytes)
}

# フォルダー配下を「相対パス + 内容ハッシュ」の並びで正規化する。
# 暗号化前と復号後を比較して、内容が完全に一致するかを見るために使う。
function Get-TreeDigest {
  param([string]$Root)
  if (-not (Test-Path $Root)) { return "<missing>" }
  $lines = New-Object System.Collections.ArrayList
  Get-ChildItem $Root -Recurse -File | Sort-Object FullName | ForEach-Object {
    $rel = $_.FullName.Substring($Root.Length).TrimStart('\')
    $h = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
    [void]$lines.Add("$rel`t$h")
  }
  return ($lines -join "`n")
}

# CLI を起動して終了コードと出力を返す。stderr は捨てずに拾う。
function Invoke-Cli {
  param([string[]]$Arguments)
  $stdout = Join-Path $Work "_stdout.txt"
  $stderr = Join-Path $Work "_stderr.txt"
  $p = Start-Process -FilePath $Cli -ArgumentList $Arguments -NoNewWindow -Wait -PassThru `
       -RedirectStandardOutput $stdout -RedirectStandardError $stderr
  $out = ""
  $err = ""
  # 新 CLI はコンソール出力を UTF-8 に統一している。既定（CP932）で読むと化ける
  if (Test-Path $stdout) { $out = (Get-Content $stdout -Raw -Encoding UTF8) }
  if (Test-Path $stderr) { $err = (Get-Content $stderr -Raw -Encoding UTF8) }
  return [PSCustomObject]@{ ExitCode = $p.ExitCode; StdOut = $out; StdErr = $err }
}

# GUI 本体をコマンドライン引数で駆動する（本物の Form1 の経路を通す）。
function Invoke-Gui {
  param([string[]]$Arguments, [int]$TimeoutSec = 60)
  $p = Start-Process -FilePath $Gui -ArgumentList $Arguments -PassThru
  if (-not $p.WaitForExit($TimeoutSec * 1000)) {
    try { $p.Kill() } catch { }
    return -999
  }
  return $p.ExitCode
}

function Invoke-LegacyGui {
  param([string]$Exe, [string[]]$Arguments, [int]$TimeoutSec = 60)
  $p = Start-Process -FilePath $Exe -ArgumentList $Arguments -PassThru
  if (-not $p.WaitForExit($TimeoutSec * 1000)) {
    try { $p.Kill() } catch { }
    return -999
  }
  return $p.ExitCode
}

function Add-Result {
  param([string]$Name, [string]$Status, [string]$Detail = "")
  [void]$script:Results.Add([PSCustomObject]@{ Name = $Name; Status = $Status; Detail = $Detail })
  $color = "Gray"
  if ($Status -eq "PASS") { $color = "Green" }
  elseif ($Status -eq "FAIL") { $color = "Red" }
  elseif ($Status -eq "SKIP") { $color = "Yellow" }
  $line = "  [{0}] {1}" -f $Status, $Name
  if ($Detail) { $line += "  -- $Detail" }
  Write-Host $line -ForegroundColor $color
}

# テスト 1 件を実行する。ScriptBlock は $null（成功）か失敗理由の文字列を返す。
function Test-Case {
  param([string]$Name, [scriptblock]$Body)
  if ($Name -notlike $Filter) { return }
  try {
    $reason = & $Body
    if ($null -eq $reason) { Add-Result $Name "PASS" }
    elseif ($reason -like "SKIP:*") { Add-Result $Name "SKIP" ($reason.Substring(5).Trim()) }
    else { Add-Result $Name "FAIL" $reason }
  }
  catch {
    Add-Result $Name "FAIL" ("例外: " + $_.Exception.Message)
  }
}

$PW = "DogfoodPassw0rd!"

#======================================================================
Write-Host "A. 基本ラウンドトリップ (v150)" -ForegroundColor White
#======================================================================

Test-Case "A-01 単一ファイル" {
  $d = New-TestDir "a01"; New-TestFile "$d\src\a.txt"
  $before = Get-TreeDigest "$d\src"
  $r = Invoke-Cli @("/en", "/p=$PW", "/y", "/out=$d\a.atc", "$d\src\a.txt")
  if ($r.ExitCode -ne 0) { return "暗号化が失敗 (exit=$($r.ExitCode)) $($r.StdErr)" }
  New-Item -ItemType Directory -Force "$d\out" | Out-Null
  $r = Invoke-Cli @("/de", "/p=$PW", "/y", "/nopfldr=1", "/dsaveto=$d\out", "$d\a.atc")
  if ($r.ExitCode -ne 0) { return "復号が失敗 (exit=$($r.ExitCode)) $($r.StdErr)" }
  if ((Get-TreeDigest "$d\out") -ne $before) { return "内容が一致しない" }
  return $null
}

Test-Case "A-02 フォルダー" {
  $d = New-TestDir "a02"
  New-TestFile "$d\src\a.txt"; New-TestFile "$d\src\sub\b.dat" 8; New-TestFile "$d\src\sub\deep\c.bin" 2
  $before = Get-TreeDigest "$d\src"
  $r = Invoke-Cli @("/en", "/p=$PW", "/y", "/out=$d\src.atc", "$d\src")
  if ($r.ExitCode -ne 0) { return "暗号化が失敗 (exit=$($r.ExitCode))" }
  New-Item -ItemType Directory -Force "$d\out" | Out-Null
  $r = Invoke-Cli @("/de", "/p=$PW", "/y", "/nopfldr=1", "/dsaveto=$d\out", "$d\src.atc")
  if ($r.ExitCode -ne 0) { return "復号が失敗 (exit=$($r.ExitCode))" }
  if ((Get-TreeDigest "$d\out\src") -ne $before) { return "内容が一致しない" }
  return $null
}

Test-Case "A-03 自己実行形式 (SFX)" {
  $d = New-TestDir "a03"; New-TestFile "$d\src\a.txt"
  $before = Get-TreeDigest "$d\src"
  $r = Invoke-Cli @("/en", "/p=$PW", "/y", "/exeout=1", "/out=$d\a.exe", "$d\src\a.txt")
  if ($r.ExitCode -ne 0) { return "暗号化が失敗 (exit=$($r.ExitCode))" }
  # 自己実行形式には復号プログラム（Exeout.exe）が同梱される。
  # 現行スタブは 3,090,944 バイトなので、これを下回るなら埋め込みに失敗している。
  $sfxSize = (Get-Item "$d\a.exe").Length
  if ($sfxSize -lt 3000000) { return "SFX が $sfxSize バイトしかない（復号プログラムが入っていない?）" }
  New-Item -ItemType Directory -Force "$d\out" | Out-Null
  $r = Invoke-Cli @("/de", "/p=$PW", "/y", "/nopfldr=1", "/dsaveto=$d\out", "$d\a.exe")
  if ($r.ExitCode -ne 0) { return "復号が失敗 (exit=$($r.ExitCode))" }
  if ((Get-TreeDigest "$d\out") -ne $before) { return "内容が一致しない" }
  return $null
}

Test-Case "A-04 /oneby 個別暗号化" {
  $d = New-TestDir "a04"
  New-TestFile "$d\src\a.txt"; New-TestFile "$d\src\b.csv"; New-TestFile "$d\src\sub\c.txt"
  $r = Invoke-Cli @("/en", "/p=$PW", "/y", "/oneby=1", "$d\src")
  if ($r.ExitCode -ne 0) { return "暗号化が失敗 (exit=$($r.ExitCode))" }
  foreach ($f in @("$d\src\a.atc", "$d\src\b.atc", "$d\src\sub\c.atc")) {
    if (-not (Test-Path $f)) { return "個別の暗号化ファイルが作られていない: $f" }
  }
  return $null
}

Test-Case "A-05 /oneby と /out=ファイル の矛盾検出" {
  $d = New-TestDir "a05"; New-TestFile "$d\src\a.txt"
  $r = Invoke-Cli @("/en", "/p=$PW", "/y", "/oneby=1", "/out=$d\x.atc", "$d\src")
  if ($r.ExitCode -ne 2) { return "終了コードが 2 でない (exit=$($r.ExitCode))" }
  return $null
}

Test-Case "A-06 /autoname 書式展開" {
  $d = New-TestDir "a06"; New-TestFile "$d\report.xlsx"
  $stamp = Get-Date -Format "yyyyMMdd"
  $r = Invoke-Cli @("/en", "/p=$PW", "/y", "/autoname=<filename>_<date:yyyyMMdd><ext>", "/saveto=$d", "$d\report.xlsx")
  if ($r.ExitCode -ne 0) { return "暗号化が失敗 (exit=$($r.ExitCode))" }
  if (-not (Test-Path "$d\report_$stamp.atc")) { return "期待したファイル名にならない: report_$stamp.atc" }
  return $null
}

Test-Case "A-07 /autoname 連番の衝突回避" {
  $d = New-TestDir "a07"; New-TestFile "$d\x.txt"
  for ($i = 1; $i -le 3; $i++) {
    $r = Invoke-Cli @("/en", "/p=$PW", "/autoname=fix_<num:3><ext>", "/saveto=$d", "$d\x.txt")
    if ($r.ExitCode -ne 0) { return "$i 回目の暗号化が失敗 (exit=$($r.ExitCode))" }
  }
  foreach ($n in @("001", "002", "003")) {
    if (-not (Test-Path "$d\fix_$n.atc")) { return "連番が繰り上がっていない: fix_$n.atc" }
  }
  return $null
}

Test-Case "A-08 /camoext 拡張子偽装" {
  $d = New-TestDir "a08"; New-TestFile "$d\src\a.txt"
  $before = Get-TreeDigest "$d\src"
  $r = Invoke-Cli @("/en", "/p=$PW", "/y", "/camoext=.jpg", "/saveto=$d", "$d\src\a.txt")
  if ($r.ExitCode -ne 0) { return "暗号化が失敗 (exit=$($r.ExitCode))" }
  if (-not (Test-Path "$d\a.jpg")) { return "偽装拡張子のファイルが無い" }
  # 拡張子が違っても中身で判定して復号できること
  New-Item -ItemType Directory -Force "$d\out" | Out-Null
  $r = Invoke-Cli @("/de", "/p=$PW", "/y", "/nopfldr=1", "/dsaveto=$d\out", "$d\a.jpg")
  if ($r.ExitCode -ne 0) { return "偽装拡張子ファイルの復号が失敗 (exit=$($r.ExitCode))" }
  if ((Get-TreeDigest "$d\out") -ne $before) { return "内容が一致しない" }
  return $null
}

Test-Case "A-09 /camoext 不正文字の拒否" {
  $d = New-TestDir "a09"; New-TestFile "$d\a.txt"
  $r = Invoke-Cli @("/en", "/p=$PW", "/y", "/camoext=a?b", "$d\a.txt")
  if ($r.ExitCode -ne 2) { return "終了コードが 2 でない (exit=$($r.ExitCode))" }
  return $null
}

Test-Case "A-10 /withext 元拡張子を残す" {
  $d = New-TestDir "a10"; New-TestFile "$d\report.xlsx"
  $r = Invoke-Cli @("/en", "/p=$PW", "/y", "/withext=1", "/saveto=$d", "$d\report.xlsx")
  if ($r.ExitCode -ne 0) { return "暗号化が失敗 (exit=$($r.ExitCode))" }
  if (-not (Test-Path "$d\report.xlsx.atc")) { return "report.xlsx.atc が無い" }
  return $null
}

Test-Case "A-11 拡張子・処理種別の自動判定" {
  $d = New-TestDir "a11"; New-TestFile "$d\src\a.txt"
  $before = Get-TreeDigest "$d\src"
  # /en /de を付けない
  $r = Invoke-Cli @("/p=$PW", "/y", "/out=$d\a.atc", "$d\src\a.txt")
  if ($r.ExitCode -ne 0) { return "暗号化として判定されなかった (exit=$($r.ExitCode))" }
  New-Item -ItemType Directory -Force "$d\out" | Out-Null
  $r = Invoke-Cli @("/p=$PW", "/y", "/nopfldr=1", "/dsaveto=$d\out", "$d\a.atc")
  if ($r.ExitCode -ne 0) { return "復号として判定されなかった (exit=$($r.ExitCode))" }
  if ((Get-TreeDigest "$d\out") -ne $before) { return "内容が一致しない" }
  return $null
}

Test-Case "A-12 埋め込み SFX スタブが現行 Exeout.exe と一致し現行ソースの型を含む" {
  # 旧版はビルド出力を 16 進化した生成 C#（ExeOut4.cs）を手作業で更新していたため、
  # 古いスタブが埋め込まれたまま気づけない事故があった（2026-09-04）。
  # 5.5 はその埋め込み方式（Exeout48.cs の rawData に ExeToHex で差し込み）に戻した。
  # そこで (1) 実際に作った SFX の先頭が「現行ビルドの Exeout.exe」と一致すること
  #（＝埋め込みが古くない）、(2) そのスタブに現行 ExeOutShared の型が含まれること
  # を検査する。現行 Exeout.exe が未ビルドなら検証できないので SKIP。
  $builtStub = Join-Path $repoRoot "src\Exeout\bin\Release\net48\Exeout.exe"
  if (-not (Test-Path $builtStub)) {
    return "SKIP: 現行 Exeout.exe が未ビルド（先に Exeout.csproj を Release でビルド）"
  }
  $bytes = [IO.File]::ReadAllBytes($builtStub)

  # SFX を作り、その先頭 = 埋め込みスタブが現行 Exeout.exe と一致するか
  $d = New-TestDir "a12"; New-TestFile "$d\a.txt"
  $r = Invoke-Cli @("/en", "/p=$PW", "/y", "/exeout=1", "/out=$d\a.exe", "$d\a.txt")
  if ($r.ExitCode -ne 0) { return "SFX 作成に失敗 (exit=$($r.ExitCode)) $($r.StdErr)" }
  $sfxBytes = [IO.File]::ReadAllBytes("$d\a.exe")
  if ($sfxBytes.Length -lt $bytes.Length) { return "SFX がスタブより小さい（埋め込み失敗?）" }

  $sha = [System.Security.Cryptography.SHA256]::Create()
  $prefix = New-Object byte[] $bytes.Length
  [Array]::Copy($sfxBytes, 0, $prefix, 0, $bytes.Length)
  if ([BitConverter]::ToString($sha.ComputeHash($prefix)) -ne
      [BitConverter]::ToString($sha.ComputeHash($bytes))) {
    return "SFX 先頭が現行 Exeout.exe と一致しない（埋め込みが古い可能性）"
  }

  $names = New-Object System.Collections.Generic.List[string]
  $names.Add("AppLanguage")
  Get-ChildItem (Join-Path $repoRoot "src\ExeOutShared") -Filter *.cs -File | ForEach-Object {
    $body = Get-Content $_.FullName -Raw
    foreach ($m in [regex]::Matches($body, "(?m)^[\s\w]*class\s+([A-Za-z_][A-Za-z0-9_]*)")) {
      $names.Add($m.Groups[1].Value)
    }
  }

  $missing = @()
  foreach ($n in ($names | Sort-Object -Unique)) {
    $needle = [Text.Encoding]::UTF8.GetBytes($n)
    $found = $false
    for ($i = 0; $i -le $bytes.Length - $needle.Length; $i++) {
      if ($bytes[$i] -eq $needle[0]) {
        $match = $true
        for ($j = 1; $j -lt $needle.Length; $j++) {
          if ($bytes[$i + $j] -ne $needle[$j]) { $match = $false; break }
        }
        if ($match) { $found = $true; break }
      }
    }
    if (-not $found) { $missing += $n }
  }
  if ($missing.Count -gt 0) {
    return ("同梱スタブに次の型が含まれていない（ソースと食い違い）: " + ($missing -join ", "))
  }
  return $null
}

Test-Case "A-13 パスワード付き ZIP の往復（3 方式）" {
  $d = New-TestDir "a13"
  New-TestFile "$d\src\a.txt"
  New-TestFile "$d\src\sub\b.txt"
  $before = Get-TreeDigest "$d\src"
  foreach ($algo in @(0, 1, 2)) {
    $zip = "$d\out$algo.zip"
    $r = Invoke-Cli @("/en", "/entype=3", "/p=$PW", "/zipalgo=$algo", "/y", "/out=$zip", "$d\src")
    if ($r.ExitCode -ne 0) { return "algo=$algo 暗号化に失敗 (exit=$($r.ExitCode)) $($r.StdErr)" }
    if (-not (Test-Path $zip)) { return "algo=$algo ZIP が作られていない" }
    New-Item -ItemType Directory -Force "$d\o$algo" | Out-Null
    $r = Invoke-Cli @("/de", "/p=$PW", "/y", "/dsaveto=$d\o$algo", $zip)
    if ($r.ExitCode -ne 0) { return "algo=$algo 復号に失敗 (exit=$($r.ExitCode)) $($r.StdErr)" }
    # 書庫は選んだフォルダー自身を最上位に含む
    if ((Get-TreeDigest "$d\o$algo\src") -ne $before) { return "algo=$algo 内容が一致しない" }
  }
  return $null
}

Test-Case "A-14 ZIP のパスワード誤りは 4" {
  $d = New-TestDir "a14"; New-TestFile "$d\src\a.txt"
  $zip = "$d\x.zip"
  $r = Invoke-Cli @("/en", "/entype=3", "/p=$PW", "/y", "/out=$zip", "$d\src")
  if ($r.ExitCode -ne 0) { return "暗号化に失敗 (exit=$($r.ExitCode))" }
  New-Item -ItemType Directory -Force "$d\out" | Out-Null
  $r = Invoke-Cli @("/de", "/p=WrongPassword!", "/y", "/dsaveto=$d\out", $zip)
  if ($r.ExitCode -ne 4) { return "終了コードが 4 でない (exit=$($r.ExitCode))" }
  return $null
}

Test-Case "A-15 ZIP のディレクトリ・トラバーサルを拒否する" {
  # エントリー名は攻撃者の制御下にある。展開先の外を指す名前を弾けるかを見る。
  # これは .atc 側で実際に穴が見つかった攻撃（PathSafety の導入理由）と同じもの。
  # SDK スタイル移行後は packages\ ディレクトリが無いので NuGet のグローバルキャッシュから拾う
  $dll = Join-Path $env:USERPROFILE ".nuget\packages\sharpziplib\1.4.2\lib\netstandard2.0\ICSharpCode.SharpZipLib.dll"
  if (-not (Test-Path $dll)) { return "SKIP: SharpZipLib が見つかりません" }
  try { Add-Type -Path $dll -ErrorAction SilentlyContinue } catch { }

  $d = New-TestDir "a15"
  New-Item -ItemType Directory -Force "$d\out" | Out-Null
  $zip = "$d\evil.zip"
  $fs = [IO.File]::Create($zip)
  try {
    $z = New-Object ICSharpCode.SharpZipLib.Zip.ZipOutputStream($fs)
    $z.SetLevel(0)
    $z.Password = $PW
    # ZipEntry.CleanName に潰されないよう、名前は生のまま入れる
    $e = New-Object ICSharpCode.SharpZipLib.Zip.ZipEntry("../evil.txt")
    $bytes = [Text.Encoding]::UTF8.GetBytes("pwned")
    $e.Size = $bytes.Length
    $z.PutNextEntry($e); $z.Write($bytes, 0, $bytes.Length); $z.CloseEntry(); $z.Finish(); $z.Close()
  }
  finally { $fs.Dispose() }

  $r = Invoke-Cli @("/de", "/p=$PW", "/y", "/dsaveto=$d\out", $zip)
  # 展開先の外に書かれていないことが本質。終了コードは問わない。
  if (Test-Path "$d\evil.txt") { return "展開先の外にファイルが作られた: $d\evil.txt" }
  return $null
}

#======================================================================
Write-Host ""
Write-Host "B. 旧形式の復号互換" -ForegroundColor White
#======================================================================

# 旧形式の検体は %TEMP% 配下にキャッシュして使い回す。
#
# 旧本体はコマンドラインで動かしても確認画面で止まることがある
# （AC5 で「/p= 指定時に確認画面で止まる」を修正済み。AC3 / AC4 には入っていない）。
# 旧 Tester が /mempexe=1 を付けていたのはこのため。それでも環境によっては
# 初回起動時のダイアログ等で止まりうるので、
#   * 検体はキャッシュして 2 回目以降は旧本体を起動しない
#   * 止まった場合はタイムアウトさせ、「入力待ちの可能性」を明示して失敗させる
# という作りにしている。人が横で押さないと通らないテストは、通っても意味がない。
$script:FixtureRoot = Join-Path $env:TEMP "atc_regress_fixtures"

function Get-LegacyFixture {
  param([string]$Label, [string]$Exe)

  $dir = Join-Path $script:FixtureRoot $Label
  $atc = Join-Path $dir "legacy.atc"
  $src = Join-Path $dir "legacy.txt"

  if ((Test-Path $atc) -and (Test-Path $src) -and -not $RefreshFixtures) {
    return [PSCustomObject]@{ Atc = $atc; Source = $src; Error = $null }
  }

  if (-not (Test-Path $Exe)) {
    return [PSCustomObject]@{ Atc = $null; Source = $null; Error = "SKIP: 旧本体が見つかりません ($Exe)" }
  }

  if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
  New-Item -ItemType Directory -Force $dir | Out-Null
  New-TestFile $src 16

  # /mempexe=1 と /ow=0 は旧 Tester が使っていた「確認画面を出さない」組み合わせ
  $code = Invoke-LegacyGui $Exe @("/en=1", "/p=$PW", "/ow=0", "/mempexe=1", "/exit=1", $src) 30

  if (-not (Test-Path $atc)) {
    $hint = "旧本体で検体を作れませんでした (exit=$code)"
    if ($code -eq -999) { $hint += " — 確認画面で入力待ちになった可能性があります" }
    return [PSCustomObject]@{ Atc = $null; Source = $null; Error = $hint }
  }

  return [PSCustomObject]@{ Atc = $atc; Source = $src; Error = $null }
}

function Test-LegacyFormat {
  param([string]$Label, [string]$Exe, [string]$Dir)

  $fixture = Get-LegacyFixture $Label $Exe
  if ($fixture.Error) { return $fixture.Error }

  $srcHash = (Get-FileHash $fixture.Source -Algorithm SHA256).Hash

  $d = New-TestDir $Dir
  New-Item -ItemType Directory -Force "$d\out" | Out-Null
  $r = Invoke-Cli @("/de", "/p=$PW", "/y", "/nopfldr=1", "/dsaveto=$d\out", $fixture.Atc)
  if ($r.ExitCode -ne 0) { return "復号が失敗 (exit=$($r.ExitCode)) $($r.StdErr)" }

  $outFile = Get-ChildItem "$d\out" -Recurse -File | Select-Object -First 1
  if (-not $outFile) { return "復号結果が無い" }
  if ((Get-FileHash $outFile.FullName -Algorithm SHA256).Hash -ne $srcHash) { return "内容が一致しない" }
  return $null
}

Test-Case "B-01 旧形式 v105 (AttacheCase 2) の復号" { Test-LegacyFormat "v105" $legacyExe["v105"] "b01" }
Test-Case "B-02 旧形式 v130 (AttacheCase 3) の復号" { Test-LegacyFormat "v130" $legacyExe["v130"] "b02" }
Test-Case "B-03 旧形式 v140 (AttacheCase 4) の復号" { Test-LegacyFormat "v140" $legacyExe["v140"] "b03" }

#======================================================================
Write-Host ""
Write-Host "C. 公開鍵方式" -ForegroundColor White
#======================================================================

# RSA 鍵ファイル方式（.atcpub / .atcpvt）による暗号化は 5.0 で一度畳んだが、
# 5.5 で復活させた（利用者判断 2026-09-12。Core の Crypto/RsaKeyFile を参照）。
# 形式は v150（RSA 単体の宛先エントリ）。ここでは往復と、鍵の不一致・鍵長の拒否を見る。
$script:KeyDir = New-TestDir "keys"

# 鍵ペア（.atcpub / .atcpvt）は自前で作る。本体の鍵生成は GUI（/genrsa）にしかないため。
# 中身は Modulus / Exponent（+ 秘密鍵は D 以下）だけあればよい（token / id は照合に使わない）
function New-RsaKeyPairFixture {
  param([string]$PublicPath, [string]$PrivatePath, [int]$Bits = 4096)
  try {
    $rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider $Bits
    try {
      [System.IO.File]::WriteAllText($PublicPath, $rsa.ToXmlString($false), [System.Text.Encoding]::UTF8)
      [System.IO.File]::WriteAllText($PrivatePath, $rsa.ToXmlString($true), [System.Text.Encoding]::UTF8)
    } finally {
      $rsa.Dispose()
    }
    return $true
  } catch {
    return $false
  }
}

Test-Case "C-01 /genrsa で鍵ペア（.atcpub / .atcpvt）が生成される" {
  if ($NoGui) { return "SKIP: -NoGui のため（GUI が必要）" }
  $d = New-TestDir "c01"
  # 固まると Invoke-Gui がタイムアウトして -999 を返す（無人実行でモーダルが出た疑い）
  $code = Invoke-Gui @("/genrsa=$d", "/exit=1") 60
  if ($code -eq -999) { return "終了せずに固まった（無人実行でモーダルが出た可能性）" }
  if (-not (Test-Path "$d\AttacheCase_RSA.atcpub")) { return "公開鍵が生成されていない" }
  if (-not (Test-Path "$d\AttacheCase_RSA.atcpvt")) { return "秘密鍵が生成されていない" }
  return $null
}

Test-Case "C-02 /pubkey で暗号化 → /privkey で復号（RSA-4096）" {
  $pub = Join-Path $script:KeyDir "rsa4096.atcpub"
  $pvt = Join-Path $script:KeyDir "rsa4096.atcpvt"
  if (-not (New-RsaKeyPairFixture $pub $pvt 4096)) { return "SKIP: 鍵ペアの検体を作れませんでした" }
  $d = New-TestDir "c02"; New-TestFile "$d\src\a.txt"; New-TestFile "$d\src\sub\b.dat" 8
  $before = Get-TreeDigest "$d\src"
  $r = Invoke-Cli @("/en", "/pubkey=$pub", "/y", "/out=$d\a.atc", "$d\src")
  if ($r.ExitCode -ne 0) { return "暗号化が失敗 (exit=$($r.ExitCode)) $($r.StdErr)" }
  New-Item -ItemType Directory -Force "$d\out" | Out-Null
  $r = Invoke-Cli @("/de", "/privkey=$pvt", "/y", "/nopfldr=1", "/dsaveto=$d\out", "$d\a.atc")
  if ($r.ExitCode -ne 0) { return "復号が失敗 (exit=$($r.ExitCode)) $($r.StdErr)" }
  if ((Get-TreeDigest "$d\out\src") -ne $before) { return "内容が一致しない" }
  return $null
}

Test-Case "C-02b 別の秘密鍵では復号できない → 4、パスワードでも開けない → 4" {
  $pub = Join-Path $script:KeyDir "rsa4096.atcpub"
  $other = Join-Path $script:KeyDir "other.atcpvt"
  $otherPub = Join-Path $script:KeyDir "other.atcpub"
  if (-not (Test-Path $pub)) { return "SKIP: C-02 の鍵がありません" }
  if (-not (New-RsaKeyPairFixture $otherPub $other 2048)) { return "SKIP: 鍵ペアの検体を作れませんでした" }
  $d = New-TestDir "c02b"; New-TestFile "$d\a.txt"
  $r = Invoke-Cli @("/en", "/pubkey=$pub", "/y", "/out=$d\a.atc", "$d\a.txt")
  if ($r.ExitCode -ne 0) { return "暗号化が失敗 (exit=$($r.ExitCode)) $($r.StdErr)" }
  New-Item -ItemType Directory -Force "$d\out" | Out-Null
  $r = Invoke-Cli @("/de", "/privkey=$other", "/y", "/dsaveto=$d\out", "$d\a.atc")
  if ($r.ExitCode -ne 4) { return "別の秘密鍵で exit=$($r.ExitCode)（4 を期待）" }
  $r = Invoke-Cli @("/de", "/p=$PW", "/y", "/dsaveto=$d\out", "$d\a.atc")
  if ($r.ExitCode -ne 4) { return "パスワード指定で exit=$($r.ExitCode)（4 を期待）" }
  if (Get-ChildItem "$d\out" -Recurse -File) { return "失敗したのに出力ファイルが残っている" }
  return $null
}

Test-Case "C-02c RSA-2048 の鍵でも往復できる（AttacheCase 4 の鍵と同じ長さ）" {
  $pub = Join-Path $script:KeyDir "rsa2048.atcpub"
  $pvt = Join-Path $script:KeyDir "rsa2048.atcpvt"
  if (-not (New-RsaKeyPairFixture $pub $pvt 2048)) { return "SKIP: 鍵ペアの検体を作れませんでした" }
  $d = New-TestDir "c02c"; New-TestFile "$d\src\a.txt"
  $before = Get-TreeDigest "$d\src"
  $r = Invoke-Cli @("/en", "/pubkey=$pub", "/y", "/out=$d\a.atc", "$d\src\a.txt")
  if ($r.ExitCode -ne 0) { return "暗号化が失敗 (exit=$($r.ExitCode)) $($r.StdErr)" }
  New-Item -ItemType Directory -Force "$d\out" | Out-Null
  $r = Invoke-Cli @("/de", "/privkey=$pvt", "/y", "/nopfldr=1", "/dsaveto=$d\out", "$d\a.atc")
  if ($r.ExitCode -ne 0) { return "復号が失敗 (exit=$($r.ExitCode)) $($r.StdErr)" }
  if ((Get-TreeDigest "$d\out") -ne $before) { return "内容が一致しない" }
  return $null
}

Test-Case "C-02d 1024 ビットの鍵は使い方の誤り（2）として断り、出力を残さない" {
  $pub = Join-Path $script:KeyDir "rsa1024.atcpub"
  $pvt = Join-Path $script:KeyDir "rsa1024.atcpvt"
  if (-not (New-RsaKeyPairFixture $pub $pvt 1024)) { return "SKIP: 鍵ペアの検体を作れませんでした" }
  $d = New-TestDir "c02d"; New-TestFile "$d\a.txt"
  $r = Invoke-Cli @("/en", "/pubkey=$pub", "/y", "/out=$d\a.atc", "$d\a.txt")
  if ($r.ExitCode -ne 2) { return "exit=$($r.ExitCode)（2 を期待） $($r.StdErr)" }
  if (Test-Path "$d\a.atc") { return "断ったのに出力ファイルが残っている" }
  return $null
}

Test-Case "C-03 既存の RSA 秘密鍵による復号は残っている" {
  # 暗号化を畳んだので、この検体はもう本体では作れない。
  # 手元に v4 以前で作った RSA 暗号化ファイルがあれば、下記に置くと検査する。
  #   <FixtureRoot>\rsa\legacy_rsa.atc   … RSA 公開鍵で暗号化したファイル
  #   <FixtureRoot>\rsa\legacy_rsa.atcpvt … 対応する秘密鍵
  #   <FixtureRoot>\rsa\legacy_rsa.sha256 … 復号後に期待される SHA-256（大文字16進）
  $dir = Join-Path $script:FixtureRoot "rsa"
  $atc = Join-Path $dir "legacy_rsa.atc"
  $pvt = Join-Path $dir "legacy_rsa.atcpvt"
  $sha = Join-Path $dir "legacy_rsa.sha256"
  if (-not ((Test-Path $atc) -and (Test-Path $pvt) -and (Test-Path $sha))) {
    return "SKIP: 検体がありません（$dir に legacy_rsa.atc / .atcpvt / .sha256 を置くと検査します）"
  }

  $d = New-TestDir "c03"
  New-Item -ItemType Directory -Force "$d\out" | Out-Null
  $r = Invoke-Cli @("/de", "/privkey=$pvt", "/y", "/nopfldr=1", "/dsaveto=$d\out", $atc)
  if ($r.ExitCode -ne 0) { return "復号が失敗 (exit=$($r.ExitCode)) $($r.StdErr)" }
  $outFile = Get-ChildItem "$d\out" -Recurse -File | Select-Object -First 1
  if (-not $outFile) { return "復号結果が無い" }
  $expected = (Get-Content $sha -Raw).Trim().ToUpperInvariant()
  if ((Get-FileHash $outFile.FullName -Algorithm SHA256).Hash -ne $expected) { return "内容が一致しない" }
  return $null
}

#======================================================================
Write-Host ""
Write-Host "D. 異常系と終了コード" -ForegroundColor White
#======================================================================

Test-Case "D-01 誤ったパスワード → 4" {
  $d = New-TestDir "d01"; New-TestFile "$d\a.txt"
  Invoke-Cli @("/en", "/p=$PW", "/y", "/out=$d\a.atc", "$d\a.txt") | Out-Null
  New-Item -ItemType Directory -Force "$d\out" | Out-Null
  $r = Invoke-Cli @("/de", "/p=WrongPassword", "/y", "/dsaveto=$d\out", "$d\a.atc")
  if ($r.ExitCode -ne 4) { return "exit=$($r.ExitCode)" }
  return $null
}

Test-Case "D-02 出力先に同名ファイル → 5" {
  $d = New-TestDir "d02"; New-TestFile "$d\a.txt"
  Invoke-Cli @("/en", "/p=$PW", "/y", "/out=$d\a.atc", "$d\a.txt") | Out-Null
  $r = Invoke-Cli @("/en", "/p=$PW", "/out=$d\a.atc", "$d\a.txt")
  if ($r.ExitCode -ne 5) { return "exit=$($r.ExitCode)" }
  return $null
}

Test-Case "D-03 不明なオプション → 2" {
  $d = New-TestDir "d03"; New-TestFile "$d\a.txt"
  $r = Invoke-Cli @("/en", "/p=$PW", "/nosuchoption=1", "$d\a.txt")
  if ($r.ExitCode -ne 2) { return "exit=$($r.ExitCode)" }
  return $null
}

Test-Case "D-04 入力が見つからない → 3" {
  $d = New-TestDir "d04"
  $r = Invoke-Cli @("/en", "/p=$PW", "$d\nofile.txt")
  if ($r.ExitCode -ne 3) { return "exit=$($r.ExitCode)" }
  return $null
}

Test-Case "D-05 パスワード未指定 → 2" {
  $d = New-TestDir "d05"; New-TestFile "$d\a.txt"
  $r = Invoke-Cli @("/en", "$d\a.txt")
  if ($r.ExitCode -ne 2) { return "exit=$($r.ExitCode)" }
  return $null
}

Test-Case "D-06 GUI 専用オプションは受理して無視" {
  $d = New-TestDir "d06"; New-TestFile "$d\a.txt"
  $r = Invoke-Cli @("/en", "/p=$PW", "/y", "/wmin=1", "/theme=dark", "/exit=1", "/tsktr=1", "/out=$d\a.atc", "$d\a.txt")
  if ($r.ExitCode -ne 0) { return "exit=$($r.ExitCode) $($r.StdErr)" }
  return $null
}

Test-Case "D-07 未対応オプションは理由つきで拒否" {
  $d = New-TestDir "d07"; New-TestFile "$d\a.txt"
  # /zipsaveto と /zipalgo は ZIP 対応にともなって「対応済み」になったので、
  # まだ対応していないサルベージ系で検査する。
  $r = Invoke-Cli @("/en", "/p=$PW", "/y", "/slvgfolder=$d", "$d\a.txt")
  if ($r.ExitCode -ne 2) { return "exit=$($r.ExitCode)" }
  if ($r.StdErr -notmatch "Salvage") { return "理由が説明されていない" }
  return $null
}

# 鍵導出パラメータの改ざん。平文ヘッダ 36 バイトの直後が鍵保護ブロックで、
#   Salt(16) → offset 36..51 / MemoryKB(4) → 52 / Iterations(4) → 56 / Parallelism(4) → 60 / EncKeySize(2) → 64
function Test-Tamper {
  param([string]$Dir, [int]$Offset, [byte[]]$Bytes, [int]$ExpectedExit = 8)
  $d = New-TestDir $Dir; New-TestFile "$d\a.txt"
  $r = Invoke-Cli @("/en", "/p=$PW", "/y", "/out=$d\a.atc", "$d\a.txt")
  if ($r.ExitCode -ne 0) { return "検体の暗号化が失敗 (exit=$($r.ExitCode))" }

  $b = [System.IO.File]::ReadAllBytes("$d\a.atc")
  [Array]::Copy($Bytes, 0, $b, $Offset, $Bytes.Length)
  [System.IO.File]::WriteAllBytes("$d\bad.atc", $b)

  New-Item -ItemType Directory -Force "$d\out" | Out-Null
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $r = Invoke-Cli @("/de", "/p=$PW", "/y", "/dsaveto=$d\out", "$d\bad.atc")
  $sw.Stop()
  if ($r.ExitCode -ne $ExpectedExit) { return "exit=$($r.ExitCode) (期待 $ExpectedExit)" }
  # 範囲検証が効いていれば Argon2 を呼ばずに即座に落ちる
  if ($sw.Elapsed.TotalSeconds -gt 5) { return "拒否までに $([math]::Round($sw.Elapsed.TotalSeconds,1)) 秒かかった（検証前に鍵導出が走った疑い）" }
  return $null
}

Test-Case "D-08 改ざん: Argon2 メモリコスト過大 → 8" {
  Test-Tamper "d08" 52 ([BitConverter]::GetBytes([int]2147483647))
}
Test-Case "D-09 改ざん: Argon2 反復回数過大 → 8" {
  Test-Tamper "d09" 56 ([BitConverter]::GetBytes([int]2000000000))
}
Test-Case "D-10 改ざん: Argon2 並列度 0 → 8" {
  Test-Tamper "d10" 60 ([BitConverter]::GetBytes([int]0))
}
Test-Case "D-11 改ざん: 鍵サイズ不正 → 8" {
  Test-Tamper "d11" 64 ([BitConverter]::GetBytes([int16]30000))
}

Test-Case "D-12 切り詰められたファイル → 8" {
  $d = New-TestDir "d12"; New-TestFile "$d\a.txt"
  Invoke-Cli @("/en", "/p=$PW", "/y", "/out=$d\a.atc", "$d\a.txt") | Out-Null
  $b = [System.IO.File]::ReadAllBytes("$d\a.atc")
  [System.IO.File]::WriteAllBytes("$d\bad.atc", $b[0..99])
  New-Item -ItemType Directory -Force "$d\out" | Out-Null
  $r = Invoke-Cli @("/de", "/p=$PW", "/y", "/dsaveto=$d\out", "$d\bad.atc")
  if ($r.ExitCode -ne 8) { return "exit=$($r.ExitCode)" }
  return $null
}

Test-Case "D-13 暗号化ファイルでないものを復号 → 9" {
  $d = New-TestDir "d13"; New-TestFile "$d\a.txt"
  New-Item -ItemType Directory -Force "$d\out" | Out-Null
  $r = Invoke-Cli @("/de", "/p=$PW", "/y", "/dsaveto=$d\out", "$d\a.txt")
  if ($r.ExitCode -ne 9) { return "exit=$($r.ExitCode)" }
  return $null
}

# ZIP ではパスワードファイルが成立しない。受け取る側はエクスプローラーに
# パスワードを打ち込んで開くので、ファイルから導いた鍵は再現できない。
# 黙って無視して ZIP を作ると、利用者が指定した認証情報と違うもので保護してしまう。
Test-Case "D-14 ZIP にパスワードファイルを渡すと 12" {
  $d = New-TestDir "d14"; New-TestFile "$d\src\a.txt"
  Set-Content -Encoding utf8 "$d\key.bin" "some-key-material"

  # テキストのパスワードを併せて渡しても通さない（以前は黙って成功していた）
  $r = Invoke-Cli @("/en", "/entype=3", "/pfile=$d\key.bin", "/p=$PW", "/y", "/out=$d\x.zip", "$d\src")
  if ($r.ExitCode -ne 12) { return "テキストパスワード併用: exit=$($r.ExitCode)" }
  if (Test-Path "$d\x.zip") { return "断ったのに ZIP が作られている" }

  # テキストのパスワードが無い場合も同じ理由で 12
  $r = Invoke-Cli @("/en", "/entype=3", "/pfile=$d\key.bin", "/y", "/out=$d\y.zip", "$d\src")
  if ($r.ExitCode -ne 12) { return "パスワードファイルのみ: exit=$($r.ExitCode)" }
  return $null
}

# 範囲外の /zipalgo を黙って 0（ZipCrypto）へ倒すと、
# 呼び出し側の意図より弱い方式で暗号化してしまう。
Test-Case "D-15 /zipalgo の範囲外は 2" {
  $d = New-TestDir "d15"; New-TestFile "$d\src\a.txt"
  foreach ($algo in @("-1", "3", "9")) {
    $r = Invoke-Cli @("/en", "/entype=3", "/p=$PW", "/zipalgo=$algo", "/y", "/out=$d\o.zip", "$d\src")
    if ($r.ExitCode -ne 2) { return "zipalgo=$algo exit=$($r.ExitCode)" }
  }
  return $null
}

#======================================================================
Write-Host ""
Write-Host "E. 元ファイルの削除" -ForegroundColor White
#======================================================================

Test-Case "E-01 /del=1 通常削除" {
  $d = New-TestDir "e01"; New-TestFile "$d\a.txt"
  $r = Invoke-Cli @("/en", "/p=$PW", "/y", "/del=1", "/out=$d\a.atc", "$d\a.txt")
  if ($r.ExitCode -ne 0) { return "exit=$($r.ExitCode)" }
  if (Test-Path "$d\a.txt") { return "元ファイルが残っている" }
  return $null
}

Test-Case "E-02 /del=3 完全削除" {
  $d = New-TestDir "e02"; New-TestFile "$d\a.txt"
  $r = Invoke-Cli @("/en", "/p=$PW", "/y", "/del=3", "/out=$d\a.atc", "$d\a.txt")
  if ($r.ExitCode -ne 0) { return "exit=$($r.ExitCode)" }
  if (Test-Path "$d\a.txt") { return "元ファイルが残っている" }
  return $null
}

Test-Case "E-03 /del=2 ごみ箱（固定ドライブ）" {
  $d = New-TestDir "e03"
  $name = "atc_regress_recycle_" + (Get-Random) + ".txt"
  New-TestFile "$d\$name"
  $r = Invoke-Cli @("/en", "/p=$PW", "/y", "/del=2", "/out=$d\a.atc", "$d\$name")
  if ($r.ExitCode -ne 0) { return "exit=$($r.ExitCode) $($r.StdErr)" }
  if (Test-Path "$d\$name") { return "元ファイルが残っている" }
  try {
    $shell = New-Object -ComObject Shell.Application
    $found = $shell.Namespace(10).Items() | Where-Object { $_.Name -eq $name }
    if (-not $found) { return "ごみ箱に見つからない（完全削除された疑い）" }
  } catch {
    return "SKIP: ごみ箱を確認できませんでした"
  }
  return $null
}

Test-Case "E-04 /del=2 は UNC パスを拒否し元ファイルを残す" {
  $d = New-TestDir "e04"; New-TestFile "$d\a.txt"
  $unc = "\\localhost\" + $d.Substring(0, 1) + "$" + $d.Substring(2)
  if (-not (Test-Path $unc)) { return "SKIP: 管理共有にアクセスできません" }
  $r = Invoke-Cli @("/en", "/p=$PW", "/y", "/del=2", "/out=$d\a.atc", "$unc\a.txt")
  if ($r.ExitCode -ne 2) { return "exit=$($r.ExitCode) (期待 2)" }
  if (-not (Test-Path "$d\a.txt")) { return "拒否したのに元ファイルが消えている" }
  return $null
}

Test-Case "E-05 /delenc 復号後に暗号化ファイルを削除" {
  $d = New-TestDir "e05"; New-TestFile "$d\a.txt"
  Invoke-Cli @("/en", "/p=$PW", "/y", "/out=$d\a.atc", "$d\a.txt") | Out-Null
  New-Item -ItemType Directory -Force "$d\out" | Out-Null
  $r = Invoke-Cli @("/de", "/p=$PW", "/y", "/delenc=1", "/dsaveto=$d\out", "$d\a.atc")
  if ($r.ExitCode -ne 0) { return "exit=$($r.ExitCode)" }
  if (Test-Path "$d\a.atc") { return "暗号化ファイルが残っている" }
  return $null
}

#======================================================================
Write-Host ""
Write-Host "F. パスワードファイル・リスト・その他" -ForegroundColor White
#======================================================================

Test-Case "F-01 パスワードファイルでのラウンドトリップ" {
  $d = New-TestDir "f01"; New-TestFile "$d\src\a.txt"; New-TestFile "$d\key.bin" 3
  $before = Get-TreeDigest "$d\src"
  $r = Invoke-Cli @("/en", "/pfile=$d\key.bin", "/y", "/out=$d\a.atc", "$d\src\a.txt")
  if ($r.ExitCode -ne 0) { return "暗号化が失敗 (exit=$($r.ExitCode)) $($r.StdErr)" }
  New-Item -ItemType Directory -Force "$d\out" | Out-Null
  $r = Invoke-Cli @("/de", "/dpfile=$d\key.bin", "/y", "/nopfldr=1", "/dsaveto=$d\out", "$d\a.atc")
  if ($r.ExitCode -ne 0) { return "復号が失敗 (exit=$($r.ExitCode)) $($r.StdErr)" }
  if ((Get-TreeDigest "$d\out") -ne $before) { return "内容が一致しない" }
  return $null
}

Test-Case "F-02 パスワードファイルが 1 バイト違うと復号できない" {
  $d = New-TestDir "f02"; New-TestFile "$d\a.txt"; New-TestFile "$d\key.bin" 3
  Invoke-Cli @("/en", "/pfile=$d\key.bin", "/y", "/out=$d\a.atc", "$d\a.txt") | Out-Null
  $b = [System.IO.File]::ReadAllBytes("$d\key.bin"); $b[0] = [byte](($b[0] + 1) % 256)
  [System.IO.File]::WriteAllBytes("$d\key2.bin", $b)
  New-Item -ItemType Directory -Force "$d\out" | Out-Null
  $r = Invoke-Cli @("/de", "/dpfile=$d\key2.bin", "/y", "/dsaveto=$d\out", "$d\a.atc")
  if ($r.ExitCode -ne 4) { return "exit=$($r.ExitCode) (期待 4)" }
  return $null
}

Test-Case "F-03 /list でパス一覧を読み込む" {
  $d = New-TestDir "f03"
  New-TestFile "$d\src\a.txt"; New-TestFile "$d\src\b.txt"
  Set-Content "$d\list.txt" -Value @("src\a.txt", "src\b.txt") -Encoding UTF8
  $r = Invoke-Cli @("/en", "/p=$PW", "/y", "/list=$d\list.txt", "/out=$d\packed.atc")
  if ($r.ExitCode -ne 0) { return "exit=$($r.ExitCode) $($r.StdErr)" }
  if (-not (Test-Path "$d\packed.atc")) { return "暗号化ファイルが無い" }
  return $null
}

Test-Case "F-04 /list に存在しないパスがあると 2" {
  $d = New-TestDir "f04"
  Set-Content "$d\list.txt" -Value @("nosuchfile.txt") -Encoding UTF8
  $r = Invoke-Cli @("/en", "/p=$PW", "/list=$d\list.txt")
  if ($r.ExitCode -ne 2) { return "exit=$($r.ExitCode)" }
  return $null
}

Test-Case "F-05 --help は 0 で終了し使い方を出す" {
  $r = Invoke-Cli @("--help")
  if ($r.ExitCode -ne 0) { return "exit=$($r.ExitCode)" }
  # ヘルプは表示言語に従う（日本語は「書式:」、英語は「Usage:」）
  if ($r.StdOut -notmatch "USAGE|Usage:|書式:") { return "使い方が出ていない" }
  if ($r.StdOut -notmatch "EXIT CODES") { return "終了コード表が出ていない" }
  return $null
}

Test-Case "F-06 結果パスは標準出力へ出る" {
  $d = New-TestDir "f06"; New-TestFile "$d\a.txt"
  $r = Invoke-Cli @("/en", "/p=$PW", "/y", "/out=$d\a.atc", "$d\a.txt")
  if ($r.ExitCode -ne 0) { return "exit=$($r.ExitCode)" }
  if ($r.StdOut.Trim() -ne "$d\a.atc") { return "標準出力が想定と違う: '$($r.StdOut.Trim())'" }
  return $null
}

#======================================================================
Write-Host ""
Write-Host "G. 本体 GUI の経路" -ForegroundColor White
#======================================================================

Test-Case "G-01 GUI をコマンドラインで駆動して暗号化" {
  if ($NoGui) { return "SKIP: -NoGui のため" }
  $d = New-TestDir "g01"; New-TestFile "$d\src\a.txt"
  $code = Invoke-Gui @("/en=1", "/p=$PW", "/exit=1", "/ow=0", "$d\src\a.txt")
  if ($code -eq -999) { return "GUI が終了しませんでした（タイムアウト）" }
  if (-not (Test-Path "$d\src\a.atc")) { return "GUI が暗号化ファイルを作らなかった" }
  return $null
}

Test-Case "G-02 GUI が作ったファイルを CLI で復号できる" {
  if ($NoGui) { return "SKIP: -NoGui のため" }
  $d = New-TestDir "g02"; New-TestFile "$d\src\a.txt"
  $srcHash = (Get-FileHash "$d\src\a.txt" -Algorithm SHA256).Hash
  Invoke-Gui @("/en=1", "/p=$PW", "/exit=1", "/ow=0", "$d\src\a.txt") | Out-Null
  if (-not (Test-Path "$d\src\a.atc")) { return "GUI が暗号化ファイルを作らなかった" }
  New-Item -ItemType Directory -Force "$d\out" | Out-Null
  $r = Invoke-Cli @("/de", "/p=$PW", "/y", "/nopfldr=1", "/dsaveto=$d\out", "$d\src\a.atc")
  if ($r.ExitCode -ne 0) { return "復号が失敗 (exit=$($r.ExitCode))" }
  $f = Get-ChildItem "$d\out" -Recurse -File | Select-Object -First 1
  if (-not $f) { return "復号結果が無い" }
  if ((Get-FileHash $f.FullName -Algorithm SHA256).Hash -ne $srcHash) { return "内容が一致しない" }
  return $null
}

Test-Case "G-03 CLI が作ったファイルを GUI で復号できる" {
  if ($NoGui) { return "SKIP: -NoGui のため" }
  $d = New-TestDir "g03"; New-TestFile "$d\a.txt"
  $srcHash = (Get-FileHash "$d\a.txt" -Algorithm SHA256).Hash
  Invoke-Cli @("/en", "/p=$PW", "/y", "/out=$d\a.atc", "$d\a.txt") | Out-Null
  Remove-Item "$d\a.txt" -Force
  $code = Invoke-Gui @("/de=1", "/p=$PW", "/exit=1", "/ow=0", "/nopfldr=1", "$d\a.atc")
  if ($code -eq -999) { return "GUI が終了しませんでした（タイムアウト）" }
  $f = Get-ChildItem $d -Recurse -File -Filter "a.txt" | Select-Object -First 1
  if (-not $f) { return "GUI が復号結果を作らなかった" }
  if ((Get-FileHash $f.FullName -Algorithm SHA256).Hash -ne $srcHash) { return "内容が一致しない" }
  return $null
}

# 署名が "<?xml" に見えるだけの壊れたファイルを渡すと、CheckFileType の
# XElement.Load が XmlException を投げ、GUI 起動時にそのまま落ちていた。
# 終了コード -532462766 (0xE0434352) = .NET 未処理例外。
Test-Case "G-05 壊れた XML を渡しても GUI が落ちない" {
  if ($NoGui) { return "SKIP: -NoGui のため" }
  $d = New-TestDir "g05"
  New-Item -ItemType Directory -Force "$d\out" | Out-Null

  $cases = @{
    "broken.xml"    = '<?xml version="1.0"?><token>AttacheCase</token'
    "broken2.txt"   = '<?xml ve this is not xml at all >>><<<'
  }
  foreach ($name in $cases.Keys) {
    [System.IO.File]::WriteAllText("$d\$name", $cases[$name], (New-Object System.Text.UTF8Encoding($false)))
  }
  # BOM 付きの壊れた XML（署名 EF-BB-BF-3C-3F-78-6D-6C の経路）
  [System.IO.File]::WriteAllText("$d\bombroken.xml", '<?xml version="1.0"?><a><b></a>', (New-Object System.Text.UTF8Encoding($true)))

  foreach ($name in @("broken.xml", "broken2.txt", "bombroken.xml")) {
    $code = Invoke-Gui @("/en=1", "/p=$PW", "/exit=1", "/ow=0", "/saveto=$d\out", "$d\$name")
    if ($code -eq -999) { return "$name で GUI が終了しませんでした（タイムアウト）" }
    if ($code -ne 0) {
      return "$name で exit=$code（-532462766 は .NET 未処理例外）"
    }
  }
  return $null
}
Test-Case "G-04 GUI が廃止オプションでエラーにならない" {
  if ($NoGui) { return "SKIP: -NoGui のため" }
  $d = New-TestDir "g04"; New-TestFile "$d\a.txt"
  $code = Invoke-Gui @("/en=1", "/p=$PW", "/exit=1", "/ow=0", "/wmin=1", "/tskb=1", "$d\a.txt")
  if ($code -eq -999) { return "GUI が終了しませんでした（タイムアウト）" }
  if (-not (Test-Path "$d\a.atc")) { return "暗号化ファイルが作られなかった" }
  return $null
}

#======================================================================
Write-Host ""
Write-Host "H. Post-PPAP" -ForegroundColor White
#======================================================================

# 【重要】このグループは本番のキーサーバへ問い合わせる。
#
# 秘密鍵はサーバに存在しない（設計上、サーバが持つのは公開鍵だけ）。
# 復号に使う秘密鍵は %APPDATA%\AttacheCase\PostPPAP\accounts.json に
# DPAPI で「この PC・このユーザー」に紐付けて保存されている。
# したがって自己ラウンドトリップは、この PC で登録を済ませていないと成立しない。
#
#   /p-ppap=EMAIL  暗号化 … サーバから相手の公開鍵を引く（ネットワーク要／登録は不要）
#   /p-ppap        復号   … この PC の秘密鍵を使う（ネットワーク不要／登録が必要）

$script:AccountsJson = Join-Path $env:APPDATA "AttacheCase\PostPPAP\accounts.json"
$script:HasPostPpapAccount = Test-Path $script:AccountsJson

Test-Case "H-01 キーサーバから公開鍵を引いて暗号化" {
  if (-not $PostPpap) { return "SKIP: -PostPpap を付けたときだけ実行します（本番キーサーバを叩くため）" }
  if (-not $PostPpapEmail) { return "SKIP: -PostPpapEmail か環境変数 ATC_POSTPPAP_EMAIL が必要です" }

  $d = New-TestDir "h01"; New-TestFile "$d\src\a.txt"
  $r = Invoke-Cli @("/en", "/p-ppap=$PostPpapEmail", "/y", "/out=$d\a.atc", "$d\src\a.txt")
  if ($r.ExitCode -ne 0) { return "暗号化が失敗 (exit=$($r.ExitCode)) $($r.StdErr)" }
  if (-not (Test-Path "$d\a.atc")) { return "暗号化ファイルが無い" }

  # パスワード方式ではなく公開鍵方式のファイルになっていることを確かめる。
  # パスワードで開こうとすると、試行せずに案内を返すはず。
  New-Item -ItemType Directory -Force "$d\out" | Out-Null
  $r2 = Invoke-Cli @("/de", "/p=$PW", "/y", "/dsaveto=$d\out", "$d\a.atc")
  if ($r2.ExitCode -ne 2) { return "公開鍵方式のファイルになっていない (パスワード復号の exit=$($r2.ExitCode))" }
  if ($r2.StdErr -notmatch "p-ppap") { return "案内メッセージに /p-ppap が含まれない" }
  return $null
}

Test-Case "H-02 自分宛に暗号化して自分の秘密鍵で復号" {
  if (-not $PostPpap) { return "SKIP: -PostPpap を付けたときだけ実行します" }
  if (-not $PostPpapEmail) { return "SKIP: -PostPpapEmail か環境変数 ATC_POSTPPAP_EMAIL が必要です" }
  if (-not $script:HasPostPpapAccount) {
    return "SKIP: この PC に Post-PPAP アカウントが登録されていません ($script:AccountsJson)"
  }

  $d = New-TestDir "h02"; New-TestFile "$d\src\a.txt"
  $before = Get-TreeDigest "$d\src"

  $r = Invoke-Cli @("/en", "/p-ppap=$PostPpapEmail", "/y", "/out=$d\a.atc", "$d\src\a.txt")
  if ($r.ExitCode -ne 0) { return "暗号化が失敗 (exit=$($r.ExitCode)) $($r.StdErr)" }

  New-Item -ItemType Directory -Force "$d\out" | Out-Null
  $r = Invoke-Cli @("/de", "/p-ppap", "/y", "/nopfldr=1", "/dsaveto=$d\out", "$d\a.atc")
  if ($r.ExitCode -ne 0) { return "復号が失敗 (exit=$($r.ExitCode)) $($r.StdErr)" }
  if ((Get-TreeDigest "$d\out") -ne $before) { return "内容が一致しない" }
  return $null
}

Test-Case "H-03 未登録アドレスは見つからないと返す" {
  if (-not $PostPpap) { return "SKIP: -PostPpap を付けたときだけ実行します" }
  if (-not $PostPpapEmail) { return "SKIP: -PostPpapEmail か環境変数 ATC_POSTPPAP_EMAIL が必要です" }

  $d = New-TestDir "h03"; New-TestFile "$d\a.txt"
  $unknown = "does-not-exist-" + (Get-Random) + "@example.invalid"
  $r = Invoke-Cli @("/en", "/p-ppap=$unknown", "/y", "/out=$d\a.atc", "$d\a.txt")
  if ($r.ExitCode -eq 0) { return "未登録アドレスなのに暗号化が成功してしまった" }
  if ($r.ExitCode -ne 3) { return "exit=$($r.ExitCode) (期待 3: 公開鍵が見つからない)" }
  return $null
}

Test-Case "H-04 アカウント未登録なら復号は明示的に断る" {
  if (-not $PostPpap) { return "SKIP: -PostPpap を付けたときだけ実行します" }
  if (-not $PostPpapEmail) { return "SKIP: -PostPpapEmail か環境変数 ATC_POSTPPAP_EMAIL が必要です" }
  if ($script:HasPostPpapAccount) { return "SKIP: この PC にはアカウントが登録済みです" }

  $d = New-TestDir "h04"; New-TestFile "$d\a.txt"
  $r = Invoke-Cli @("/en", "/p-ppap=$PostPpapEmail", "/y", "/out=$d\a.atc", "$d\a.txt")
  if ($r.ExitCode -ne 0) { return "暗号化が失敗 (exit=$($r.ExitCode))" }

  New-Item -ItemType Directory -Force "$d\out" | Out-Null
  $r = Invoke-Cli @("/de", "/p-ppap", "/y", "/dsaveto=$d\out", "$d\a.atc")
  if ($r.ExitCode -ne 2) { return "exit=$($r.ExitCode) (期待 2)" }
  if ($r.StdErr -notmatch "Post-PPAP") { return "理由が説明されていない" }
  return $null
}

#======================================================================
# 結果
#======================================================================

$pass = @($script:Results | Where-Object { $_.Status -eq "PASS" }).Count
$fail = @($script:Results | Where-Object { $_.Status -eq "FAIL" }).Count
$skip = @($script:Results | Where-Object { $_.Status -eq "SKIP" }).Count

Write-Host ""
Write-Host ("=" * 60)
Write-Host ("  PASS: {0}   FAIL: {1}   SKIP: {2}" -f $pass, $fail, $skip)
Write-Host ("=" * 60)

if ($fail -gt 0) {
  Write-Host ""
  Write-Host "失敗したテスト:" -ForegroundColor Red
  $script:Results | Where-Object { $_.Status -eq "FAIL" } | ForEach-Object {
    Write-Host ("  {0}  -- {1}" -f $_.Name, $_.Detail) -ForegroundColor Red
  }
}

if ($KeepWork) {
  Write-Host ""
  Write-Host "作業フォルダーを残しました: $Work"
} else {
  Remove-Item $Work -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
if ($fail -gt 0) { exit 1 } else { exit 0 }
