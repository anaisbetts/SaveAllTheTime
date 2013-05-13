using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.Language.Intellisense;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using ReactiveUI;
using NLog.Layouts;
using NLog.Config;
using NLog.Targets;
using System.Diagnostics;

namespace SaveAllTheTime
{
    /// <summary>
    /// Establishes an <see cref="IAdornmentLayer"/> to place the adornment on and exports the <see cref="IWpfTextViewCreationListener"/>
    /// that instantiates the adornment on the event of a <see cref="IWpfTextView"/>'s creation
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class SaveAllTheTimeFactory : IWpfTextViewCreationListener
    {
        /// <summary>
        /// Defines the adornment layer for the scarlet adornment. This layer is ordered 
        /// after the selection layer in the Z-order
        /// </summary>
        [Export(typeof(AdornmentLayerDefinition))]
        [Name("SaveAllTheTimeAdornment")]
        [Order(After = PredefinedAdornmentLayers.Caret)]
        public AdornmentLayerDefinition editorAdornmentLayer = null;

        readonly IVisualStudioOps _vsOps;

        [ImportingConstructor]
        public SaveAllTheTimeFactory(IVisualStudioOps vsOps)
        {
            _vsOps = vsOps;
        }

        /// <summary>
        /// Instantiates a ViewportAdornment1 manager when a textView is created.
        /// </summary>
        /// <param name="textView">The <see cref="IWpfTextView"/> upon which the adornment should be placed</param>
        public void TextViewCreated(IWpfTextView textView)
        {
            new SaveAllTheTimeAdornment(textView, _vsOps);
        }
    }

    // NB: This class name is Magical, don't change it. Fody looks for it and
    // writes in a .NET Module Initializer
    public static class ModuleInitializer
    {
        public static void Initialize()
        {
            var reg = new ReactiveUI.NLog.Registrations();
            reg.Register((f, t) => ((ModernDependencyResolver)RxApp.DependencyResolver).Register(f, t));

#if DEBUG
            var debugTarget = new DebuggerTarget() {
                Name = "debug",
                Layout = new SimpleLayout(@"${longdate} - ${level:uppercase=true}: ${message}${onexception:${newline}EXCEPTION\: ${exception:format=ToString}}"),
            };

            var debugRule = new LoggingRule("*", NLog.LogLevel.Info, debugTarget);
            NLog.LogManager.Configuration.AddTarget("debug", debugTarget);
            NLog.LogManager.Configuration.LoggingRules.Add(debugRule);

            NLog.LogManager.ReconfigExistingLoggers();
#endif
        }
    }
}
