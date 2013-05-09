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

namespace SaveAllTheTime.ViewModels
{
    public enum CommitHintState {
        Loading,
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

        ObservableAsPropertyHelper<CommitHintState> _HintState;
        public CommitHintState HintState {
            get { return _HintState.Value; }
        }

        public ReactiveCommand Open { get; protected set; }

        public CommitHintViewModel(string filePath, IGitRepoOps gitRepoOps = null, IFilesystemWatchCache watchCache = null)
        {
            FilePath = filePath;
            watchCache = watchCache ?? _defaultWatchCache;
            _gitRepoOps = gitRepoOps ?? new GitRepoOps();

            this.WhenAny(x => x.FilePath, x => x.Value)
                .Select(_gitRepoOps.FindGitRepo)
                .ToProperty(this, x => x.RepoPath, out _RepoPath);

            this.WhenAny(x => x.RepoPath, x => x.Value)
                .Where(x => !String.IsNullOrWhiteSpace(x))
                .Select(_gitRepoOps.ProtocolUrlForRepoPath)
                .ToProperty(this, x => x.ProtocolUrl, out _ProtocolUrl);

            var repoWatch = this.WhenAny(x => x.RepoPath, x => x.Value)
                .Where(x => !String.IsNullOrWhiteSpace(x))
                .Select(x => watchCache.Register(x).Select(_ => x))
                .Switch();
            
            repoWatch
                .Select(x => _gitRepoOps.LastCommitTime(x))
                .Select(x => x == null ? _gitRepoOps.ApplicationStartTime :
                    (_gitRepoOps.ApplicationStartTime > x.Value ? _gitRepoOps.ApplicationStartTime : x.Value))
                .ToProperty(this, x => x.LastRepoCommitTime, out _LastRepoCommitTime);

            // TODO
            Observable.Return(CommitHintState.Green)
                .ToProperty(this, x => x.HintState, out _HintState);

            Open = new ReactiveCommand(this.WhenAny(x => x.ProtocolUrl, x => !String.IsNullOrWhiteSpace(x.Value)));

            // NB: Because _LastRepoCommitTime at the end of the day creates a
            // FileSystemWatcher, we have to dispose it or else we'll get FSW 
            // messages for evar.
            _inner = _LastRepoCommitTime;
        }

        public void Dispose()
        {
            var disp = Interlocked.Exchange(ref _inner, null);
            if (disp != null) disp.Dispose();
        }
    }
}
