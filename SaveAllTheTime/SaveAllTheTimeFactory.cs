﻿using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.Language.Intellisense;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using ReactiveUI;

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

        readonly ICompletionBroker _completionBroker;
        readonly DTE _dte;

        [ImportingConstructor]
        public SaveAllTheTimeFactory(ICompletionBroker completionBroker, SVsServiceProvider vsServiceProvider)
        {
            _completionBroker = completionBroker;
            _dte = (DTE)vsServiceProvider.GetService(typeof(_DTE));
        }

        /// <summary>
        /// Instantiates a ViewportAdornment1 manager when a textView is created.
        /// </summary>
        /// <param name="textView">The <see cref="IWpfTextView"/> upon which the adornment should be placed</param>
        public void TextViewCreated(IWpfTextView textView)
        {
            new SaveAllTheTimeAdornment(textView, _completionBroker, _dte);
        }
    }
}
