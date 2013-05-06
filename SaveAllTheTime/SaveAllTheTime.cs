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
using Microsoft.VisualStudio.TextManager.Interop;
using SaveAllTheTime.ViewModels;
using SaveAllTheTime.Views;

namespace SaveAllTheTime
{
    /// <summary>
    /// A class detailing the margin's visual definition including both size and content.
    /// </summary>
    sealed class SaveAllTheTime : Border, IWpfTextViewMargin
    {
        public const string MarginName = "SaveAllTheTime";
        readonly IWpfTextView _textView;
        readonly DTE _dte;
        IDisposable _inner;

        public SaveAllTheTime(IWpfTextView textView, ICompletionBroker completionBroker, DTE dte)
        {
            _textView = textView;
            _dte = dte;

            this.Visibility = Visibility.Visible;
            this.ClipToBounds = false;

            this.Child = new CommitHintView() { ViewModel = new CommitHintViewModel(getFilePathFromView(textView)) };
            this.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;

            _inner = Observable.FromEventPattern<TextContentChangedEventArgs>(x => textView.TextBuffer.Changed += x, x => textView.TextBuffer.Changed -= x)
                .Throttle(TimeSpan.FromSeconds(2.0), TaskPoolScheduler.Default)
                .Where(_ => !completionBroker.IsCompletionActive(textView))
                .Subscribe(_ =>
                    Dispatcher.BeginInvoke(new Action(saveAll)));
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

        string getFilePathFromView(IWpfTextView textView)
        {
            var buffer = textView.TextDataModel.DocumentBuffer;
            if (!buffer.Properties.ContainsProperty(typeof(ITextDocument))) return null;

            var doc = buffer.Properties[typeof(ITextDocument)] as ITextDocument;
            if (doc == null) return null;

            return doc.FilePath;
        }

        void saveAll()
        {
            try {
                _dte.ExecuteCommand("File.SaveAll");
            } catch (Exception) {
                // RIP Saving
            }
        }
    }
}
