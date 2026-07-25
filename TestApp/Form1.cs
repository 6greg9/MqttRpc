using System.Text.Json;
using MqttRpc;

namespace TestApp;

public partial class Form1 : Form
{
    private readonly MqttRpcRouter _router;
    private DesktopAppHost? _desktopHost;

    public Form1()
    {
        InitializeComponent();
        _router = DemoRpcRoutes.CreateRouter();
    }

    public async void button1_Click(object sender, EventArgs e)
    {
        if (_desktopHost is null)
        {
            MessageBox.Show("Desktop host is not ready.", "MqttRpc");
            return;
        }

        try
        {
            await using var client = new MqttRpcClient(
                new MqttRpcClientOptions(_desktopHost.MqttWebSocketUri));
            await client.ConnectAsync();

            var status = await client.CallAsync<GetStatusRequest, DeviceStatusResponse>(
                "device/getStatus",
                new GetStatusRequest("winforms-demo"));

            MessageBox.Show(
                JsonSerializer.Serialize(
                    status,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)
                    {
                        WriteIndented = true
                    }),
                ".NET MqttRpc response");
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "MqttRpc call failed");
        }
    }

    public async Task RebootAsync(string reason)
    {
        if (_desktopHost is null)
        {
            throw new InvalidOperationException("Desktop host is not ready.");
        }

        await using var client = new MqttRpcClient(
            new MqttRpcClientOptions(_desktopHost.MqttWebSocketUri));
        await client.ConnectAsync();

        var response = await client.CallAsync<RebootRequest, RebootResponse>(
            "device/reboot",
            new RebootRequest(reason));

        MessageBox.Show(response.Message, ".NET MqttRpc response");
    }

    private async void Form1_Load(object sender, EventArgs e)
    {
        try
        {
            _desktopHost = await DesktopAppHost.StartAsync(_router);
            await webView21.EnsureCoreWebView2Async();
            webView21.Source = _desktopHost.BaseAddress;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.ToString(), "Desktop host failed to start");
        }
    }

    private void formToolStripMenuItem_Click(object sender, EventArgs e)
    {
        var form = new Form2(this);
        form.Show();
    }

    protected override async void OnFormClosed(FormClosedEventArgs e)
    {
        if (_desktopHost is not null)
        {
            await _desktopHost.DisposeAsync();
        }

        base.OnFormClosed(e);
    }
}
