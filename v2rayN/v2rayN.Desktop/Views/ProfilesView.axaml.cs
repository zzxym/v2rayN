using DialogHostAvalonia;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

public partial class ProfilesView : ReactiveUserControl<ProfilesViewModel>
{
    private static Config _config;
    private Window? _window;

    public ProfilesView()
    {
        InitializeComponent();
    }

    public ProfilesView(Window window)
    {
        InitializeComponent();

        _config = AppManager.Instance.Config;
        _window = window;


        txtServerFilter.KeyDown += TxtServerFilter_KeyDown;
        lstProfiles.KeyDown += LstProfiles_KeyDown;
        lstProfiles.SelectionChanged += lstProfiles_SelectionChanged;
        lstProfiles.DoubleTapped += LstProfiles_DoubleTapped;
        lstProfiles.LoadingRow += LstProfiles_LoadingRow;
        lstProfiles.Sorting += LstProfiles_Sorting;
        //if (_config.uiItem.enableDragDropSort)
        //{
        //    lstProfiles.AllowDrop = true;
        //    lstProfiles.PreviewMouseLeftButtonDown += LstProfiles_PreviewMouseLeftButtonDown;
        //    lstProfiles.MouseMove += LstProfiles_MouseMove;
        //    lstProfiles.DragEnter += LstProfiles_DragEnter;
        //    lstProfiles.Drop += LstProfiles_Drop;
        //}

        ViewModel = new ProfilesViewModel(null);

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.ProfileItems, v => v.lstProfiles.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedProfile, v => v.lstProfiles.SelectedItem).DisposeWith(disposables);

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


        });

        RestoreUI();
    }

    private async void LstProfiles_Sorting(object? sender, DataGridColumnEventArgs e)
    {
        e.Handled = true;

        if (ViewModel != null && e.Column?.Tag?.ToString() != null)
        {
            await ViewModel.SortServer(e.Column.Tag.ToString());
        }

        e.Handled = false;
    }

    #region Event







    private void lstProfiles_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.SelectedProfiles = lstProfiles.SelectedItems.Cast<ProfileItemModel>().ToList();
        }
    }

    private void LstProfiles_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        var source = e.Source as Border;
        if (source?.Name == "HeaderBackground")
        {
            return;
        }

        ViewModel?.SetDefaultServer();
    }

    private void LstProfiles_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        e.Row.Header = $" {e.Row.Index + 1}";
    }

    //private void LstProfiles_ColumnHeader_Click(object? sender, RoutedEventArgs e)
    //{
    //    var colHeader = sender as DataGridColumnHeader;
    //    if (colHeader == null || colHeader.TabIndex < 0 || colHeader.Column == null)
    //    {
    //        return;
    //    }

    //    var colName = ((MyDGTextColumn)colHeader.Column).ExName;
    //    ViewModel?.SortServer(colName);
    //}



    private void LstProfiles_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta)
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



    private void TxtServerFilter_KeyDown(object? sender, KeyEventArgs e)
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
            foreach (var item2 in lstProfiles.Columns)
            {
                if (item2.Tag == null)
                {
                    continue;
                }
                if (item2.Tag.Equals(item.Name))
                {
                    if (item.Width < 0)
                    {
                        item2.IsVisible = false;
                    }
                    else
                    {
                        item2.Width = new DataGridLength(item.Width, DataGridLengthUnitType.Pixel);
                        item2.DisplayIndex = displayIndex++;
                    }
                    if (item.Name.ToLower().StartsWith("to"))
                    {
                        item2.IsVisible = _config.GuiItem.EnableStatistics;
                    }
                }
            }
        }
    }

    private void StorageUI()
    {
        List<ColumnItem> lvColumnItem = new();
        foreach (var item2 in lstProfiles.Columns)
        {
            if (item2.Tag == null)
            {
                continue;
            }
            lvColumnItem.Add(new()
            {
                Name = (string)item2.Tag,
                Width = (int)(item2.IsVisible == true ? item2.ActualWidth : -1),
                Index = item2.DisplayIndex
            });
        }
        _config.UiItem.MainColumnItem = lvColumnItem;
    }

    #endregion UI

    #region Drag and Drop

    //private Point startPoint = new();
    //private int startIndex = -1;
    //private string formatData = "ProfileItemModel";

    ///// <summary>
    ///// Helper to search up the VisualTree
    ///// </summary>
    ///// <typeparam name="T"></typeparam>
    ///// <param name="current"></param>
    ///// <returns></returns>
    //private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    //{
    //    do
    //    {
    //        if (current is T)
    //        {
    //            return (T)current;
    //        }
    //        current = VisualTreeHelper.GetParent(current);
    //    }
    //    while (current != null);
    //    return null;
    //}

    //private void LstProfiles_PreviewMouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
    //{
    //    // Get current mouse position
    //    startPoint = e.GetPosition(null);
    //}

    //private void LstProfiles_MouseMove(object? sender, MouseEventArgs e)
    //{
    //    // Get the current mouse position
    //    Point mousePos = e.GetPosition(null);
    //    Vector diff = startPoint - mousePos;

    //    if (e.LeftButton == MouseButtonState.Pressed &&
    //        (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
    //               Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance))
    //    {
    //        // Get the dragged Item
    //        if (sender is not DataGrid listView) return;
    //        var listViewItem = FindAncestor<DataGridRow>((DependencyObject)e.OriginalSource);
    //        if (listViewItem == null) return;           // Abort
    //                                                    // Find the data behind the ListViewItem
    //        ProfileItemModel item = (ProfileItemModel)listView.ItemContainerGenerator.ItemFromContainer(listViewItem);
    //        if (item == null) return;                   // Abort
    //                                                    // Initialize the drag & drop operation
    //        startIndex = lstProfiles.SelectedIndex;
    //        DataObject dragData = new(formatData, item);
    //        DragDrop.DoDragDrop(listViewItem, dragData, DragDropEffects.Copy | DragDropEffects.Move);
    //    }
    //}

    //private void LstProfiles_DragEnter(object? sender, DragEventArgs e)
    //{
    //    if (!e.Data.GetDataPresent(formatData) || sender != e.Source)
    //    {
    //        e.Effects = DragDropEffects.None;
    //    }
    //}

    //private void LstProfiles_Drop(object? sender, DragEventArgs e)
    //{
    //    if (e.Data.GetDataPresent(formatData) && sender == e.Source)
    //    {
    //        // Get the drop Item destination
    //        if (sender is not DataGrid listView) return;
    //        var listViewItem = FindAncestor<DataGridRow>((DependencyObject)e.OriginalSource);
    //        if (listViewItem == null)
    //        {
    //            // Abort
    //            e.Effects = DragDropEffects.None;
    //            return;
    //        }
    //        // Find the data behind the Item
    //        ProfileItemModel item = (ProfileItemModel)listView.ItemContainerGenerator.ItemFromContainer(listViewItem);
    //        if (item == null) return;
    //        // Move item into observable collection
    //        // (this will be automatically reflected to lstView.ItemsSource)
    //        e.Effects = DragDropEffects.Move;

    //        ViewModel?.MoveServerTo(startIndex, item);

    //        startIndex = -1;
    //    }
    //}

    #endregion Drag and Drop
}
