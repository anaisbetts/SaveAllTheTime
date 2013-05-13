using Newtonsoft.Json;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace SaveAllTheTime.Models
{
    [DataContract]
    public class UserSettings : ReactiveObject
    {
        bool _ShouldHideCommitWidget;
        [DataMember]
        public bool ShouldHideCommitWidget {
            get { return _ShouldHideCommitWidget; }
            set { this.RaiseAndSetIfChanged(ref _ShouldHideCommitWidget, value); }
        }

        bool _HasShownTFSGitWarning;
        [DataMember]
        public bool HasShownTFSGitWarning {
            get { return _HasShownTFSGitWarning; }
            set { this.RaiseAndSetIfChanged(ref _HasShownTFSGitWarning, value); }
        }

        public static UserSettings Load()
        {
            try {
                return JsonConvert.DeserializeObject<UserSettings>(File.ReadAllText(userSettingsPath, Encoding.UTF8));
            } catch (Exception ex) {
                LogHost.Default.ErrorException("Couldn't load settings, creating them from scratch", ex);
                return new UserSettings();
            }
        }

        public IDisposable AutoSave()
        {
            return this.AutoPersist(x => Observable.Start(() =>
                File.WriteAllText(userSettingsPath, JsonConvert.SerializeObject(x), Encoding.UTF8)));
        }

        public UserSettings()
        {
        }

        static string userSettingsPath {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SaveAllTheTime", "settings.json"); }
        }
    }
}
