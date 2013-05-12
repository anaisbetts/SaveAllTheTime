using System.Reactive.Linq;
using LibGit2Sharp;
using Microsoft.Win32;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;
using SaveAllTheTime.Models;
using System.Threading;
using System.Reactive.Disposables;
using System.Reactive;
using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Subjects;

namespace SaveAllTheTime.ViewModels
{
    public enum CommitHintState {
        Green,
        Yellow,
        Red,
        Error,
    };

    public class CommitHintViewModel : ReactiveObject, IDisposable
    {
        static readonly IFilesystemWatchCache _defaultWatchCache = new FilesystemWatchCache();
        readonly IGitRepoOps _gitRepoOps;
        IDisposable _inner;

        public string FilePath { get; protected set; }
        public UserSettings UserSettings { get; protected set; }
        public bool IsGitHubForWindowsInstalled { get; protected set; }

        double? _MinutesTimeOverride;
        public double? MinutesTimeOverride {
            get { return _MinutesTimeOverride; }
            set { this.RaiseAndSetIfChanged(ref _MinutesTimeOverride, value); }
        }

        ObservableAsPropertyHelper<string> _RepoPath;
        public string RepoPath {
            get { return _RepoPath.Value; }
        }

        ObservableAsPropertyHelper<string> _ProtocolUrl;
        public string ProtocolUrl {
            get { return _ProtocolUrl.Value; }
        }

        ObservableAsPropertyHelper<DateTimeOffset> _LastRepoCommitTime;
        public DateTimeOffset LastRepoCommitTime {
            get { return _LastRepoCommitTime.Value; }
        }

        ObservableAsPropertyHelper<DateTimeOffset> _LastTextActiveTime;
        public DateTimeOffset LastTextActiveTime {
            get { return _LastTextActiveTime.Value; }
        }

        ObservableAsPropertyHelper<CommitHintState> _HintState;
        public CommitHintState HintState {
            get { return _HintState.Value; }
        }

        ObservableAsPropertyHelper<double> _SuggestedOpacity;
        public double SuggestedOpacity {
            get { return _SuggestedOpacity.Value; }
        }

        ObservableAsPropertyHelper<RepositoryStatus> _LatestRepoStatus;
        public RepositoryStatus LatestRepoStatus {
            get { return _LatestRepoStatus.Value; }
        }

        public ReactiveCommand Open { get; protected set; }
        public ReactiveCommand GoAway { get; protected set; }
        public ReactiveAsyncCommand RefreshStatus { get; protected set; }
        public ReactiveAsyncCommand RefreshLastCommitTime { get; protected set; }

