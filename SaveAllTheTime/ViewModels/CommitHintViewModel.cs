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
        public ReactiveAsyncCommand RefreshStatus { get; protected set; }

        static CommitHintViewModel()
        {
            // NB: This is a bug in ReactiveUI :-/
            MessageBus.Current = new MessageBus();
        }

        public CommitHintViewModel(string filePath, IVisualStudioOps vsOps, IGitRepoOps gitRepoOps = null, IFilesystemWatchCache watchCache = null)
        {
            FilePath = filePath;
            watchCache = watchCache ?? _defaultWatchCache;
            _gitRepoOps = gitRepoOps ?? new GitRepoOps();

            this.WhenAny(x => x.FilePath, x => x.Value)
                .Where(x => !String.IsNullOrWhiteSpace(x))
                .Select(_gitRepoOps.FindGitRepo)
                .ToProperty(this, x => x.RepoPath, out _RepoPath);

            this.WhenAny(x => x.RepoPath, x => x.Value)
                .Where(x => !String.IsNullOrWhiteSpace(x))
                .Select(_gitRepoOps.ProtocolUrlForRepoPath)
                .ToProperty(this, x => x.ProtocolUrl, out _ProtocolUrl);

            var repoWatch = this.WhenAny(x => x.RepoPath, x => x.Value)
                .Where(x => !String.IsNullOrWhiteSpace(x))
                .Select(x => watchCache.Register(Path.Combine(x, ".git")).Select(_ => x))
                .Switch();

            Open = new ReactiveCommand(this.WhenAny(x => x.ProtocolUrl, x => !String.IsNullOrWhiteSpace(x.Value)));
            RefreshStatus = new ReactiveAsyncCommand(this.WhenAny(x => x.RepoPath, x => !String.IsNullOrWhiteSpace(x.Value)));

            repoWatch
                .Select(x => _gitRepoOps.LastCommitTime(x))
                .Select(x => x == null ? _gitRepoOps.ApplicationStartTime : x.Value)
                .StartWith(_gitRepoOps.ApplicationStartTime)
                .ToProperty(this, x => x.LastRepoCommitTime, out _LastRepoCommitTime);

            var commandSub = this.WhenAny(x => x.LastRepoCommitTime, _ => Unit.Default)
                .InvokeCommand(RefreshStatus);

            MessageBus.Current.Listen<Unit>("AnyDocumentChanged")
                .Timestamp(RxApp.MainThreadScheduler)
                .Select(x => x.Timestamp)
                .StartWith(_gitRepoOps.ApplicationStartTime)
                .Do(x => Debug.WriteLine(String.Format("Last Change: {0}", x)))
                .ToProperty(this, x => x.LastTextActiveTime, out _LastTextActiveTime);

            this.WhenAny(x => x.LastRepoCommitTime, x => x.LastTextActiveTime, (commit, active) => active.Value - commit.Value)
                .Where(x => x.Ticks > 0)
                .Select(LastCommitTimeToOpacity)
                .ToProperty(this, x => x.SuggestedOpacity, out _SuggestedOpacity, 1.0);

            var hintState = new Subject<CommitHintState>();
            hintState.ToProperty(this, x => x.HintState, out _HintState);

            this.WhenAny(x => x.SuggestedOpacity, x => x.Value)
                .Select(x => {
                    if (x >= 0.8) return CommitHintState.Red;
                    if (x >= 0.5) return CommitHintState.Yellow;
                    return CommitHintState.Green;
                })
                .Subscribe(hintState);

            Open.Subscribe(_ => vsOps.SaveAll());

            RefreshStatus.RegisterAsyncObservable(_ => _gitRepoOps.GetStatus(RepoPath))
                .ToProperty(this, x => x.LatestRepoStatus, out _LatestRepoStatus);

            // NB: Because _LastRepoCommitTime at the end of the day creates a
            // FileSystemWatcher, we have to dispose it or else we'll get FSW 
            // messages for evar.
            _inner = new CompositeDisposable(_LastRepoCommitTime, _LastTextActiveTime);
        }

        public double LastCommitTimeToOpacity(TimeSpan timeSinceLastCommit)
        {
            // http://fooplot.com/#W3sidHlwZSI6MCwiZXEiOiIwLjM1KmVeKDAuMDIwKngpIiwiY29sb3IiOiIjMDAwMDAwIn0seyJ0eXBlIjoxMDAwLCJ3aW5kb3ciOlsiMCIsIjY1IiwiMCIsIjIiXX1d
            var ret = 0.35 * Math.Exp(0.04 * timeSinceLastCommit.TotalMinutes);
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
