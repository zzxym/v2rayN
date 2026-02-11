using ReactiveUI;
using ServiceLib.Handler;
using ServiceLib.Models;
using System.Collections.ObjectModel;
using System.IO;
using ServiceLib.Manager;
using System.Text.Json;

namespace ServiceLib.ViewModels;

public class SubscriptionMaintenanceViewModel : MyReactiveObject
{
    private Config _config;
    private readonly Func<EViewAction, object?, Task<bool>>? _updateView;

    // Observable properties
    [Reactive]
    public ObservableCollection<SubItem> Subscriptions { get; set; } = [];

    [Reactive]
    public SubItem SelectedSubscription { get; set; }

    [Reactive]
    public ObservableCollection<ServerConfig> Servers { get; set; } = [];

    [Reactive]
    public ServerConfig SelectedServer { get; set; }

    [Reactive]
    public ObservableCollection<string> Protocols { get; set; } = [];

    [Reactive]
    public string SelectedProtocol { get; set; }

    [Reactive]
    public string Username { get; set; }

    [Reactive]
    public string Password { get; set; }

    [Reactive]
    public string TargetPath { get; set; }

    [Reactive]
    public string Status { get; set; }

    // Commands
    public ReactiveCommand<Unit, Unit> UploadCmd { get; }
    public ReactiveCommand<Unit, Unit> RefreshCmd { get; }
    public ReactiveCommand<Unit, Unit> SaveServerCmd { get; }

    public SubscriptionMaintenanceViewModel(Func<EViewAction, object?, Task<bool>>? updateView)
    {
        _config = AppManager.Instance.Config;
        _updateView = updateView;

        // Initialize commands
        UploadCmd = ReactiveCommand.CreateFromTask(UploadAsync);
        RefreshCmd = ReactiveCommand.CreateFromTask(RefreshAsync);
        SaveServerCmd = ReactiveCommand.CreateFromTask(SaveServerAsync);

        // Initialize data
        InitializeData();
    }

    private void InitializeData()
    {
        // Load subscriptions
        LoadSubscriptions();

        // Load protocols (only WebDAV)
        Protocols.Add("WebDAV");
        SelectedProtocol = Protocols[0];

        // Load servers from config
        LoadServers();

        // Set status
        Status = "就绪";
    }

    private async Task LoadSubscriptions()
    {
        var subscriptions = await AppManager.Instance.SubItems();
        Subscriptions.Clear();
        Subscriptions.AddRange(subscriptions);

        if (Subscriptions.Count > 0)
        {
            // Get current selected subscription from config
            var currentSubId = _config.SubIndexId;
            if (!currentSubId.IsNullOrEmpty())
            {
                var currentSub = Subscriptions.FirstOrDefault(s => s.Id == currentSubId);
                if (currentSub != null)
                {
                    SelectedSubscription = currentSub;
                    return;
                }
            }
            // Fallback to first subscription if no current selected
            SelectedSubscription = Subscriptions[0];
        }
    }

    private void LoadServers()
    {
        try
        {
            // Create config directory if not exists
            var configDir = Path.Combine(Utils.StartupPath(), "guiConfigs");
            Directory.CreateDirectory(configDir);

            // Load server configs
            var configFile = Path.Combine(configDir, "serverConfigs.json");
            if (File.Exists(configFile))
            {
                var jsonContent = File.ReadAllText(configFile);
                var serverConfigs = JsonSerializer.Deserialize<List<ServerConfig>>(jsonContent);
                if (serverConfigs != null)
                {
                    Servers.Clear();
                    Servers.AddRange(serverConfigs);
                }
            }

            // Add default server if no servers exist
            if (Servers.Count == 0)
            {
                var defaultServer = new ServerConfig
                {
                    Name = "默认服务器",
                    Address = "https://example.com/webdav",
                    Username = "",
                    Password = "",
                    TargetPath = "/"
                };
                Servers.Add(defaultServer);
            }

            if (Servers.Count > 0)
            {
                SelectedServer = Servers[0];
                // Update form fields with selected server info
                Username = SelectedServer.Username;
                Password = SelectedServer.Password;
                TargetPath = SelectedServer.TargetPath ?? "/";
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("LoadServers", ex);
            Status = $"加载服务器配置失败: {ex.Message}";
        }
    }

    private async Task SaveServerAsync()
    {
        try
        {
            if (SelectedServer == null)
            {
                Status = "请选择服务器";
                return;
            }

            // Update selected server info
            SelectedServer.Username = Username;
            SelectedServer.Password = Password;
            SelectedServer.TargetPath = TargetPath;

            // Save to config file
            var configDir = Path.Combine(Utils.StartupPath(), "guiConfigs");
            Directory.CreateDirectory(configDir);

            var configFile = Path.Combine(configDir, "serverConfigs.json");
            var jsonContent = JsonSerializer.Serialize(Servers, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(configFile, jsonContent);

            Status = "服务器配置保存成功";
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SaveServerAsync", ex);
            Status = $"保存服务器配置失败: {ex.Message}";
        }
    }

    private async Task UploadAsync()
    {
        if (SelectedSubscription == null)
        {
            Status = "请选择订阅分组";
            return;
        }

        if (SelectedServer == null)
        {
            Status = "请选择服务器";
            return;
        }

        try
        {
            Status = "正在上传...";

            // Get the file path for the selected subscription
            var fileName = Path.Combine(Utils.StartupPath(), "guiNodes", SelectedSubscription.Remarks);
            if (!File.Exists(fileName))
            {
                Status = $"文件不存在: {fileName}";
                return;
            }

            // Read file content
            var fileContent = await File.ReadAllTextAsync(fileName);

            // Upload via WebDAV
            await UploadViaWebDav(fileName, SelectedServer, Username, Password, TargetPath);

            Status = "上传成功";
        }
        catch (Exception ex)
        {
            Status = $"上传失败: {ex.Message}";
        }
    }

    private async Task UploadViaWebDav(string fileName, ServerConfig server, string username, string password, string targetPath)
    {
        try
        {
            using var httpClient = new HttpClient();
            
            // Set timeout
            httpClient.Timeout = TimeSpan.FromMinutes(5);
            
            // Add authentication
            var credential = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{username}:{password}"));
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credential);
            
            // Construct target URL
            var fileNameOnly = Path.GetFileName(fileName);
            var targetUrl = new Uri(new Uri(server.Address.TrimEnd('/')), $"{targetPath.TrimEnd('/')}/{fileNameOnly}");
            
            // Read file content
            var fileContent = await File.ReadAllBytesAsync(fileName);
            using var content = new ByteArrayContent(fileContent);
            
            // Set content type
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            
            // Execute PUT request
            var response = await httpClient.PutAsync(targetUrl, content);
            
            // Check response
            response.EnsureSuccessStatusCode();
            
            Status = $"通过WebDAV上传到 {server.Name} 成功";
        }
        catch (Exception ex)
        {
            Logging.SaveLog("UploadViaWebDav", ex);
            Status = $"通过WebDAV上传失败: {ex.Message}";
            throw;
        }
    }

    private async Task RefreshAsync()
    {
        await LoadSubscriptions();
        LoadServers();
        Status = "已刷新";
    }
}

// Server config model
public class ServerConfig
{
    public string Name { get; set; }
    public string Address { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
    public string TargetPath { get; set; }
}
