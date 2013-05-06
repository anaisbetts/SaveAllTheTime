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

namespace SaveAllTheTime.ViewModels
{
    public class CommitHintViewModel : ReactiveObject
    {
        public string FilePath { get; protected set; }

        ObservableAsPropertyHelper<string> _RepoPath;
        public string RepoPath {
            get { return _RepoPath.Value; }
        }

        ObservableAsPropertyHelper<string> _ProtocolUrl;
        public string ProtocolUrl {
            get { return _ProtocolUrl.Value; }
        }

        public ReactiveCommand Open { get; protected set; }

        public CommitHintViewModel(string filePath)
        {
            FilePath = filePath;

            this.WhenAny(x => x.FilePath, x => x.Value)
                .Select(findGitRepo)
                .ToProperty(this, x => x.RepoPath, out _RepoPath);

            this.WhenAny(x => x.RepoPath, x => x.Value)
                .Where(x => !String.IsNullOrWhiteSpace(x))
                .Select(protocolUrlForRepoPath)
                .ToProperty(this, x => x.ProtocolUrl, out _ProtocolUrl);

            Open = new ReactiveCommand(this.WhenAny(x => x.RepoPath, x => !String.IsNullOrWhiteSpace(x.Value)));
        }
        
        string protocolUrlForRepoPath(string repoPath)
        {
            if (isGitHubForWindowsInstalled()) return null;

            var remoteUrl = default(string);
            using (var repo = new Repository(repoPath)) {
                var remote = repo.Network.Remotes.FirstOrDefault(x => x.Name.Equals("origin", StringComparison.OrdinalIgnoreCase));
                if (remote == null) return null;

                remoteUrl = remote.Url.ToLowerInvariant();
            }


            // Either https://github.com/reactiveui/ReactiveUI.git or
            // git@github.com:reactiveui/ReactiveUI.git

            var nwo = default(string);
            if (remoteUrl.StartsWith("https://github.com")) {
                nwo = remoteUrl.Replace("https://github.com/", "");
            } else if (remoteUrl.StartsWith("git@github.com")) {
                nwo = remoteUrl.Replace("git@github.com:", "");
            }

            if (nwo == null) {
                return null;
            }
                
            nwo = (new Regex(".git$")).Replace(nwo, "");
            return String.Format("github-windows://openRepo/https://github.com/{0}", nwo);
        }

        bool? isGhfwInstalled;
        bool isGitHubForWindowsInstalled()
        {
            if (isGhfwInstalled != null) return isGhfwInstalled.Value;

            try {
                var hkcu = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Default);
                hkcu.OpenSubKey("github-windows", RegistryKeyPermissionCheck.ReadSubTree);
            } catch (Exception) {
                return (isGhfwInstalled = false).Value;
            }

            return (isGhfwInstalled = true).Value;
        }

        string findGitRepo(string filePath)
        {
            var di = new DirectoryInfo(Path.GetDirectoryName(filePath));
            while (di != null) {
                if ((new DirectoryInfo(di.FullName + "\\.git")).Exists) {
                    return di.FullName;
                }

                di = di.Parent;
            }

            return null;
        }
    }
}
