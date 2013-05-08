using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using Microsoft.Win32;
using ReactiveUI;
using System;

namespace SaveAllTheTime.Models
{
    public interface IGitRepoOps
    {
        DateTimeOffset ApplicationStartTime { get; }

        string ProtocolUrlForRepoPath(string repoPath);
        string FindGitRepo(string filePath);
        DateTimeOffset? LastCommitTime(string repoPath);
    }

    public class GitRepoOps : IGitRepoOps, IEnableLogger
    {
        static GitRepoOps()
        {
            _ApplicationStartTime = RxApp.DeferredScheduler.Now;
        }

        readonly static DateTimeOffset _ApplicationStartTime;
        public DateTimeOffset ApplicationStartTime {
            get { return _ApplicationStartTime; }
        }

        public string ProtocolUrlForRepoPath(string repoPath)
        {
            if (!isGitHubForWindowsInstalled()) return null;

            var remoteUrl = default(string);
            var repo = default(Repository);

            try {
                repo = new Repository(repoPath);
                var remote = repo.Network.Remotes.FirstOrDefault(x => x.Name.Equals("origin", StringComparison.OrdinalIgnoreCase));
                if (remote == null) return null;

                remoteUrl = remote.Url.ToLowerInvariant();
            } catch (Exception ex) {
                this.Log().WarnException("Failed to open repo: " + repoPath, ex);
                return null;
            } finally {
                if (repo != null) repo.Dispose();
            }

            return protocolUrlForRemoteUrl(remoteUrl);
        }

        public string FindGitRepo(string filePath)
        {
            lock (findGitRepoCache) {
                return findGitRepoCache.Get(filePath);
            }
        }

        public DateTimeOffset? LastCommitTime(string repoPath)
        {
            var repo = default(Repository);
            try {
                repo = new Repository(repoPath);
                if (repo.Head == null || repo.Head.Tip == null) return null;

                return repo.Head.Tip.Author.When;
            } catch (Exception ex) {
                this.Log().WarnException("Couldn't read commit time on repo: " + repoPath, ex);
            } finally {
                if (repo != null) repo.Dispose();
            }

            return null;
        }

        internal static string protocolUrlForRemoteUrl(string remoteUrl)
        {
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

        bool? isGhfwInstalled;
        bool isGitHubForWindowsInstalled()
        {
            if (isGhfwInstalled != null) return isGhfwInstalled.Value;

            try {
                var hkcu = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Default);
                hkcu.OpenSubKey("github-windows", RegistryKeyPermissionCheck.ReadSubTree);
            } catch (Exception ex) {
                this.Log().WarnException("Couldn't detect if GH4W is installed, bailing", ex);
                return (isGhfwInstalled = false).Value;
            }

            return (isGhfwInstalled = true).Value;
        }
    }
}