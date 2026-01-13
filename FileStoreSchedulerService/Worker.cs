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
                .Select(static p => new Regex("^" + Regex.Escape(p).Replace("\\*", ".*") + "$",
                    RegexOptions.Compiled | RegexOptions.IgnoreCase))];
        }

        private bool MatchesPattern(string filePath)
        {
            var name = Path.GetFileName(filePath);
            return _compiledPatterns.Any(r => r.IsMatch(name));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    """
                    [SchedulerService::BOOT...READY]
                    > EntryDirectory: {Entry}
                    > DestDirectory : {Dest}
                    > PausePeriods  : {Start} - {End}
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

            var now = DateTime.Now.TimeOfDay;
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
            var entryDir = Path.GetFullPath(_options.EntryDirectory);
            var destDir = Path.GetFullPath(_options.DestDirectory);

            if (!Directory.Exists(entryDir))
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning("Entry directory '{Entry}' does not exist. Skipping processing.", entryDir);
                }
                return;
            }

            Directory.CreateDirectory(destDir);

            var allFiles = Directory.EnumerateFiles(entryDir, "*", searchOption);

            List<string> foundFiles = [.. allFiles
                .Where(MatchesPattern).OrderBy(static f => f)];

            if (foundFiles.Count == 0) return;

            try
            {
                foreach (var srcPath in foundFiles)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    try
                    {
                        var relativePath = Path.GetRelativePath(entryDir, srcPath);
                        var destPath = Path.Combine(destDir, relativePath);
                        var destDirectory = Path.GetDirectoryName(destPath) ?? destDir;

                        if (!Directory.Exists(destDirectory))
                        {
                            Directory.CreateDirectory(destDirectory);
                        }

                        var moved = await TryMoveWithRetriesAsync(srcPath, destPath, stoppingToken);
                        if (moved)
                        {
                            if (_logger.IsEnabled(LogLevel.Information))
                            {
                                _logger.LogInformation("Moved file '{File}' → '{Dest}'", srcPath, destPath);
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
            var attemptedDest = destPath;

            for (var attempt = 0; attempt < _options.MoveRetryCount; attempt++)
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
