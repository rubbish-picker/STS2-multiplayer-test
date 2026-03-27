namespace AiEvent;

public static class AiEventFallbacks
{
    public static AiGeneratedEventPayload Create(AiEventSlot slot)
    {
        string eventKey = AiEventRegistry.GetEventKey(slot);

        return new AiGeneratedEventPayload
        {
            Slot = slot,
            EventKey = eventKey,
            Options =
            [
                new AiEventOptionPayload
                {
                    Key = "OPTION_A",
                    Effects =
                    [
                        new AiEventEffectPayload { Type = "gain_gold", Amount = 45 },
                        new AiEventEffectPayload { Type = "heal", Amount = 8 },
                    ],
                },
                new AiEventOptionPayload
                {
                    Key = "OPTION_B",
                    Effects =
                    [
                        new AiEventEffectPayload { Type = "upgrade_cards", Count = 1 },
                    ],
                },
                new AiEventOptionPayload
                {
                    Key = "OPTION_C",
                    Effects =
                    [
                        new AiEventEffectPayload { Type = "damage_self", Amount = 5 },
                        new AiEventEffectPayload { Type = "obtain_random_relic", Count = 1 },
                    ],
                },
            ],
            Eng = new AiLocalizedEventText
            {
                Title = $"{GetEnglishAreaName(slot)} Draft",
                InitialDescription =
                    $"A half-finished page waits in the {GetEnglishAreaName(slot).ToLowerInvariant()} shadows, as though the Spire expects you to complete it.\n\n" +
                    "The prose already knows its cadence. Only the price is still unwritten.",
                Options =
                [
                    new AiLocalizedOptionText
                    {
                        Key = "OPTION_A",
                        Title = "Accept the Draft",
                        Description = "Gain [blue]45[/blue] [gold]Gold[/gold]. [green]Heal 8[/green] HP.",
                        ResultDescription =
                            "You let the page settle as written. Coins slide free from its margins, and a gentle warmth steadies your breathing.",
                    },
                    new AiLocalizedOptionText
                    {
                        Key = "OPTION_B",
                        Title = "Revise It",
                        Description = "[gold]Upgrade[/gold] a card in your [gold]Deck[/gold].",
                        ResultDescription =
                            "You scratch out the weak lines and keep only the sharp ones. One card in your deck returns improved.",
                    },
                    new AiLocalizedOptionText
                    {
                        Key = "OPTION_C",
                        Title = "Bleed for an Ending",
                        Description = "Lose [red]5[/red] HP. Obtain a random [gold]Relic[/gold].",
                        ResultDescription =
                            "A few drops of blood finish the final sentence. The page folds around a relic and offers it to you.",
                    },
                ],
            },
            Zhs = new AiLocalizedEventText
            {
                Title = $"{GetChineseAreaName(slot)}草稿",
                InitialDescription =
                    $"{GetChineseAreaName(slot)}的阴影里放着一页尚未写完的稿纸，像是尖塔故意把它留给后来者。\n\n" +
                    "叙述已经成形，只剩下回报与代价还悬而未决。",
                Options =
                [
                    new AiLocalizedOptionText
                    {
                        Key = "OPTION_A",
                        Title = "照单收下",
                        Description = "获得[blue]45[/blue][gold]金币[/gold]。回复[green]8[/green]点生命。",
                        ResultDescription =
                            "你没有改动这份草稿。纸页边缘抖落出金币，一股温热也随之回到你体内。",
                    },
                    new AiLocalizedOptionText
                    {
                        Key = "OPTION_B",
                        Title = "重写一段",
                        Description = "[gold]升级[/gold]你[gold]牌组[/gold]中的一张卡牌。",
                        ResultDescription =
                            "你删去疲软的句子，只留下最锋利的部分。牌组中也有一张卡牌因此变强。",
                    },
                    new AiLocalizedOptionText
                    {
                        Key = "OPTION_C",
                        Title = "以血落款",
                        Description = "失去[red]5[/red]点生命。获得一个随机[gold]遗物[/gold]。",
                        ResultDescription =
                            "几滴鲜血补完了最后一句。纸页卷起一件遗物，像回礼一样递到你手中。",
                    },
                ],
            },
        };
    }

    private static string GetEnglishAreaName(AiEventSlot slot)
    {
        return slot switch
        {
            AiEventSlot.Overgrowth => "Overgrowth",
            AiEventSlot.Hive => "Hive",
            AiEventSlot.Glory => "Glory",
            AiEventSlot.Underdocks => "Underdocks",
            AiEventSlot.Shared => "Shared",
            _ => "Shared",
        };
    }

    private static string GetChineseAreaName(AiEventSlot slot)
    {
        return slot switch
        {
            AiEventSlot.Overgrowth => "蔓生区",
            AiEventSlot.Hive => "蜂巢",
            AiEventSlot.Glory => "荣光区",
            AiEventSlot.Underdocks => "下层船坞",
            AiEventSlot.Shared => "尖塔",
            _ => "尖塔",
        };
    }
}
