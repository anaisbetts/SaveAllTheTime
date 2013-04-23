using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.Language.Intellisense;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace SaveAllTheTime
{
    /// <summary>
    /// Export a <see cref="IWpfTextViewMarginProvider"/>, which returns an instance of the margin for the editor
    /// to use.
    /// </summary>
    [Export(typeof(IWpfTextViewMarginProvider))]
    [Name(SaveAllTheTime.MarginName)]
    [Order(After = PredefinedMarginNames.HorizontalScrollBar)] //Ensure that the margin occurs below the horizontal scrollbar
    [MarginContainer(PredefinedMarginNames.Bottom)] //Set the container to the bottom of the editor window
    [ContentType("text")] //Show this margin for all text-based types
    [TextViewRole(PredefinedTextViewRoles.Interactive)]
    internal sealed class MarginFactory : IWpfTextViewMarginProvider
    {
        readonly ICompletionBroker _completionBroker;
        readonly DTE _dte;

        [ImportingConstructor]
        public MarginFactory(ICompletionBroker completionBroker, SVsServiceProvider vsServiceProvider)
        {
            _completionBroker = completionBroker;
            _dte = (DTE)vsServiceProvider.GetService(typeof(_DTE));
        }

        public IWpfTextViewMargin CreateMargin(IWpfTextViewHost textViewHost, IWpfTextViewMargin containerMargin)
        {
            return new SaveAllTheTime(textViewHost.TextView, _completionBroker, _dte);
        }
    }
}
