using Newtonsoft.Json;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public static UserSettings Load()
        {
            return JsonConvert.DeserializeObject<UserSettings>(File.ReadAllText(userSettingsPath, Encoding.UTF8));
        }

        public IDisposable AutoSave()
        {
            return this.AutoPersist(x => 
                File.WriteAllText(userSettingsPath, JsonConvert.SerializeObject(this), Encoding.UTF8));
        }

        public UserSettings()
        {
        }

        static string userSettingsPath {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SaveAllTheTime", "settings.json"); }
        }
    }
}
