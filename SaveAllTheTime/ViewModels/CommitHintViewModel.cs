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

namespace SaveAllTheTime.ViewModels
{
    public class CommitHintViewModel : ReactiveObject
    {
        readonly IGitRepoOps _gitRepoOps;

        public string FilePath { get; protected set; }

        ObservableAsPropertyHelper<string> _RepoPath;
        public string RepoPath {
            get { return _RepoPath.Value; }
        }

        ObservableAsPropertyHelper<string> _ProtocolUrl;
        public string ProtocolUrl {
            get { return _ProtocolUrl.Value; }
        }

        Brush _ForegroundBrush = new SolidColorBrush(Color.FromRgb(255, 0, 0));
        public Brush ForegroundBrush {
            get { return _ForegroundBrush; }
            set { this.RaiseAndSetIfChanged(ref _ForegroundBrush, value); }
        }

        public ReactiveCommand Open { get; protected set; }

        public CommitHintViewModel(string filePath, IGitRepoOps gitRepoOps = null)
        {
            FilePath = filePath;
            _gitRepoOps = gitRepoOps ?? new GitRepoOps();

            this.WhenAny(x => x.FilePath, x => x.Value)
                .Select(_gitRepoOps.FindGitRepo)
                .ToProperty(this, x => x.RepoPath, out _RepoPath);

            this.WhenAny(x => x.RepoPath, x => x.Value)
                .Where(x => !String.IsNullOrWhiteSpace(x))
                .Select(_gitRepoOps.ProtocolUrlForRepoPath)
                .ToProperty(this, x => x.ProtocolUrl, out _ProtocolUrl);

            Open = new ReactiveCommand(this.WhenAny(x => x.RepoPath, x => !String.IsNullOrWhiteSpace(x.Value)));
        }
    }
}
