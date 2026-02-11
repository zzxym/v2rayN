using v2rayN.Desktop.Base;

namespace v2rayN.Desktop.Views;

public partial class SubscriptionMaintenanceView : ReactiveUserControl<SubscriptionMaintenanceViewModel>
{
    public SubscriptionMaintenanceView()
    {
        InitializeComponent();

        ViewModel = new SubscriptionMaintenanceViewModel(UpdateViewHandler);

        this.WhenActivated(disposables =>
        {
            this.Bind(ViewModel, vm => vm.SelectedSubscription, v => v.cmbSubscriptions.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedServer, v => v.cmbServers.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedProtocol, v => v.cmbProtocol.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Username, v => v.txtUsername.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Password, v => v.txtPassword.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.TargetPath, v => v.txtTargetPath.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Status, v => v.txtStatus.Text).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.UploadCmd, v => v.btnUpload).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SaveServerCmd, v => v.btnSaveServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RefreshCmd, v => v.btnRefresh).DisposeWith(disposables);
        });
    }

    private async Task<bool> UpdateViewHandler(EViewAction action, object? obj)
    {
        return await Task.FromResult(true);
    }
}