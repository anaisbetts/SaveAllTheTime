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

namespace SaveAllTheTime.Tests.Models
{
    public class GitRepoOpsTests : IEnableLogger
    {
        [Theory]
        [InlineData("https://notagithuburl.com", false)]
        [InlineData("https://notagithuburl.com", false)]
        [InlineData("git@bitbucket.org:birkenfeld/pygments-main", false)]
        [InlineData("git@github.com:reactiveui/ReactiveUI.git", true)]
        [InlineData("https://github.com/github/Akavache.git", true)]
        [InlineData("git://github.com/github/Akavache.git", false)]
        public void ProtocolUrlCheck(string remoteUrl, bool shouldNotBeNull)
        {
            var result = GitRepoOps.ProtocolUrlForRemoteUrl(remoteUrl);
            this.Log().Info("Protocol URL: {0}", result);

            Assert.Equal(shouldNotBeNull, !String.IsNullOrEmpty(result));
        }
    }
}
