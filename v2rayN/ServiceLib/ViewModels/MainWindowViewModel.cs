using System.Reactive.Concurrency;

namespace ServiceLib.ViewModels;

public class MainWindowViewModel : MyReactiveObject
{
    #region Menu

    public ReactiveCommand<Unit, Unit> ReloadCmd { get; }

    [Reactive]
    public bool BlReloadEnabled { get; set; }

    [Reactive]
    public bool ShowClashUI { get; set; }

    [Reactive]
    public int TabMainSelectedIndex { get; set; }

    [Reactive] public bool BlIsWindows { get; set; }

    #endregion Menu

    #region Init

    public MainWindowViewModel(Func<EViewAction, object?, Task<bool>>? updateView)
    {
        _config = AppManager.Instance.Config;
        _updateView = updateView;
        BlIsWindows = Utils.IsWindows();

        #region WhenAnyValue && ReactiveCommand

        ReloadCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await Reload();
        });

        #endregion WhenAnyValue && ReactiveCommand

        #region AppEvents

        AppEvents.ReloadRequested
            .AsObservable()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(async _ => await Reload());

        #endregion AppEvents

        _ = Init();
    }

    private async Task Init()
    {
        AppManager.Instance.ShowInTaskbar = true;

        await ConfigHandler.InitBuiltinDNS(_config);
        await ConfigHandler.InitBuiltinFullConfigTemplate(_config);
        await InitDefaultSubscription();
        await ProfileExManager.Instance.Init();
        await CoreManager.Instance.Init(_config, UpdateHandler);
        TaskManager.Instance.RegUpdateTask(_config, UpdateTaskHandler);

        if (_config.GuiItem.EnableStatistics || _config.GuiItem.DisplayRealTimeSpeed)
        {
            await StatisticsManager.Instance.Init(_config, UpdateStatisticsHandler);
        }
        await RefreshServers();

        await Reload();
    }

    private async Task InitDefaultSubscription()
    {
        // 检查是否已存在'用户体验'订阅分组
        var subscriptions = await AppManager.Instance.SubItems();
        var existingSub = subscriptions.FirstOrDefault(s => s.Remarks == "用户体验");
        
        if (existingSub == null)
        {
            // 创建新的订阅分组
            var subItem = new SubItem
            {
                Id = Utils.GetGuid(false),
                Remarks = "用户体验",
                Url = "https://rss.xiaolin.cc/test",
                Enabled = true,
                AutoUpdateInterval = 1, // 1分钟自动更新
                UpdateTime = 0
            };
            
            await ConfigHandler.AddSubItem(_config, subItem);
            Logging.SaveLog("已创建默认订阅分组: 用户体验");
        }
        else
        {
            // 如果已存在，确保自动更新设置正确
            if (existingSub.AutoUpdateInterval != 1)
            {
                existingSub.AutoUpdateInterval = 1;
                existingSub.Enabled = true;
                await ConfigHandler.AddSubItem(_config, existingSub);
                Logging.SaveLog("已更新订阅分组: 用户体验");
            }
        }
    }

    #endregion Init

    #region Actions

    private async Task UpdateHandler(bool notify, string msg)
    {
        NoticeManager.Instance.SendMessage(msg);
        if (notify)
        {
            NoticeManager.Instance.Enqueue(msg);
        }
        await Task.CompletedTask;
    }

    private async Task UpdateTaskHandler(bool success, string msg)
    {
        NoticeManager.Instance.SendMessageEx(msg);
        if (success)
        {
            var indexIdOld = _config.IndexId;
            await RefreshServers();
            if (indexIdOld != _config.IndexId)
            {
                await Reload();
            }
            if (_config.UiItem.EnableAutoAdjustMainLvColWidth)
            {
                AppEvents.AdjustMainLvColWidthRequested.Publish();
            }
        }
    }

    private async Task UpdateStatisticsHandler(ServerSpeedItem update)
    {
        if (!AppManager.Instance.ShowInTaskbar)
        {
            return;
        }
        AppEvents.DispatcherStatisticsRequested.Publish(update);
        await Task.CompletedTask;
    }

    #endregion Actions

    #region Servers && Groups

    private async Task RefreshServers()
    {
        AppEvents.ProfilesRefreshRequested.Publish();

        await Task.Delay(200);
    }

    #endregion Servers && Groups

    #region core job

    private bool _hasNextReloadJob = false;
    private readonly SemaphoreSlim _reloadSemaphore = new(1, 1);

    public async Task Reload()
    {
        //If there are unfinished reload job, marked with next job.
        if (!await _reloadSemaphore.WaitAsync(0))
        {
            _hasNextReloadJob = true;
            return;
        }

        try
        {
            SetReloadEnabled(false);

            var msgs = await ActionPrecheckManager.Instance.Check(_config.IndexId);
            if (msgs.Count > 0)
            {
                foreach (var msg in msgs)
                {
                    NoticeManager.Instance.SendMessage(msg);
                }
                NoticeManager.Instance.Enqueue(Utils.List2String(msgs.Take(10).ToList(), true));
                return;
            }

            await Task.Run(async () =>
            {
                await LoadCore();
                await SysProxyHandler.UpdateSysProxy(_config, false);
                await Task.Delay(1000);
            });
            AppEvents.TestServerRequested.Publish();

            var showClashUI = AppManager.Instance.IsRunningCore(ECoreType.sing_box);
            if (showClashUI)
            {
                AppEvents.ProxiesReloadRequested.Publish();
            }

            ReloadResult(showClashUI);
        }
        finally
        {
            SetReloadEnabled(true);
            _reloadSemaphore.Release();
            //If there is a next reload job, execute it.
            if (_hasNextReloadJob)
            {
                _hasNextReloadJob = false;
                await Reload();
            }
        }
    }

    private void ReloadResult(bool showClashUI)
    {
        RxApp.MainThreadScheduler.Schedule(() =>
        {
            ShowClashUI = showClashUI;
            TabMainSelectedIndex = showClashUI ? TabMainSelectedIndex : 0;
        });
    }

    private void SetReloadEnabled(bool enabled)
    {
        RxApp.MainThreadScheduler.Schedule(() => BlReloadEnabled = enabled);
    }

    private async Task LoadCore()
    {
        var node = await ConfigHandler.GetDefaultServer(_config);
        await CoreManager.Instance.LoadCore(node);
    }

    #endregion core job
}
