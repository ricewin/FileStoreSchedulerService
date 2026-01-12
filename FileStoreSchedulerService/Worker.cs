using FileStoreSchedulerService.Models;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace FileStoreSchedulerService
{
    internal class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly SchedulerOptions _options;
        private readonly List<Regex> _compiledPatterns;
        public Worker(ILogger<Worker> logger, IOptionsMonitor<SchedulerOptions> options)
        {
            _logger = logger;
            _options = options.CurrentValue;
            _compiledPatterns = [.. _options.Patterns
                .Select(p => new Regex("^" + Regex.Escape(p).Replace("\\*", ".*") + "$",
                    RegexOptions.Compiled | RegexOptions.IgnoreCase))];
        }

        bool MatchesPattern(string filePath)
        {
            string name = Path.GetFileName(filePath);
            return _compiledPatterns.Any(r => r.IsMatch(name));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    """
                    [SchedulerService::BOOT]
                    > EntryDirectory: {Entry}
                    > DestDirectory : {Dest}
                    > PausePeriods  : {Start} - {End}
                    [SYSTEM READY]
                    """,
                    _options.EntryDirectory,
                    _options.DestDirectory,
                    _options.PausePeriods[0].StartTime,
                    _options.PausePeriods[0].EndTime
                   );
            }

            if (string.IsNullOrWhiteSpace(_options.EntryDirectory) || string.IsNullOrWhiteSpace(_options.DestDirectory))
            {
                _logger.LogError("EntryDirectory or DestDirectory is not set. SchedulerService is stopping.");
                return;
            }

            SearchOption searchOption = _options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (IsInPausePeriod())
                    {
                        if (_logger.IsEnabled(LogLevel.Debug))
                        {
                            _logger.LogDebug("Currently in pause period. Skipping this cycle.");
                        }
                    }
                    await ProcessOnce(searchOption, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during file processing");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_options.IntervalInSeconds), stoppingToken);
                }
                catch (TaskCanceledException) { }
            }
        }

        private bool IsInPausePeriod()
        {
            if (_options.PausePeriods == null || _options.PausePeriods.Count == 0) return false;

            TimeSpan now = DateTime.Now.TimeOfDay;
            foreach (PausePeriod period in _options.PausePeriods)
            {

                if (period.StartTime <= period.EndTime)
                {
                    // 通常の時間帯
                    if (now >= period.StartTime && now <= period.EndTime) return true;
                }
                else
                {
                    // 日跨ぎの時間帯
                    if (now >= period.StartTime || now <= period.EndTime) return true;
                }
            }
            return false;
        }

        private async Task ProcessOnce(SearchOption searchOption, CancellationToken stoppingToken)
        {
            string entryDir = Path.GetFullPath(_options.EntryDirectory);
            string destDir = Path.GetFullPath(_options.DestDirectory);

            if (!Directory.Exists(entryDir))
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning("Entry directory '{Entry}' does not exist. Skipping processing.", entryDir);
                }
                return;
            }

            Directory.CreateDirectory(destDir);

            IEnumerable<string> allFiles = Directory.EnumerateFiles(entryDir, "*", searchOption);

            List<string> foundFiles = allFiles
                .Where(f => MatchesPattern(f))
                .OrderBy(f => f)
                .ToList();

            if (foundFiles.Count == 0) return;

            try
            {
                foreach (string? srcPath in foundFiles)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    try
                    {
                        string relativePath = Path.GetRelativePath(entryDir, srcPath);
                        string destPath = Path.Combine(destDir, relativePath);
                        string destDirectory = Path.GetDirectoryName(destPath) ?? destDir;

                        if (!Directory.Exists(destDirectory))
                        {
                            Directory.CreateDirectory(destDirectory);
                        }

                        bool moved = await TryMoveWithRetriesAsync(srcPath, destPath, stoppingToken);
                        if (moved)
                        {
                            if (_logger.IsEnabled(LogLevel.Information))
                            {
                                _logger.LogInformation("Moved file '{File}' => '{Dest}'", srcPath, destPath);
                            }
                        }
                        else
                        {
                            if (_logger.IsEnabled(LogLevel.Warning))
                            {
                                _logger.LogWarning("Failed to move file '{File}' to '{Dest}' after {Attempts} attempts", srcPath, destPath, _options.MoveRetryCount);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (_logger.IsEnabled(LogLevel.Error))
                        {
                            _logger.LogError(ex, "Error moving file '{File}'", srcPath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (_logger.IsEnabled(LogLevel.Error))
                {
                    _logger.LogError(ex, "Error enumerating files in directory '{Entry}'", entryDir);
                }
            }
        }

        private async Task<bool> TryMoveWithRetriesAsync(string srcPath, string destPath, CancellationToken cancellationToken)
        {
            string attemptedDest = destPath;

            for (int attempt = 0; attempt < _options.MoveRetryCount; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (File.Exists(attemptedDest))
                    {
                        attemptedDest = GetUniqueDestPath(attemptedDest);
                    }

                    using (FileStream sourceStream = new(srcPath, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        // ファイルがロックされていないことを確認
                    }

                    File.Move(srcPath, attemptedDest);
                    return true;
                }
                catch (IOException ioe)
                {
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug(ioe, "IOException when moving file '{File}' to '{Dest}' on attempt {Attempt}", srcPath, attemptedDest, attempt + 1);
                    }

                    if (attempt < _options.MoveRetryCount)
                    {
                        // ファイルがロックされている可能性があるため、リトライ
                        await Task.Delay(_options.MoveRetryDelayInMs, cancellationToken);
                        continue;
                    }
                    return false;
                }
                catch (UnauthorizedAccessException uae)
                {
                    if (_logger.IsEnabled(LogLevel.Warning))
                    {
                        _logger.LogWarning(uae, "UnauthorizedAccessException when moving file '{File}' to '{Dest}' on attempt {Attempt}", srcPath, attemptedDest, attempt + 1);
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    if (_logger.IsEnabled(LogLevel.Error))
                    {
                        _logger.LogError(ex, "Failed to move file '{File}' to '{Dest}' after {Attempts} attempts", srcPath, destPath, attempt + 1);
                    }
                    return false;
                }
            }
            return false;
        }

        private static string GetUniqueDestPath(string destPath) => destPath + "." + Guid.NewGuid().ToString("N");
    }
}
