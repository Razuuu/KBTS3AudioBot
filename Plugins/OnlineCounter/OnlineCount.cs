using System;
using System.Threading.Tasks;
using TS3AudioBot;
using TS3AudioBot.Plugins;
using TSLib.Full.Book;
using TSLib;
using TSLib.Full;
using TSLib.Messages;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace OnlineCounter
{
	public class OnlineCount : IBotPlugin
	{
		private TsFullClient tsFullClient;
		private Ts3Client ts3Client;
		private Connection serverView;

		private static readonly HttpClient httpClient = new HttpClient();

		private readonly ulong channelToUpdateId = 171; // replace with the ID of the channel to update
		private readonly TimeSpan resetInterval = TimeSpan.FromDays(1);
		//private readonly TimeSpan resetInterval = TimeSpan.FromMinutes(1);
		private readonly List<uint> excludedGroups = new List<uint> { 11, 47, 115 }; // replace with the IDs of the excluded groups
		public static string filePath = "badusernames.txt";
		private static readonly string logFilePath = "geolocation_log.txt"; // Path to your log file

		private List<string> userIDS = new List<string>();
		private List<string> userNames = new List<string>();

		private readonly object countLock = new object();
		private uint count = 0;
		private uint countToday = 0;
		private DateTime lastResetTime = DateTime.MinValue;
		private bool isChecking = false;
		//private string jsonFilePath = "data.json";

		public OnlineCount(Ts3Client ts3Client, Connection serverView, TsFullClient tsFull)
		{
			this.ts3Client = ts3Client;
			this.tsFullClient = tsFull;
			this.serverView = serverView;
		}

		public void Initialize()
		{
			tsFullClient.OnClientEnterView += OnUserConnected;
			tsFullClient.OnClientLeftView += OnUserDisconnected;
			tsFullClient.OnClientUpdated += onUserChangedNickname;
			ResetCountPeriodically();
			lastResetTime = DateTime.UtcNow;
			//CheckOnlineUsers(true);
			CheckOnlineUsersNeu(true);

		}

		private void onUserChangedNickname(object sender, IEnumerable<ClientUpdated> e)
		{
			CheckOnlineUsersNeu(true);
		}

		private void OnUserConnected(object sender, IEnumerable<ClientEnterView> clients)
		{
			CheckOnlineUsersNeu(true);
		}

		private void OnUserDisconnected(object sender, IEnumerable<ClientLeftView> clients)
		{
			CheckOnlineUsersNeu(false);
		}

		private async void CheckOnlineUsersNeu(bool connected)
		{
			if (isChecking) { return; }
			isChecking = true;
			//uint oldCount = count;

			await Task.Delay(500); // Add a 500ms delay before starting the method

			count = 0;
			//int testcount = 0;
			//bool skipCurrentClient = false;
			foreach (var oneuser in serverView.Clients)
			{
				if (CheckBadUsernames(oneuser.Value.Name))
				{
				    var cci = await tsFullClient.GetClientConnectionInfo(oneuser.Value.Id);
					ulong insta = 1;
					await tsFullClient.KickClientFromServer(oneuser.Value.Id, "No DDoS, No Trolls, No Nazis and No Kevin you immature little prick, you neither. Please go back to Discord and stay there!");
					await tsFullClient.SendServerMessage("User IP: "+cci.Value.Ip, insta);
					string geolocation = await GetGeolocationAsync(cci.Value.Ip);
					await tsFullClient.SendServerMessage("Location: " + geolocation, insta);
					await tsFullClient.SendServerMessage("Possible DDoS, report the IP!", insta);
					//Console.WriteLine("Bad Username: " + oneuser.Value.Name);
				}
				// Check if is full user
				if (oneuser.Value.ClientType == ClientType.Full)
				{
					//Console.WriteLine("ID: " + oneuser.Value.Id.Value + " | Type: " + oneuser.Value.ClientType);

					//Check if user is in excludet group
					bool skipCurrentClient = false;
					foreach (var sg in excludedGroups)
					{
						ServerGroupId newSG = (ServerGroupId)sg;
						if (oneuser.Value.ServerGroups.Contains(newSG))
						{
							//Console.WriteLine("Skipping Bot");
							skipCurrentClient = true;
							break;
						}
					}

					// Skip processing this user and move to the next iteration
					if (skipCurrentClient)
						continue;
					// User is Fulluser and is not a Bot go on
					bool containsUserID = userIDS.Any(item => item == oneuser.Value.Uid.Value.ToString());
					count++;
					if (connected && !containsUserID)
					{
						//testcount++;
						countToday++;
						userNames.Add(oneuser.Value.Name);
						userIDS.Add(oneuser.Value.Uid.Value.ToString());
						//Console.WriteLine("User Added: " + oneuser.Value.Name);
					}

				}
			}
			UpdateChannelName();
			//Console.WriteLine("Currently "+ count + " users online of "+ countToday + " today");
			isChecking = false;
		}


		public static async Task<string> GetGeolocationAsync(string ipAddress)
		{
			try
			{
				string apiUrl = $"http://ip-api.com/json/{ipAddress}";

				// Send a GET request to the API
				HttpResponseMessage response = await httpClient.GetAsync(apiUrl);
				response.EnsureSuccessStatusCode();

				// Read the response content as a string
				string content = await response.Content.ReadAsStringAsync();

				// Parse the response JSON
				JObject json = JObject.Parse(content);

				// Extract relevant fields (country, region, city, etc.)
				string country = json["country"]?.ToString();
				string region = json["regionName"]?.ToString();
				string city = json["city"]?.ToString();
				string isp = json["isp"]?.ToString();

				// Create a formatted string with the geolocation details
				string logEntry = $"[{DateTime.Now}] IP: {ipAddress}, Country: {country}, Region: {region}, City: {city}, ISP: {isp}";

				// Log the entry to a file
				LogToFile(logEntry);

				// Return a formatted string with the geolocation details
				return logEntry;
			}
			catch (Exception ex)
			{
				string errorLog = $"[{DateTime.Now}] Error fetching geolocation for IP {ipAddress}: {ex.Message}";
				LogToFile(errorLog);
				return errorLog;
			}
		}

		// Method to log the geolocation data into a file
		private static void LogToFile(string logEntry)
		{
			try
			{
				// Append the log entry to the file
				System.IO.File.AppendAllText(logFilePath, logEntry + Environment.NewLine);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error writing to log file: {ex.Message}");
			}
		}


		private async void CheckOnlineUsers(bool connected)
		{
			if (isChecking) { return; }
			isChecking = true;
			//Console.WriteLine("Checking online users");
			bool doUpdate = false;
			uint oldCount = count;
			count = 0;
			var allUsers = await tsFullClient.ClientList();

			if (allUsers)
			{
				Console.WriteLine("List is not null!");
			}
			else
			{
				Console.WriteLine("List is null!");
				return;
			}


			try
			{
				foreach (var user in allUsers.Value)
				{

					//var ServerGroupIDs = await tsFullClient.ServerGroupsByClientDbId(user.DatabaseId);
					var fulluser = await tsFullClient.ClientInfo(user.ClientId);

					if (fulluser)
					{
						Console.WriteLine("Fulluser is not null!");
					}
					else
					{
						Console.WriteLine("Fulluser is null!");
						return;
					}
					//Valid Full CLient
					bool skipClient = false;
					bool containsUserID = userIDS.Any(item => item == fulluser.Value.Uid.ToString());

					if (fulluser.Value.ClientType.Equals(ClientType.Full))
					{
						foreach (var sg in excludedGroups)
						{
							ServerGroupId newSG = (ServerGroupId)sg;
							if (fulluser.Value.ServerGroups.Contains(newSG))
							{
								//Console.WriteLine(user.Name + " Online but is a Bot");
								skipClient = true;
								break;
							}
						}

						if (skipClient)
						{
							continue;
						}
						//Console.WriteLine(user.Name + " Online and Not a Bot UID:"+ fulluser.Value.Uid);
						count++;
						doUpdate = true;
						if (connected && !containsUserID)
						{
							countToday++;
							userNames.Add(fulluser.Value.Name);
							userIDS.Add(fulluser.Value.Uid.ToString());
							//AddTsUserToDB(fulluser.Value.Name);
						}
					}

				}
				if (doUpdate && oldCount != count && count <= countToday) { UpdateChannelName(); }
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.ToString());
			}
			isChecking = false;
		}

		private async Task ResetCountPeriodically()
		{
			while (true)
			{
				await Task.Delay(resetInterval);
				Console.WriteLine("Resetting Online Counter!");
				ResetCount();
			}
		}

		private void ResetCount()
		{
			lock (countLock)
			{
				count = 0;
				countToday = 0;
				lastResetTime = DateTime.UtcNow;
				userNames.Clear();
				userIDS.Clear();
				//TSuserDB.DeleteAllData(jsonFilePath);
				//CheckOnlineUsers(true);
				CheckOnlineUsersNeu(true);
				ts3Client.SendServerMessage("[b][color=red]Online Counter Reset![/color][/b]");
			}
			//tsFullClient.SendGlobalMessage("[b][color=red]Online Counter wurde zurückgesetzt![/color][/b]");

		}

		private string GetChannelName()
		{
			lock (countLock)
			{
				return $"[cspacer73]Today {count} of {countToday} online";
				//return $"[cspacer73] {count} users online today";
			}
		}

		private async void UpdateChannelName()
		{
			string usernameList = "";
			try
			{
				if (userNames.Count <= 0)
				{
					// No usernames
					usernameList = "No user Online";
				}
				else
				{
					foreach (var user in userNames)
					{
						usernameList = usernameList + "- " + user + "\n";
					}
				}
				string newChanDis = $"Last Reset: {lastResetTime}\n\n[b]Userlist:[/b]\n{usernameList}";
				string newChanTop = $"Last Reset: {lastResetTime}";
				ChannelId channelId = new ChannelId(channelToUpdateId);

				//await tsFullClient.ChannelEdit(currentChannel, codec: defaultCodec, codecQuality: defaultCodecQuality);
				//Console.WriteLine("Channel name: " + GetChannelName());
				await tsFullClient.ChannelEdit(channelId, name: GetChannelName(), description: newChanDis, topic: newChanTop);
				//$"UPDATE channels SET channel_name='{GetChannelName()}' WHERE channel_id={channelToUpdateId}";
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to update channel name: {ex.Message}");
			}
		}

		//public static bool CheckBadUsernames(string inputString, bool exact = true)
		//{
		//	// Check if the file exists
		//	if (!System.IO.File.Exists(filePath))
		//	{
		//		throw new FileNotFoundException($"File not found: {filePath}");
		//	}

		//	// Read all lines from the file
		//	string[] lines = System.IO.File.ReadAllLines(filePath);

		//	if (exact)
		//	{
		//		// Here we check for an exact match
		//		return lines.Any(line => inputString == line);
		//	}

		//	// Check if any line is contained within the username
		//	return lines.Any(line => inputString.Contains(line));
		//}

		public static bool CheckBadUsernames(string inputString, bool ignoreCase = true)
		{
			// Check if the file exists
			if (!System.IO.File.Exists(filePath))
			{
				throw new FileNotFoundException($"File not found: {filePath}");
			}

			// Read all lines from the file
			string[] lines = System.IO.File.ReadAllLines(filePath);

			// If we are ignoring case, convert both inputString and lines to lower case
			if (ignoreCase)
			{
				inputString = inputString.ToLower();
				lines = lines.Select(line => line.ToLower()).ToArray();
			}

			// Loop through each line in the file
			foreach (var line in lines)
			{
				if (line.Contains("*"))
				{
					// Treat the '*' as a wildcard, removing it for partial match
					string pattern = line.Replace("*", "");

					// If inputString contains the pattern, it's a bad username
					if (inputString.Contains(pattern))
					{
						return true;
					}
				}
				else
				{
					// Exact match case
					if (inputString == line)
					{
						return true;
					}
				}
			}

			// If no matches found
			return false;
		}


		public void Dispose()
		{
			tsFullClient.OnClientEnterView -= OnUserConnected;
			tsFullClient.OnClientLeftView -= OnUserDisconnected;
			tsFullClient.OnClientUpdated -= onUserChangedNickname;
		}
	}

}
