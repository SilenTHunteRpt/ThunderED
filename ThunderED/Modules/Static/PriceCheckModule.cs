﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Newtonsoft.Json;
using ThunderED.Classes;
using ThunderED.Helpers;
using ThunderED.Json;
using ThunderED.Json.EveCentral;

namespace ThunderED.Modules.Static
{
    internal class PriceCheckModule: AppModuleBase
    {
        public override LogCat Category => LogCat.PriceCheck;
        public override Task Run(object prm)
        {
            return Task.CompletedTask;
        }

        public static async Task Check(ICommandContext context, string command, string system)
        {
            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.Add("User-Agent", SettingsManager.DefaultUserAgent);

                    HttpResponseMessage itemID;
                    if (command.ToLower().StartsWith("search"))
                        itemID = await httpClient.GetAsync($"{SettingsManager.Settings.Config.ESIAddress}latest/search/?categories=inventory_type&datasource=tranquility&language=en-us&search=" +
                                                           $"{command.TrimStart(new char[] {'s', 'e', 'a', 'r', 'c', 'h'})}&strict=false");
                    else
                        itemID = await httpClient.GetAsync($"{SettingsManager.Settings.Config.ESIAddress}latest/search/?categories=inventory_type&datasource=tranquility&language=en-us&search=" +
                                                           $"{command.ToLower()}&strict=true");

                    if (!itemID.IsSuccessStatusCode)
                    {
                        await APIHelper.DiscordAPI.ReplyMessageAsync(context, LM.Get("ESIFailure"));
                        await Task.CompletedTask;
                        itemID?.Dispose();
                        return;
                    }

                    var itemIDResult = await itemID.Content.ReadAsStringAsync();
                    var itemIDResults = JsonConvert.DeserializeObject<JsonClasses.SearchInventoryType>(itemIDResult);
                    itemID?.Dispose();

                    if (string.IsNullOrWhiteSpace(itemIDResults.inventory_type?.ToString()))
                        await APIHelper.DiscordAPI.ReplyMessageAsync(context, LM.Get("itemNotExist",command));
                    else if (itemIDResults.inventory_type.Count() > 1)
                    {
                        await APIHelper.DiscordAPI.ReplyMessageAsync(context, LM.Get("seeDM"));

                        var channel = await context.Message.Author.GetOrCreateDMChannelAsync();

                        var tmp = JsonConvert.SerializeObject(itemIDResults.inventory_type);
                        var httpContent = new StringContent(tmp);

                        var itemName = await httpClient.PostAsync($"{SettingsManager.Settings.Config.ESIAddress}latest/universe/names/?datasource=tranquility", httpContent);

                        if (!itemName.IsSuccessStatusCode)
                        {
                            await APIHelper.DiscordAPI.ReplyMessageAsync(context, channel, LM.Get("ESIFailure")).ConfigureAwait(false);
                            await Task.CompletedTask;
                            itemName?.Dispose();
                            return;
                        }

                        var itemNameResult = await itemName.Content.ReadAsStringAsync();
                        var itemNameResults = JsonConvert.DeserializeObject<List<JsonClasses.SearchName>>(itemNameResult);
                        itemName?.Dispose();

                        await LogHelper.LogInfo($"Sending {context.Message.Author}'s Price check to {channel.Name}", LogCat.PriceCheck);
                        var builder = new EmbedBuilder()
                            .WithColor(new Color(0x00D000))
                            .WithAuthor(author =>
                            {
                                author
                                    .WithName(LM.Get("manyItemsFound"));
                            })
                            .WithDescription(LM.Get("searchExample"));
                        var count = 0;
                        foreach (var inventoryType in itemIDResults.inventory_type)
                        {
                            if (count < 25)
                            {
                                builder.AddField($"{itemNameResults.FirstOrDefault(x => x.id == inventoryType).name}", "\u200b");
                            }
                            else
                            {
                                var embed2 = builder.Build();

                                await APIHelper.DiscordAPI.SendMessageAsync(channel, "", embed2).ConfigureAwait(false);

                                builder.Fields.Clear();
                                count = 0;
                            }

                            count++;
                        }

                        var embed = builder.Build();
                        await APIHelper.DiscordAPI.SendMessageAsync(channel, "", embed).ConfigureAwait(false);
                    }
                    else
                    {
                        try
                        {
                            var httpContent = new StringContent($"[{itemIDResults.inventory_type[0]}]", Encoding.UTF8, "application/json");
                            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                            var itemName = await httpClient.PostAsync($"{SettingsManager.Settings.Config.ESIAddress}latest/universe/names/?datasource=tranquility", httpContent);

                            if (!itemName.IsSuccessStatusCode)
                            {
                                await APIHelper.DiscordAPI.ReplyMessageAsync(context, LM.Get("ESIFailure"));
                                await Task.CompletedTask;
                                itemName?.Dispose();
                                return;
                            }

                            var itemNameResult = await itemName.Content.ReadAsStringAsync();
                            var itemNameResults = JsonConvert.DeserializeObject<List<JsonClasses.SearchName>>(itemNameResult)[0];
                            itemName?.Dispose();

                            await GoFuzz(httpClient, context, system, itemIDResults, itemNameResults);

                        }
                        catch (Exception ex)
                        {
                            await LogHelper.LogEx(ex.Message, ex, LogCat.PriceCheck);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await APIHelper.DiscordAPI.ReplyMessageAsync(context, "ERROR Please inform Discord/Bot Owner");
                await LogHelper.LogEx(ex.Message, ex, LogCat.PriceCheck);
            }
        }

        private static async Task GoFuzz(HttpClient httpClient, ICommandContext context, string system,
            JsonClasses.SearchInventoryType itemIDResults, JsonClasses.SearchName itemNameResults)
        {
            var url = "https://market.fuzzwork.co.uk/aggregates/";

            var systemAddon = string.Empty;
            var systemTextAddon = string.IsNullOrEmpty(system) ? null : $"{LM.Get("fromSmall")} {system}";
            switch (system?.ToLower())
            {
                default:
                    systemAddon = "?station=60003760";
                    break;
                case "amarr":
                    systemAddon = "?station=60008494";
                    break;
                case "rens":
                    systemAddon = "?station=60004588";
                    break;
                case "dodixie":
                    systemAddon = "?station=60011866";
                    break;
            }

            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("User-Agent", SettingsManager.DefaultUserAgent);
            var webReply = await httpClient.GetStringAsync($"{url}{systemAddon}&types={itemIDResults.inventory_type[0]}");
            var market = JsonConvert.DeserializeObject<Dictionary<string,JsonEveCentral.FuzzItems>>(webReply);

            await LogHelper.LogInfo($"Sending {context.Message.Author}'s Price check", LogCat.PriceCheck);
            foreach (var marketReply in market.Values)
            {
                var builder = new EmbedBuilder()
                    .WithColor(new Color(0x00D000))
                    .WithThumbnailUrl($"https://image.eveonline.com/Type/{itemNameResults.id}_64.png")
                    .WithAuthor(author =>
                    {
                        author
                            .WithName($"{LM.Get("Item")}: {itemNameResults.name}")
                            .WithUrl($"https://www.fuzzwork.co.uk/info/?typeid={itemNameResults.id}/");
                    })
                    .WithDescription($"{LM.Get("Prices")} {systemTextAddon}")
                    .AddField(LM.Get("Buy"), $"{LM.Get("marketHigh")}: {marketReply.buy.max:N2}{Environment.NewLine}" +
                                             $"{LM.Get("marketMid")}: {marketReply.buy.weightedAverage:N2}{Environment.NewLine}" +
                                             $"{LM.Get("marketLow")}: {marketReply.buy.min:N2}{Environment.NewLine}" +
                                             $"{LM.Get("Volume")}: {marketReply.buy.volume}", true)
                    .AddField(LM.Get("Sell"), $"{LM.Get("marketLow")}: {marketReply.sell.min:N2}{Environment.NewLine}" +
                                              $"{LM.Get("marketMid")}: {marketReply.sell.weightedAverage:N2}{Environment.NewLine}" +
                                              $"{LM.Get("marketHigh")}: {marketReply.sell.max:N2}{Environment.NewLine}" +
                                              $"{LM.Get("Volume")}: {marketReply.sell.volume:N0}", true);

                var embed = builder.Build();
                await APIHelper.DiscordAPI.ReplyMessageAsync(context, "", embed).ConfigureAwait(false);
                await Task.Delay(500);
            }
        }

        private static async Task GoMarketeer(HttpClient httpClient, ICommandContext context, string system, JsonClasses.SearchInventoryType itemIDResults, JsonClasses.SearchName itemNameResults)
        {
            var url = "https://api.evemarketer.com/ec";

            var systemAddon = string.Empty;
            var systemTextAddon = string.IsNullOrEmpty(system) ? null : $"{LM.Get("fromSmall")} {system}";

            switch (system?.ToLower())
            {
                case "jita":
                    systemAddon = "&usesystem=30000142";
                    break;
                case "amarr":
                    systemAddon = "&usesystem=30002187";
                    break;
                case "rens":
                    systemAddon = "&usesystem=30002510";
                    break;
                case "dodixie":
                    systemAddon = "&usesystem=30002659";
                    break;
            }

            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("User-Agent", SettingsManager.DefaultUserAgent);
            var webReply = await httpClient.GetStringAsync($"{url}/marketstat/json?typeid={itemIDResults.inventory_type[0]}{systemAddon}");
            var marketReply = JsonConvert.DeserializeObject<List<JsonEveCentral.Items>>(webReply)[0];

            await LogHelper.LogInfo($"Sending {context.Message.Author}'s Price check", LogCat.PriceCheck);
            var builder = new EmbedBuilder()
                .WithColor(new Color(0x00D000))
                .WithThumbnailUrl($"https://image.eveonline.com/Type/{itemNameResults.id}_64.png")
                .WithAuthor(author =>
                {
                    author
                        .WithName($"{LM.Get("Item")}: {itemNameResults.name}")
                        .WithUrl($"https://www.fuzzwork.co.uk/info/?typeid={itemNameResults.id}/");
                })
                .WithDescription($"{LM.Get("Prices")} {systemTextAddon}")
                .AddField(LM.Get("Buy"), $"{LM.Get("marketLow")}: {marketReply.buy.min:N2}{Environment.NewLine}" +
                                            $"{LM.Get("marketMid")}: {marketReply.buy.avg:N2}{Environment.NewLine}" +
                                            $"{LM.Get("marketHigh")}: {marketReply.buy.max:N2}", true)
                .AddField(LM.Get("Sell"), $"{LM.Get("marketLow")}: {marketReply.sell.min:N2}{Environment.NewLine}" +
                                            $"{LM.Get("marketMid")}: {marketReply.sell.avg:N2}{Environment.NewLine}" +
                                            $"{LM.Get("marketHigh")}: {marketReply.sell.max:N2}", true)
                .AddField(LM.Get("Extra"), "\u200b")
                .AddField(LM.Get("Buy"), $"5%: {marketReply.buy.fivePercent:N2}{Environment.NewLine}" +
                                            $"{LM.Get("Volume")}: {marketReply.buy.volume}", true)
                .AddField(LM.Get("Sell"), $"5%: {marketReply.sell.fivePercent:N2}{Environment.NewLine}" +
                                            $"{LM.Get("Volume")}: {marketReply.sell.volume:N0}", true);
            var embed = builder.Build();
            await APIHelper.DiscordAPI.ReplyMessageAsync(context, "", embed).ConfigureAwait(false);
        }

    }
}
