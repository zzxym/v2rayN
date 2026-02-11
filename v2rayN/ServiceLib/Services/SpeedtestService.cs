namespace ServiceLib.Services;

public class SpeedtestService(Config config, Func<SpeedTestResult, Task> updateFunc)
{
    private static readonly string _tag = "SpeedtestService";
    private readonly Config? _config = config;
    private readonly Func<SpeedTestResult, Task>? _updateFunc = updateFunc;
    private static readonly ConcurrentBag<string> _lstExitLoop = new();

    public void RunLoop(ESpeedActionType actionType, List<ProfileItem> selecteds)
    {
        Task.Run(async () =>
        {
            await RunAsync(actionType, selecteds);
            await ProfileExManager.Instance.SaveTo();
            await UpdateFunc("", ResUI.SpeedtestingCompleted);
        });
    }

    public void ExitLoop()
    {
        if (!_lstExitLoop.IsEmpty)
        {
            _ = UpdateFunc("", ResUI.SpeedtestingStop);

            _lstExitLoop.Clear();
        }
    }

    private static bool ShouldStopTest(string exitLoopKey)
    {
        return !_lstExitLoop.Any(p => p == exitLoopKey);
    }

    private async Task RunAsync(ESpeedActionType actionType, List<ProfileItem> selecteds)
    {
        var exitLoopKey = Utils.GetGuid(false);
        _lstExitLoop.Add(exitLoopKey);

        var lstSelected = await GetClearItem(actionType, selecteds);

        switch (actionType)
        {
            case ESpeedActionType.Tcping:
                await RunTcpingAsync(lstSelected);
                break;

            case ESpeedActionType.Realping:
                await RunRealPingBatchAsync(lstSelected, exitLoopKey);
                break;

            case ESpeedActionType.Speedtest:
                await RunMixedTestAsync(lstSelected, 1, true, exitLoopKey);
                break;

            case ESpeedActionType.Mixedtest:
                await RunMixedTestAsync(lstSelected, _config.SpeedTestItem.MixedConcurrencyCount, true, exitLoopKey);
                break;

            case ESpeedActionType.Googleping:
                await RunGooglePingBatchAsync(lstSelected, exitLoopKey);
                break;

            case ESpeedActionType.Huaweiping:
                await RunHuaweiPingBatchAsync(lstSelected, exitLoopKey);
                break;
        }
    }

    private async Task<List<ServerTestItem>> GetClearItem(ESpeedActionType actionType, List<ProfileItem> selecteds)
    {
        var lstSelected = new List<ServerTestItem>();
        foreach (var it in selecteds)
        {
            if (it.ConfigType.IsComplexType())
            {
                continue;
            }

            if (it.Port <= 0)
            {
                continue;
            }

            lstSelected.Add(new ServerTestItem()
            {
                IndexId = it.IndexId,
                Address = it.Address,
                Port = it.Port,
                ConfigType = it.ConfigType,
                QueueNum = selecteds.IndexOf(it)
            });
        }

        //clear test result
        foreach (var it in lstSelected)
        {
            switch (actionType)
            {
                case ESpeedActionType.Tcping:
                case ESpeedActionType.Realping:
                case ESpeedActionType.Googleping:
                case ESpeedActionType.Huaweiping:
                    await UpdateFunc(it.IndexId, ResUI.Speedtesting, "");
                    ProfileExManager.Instance.SetTestDelay(it.IndexId, 0);
                    break;

                case ESpeedActionType.Speedtest:
                    await UpdateFunc(it.IndexId, "", ResUI.SpeedtestingWait);
                    ProfileExManager.Instance.SetTestSpeed(it.IndexId, 0);
                    break;

                case ESpeedActionType.Mixedtest:
                    await UpdateFunc(it.IndexId, ResUI.Speedtesting, ResUI.SpeedtestingWait);
                    ProfileExManager.Instance.SetTestDelay(it.IndexId, 0);
                    ProfileExManager.Instance.SetTestSpeed(it.IndexId, 0);
                    break;
            }
        }

        if (lstSelected.Count > 1 && (actionType == ESpeedActionType.Speedtest || actionType == ESpeedActionType.Mixedtest))
        {
            NoticeManager.Instance.Enqueue(ResUI.SpeedtestingPressEscToExit);
        }

        return lstSelected;
    }

