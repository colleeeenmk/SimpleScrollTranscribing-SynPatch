using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;

namespace ScrollCraftingPatcher;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        return await SynthesisPipeline.Instance
            .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
            .SetTypicalOpen(GameRelease.SkyrimSE, "ScrollCraftingPatcher.esp")
            .Run(args);
    }

    public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
    {
        // ── Workbench keyword ─────────────────────────────────────────────────────
        if (!state.LinkCache.TryResolve<IKeywordGetter>("ScrollCrafting", out var scrollCraftingKywd))
            throw new Exception(
                "ERROR: 'ScrollCrafting' keyword not found. " +
                "Make sure ScrollCrafting.esp is active in your load order.");

        Console.WriteLine($"Found ScrollCrafting keyword: 0x{scrollCraftingKywd.FormKey.ID:X} " +
                          $"({scrollCraftingKywd.FormKey.ModKey.FileName})");

        // ── Ingredients ───────────────────────────────────────────────────────────
        var inkwell      = state.LinkCache.Resolve<IMiscItemGetter>("Inkwell01").ToLink<IItemGetter>();
        var paperRoll    = state.LinkCache.Resolve<IMiscItemGetter>("PaperRoll").ToLink<IItemGetter>();

        // Soul gems by spell tier
        var soulGemPetty   = state.LinkCache.Resolve<ISoulGemGetter>("SoulGemPettyFilled").ToLink<IItemGetter>();   // Novice
        var soulGemLesser  = state.LinkCache.Resolve<ISoulGemGetter>("SoulGemLesserFilled").ToLink<IItemGetter>();  // Apprentice
        var soulGemCommon  = state.LinkCache.Resolve<ISoulGemGetter>("SoulGemCommonFilled").ToLink<IItemGetter>();  // Adept
        var soulGemGreater = state.LinkCache.Resolve<ISoulGemGetter>("SoulGemGreaterFilled").ToLink<IItemGetter>(); // Expert
        var soulGemGrand   = state.LinkCache.Resolve<ISoulGemGetter>("SoulGemGrandFilled").ToLink<IItemGetter>();   // Master

        // ── Spell tome lookup ─────────────────────────────────────────────────────
        Console.WriteLine("\nBuilding spell tome lookup...");
        var tomeByPrimaryMgef = new Dictionary<FormKey, (ISpellGetter Spell, IBookGetter Book)>();
        var tomeBySpellName   = new Dictionary<string, (ISpellGetter Spell, IBookGetter Book)>(StringComparer.OrdinalIgnoreCase);

        foreach (var book in state.LoadOrder.PriorityOrder.Book().WinningOverrides())
        {
            if (book.Teaches is not IBookSpellGetter bookSpell || bookSpell.Spell.IsNull) continue;
            if (!state.LinkCache.TryResolve<ISpellGetter>(bookSpell.Spell.FormKey, out var spell)) continue;

            var pair       = (spell, book);
            var maxCost    = 0f;
            var primaryKey = FormKey.Null;

            foreach (var effect in spell.Effects)
            {
                state.LinkCache.TryResolve<IMagicEffectGetter>(effect.BaseEffect.FormKey, out var mgef);
                if (mgef is null || !(mgef.BaseCost > maxCost)) continue;
                maxCost    = mgef.BaseCost;
                primaryKey = effect.BaseEffect.FormKey;
            }

            if (!primaryKey.IsNull) tomeByPrimaryMgef.TryAdd(primaryKey, pair);

            if (spell.Name?.String is string spellName && !string.IsNullOrWhiteSpace(spellName))
                tomeBySpellName.TryAdd(spellName, pair);
        }

        Console.WriteLine($"  → {tomeByPrimaryMgef.Count} spells indexed by primary effect.");
        Console.WriteLine($"  → {tomeBySpellName.Count} spells indexed by name.");

        // ── Pass 1 — spell-matched recipes ───────────────────────────────────────
        Console.WriteLine("\nProcessing scrolls...\n");
        var recipeCount    = 0;
        var passOneRecipes = new HashSet<FormKey>();

        foreach (var scroll in state.LoadOrder.PriorityOrder.Scroll().WinningOverrides())
        {
            var scrollName = scroll.Name?.ToString()
                          ?? scroll.EditorID
                          ?? scroll.FormKey.ToString();

            // Exclusions
            if (scroll.FormKey.ModKey.FileName.ToString().Equals("Dragonborn.esm", StringComparison.OrdinalIgnoreCase)
                && (scrollName.Contains("spider", StringComparison.OrdinalIgnoreCase)
                    || (scroll.EditorID ?? "").Contains("spider", StringComparison.OrdinalIgnoreCase)))
            {
                PrintSkipped($"Dragonborn spider scroll ({scrollName}).");
                continue;
            }

            if (scrollName.Contains("Shalidor's Insights", StringComparison.OrdinalIgnoreCase))
            {
                PrintSkipped($"Shalidor's Insights scroll ({scrollName}).");
                continue;
            }

            Console.WriteLine(
                $"Processing scroll: {scrollName} " +
                $"(0x{scroll.FormKey.ID:X6}: {scroll.FormKey.ModKey.FileName})");

            // Spell tier + primary MGEF
            var max = 0.0f;
            uint costliestEffectLevel = 0;
            var primaryMgefKey = FormKey.Null;

            foreach (var effect in scroll.Effects)
            {
                state.LinkCache.TryResolve<IMagicEffectGetter>(effect.BaseEffect.FormKey, out var mgef);
                if (mgef is null) continue;
                if (!(mgef.BaseCost > max)) continue;
                max = mgef.BaseCost;
                costliestEffectLevel = mgef.MinimumSkillLevel;
                primaryMgefKey = effect.BaseEffect.FormKey;
            }

            var soulGemLink = costliestEffectLevel switch
            {
                < 25            => soulGemPetty,
                >= 25 and < 50  => soulGemLesser,
                >= 50 and < 75  => soulGemCommon,
                >= 75 and < 100 => soulGemGreater,
                >= 100          => soulGemGrand
            };

            // Tome match
            (ISpellGetter Spell, IBookGetter Book) tomeMatch = default;

            if (!tomeByPrimaryMgef.TryGetValue(primaryMgefKey, out tomeMatch))
            {
                var spellLabel = (scroll.Name?.String ?? "")
                    .Replace("Scroll of the ", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("Scroll of ",     "", StringComparison.OrdinalIgnoreCase)
                    .Trim();

                if (!tomeBySpellName.TryGetValue(spellLabel, out tomeMatch))
                {
                    PrintSkipped($"no matching tome " +
                        $"(primary MGEF: {primaryMgefKey.ID:X6}, name tried: '{spellLabel}').");
                    continue;
                }
            }

            var matchedSpell = tomeMatch.Spell;
            var matchedBook  = tomeMatch.Book;

            // ConstructibleObject
            var recipe = state.PatchMod.ConstructibleObjects.AddNew();

            recipe.EditorID           = $"ScrollRecipe_{matchedSpell.Name!.String!.Replace(" ", "")}";
            recipe.CreatedObject      = new FormLinkNullable<IConstructibleGetter>(scroll.FormKey);
            recipe.CreatedObjectCount = 2;
            recipe.WorkbenchKeyword   = new FormLinkNullable<IKeywordGetter>(scrollCraftingKywd.FormKey);

            recipe.Items = new ExtendedList<ContainerEntry>
            {
                new() { Item = new ContainerItem { Item = inkwell,     Count = 1 } },
                new() { Item = new ContainerItem { Item = paperRoll,   Count = 2 } },
                new() { Item = new ContainerItem { Item = soulGemLink, Count = 1 } },
            };

            // Conditions
            var scrollCountData = new GetItemCountConditionData();
            scrollCountData.ItemOrList.Link.SetTo(scroll);
            recipe.Conditions.Add(new ConditionFloat
            {
                Flags           = Condition.Flag.OR,
                CompareOperator = CompareOperator.GreaterThanOrEqualTo,
                ComparisonValue = 1.0f,
                Data            = scrollCountData
            });

            var bookCountData = new GetItemCountConditionData();
            bookCountData.ItemOrList.Link.SetTo(matchedBook);
            recipe.Conditions.Add(new ConditionFloat
            {
                Flags           = Condition.Flag.OR,
                CompareOperator = CompareOperator.GreaterThanOrEqualTo,
                ComparisonValue = 1.0f,
                Data            = bookCountData
            });

            var hasSpellData = new HasSpellConditionData();
            hasSpellData.Spell.Link.SetTo(matchedSpell);
            recipe.Conditions.Add(new ConditionFloat
            {
                CompareOperator = CompareOperator.EqualTo,
                ComparisonValue = 1.0f,
                Data            = hasSpellData
            });

            Console.WriteLine(
                $"  → Recipe '{recipe.EditorID}'" +
                $" | Tier: {GetTierName(costliestEffectLevel)}" +
                $" | Spell: {matchedSpell.Name?.String ?? "—"}" +
                $" | Tome: {matchedBook.Name?.String ?? "—"}");

            passOneRecipes.Add(scroll.FormKey);
            recipeCount++;
        }

        Console.WriteLine();
        Console.WriteLine($"Done. Created {recipeCount} spell-matched scroll recipes.");

        // ── Pass 2 — basic scroll recipes ────────────────────────────────────────
        Console.WriteLine("\nProcessing basic scroll recipes...\n");
        var basicRecipeCount = 0;

        foreach (var scroll in state.LoadOrder.PriorityOrder.Scroll().WinningOverrides())
        {
            var scrollName   = scroll.Name?.ToString() ?? scroll.EditorID ?? scroll.FormKey.ToString();
            var scrollEdidId = scroll.EditorID ?? "";

            // Exclusions
            if (passOneRecipes.Contains(scroll.FormKey)) continue;

            if (scrollName.Contains("Shalidor's Insights", StringComparison.OrdinalIgnoreCase))
            {
                PrintSkipped($"Shalidor's Insights scroll ({scrollName}).");
                continue;
            }

            if (scroll.FormKey.ModKey.FileName.ToString().Equals("Dragonborn.esm", StringComparison.OrdinalIgnoreCase)
                && (scrollName.Contains("spider", StringComparison.OrdinalIgnoreCase)
                    || scrollEdidId.Contains("spider", StringComparison.OrdinalIgnoreCase)))
            {
                PrintSkipped($"Dragonborn spider scroll ({scrollName}).");
                continue;
            }

            Console.WriteLine(
                $"Processing scroll: {scrollName} " +
                $"(0x{scroll.FormKey.ID:X6}: {scroll.FormKey.ModKey.FileName})");

            // Spell tier
            var max = 0.0f;
            uint costliestEffectLevel = 0;

            foreach (var effect in scroll.Effects)
            {
                state.LinkCache.TryResolve<IMagicEffectGetter>(effect.BaseEffect.FormKey, out var mgef);
                if (mgef is null) continue;
                if (!(mgef.BaseCost > max) && max > 0.0f) continue;
                max = mgef.BaseCost;
                costliestEffectLevel = max > 0.0f ? mgef.MinimumSkillLevel : Math.Max(costliestEffectLevel, mgef.MinimumSkillLevel);
            }

            var soulGemLink = costliestEffectLevel switch
            {
                < 25            => soulGemPetty,
                >= 25 and < 50  => soulGemLesser,
                >= 50 and < 75  => soulGemCommon,
                >= 75 and < 100 => soulGemGreater,
                >= 100          => soulGemGrand
            };

            // ConstructibleObject
            var scrollDisplayName = (scroll.Name?.String ?? scrollEdidId)
                .Replace("Scroll of the ", "", StringComparison.OrdinalIgnoreCase)
                .Replace("Scroll of ",     "", StringComparison.OrdinalIgnoreCase)
                .Replace(" ", "")
                .Trim();
            var editorId = !string.IsNullOrWhiteSpace(scrollDisplayName)
                ? $"ScrollTranscribe_{scrollDisplayName}"
                : $"ScrollTranscribe_{scroll.FormKey.ModKey.Name}_{scroll.FormKey.ID:X6}";

            var recipe = state.PatchMod.ConstructibleObjects.AddNew();

            recipe.EditorID           = editorId;
            recipe.CreatedObject      = new FormLinkNullable<IConstructibleGetter>(scroll.FormKey);
            recipe.CreatedObjectCount = 2;
            recipe.WorkbenchKeyword   = new FormLinkNullable<IKeywordGetter>(scrollCraftingKywd.FormKey);

            recipe.Items = new ExtendedList<ContainerEntry>
            {
                new() { Item = new ContainerItem { Item = inkwell,     Count = 1 } },
                new() { Item = new ContainerItem { Item = paperRoll,   Count = 2 } },
                new() { Item = new ContainerItem { Item = soulGemLink, Count = 1 } },
            };

            // Condition
            var scrollCountData = new GetItemCountConditionData();
            scrollCountData.ItemOrList.Link.SetTo(scroll);
            recipe.Conditions.Add(new ConditionFloat
            {
                CompareOperator = CompareOperator.GreaterThanOrEqualTo,
                ComparisonValue = 1.0f,
                Data            = scrollCountData
            });

            Console.WriteLine($"  → Recipe '{recipe.EditorID}' | Tier: {GetTierName(costliestEffectLevel)}");

            basicRecipeCount++;
        }

        Console.WriteLine();
        Console.WriteLine($"Done. Created {basicRecipeCount} basic scroll recipes.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static void PrintSkipped(string reason) =>
        Console.WriteLine($"  → [SKIPPED] {reason}");

    private static string GetTierName(uint skillLevel) => skillLevel switch
    {
        < 25            => "Novice",
        >= 25 and < 50  => "Apprentice",
        >= 50 and < 75  => "Adept",
        >= 75 and < 100 => "Expert",
        >= 100          => "Master"
    };
}
