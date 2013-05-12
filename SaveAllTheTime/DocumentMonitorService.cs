using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Threading;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace SaveAllTheTime
{
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("any")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    sealed class DocumentMonitorService : IWpfTextViewCreationListener, IVsRunningDocTableEvents, IVsRunningDocTableEvents2
    {
        #region VsWindowFrameMonitor 

        /// <summary>
        /// The IVsWindowFrameNotify interfaces don't provide the IVsWindowFrame instance on which the events
        /// are being raised.  This type allows us to pair the events with the instance in question 
        /// </summary>
        sealed class VsWindowFrameMonitor : IVsWindowFrameNotify, IVsWindowFrameNotify2
        {
            readonly DocumentMonitorService _documentMonitorService;
            readonly IVsWindowFrame _vsWindowFrame;

            internal uint Cookie;

            internal VsWindowFrameMonitor(DocumentMonitorService documentMonitorService, IVsWindowFrame vsWindowFrame)
            {
                _documentMonitorService = documentMonitorService;
                _vsWindowFrame = vsWindowFrame;
            }

            public int OnClose(ref uint pgrfSaveOptions)
            {
                _documentMonitorService.OnVsWindowFrameClosed(_vsWindowFrame, Cookie);
                return VSConstants.S_OK;
            }

            public int OnDockableChange(int fDockable)
            {
                return VSConstants.S_OK;
            }

            public int OnMove()
            {
                return VSConstants.S_OK;
            }

            public int OnShow(int fShow)
            {
                return VSConstants.S_OK;
            }

            public int OnSize()
            {
                return VSConstants.S_OK;
            }
        }

        #endregion

        readonly RunningDocumentTable _runningDocumentTable;
        readonly DTE _dte;

        /// <summary>
        /// This event is raised whenever there is a change in the dirty state of any open document
        /// in the solution.  
        /// </summary>
        event EventHandler _changed;

        /// <summary>
        /// This is the set of IVsWindowFrame instances for which we are currently monitoring 
        /// events on.  
        /// </summary>
        HashSet<IVsWindowFrame> _vsWindowFrameSet = new HashSet<IVsWindowFrame>();

        [ImportingConstructor]
        internal DocumentMonitorService(SVsServiceProvider vsServiceProvider)
        {
            _runningDocumentTable = new RunningDocumentTable(vsServiceProvider);
            _runningDocumentTable.Advise(this);
            _dte = (DTE)vsServiceProvider.GetService(typeof(_DTE));

            var dispatcher = Dispatcher.CurrentDispatcher;
            Observable.FromEventPattern(x => _changed += x, x => _changed -= x)
                .Throttle(TimeSpan.FromSeconds(2.0), TaskPoolScheduler.Default)
                .Subscribe(_ => dispatcher.BeginInvoke(new Action(() => SaveAll())));
        }

        void RaiseChanged()
        {
            var changed = _changed;
            if (changed != null) {
                changed(this, EventArgs.Empty);
            }
        }

        void OnTextBufferChanged(object sender, EventArgs e)
        {
            RaiseChanged();
        }

        void OnNotifyPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "DocumentIsDirty") {
                RaiseChanged();
            }
        }

        void OnVsWindowFrameClosed(IVsWindowFrame vsWindowFrame, uint cookie)
        {
            var notifyPropertyChanged = vsWindowFrame as INotifyPropertyChanged;
            if (notifyPropertyChanged != null) {
                notifyPropertyChanged.PropertyChanged -= OnNotifyPropertyChanged;
            }

            var vsWindowFrame2 = vsWindowFrame as IVsWindowFrame2;
            if (vsWindowFrame2 != null) {
                vsWindowFrame2.Unadvise(cookie);
            }

            _vsWindowFrameSet.Remove(vsWindowFrame);
        }

        void SaveAll()
        {
            try {
                _dte.ExecuteCommand("File.SaveAll");
            }
            catch (Exception) {

            }
        }

        #region IWpfTextViewCreationListener

        public void TextViewCreated(IWpfTextView textView)
        {
            var textBuffer = textView.TextBuffer;
            textBuffer.Changed += OnTextBufferChanged;
            textView.Closed += (sender, e) => {
                textBuffer.Changed -= OnTextBufferChanged;
            };
        }

        #endregion

        #region IVsRunningDocTableEvents

        public int OnAfterAttributeChange(uint docCookie, uint grfAttribs)
        {
            uint target = (uint)(__VSRDTATTRIB.RDTA_DocDataIsDirty);
            if (0 != (target & grfAttribs)) {
                RaiseChanged();
            }

            return VSConstants.S_OK;
        }

        public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame vsWindowFrame)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterFirstDocumentLock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterSave(uint docCookie)
        {
            return VSConstants.S_OK;
        }

        public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame vsWindowFrame)
        {
            if (_vsWindowFrameSet.Contains(vsWindowFrame)) {
                return VSConstants.S_OK;
            }

            // Even though project files are in the running document table events about their dirty state are not always 
            // properly raised by Visual Studio.  In particular when they are modified via the project property 
            // designer (aka application designer).  However these IVsWindowFrame implementations do implement the 
            // INotifyPropertyChanged interface and we can hook into the IsDocumentDirty property instead
            //
            // This is an implementation detail of IVsWindowFrame (specifically WindowFrame inside the DLL 
            // Microsoft.VisualStudio.Platform.WindowManagement).  Hence it can change from version to version of 
            // Visual Studio.  But this is the behavior in 2010+ and unlikely to change.  Need to be aware of these
            // potential break though going forward 
            var notifyPropertyChanged = vsWindowFrame as INotifyPropertyChanged;
            var vsWindowFrame2 = vsWindowFrame as IVsWindowFrame2;
            if (notifyPropertyChanged == null || vsWindowFrame2 == null) {
                return VSConstants.S_OK;
            }

            var vsWindowFrameMonitor = new VsWindowFrameMonitor(this, vsWindowFrame);
            if (!ErrorHandler.Succeeded(vsWindowFrame2.Advise(vsWindowFrameMonitor, out vsWindowFrameMonitor.Cookie))) {
                return VSConstants.S_OK;
            }

            notifyPropertyChanged.PropertyChanged += OnNotifyPropertyChanged;
            _vsWindowFrameSet.Add(vsWindowFrame);
            return VSConstants.S_OK;
        }

        public int OnBeforeLastDocumentUnlock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
        {
            return VSConstants.S_OK;
        }

        #endregion

        #region IVsRunningDocTableEvents

        public int OnAfterAttributeChangeEx(uint docCookie, uint grfAttribs, IVsHierarchy pHierOld, uint itemidOld, string pszMkDocumentOld, IVsHierarchy pHierNew, uint itemidNew, string pszMkDocumentNew)
        {
            uint target = (uint)(__VSRDTATTRIB.RDTA_DocDataIsDirty);
            if (0 != (target & grfAttribs)) {
                RaiseChanged();
            }

            return VSConstants.S_OK;
        }

        #endregion
    }
}