    private async Task RunTcpingAsync(List<ServerTestItem> selecteds)
    {
        foreach (var it in selecteds)
        {
            try
            {
                var responseTime = await GetTcpingTime(it.Address, it.Port);

                ProfileExManager.Instance.SetTestDelay(it.IndexId, responseTime);
                await UpdateFunc(it.IndexId, responseTime.ToString());
            }
            catch (Exception ex)
            {
                Logging.SaveLog(_tag, ex);
            }
        }
    }

    private async Task RunRealPingBatchAsync(List<ServerTestItem> lstSelected, string exitLoopKey, int pageSize = 0)
    {
        if (pageSize <= 0)
        {
            pageSize = lstSelected.Count < Global.SpeedTestPageSize ? lstSelected.Count : Global.SpeedTestPageSize;
        }
        var lstTest = GetTestBatchItem(lstSelected, pageSize);

        List<ServerTestItem> lstFailed = new();
        foreach (var lst in lstTest)
        {
            var ret = await RunRealPingAsync(lst, exitLoopKey);
            if (ret == false)
            {
                lstFailed.AddRange(lst);
            }
            await Task.Delay(100);
        }

        //Retest the failed part
        var pageSizeNext = pageSize / 2;
        if (lstFailed.Count > 0 && pageSizeNext > 0)
        {
            if (ShouldStopTest(exitLoopKey))
            {
                await UpdateFunc("", ResUI.SpeedtestingSkip);
                return;
            }

            await UpdateFunc("", string.Format(ResUI.SpeedtestingTestFailedPart, lstFailed.Count));

            if (pageSizeNext > _config.SpeedTestItem.MixedConcurrencyCount)
            {
                await RunRealPingBatchAsync(lstFailed, exitLoopKey, pageSizeNext);
            }
            else
            {
                await RunMixedTestAsync(lstSelected, _config.SpeedTestItem.MixedConcurrencyCount, false, exitLoopKey);
            }
        }
    }

    private async Task RunGooglePingBatchAsync(List<ServerTestItem> lstSelected, string exitLoopKey, int pageSize = 0)
    {
        if (pageSize <= 0)
        {
            pageSize = lstSelected.Count < Global.SpeedTestPageSize ? lstSelected.Count : Global.SpeedTestPageSize;
        }
        var lstTest = GetTestBatchItem(lstSelected, pageSize);

        foreach (var lst in lstTest)
        {
            await RunGooglePingAsync(lst, exitLoopKey);
            await Task.Delay(100);
        }
    }

    private async Task RunHuaweiPingBatchAsync(List<ServerTestItem> lstSelected, string exitLoopKey, int pageSize = 0)
    {
        if (pageSize <= 0)
        {
            pageSize = lstSelected.Count < Global.SpeedTestPageSize ? lstSelected.Count : Global.SpeedTestPageSize;
        }
        var lstTest = GetTestBatchItem(lstSelected, pageSize);

        foreach (var lst in lstTest)
        {
            await RunHuaweiPingAsync(lst, exitLoopKey);
            await Task.Delay(100);
        }
    }

    private async Task<bool> RunGooglePingAsync(List<ServerTestItem> lst, string exitLoopKey)
    {
        foreach (var it in lst)
        {
            if (ShouldStopTest(exitLoopKey))
            {
                await UpdateFunc(it.IndexId, "", ResUI.SpeedtestingSkip);
                continue;
            }

            ProcessService processService = null;
            try
            {
                processService = await CoreManager.Instance.LoadCoreConfigSpeedtest(it);
                if (processService is null)
                {
                    await UpdateFunc(it.IndexId, "", ResUI.FailedToRunCore);
                    continue;
                }

                await Task.Delay(1000);

                var delay = await DoGooglePing(it);
                await UpdateFunc(it.IndexId, delay > 0 ? delay.ToString() : "Failed", "");
                ProfileExManager.Instance.SetTestDelay(it.IndexId, delay > 0 ? (int)delay : 0);
            }
            catch (Exception ex)
            {
                await UpdateFunc(it.IndexId, "", "Failed");
                Logging.SaveLog(ex.Message, ex);
            }
            finally
            {
                processService?.Dispose();
            }
        }

        return true;
    }

