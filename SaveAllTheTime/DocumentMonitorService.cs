using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Threading;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using ReactiveUI;

namespace SaveAllTheTime
{
    [Export(typeof(IWpfTextViewCreationListener))]
    [Export(typeof(IVisualStudioOps))]
    [ContentType("any")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    sealed class DocumentMonitorService : IWpfTextViewCreationListener, IVsRunningDocTableEvents, IVsRunningDocTableEvents2, IVisualStudioOps
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

        readonly SVsServiceProvider _vsServiceProvider;
        readonly ICompletionBroker _completionBroker;
        readonly RunningDocumentTable _runningDocumentTable;
        readonly List<ITextView> _openTextViewList = new List<ITextView>();
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
        internal DocumentMonitorService(SVsServiceProvider vsServiceProvider, ICompletionBroker completionBroker)
        {
            _vsServiceProvider = vsServiceProvider;
            _runningDocumentTable = new RunningDocumentTable(vsServiceProvider);
            _runningDocumentTable.Advise(this);
            _completionBroker = completionBroker;
            _dte = (DTE)vsServiceProvider.GetService(typeof(_DTE));

            // NB: Resharper somehow fucks with this event, we need to do as 
            // little as possible in the event handler itself
            var documentChanged = Observable.FromEventPattern(x => _changed += x, x => _changed -= x)
                .ObserveOn(RxApp.TaskpoolScheduler)
                .Throttle(TimeSpan.FromSeconds(2.0), RxApp.TaskpoolScheduler)
                .Where(_ => !IsCompletionActive())
                .Select(_ => Unit.Default)
                .ObserveOn(RxApp.MainThreadScheduler);

            var dispatcher = Dispatcher.CurrentDispatcher;
            documentChanged.Subscribe(_ => dispatcher.BeginInvoke(new Action(() => SaveAll())));

            // NB: We use the message bus here, because we want to effectively
            // merge all of the text change notifications from any document
            MessageBus.Current.RegisterMessageSource(documentChanged, "AnyDocumentChanged");

            CheckAlreadyOpenDocuments(vsServiceProvider);
        }

        /// <summary>
        /// It is possible that this class is created after documents are already open in the solution.  This 
        /// means we won't get the show / opened events until the user once again brings them back into 
        /// focus.  Hence do a quick search of the open IVsWindowFrame instances and setup the event listening
        /// on them.
        /// 
        /// This problem does not exist for IWpfTextView instances.  This type implements IWpfTextViewCreationListener
        /// and hence will be around for every single IWpfTextView that is created. 
        /// </summary>
        void CheckAlreadyOpenDocuments(SVsServiceProvider vsServiceProvider)
        {
            var vsShell = (IVsUIShell)vsServiceProvider.GetService(typeof(SVsUIShell));
            var vsWindowFrames = vsShell.GetDocumentWindowFrames();
            foreach (var vsWindowFrame in vsWindowFrames) {
                CheckSubscribe(vsWindowFrame);
            }

            if (vsWindowFrames.Count > 0) {
                RaiseChanged();
            }
        }

        void CheckSubscribe(IVsWindowFrame vsWindowFrame)
        {
            if (_vsWindowFrameSet.Contains(vsWindowFrame)) {
                return;
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
                return;
            }

            var vsWindowFrameMonitor = new VsWindowFrameMonitor(this, vsWindowFrame);
            if (!ErrorHandler.Succeeded(vsWindowFrame2.Advise(vsWindowFrameMonitor, out vsWindowFrameMonitor.Cookie))) {
                return;
            }

            notifyPropertyChanged.PropertyChanged += OnNotifyPropertyChanged;
            _vsWindowFrameSet.Add(vsWindowFrame);
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

        bool IsCompletionActive()
        {
            return _openTextViewList.Any(x => _completionBroker.IsCompletionActive(x));
        }

        #region IVisualStudioOps

        public void SaveAll()
        {
            try {
                _dte.ExecuteCommand("File.SaveAll");
            }
            catch (Exception) {

            }
        }

        static readonly Guid TFSGitProviderPackageGuid = new Guid("7FE30A77-37F9-4CF2-83DD-96B207028E1B");
        static bool? isTFSGitPluginInstalled;

        public bool IsTFSGitPluginInstalled()
        {
            if (!isTFSGitPluginInstalled.HasValue) {

                // The intent of this method is to determine if TFS Git is in a mode which will interfere with
                // our extension.  As an implementation detail we check for the loaded state vs installed.  In
                // user terms loaded == enabled and only enabled extensions contribute DLLs to the instance
                // of Visual Studio.  An extension which is installed but disabled contributes nothing and 
                // won't interfere with our extension.  
                //
                // This is a handy distinction for users which occasionally need to work with TFS Git on their
                // box.  They can enable / disable which is much faster than install / uninstall 

                var guid = TFSGitProviderPackageGuid;
                var vsShell = (IVsShell)_vsServiceProvider.GetService(typeof(SVsShell));
                IVsPackage vsPackage;
                isTFSGitPluginInstalled = ErrorHandler.Succeeded(vsShell.IsPackageLoaded(ref guid, out vsPackage)) && vsPackage != null;
            }

            return isTFSGitPluginInstalled.Value;
        }

        #endregion

        #region IWpfTextViewCreationListener

        public void TextViewCreated(IWpfTextView textView)
        {
            _openTextViewList.Add(textView);
            var textBuffer = textView.TextBuffer;
            textBuffer.Changed += OnTextBufferChanged;
            textView.Closed += (sender, e) => {
                textBuffer.Changed -= OnTextBufferChanged;
                _openTextViewList.Remove(textView);
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
            CheckSubscribe(vsWindowFrame);
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
