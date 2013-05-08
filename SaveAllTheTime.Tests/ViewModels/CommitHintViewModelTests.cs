using System.Reactive.Linq;
using NSubstitute;
using ReactiveUI;
using SaveAllTheTime.Models;
using SaveAllTheTime.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Concurrency;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Extensions;
using System.Reactive.Disposables;

namespace SaveAllTheTime.Tests.ViewModels
{
    public class CommitHintViewModelTests : IEnableLogger
    {
        [Fact]
        public void CantOpenWhenThereIsNoGitRepo()
        {
            var ops = Substitute.For<IGitRepoOps>();
            var filename = @"C:\Foo\Bar\Baz.txt";

            ops.FindGitRepo(filename).Returns(default(string));

            var watch = Substitute.For<IFilesystemWatchCache>();
            watch.Register(null).ReturnsForAnyArgs(Observable.Never<string>());

            RxApp.InUnitTestRunner();
            var fixture = new CommitHintViewModel(filename, ops, watch);

            this.Log().Info("Protocol URL: {0}", fixture.ProtocolUrl);
            Assert.False(fixture.Open.CanExecute(null));
        }

        [Fact]
        public void CantOpenWhenThereIsNoRemote()
        {
            var ops = Substitute.For<IGitRepoOps>();
            var filename = @"C:\Foo\Bar\Baz.txt";

            ops.FindGitRepo(filename).Returns(@"C:\Foo");
            ops.ProtocolUrlForRepoPath(@"C:\Foo").Returns(default(string));

            var watch = Substitute.For<IFilesystemWatchCache>();
            watch.Register(null).ReturnsForAnyArgs(Observable.Never<string>());

            var fixture = new CommitHintViewModel(filename, ops, watch);

            this.Log().Info("Protocol URL: {0}", fixture.ProtocolUrl);
            Assert.False(fixture.Open.CanExecute(null));
        }

        [Fact]
        public void MakeSureWeDisposeFileSystemWatcher()
        {
            var ops = Substitute.For<IGitRepoOps>();
            var filename = @"C:\Foo\Bar\Baz.txt";

            ops.FindGitRepo(filename).Returns(@"C:\Foo");
            ops.ProtocolUrlForRepoPath(@"C:\Foo").Returns(default(string));

            var subscriptionCount = 0;
            var countingObs = Observable.Create<string>(subj => {
                subscriptionCount++;
                return Disposable.Create(() => subscriptionCount--);
            });

            var watch = Substitute.For<IFilesystemWatchCache>();
            watch.Register(null).ReturnsForAnyArgs(countingObs);
            Assert.Equal(0, subscriptionCount);

            var fixture = new CommitHintViewModel(filename, ops, watch);
            Assert.Equal(1, subscriptionCount);

            fixture.Dispose();
            Assert.Equal(0, subscriptionCount);
        }
    }

    public class CommitHintViewModelSlowTests : IEnableLogger
    {
        [Fact]
        public void CanOpenThisRepo()
        {
            var st = new StackTrace(0, true);
            var filename = st.GetFrame(0).GetFileName();

            var fixture = new CommitHintViewModel(filename);
            Assert.True(fixture.Open.CanExecute(null));
        }
    }
}