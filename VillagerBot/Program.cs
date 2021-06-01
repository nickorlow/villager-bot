using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Rest;
using Discord.WebSocket;

namespace VillagerBot
{
    class Program
    {
        public static void Main(string[] args)
            => new Program().MainAsync().GetAwaiter().GetResult();

        private DiscordSocketClient _client;
        private int UserThreshold = 3;
        private bool IsServerRunning;
        private bool ServerOverride;
        private Process GameProcess;
        private SocketRole activeRole;
        private DateTime StartTime;

        public async Task MainAsync()
        {
            _client = new DiscordSocketClient();
            _client.Log += Log;
            
            var token = Environment.GetEnvironmentVariable("DISCORD_API_KEY");
            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();
            _client.MessageReceived += CheckMessage;
            _client.UserVoiceStateUpdated += Example;
            StartTime = DateTime.UtcNow;
            _client.SetGameAsync("Server is OFF");
            await Task.Delay(-1);
        }

        private Task CheckMessage(SocketMessage m)
        {
            activeRole = _client.GetGuild(780935259239350353).GetRole(794295703786225676);
            try
            {
                if (m.Content.Length < 3 || m.Content[0..3] != "!mc")
                    return Task.CompletedTask;

                if (((SocketGuildUser)m.Author).Roles.Count(x => x.Name == ("villager-bot-admin")) == 0)
                {
                    m.Channel.SendMessageAsync("You don't have permission to do that!");
                    return Task.CompletedTask;
                }
                
                string[] parts = m.Content.Split(' ');
                switch (parts[1])
                {
                    case "limit":
                        UserThreshold = int.Parse(parts[2]);
                        m.Channel.SendMessageAsync($"Set user threshold to {UserThreshold}");
                        break;
                    
                    case "override":
                        ServerOverride = Boolean.Parse(parts[2]);
                        m.Channel.SendMessageAsync($"Server override is set to: {ServerOverride}");
                        if(!ServerOverride)
                            if (_client.GetChannel(780935259239350357).Users.Count < UserThreshold)
                                HaltServer();
                        break;
                    
                    case "start":
                        if(!ServerOverride)
                            m.Channel.SendMessageAsync($"Server override is set to: {ServerOverride}!");
                        StartServer();
                        break;
                    
                    case "halt":
                        if(!ServerOverride)
                            m.Channel.SendMessageAsync($"Server override is set to: {ServerOverride}!");
                        HaltServer();
                        break;

                    case "vardump":
                        string real = (GameProcess == null) ? "Never Started" : (!GameProcess.HasExited).ToString();
                        m.Channel.SendMessageAsync($"Debugging Variable Dump\n" +
                                                   $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                                   $"IsServerRunning: {IsServerRunning}\n" +
                                                   $"IsServerRunning (Process): {real}\n" +
                                                   $"Threshold: {UserThreshold}\n" +
                                                   $"Override: {ServerOverride}\n" +
                                                   $"ActiveUser Role: {activeRole.Name}\n" +
                                                   $".NET Version: {Environment.Version}\n" +
                                                   $"OperatingSystem: {Environment.OSVersion.Platform.ToString()}\n" +
                                                   $"OS Version: {Environment.OSVersion.VersionString}\n" +
                                                   $"Villager Bot Uptime: {DateTime.UtcNow - StartTime}\n" +
                                                   $"Villager Bot Start Time (UTC): {StartTime}\n" +
                                                   $"System Uptime: {TimeSpan.FromMilliseconds(Environment.TickCount64)}\n" + 
                                                   $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                        break;
                    
                    case "help":
                        m.Channel.SendMessageAsync($"\nMinecraft Bot Commands (Bot By nickorlow)\n" +
                                                        $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                                        $"!mc limit [num] => Sets threshold of users \n" +
                                                        $"!mc override [true/false] => Configures an override to ignore the limit\n" +
                                                        $"!mc [start/halt] => Starts/Halts the server if the override is on\n" +
                                                        $"!mc vardump => dumps variables");
                        break;
                    
                    default:
                        m.Channel.SendMessageAsync("Invalid Command!");
                        break;
                }
            }
            catch (Exception e)
            {
                m.Channel.SendMessageAsync("Invalid Command!");
            }
            return Task.CompletedTask;
        }
        
        private Task Example(SocketUser user, SocketVoiceState oldVoiceState, SocketVoiceState newVoiceState)
        {
            activeRole = _client.GetGuild(780935259239350353).GetRole(794295703786225676);
            if(oldVoiceState.VoiceChannel == null && newVoiceState.VoiceChannel != null)
            {
                //User joined
                Console.WriteLine($"User (Name: {user.Username} ID: {user.Id}) joined to a VoiceChannel (Name: {newVoiceState.VoiceChannel.Name} ID: {newVoiceState.VoiceChannel.Id})");
                
                if(ServerOverride)
                    return Task.CompletedTask;
             
                if(newVoiceState.VoiceChannel.Users.Count == 1)
                    (_client.GetGuild(780935259239350353).GetTextChannel(781007516859367495)).SendMessageAsync($"{activeRole.Mention} {user.Mention} is trying to play Minecraft. Join the VC to start the server!");
                if (newVoiceState.VoiceChannel.Users.Count >= UserThreshold)
                {
                    (_client.GetGuild(780935259239350353).GetTextChannel(781007516859367495)).SendMessageAsync(
                        $"{activeRole.Mention} User Threshold of {UserThreshold} has been met, starting Minecraft Server.");
                    StartServer();
                }
                else
                    (_client.GetGuild(780935259239350353).GetTextChannel(781007516859367495)).SendMessageAsync($"{activeRole.Mention} {newVoiceState.VoiceChannel.Users.Count} users in VC. Only {UserThreshold-newVoiceState.VoiceChannel.Users.Count} more needed!");
            }
            if (oldVoiceState.VoiceChannel != null && newVoiceState.VoiceChannel == null)
            {
                //User left
                Console.WriteLine($"User (Name: {user.Username} ID: {user.Id}) left from a VoiceChannel (Name: {oldVoiceState.VoiceChannel.Name} ID: {oldVoiceState.VoiceChannel.Id})");
                if (_client.GetChannel(780935259239350357).Users.Count < UserThreshold)
                    HaltServer();
            }
            return Task.CompletedTask;
        }

        private async Task StartServer()
        {
            if (IsServerRunning)
            {
                Console.WriteLine("Server is already running");
                return;
            }

            try
            {
                await (_client.GetGuild(780935259239350353).GetTextChannel(781007516859367495)).SendMessageAsync(
                    $"{activeRole.Mention} Server Starting!");
                
                GameProcess = new Process();
                GameProcess.StartInfo.FileName = "java";
                GameProcess.StartInfo.Arguments =
                    @"-jar " + @"C:\Users\Administrator\Documents\Minecraft2\paper-1.16.4-298.jar";
                GameProcess.Start();

                IsServerRunning = true;
                await (_client.GetGuild(780935259239350353).GetTextChannel(781007516859367495)).SendMessageAsync(
                    $"{activeRole.Mention} Server Started!");
                await _client.SetGameAsync("Server is ON");
            
            }
            catch (Exception e)
            {
                IsServerRunning = false;
                await (_client.GetGuild(780935259239350353).GetTextChannel(781007516859367495)).SendMessageAsync(
                    $"Code Broke! (Failed to start server)");
                await (_client.GetUser(397223060446511114)
                    .SendMessageAsync($"Fix Your Stuff!\n{e.Message}\n--\n{e.StackTrace}"));
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Fix Your Stuff!\n{e.Message}\n--\n{e.StackTrace}");
                Console.ForegroundColor = ConsoleColor.White;
            }
        }

        private async Task HaltServer()
        {
            if(!IsServerRunning)
                return;
            try
            {
                IsServerRunning = false;
                await (_client.GetGuild(780935259239350353).GetTextChannel(781007516859367495)).SendMessageAsync(
                    $"{activeRole.Mention} Server halting in one minute!");
                Thread.Sleep(50000);

                RestUserMessage msg = await (_client.GetGuild(780935259239350353).GetTextChannel(781007516859367495)).SendMessageAsync(
                    $"{activeRole.Mention} Server halting in 10 seconds!");
                
                Thread.Sleep(10000);
                
                GameProcess.CloseMainWindow();
                
                await _client.GetGuild(780935259239350353).GetTextChannel(781007516859367495).SendMessageAsync(
                    $"{activeRole.Mention} Server has been halted!");
                
                await _client.SetGameAsync("Server is OFF");
            }
            catch (Exception e)
            {
                await (_client.GetGuild(780935259239350353).GetTextChannel(781007516859367495)).SendMessageAsync(
                    $"Code Broke.. (Failed to stop server)");
                await _client.GetUser(397223060446511114)
                    .SendMessageAsync($"Fix Your Stuff!\n{e.Message}\n--\n{e.StackTrace}");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Fix Your Stuff!\n{e.Message}\n--\n{e.StackTrace}");
                Console.ForegroundColor = ConsoleColor.White;
            }
        }
        
        private Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }
    }
}
