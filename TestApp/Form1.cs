using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MQTTnet.AspNetCore;
using MqttRpc;
using System.Reflection.PortableExecutable;

namespace TestApp
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            var builder = WebApplication.CreateBuilder();



            // 註冊 MQTT Server
            builder.Services
                .AddHostedMqttServer(options =>
                {
                    options.WithDefaultEndpoint();    // TCP 1883

                })
                .AddMqttConnectionHandler()
                .AddConnections();

            // ⚠️ 加入 WebSocket 支援 (for MQTT over WebSocket)
            builder.Services.AddRouting();
            //builder.WebHost.UseUrls("http://0.0.0.0:5000", "http://127.0.0.1:6000");

            builder.WebHost.UseUrls("http://localhost:5678", "http://localhost:6666");

            var app = builder.Build();

            //app.Use(async (context, next) =>
            //{
            //    if (context.Request.Path.StartsWithSegments("/mqtt") && context.Connection.LocalPort != 1883)
            //    {
            //        context.Response.StatusCode = 403;
            //        await context.Response.WriteAsync("MQTT WebSocket only allowed on port 1883.");
            //        return;
            //    }

            //    await next();
            //});


            app.UseRouting();

            //app.UseWhen(ctx => ctx.Connection.LocalPort == 6666, app2 =>
            //{

            //    app2.UseEndpoints(endpoints =>
            //    {
            //        // WebSocket 路徑 /mqtt
            //        endpoints.MapConnectionHandler<MqttConnectionHandler>("/mqtt", options =>
            //        {
            //            options.WebSockets.SubProtocolSelector = protocolList => "mqtt";
            //        });
            //        endpoints.Map("/hii", () =>
            //        {
            //            return "hihi";
            //        });


            //    });


            //});
            app.UseEndpoints(endpoints =>
            {
                // WebSocket 路徑 /mqtt
                endpoints.MapConnectionHandler<MqttConnectionHandler>("/mqtt", options =>
                {
                    options.WebSockets.SubProtocolSelector = protocolList => "mqtt";
                });
                endpoints.Map("/hii", () =>
                {
                    return "hihi";
                });


            });

            // else

            app.MapGet("/hi", () =>
            {
                return "hi";
            });
            app.UseDefaultFiles();
            app.UseStaticFiles();

            // 
            app.UseMqttServer(
                 server =>
                 {
                     server.ClientConnectedAsync += async e =>
                     {

                     };
                     server.InterceptingPublishAsync += async e =>
                     {
                         MqttRpcExtension.HandleRpcRequest(e);
                     };
                     /*
                      * Attach event handlers etc. if required.
                      */

                 }
            );

            app.RunAsync();

        }

        private async void button1_Click(object sender, EventArgs e)
        {

            try
            {
                var client = new MqttRpcClient("ws://localhost", 6666);
                await client.ConnectAsync();
                var rtn = await client.CallAsync("device/getStatus", new { aaa = "bbb" });
                await client.DisposeAsync();
            }
            catch (Exception ex)
            {
            }


        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            await webView21.EnsureCoreWebView2Async();

            //var path = Path.Combine(Application.StartupPath, "wwwroot", "index.html");

            //webView21.Source = new Uri(path);

            webView21.Source = new Uri("localhost:5678");
        }

        private void formToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var frm = new Form2();
            frm.Show();
        }
    }
}
