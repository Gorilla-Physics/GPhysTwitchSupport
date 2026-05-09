using BepInEx;
using GorillaLocomotion;
using GPhys.Abstracts;
using GPhys.Types;
using GPhys.Types.Objects;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace GPhysTwitchSupport
{
    public class Plugin : IGPhysPlugin
    {
        public class ChatMessage
        {
            public string Platform;
            public string Username;
            public string Message;
            public bool IsModerator;
            public bool IsBroadcaster;
        }

        public class ChatCommand
        {
            public string Name;
            public Action<ChatMessage, string[]> Execute;
        }

        public string PluginName => "GPhys Twitch Support";
        public string Version => "1.1.0";

        public Dictionary<string, ChatCommand> Commands = new Dictionary<string, ChatCommand>();

        private TwitchIRCClient twitchClient;
        private YouTubeChatClient youtubeClient;

        [Unused]
        public void Cleanup()
        {
            twitchClient?.Disconnect();
            youtubeClient?.Disconnect();
        }

        [Unused]
        public void OnGUI() { }

        [Unused]
        public void RegisterSpawnables() { }

        [Unused]
        public void Update()
        {
            twitchClient?.ProcessMessages();
            youtubeClient?.ProcessMessages();
        }

        public void Initialize(GPhys.Plugin gphysInstance)
        {
            Commands["spawnobject"] = new ChatCommand
            {
                Name = "spawnobject",
                Execute = (msg, args) =>
                {
                    string objectName = args.Length > 0 ? args[0] : "Headcrab";
                    gphysInstance.SpawnObjectWithName(objectName, GTPlayer.Instance.headCollider.bounds.center, Quaternion.identity);
                }
            };

            Commands["killplayer"] = new ChatCommand
            {
                Name = "killplayer",
                Execute = (msg, args) =>
                {
                    GTHealth.Instance.Damage(1200f);
                }
            };

            Commands["damageplayer"] = new ChatCommand
            {
                Name = "damageplayer",
                Execute = (msg, args) =>
                {
                    if (args.Length > 0 && float.TryParse(args[0], out float damage))
                    {
                        GTHealth.Instance.Damage(damage);
                    }
                }
            };

            Commands["healplayer"] = new ChatCommand
            {
                Name = "healplayer",
                Execute = (msg, args) =>
                {
                    if (args.Length > 0 && float.TryParse(args[0], out float heal))
                    {
                        GTHealth.Instance.Heal(heal);
                    }
                }
            };

            Commands["canister"] = new ChatCommand
            {
                Name = "canister",
                Execute = (msg, args) =>
                {
                    // parse type
                    HeadcrabType type = args.Length > 0 ? args[0].ToLower() switch
                    {
                        "fast" => HeadcrabType.Fast,
                        "poison" => HeadcrabType.Poison,
                        "classic" => HeadcrabType.Normal,
                        "default" => HeadcrabType.Normal,
                        "baby" => HeadcrabType.Baby,
                        _ => HeadcrabType.Normal
                    } : HeadcrabType.Normal;
                    int count = args.Length > 1 && int.TryParse(args[1], out int c) ? c : 4;
                    Vector3 position = GTPlayer.Instance.headCollider.bounds.center;
                    HeadcrabCanisterManager.LaunchStrikeAt(position, type, count);
                }
            };

            Commands["explosion"] = new ChatCommand
            {
                Name = "explosion",
                Execute = (msg, args) =>
                {
                    Vector3 position = GTPlayer.Instance.headCollider.bounds.center;
                    ExplosionLibrary.Instance.Explode("Default", position);
                }
            };

            Commands["cleanup"] = new ChatCommand
            {
                Name = "cleanup",
                Execute = (msg, args) =>
                {
                    gphysInstance.Cleanup();
                }
            };

            string twitchOAuth = LoadConfig("twitch_oauth");
            string twitchChannel = LoadConfig("twitch_channel");
            string twitchNick = LoadConfig("twitch_nick");

            if (!string.IsNullOrEmpty(twitchOAuth) && !string.IsNullOrEmpty(twitchChannel))
            {
                twitchClient = new TwitchIRCClient(twitchOAuth, twitchNick, twitchChannel, OnGotMessageage);
                twitchClient.Connect();
            }

            string youtubeApiKey = LoadConfig("youtube_api_key");
            string youtubeLiveId = LoadConfig("youtube_live_id");

            if (!string.IsNullOrEmpty(youtubeApiKey) && !string.IsNullOrEmpty(youtubeLiveId))
            {
                youtubeClient = new YouTubeChatClient(youtubeApiKey, youtubeLiveId, OnGotMessageage);
                youtubeClient.Connect();
            }
        }

        private string LoadConfig(string key)
        {
            string configPath = Path.Combine(Paths.ConfigPath, "GTwitch.txt");
            if (File.Exists(configPath))
            {
                foreach (string line in File.ReadAllLines(configPath))
                {
                    if (line.StartsWith(key + "="))
                    {
                        return line.Substring(key.Length + 1).Trim();
                    }
                }
            }
            else
            {
                Debug.LogWarning($"Config file not found: {configPath}");
                File.WriteAllText(configPath, "twitch_oauth=\ntwitch_channel=\ntwitch_nick=\nyoutube_api_key=\nyoutube_live_id=");
            }
            return null;
        }

        public void OnGotMessageage(ChatMessage message)
        {
            if (message.Message.StartsWith("!"))
            {
                string[] parts = message.Message.Substring(1).Split(' ');
                string commandName = parts[0].ToLower();
                string[] args = parts.Length > 1 ? parts[1..] : new string[0];
                if (Commands.TryGetValue(commandName, out ChatCommand command))
                {
                    command.Execute(message, args);
                }
                else
                {
                    Debug.Log($"Unknown command: {commandName} from {message.Platform}");
                }
            }
        }
    }

    public class TwitchIRCClient
    {
        private TcpClient tcpClient;
        private StreamReader reader;
        private StreamWriter writer;
        private Thread readThread;
        private Queue<string> messageQueue = new Queue<string>();
        private object queueLock = new object();
        private Action<Plugin.ChatMessage> onMessage;
        private string channel;
        private bool running;

        public TwitchIRCClient(string oauth, string nick, string channel, Action<Plugin.ChatMessage> onMessage)
        {
            this.channel = channel.ToLower();
            this.onMessage = onMessage;
            this.oauth = oauth;
            this.nick = nick;
        }

        private string oauth;
        private string nick;

        public void Connect()
        {
            tcpClient = new TcpClient("irc.chat.twitch.tv", 6667);
            reader = new StreamReader(tcpClient.GetStream());
            writer = new StreamWriter(tcpClient.GetStream()) { NewLine = "\r\n", AutoFlush = true };

            writer.WriteLine($"PASS {oauth}");
            writer.WriteLine($"NICK {nick}");
            writer.WriteLine($"JOIN #{channel}");
            writer.WriteLine("CAP REQ :twitch.tv/tags");

            running = true;
            readThread = new Thread(ReadMessages);
            readThread.Start();
        }

        private void ReadMessages()
        {
            while (running && tcpClient.Connected)
            {
                try
                {
                    string line = reader.ReadLine();
                    if (line != null)
                    {
                        if (line.StartsWith("PING"))
                        {
                            writer.WriteLine("PONG :tmi.twitch.tv");
                        }
                        else if (line.Contains("PRIVMSG"))
                        {
                            lock (queueLock)
                            {
                                messageQueue.Enqueue(line);
                            }
                        }
                    }
                }
                catch { }
            }
        }

        public void ProcessMessages()
        {
            lock (queueLock)
            {
                while (messageQueue.Count > 0)
                {
                    string line = messageQueue.Dequeue();
                    ParseMessage(line);
                }
            }
        }

        private void ParseMessage(string line)
        {
            int messageStart = line.IndexOf("PRIVMSG");
            if (messageStart == -1) return;

            int colonIndex = line.IndexOf(':', messageStart);
            if (colonIndex == -1) return;

            string message = line.Substring(colonIndex + 1);

            string username = "";
            int userStart = line.IndexOf("display-name=");
            if (userStart != -1)
            {
                userStart += 13;
                int userEnd = line.IndexOf(';', userStart);
                if (userEnd != -1)
                {
                    username = line.Substring(userStart, userEnd - userStart);
                }
            }

            bool isMod = line.Contains("mod=1");
            bool isBroadcaster = line.Contains("badges=broadcaster");

            onMessage?.Invoke(new Plugin.ChatMessage
            {
                Platform = "Twitch",
                Username = username,
                Message = message,
                IsModerator = isMod,
                IsBroadcaster = isBroadcaster
            });
        }

        public void Disconnect()
        {
            running = false;
            tcpClient?.Close();
            readThread?.Join(1000);
        }
    }

    public class YouTubeChatClient
    {
        private HttpClient httpClient;
        private string apiKey;
        private string liveChatId;
        private Action<Plugin.ChatMessage> onMessage;
        private Thread pollThread;
        private bool running;
        private string nextPageToken = "";
        private HashSet<string> processedMessages = new HashSet<string>();

        public YouTubeChatClient(string apiKey, string liveId, Action<Plugin.ChatMessage> onMessage)
        {
            this.apiKey = apiKey;
            this.onMessage = onMessage;
            httpClient = new HttpClient();
            GetLiveChatId(liveId);
        }

        private async void GetLiveChatId(string liveId)
        {
            try
            {
                string url = $"https://www.googleapis.com/youtube/v3/videos?part=liveStreamingDetails&id={liveId}&key={apiKey}";
                string response = await httpClient.GetStringAsync(url);

                int chatIdIndex = response.IndexOf("\"activeLiveChatId\":\"");
                if (chatIdIndex != -1)
                {
                    chatIdIndex += 20;
                    int endIndex = response.IndexOf("\"", chatIdIndex);
                    liveChatId = response.Substring(chatIdIndex, endIndex - chatIdIndex);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"YouTube API Error: {e.Message}");
            }
        }

        public void Connect()
        {
            running = true;
            pollThread = new Thread(PollMessages);
            pollThread.Start();
        }

        private async void PollMessages()
        {
            while (running)
            {
                if (string.IsNullOrEmpty(liveChatId))
                {
                    Thread.Sleep(5000);
                    continue;
                }

                try
                {
                    string url = $"https://www.googleapis.com/youtube/v3/liveChat/messages?liveChatId={liveChatId}&part=snippet,authorDetails&key={apiKey}";
                    if (!string.IsNullOrEmpty(nextPageToken))
                    {
                        url += $"&pageToken={nextPageToken}";
                    }

                    string response = await httpClient.GetStringAsync(url);
                    ParseMessages(response);

                    int tokenIndex = response.IndexOf("\"nextPageToken\":\"");
                    if (tokenIndex != -1)
                    {
                        tokenIndex += 17;
                        int endIndex = response.IndexOf("\"", tokenIndex);
                        nextPageToken = response.Substring(tokenIndex, endIndex - tokenIndex);
                    }

                    int intervalIndex = response.IndexOf("\"pollingIntervalMillis\":");
                    if (intervalIndex != -1)
                    {
                        intervalIndex += 24;
                        int endIndex = response.IndexOf(",", intervalIndex);
                        if (endIndex == -1) endIndex = response.IndexOf("}", intervalIndex);
                        string intervalStr = response.Substring(intervalIndex, endIndex - intervalIndex);
                        if (int.TryParse(intervalStr, out int interval))
                        {
                            Thread.Sleep(interval);
                            continue;
                        }
                    }
                }
                catch { }

                Thread.Sleep(2000);
            }
        }

        private void ParseMessages(string json)
        {
            int itemsIndex = json.IndexOf("\"items\":[");
            if (itemsIndex == -1) return;

            int currentIndex = itemsIndex + 9;
            while (true)
            {
                int messageIdStart = json.IndexOf("\"id\":\"", currentIndex);
                if (messageIdStart == -1 || messageIdStart > json.IndexOf("]", itemsIndex)) break;
                messageIdStart += 6;
                int messageIdEnd = json.IndexOf("\"", messageIdStart);
                string messageId = json.Substring(messageIdStart, messageIdEnd - messageIdStart);

                if (processedMessages.Contains(messageId))
                {
                    currentIndex = messageIdEnd;
                    continue;
                }
                processedMessages.Add(messageId);

                int messageStart = json.IndexOf("\"displayMessage\":\"", messageIdEnd);
                if (messageStart == -1) break;
                messageStart += 18;
                int messageEnd = json.IndexOf("\"", messageStart);
                string message = json.Substring(messageStart, messageEnd - messageStart);

                int usernameStart = json.IndexOf("\"displayName\":\"", messageEnd);
                if (usernameStart == -1) break;
                usernameStart += 15;
                int usernameEnd = json.IndexOf("\"", usernameStart);
                string username = json.Substring(usernameStart, usernameEnd - usernameStart);

                bool isMod = json.IndexOf("\"isChatModerator\":true", messageIdEnd) != -1 &&
                             json.IndexOf("\"isChatModerator\":true", messageIdEnd) < json.IndexOf("},{", messageIdEnd);
                bool isBroadcaster = json.IndexOf("\"isChatOwner\":true", messageIdEnd) != -1 &&
                                   json.IndexOf("\"isChatOwner\":true", messageIdEnd) < json.IndexOf("},{", messageIdEnd);

                onMessage?.Invoke(new Plugin.ChatMessage
                {
                    Platform = "YouTube",
                    Username = username,
                    Message = message,
                    IsModerator = isMod,
                    IsBroadcaster = isBroadcaster
                });

                currentIndex = messageEnd;
            }
        }

        public void ProcessMessages() { }

        public void Disconnect()
        {
            running = false;
            pollThread?.Join(2000);
            httpClient?.Dispose();
        }
    }
}