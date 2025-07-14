using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Discord.Commands;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Runtime.InteropServices;
using System.IO;
using System.Runtime.InteropServices.ComTypes;
using System.Diagnostics.Eventing.Reader;
using System.Data;

namespace Dice
{
    class Program
    {
        private DiscordSocketClient _client;
        private CommandService _commands;
        private IServiceProvider _services;
        private readonly string token;

        public Program()
        {
            token = System.Configuration.ConfigurationManager.AppSettings["DiscordBotToken"];
        }

        static void Main(string[] args)
        {
            new Program().MainAsync().GetAwaiter().GetResult();
        }

        public async Task MainAsync()
        {
            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent
            };
            _client = new DiscordSocketClient(config);
            _commands = new CommandService();
            _client.MessageReceived += HandleCommandAsync;
            _client.Log += Log;
            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();
            await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
            await Task.Delay(-1);
        }

        private Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }
        private async Task HandleCommandAsync(SocketMessage arg)
        {
            var message = arg as SocketUserMessage;
            if (message == null || message.Author.IsBot) return;

            int argPos = 0;
            if (message.HasCharPrefix('!', ref argPos))
            {
                var context = new SocketCommandContext(_client, message);
                var result = await _commands.ExecuteAsync(context, argPos, _services);
                if (!result.IsSuccess)
                    await context.Channel.SendMessageAsync(result.ErrorReason);
            }
        }
    }

    public class DiceModule : ModuleBase<SocketCommandContext>
    {
        private const string patternForPower = "^(k|K)(100|[1-9]?[0-9])\\[(1[0-3]|[89])\\]((\\+|\\-)[0-9]+)?(\\$(\\+[0-9]|1[0-3]|[0-9]))?(\\#[0-9])?((r|R)[0-9]+)?$";
        private const string patternForDice = "^[0-9]*(d|D)[0-9]+$";
        
        public string Declutter(string input)
        {
            Regex targets = new Regex(@"([0-9\(][0-9\+\-\*/\.\(\)\s]+[0-9\)])");
            return targets.Replace(input, m =>
            {
                string candidate = m.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(candidate) && Regex.IsMatch(candidate, @"\d"))
                {
                    try
                    {
                        DataTable dt = new DataTable();
                        var result = dt.Compute(candidate, "");
                        if (result is double d)
                        {
                            int intResult = (int)Math.Floor(d);
                            return intResult.ToString();
                        }
                        return result.ToString();
                    }
                    catch
                    {
                        return candidate;
                    }
                }
                return candidate;
            });
        }

        [Command("dice")]
        public async Task DiceAsync(int sides = 6)
        {
            if (sides < 1)
            {
                await ReplyAsync("正の整数を指定してください。");
                return;
            }

            Random random = new Random();
            int result = random.Next(1, sides + 1);

            await ReplyAsync($"{Context.User.Username}さんが振った{sides}面体のダイスの結果は: {result}です！");
        }

        [Command("power")]
        public async Task PowerAsync(string input = null)
        {
            if (input == null)
            {
                await ReplyAsync("計算する対象を指定してください。");
                return;
            }

            string power = Declutter(input);

            Match match = Regex.Match(power, patternForPower);

            if (!match.Success)
            {
                await ReplyAsync($"{power}は、形式が間違っています。");
                return;
            }

            string result = $"{power} > 2D:[";
            var separated = SeparateOfPower(power);
            var p = separated[0];
            var c = separated[1];
            var extra = separated[2];
            var specialAttack = separated[3];
            var criticalRay = separated[4];
            var faceFixed = separated[5];
            var kubikiri = separated[6];
            var tableOfPower = GetsTableOfPower();
            List<string> damages = new List<string>();
            List<string> sumFaces = new List<string>();
            Random random = new Random();
            int criticalNumber = 0;
            int totaldamage = 0;
            while (true)
            {
                int right = random.Next(1, 7);
                int left = random.Next(1, 7);
                result += $"{right},{left}";
                int sum = right + left;
                if (criticalNumber == 0)
                {
                    if (faceFixed > 0) sum = faceFixed;
                    else sum += criticalRay;
                }
                sum += specialAttack;
                sum = Math.Min(sum, 12);
                sumFaces.Add(sum.ToString());
                if (right == 1 && left == 1)
                {
                    damages.Add("\\*\\*");
                    break;
                }
                string thisDamage = tableOfPower[p + 1][sum - 1].ToString();
                damages.Add(thisDamage);
                totaldamage += int.Parse(thisDamage);
                if (sum < c) break;
                if (kubikiri > 0 && p != 100) p = Math.Min(p + kubikiri, 100);
                criticalNumber++;
                result += " ";
            }
            string total = (totaldamage + extra).ToString();
            if (totaldamage == 0 && damages.Count == 1 && damages[0] == "\\*\\*") total = "\\*\\*";
            result += $"]={string.Join(",", sumFaces)} > ";
            if (total != "\\*\\*") result += $"{string.Join(",", damages)}+{extra} > ";
            if (criticalNumber > 0) result += $"{criticalNumber}回転 > ";
            result += $"{total}";
            if (total == "\\*\\*") result += " > 自動的失敗";
            await ReplyAsync(result);
        }


        [Command("expect")]
        public async Task EVAsync(string input = null)
        {
            if (input == null)
            {
                await ReplyAsync("計算する対象を指定してください。");
                return;
            }

            string str = Declutter(input);

            Match matchPower = Regex.Match(str, patternForPower);
            Match matchDice = Regex.Match(str, patternForDice);

            if(!(matchPower.Success || matchDice.Success))
            {
                await ReplyAsync($"{str}は、登録されていない形式です。\n以下のパターンのみ有効です。\n{patternForPower}\n{patternForDice}");
                return;
            }

            double expectedValue = 0;

            if (matchPower.Success)
            {
                expectedValue = CalculateExpectedValueOfPower(str);
            }
            else if (matchDice.Success)
            {
                expectedValue = CalculateExpectedValueOfDice(str);
            }
            await ReplyAsync($"{str}の期待値は、{expectedValue:F3}です。");
        }

        public List<int> SeparateOfDice(string str)
        {
            return str.ToLower().Split('d').Select(int.Parse).ToList();
        }

        public double CalculateExpectedValueOfDice(string str)
        {
            var num = SeparateOfDice(str);
            return num[0] * (1 + num[1]) / (double)2;
        }

        public List<int> SeparateOfPower(string str)
        {
            string[] damage = new string[1] { str.ToLower() };
            int kubikiri = 0;
            if (damage[0].Contains('r'))
            {
                damage = damage[0].Split('r');
                kubikiri = int.Parse(damage[1]);
            }
            int probabilityLevel = 0;
            if (damage[0].Contains('#'))
            {
                damage = damage[0].Split('#');
                probabilityLevel = int.Parse(damage[1]);
            }
            int criticalRay = 0;
            int faceFixed = -1;
            if (damage[0].Contains("$+"))
            {
                damage = damage[0].Split('$');
                criticalRay = int.Parse(damage[1].Substring(1));
            }
            else if (damage[0].Contains('$'))
            {
                damage = damage[0].Split('$');
                faceFixed = int.Parse(damage[1]);
            }
            damage = damage[0].Split('[', ']');
            int power = int.Parse(damage[0].Substring(1));
            int critical = int.Parse(damage[1]);
            int extraDamage = int.Parse(damage[2].Substring(1));
            return new List<int> { power, critical, extraDamage, probabilityLevel, criticalRay, faceFixed, kubikiri };
        }

        public List<List<int>> GetsTableOfPower()
        {
            List<List<int>> tableOfPower;
            var assembly = Assembly.GetExecutingAssembly();
            using (Stream streamForPower = assembly.GetManifestResourceStream("Dice.PowerTable.xml"))
            using (StreamReader readerForPower = new StreamReader(streamForPower))
            {
                string xmlContentForPower = readerForPower.ReadToEnd();
                XDocument docForPower = XDocument.Parse(xmlContentForPower);
                tableOfPower = docForPower.Descendants("Row").Select(row => row.Elements("Data").Select(data => int.Parse(data.Value)).ToList()).ToList();
            }
            return tableOfPower;
        }

        public List<List<double>> GetsTableOfProbability()
        {
            List<List<double>> tableOfProbability;
            var assembly = Assembly.GetExecutingAssembly();
            using (Stream streamForCritical = assembly.GetManifestResourceStream("Dice.ProbabilityTable.xml"))
            using (StreamReader readerForCritical = new StreamReader(streamForCritical))
            {
                string xmlContentForCritical = readerForCritical.ReadToEnd();
                XDocument docForCritical = XDocument.Parse(xmlContentForCritical);
                tableOfProbability = docForCritical.Descendants("Row").Select(row => row.Elements("Data").Select(data => {
                    var v = data.Value.Split('/').Select(double.Parse).ToList();
                    double probability = v[0] / v[1];
                    return probability;
                }).ToList()).ToList();
            }
            return tableOfProbability;
        }

        public double CalculateExpectedValueOfPower(string str)
        {
            var separated = SeparateOfPower(str);
            var power = separated[0];
            var critical = separated[1];
            var extraDamage = separated[2];
            var probabilityLevel = separated[3];
            var criticalRay = separated[4];
            var faceFixed = separated[5];
            var kubikiri = separated[6];
            double expectedValue = 0;
            var tableOfPower = GetsTableOfPower();
            var tableOfProbability = GetsTableOfProbability();
            double sumNonCritical = 1;
            double sumCriticalWithCriRay = 0;
            double expectedValueWithCriRay = 0;
            for(int i = 2; i < 13; i++)
            {
                if (i + probabilityLevel >= critical && i != 2) sumNonCritical -= tableOfProbability[1][i - 2];
                if (i + probabilityLevel + criticalRay >= critical && i != 2) sumCriticalWithCriRay += tableOfProbability[1][i - 2];
                expectedValue += tableOfPower[power + 1][i - 1] * tableOfProbability[probabilityLevel + 1][i - 2];
                expectedValueWithCriRay += tableOfPower[power + 1][i - 1] * tableOfProbability[Math.Min(9, probabilityLevel + criticalRay) + 1][i - 2];
            }
            expectedValue /= sumNonCritical;
            if (kubikiri != 0 && power != 100 && criticalRay == 0 && faceFixed < 0) expectedValue += (1 - sumNonCritical) * Kubikiri(power, kubikiri, probabilityLevel, sumNonCritical, tableOfPower, tableOfProbability);
            else if (kubikiri != 0 && power != 100) expectedValue = Kubikiri(power, kubikiri, probabilityLevel, sumNonCritical, tableOfPower, tableOfProbability);
            if (criticalRay > 0) expectedValue = expectedValueWithCriRay + expectedValue * sumCriticalWithCriRay;
            if (faceFixed >= 2 && faceFixed >= critical) expectedValue += tableOfPower[power + 1][faceFixed - 1];
            if (faceFixed >= 2 && faceFixed < critical) expectedValue = tableOfPower[power + 1][faceFixed - 1];
            expectedValue += (double)(extraDamage * 35) / 36;
            return expectedValue;
        }

        public double Kubikiri(int start, int r, int probabilityLevel, double sumNonCritical, List<List<int>> tableOfPower, List<List<double>> tableOfProbability)
        {
            double expectedValue = 0;
            int now = start;
            int count = 0;
            while(true)
            {
                if (now >= 100) break;
                now = Math.Min(now + r, 100);
                for (int i = 2; i < 13; i++)
                {
                    if (now == 100) expectedValue += Math.Pow(1 - sumNonCritical, count) * tableOfPower[now + 1][i - 1] * tableOfProbability[probabilityLevel + 1][i - 2] / sumNonCritical;
                    else expectedValue += Math.Pow(1 - sumNonCritical, count) * tableOfPower[now + 1][i - 1] * tableOfProbability[probabilityLevel + 1][i - 2];
                }
                count++;
            }
            return expectedValue;
        }
    }
}