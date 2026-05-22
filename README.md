1. Make sure Synthesis is set up correctly. Download from https://github.com/Mutagen-Modding/Synthesis/releases and follow the directions at https://mutagen-modding.github.io/Synthesis/Installation/ if you need help.
2. Download the Synthesis patcher from the Simple Scroll Transcribing modpage and double click to add it to Synthesis, or search for it in the Synthesis UI.
3. Run your patcher. Check the output to see which scrolls had recipies created. Spider scrolls from Dragonborn are excluded as they are meant to be crafted a different way. Shalidor's Insights are excluded by default. Let me know if you hate that.
   3a. Pass 1 creates recipes following the original logic of the mod, where if you know a spell, or if you possess at least one copy of the scroll or its associated spell tome, you can make new copies using an inkwell, 2 rolls of paper, and a spell-level appropriate soul gem.
   3b. Pass 2 adds a new logic that creates a transcription recipe for scrolls that exist, but don't have an associated standalone spell/spell tome. If you have it, why can't you write it down?!
4. Close Synthesis and activate your patch.
5. ???
6. Profit.


Scroll recipes created in the first pass use the "Spell tome >=1 OR scroll >=1 OR HasSpell" logic that I used originally. I realized while testing with mods like Apocalypse that there are some scrolls that exist but dont have tomes. Thus the 2nd pass adding straight scroll transcriptions - I still think you should be able to write stuff down if you have a copy of it right in front of you. That was sort of the whole point of this mod. 

If a scroll recipe is created and for some reason you don't want it there at all, you can delete the appropriate "ScrollRecipe_" or "ScrollTranscribe_" entry under "Constructible Object" in your patch.

The beauty of this patch is it should work with any heinous almagamation of magic mod mashups you might be using. Make sure to sort your mods in the order you want them before running the patcher. For example - if you use Mysticism and Odin together, and you prefer Mysticism's changes to any spells that Odin also touches, load Mysticism later. That way those overwrites win. The patcher will create recipes for all vanilla scrolls based on who wins, and create new recipes for all added scrolls.

Let me know if something is horrendously broken.
