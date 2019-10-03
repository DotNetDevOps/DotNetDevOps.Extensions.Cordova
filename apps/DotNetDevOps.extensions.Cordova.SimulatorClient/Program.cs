using Microsoft.AspNetCore.SignalR.Client;
using Polly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace DotNetDevOps.extensions.Cordova.SimulatorClient
{
    public class FileEvent
    {
        public long FileLength { get; set; }
        public string FileHash { get; set; }
        public string Event { get; set; }
        public string Path { get; set; }
    }
    public class FileRawEvent: FileEvent
    {
       public byte[] Data { get; set; }
    }
    class Program
    {
        static async Task Main(string[] args)
        {
            var path = @"C:\dev\com.kjeldager.dca".Replace("\\","/");
            var http = new HttpClient() { BaseAddress = new Uri("http://localhost:5000") };

            var connection = await Polly.Policy.Handle<Exception>().RetryAsync(3).ExecuteAsync(async () =>
            {
                var connection = new HubConnectionBuilder().WithAutomaticReconnect().WithUrl("http://localhost:5000/data").Build();


                connection.On("GetFileData", async (FileRawEvent file) =>
             {
                 file.Data = await File.ReadAllBytesAsync(path + file.Path);

                 await http.PostAsync("/files", new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(file)));
             });
                await connection.StartAsync();
                return connection;
            });


            var ignores = new List<string>() { "/.git", "*.TMP" ,"*~" };

            var channel = Channel.CreateBounded<FileEvent>(10);

            using (FileSystemWatcher watcher = new FileSystemWatcher())
            {
                watcher.Path = path;

                // Watch for changes in LastAccess and LastWrite times, and
                // the renaming of files or directories.
                watcher.NotifyFilter = NotifyFilters.LastAccess
                                     | NotifyFilters.LastWrite
                                     | NotifyFilters.FileName
                                     | NotifyFilters.DirectoryName;

                // Only watch text files.
                //  watcher.Filter = "*.txt";
                var queu = new Queue<string>();
                var block = new ActionBlock<string>(async (file) =>
                {
                    using (var md5 = MD5.Create())
                    {
                        await SendFileInfoEvent(path, channel, md5, file);
                    }
                });

                void OnChanged(object source, FileSystemEventArgs e)
                {

                    if (TestFilePath(e, path, ignores))
                        return;

                    // Specify what is done when a file is changed, created, or deleted.
                    Console.WriteLine($"File: {e.FullPath} {e.ChangeType}");

                    if (!block.Post(e.FullPath))
                    {
                        queu.Enqueue(e.FullPath);
                    }


                }

                void OnRenamed(object source, RenamedEventArgs e)
                {


                    if (TestFilePath(e, path, ignores))
                        return;



                    // Specify what is done when a file is renamed.
                    Console.WriteLine($"File: {e.OldFullPath} renamed to {e.FullPath}");

                    if (!block.Post(e.FullPath))
                    {
                        queu.Enqueue(e.FullPath);
                    }

                }

                // Add event handlers.
                watcher.Changed += OnChanged;
                watcher.Created += OnChanged;
                watcher.Deleted += OnChanged;
                watcher.Renamed += OnRenamed;

                // Begin watching.
                watcher.EnableRaisingEvents = true;





                await connection.SendAsync("FileStream", channel.Reader);
                using (var md5 = MD5.Create())
                {


                    await ScanDirAsync(path, path, channel, md5, ignores);

                }






                channel.Writer.Complete();
                 

                await RunCommandAsync(connection, "install");
                await RunCommandAsync(connection, "run generate");

                await Task.Delay(Timeout.Infinite);

            }

            Console.WriteLine("Hello World!");
           
        }

        private static async Task RunCommandAsync(HubConnection connection, string command)
        {
            await foreach (var log in connection.StreamAsync<string>("Command", command))
            {
                Console.WriteLine(log);
            }
        }

        private static bool TestFilePath(FileSystemEventArgs e, string path, List<string> ignores)
        {
            var fullPath = Path.GetDirectoryName(e.FullPath).Replace("\\","/").Trim('/');
            while(fullPath.Length >= path.Length)
            {
                if (File.Exists(fullPath + "/.gitignore"))
                {
                    ignores = ignores.Concat(Policy
                        .Handle<Exception>()
                        .Retry(3).Execute(()=>
                            File.ReadAllLines(fullPath + "/.gitignore").Where(l => l.Length > 0 && !l.StartsWith("#")))
                        ).ToList();
                }
                fullPath = Path.GetDirectoryName( fullPath).Trim('/'); 
            }

            
            if (ignores.Any(i => e.FullPath.StartsWith(path + i)))
            {
                return true;
            }
            foreach (var ignore in ignores.Where(i => i.Contains("*")))
            {
                var patternEscapedForStar = path + ignore;
                patternEscapedForStar = patternEscapedForStar.Replace(".", "\\.").Replace(@"*", @"[^/]*");
                if (new Regex(patternEscapedForStar).IsMatch(e.FullPath))
                {
                    return true;
                }

            }

            return false;
        }

        private static async Task<List<string>> ScanDirAsync(string roothPath,string path, Channel<FileEvent> channel, MD5 md5, List<string> ignores)
        {
            ignores = await ScanFilesAsync(roothPath, path, channel, md5, ignores);

            foreach (var dir in Directory.GetDirectories(path).Select(d=>d.Replace("\\","/")))
            {
                if (ignores.Any(i => dir.StartsWith(path + i)))
                {
                    continue;
                }

                // await ScanFilesAsync(path, channel, md5, ignores);
                await ScanDirAsync(roothPath,dir, channel, md5, ignores);
            }

            return ignores;
        }

        private static async Task<List<string>> ScanFilesAsync(string roothPath,string path, Channel<FileEvent> channel, MD5 md5,List<string> igores)
        {
            var files = Directory.GetFiles(path).Select(k => k.Replace("\\", "/")).ToArray();
            var gitIgnore = igores.Concat(
                files.Where(f => Path.GetFileName(f) == ".gitignore").Select(f => File.ReadAllLines(f)).SelectMany(l => l).Where(l => l.Length > 0&& !l.StartsWith("#"))).ToList();

            foreach (var file in files)
            {
                if (gitIgnore.Any(i => file.StartsWith(path+ i)))
                {
                    continue;
                }
                //var info = new FileInfo(file);

                await SendFileInfoEvent(roothPath, channel, md5, file);
            }
            return gitIgnore;
        }

        private static async Task SendFileInfoEvent(string roothPath, Channel<FileEvent> channel, MD5 md5, string file)
        {
            using (var stream = File.OpenRead(file))
            {


                await channel.Writer.WriteAsync(
                    new FileEvent
                    {
                        Path = file.Substring(roothPath.Length).Replace("\\", "/"),
                        Event = "SCAN",
                        FileLength = stream.Length,
                        FileHash = BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", "").ToLowerInvariant()
                    });
            }
        }
    }
}
