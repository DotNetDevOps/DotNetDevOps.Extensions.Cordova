using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace DotNetDevOps.extensions.Cordova.SimulatorHost
{
    public class FileEvent
    {
        public long FileLength { get; set; }
        public string FileHash { get; set; }
        public string Event { get; set; }
        public string Path { get; set; }
    }
    public class FileRawEvent : FileEvent
    {
        public byte[] Data { get; set; }
    }
    public class StreamingHub : Hub 
    {

        public ChannelReader<string> Command(string command)
        {
            Console.WriteLine("Running command: " + command);

            var channel = Channel.CreateUnbounded<string>();
            
            async Task ExecuteCommand()
            {
                try
                {
                    var block = new ActionBlock<string>(async (line) =>
                    {
                        await channel.Writer.WriteAsync(line);

                    });
                    var process = new Process();


                    process.StartInfo.FileName = @"""C:\Program Files\nodejs\node.exe""";
                    process.StartInfo.Arguments = $@"""C:\Program Files\nodejs\node_modules\npm\bin\npm-cli.js"" {command}";
                    process.StartInfo.WorkingDirectory = Program.TargetPath;

                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.UseShellExecute = false;


                    process.StartInfo.RedirectStandardOutput = true;
                    process.OutputDataReceived += (sender, data) =>
                    {
                        Console.WriteLine(data.Data);
                        block.Post(data.Data);
                    };

                    process.StartInfo.RedirectStandardError = true;
                    process.ErrorDataReceived += (sender, data) =>
                    {
                        Console.WriteLine(data.Data);
                        block.Post(data.Data);
                    };


                    process.Start();
                    Console.WriteLine("Started");

                    process.WaitForExit();
                    Console.WriteLine("Completed");
                    block.Complete();
                    await block.Completion;
                    Console.WriteLine("Done");
                    channel.Writer.Complete();

                }
                catch(Exception ex)
                {
                    channel.Writer.Complete(ex);
                    Console.WriteLine(ex.ToString());
                }

            }

            _ = ExecuteCommand();


            return channel.Reader;
        }
        public async Task FileStream(IAsyncEnumerable<FileEvent> stream)
        {
            using (var md5 = MD5.Create())
            {
                await foreach (var item in stream)
                {

                    Console.WriteLine($"\t{item.Path} : {item.FileLength} : {item.FileHash}");

                    if (!File.Exists(Program.TargetPath + item.Path))
                    {
                        await Clients.Caller.SendAsync("GetFileData", item);
                    }
                    else
                    {
                        using (var fs = File.OpenRead(Program.TargetPath + item.Path))
                        {
                            var hash = BitConverter.ToString(md5.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                            if(hash != item.FileHash)
                            {
                                await Clients.Caller.SendAsync("GetFileData", item);
                            }
                        }

                    }


                }
            }
        }
    }
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
    class Program
    {
        public static string TargetPath { get; set; }
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");

            TargetPath = @"C:\dev\com.kjeldager.dcatest";
            if (Directory.Exists(TargetPath))
                Directory.Delete(TargetPath, true);
            Directory.CreateDirectory(TargetPath);

              var host = Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                }).Build();

            host.Run();

        }

       
    }
}
