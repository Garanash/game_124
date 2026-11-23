using System.Collections.Generic;
using System.Linq;

namespace Realmforge.Rules.Characters
{
    /// <summary>
    /// How a hero looks, chosen at creation. Face and hair are indices into the race's appearance kit on the
    /// client; the server only bounds them, because kits are art assets the server does not load.
    /// </summary>
    public sealed class CharacterLook
    {
        public const int Bald = -1;

        public string Sex { get; set; } = "female";
        public int Face { get; set; }
        public int Hair { get; set; }
        public int HairColor { get; set; }
        public int EyeColor { get; set; }
        public string Build { get; set; } = "athletic";
        public int HeightPercent { get; set; } = 100;
        public string Outfit { get; set; } = "ranger";
    }

    public static class CharacterLookRules
    {
        public const int MaxKitVariant = 15;
        public const int MinHeightPercent = 94;
        public const int MaxHeightPercent = 106;
        private const int MaxColor = 0xFFFFFF;

        public static readonly IReadOnlyCollection<string> Sexes = new[] { "female", "male" };
        public static readonly IReadOnlyCollection<string> Builds = new[] { "slender", "athletic", "sturdy" };
        public static readonly IReadOnlyCollection<string> Outfits = new[] { "commoner", "ranger", "mage", "noble", "guard", "artisan" };

        public static RuleResult Validate(CharacterLook look)
        {
            if (!Sexes.Contains(look.Sex) || !Builds.Contains(look.Build) || !Outfits.Contains(look.Outfit))
            {
                return RuleResult.Fail("look.unknown_option", "Unknown sex, build or outfit.");
            }
            if (look.Face < 0 || look.Face > MaxKitVariant || look.Hair < CharacterLook.Bald || look.Hair > MaxKitVariant)
            {
                return RuleResult.Fail("look.variant", "Face or hair is out of range.");
            }
            if (look.HairColor < 0 || look.HairColor > MaxColor || look.EyeColor < 0 || look.EyeColor > MaxColor)
            {
                return RuleResult.Fail("look.color", "Colours must be 24-bit RGB.");
            }
            if (look.HeightPercent < MinHeightPercent || look.HeightPercent > MaxHeightPercent)
            {
                return RuleResult.Fail("look.height", "Height is out of range.");
            }
            return RuleResult.Ok();
        }
    }
}
