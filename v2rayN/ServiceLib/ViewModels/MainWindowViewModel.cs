using System.Reactive.Concurrency;
using ReactiveUI;
using ServiceLib.Common;
using ServiceLib.Handler;
using ServiceLib.Manager;
using ServiceLib.Models;
using ServiceLib.Resx;
using ServiceLib.Services;
using System.Reactive;
using System.Windows.Input;

namespace ServiceLib.ViewModels;

public class MainWindowViewModel : MyReactiveObject
{
    #region Menu

    public ReactiveCommand<Unit, Unit> ReloadCmd { get; }
    public ReactiveCommand<Unit, Unit> ManualUpdateSubCmd { get; }

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

        ManualUpdateSubCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await UpdateSubscriptionProcess("", false);
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
        try
        {
            Logging.SaveLog("MainWindowViewModel.Init() 开始执行...");
            
            AppManager.Instance.ShowInTaskbar = true;

            Logging.SaveLog("初始化 DNS...");
            await ConfigHandler.InitBuiltinDNS(_config);
            
            Logging.SaveLog("初始化 FullConfigTemplate...");
            await ConfigHandler.InitBuiltinFullConfigTemplate(_config);
            
            Logging.SaveLog("初始化 Routing...");
            await ConfigHandler.InitBuiltinRouting(_config);
            
            Logging.SaveLog("初始化默认订阅...");
            await InitDefaultSubscription();
            
            Logging.SaveLog("初始化 ProfileExManager...");
            await ProfileExManager.Instance.Init();
            
            Logging.SaveLog("初始化 CoreManager...");
            await CoreManager.Instance.Init(_config, UpdateHandler);
            
            TaskManager.Instance.RegUpdateTask(_config, UpdateTaskHandler);

            if (_config.GuiItem.EnableStatistics || _config.GuiItem.DisplayRealTimeSpeed)
            {
                Logging.SaveLog("初始化 StatisticsManager...");
                await StatisticsManager.Instance.Init(_config, UpdateStatisticsHandler);
            }
            
            Logging.SaveLog("刷新服务器列表...");
            await RefreshServers();

            // 触发订阅和路由菜单刷新
            Logging.SaveLog("触发订阅和路由菜单刷新...");
            AppEvents.SubscriptionsRefreshRequested.Publish();
            AppEvents.RoutingsMenuRefreshRequested.Publish();

            Logging.SaveLog("执行 Reload...");
            await Reload();
            
            Logging.SaveLog("MainWindowViewModel.Init() 执行完成");
        }
        catch (Exception ex)
        {
            Logging.SaveLog("MainWindowViewModel.Init() 异常: " + ex.Message);
            Logging.SaveLog(ex.StackTrace);
        }
    }

    private async Task InitDefaultSubscription()
    {
        try
        {
            Logging.SaveLog("开始初始化默认订阅...");
            
            // 检查是否已存在'用户体验'订阅分组
            var subscriptions = await AppManager.Instance.SubItems();
            Logging.SaveLog($"当前订阅数量: {subscriptions?.Count ?? 0}");
            
            var existingSub = subscriptions?.FirstOrDefault(s => s.Remarks == "用户体验");
            
            if (existingSub == null)
            {
                Logging.SaveLog("未找到'用户体验'订阅分组，准备创建...");
                
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
                
                var result = await ConfigHandler.AddSubItem(_config, subItem);
                Logging.SaveLog($"创建订阅分组结果: {result}, ID: {subItem.Id}");
                
                if (result == 0)
                {
                    Logging.SaveLog("已创建默认订阅分组: 用户体验");
                    existingSub = subItem;
                }
                else
                {
                    Logging.SaveLog("创建订阅分组失败");
                    return;
                }
            }
            else
            {
                Logging.SaveLog($"找到已存在的订阅分组: {existingSub.Id}");
                
                // 如果已存在，确保自动更新设置正确
                if (existingSub.AutoUpdateInterval != 1)
                {
                    existingSub.AutoUpdateInterval = 1;
                    existingSub.Enabled = true;
                    await ConfigHandler.AddSubItem(_config, existingSub);
                    Logging.SaveLog("已更新订阅分组: 用户体验");
                }
            }
            
            // 创建默认的负载均衡策略组
            if (existingSub != null)
            {
                await InitDefaultPolicyGroup(existingSub);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("InitDefaultSubscription异常: " + ex.Message);
            Logging.SaveLog(ex.StackTrace);
        }
    }

    private async Task InitDefaultPolicyGroup(SubItem subItem)
    {
        // 检查是否已存在'负载均衡'策略组
        var allProfiles = new List<ProfileItem>();
        var subscriptions = await AppManager.Instance.SubItems();
        foreach (var sub in subscriptions)
        {
            var profiles = await AppManager.Instance.ProfileItems(sub.Id);
            if (profiles != null)
            {
                allProfiles.AddRange(profiles);
            }
        }
        
        var existingPolicyGroup = allProfiles.FirstOrDefault(p => 
            p.ConfigType == EConfigType.PolicyGroup && 
            p.Remarks == "负载均衡");
        
        if (existingPolicyGroup == null)
        {
            // 创建新的策略组
            var policyGroup = new ProfileItem
            {
                IndexId = Utils.GetGuid(false),
                Remarks = "负载均衡",
                ConfigType = EConfigType.PolicyGroup,
                CoreType = ECoreType.Xray,
                Subid = subItem.Id
            };
            
            // 设置协议额外信息
            var protocolExtra = new ProtocolExtraItem
            {
                MultipleLoad = EMultipleLoad.Fallback, // 故障转移
                SubChildItems = subItem.Id // 关联到用户体验订阅分组
            };
            policyGroup.SetProtocolExtra(protocolExtra);
            
            await SQLiteHelper.Instance.InsertAsync(policyGroup);
            
            // 设置为活动状态
            _config.IndexId = policyGroup.IndexId;
            await ConfigHandler.SaveConfig(_config);
            
            Logging.SaveLog("已创建默认策略组: 负载均衡，并设置为活动");
        }
        else
        {
            // 如果已存在，确保设置正确
            var protocolExtra = existingPolicyGroup.GetProtocolExtra();
            if (protocolExtra.MultipleLoad != EMultipleLoad.Fallback ||
                protocolExtra.SubChildItems != subItem.Id ||
                existingPolicyGroup.CoreType != ECoreType.Xray)
            {
                // 创建新的ProtocolExtraItem对象
                var newProtocolExtra = new ProtocolExtraItem
                {
                    MultipleLoad = EMultipleLoad.Fallback, // 故障转移
                    SubChildItems = subItem.Id // 关联到用户体验订阅分组
                };
                existingPolicyGroup.CoreType = ECoreType.Xray;
                existingPolicyGroup.SetProtocolExtra(newProtocolExtra);
                
                await SQLiteHelper.Instance.UpdateAsync(existingPolicyGroup);
                
                // 设置为活动状态
                _config.IndexId = existingPolicyGroup.IndexId;
                await ConfigHandler.SaveConfig(_config);
                
                Logging.SaveLog("已更新策略组: 负载均衡，并设置为活动");
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

    #region Subscription

    public async Task UpdateSubscriptionProcess(string subId, bool blProxy)
    {
        await Task.Run(async () => await SubscriptionHandler.UpdateProcess(_config, subId, blProxy, UpdateTaskHandler));
        
        // 订阅更新完成后，重新创建策略组
        var subItem = await AppManager.Instance.GetSubItem(subId);
        if (subItem == null)
        {
            // 如果subId为空（更新全部订阅），获取"用户体验"订阅分组
            var subscriptions = await AppManager.Instance.SubItems();
            subItem = subscriptions?.FirstOrDefault(s => s.Remarks == "用户体验");
        }
        
        if (subItem != null)
        {
            await InitDefaultPolicyGroup(subItem);
            // 刷新服务器列表显示
            AppEvents.ProfilesRefreshRequested.Publish();
        }
    }

    #endregion Subscription
}
