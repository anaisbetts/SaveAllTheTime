using System.Reactive.Linq;
using ReactiveUI;
using SaveAllTheTime.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Diagnostics;
using SaveAllTheTime.ViewModels;

namespace SaveAllTheTime.Views
{
    /// <summary>
    /// Interaction logic for CommitHintView.xaml
    /// </summary>
    public partial class CommitHintView : UserControl, IViewFor<CommitHintViewModel>
    {
        public CommitHintView()
        {
            InitializeComponent();

            this.WhenAnyObservable(x => x.ViewModel.Open.CanExecuteObservable)
                .BindTo(this, x => x.visualRoot.Visibility);

            this.WhenAny(x => x.ViewModel.HintState, x => x.Value.ToString())
                .Subscribe(x => VisualStateManager.GoToElementState(visualRoot, x, true));

            this.WhenAnyObservable(x => x.ViewModel.RefreshStatus.ItemsInflight)
                .Select(x => x != 0 ? "Loading" : "NotLoading")
                .Subscribe(x => VisualStateManager.GoToElementState(visualRoot, x, true));

            this.BindCommand(ViewModel, x => x.Open, x => x.Open);

            this.WhenAnyObservable(x => x.ViewModel.Open)
                .Subscribe(x => Process.Start(ViewModel.ProtocolUrl));

            this.WhenAny(x => x.ViewModel.SuggestedOpacity, x => x.Value)
                .Select(x => x + 0.25)
                .BindTo(this, x => x.visualRoot.Opacity);

            this.WhenAny(x => x.IsMouseOver, x => x.Value ? "Hover" : "NoHover")
                .Subscribe(x => VisualStateManager.GoToElementState(visualRoot, x, true));

            this.WhenAnyObservable(
                    x => x.ViewModel.RefreshLastCommitTime.ThrownExceptions,
                    x => x.ViewModel.RefreshStatus.ThrownExceptions)
                .Subscribe(_ => VisualStateManager.GoToElementState(visualRoot, "Error", true));

            Observable.FromEventPattern<MouseButtonEventHandler, MouseButtonEventArgs>(x => visualRoot.PreviewMouseUp += x, x => visualRoot.PreviewMouseUp += x)
                .Where(x => x.EventArgs.ChangedButton == MouseButton.Right)
                .Subscribe(x => {
                    Open.ContextMenu.IsOpen = true;
                    x.EventArgs.Handled = true;
                });

            this.BindCommand(ViewModel, x => x.GoAway, x => x.GoAway);

            this.WhenAnyObservable(x => x.ViewModel.GoAway)
                .Subscribe(_ => {
                    var result = MessageBox.Show(
                        "This will hide the commit widget for good. Are you sure?", 
                        "Death to Widgets", MessageBoxButton.YesNo);

                    if (result == MessageBoxResult.No) return;
                    visualRoot.Visibility = Visibility.Collapsed;
                    ViewModel.UserSettings.ShouldHideCommitWidget = true;
                });

            /* Uncomment this and the XAML section if you want to test the
             * transitions over time
            this.Bind(ViewModel, x => x.MinutesTimeOverride, x => x.MinutesTimeOverride.Value);

            this.WhenAny(x => x.ViewModel.MinutesTimeOverride, x => x.Value)
                .Where(x => x.HasValue)
                .Select(x => x.Value.ToString("###.#"))
                .BindTo(this, x => x.MinutesTimeOverrideDisplay.Text);
            */
        }

        public CommitHintViewModel ViewModel {
            get { return (CommitHintViewModel)GetValue(ViewModelProperty); }
            set { SetValue(ViewModelProperty, value); }
        }
        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register("ViewModel", typeof(CommitHintViewModel), typeof(CommitHintView), new PropertyMetadata(null));

        object IViewFor.ViewModel {
            get { return ViewModel; }
            set { ViewModel = (CommitHintViewModel)value; }
        }
    }
}