        public CommitHintViewModel(string filePath, IVisualStudioOps vsOps, UserSettings settings = null, IGitRepoOps gitRepoOps = null, IFilesystemWatchCache watchCache = null)
        {
            FilePath = filePath;
            watchCache = watchCache ?? _defaultWatchCache;
            _gitRepoOps = gitRepoOps ?? new GitRepoOps();
            UserSettings = settings ?? new UserSettings();

            IsGitHubForWindowsInstalled = _gitRepoOps.IsGitHubForWindowsInstalled();

            this.Log().Info("Starting Commit Hint for {0}", filePath);

            this.WhenAny(x => x.FilePath, x => x.Value)
                .Where(x => !String.IsNullOrWhiteSpace(x))
                .Select(_gitRepoOps.FindGitRepo)
                .ToProperty(this, x => x.RepoPath, out _RepoPath);

            this.WhenAny(x => x.RepoPath, x => x.Value)
                .Where(x => !String.IsNullOrWhiteSpace(x))
                .Select(_gitRepoOps.ProtocolUrlForRepoPath)
                .ToProperty(this, x => x.ProtocolUrl, out _ProtocolUrl);

            Open = new ReactiveCommand(this.WhenAny(x => x.ProtocolUrl, x => !String.IsNullOrWhiteSpace(x.Value)));
            GoAway = new ReactiveCommand();
            RefreshStatus = new ReactiveAsyncCommand(this.WhenAny(x => x.RepoPath, x => !String.IsNullOrWhiteSpace(x.Value)));
            RefreshLastCommitTime = new ReactiveAsyncCommand(this.WhenAny(x => x.RepoPath, x => !String.IsNullOrWhiteSpace(x.Value)));

            var repoWatchSub = this.WhenAny(x => x.RepoPath, x => x.Value)
                .Where(x => !String.IsNullOrWhiteSpace(x))
                .Select(x => watchCache.Register(Path.Combine(x, ".git", "refs")).Select(_ => x))
                .Switch()
                .InvokeCommand(RefreshLastCommitTime);

            RefreshLastCommitTime.RegisterAsyncObservable(_ => _gitRepoOps.LastCommitTime(RepoPath))
                .StartWith(_gitRepoOps.ApplicationStartTime)
                .ToProperty(this, x => x.LastRepoCommitTime, out _LastRepoCommitTime);

            MessageBus.Current.Listen<Unit>("AnyDocumentChanged")
                .Timestamp(RxApp.MainThreadScheduler)
                .Select(x => x.Timestamp)
                .StartWith(_gitRepoOps.ApplicationStartTime)
                .ToProperty(this, x => x.LastTextActiveTime, out _LastTextActiveTime);

            var refreshDisp = this.WhenAny(x => x.LastTextActiveTime, x => Unit.Default)
                .Buffer(TimeSpan.FromSeconds(5), RxApp.TaskpoolScheduler)
                .StartWith(new List<Unit> { Unit.Default })
                .ObserveOn(RxApp.MainThreadScheduler)
                .InvokeCommand(RefreshStatus);

            this.WhenAny(x => x.LastRepoCommitTime, x => x.LastTextActiveTime, x => x.MinutesTimeOverride, (commit, active, _) => active.Value - commit.Value)
                .Select(x => x.Ticks < 0 ? TimeSpan.Zero : x)
                .Select(x => MinutesTimeOverride != null ? TimeSpan.FromMinutes(MinutesTimeOverride.Value) : x)
                .Select(x => LastCommitTimeToOpacity(x))
                .ToProperty(this, x => x.SuggestedOpacity, out _SuggestedOpacity, 1.0);

            var hintState = new Subject<CommitHintState>();
            hintState.ToProperty(this, x => x.HintState, out _HintState);

            Open.Subscribe(_ => vsOps.SaveAll());

            RefreshStatus.RegisterAsyncObservable(_ => _gitRepoOps.GetStatus(RepoPath))
                .ToProperty(this, x => x.LatestRepoStatus, out _LatestRepoStatus);

            this.WhenAny(x => x.SuggestedOpacity, x => x.LatestRepoStatus, (opacity, status) => new { Opacity = opacity.Value, Status = status.Value })
                .Select(x => {
                    if (x.Status == null) return CommitHintState.Green;
                    if (!x.Status.Added.Any() &&
                        !x.Status.Removed.Any() &&
                        !x.Status.Modified.Any() &&
                        !x.Status.Missing.Any()) return CommitHintState.Green;

                    if (x.Opacity >= 0.95) return CommitHintState.Red;
                    if (x.Opacity >= 0.6) return CommitHintState.Yellow;
                    return CommitHintState.Green;
                })
                .Subscribe(hintState);

            // NB: Because _LastRepoCommitTime at the end of the day creates a
            // FileSystemWatcher, we have to dispose it or else we'll get FSW 
            // messages for evar.
            _inner = new CompositeDisposable(repoWatchSub, _LastRepoCommitTime, _LastTextActiveTime);
        }

        public double LastCommitTimeToOpacity(TimeSpan timeSinceLastCommit)
        {
            // http://fooplot.com/#W3sidHlwZSI6MCwiZXEiOiIwLjM1KmVeKDAuMDIwKngpIiwiY29sb3IiOiIjMDAwMDAwIn0seyJ0eXBlIjoxMDAwLCJ3aW5kb3ciOlsiMCIsIjY1IiwiMCIsIjIiXX1d
            var ret = 0.35 * Math.Exp(0.02 * timeSinceLastCommit.TotalMinutes);
            return clamp(ret, 0.0, 1.0);
        }

        double clamp(double value, double min, double max)
        {
            if (value < min) return min;
            return Math.Min(max, value);
        }

        public void Dispose()
        {
            var disp = Interlocked.Exchange(ref _inner, null);
            if (disp != null) disp.Dispose();
        }
    }
}
