using ReactiveUI;
using System;

namespace SaveAllTheTime.Tests
{
    public static class ModuleInitializer
    {
        public static void Initialize()
        {
            // NB: This line actually exists to invoke all of RxUI's static
            // constructors that set shit up.
            LogHost.Default.Info("Initializing Test DLL");
        }
    }
}