using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Reactive.Testing;
using ReactiveUI;
using System.Reactive.Linq;
using System.Reactive.Disposables;
using ReactiveUI.Testing;
using SaveAllTheTime.Models;
using Xunit;

namespace SaveAllTheTime.Tests.Models
{
    public class FilesystemWatchCacheTests : IEnableLogger
    {
        [Fact]
        public void FilesystemWatchCacheSmokeTest()
        {
            (new TestScheduler()).With(sched => {
                var targetDir = Path.GetTempPath();
                var targetFile = Path.Combine(targetDir, Guid.NewGuid() + "__" + Guid.NewGuid());
                var fixture = (new FilesystemWatchCache()).Register(targetDir);

                var output = fixture.CreateCollection();
                File.WriteAllText(targetFile, "Foo");
                File.Delete(targetFile);

                // FilesystemWatchCache should debounce FS notifications
                Assert.Equal(0, output.Count);

                sched.AdvanceByMs(100);
                Assert.Equal(0, output.Count);

                // Debounce interval currently at 250ms
                sched.AdvanceByMs(250);
                Assert.NotEqual(0, output.Count);
                Assert.True(output.Contains(targetFile));

                // FilesystemWatchCache shouldn't be watching after we disconnect
                var currentCount = output.Count;
                output.Dispose();

                File.WriteAllText(targetFile, "Foo");
                File.Delete(targetFile);

                sched.AdvanceByMs(10000);
                Assert.Equal(currentCount, output.Count);

                foreach (var v in output) {
                    this.Log().Info(v);
                }
            });
        }
    }
}
