using System.Collections.Generic;
using BaseLib.Abstracts;
using BaseLib;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.RelicPools;

namespace CocoRelics;

[Pool(typeof(SharedRelicPool))]
public sealed class ZeduCoco : CustomRelicModel
{
    private const string CustomIconPath = "res://CocoRelics/images/relics/pin_coco.png";

    public override RelicRarity Rarity => RelicRarity.Rare;

    public override string PackedIconPath => CustomIconPath;

    protected override string PackedIconOutlinePath => CustomIconPath;

    protected override string BigIconPath => CustomIconPath;

    protected override IEnumerable<IHoverTip> ExtraHoverTips => HoverTipFactory.FromRelic<BigMeal>();
}
