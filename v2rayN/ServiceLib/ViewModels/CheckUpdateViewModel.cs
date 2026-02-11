namespace ServiceLib.ViewModels;

public class CheckUpdateViewModel : MyReactiveObject
{
    private const string _geo = "GeoFiles";
    private List<CheckUpdateModel> _lstUpdated = [];
    private static readonly string _tag = "CheckUpdateViewModel";

    public IObservableCollection<CheckUpdateModel> CheckUpdateModels { get; } = new ObservableCollectionExtended<CheckUpdateModel>();
    public ReactiveCommand<Unit, Unit> CheckUpdateCmd { get; }

    public CheckUpdateViewModel(Func<EViewAction, object?, Task<bool>>? updateView)
    {
        _config = AppManager.Instance.Config;
        _updateView = updateView;

        CheckUpdateCmd = ReactiveCommand.CreateFromTask(CheckUpdate);
        CheckUpdateCmd.ThrownExceptions.Subscribe(ex =>
        {
            Logging.SaveLog(_tag, ex);
        });

        RefreshCheckUpdateItems();
    }

    private void RefreshCheckUpdateItems()
    {
        CheckUpdateModels.Clear();

        //Not Windows and under Win10
        if (!(Utils.IsWindows() && Environment.OSVersion.Version.Major < 10))
        {
            CheckUpdateModels.Add(GetCheckUpdateModel(ECoreType.Xray.ToString()));
            CheckUpdateModels.Add(GetCheckUpdateModel(ECoreType.mihomo.ToString()));
            CheckUpdateModels.Add(GetCheckUpdateModel(ECoreType.sing_box.ToString()));
        }
        CheckUpdateModels.Add(GetCheckUpdateModel(_geo));
    }

    private CheckUpdateModel GetCheckUpdateModel(string coreType)
    {
        return new()
        {
            IsSelected = _config.CheckUpdateItem.SelectedCoreTypes?.Contains(coreType) ?? true,
            CoreType = coreType,
            Remarks = ResUI.menuCheckUpdate,
        };
    }

    private async Task SaveSelectedCoreTypes()
    {
        _config.CheckUpdateItem.SelectedCoreTypes = CheckUpdateModels.Where(t => t.IsSelected == true).Select(t => t.CoreType ?? "").ToList();
        await ConfigHandler.SaveConfig(_config);
    }

    private async Task CheckUpdate()
    {
        await Task.Run(CheckUpdateTask);
    }

    private async Task CheckUpdateTask()
    {
        _lstUpdated.Clear();
        _lstUpdated = CheckUpdateModels.Where(x => x.IsSelected == true)
                .Select(x => new CheckUpdateModel() { CoreType = x.CoreType }).ToList();
        await SaveSelectedCoreTypes();

        for (var k = CheckUpdateModels.Count - 1; k >= 0; k--)
        {
            var item = CheckUpdateModels[k];
            if (item.IsSelected != true)
            {
                continue;
            }

            await UpdateView(item.CoreType, "...");
            if (item.CoreType == _geo)
            {
                await CheckUpdateGeo();
            }
            else
            {
                await CheckUpdateCore(item, false);
            }
        }

        await UpdateFinished();
    }

    private void UpdatedPlusPlus(string coreType, string fileName)
    {
        var item = _lstUpdated.FirstOrDefault(x => x.CoreType == coreType);
        if (item == null)
        {
            return;
        }
        item.IsFinished = true;
        if (!fileName.IsNullOrEmpty())
        {
            item.FileName = fileName;
        }
    }

    private async Task CheckUpdateGeo()
    {
        async Task _updateUI(bool success, string msg)
        {
            await UpdateView(_geo, msg);
            if (success)
            {
                UpdatedPlusPlus(_geo, "");
            }
        }
        await new UpdateService(_config, _updateUI).UpdateGeoFileAll()
            .ContinueWith(t => UpdatedPlusPlus(_geo, ""));
    }

    private async Task CheckUpdateCore(CheckUpdateModel model, bool preRelease)
    {
        async Task _updateUI(bool success, string msg)
        {
            await UpdateView(model.CoreType, msg);
            if (success)
            {
                await UpdateView(model.CoreType, ResUI.MsgUpdateV2rayCoreSuccessfullyMore);

                UpdatedPlusPlus(model.CoreType, msg);
            }
        }
        var type = (ECoreType)Enum.Parse(typeof(ECoreType), model.CoreType);
        await new UpdateService(_config, _updateUI).CheckUpdateCore(type, preRelease)
            .ContinueWith(t => UpdatedPlusPlus(model.CoreType, ""));
    }

    private async Task UpdateFinished()
    {
        if (_lstUpdated.Count > 0 && _lstUpdated.Count(x => x.IsFinished == true) == _lstUpdated.Count)
        {
            await UpdateFinishedSub(false);
            await Task.Delay(2000);
            await UpgradeCore();
            await Task.Delay(1000);
            await UpdateFinishedSub(true);
        }
    }

    private async Task UpdateFinishedSub(bool blReload)
    {
        RxApp.MainThreadScheduler.Schedule(blReload, (scheduler, blReload) =>
        {
            _ = UpdateFinishedResult(blReload);
            return Disposable.Empty;
        });
        await Task.CompletedTask;
    }

    public async Task UpdateFinishedResult(bool blReload)
    {
        if (blReload)
        {
            AppEvents.ReloadRequested.Publish();
        }
        else
        {
            await CoreManager.Instance.CoreStop();
        }
    }

    private async Task UpgradeCore()
    {
        foreach (var item in _lstUpdated)
        {
            if (item.FileName.IsNullOrEmpty())
            {
                continue;
            }

            var fileName = item.FileName;
            if (!File.Exists(fileName))
            {
                continue;
            }
            var toPath = Utils.GetBinPath("", item.CoreType);

            if (fileName.Contains(".tar.gz"))
            {
                FileUtils.DecompressTarFile(fileName, toPath);
                var dir = new DirectoryInfo(toPath);
                if (dir.Exists)
                {
                    foreach (var subDir in dir.GetDirectories())
                    {
                        FileUtils.CopyDirectory(subDir.FullName, toPath, false, true);
                        subDir.Delete(true);
                    }
                }
            }
            else if (fileName.Contains(".gz"))
            {
                FileUtils.DecompressFile(fileName, toPath, item.CoreType);
            }
            else
            {
                FileUtils.ZipExtractToFile(fileName, toPath, "geo");
            }

            if (Utils.IsNonWindows())
            {
                var filesList = new DirectoryInfo(toPath).GetFiles().Select(u => u.FullName).ToList();
                foreach (var file in filesList)
                {
                    await Utils.SetLinuxChmod(Path.Combine(toPath, item.CoreType.ToLower()));
                }
            }

            await UpdateView(item.CoreType, ResUI.MsgUpdateV2rayCoreSuccessfully);

            if (File.Exists(fileName))
            {
                File.Delete(fileName);
            }
        }
    }

    private async Task UpdateView(string coreType, string msg)
    {
        var item = new CheckUpdateModel()
        {
            CoreType = coreType,
            Remarks = msg,
        };

        RxApp.MainThreadScheduler.Schedule(item, (scheduler, model) =>
        {
            _ = UpdateViewResult(model);
            return Disposable.Empty;
        });
        await Task.CompletedTask;
    }

    public async Task UpdateViewResult(CheckUpdateModel model)
    {
        var found = CheckUpdateModels.FirstOrDefault(t => t.CoreType == model.CoreType);
        if (found == null)
        {
            return;
        }
        found.Remarks = model.Remarks;
        await Task.CompletedTask;
    }
}
