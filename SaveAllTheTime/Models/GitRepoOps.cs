using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using Microsoft.Win32;
using ReactiveUI;
using System;
using System.Reactive.Linq;

namespace SaveAllTheTime.Models
{
    public interface IGitRepoOps
    {
        DateTimeOffset ApplicationStartTime { get; }

        bool IsGitHubForWindowsInstalled();
        string ProtocolUrlForRepoPath(string repoPath);
        string FindGitRepo(string filePath);
        IObservable<DateTimeOffset> LastCommitTime(string repoPath);
        IObservable<RepositoryStatus> GetStatus(string repoPath);
    }

    public class GitRepoOps : IGitRepoOps, IEnableLogger
    {
        static GitRepoOps()
        {
            _ApplicationStartTime = RxApp.MainThreadScheduler.Now;
        }

        readonly static DateTimeOffset _ApplicationStartTime;
        public DateTimeOffset ApplicationStartTime {
            get { return _ApplicationStartTime; }
        }

        public string ProtocolUrlForRepoPath(string repoPath)
        {
            var remoteUrl = default(string);
            var repo = default(Repository);

            try {
                repo = new Repository(repoPath);
                var remote = repo.Network.Remotes.FirstOrDefault(x => x.Name.Equals("origin", StringComparison.OrdinalIgnoreCase));
                if (remote == null) return null;

                this.Log().Info("Using remote {0} for repo {1}", remote.Url, repoPath);
                remoteUrl = remote.Url.ToLowerInvariant();
            } catch (Exception ex) {
                this.Log().WarnException("Failed to open repo: " + repoPath, ex);
                return null;
            } finally {
                if (repo != null) repo.Dispose();
            }

            var ret = protocolUrlForRemoteUrl(remoteUrl);
            this.Log().Info("Protocol URL for {0} is {1}", repoPath, ret);
            return ret;
        }

        public string FindGitRepo(string filePath)
        {
            if (filePath == null) return null;

            lock (findGitRepoCache) {
                return findGitRepoCache.Get(filePath);
            }
        }

        public IObservable<DateTimeOffset> LastCommitTime(string repoPath)
        {
            return Observable.Start(() => {
                var repo = default(Repository);
                try {
                    repo = new Repository(repoPath);
                    if (repo.Head == null || repo.Head.Tip == null) {
                        throw new Exception("Couldn't find commit");
                    }

                    this.Log().Debug("Last Commit Time: {0}", repo.Head.Tip.Author.When);
                    return repo.Head.Tip.Author.When;
                } catch (Exception ex) {
                    this.Log().WarnException("Couldn't read commit time on repo: " + repoPath, ex);
                    throw;
                } finally {
                    if (repo != null) repo.Dispose();
                }
            }, RxApp.TaskpoolScheduler);
        }

        public IObservable<RepositoryStatus> GetStatus(string repoPath)
        {
            return Observable.Start(() => {
                var repo = default(Repository);
                try {
                    repo = new Repository(repoPath);
                    return repo.Index.RetrieveStatus();
                } catch (Exception ex) {
                    this.Log().WarnException("Couldn't read status for repo: " + repoPath, ex);
                    throw;
                } finally {
                    if (repo != null) repo.Dispose();
                }
            }, RxApp.TaskpoolScheduler);
        }

        bool? isGhfwInstalled;
        public bool IsGitHubForWindowsInstalled()
        {
            if (isGhfwInstalled != null) return isGhfwInstalled.Value;

            try {
                var hkcu = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Default);
                hkcu.OpenSubKey("github-windows", RegistryKeyPermissionCheck.ReadSubTree);
            } catch (Exception ex) {
                this.Log().WarnException("Couldn't detect if GH4W is installed, bailing", ex);
                return (isGhfwInstalled = false).Value;
            }

            this.Log().Info("GH4W is installed, rad");
            return (isGhfwInstalled = true).Value;
        }

        internal string protocolUrlForRemoteUrl(string remoteUrl)
        {
            try {
                var uri = new Uri(String.Format("github-windows://openRepo/{0}", remoteUrl));
                return uri.ToString();
            } catch (Exception ex) {
                this.Log().Warn("Tried to use bogus remote URL: " + remoteUrl, ex);
                return null;
            }
        }

        static MemoizingMRUCache<string, string> findGitRepoCache = new MemoizingMRUCache<string, string>(
            (x, _) => findGitRepo(x), 
            50);

        static string findGitRepo(string filePath)
        {
            if (String.IsNullOrWhiteSpace(filePath)) return null;

            var fi = new FileInfo(filePath);
            if (!fi.Exists) return null;

            var di = fi.Directory;
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