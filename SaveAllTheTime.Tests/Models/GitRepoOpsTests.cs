using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NSubstitute;
using ReactiveUI;
using Xunit;
using Xunit.Extensions;
using SaveAllTheTime.Models;
using System.Reactive.Linq;
using System.Reactive.Disposables;

namespace SaveAllTheTime.Tests.Models
{
    public class GitRepoOpsTests : IEnableLogger
    {
        [Theory]
        [InlineData("https://notagithuburl.com", false)]
        [InlineData("https://notagithuburl.com", false)]
        [InlineData("git@bitbucket.org:birkenfeld/pygments-main", false)]
        [InlineData("git://github.com/github/Akavache.git", false)]
        [InlineData("git@github.com:reactiveui/ReactiveUI.git", true)]
        [InlineData("https://github.com/github/Akavache.git", true)]
        public void ProtocolUrlCheck(string remoteUrl, bool shouldNotBeNull)
        {
            var result = GitRepoOps.protocolUrlForRemoteUrl(remoteUrl);
            this.Log().Info("Protocol URL: {0}", result);

            Assert.Equal(shouldNotBeNull, !String.IsNullOrEmpty(result));
        }

        [Fact]
        public void RefCountWillResubscribe()
        {
            var subscribeCount = 0;

            var fixture = Observable.Create<int>(subj => {
                subscribeCount++;
                subj.OnNext(10);

                return Disposable.Empty;
            }).Publish().RefCount();

            Assert.Equal(0, subscribeCount);

            var disp1 = fixture.Subscribe();
            var disp2 = fixture.Subscribe();

            Assert.Equal(1, subscribeCount);

            disp1.Dispose();
            disp2.Dispose();

            Assert.Equal(1, subscribeCount);

            var disp3 = fixture.Subscribe();

            Assert.Equal(2, subscribeCount);
        }
    }
}
