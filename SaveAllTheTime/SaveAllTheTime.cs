using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
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
using System.Reactive.Disposables;
using ReactiveUI;
using SaveAllTheTime.Models;
using System.IO;

namespace SaveAllTheTime
{
    public interface IVisualStudioOps
    {
        void SaveAll();
    }

    /// <summary>
    /// Adornment class that draws a square box in the top right hand corner of the viewport
    /// </summary>
    sealed class SaveAllTheTimeAdornment : IDisposable
    {
        readonly IWpfTextView _view;
        readonly IAdornmentLayer _adornmentLayer;

        IDisposable _inner;

        static UserSettings settings;

        static SaveAllTheTimeAdornment()
        {
            settings = UserSettings.Load();
            settings.AutoSave();
        }

        /// <summary>
        /// Creates a square image and attaches an event handler to the layout changed event that
        /// adds the the square in the upper right-hand corner of the TextView via the adornment layer
        /// </summary>
        /// <param name="view">The <see cref="IWpfTextView"/> upon which the adornment will be drawn</param>
        public SaveAllTheTimeAdornment(IWpfTextView view, IVisualStudioOps vsOps)
        {
            _view = view;
            _adornmentLayer = view.GetAdornmentLayer("SaveAllTheTimeAdornment");

            var filePath = getFilePathFromView(_view);
            if (shouldSuppressAdornment(filePath)) {
                _inner = Disposable.Empty;
                return;
            }

            var commitControl = new CommitHintView() { 
                ViewModel = new CommitHintViewModel(filePath, vsOps, settings),
            };

            var disp = new CompositeDisposable();

            var sizeChanged = Observable.Merge(
                Observable.FromEventPattern<EventHandler, EventArgs>(x => _view.ViewportHeightChanged += x, x => _view.ViewportHeightChanged -= x),
                Observable.FromEventPattern<EventHandler, EventArgs>(x => _view.ViewportWidthChanged += x, x => _view.ViewportWidthChanged -= x));

            var hasAdded = false;
            disp.Add(sizeChanged.Subscribe(__ => {
                if (!hasAdded) {
                    _adornmentLayer.AddAdornment(AdornmentPositioningBehavior.ViewportRelative, null, null, commitControl, null);
                    hasAdded = true;
                }

                // NB: The scheduling is to get around initialization where ActualXXX is zero
                var action = new Action<double>(_ => {
                    Canvas.SetLeft(commitControl, _view.ViewportRight - commitControl.ActualWidth);
                    Canvas.SetTop(commitControl, _view.ViewportBottom - commitControl.ActualHeight);
                });

                if (commitControl.ActualWidth > 1) {
                    action(commitControl.ActualWidth);
                } else {
                    commitControl.WhenAny(x => x.ActualWidth, x => x.Value)
                        .Where(x => x > 1)
                        .Take(1)
                        .Subscribe(action);
                }
            }));

            disp.Add(Disposable.Create(() => _adornmentLayer.RemoveAllAdornments()));

            disp.Add(Observable.FromEventPattern<EventHandler, EventArgs>(x => _view.Closed += x, x => _view.Closed -= x)
                .Subscribe(_ => Dispose()));

            _inner = disp;
            disp.Add(commitControl.ViewModel);
        }

        public void Dispose()
        {
            var disp = Interlocked.Exchange(ref _inner, null);
            if (disp != null) {
                disp.Dispose();
            }
        }

        bool shouldSuppressAdornment(string filePath)
        {
            if (String.IsNullOrWhiteSpace(filePath)) return true;
            if (!File.Exists(filePath)) return true;

            var toLower = filePath.ToLowerInvariant();
            if (toLower.Contains("jetbrains") && toLower.Contains("solutioncache")) {
                return true;
            }

            return false;
        }

        string getFilePathFromView(IWpfTextView textView)
        {
            var buffer = textView.TextDataModel.DocumentBuffer;
            if (!buffer.Properties.ContainsProperty(typeof(ITextDocument))) return null;

            var doc = buffer.Properties[typeof(ITextDocument)] as ITextDocument;
            if (doc == null) return null;

            return doc.FilePath;
        }
    }
}
