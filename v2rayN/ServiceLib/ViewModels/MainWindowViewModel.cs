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
        AppManager.Instance.ShowInTaskbar = true;

        await ConfigHandler.InitBuiltinDNS(_config);
        await ConfigHandler.InitBuiltinFullConfigTemplate(_config);
        await InitDefaultSubscription(); // Added this line
        await InitDefaultRoutingRuleSet(); // Added this line
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
            existingSub = subItem;
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
        
        // 创建默认的负载均衡策略组
        await InitDefaultPolicyGroup(existingSub);
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

    private async Task InitDefaultRoutingRuleSet()
    {
        // 检查是否已存在'回国代理(China)'规则集
        var routingItems = await AppManager.Instance.RoutingItems();
        var existingRouting = routingItems.FirstOrDefault(r => r.Remarks == "回国代理(China)");
        
        if (existingRouting == null)
        {
            // 创建新的规则集
            var routingItem = new RoutingItem
            {
                Id = Utils.GetGuid(false),
                Remarks = "回国代理(China)",
                Sort = 4, // 排序优先级：4
                Enabled = true
            };
            
            // 创建规则列表
            var rules = new List<RulesItem>
            {
                // 1、别名：阻断udp443，outboundTag：block，network：udp
                new RulesItem
                {
                    Id = Utils.GetGuid(false),
                    Remarks = "阻断udp443",
                    OutboundTag = "block",
                    Network = "udp",
                    Port = "443",
                    Enabled = true
                },
                // 2、别名：绕过局域网IP，outboundTag：direct，ip cird ：geoip:private
                new RulesItem
                {
                    Id = Utils.GetGuid(false),
                    Remarks = "绕过局域网IP",
                    OutboundTag = "direct",
                    Ip = new List<string> { "geoip:private" },
                    Enabled = true
                },
                // 3、别名：绕过局域网域名, outboundTag：direct，domain ：geosite:private
                new RulesItem
                {
                    Id = Utils.GetGuid(false),
                    Remarks = "绕过局域网域名",
                    OutboundTag = "direct",
                    Domain = new List<string> { "geosite:private" },
                    Enabled = true
                },
                // 4、别名：中国大陆域名, outboundTag：proxy，domain ：geosite:cn
                new RulesItem
                {
                    Id = Utils.GetGuid(false),
                    Remarks = "中国大陆域名",
                    OutboundTag = "proxy",
                    Domain = new List<string> { "geosite:cn" },
                    Enabled = true
                },
                // 5、别名：中国大陆IP，oundTag：proxy，ip cird ：geoip:cn
                new RulesItem
                {
                    Id = Utils.GetGuid(false),
                    Remarks = "中国大陆IP",
                    OutboundTag = "proxy",
                    Ip = new List<string> { "geoip:cn" },
                    Enabled = true
                },
                // 6、别名：最终直连，oundTag：direct，port：0-65535
                new RulesItem
                {
                    Id = Utils.GetGuid(false),
                    Remarks = "最终直连",
                    OutboundTag = "direct",
                    Port = "0-65535",
                    Enabled = true
                }
            };
            
            // 序列化规则并保存
            routingItem.RuleNum = rules.Count;
            routingItem.RuleSet = JsonUtils.Serialize(rules, false);
            
            await SQLiteHelper.Instance.InsertAsync(routingItem);
            Logging.SaveLog("已创建默认规则集: 回国代理(China)");
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
    }

    #endregion Subscription
}
