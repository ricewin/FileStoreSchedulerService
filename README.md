# FileStoreSchedulerService

- コンソール アプリ

## 概要

指定された EntryDirectory 以下のファイル（例: \*.ts）を定期的に検出して DestDirectory に移動するアプリケーションです。

1. サブディレクトリの相対構造は保持します。
1. 停止（Pause）期間を設定して、その期間は処理をスキップできます。
1. ターミナルで実行します。Windows サービス版（サービスとして常駐）としても使えます。
1. 設定は JSON（例: AppConfig.json）です。

## 実行環境

.NET 10.0 による実装なので、Runtime または SDK が必要

[Download .NET](https://dotnet.microsoft.com/download)

## 実行（コンソール）

1. exe と同階層に設定ファイル `AppConfig.json` を置く。
1. 設定ファイルは適宜変更。

### 実行

直接 `FileStoreSchedulerService.exe` を使う。

## Windows サービス化

管理者でターミナルから実行します。

> [!TIP]
> オプションとその値の間にスペースが必要です。
>
> `binPath= c:\` `start= auto`

### インストール

```bash
sc create FileStoreSchedulerService binPath= "C:\path\to\FileStoreSchedulerService.exe" start= auto
```

```bash
sc start FileStoreSchedulerService
```

### アンインストール

```bash
sc delete FileStoreSchedulerService
```

## 設定ファイル例 (JSON)

> [!TIP]
> バックスラッシュにエスケープが必要です。
>
> `D:\ -> D:\\` `\\network -> \\\\network`

```json
{
  "EntryDirectory": "D:\\Entry",
  "DestDirectory": "\\\\NAS\\Destination",
  "Patterns": ["*.ts"],
  "IntervalSeconds": 60,
  "Recursive": true,
  "MoveRetryCount": 1,
  "MoveRetryDelayMs": 2000,
  "PausePeriods": [
    {
      "Start": "00:00",
      "End": "07:00"
    }
  ]
}
```

## 注意

> [!IMPORTANT]
>
> - サービス実行時のアカウント（LocalSystem / 特定ユーザー）によってはネットワークドライブへのアクセスに制限があります。サービス実行ユーザーを調整してください。
> - 実運用ではログをファイルに出す、ファイルサイズ変化で書き込み中を判定する等の追加検討を推奨します。
