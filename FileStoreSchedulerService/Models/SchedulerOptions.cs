namespace FileStoreSchedulerService.Models
{
    public class SchedulerOptions
    {
        // 検出対象ルートディレクトリ
        public string EntryDirectory { get; set; } = string.Empty;
        // 移動先ルートディレクトリ
        public string DestDirectory { get; set; } = string.Empty;
        // 検出対象パターン (例: ["*.ts"])
        public List<string> Patterns { get; set; } = ["*.ts"];
        // 検出間隔 (秒)
        public int IntervalInSeconds { get; set; } = 60;
        // サブディレクトリも検出するか
        public bool Recursive { get; set; } = true;
        // ファイル移動リトライ回数
        public int MoveRetryCount { get; set; } = 1;
        // ファイル移動リトライ待機時間 (ミリ秒)
        public int MoveRetryDelayInMs { get; set; } = 1000;
        // 処理一時停止期間リスト
        public List<PausePeriod> PausePeriods { get; set; } = [];
    }
    public class PausePeriod
    {
        public string Start { get; set; } = "00:00";
        public string End { get; set; } = "00:00";
        public TimeSpan StartTime => TimeSpan.Parse(Start);
        public TimeSpan EndTime => TimeSpan.Parse(End);
    }
}
