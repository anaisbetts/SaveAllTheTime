using System.Reactive.Linq;
using ReactiveUI;
using SaveAllTheTime.ViewModels;
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
                .BindTo(this, x => x.Open.Visibility);

            this.OneWayBind(ViewModel, x => x.ForegroundBrush, x => x.Open.Foreground);
            this.BindCommand(ViewModel, x => x.Open, x => x.Open);

            this.WhenAnyObservable(x => x.ViewModel.Open)
                .Subscribe(x => Process.Start(ViewModel.ProtocolUrl));
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