    private async Task<bool> RunHuaweiPingAsync(List<ServerTestItem> lst, string exitLoopKey)
    {
        foreach (var it in lst)
        {
            if (ShouldStopTest(exitLoopKey))
            {
                await UpdateFunc(it.IndexId, "", ResUI.SpeedtestingSkip);
                continue;
            }

            ProcessService processService = null;
            try
            {
                processService = await CoreManager.Instance.LoadCoreConfigSpeedtest(it);
                if (processService is null)
                {
                    await UpdateFunc(it.IndexId, "", ResUI.FailedToRunCore);
                    continue;
                }

                await Task.Delay(1000);

                var delay = await DoHuaweiPing(it);
                await UpdateFunc(it.IndexId, delay > 0 ? delay.ToString() : "Failed", "");
                ProfileExManager.Instance.SetTestDelay(it.IndexId, delay > 0 ? (int)delay : 0);
            }
            catch (Exception ex)
            {
                await UpdateFunc(it.IndexId, "", "Failed");
                Logging.SaveLog(ex.Message, ex);
            }
            finally
            {
                processService?.Dispose();
            }
        }

        return true;
    }

    private async Task<long> DoGooglePing(ServerTestItem it)
    {
        try
        {
            var httpPort = _config.Inbound.FirstOrDefault()?.LocalPort ?? 10808;
            var httpClientHandler = new HttpClientHandler
            {
                Proxy = new System.Net.WebProxy("http://127.0.0.1:" + httpPort),
                UseProxy = true
            };

            using var client = new HttpClient(httpClientHandler) { Timeout = TimeSpan.FromSeconds(_config.SpeedTestItem.SpeedTestTimeout) };
            var startTime = DateTime.Now;
            var response = await client.GetAsync("http://google.com/generate_204");
            var endTime = DateTime.Now;

            if (response.IsSuccessStatusCode)
            {
                return (long)(endTime - startTime).TotalMilliseconds;
            }
        }
        catch
        {
        }
        return 0;
    }

    private async Task<long> DoHuaweiPing(ServerTestItem it)
    {
        try
        {
            var httpPort = _config.Inbound.FirstOrDefault()?.LocalPort ?? 10808;
            var httpClientHandler = new HttpClientHandler
            {
                Proxy = new System.Net.WebProxy("http://127.0.0.1:" + httpPort),
                UseProxy = true
            };

            using var client = new HttpClient(httpClientHandler) { Timeout = TimeSpan.FromSeconds(_config.SpeedTestItem.SpeedTestTimeout) };
            var startTime = DateTime.Now;
            var response = await client.GetAsync("http://connectivitycheck.platform.hicloud.com/generate_204");
            var endTime = DateTime.Now;

            if (response.IsSuccessStatusCode)
            {
                return (long)(endTime - startTime).TotalMilliseconds;
            }
        }
        catch
        {
        }
        return 0;
    }

