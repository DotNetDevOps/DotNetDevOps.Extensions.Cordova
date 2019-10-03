using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace DotNetDevOps.extensions.Cordova.SimulatorHost
{
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
}
