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
            var targetDir = Path.GetTempPath();
            var targetFile = Path.Combine(targetDir, Guid.NewGuid() + "__" + Guid.NewGuid());
            var fixture = (new FilesystemWatchCache()).Register(targetDir);

            var output = fixture.CreateCollection();
            File.WriteAllText(targetFile, "Foo");
            File.Delete(targetFile);

            // FilesystemWatchCache should debounce FS notifications
            Assert.Equal(0, output.Count);

            // I hate this so bad.
            Observable.Timer(TimeSpan.FromMilliseconds(300)).Wait();

            // Debounce interval currently at 250ms
            Assert.Equal(1, output.Count);
            Assert.True(output[0].Contains(targetFile));

            // FilesystemWatchCache shouldn't be watching after we disconnect
            var currentCount = output.Count;
            output.Dispose();

            File.WriteAllText(targetFile, "Foo");
            File.Delete(targetFile);

            // I hate this so bad.
            Observable.Timer(TimeSpan.FromMilliseconds(500)).Wait();
            Assert.Equal(currentCount, output.Count);

            foreach (var v in output) {
                this.Log().Info(v);
            }
        }

        [Fact]
        public void LiveFSWInstancesShouldBeOnePerDirectory()
        {
            var fixture = new FilesystemWatchCache();
            Assert.Equal(0, FilesystemWatchCache.liveFileSystemWatcherCount);

            var dir1 = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var dir2 = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var dir3 = Path.GetTempPath();

            var disp1 = fixture.Register(dir1).Subscribe();
            var disp2 = fixture.Register(dir2).Subscribe();
            var disp3 = fixture.Register(dir3).Subscribe();

            Assert.Equal(3, FilesystemWatchCache.liveFileSystemWatcherCount);

            // dir1 already subscribed == connect to existing fsw
            var disp4 = fixture.Register(dir1).Subscribe();
            Assert.Equal(3, FilesystemWatchCache.liveFileSystemWatcherCount);

            // Refcount on dir1 still 1 because of disp4
            disp1.Dispose();
            disp2.Dispose();
            disp3.Dispose();
            Assert.Equal(1, FilesystemWatchCache.liveFileSystemWatcherCount);

            // Refcount drops to zero on dir1
            disp4.Dispose();
            Assert.Equal(0, FilesystemWatchCache.liveFileSystemWatcherCount);

            // Attempt to resurrect an existing observable
            disp1 = fixture.Register(dir1).Subscribe();
            Assert.Equal(1, FilesystemWatchCache.liveFileSystemWatcherCount);

            disp1.Dispose();
            Assert.Equal(0, FilesystemWatchCache.liveFileSystemWatcherCount);
        }
    }
}