    private async Task<bool> RunRealPingAsync(List<ServerTestItem> selecteds, string exitLoopKey)
    {
        ProcessService processService = null;
        try
        {
            processService = await CoreManager.Instance.LoadCoreConfigSpeedtest(selecteds);
            if (processService is null)
            {
                return false;
            }
            await Task.Delay(1000);

            foreach (var it in selecteds)
            {
                if (!it.AllowTest)
                {
                    await UpdateFunc(it.IndexId, ResUI.SpeedtestingSkip);
                    continue;
                }

                if (ShouldStopTest(exitLoopKey))
                {
                    return false;
                }

                await DoRealPing(it);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
        finally
        {
            if (processService != null)
            {
                await processService?.StopAsync();
            }
        }
        return true;
    }

    private async Task RunMixedTestAsync(List<ServerTestItem> selecteds, int concurrencyCount, bool blSpeedTest, string exitLoopKey)
    {
        var downloadHandle = new DownloadService();
        foreach (var it in selecteds)
        {
            if (ShouldStopTest(exitLoopKey))
            {
                await UpdateFunc(it.IndexId, "", ResUI.SpeedtestingSkip);
                continue;
            }

            ProcessService processService = null;
            try
            {
                processService = await CoreManager.Instance.LoadCoreConfigSpeedtest(it);
                if (processService is null)
                {
                    await UpdateFunc(it.IndexId, "", ResUI.FailedToRunCore);
                    continue;
                }

                await Task.Delay(1000);

                var delay = await DoRealPing(it);
                if (blSpeedTest)
                {
                    if (ShouldStopTest(exitLoopKey))
                    {
                        await UpdateFunc(it.IndexId, "", ResUI.SpeedtestingSkip);
                        continue;
                    }

                    if (delay > 0)
                    {
                        await DoSpeedTest(downloadHandle, it);
                    }
                    else
                    {
                        await UpdateFunc(it.IndexId, "", ResUI.SpeedtestingSkip);
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.SaveLog(_tag, ex);
            }
            finally
            {
                if (processService != null)
                {
                    await processService?.StopAsync();
                }
            }
        }
    }

    private async Task<int> DoRealPing(ServerTestItem it)
    {
        var webProxy = new WebProxy($"socks5://{Global.Loopback}:{it.Port}");
        var responseTime = await ConnectionHandler.GetRealPingTime(_config.SpeedTestItem.SpeedPingTestUrl, webProxy, 10);

        ProfileExManager.Instance.SetTestDelay(it.IndexId, responseTime);
        await UpdateFunc(it.IndexId, responseTime.ToString());
        return responseTime;
    }

    private async Task DoSpeedTest(DownloadService downloadHandle, ServerTestItem it)
    {
        await UpdateFunc(it.IndexId, "", ResUI.Speedtesting);

        var webProxy = new WebProxy($"socks5://{Global.Loopback}:{it.Port}");
        var url = _config.SpeedTestItem.SpeedTestUrl;
        var timeout = _config.SpeedTestItem.SpeedTestTimeout;
        await downloadHandle.DownloadDataAsync(url, webProxy, timeout, async (success, msg) =>
        {
            decimal.TryParse(msg, out var dec);
            if (dec > 0)
            {
                ProfileExManager.Instance.SetTestSpeed(it.IndexId, dec);
            }
            await UpdateFunc(it.IndexId, "", msg);
        });
    }

    private async Task<int> GetTcpingTime(string url, int port)
    {
        var responseTime = -1;

        if (!IPAddress.TryParse(url, out var ipAddress))
        {
            var ipHostInfo = await Dns.GetHostEntryAsync(url);
            ipAddress = ipHostInfo.AddressList.First();
        }

        IPEndPoint endPoint = new(ipAddress, port);
        using Socket clientSocket = new(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        var timer = Stopwatch.StartNew();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await clientSocket.ConnectAsync(endPoint, cts.Token).ConfigureAwait(false);
            responseTime = (int)timer.ElapsedMilliseconds;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            timer.Stop();
        }
        return responseTime;
    }

    private List<List<ServerTestItem>> GetTestBatchItem(List<ServerTestItem> lstSelected, int pageSize)
    {
        List<List<ServerTestItem>> lstTest = new();
        var lst1 = lstSelected.Where(t => Global.XraySupportConfigType.Contains(t.ConfigType)).ToList();
        var lst2 = lstSelected.Where(t => Global.SingboxOnlyConfigType.Contains(t.ConfigType)).ToList();

        for (var num = 0; num < (int)Math.Ceiling(lst1.Count * 1.0 / pageSize); num++)
        {
            lstTest.Add(lst1.Skip(num * pageSize).Take(pageSize).ToList());
        }
        for (var num = 0; num < (int)Math.Ceiling(lst2.Count * 1.0 / pageSize); num++)
        {
            lstTest.Add(lst2.Skip(num * pageSize).Take(pageSize).ToList());
        }

        return lstTest;
    }

    private async Task UpdateFunc(string indexId, string delay, string speed = "")
    {
        await _updateFunc?.Invoke(new() { IndexId = indexId, Delay = delay, Speed = speed });
        if (indexId.IsNotEmpty() && speed.IsNotEmpty())
        {
            ProfileExManager.Instance.SetTestMessage(indexId, speed);
        }
    }
}
