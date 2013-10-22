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
using System.Reactive.Subjects;
using Microsoft.VisualStudio.Text;

namespace SaveAllTheTime
{
    [Export(typeof(IWpfTextViewCreationListener))]
    [Export(typeof(IVisualStudioOps))]
    [ContentType("any")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    sealed class DocumentMonitorService : IWpfTextViewCreationListener, IVsRunningDocTableEvents, IVsRunningDocTableEvents2, IVisualStudioOps, IEnableLogger
    {
        readonly SVsServiceProvider _vsServiceProvider;
        readonly ICompletionBroker _completionBroker;
        readonly RunningDocumentTable _runningDocumentTable;
        readonly List<ITextView> _openTextViewList = new List<ITextView>();
        readonly DTE _dte;

        /// <summary>
        /// This event is raised whenever there is a change in the dirty state of any open document
        /// in the solution.  
        /// </summary>
        readonly Subject<Unit> _changed = new Subject<Unit>();

        /// <summary>
        /// This is the set of IVsWindowFrame instances for which we are currently monitoring 
        /// events on.  
        /// </summary>
        HashSet<IVsWindowFrame> _vsWindowFrameSet = new HashSet<IVsWindowFrame>();

        readonly HashSet<string> _sessionDocumentsLookup = new HashSet<string>();

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
            var documentChanged = _changed
                .ObserveOn(RxApp.TaskpoolScheduler)
                .Throttle(TimeSpan.FromSeconds(2.0), RxApp.TaskpoolScheduler)
                .Where(_ => !isCompletionActive())
                .Select(_ => Unit.Default)
                .ObserveOn(RxApp.MainThreadScheduler);

            documentChanged.Subscribe(_ => SaveAll());

            // NB: We use the message bus here, because we want to effectively
            // merge all of the text change notifications from any document
            MessageBus.Current.RegisterMessageSource(documentChanged, "AnyDocumentChanged");


            checkAlreadyOpenDocuments(vsServiceProvider);

            _dte.Events.WindowEvents.WindowActivated += (o,e) => _changed.OnNext(Unit.Default);
        }

        public void SaveAll()
        {
            try {
                if (!shouldSaveActiveDocument()) {
                    return;
                }

                foreach (Document item in _dte.Documents.Cast<Document>().Where(item => !item.Saved)) {
                    item.Save();
                }
            } catch (Exception ex) {
                this.Log().WarnException("Failed to save all documents", ex);
            }
        }

        public void TextViewCreated(IWpfTextView textView)
        {
            var textBuffer = textView.TextBuffer;

            _openTextViewList.Add(textView);

            var disp = Observable.FromEventPattern<TextContentChangedEventArgs>(x => textBuffer.Changed += x, x => textBuffer.Changed -= x)
                .Select(_ => Unit.Default)
                .Multicast(_changed)
                .Connect();

            textView.Closed += (sender, e) => {
                disp.Dispose();
                _openTextViewList.Remove(textView);
            };
        }

        public int OnAfterAttributeChange(uint docCookie, uint grfAttribs)
        {
            uint target = (uint)(__VSRDTATTRIB.RDTA_DocDataIsDirty);

            if (0 != (target & grfAttribs)) {
                _changed.OnNext(Unit.Default);
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
            checkSubscribe(vsWindowFrame);
            return VSConstants.S_OK;
        }

        public int OnBeforeLastDocumentUnlock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterAttributeChangeEx(uint docCookie, uint grfAttribs, IVsHierarchy pHierOld, uint itemidOld, string pszMkDocumentOld, IVsHierarchy pHierNew, uint itemidNew, string pszMkDocumentNew)
        {
            uint target = (uint)(__VSRDTATTRIB.RDTA_DocDataIsDirty);

            if (0 != (target & grfAttribs)) {
                _changed.OnNext(Unit.Default);
            }

            return VSConstants.S_OK;
        }

        bool shouldSaveActiveDocument()
        {
            string name = _dte.ActiveDocument.FullName;

            if (name.EndsWith("resx", StringComparison.InvariantCulture)) {
                return false;
            }

            if (_sessionDocumentsLookup.Contains(name)) {
                return true;
            }

            if (_dte.Solution.GetProjectItemPaths().Contains(name)) {
                _sessionDocumentsLookup.Add(name);
                return true;
            }

            return false;
        }

        bool isCompletionActive()
        {
            return _openTextViewList.Any(x => _completionBroker.IsCompletionActive(x));
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
        void checkAlreadyOpenDocuments(SVsServiceProvider vsServiceProvider)
        {
            var vsShell = (IVsUIShell)vsServiceProvider.GetService(typeof(SVsUIShell));
            var vsWindowFrames = vsShell.GetDocumentWindowFrames();

            foreach (var vsWindowFrame in vsWindowFrames) {
                checkSubscribe(vsWindowFrame);
            }

            if (vsWindowFrames.Count > 0) {
                _changed.OnNext(Unit.Default);
            }
        }

        void checkSubscribe(IVsWindowFrame vsWindowFrame)
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

            var disp = Observable.FromEventPattern<PropertyChangedEventHandler, PropertyChangedEventArgs>(x => notifyPropertyChanged.PropertyChanged += x, x => notifyPropertyChanged.PropertyChanged -= x)
                .Where(x => x.EventArgs.PropertyName == "DocumentIsDirty")
                .Select(_ => Unit.Default)
                .Multicast(_changed)
                .Connect();

            var vsWindowFrameMonitor = new VsWindowFrameMonitor(this, vsWindowFrame, disp);
            if (!ErrorHandler.Succeeded(vsWindowFrame2.Advise(vsWindowFrameMonitor, out vsWindowFrameMonitor.Cookie))) {
                return;
            }

            _vsWindowFrameSet.Add(vsWindowFrame);
        }

        void onVsWindowFrameClosed(IVsWindowFrame vsWindowFrame, uint cookie)
        {
            var vsWindowFrame2 = vsWindowFrame as IVsWindowFrame2;
            if (vsWindowFrame2 != null) {
                vsWindowFrame2.Unadvise(cookie);
            }

            _vsWindowFrameSet.Remove(vsWindowFrame);
        }

        /// <summary>
        /// The IVsWindowFrameNotify interfaces don't provide the IVsWindowFrame instance on which the events
        /// are being raised.  This type allows us to pair the events with the instance in question 
        /// </summary>
        sealed class VsWindowFrameMonitor : IVsWindowFrameNotify, IVsWindowFrameNotify2
        {
            readonly DocumentMonitorService _documentMonitorService;
            readonly IVsWindowFrame _vsWindowFrame;
            readonly IDisposable _innerDisp;

            internal uint Cookie;

            internal VsWindowFrameMonitor(DocumentMonitorService documentMonitorService, IVsWindowFrame vsWindowFrame, IDisposable innerDisp)
            {
                _documentMonitorService = documentMonitorService;
                _vsWindowFrame = vsWindowFrame;
                _innerDisp = innerDisp;
            }

            public int OnClose(ref uint pgrfSaveOptions)
            {
                _documentMonitorService.onVsWindowFrameClosed(_vsWindowFrame, Cookie);
                _innerDisp.Dispose();
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
    }
}
