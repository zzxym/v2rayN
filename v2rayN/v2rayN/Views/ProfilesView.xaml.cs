using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using v2rayN.Base;

namespace v2rayN.Views;

public partial class ProfilesView
{
    private static Config _config;

    public ProfilesView()
    {
        InitializeComponent();
        lstGroup.MaxHeight = Math.Floor(SystemParameters.WorkArea.Height * 0.20 / 40) * 40;

        _config = AppManager.Instance.Config;

        txtServerFilter.PreviewKeyDown += TxtServerFilter_PreviewKeyDown;
        lstProfiles.PreviewKeyDown += LstProfiles_PreviewKeyDown;
        lstProfiles.SelectionChanged += LstProfiles_SelectionChanged;
        lstProfiles.LoadingRow += LstProfiles_LoadingRow;

        ViewModel = new ProfilesViewModel(UpdateViewHandler);

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.ProfileItems, v => v.lstProfiles.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedProfile, v => v.lstProfiles.SelectedItem).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.SubItems, v => v.lstGroup.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedSub, v => v.lstGroup.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.ServerFilter, v => v.txtServerFilter.Text).DisposeWith(disposables);

            //servers
            this.BindCommand(ViewModel, vm => vm.SetDefaultServerCmd, v => v.menuSetDefaultServer).DisposeWith(disposables);

            //servers ping
            this.BindCommand(ViewModel, vm => vm.MixedTestServerCmd, v => v.menuMixedTestServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.TcpingServerCmd, v => v.menuTcpingServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SpeedServerCmd, v => v.menuSpeedServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.GooglePingServerCmd, v => v.menuGooglePingServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.HuaweiPingServerCmd, v => v.menuHuaweiPingServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SortServerResultCmd, v => v.menuSortServerResult).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.FastRealPingCmd, v => v.btnFastRealPing).DisposeWith(disposables);

            AppEvents.AppExitRequested
              .AsObservable()
              .ObserveOn(RxApp.MainThreadScheduler)
              .Subscribe(_ => StorageUI())
              .DisposeWith(disposables);

            AppEvents.AdjustMainLvColWidthRequested
                .AsObservable()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => AutofitColumnWidth())
                .DisposeWith(disposables);
        });

        RestoreUI();
    }

    #region Event

    private async Task<bool> UpdateViewHandler(EViewAction action, object? obj)
    {
        switch (action)
        {
            case EViewAction.ProfilesFocus:
                lstProfiles.Focus();
                break;

            case EViewAction.DispatcherRefreshServersBiz:
                Application.Current?.Dispatcher.Invoke(RefreshServersBiz, DispatcherPriority.Normal);
                break;
        }

        return await Task.FromResult(true);
    }

    public void RefreshServersBiz()
    {
        if (lstProfiles.SelectedIndex > 0)
        {
            lstProfiles.ScrollIntoView(lstProfiles.SelectedItem, null);
        }
    }

    private void LstProfiles_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.SelectedProfiles = lstProfiles.SelectedItems.Cast<ProfileItemModel>().ToList();
        }
    }

    private void LstProfiles_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        e.Row.Header = $" {e.Row.GetIndex() + 1}";
    }

    private void LstProfiles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_config.UiItem.DoubleClick2Activate)
        {
            ViewModel?.SetDefaultServer();
        }
    }



    private void LstProfiles_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
        {
            switch (e.Key)
            {
                case Key.E:
                    ViewModel?.ServerSpeedtest(ESpeedActionType.Mixedtest);
                    break;
            }
        }
        else
        {
            switch (e.Key)
            {
                case Key.Enter:
                    ViewModel?.SetDefaultServer();
                    break;

                case Key.Escape:
                    ViewModel?.ServerSpeedtestStop();
                    break;
            }
        }
    }

    private void AutofitColumnWidth()
    {
        try
        {
            foreach (var it in lstProfiles.Columns)
            {
                it.Width = new DataGridLength(1, DataGridLengthUnitType.Auto);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ProfilesView", ex);
        }
    }

    private void TxtServerFilter_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            ViewModel?.RefreshServers();
        }
    }

    #endregion Event

    #region UI

    private void RestoreUI()
    {
        var lvColumnItem = _config.UiItem.MainColumnItem.OrderBy(t => t.Index).ToList();
        var displayIndex = 0;
        foreach (var item in lvColumnItem)
        {
            foreach (MyDGTextColumn item2 in lstProfiles.Columns)
            {
                if (item2.ExName == item.Name)
                {
                    if (item.Width < 0)
                    {
                        item2.Visibility = Visibility.Hidden;
                    }
                    else
                    {
                        item2.Width = item.Width;
                        item2.DisplayIndex = displayIndex++;
                    }
                    if (item.Name.ToLower().StartsWith("to"))
                    {
                        item2.Visibility = _config.GuiItem.EnableStatistics ? Visibility.Visible : Visibility.Hidden;
                    }
                }
            }
        }
    }

    private void StorageUI()
    {
        List<ColumnItem> lvColumnItem = new();
        foreach (var t in lstProfiles.Columns)
        {
            var item2 = (MyDGTextColumn)t;
            lvColumnItem.Add(new()
            {
                Name = item2.ExName,
                Width = (int)(item2.Visibility == Visibility.Visible ? item2.ActualWidth : -1),
                Index = item2.DisplayIndex
            });
        }
        _config.UiItem.MainColumnItem = lvColumnItem;
    }

    #endregion UI
}
