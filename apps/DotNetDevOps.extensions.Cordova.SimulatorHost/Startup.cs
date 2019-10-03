using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Text.Json;

namespace DotNetDevOps.extensions.Cordova.SimulatorHost
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHealthChecks();
            services.AddSignalR(o =>{ o.MaximumReceiveMessageSize = null; });
           
        }
        public void Configure(IApplicationBuilder app)
        {

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHealthChecks("/.well-known/ready", new HealthCheckOptions()
                {
                    Predicate = (check) => check.Tags.Contains("ready"),
                });

                endpoints.MapHealthChecks("/.well-known/live", new HealthCheckOptions
                {
                    Predicate = (_) => false
                });

                endpoints.MapPost("files", async (r) =>
                {

                    using (var ms = new MemoryStream())
                    {
                        await r.Request.Body.CopyToAsync(ms);

                        var fileInfo = JsonSerializer.Deserialize<FileRawEvent>(ms.ToArray());
                        
                        if(!Directory.Exists(Path.GetDirectoryName(Program.TargetPath + fileInfo.Path)))
                            Directory.CreateDirectory(Path.GetDirectoryName(Program.TargetPath + fileInfo.Path));
                        
                        await File.WriteAllBytesAsync(Program.TargetPath + fileInfo.Path, fileInfo.Data);

                    }
                     
                });

                endpoints.MapHub<StreamingHub>("/data");


            });

        }
        }
}
