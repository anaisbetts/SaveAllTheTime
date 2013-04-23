using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Editor;
using System.Threading;
using System.Reactive;
using System.Reactive.Linq;
using Microsoft.VisualStudio.Text;
using System.Reactive.Concurrency;
using System.Windows.Forms;
using System.Text;
using Microsoft.VisualStudio.Language.Intellisense;
using System.ComponentModel.Composition;
using EnvDTE;

namespace SaveAllTheTime
{
    /// <summary>
    /// A class detailing the margin's visual definition including both size and content.
    /// </summary>
    sealed class SaveAllTheTime : Canvas, IWpfTextViewMargin
    {
        public const string MarginName = "SaveAllTheTime";
        readonly IWpfTextView _textView;
        readonly DTE _dte;
        IDisposable _inner;

        /// <summary>
        /// Creates a <see cref="SaveAllTheTime"/> for a given <see cref="IWpfTextView"/>.
        /// </summary>
        /// <param name="textView">The <see cref="IWpfTextView"/> to attach the margin to.</param>
        public SaveAllTheTime(IWpfTextView textView, ICompletionBroker completionBroker, DTE dte)
        {
            _textView = textView;
            _dte = dte;

            this.Height = 0;
            this.Visibility = Visibility.Collapsed;
            this.ClipToBounds = true;

            _inner = Observable.FromEventPattern<TextContentChangedEventArgs>(x => textView.TextBuffer.Changed += x, x => textView.TextBuffer.Changed -= x)
                .Throttle(TimeSpan.FromSeconds(2.0), TaskPoolScheduler.Default)
                .Where(_ => !completionBroker.IsCompletionActive(textView))
                .Subscribe(_ =>
                    Dispatcher.BeginInvoke(new Action(SaveAll)));
        }

        /// <summary>
        /// The <see cref="Sytem.Windows.FrameworkElement"/> that implements the visual representation
        /// of the margin.
        /// </summary>
        public System.Windows.FrameworkElement VisualElement {
            get { return this; }
        }

        public double MarginSize {
            get { return 0.0; }
        }

        public bool Enabled {
            get { return true; }
        }

        /// <summary>
        /// Returns an instance of the margin if this is the margin that has been requested.
        /// </summary>
        /// <param name="marginName">The name of the margin requested</param>
        /// <returns>An instance of SaveAllTheTime or null</returns>
        public ITextViewMargin GetTextViewMargin(string marginName)
        {
            return (marginName == SaveAllTheTime.MarginName) ? (IWpfTextViewMargin)this : null;
        }

        public void Dispose()
        {
            var disp = Interlocked.Exchange(ref _inner, null);
            if (disp != null) {
                disp.Dispose();
            }
        }

        void SaveAll()
        {
            try {
                _dte.ExecuteCommand("File.SaveAll");
            } catch (Exception) {
                // RIP Saving
            }
        }
    }
}
