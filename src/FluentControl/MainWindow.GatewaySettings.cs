using FluentControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static FluentControl.Services.Strings;
namespace FluentControl;

public sealed partial class MainWindow
{
    private void BuildGatewaySettings(StackPanel section)
    {
        var settings = state.Settings.RemoteGateway ??= new();
        section.Children.Add(new TextBlock { Text = T("默认使用 FC 自带的本机网关。可连接 NAS 或树莓派上的远程网关；USB 配网仍在本机完成。", "FC uses its bundled local Gateway by default. Optionally connect to a Gateway on a NAS or Raspberry Pi; USB setup stays local."), TextWrapping = TextWrapping.Wrap });
        var address = new TextBox { Header = T("远程网关地址", "Remote Gateway address"), Text = settings.Address, PlaceholderText = "https://gateway.example/", MaxLength = 512 };
        var token = new PasswordBox { Header = T("网关访问令牌", "Gateway access token"), MaxLength = 64 };
        var insecure = new CheckBox { Content = T("允许在可信局域网中使用明文 HTTP", "Allow unencrypted HTTP on a trusted private network"), IsChecked = settings.AllowPrivateHttp };
        var apply = new Button { Content = T("测试并连接远程网关", "Test and connect to remote Gateway"), IsEnabled = !uiTest };
        var local = new Button { Content = T("使用 FC 自带的本机网关", "Use FC's bundled local Gateway"), IsEnabled = settings.Enabled && !uiTest };
        section.Children.Add(address); section.Children.Add(token); section.Children.Add(insecure); section.Children.Add(apply); section.Children.Add(local);
        apply.Click += async (_, _) =>
        {
            apply.IsEnabled = false; local.IsEnabled = false;
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(sensorLifetime.Token); cancel.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                var uri = FcsRemoteGateway.ValidateAddress(address.Text, insecure.IsChecked == true);
                var key = token.Password.Length == 0 ? DeviceKeyStore.Read(FcsRemoteGateway.KeyId(uri)) : token.Password;
                await FcsRemoteGateway.TestAsync(uri, key, cancel.Token);
                if (closed) return;
                DeviceKeyStore.Save(FcsRemoteGateway.KeyId(uri), key);
                settings.Address = uri.AbsoluteUri; settings.AllowPrivateHttp = insecure.IsChecked == true; settings.Enabled = true;
                FcsRemoteGateway.Configure(settings); lightSourceListRevision++;
                SaveAdaptiveSettings(); // Invalidates the previous connection's pending brightness work.
                token.Password = "";
                ShowStatus(T("已连接远程网关，可通过发现设备添加订阅。", "Remote Gateway connected. Discover devices to add subscriptions."), InfoBarSeverity.Success);
            }
            catch (OperationCanceledException) { }
            catch { if (!closed) ShowStatus(T("网关连接失败，请检查地址、令牌和网络。", "Gateway connection failed. Check the address, token and network."), InfoBarSeverity.Warning); }
            finally { if (!closed) { apply.IsEnabled = true; local.IsEnabled = settings.Enabled; } }
        };
        local.Click += (_, _) =>
        {
            settings.Enabled = false; FcsRemoteGateway.Configure(settings); lightSourceListRevision++; SaveAdaptiveSettings();
            FcsGatewayClient.EnsureStarted(); BuildSettings();
        };
    }
}
